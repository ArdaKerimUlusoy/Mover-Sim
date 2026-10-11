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

    public State CurrentState => state.Value;

    private Vector3 Eye => transform.position + Vector3.up * eyeHeight;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.enabled = false; // NavMesh hazır olana kadar
        if (cone == null) cone = GetComponentInChildren<VisionConeVisual>();
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
        investigatePoint = pos;
        investigateIsFake = isFake;
        investigateDisplay = display;
        lookAroundUntil = -1f;
        SetState(State.Investigate);
        MoveTo(pos, investigateSpeed);
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
        SetState(State.Chase);
        MoveTo(pos, chaseSpeed);
    }

    public void ServerOnAlarmEnded()
    {
        if (!IsServer || !Online) return;
        focus = null;
        SetState(State.Patrol);
        GoToWaypoint();
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

        OpenNearbyDoors();
        PlayerStealth seen = LookForPlayers();
        CheckForFakes();

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
            BeginSuspicious(seen);
            return;
        }

        if (lookAroundUntil < 0f)
        {
            if (!agent.pathPending && agent.remainingDistance <= (investigateIsFake ? 1.8f : 0.8f))
            {
                agent.ResetPath();
                lookAroundUntil = Time.time + lookAroundSeconds;

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
            transform.Rotate(0f, 90f * Time.deltaTime, 0f);
            if (Time.time >= lookAroundUntil)
            {
                lookAroundUntil = -1f;
                SetState(State.Patrol);
                GoToWaypoint();
            }
        }
    }

    private void TickChase(PlayerStealth seen)
    {
        agent.speed = chaseSpeed;

        // Hedef: görülen aranan/şüpheli oyuncu
        if (seen != null && (seen.IsWanted || seen.Suspicion > 0.5f))
        {
            focus = seen;
            lastFocusSeen = Time.time;
        }

        if (focus != null && !focus.IsHidden && Time.time - lastFocusSeen < 3f)
        {
            lastKnownPos = focus.transform.position;
            agent.SetDestination(lastKnownPos);

            if (Vector3.Distance(transform.position, focus.transform.position) <= catchDistance)
            {
                PlaySoundRpc(SoundId.GuardAlert, transform.position);
                focus.ServerCatch();
                focus = null;
            }
        }
        else
        {
            focus = null;
            if (!agent.pathPending && agent.remainingDistance <= 0.8f)
            {
                // Son bilinen yerde değilse rastgele yakın bir noktayı ara
                Vector3 random = lastKnownPos + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
                if (NavMesh.SamplePosition(random, out NavMeshHit hit, 3f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
            }
        }
    }

    /// <summary>Görüş alanındaki değiştirilmiş eserlere bir kez bakar; kötü sahteyi fark edebilir (GDD Bölüm 4 tablosu).</summary>
    private void CheckForFakes()
    {
        if (Time.time < nextFakeCheck || state.Value == State.Chase) return;
        nextFakeCheck = Time.time + 0.5f;

        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || !d.IsSwapped || inspected.Contains(d)) continue;
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

    private void OpenNearbyDoors()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position + transform.forward * 0.8f, 1.2f, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
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
