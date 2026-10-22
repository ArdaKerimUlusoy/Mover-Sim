using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Güvenlik görevlisi (GDD Bölüm 5). Tüm kararlar SUNUCUDA verilir, diğerleri pozisyonu NetworkTransform ile görür.
/// Durumlar:
///   Devriye     → noktalar arasında dolaşır
///   Şüphelenme  → şüpheli davranan birini görünce durup ona bakar ("?"), şüphe dolarsa alarm
///   Araştırma   → ses (bozuk para), kamera ihbarı veya kötü bir sahte eser için o noktaya gider ("?")
///   Kovalama    → alarm sırasında aranan oyuncuyu kovalar ("!"), yakalarsa oyuncu garaja gönderilir
/// Sahte eser kontrolü: önünden geçtiği değiştirilmiş eserlere bakar; benzerlik ne kadar düşükse fark etme şansı o kadar yüksek.
/// Kurulum: NetworkObject + NetworkTransform + NavMeshAgent (kapalı başlasın) + bu bileşen; sahnede NavMesh (RuntimeNavMeshBaker).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class GuardAI : NetworkBehaviour
{
    public enum State : byte { Patrol, Suspicious, Investigate, Chase }

    [Header("Devriye")]
    [SerializeField] private Transform[] waypoints = new Transform[0];
    [SerializeField] private float waitAtWaypoint = 1.5f;

    [Header("Görüş")]
    [SerializeField] private float viewRange = 11f;
    [SerializeField] private float viewAngle = 90f;
    [SerializeField] private float eyeHeight = 0.7f;
    [Tooltip("Saniyede şüphe artışı (yakında daha hızlı).")]
    [SerializeField] private float baseSuspicionRate = 0.9f;

    [Header("Hızlar")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float investigateSpeed = 3.2f;
    [SerializeField] private float chaseSpeed = 5.6f;

    [Header("Diğer")]
    [SerializeField] private float catchDistance = 1.3f;
    [SerializeField] private float lookAroundSeconds = 3f;
    [SerializeField] private VisionConeVisual cone;

    [Header("Kovalama zekası")]
    [Tooltip("Kovaladığı kişiyi kaybedince etrafı ne kadar süre arar.")]
    [SerializeField] private float searchSeconds = 14f;
    [SerializeField] private float searchRadius = 7f;

    [Header("Görev (MuseumSession)")]
    [Tooltip("Bu güvenlik hangi görev seviyesinden itibaren görevde? (GDD: seviye arttıkça güvenlik sayısı artar)")]
    [SerializeField, Min(1)] private int requiredLevel = 1;

    private bool shownOnline = true;

    /// <summary>Bu görevde görevde mi? Değilse görünmez, yürümez, kimseyi görmez.</summary>
    public bool Online => MuseumSession.IsLevelActive(requiredLevel);

    private readonly NetworkVariable<State> state = new NetworkVariable<State>(State.Patrol);
    // "?" işaretinin dolum oranı (0–100): şüphelendiği oyuncunun şüphe seviyesi
    private readonly NetworkVariable<byte> alert = new NetworkVariable<byte>(0);

    // İstemci: "!" / "?" çıkış animasyonu için
    private State shownState = State.Patrol;
    private float stateChangedAt = -10f;

    private NavMeshAgent agent;
    private int waypointIndex;
    private float waitUntil;
    private Vector3 investigatePoint;
    private bool investigateIsFake;
    private ArtworkDisplay investigateDisplay;
    private float lookAroundUntil = -1f;
    private PlayerStealth focus;
    private float lastFocusSeen;
    private Vector3 lastKnownPos;
    private float nextFakeCheck;
    private readonly HashSet<ArtworkDisplay> inspected = new HashSet<ArtworkDisplay>();

    // Kovalama / arama
    private float searchUntil = -1f;        // > şimdi: kaybettiği kişiyi arıyor (Investigate durumunda)
    private HidingSpot huntSpot;            // kovaladığı kişinin saklandığı dolap (gördüyse)
    private float lastOwnSight = -100f;     // kovaladığı kişiyi KENDİ GÖZÜYLE en son ne zaman gördü (kamera / telsiz sayılmaz)

    // Takılma: iki güvenlik yüz yüze gelip kilitlenirse yana çekilir, olmazsa başka noktaya gider
    private float stuckTime;
    private float sidestepUntil = -1f;
    private Vector3 sidestepDir;

    // Ararken / incelerken yakındaki dolapları açıp bakar (içinde biri varsa yakalar)
    private HidingSpot closetTarget;
    private float closetGiveUp;
    private readonly HashSet<HidingSpot> checkedClosets = new HashSet<HidingSpot>();
    private const float ClosetSearchRange = 6f;
    private const float ClosetChanceWhileSearching = 0.8f; // kaybettiği kişiyi ararken
    private const float ClosetChanceWhileInvestigating = 0.3f; // ses / şüphe incelerken
    private Vector3 focusVelocity;
    private Vector3 focusPrevPos;
    private float focusPrevTime = -1f;
    private float nextRealarm;

    public State CurrentState => state.Value;

    private Vector3 Eye => transform.position + Vector3.up * eyeHeight;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.enabled = false; // NavMesh hazır olana kadar
        if (cone == null) cone = GetComponentInChildren<VisionConeVisual>();
        if (GetComponent<GuardLook>() == null) gameObject.AddComponent<GuardLook>(); // kapsül yerine üniformalı bekçi

        // Karşılaşan iki güvenlik birbirine yol versin: her birinin önceliği farklı (eşit öncelikte ikisi de beklerdi)
        agent.avoidancePriority = 20 + Mathf.Abs(name.GetHashCode() % 30);
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            SecurityManager.Guards.Add(this);
            NoiseSystem.NoiseMade += OnNoise;
        }
    }

    public override void OnNetworkDespawn()
    {
        SecurityManager.Guards.Remove(this);
        NoiseSystem.NoiseMade -= OnNoise;
    }

    // ---------------- Dışarıdan olaylar (sunucu) ----------------

    private void OnNoise(Vector3 pos, float radius)
    {
        if (!Online) return;
        if (state.Value == State.Chase) return;
        if (Vector3.Distance(transform.position, pos) <= radius) ServerInvestigate(pos, false);
    }

    public void ServerInvestigate(Vector3 pos, bool isFake, ArtworkDisplay display = null)
    {
        if (!IsServer || !Online || state.Value == State.Chase) return;
        searchUntil = -1f;
        investigatePoint = pos;
        investigateIsFake = isFake;
        investigateDisplay = display;
        lookAroundUntil = -1f;
        SetState(State.Investigate);
        MoveTo(pos, investigateSpeed);
    }

    /// <summary>Sesli sohbette söylenen hırsızlık kelimesini duyduğunda: sahte fark etme şansı artar.</summary>
    private const float OverheardDetectBonus = 0.3f;

    private struct OverheardRoll { public float time; public bool caught; }
    private static readonly System.Collections.Generic.Dictionary<ArtworkDisplay, OverheardRoll> OverheardRolls =
        new System.Collections.Generic.Dictionary<ArtworkDisplay, OverheardRoll>();

    /// <summary>Sunucu: bir oyuncunun hırsızlıkla ilgili konuştuğunu duydu (CrimeTalk).
    /// Konuşanın yanındaki esere gidip inceler; eser değiştirilmişse fark etme şansı artar (fark ederse varınca alarm).
    /// Eser yoksa konuşanın olduğu yere bakar. Duyduysa true döner.</summary>
    public bool ServerHearCrimeTalk(PlayerStealth speaker, ArtworkDisplay display)
    {
        if (!IsServer || !Online || speaker == null) return false;
        if (state.Value == State.Chase) return true; // zaten kovalıyor

        PlaySoundRpc(SoundId.GuardHuh, transform.position);
        lastKnownPos = speaker.transform.position;

        if (display != null)
        {
            // Sahte fark etme zarı aynı eser için en fazla overheardRollInterval sn'de bir (tüm güvenlikler ortak):
            // arada gelen bildirimler güvenliği yine yönlendirir ama son zarın sonucunu kullanır (spamle zar attırılamaz).
            bool caught;
            float now = Time.time;
            if (OverheardRolls.TryGetValue(display, out OverheardRoll r) &&
                now - r.time < CrimeWordSettings.Active.overheardRollInterval)
            {
                caught = r.caught && display.IsSwapped;
            }
            else
            {
                inspected.Remove(display); // önünden geçerken bir kez daha bakar
                caught = display.IsSwapped &&
                         Random.value < Mathf.Clamp01(DetectChance(display.LeftSimilarity) + OverheardDetectBonus);
                if (OverheardRolls.Count > 64) OverheardRolls.Clear(); // eski oturumların (yok edilmiş) eserleri birikmesin
                OverheardRolls[display] = new OverheardRoll { time = now, caught = caught };
            }
            ServerInvestigate(display.transform.position, caught, display);
        }
        else
        {
            ServerInvestigate(speaker.transform.position, false);
        }
        if (alert.Value < 45) alert.Value = 45;
        return true;
    }

    public void ServerOnAlarm(Vector3 pos, PlayerStealth culprit)
    {
        if (!IsServer || !Online) return;
        lastKnownPos = pos;

        // Telsizden haber: suçlunun yerini birkaç saniye boyunca bilirler
        if (culprit != null)
        {
            focus = culprit;
            lastFocusSeen = Time.time + 2f;
        }

        alert.Value = 100;
        searchUntil = -1f;
        SetState(State.Chase);
        MoveTo(pos, chaseSpeed);
    }

    public void ServerOnAlarmEnded()
    {
        if (!IsServer || !Online) return;

        if (state.Value == State.Chase)
        {
            // Hâlâ gözünün önündeyse bırakmaz; az önce kaybettiyse son görüldüğü yeri arar
            if (focus != null && !focus.IsJailed && Time.time - lastFocusSeen < 2.5f) return;
            focus = null;
            BeginSearch(lastKnownPos);
            return;
        }
        focus = null;
        SetState(State.Patrol);
        GoToWaypoint();
    }

    /// <summary>Sunucu: biri hapis kilidini kurcalıyor → koşarak hapse gider, orada kimi görürse şüphelenir.</summary>
    public void ServerRespondToJailbreak(Vector3 jailPos, PlayerStealth rescuer)
    {
        if (!IsServer || !Online) return;
        if (state.Value == State.Chase) { lastKnownPos = jailPos; return; }

        investigatePoint = jailPos;
        investigateIsFake = false;
        investigateDisplay = null;
        lookAroundUntil = -1f;
        searchUntil = Time.time + searchSeconds * 0.6f;
        lastKnownPos = jailPos;
        SetState(State.Investigate);
        alert.Value = 80;
        MoveTo(jailPos, investigateSpeed * 1.5f);
        PlaySoundRpc(SoundId.GuardHuh, transform.position);
    }

    /// <summary>MuseumLayout: haritanın kat planına göre yeni devriye yolu.</summary>
    public void SetWaypoints(Transform[] points)
    {
        if (points == null || points.Length == 0) return;
        waypoints = points;
        waypointIndex = 0;
    }

    /// <summary>Sunucu: müze düzeni değişti (yeni harita) → ilk devriye noktasına ışınlan, devriyeye başla.</summary>
    /// <summary>Sunucu: bu oyuncuyu artık kovalamaz / aramaz (hapisten çıktı, yeni görev). Devriyeye döner.</summary>
    public void ServerForget(PlayerStealth p)
    {
        if (!IsServer || p == null || focus != p) return;
        focus = null;
        huntSpot = null;
        searchUntil = -1f;
        lookAroundUntil = -1f;
        alert.Value = 0;
        state.Value = State.Patrol;
        GoToWaypoint();
    }

    public void ServerResetForLayout()
    {
        if (!IsServer) return;
        focus = null;
        huntSpot = null;
        searchUntil = -1f;
        lookAroundUntil = -1f;
        waypointIndex = 0;
        state.Value = State.Patrol;
        alert.Value = 0;
        if (agent.enabled && agent.isOnNavMesh && waypoints != null && waypoints.Length > 0 && waypoints[0] != null &&
            NavMesh.SamplePosition(waypoints[0].position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
        }
        GoToWaypoint();
    }

    /// <summary>Kaybettiği kişiyi aramaya başlar: önce gittiği yöne doğru, sonra etrafta birkaç noktaya bakar.</summary>
    private void BeginSearch(Vector3 from)
    {
        Vector3 guess = from + Vector3.ClampMagnitude(focusVelocity * 1.5f, 5f);
        investigatePoint = guess;
        investigateIsFake = false;
        investigateDisplay = null;
        lookAroundUntil = -1f;
        searchUntil = Time.time + searchSeconds;
        lastKnownPos = from;
        SetState(State.Investigate);
        alert.Value = 70;
        MoveTo(guess, investigateSpeed * 1.25f);
    }

    /// <summary>Telsiz: kovalayan bir güvenlik hedefi görünce diğer kovalayanlara yerini söyler.</summary>
    private void ShareSighting(PlayerStealth target, Vector3 pos)
    {
        foreach (GuardAI g in SecurityManager.Guards)
        {
            if (g == null || g == this || !g.Online || g.state.Value != State.Chase) continue;
            if (g.focus != null && g.focus != target && Time.time - g.lastFocusSeen < 1f) continue; // kendi hedefini görüyor
            g.focus = target;
            g.lastKnownPos = pos;
            g.lastFocusSeen = Mathf.Max(g.lastFocusSeen, Time.time - 1.5f); // birkaç saniye o yöne koşar
        }
    }

    private void UpdateFocusVelocity()
    {
        if (focus == null) return;
        Vector3 p = focus.transform.position;
        if (focusPrevTime > 0f && Time.time - focusPrevTime > 0.05f)
        {
            Vector3 v = (p - focusPrevPos) / (Time.time - focusPrevTime);
            v.y = 0f;
            if (v.sqrMagnitude < 100f) focusVelocity = Vector3.Lerp(focusVelocity, v, 0.5f);
        }
        if (focusPrevTime < 0f || Time.time - focusPrevTime > 0.05f)
        {
            focusPrevPos = p;
            focusPrevTime = Time.time;
        }
    }

    // ---------------- Ana döngü ----------------

    private void Update()
    {
        bool online = Online;
        if (online != shownOnline) ApplyOnline(online);
        if (!online) return;

        UpdateConeVisual();

        if (!IsServer || !IsSpawned) return;

        if (!agent.enabled)
        {
            if (!RuntimeNavMeshBaker.Ready) return;
            agent.enabled = true;
            agent.Warp(transform.position);
            GoToWaypoint();
        }
        if (!agent.isOnNavMesh) return;

        if (UpdateUnstuck()) return;
        OpenNearbyDoors();
        PlayerStealth seen = LookForPlayers();
        CheckForFakes();

        // Alarm çalarken aranan / tanınan birini görürse doğrudan kovalar
        SecurityManager sm = SecurityManager.Instance;
        if (seen != null && state.Value != State.Chase && sm != null && sm.AlarmActive && (seen.IsWanted || seen.IsKnownOffender))
        {
            focus = seen;
            lastFocusSeen = Time.time;
            focusPrevTime = -1f;
            searchUntil = -1f;
            alert.Value = 100;
            SetState(State.Chase);
        }

        switch (state.Value)
        {
            case State.Patrol: TickPatrol(seen); break;
            case State.Suspicious: TickSuspicious(seen); break;
            case State.Investigate: TickInvestigate(seen); break;
            case State.Chase: TickChase(seen); break;
        }
    }

    /// <summary>Görüş alanındaki oyunculara şüphe ekler; en şüpheli görüneni döner.</summary>
    private PlayerStealth LookForPlayers()
    {
        PlayerStealth best = null;
        float bestScore = 0f;

        foreach (PlayerStealth p in PlayerStealth.All)
        {
            if (p == null || !p.IsSpawned) continue;

            float range = viewRange * (p.IsCrouching ? 0.75f : 1f);
            if (!Vision.CanSee(Eye, transform.forward, range, viewAngle, p)) continue;

            // Güvenlik odasına girmiş birini görürse: anında alarm
            if (RestrictedArea.IsInsideInstant(p.transform.position) && !p.IsJailed && SecurityManager.Instance != null && !SecurityManager.Instance.AlarmActive)
            {
                PlaySoundRpc(SoundId.GuardAlert, transform.position);
                SecurityManager.Instance.ServerTriggerAlarm(p.transform.position, "alarm.secroom", p);
            }

            float dist = Vector3.Distance(transform.position, p.transform.position);
            float proximity = Mathf.Lerp(1.6f, 0.4f, dist / Mathf.Max(range, 0.01f));
            p.ServerReportSeen(baseSuspicionRate * proximity, Eye);

            float score = p.SuspiciousLevel + p.Suspicion;
            if (score > bestScore) { bestScore = score; best = p; }
        }
        return best;
    }

    private void TickPatrol(PlayerStealth seen)
    {
        if (seen != null && seen.SuspiciousLevel > 0f)
        {
            BeginSuspicious(seen);
            return;
        }

        agent.speed = patrolSpeed;
        if (!agent.pathPending && agent.remainingDistance <= 0.4f)
        {
            if (waitUntil < 0f) waitUntil = Time.time + waitAtWaypoint;
            else if (Time.time >= waitUntil)
            {
                waitUntil = -1f;
                waypointIndex = waypoints.Length > 0 ? (waypointIndex + 1) % waypoints.Length : 0;
                GoToWaypoint();
            }
        }
    }

    private void BeginSuspicious(PlayerStealth target)
    {
        focus = target;
        lastFocusSeen = Time.time;
        lastKnownPos = target.transform.position;
        SetState(State.Suspicious);
        agent.ResetPath();
        PlaySoundRpc(SoundId.GuardHuh, transform.position);
    }

    private void TickSuspicious(PlayerStealth seen)
    {
        if (focus != null) alert.Value = (byte)Mathf.RoundToInt(Mathf.Clamp01(focus.Suspicion) * 100f);

        if (focus != null && seen == focus && (focus.SuspiciousLevel > 0f || focus.Suspicion > 0.1f))
        {
            lastFocusSeen = Time.time;
            lastKnownPos = focus.transform.position;
            FaceTowards(lastKnownPos);

            // Şüphelenince oyuncuya doğru yürür, yakınına gelince durup izler
            if (Vector3.Distance(transform.position, lastKnownPos) > 3f) MoveTo(lastKnownPos, patrolSpeed);
            else agent.ResetPath();
        }
        else if (Time.time - lastFocusSeen > 2f)
        {
            // Gözden kaybetti: son görüldüğü yere bak
            ServerInvestigate(lastKnownPos, false);
            focus = null;
        }
    }

    private void TickInvestigate(PlayerStealth seen)
    {
        if (seen != null && seen.SuspiciousLevel > 0f)
        {
            closetTarget = null;
            BeginSuspicious(seen);
            return;
        }

        if (closetTarget != null) { TickCloset(); return; }

        if (lookAroundUntil < 0f)
        {
            if (!agent.pathPending && agent.remainingDistance <= (investigateIsFake ? 1.8f : 0.8f))
            {
                agent.ResetPath();
                lookAroundUntil = Time.time + (searchUntil > Time.time ? lookAroundSeconds * 0.5f : lookAroundSeconds);

                // Yakında dolap varsa açıp bakabilir
                if (!investigateIsFake && TryPickCloset()) return;

                if (investigateIsFake && SecurityManager.Instance != null)
                {
                    // Sahte eserin önüne geldi ve emin oldu → alarm
                    SecurityManager.Instance.ServerTriggerAlarm(investigatePoint, "alarm.fake", null);
                    PlaySoundRpc(SoundId.GuardAlert, transform.position);
                }
            }
        }
        else
        {
            // Etrafa bakın
            transform.Rotate(0f, (searchUntil > Time.time ? 140f : 90f) * Time.deltaTime, 0f);
            if (Time.time >= lookAroundUntil)
            {
                lookAroundUntil = -1f;
                if (searchUntil > Time.time && TryPickSearchPoint(out Vector3 next))
                {
                    // Arama sürüyor: kaybettiği yerin çevresinde başka bir noktaya bak
                    investigatePoint = next;
                    MoveTo(next, investigateSpeed * 1.15f);
                    return;
                }
                searchUntil = -1f;
                SetState(State.Patrol);
                GoToWaypoint();
            }
        }
    }

    private void TickChase(PlayerStealth seen)
    {
        agent.speed = chaseSpeed;

        // Hedef: görülen aranan / tanınan / çok şüpheli oyuncu
        if (seen != null && (seen.IsWanted || seen.IsKnownOffender || seen.Suspicion > 0.5f))
        {
            if (focus != seen) focusPrevTime = -1f;
            focus = seen;
            lastFocusSeen = Time.time;
        }
        if (seen != null && seen == focus) lastOwnSight = Time.time; // kendi gözüyle gördü
        if (focus != null && (!focus.IsSpawned || focus.IsJailed)) { focus = null; huntSpot = null; }

        // KENDİ GÖZÜNÜN önünde dolaba / perdeye saklandıysa: oraya gider, açar ve yakalar.
        // Kamera ya da başka güvenliğin telsiz haberi sayılmaz: saklanırken sadece bu güvenliğin görüşü önemli.
        if (focus != null && focus.IsHidden)
        {
            if (huntSpot == null && Time.time - lastOwnSight < 1.2f) huntSpot = HidingSpot.FindOccupiedBy(focus);
            if (huntSpot != null)
            {
                MoveTo(huntSpot.transform.position, chaseSpeed);
                if (Vector3.Distance(transform.position, huntSpot.transform.position) <= 2.1f)
                {
                    PlaySoundRpc(SoundId.GuardAlert, transform.position);
                    if (huntSpot.ServerEvict(focus)) focus.ServerCatch();
                    huntSpot = null;
                    focus = null;
                }
                return;
            }
        }

        if (focus != null && !focus.IsHidden && Time.time - lastFocusSeen < 3f)
        {
            UpdateFocusVelocity();
            lastKnownPos = focus.transform.position;
            bool seenNow = Time.time - lastFocusSeen < 0.05f;

            if (seenNow)
            {
                ShareSighting(focus, lastKnownPos);
                focus.ServerMarkWanted(Time.time + 6f);

                // Alarm sustuysa ama hâlâ kaçıyorsa yeniden çaldır (kovalamayı bırakmaz)
                SecurityManager sm = SecurityManager.Instance;
                if (sm != null && !sm.AlarmActive && Time.time >= nextRealarm)
                {
                    nextRealarm = Time.time + 6f;
                    sm.ServerTriggerAlarm(lastKnownPos, "alarm.spotted", focus);
                }
            }

            // Kaçtığı yöne biraz önden kes
            float dist = Vector3.Distance(transform.position, lastKnownPos);
            Vector3 lead = lastKnownPos + focusVelocity * Mathf.Clamp(dist / Mathf.Max(chaseSpeed, 0.1f), 0f, 0.8f);
            if (NavMesh.SamplePosition(lead, out NavMeshHit lh, 1.5f, NavMesh.AllAreas)) agent.SetDestination(lh.position);
            else agent.SetDestination(lastKnownPos);

            if (Vector3.Distance(transform.position, focus.transform.position) <= catchDistance)
            {
                PlaySoundRpc(SoundId.GuardAlert, transform.position);
                focus.ServerCatch();
                focus = null;
            }
        }
        else
        {
            // Kaybetti: kişiyi tanır, son görüldüğü yerden gittiği yöne doğru arar
            if (focus != null) focus.ServerMarkOffender();
            focus = null;
            huntSpot = null;
            if (!agent.pathPending && agent.remainingDistance <= 0.8f)
            {
                if (TryPickSearchPoint(out Vector3 next)) agent.SetDestination(next);
            }
        }
    }

    /// <summary>Yakındaki, henüz bakmadığı bir dolabı seçer (ararken büyük, incelerken küçük ihtimalle).</summary>
    private bool TryPickCloset()
    {
        bool searching = searchUntil > Time.time;
        if (!searching) checkedClosets.Clear(); // yeni bir inceleme: dolaplara yeniden bakılabilir
        HidingSpot best = null;
        float bestD = ClosetSearchRange;
        foreach (HidingSpot h in HidingSpot.All)
        {
            if (h == null || !h.IsSpawned || !h.gameObject.activeInHierarchy || !h.IsPainted || checkedClosets.Contains(h)) continue;
            float d = Vector3.Distance(transform.position, h.transform.position);
            if (d < bestD) { bestD = d; best = h; }
        }
        if (best == null) return false;
        checkedClosets.Add(best);
        if (Random.value > (searching ? ClosetChanceWhileSearching : ClosetChanceWhileInvestigating)) return false;
        closetTarget = best;
        closetGiveUp = Time.time + 6f;
        MoveTo(best.transform.position, investigateSpeed);
        return true;
    }

    /// <summary>Dolaba yürür, açar: içinde biri varsa çıkarır ve yakalar. Sonra etrafa bakmaya devam eder.</summary>
    private void TickCloset()
    {
        HidingSpot h = closetTarget;
        if (h == null || !h.IsSpawned || Time.time > closetGiveUp) { closetTarget = null; return; }
        if (Vector3.Distance(transform.position, h.transform.position) > 1.9f)
        {
            if (!agent.pathPending && !agent.hasPath) MoveTo(h.transform.position, investigateSpeed);
            return;
        }

        agent.ResetPath();
        FaceTowards(h.transform.position);
        closetTarget = null;
        h.ServerPlayOpened();
        PlayerStealth hider = h.ServerOccupant();
        if (hider != null && h.ServerEvict(hider))
        {
            PlaySoundRpc(SoundId.GuardAlert, transform.position);
            hider.ServerCatch(); // dolapta saklanırken bulundu
        }
        lookAroundUntil = Time.time + 1.2f;
    }

    /// <summary>Sunucu: bu güvenlik oyuncuyu şu an kendi gözüyle görüyor mu? (Hapis kurtarma vb. için.)</summary>
    public bool ServerCanSee(PlayerStealth p)
    {
        if (!IsServer || !Online || p == null || !p.IsSpawned) return false;
        float range = viewRange * (p.IsCrouching ? 0.75f : 1f);
        return Vision.CanSee(Eye, transform.forward, range, viewAngle, p);
    }

    /// <summary>
    /// Yürümesi gerekirken ~1 sn yerinde saydıysa (çoğunlukla dar kapıda başka bir güvenlikle / ziyaretçiyle yüz yüze):
    /// kısa bir an sağa çekilir; yine de ilerleyemezse devriyede sıradaki noktaya, incelemede bulunduğu yere geçer.
    /// true: bu karede sadece yana çekiliyor (diğer işler bekler).
    /// </summary>
    private bool UpdateUnstuck()
    {
        if (Time.time < sidestepUntil)
        {
            agent.Move(sidestepDir * (1.6f * Time.deltaTime));
            return true;
        }

        bool wantsToMove = agent.hasPath && !agent.pathPending && agent.remainingDistance > agent.stoppingDistance + 0.4f &&
                           !agent.isStopped;
        if (!wantsToMove || agent.velocity.sqrMagnitude > 0.09f) { stuckTime = 0f; return false; }

        stuckTime += Time.deltaTime;
        if (stuckTime < 0.9f) return false;

        if (stuckTime < 2.6f)
        {
            // Yana çekil: sağ ya da sol (önünde kim varsa onun tersine), yürünebilir tarafa
            Vector3 side = transform.right;
            GuardAI other = NearestGuard(1.8f);
            if (other != null && Vector3.Dot(other.transform.position - transform.position, side) > 0f) side = -side;
            if (!NavMesh.SamplePosition(transform.position + side * 0.8f, out NavMeshHit h, 0.4f, NavMesh.AllAreas)) side = -side;
            sidestepDir = side;
            sidestepUntil = Time.time + 0.45f;
            stuckTime = 1.7f; // bir kez daha denemeye vakit kalsın
            return true;
        }

        // Hâlâ ilerleyemiyor: hedefi bırak
        stuckTime = 0f;
        if (state.Value == State.Patrol)
        {
            if (waypoints != null && waypoints.Length > 0) waypointIndex = (waypointIndex + 1) % waypoints.Length;
            GoToWaypoint();
        }
        else if (state.Value == State.Investigate) { agent.ResetPath(); if (lookAroundUntil < 0f) lookAroundUntil = Time.time + 1f; }
        return false;
    }

    private GuardAI NearestGuard(float within)
    {
        GuardAI best = null;
        float bestD = within;
        foreach (GuardAI g in SecurityManager.Guards)
        {
            if (g == null || g == this || !g.Online) continue;
            float d = Vector3.Distance(g.transform.position, transform.position);
            if (d < bestD) { bestD = d; best = g; }
        }
        return best;
    }

    /// <summary>Son görüldüğü yerin çevresinde (gittiği yöne ağırlıklı) yürünebilir rastgele bir nokta.</summary>
    private bool TryPickSearchPoint(out Vector3 point)
    {
        Vector3 bias = Vector3.ClampMagnitude(focusVelocity, 3f);
        for (int i = 0; i < 6; i++)
        {
            Vector2 r = Random.insideUnitCircle * searchRadius;
            Vector3 p = lastKnownPos + bias + new Vector3(r.x, 0f, r.y);
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        }
        point = lastKnownPos;
        return false;
    }

    /// <summary>Görüş alanındaki değiştirilmiş eserlere bir kez bakar; kötü sahteyi fark edebilir (GDD Bölüm 4 tablosu).</summary>
    private void CheckForFakes()
    {
        if (Time.time < nextFakeCheck || state.Value == State.Chase) return;
        nextFakeCheck = Time.time + 0.5f;

        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null) continue;
            if (!d.IsSwapped) { inspected.Remove(d); continue; } // orijinali geri kondu: yeniden değiştirilirse tekrar bakılır
            if (inspected.Contains(d)) continue;
            if (Vector3.Distance(transform.position, d.transform.position) > 5f) continue;
            if (!Vision.CanSeePoint(Eye, transform.forward, 5f, viewAngle, d.transform.position + Vector3.up * 0.6f, d.transform)) continue;

            inspected.Add(d);
            if (Random.value < DetectChance(d.LeftSimilarity))
            {
                PlaySoundRpc(SoundId.GuardHuh, transform.position);
                ServerInvestigate(d.transform.position, true, d);
                return;
            }
        }
    }

    private static float DetectChance(byte similarity)
    {
        if (similarity == InventorySlot.NoSimilarity) return 1f;
        if (similarity >= 90) return 0f;     // Kusursuz: fark etmez
        if (similarity >= 75) return 0.15f;  // İyi: uzun bakarsa
        if (similarity >= 50) return 0.5f;   // Şüpheli
        return 0.9f;                         // Kaba
    }

    // Kare başı OverlapSphere dizisi ayırmasın (tüm güvenlikler sırayla, ana thread'de ve senkron kullanır).
    // 64: 1.2 m küre içindeki zemin + duvar + dekor + kendi çarpıştırıcıları için bol pay (taşarsa fazlası görülmez).
    private static readonly Collider[] doorHits = new Collider[64];

    private void OpenNearbyDoors()
    {
        int n = Physics.OverlapSphereNonAlloc(transform.position + transform.forward * 0.8f, 1.2f, doorHits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = doorHits[i];
            doorHits[i] = null; // sahneden silinen objelere referans tutulmasın
            DoorInteractable door = c.GetComponentInParent<DoorInteractable>();
            if (door != null) door.ServerSetOpen(true);
        }
    }

    /// <summary>Görev seviyesi gelmemiş güvenlik sahnede durur ama yok sayılır (obje kapatılmaz: ağ senkronu bozulmasın).</summary>
    private void ApplyOnline(bool online)
    {
        shownOnline = online;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = online;
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = online;

        if (!IsServer || !IsSpawned) return;
        if (!online)
        {
            if (agent.enabled && agent.isOnNavMesh) agent.ResetPath();
            agent.enabled = false;
            focus = null;
            alert.Value = 0;
            state.Value = State.Patrol;
        }
        // online olunca Update, NavMesh hazırsa agent'ı yeniden açıp devriyeye başlatır
    }

    // ---------------- Yardımcılar ----------------

    private void GoToWaypoint()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        waitUntil = -1f;
        if (waypoints == null || waypoints.Length == 0)
        {
            agent.ResetPath();
            return;
        }
        Transform wp = waypoints[waypointIndex % waypoints.Length];
        if (wp != null) MoveTo(wp.position, patrolSpeed);
    }

    private void MoveTo(Vector3 pos, float speed)
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        agent.speed = speed;
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
        else agent.SetDestination(pos);
    }

    private void FaceTowards(Vector3 pos)
    {
        Vector3 dir = pos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 5f);
    }

    private void SetState(State s)
    {
        if (state.Value == s) return;
        state.Value = s;
        if (s == State.Patrol) alert.Value = 0;
        else if (s == State.Investigate) alert.Value = 30;
        if (s == State.Chase) PlaySoundRpc(SoundId.GuardAlert, transform.position);
    }

    [Rpc(SendTo.Everyone)]
    private void PlaySoundRpc(SoundId id, Vector3 pos)
    {
        GameAudio.PlayAt(id, pos);
    }

    // ---------------- Görünüm (herkes) ----------------

    private void UpdateConeVisual()
    {
        if (cone == null) return;
        Color c;
        switch (state.Value)
        {
            case State.Chase: c = new Color(1f, 0.2f, 0.15f, 0.32f); break;
            case State.Suspicious:
            case State.Investigate: c = new Color(1f, 0.6f, 0.15f, 0.28f); break;
            default: c = new Color(1f, 0.92f, 0.4f, 0.2f); break;
        }
        cone.Set(viewRange, viewAngle, transform.forward, c);
    }

    private GUIStyle iconStyle;

    /// <summary>Kafanın üstünde tepki işareti:
    ///   "?" sarı → turuncu → kırmızı (şüphe doldukça, altında dolum çubuğu)
    ///   "!" kırmızı, büyük ve zıplayarak çıkar (alarm / kovalama)</summary>
    private void OnGUI()
    {
        if (!IsSpawned || !Online) return;

        if (state.Value != shownState)
        {
            shownState = state.Value;
            stateChangedAt = Time.unscaledTime;
        }
        if (state.Value == State.Patrol) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.55f);
        if (sp.z <= 0f || sp.z > 35f) return;

        if (iconStyle == null)
        {
            iconStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        bool chase = state.Value == State.Chase;
        float t = Time.unscaledTime - stateChangedAt;
        float alert01 = alert.Value / 100f;

        // Çıkış animasyonu: büyüyüp yerine oturur
        float pop = t < 0.25f ? Mathf.Lerp(1.8f, 1f, t / 0.25f) : 1f;
        float shake = chase ? Mathf.Sin(Time.unscaledTime * 30f) * 2f : 0f;
        int size = Mathf.RoundToInt((chase ? 46f : 34f + alert01 * 8f) * pop);
        iconStyle.fontSize = size;

        string icon = chase ? "!" : "?";
        Color color = chase ? new Color(1f, 0.2f, 0.15f)
                            : Color.Lerp(new Color(1f, 0.9f, 0.3f), new Color(1f, 0.35f, 0.15f), alert01);

        Rect r = new Rect(sp.x - 30f + shake, Screen.height - sp.y - 40f, 60f, 64f);
        iconStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), icon, iconStyle);
        iconStyle.normal.textColor = color;
        GUI.Label(r, icon, iconStyle);

        // "?" altında şüphe dolum çubuğu
        if (!chase && state.Value == State.Suspicious)
        {
            Rect bar = new Rect(sp.x - 18f, Screen.height - sp.y + 22f, 36f, 5f);
            UIUtil.DrawRect(bar, new Color(0f, 0f, 0f, 0.6f));
            UIUtil.DrawRect(new Rect(bar.x, bar.y, bar.width * alert01, bar.height), color);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (waypoints == null) return;
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.DrawSphere(waypoints[i].position, 0.25f);
            Transform next = waypoints[(i + 1) % waypoints.Length];
            if (next != null) Gizmos.DrawLine(waypoints[i].position, next.position);
        }
    }
}
