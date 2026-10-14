using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// MAYMUNCUK: Staff Only odasının kilitli kapısını açar.
///  - Maymuncuk garaj mağazasından adetle alınır (kişisel cüzdan, ucuz: UpgradeCatalog.LockpickPrice, en fazla LockpickMax).
///  - Kapıya bakıp E'yi basılı tut. "Başarı" ve "Kırılma" çubukları yarışır (Aion 2'deki zanaat gibi), önce dolan kazanır:
///    Başarı → kapı açılır (maymuncuk harcanmaz). Kırılma → maymuncuk kırılır (bir adet gider), kapı kilitli kalır; başka maymuncukla yeniden dene.
///    Kırılma ihtimali: UpgradeCatalog.LockpickBreakChance. Bu sırada SUÇ işliyorsun: güvenlik görürse yakalar.
///    İtilirsen / bakışını kaçırırsan / E'yi bırakırsan deneme iptal olur (maymuncuk kırılmaz), baştan başlar.
///  - Açılınca kapı bütün ekip için görev sonuna kadar açık kalır (yeni müzede yine kilitli). Açılırken ses çıkar.
///  - Ekipte bir kişinin maymuncuğu olması yeter.
///
/// Ağ (SUNUCU YETKİLİ, named message):
///  istemci → sunucu "SS_LockpickReq": Begin(id, kapı konumu) / Finish(id) / Cancel(id)
///  sunucu → istemci "SS_LockpickRes": Started(id, süre) / Rejected / Cancelled / Success / Broke
///  - Begin: sunucu yakınlığı, maymuncuğu, kapının kilitli olduğunu ve bu oyuncunun süren denemesi olmadığını doğrular;
///    deneme süresini SUNUCU seçer, başlangıcı sunucu saatiyle kaydeder.
///  - Finish: süre dolmadan gelirse reddedilir (deneme düşer, maymuncuk kırılmaz). Doğrulama yeniden yapılır,
///    başarı/kırılma ZARINI SUNUCU atar ve sonucu uygular (kapıyı açar / maymuncuğu kırar), istemciye bildirir.
///  - Deneme sürerken oyuncu uzaklaşır / hapse düşer / saklanır / kapı başkasınca açılır / oyuncu ayrılırsa kayıt silinir.
///  İstemci sonucu deneme bitene kadar bilmez (çubuklar sonuç gelene kadar başa baş gider) — "kırılacak gibi, bırak" hilesi de yok.
/// Sahneye bir şey eklemeye gerek yok.
/// </summary>
public class LockpickSystem : MonoBehaviour
{
    public static LockpickSystem Instance { get; private set; }

    /// <summary>Yerel oyuncu şu an kilit açıyor mu? (PlayerInteractor suç bayrağına ekler.)</summary>
    public static bool IsPicking { get; private set; }

    private const string ReqMsg = "SS_LockpickReq";
    private const string ResMsg = "SS_LockpickRes";
    private const NetworkDelivery Delivery = NetworkDelivery.ReliableSequenced; // Begin/Cancel/Begin sırası korunmalı

    private const float Range = 2.6f;        // kapıya ne kadar yakından açılır
    private const float ServerRange = 4f;    // sunucu: gecikme payıyla
    private const float DoorMatchTolerance = 2f; // istemcinin bildirdiği kapı konumu ile sunucudaki kapı arası
    private const float UnlockNoiseRadius = 4.5f;
    private const float BreakNoiseRadius = 2.5f;

    private const float FinishSlack = 0.05f;     // sunucu: süre kontrolünde yuvarlama payı (gecikme zaten istemci aleyhine)
    private const float FinishGrace = 5f;        // sunucu: süre dolduktan sonra Finish bu kadar gelmezse deneme düşer
    private const float ServerTickInterval = 0.25f;
    private const float ResponseTimeout = 5f;    // istemci: sunucudan cevap gelmezse vazgeç
    private const float ShowTime = 0.45f;        // istemci: sonuç geldikten sonra kazanan çubuğu gösterme süresi

    // Mesaj kodları
    private const byte OpBegin = 0, OpFinish = 1, OpCancel = 2;
    private const byte ResStarted = 0, ResRejected = 1, ResCancelled = 2, ResSuccess = 3, ResBroke = 4;

    private CustomMessagingManager regCmm;
    private NetworkManager regNm;

    private StaffDoor target;
    private bool waitRelease;
    private float nextTick;
    private float sentAt = -10f;

    private static readonly RaycastHit[] rayHits = new RaycastHit[16];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("LockpickSystem");
        DontDestroyOnLoad(go);
        go.AddComponent<LockpickSystem>();
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        Unregister();
        if (Instance == this) Instance = null;
        IsPicking = false;
        attempts.Clear();
    }

    /// <summary>İtildim vb.: kilit açma baştan (E bırakılıp yeniden basılmalı).</summary>
    public static void Interrupt()
    {
        IsPicking = false;
        if (Instance == null) return;
        if (Instance.phase == Phase.Requesting || Instance.phase == Phase.Picking)
        {
            Instance.waitRelease = true; // yarıda kalan deneme sayılmaz, maymuncuk kırılmaz
            Instance.AbortLocal();
        }
    }

    // =====================================================================
    //  İstemci döngüsü
    // =====================================================================

    private enum Phase : byte
    {
        Idle,       // deneme yok
        Requesting, // Begin yollandı, sunucudan Started bekleniyor
        Picking,    // çubuklar doluyor (süre sunucudan geldi)
        Finishing,  // Finish yollandı, sonuç bekleniyor
        Showing     // sonuç geldi, kazanan çubuk kısa süre gösteriliyor
    }

    private Phase phase;
    private float phaseAt;
    private ushort attemptId;
    private bool resultSuccess;

    private float elapsed, attemptTime, phaseS, phaseF, capS, capF;
    private float successBar, failBar, frozenS, frozenF;

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool active = nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient) && nm.CustomMessagingManager != null;
        if (!active)
        {
            if (regCmm != null) Unregister();
            attempts.Clear();
            Reset();
            return;
        }
        if (regCmm != nm.CustomMessagingManager) Register(nm);
        if (nm.IsServer) ServerTick(nm);

        target = FindDoor();
        Keyboard kb = Keyboard.current;
        bool held = kb != null && kb.eKey.isPressed;
        if (!held) waitRelease = false;

        float now = Time.unscaledTime;

        if (phase == Phase.Finishing)
        {
            // Sonuç bekleniyor: E'yi bırakmak artık iptal etmez (bitiş zaten yollandı)
            if (now - phaseAt > ResponseTimeout) EndLocal();
            return;
        }
        if (phase == Phase.Showing)
        {
            float t = Mathf.Clamp01((now - phaseAt) / (ShowTime * 0.6f));
            successBar = resultSuccess ? Mathf.Lerp(frozenS, 1f, t) : frozenS;
            failBar = resultSuccess ? frozenF : Mathf.Lerp(frozenF, 1f, t);
            if (now - phaseAt > ShowTime) EndLocal();
            return;
        }

        PlayerInventory inv = PlayerInventory.Local;
        bool has = inv != null && inv.HasLockpick;

        if (target == null || !has || !held || waitRelease || now - sentAt < 0.8f)
        {
            if (phase != Phase.Idle) AbortLocal();
            return;
        }

        if (phase == Phase.Idle)
        {
            StartRequest(nm);
            return;
        }

        IsPicking = true;

        if (phase == Phase.Requesting)
        {
            if (now - phaseAt > ResponseTimeout) { waitRelease = true; AbortLocal(); }
            return;
        }

        // Phase.Picking
        elapsed += Time.deltaTime;
        float x = Mathf.Clamp01(elapsed / attemptTime);
        // Sonuç henüz bilinmiyor (zarı sunucu bitişte atar): iki çubuk başa baş dolar, ikisi de 1'e varmaz.
        successBar = capS * Wobble(x, phaseS);
        failBar = capF * Wobble(x, phaseF);

        // Kilit kurcalama sesi (yalnızca kendin duyarsın; asıl ses kilit açılınca / kırılınca)
        if (now >= nextTick)
        {
            nextTick = now + 0.55f;
            GameAudio.PlayAt(SoundId.UiClick, target.Center, 0.35f, 0.25f);
        }

        if (x >= 1f)
        {
            frozenS = successBar;
            frozenF = failBar;
            phase = Phase.Finishing; // yerel host'ta cevap senkron gelir: önce durumu değiştir
            phaseAt = now;
            IsPicking = false;
            waitRelease = true;
            sentAt = now;
            SendToServer(OpFinish, attemptId, Vector3.zero);
        }
    }

    private void StartRequest(NetworkManager nm)
    {
        attemptId++;
        if (attemptId == 0) attemptId = 1;
        phase = Phase.Requesting; // yerel host'ta Started senkron gelir
        phaseAt = Time.unscaledTime;
        elapsed = 0f;
        successBar = failBar = 0f;
        IsPicking = true;
        SendToServer(OpBegin, attemptId, target.Center);
    }

    /// <summary>Süren denemeyi iptal et (maymuncuk kırılmaz) ve sunucuya bildir.</summary>
    private void AbortLocal()
    {
        bool tellServer = phase == Phase.Requesting || phase == Phase.Picking;
        ushort id = attemptId;
        EndLocal();
        if (tellServer) SendToServer(OpCancel, id, Vector3.zero);
    }

    private void EndLocal()
    {
        phase = Phase.Idle;
        IsPicking = false;
        elapsed = 0f;
        successBar = failBar = 0f;
    }

    /// <summary>Sunucudan gelen cevap (uzak istemcide mesajla, host'ta doğrudan).</summary>
    private void ApplyResult(byte code, ushort id, float duration)
    {
        if (id != attemptId) return; // eski bir denemeye ait
        switch (code)
        {
            case ResStarted:
                if (phase != Phase.Requesting) return;
                phase = Phase.Picking;
                phaseAt = Time.unscaledTime;
                attemptTime = Mathf.Clamp(duration, 0.5f, 15f);
                elapsed = 0f;
                capS = UnityEngine.Random.Range(0.86f, 0.97f);
                capF = UnityEngine.Random.Range(0.86f, 0.97f);
                phaseS = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                phaseF = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                successBar = failBar = 0f;
                break;

            case ResRejected:
            case ResCancelled:
                if (phase == Phase.Idle || phase == Phase.Showing) return;
                waitRelease = true;
                EndLocal();
                break;

            case ResSuccess:
            case ResBroke:
                if (phase != Phase.Finishing) return;
                resultSuccess = code == ResSuccess;
                phase = Phase.Showing;
                phaseAt = Time.unscaledTime;
                IsPicking = false;
                break;
        }
    }

    /// <summary>0→1 giden ama düzgün olmayan (bazen hızlanan, bazen yavaşlayan) eğri. f(0)=0, f(1)=1, hep artar.</summary>
    private static float Wobble(float x, float phase)
    {
        const float A = 0.035f;
        float f = x + A * (Mathf.Sin(2f * Mathf.PI * 2f * x + phase) - Mathf.Sin(phase));
        return Mathf.Clamp01(f);
    }

    private void Reset()
    {
        target = null;
        EndLocal();
        waitRelease = false;
    }

    /// <summary>Yerel oyuncu kilitli kapıya bakıyor mu (yakından, arada bir şey yokken)?</summary>
    private StaffDoor FindDoor()
    {
        if (StaffDoor.All.Count == 0 || StaffDoor.Unlocked) return null;
        PlayerStealth me = PlayerStealth.Local;
        Camera cam = Camera.main;
        if (me == null || cam == null || !me.IsSpawned) return null;
        if (ChatUI.IsTyping || SessionMenuUI.IsPaused || PlayerInventory.IsPanelOpen || WorkbenchUI.IsOpen || BriefingUI.IsOpen || PlayerController.InputLocked) return null;
        if (Cursor.lockState != CursorLockMode.Locked || me.IsJailed || me.CurrentHidingSpot != null || PushSystem.LocalKnockedOut) return null;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        int n = Physics.RaycastNonAlloc(ray, rayHits, Range + 1.5f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Collider first = null;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = rayHits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(me.transform) || h.distance >= best) continue;
            best = h.distance;
            first = h.collider;
        }
        if (first == null) return null;
        StaffDoor door = first.GetComponentInParent<StaffDoor>();
        if (door == null) return null;
        Vector3 feet = me.transform.position;
        Vector3 c = door.Center;
        c.y = feet.y;
        return Vector3.Distance(feet, c) <= Range ? door : null;
    }

    // =====================================================================
    //  Ağ
    // =====================================================================

    private void Register(NetworkManager nm)
    {
        Unregister();
        regNm = nm;
        regCmm = nm.CustomMessagingManager;
        regCmm.RegisterNamedMessageHandler(ReqMsg, OnRequest);
        regCmm.RegisterNamedMessageHandler(ResMsg, OnResponse);
    }

    private void Unregister()
    {
        if (regCmm != null)
        {
            try { regCmm.UnregisterNamedMessageHandler(ReqMsg); }
            catch (Exception) { }
            try { regCmm.UnregisterNamedMessageHandler(ResMsg); }
            catch (Exception) { }
        }
        regCmm = null;
        regNm = null;
    }

    private void SendToServer(byte op, ushort id, Vector3 doorPos)
    {
        NetworkManager nm = regNm != null ? regNm : NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;
        if (nm.IsServer)
        {
            ServerHandle(nm.LocalClientId, op, id, doorPos);
            return;
        }
        if (regCmm == null) return;
        using (var w = new FastBufferWriter(32, Allocator.Temp))
        {
            w.WriteValueSafe(op);
            w.WriteValueSafe(id);
            if (op == OpBegin) w.WriteValueSafe(doorPos);
            regCmm.SendNamedMessage(ReqMsg, NetworkManager.ServerClientId, w, Delivery);
        }
    }

    private void OnRequest(ulong sender, FastBufferReader reader)
    {
        if (regNm == null || !regNm.IsServer) return;
        try
        {
            reader.ReadValueSafe(out byte op);
            reader.ReadValueSafe(out ushort id);
            Vector3 pos = Vector3.zero;
            if (op == OpBegin) reader.ReadValueSafe(out pos);
            ServerHandle(sender, op, id, pos);
        }
        catch (Exception) { }
    }

    private void OnResponse(ulong sender, FastBufferReader reader)
    {
        if (regNm == null || regNm.IsServer || sender != NetworkManager.ServerClientId) return;
        try
        {
            reader.ReadValueSafe(out byte code);
            reader.ReadValueSafe(out ushort id);
            reader.ReadValueSafe(out float duration);
            ApplyResult(code, id, duration);
        }
        catch (Exception) { }
    }

    // =====================================================================
    //  Sunucu
    // =====================================================================

    private sealed class Attempt
    {
        public ushort id;
        public StaffDoor door;
        public double start;     // sunucu saati (Time.unscaledTimeAsDouble)
        public float duration;   // sunucunun seçtiği deneme süresi
    }

    private readonly Dictionary<ulong, Attempt> attempts = new Dictionary<ulong, Attempt>();
    private readonly List<ulong> removeBuf = new List<ulong>();
    private float nextServerTick;

    private static double ServerNow => Time.unscaledTimeAsDouble;

    private void ServerHandle(ulong clientId, byte op, ushort id, Vector3 doorPos)
    {
        switch (op)
        {
            case OpBegin: ServerBegin(clientId, id, doorPos); break;
            case OpFinish: ServerFinish(clientId, id); break;
            case OpCancel:
                if (attempts.TryGetValue(clientId, out Attempt a) && a.id == id) attempts.Remove(clientId);
                break;
        }
    }

    private void ServerBegin(ulong clientId, ushort id, Vector3 doorPos)
    {
        if (attempts.ContainsKey(clientId)) { Respond(clientId, ResRejected, id); return; } // süren denemesi var
        if (!ServerValidate(clientId, out _, out StaffDoor door)) { Respond(clientId, ResRejected, id); return; }

        // İstemcinin baktığı kapı sunucudaki yakın kapıyla örtüşmeli (NaN → reddedilir)
        float diff = Vector3.Distance(door.Center, doorPos);
        if (!(diff <= DoorMatchTolerance)) { Respond(clientId, ResRejected, id); return; }

        float duration = UpgradeCatalog.LockpickSeconds * UnityEngine.Random.Range(0.8f, 1.15f);
        attempts[clientId] = new Attempt { id = id, door = door, start = ServerNow, duration = duration };
        Respond(clientId, ResStarted, id, duration);
    }

    private void ServerFinish(ulong clientId, ushort id)
    {
        if (!attempts.TryGetValue(clientId, out Attempt a) || a.id != id) { Respond(clientId, ResRejected, id); return; }
        attempts.Remove(clientId);

        // Süre dolmadan gelen bitiş: reddedilir, deneme düşer (maymuncuk kırılmaz, kapı açılmaz)
        if (ServerNow - a.start < a.duration - FinishSlack) { Respond(clientId, ResRejected, id); return; }

        if (!ServerValidate(clientId, out PlayerInventory inv, out StaffDoor near) || near != a.door)
        {
            Respond(clientId, ResCancelled, id);
            return;
        }

        // Zarı SUNUCU atar
        bool success = UnityEngine.Random.value >= UpgradeCatalog.LockpickBreakChance;
        if (!success)
        {
            // Maymuncuk kırıldı: bir adet gider, kapı kilitli kalır (küçük bir "çıt" sesi)
            inv.ServerBreakLockpick();
            NoiseSystem.Emit(near.Center, BreakNoiseRadius);
            inv.ServerPlaySound(SoundId.ItemThud, near.Center);
            inv.ServerNotify("lockpick.broke", inv.LockpickCount.ToString());
            Respond(clientId, ResBroke, id);
            return;
        }

        // Başarı: kapı açılır. Maymuncuk harcanmaz (tasarım: yalnızca kırılınca bir adet gider).
        MuseumSession.Instance.ServerOpenStaffDoor();
        NoiseSystem.Emit(near.Center, UnlockNoiseRadius);
        inv.ServerPlaySound(SoundId.HoldComplete, near.Center);
        inv.ServerNotify("lockpick.done");
        Respond(clientId, ResSuccess, id);
    }

    /// <summary>Oyuncu şu an kilit açabilir mi? (bağlı, maymuncuğu var, hapiste/saklı değil, kilitli kapıya yakın)</summary>
    private bool ServerValidate(ulong clientId, out PlayerInventory inv, out StaffDoor near)
    {
        inv = null;
        near = null;
        NetworkManager nm = regNm != null ? regNm : NetworkManager.Singleton;
        MuseumSession ms = MuseumSession.Instance;
        if (nm == null || !nm.IsServer || ms == null || ms.StaffDoorOpen) return false;
        if (!nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null) return false;

        inv = client.PlayerObject.GetComponent<PlayerInventory>();
        PlayerStealth st = client.PlayerObject.GetComponent<PlayerStealth>();
        if (inv == null || !inv.HasLockpick) return false;
        if (st != null && (st.IsJailed || st.IsHidden)) return false;

        Vector3 pos = client.PlayerObject.transform.position;
        float best = float.MaxValue;
        foreach (StaffDoor d in StaffDoor.All)
        {
            if (d == null) continue;
            Vector3 c = d.Center;
            c.y = pos.y;
            float dist = Vector3.Distance(pos, c);
            if (dist <= ServerRange && dist < best) { best = dist; near = d; }
        }
        return near != null;
    }

    /// <summary>Süren denemeleri denetle: ayrılan oyuncu, uzaklaşma, hapis/saklanma, kapı açıldı, zaman aşımı.</summary>
    private void ServerTick(NetworkManager nm)
    {
        if (attempts.Count == 0) return;
        float now = Time.unscaledTime;
        if (now < nextServerTick) return;
        nextServerTick = now + ServerTickInterval;

        removeBuf.Clear();
        double t = ServerNow;
        foreach (KeyValuePair<ulong, Attempt> kv in attempts)
        {
            ulong cid = kv.Key;
            Attempt a = kv.Value;
            if (!nm.ConnectedClients.ContainsKey(cid)) { removeBuf.Add(cid); continue; } // oyuncu ayrıldı: sessizce sil

            bool expired = t - a.start > a.duration + FinishGrace;
            bool valid = ServerValidate(cid, out _, out StaffDoor near) && near == a.door;
            if (expired || !valid)
            {
                removeBuf.Add(cid);
                Respond(cid, ResCancelled, a.id);
            }
        }
        foreach (ulong cid in removeBuf) attempts.Remove(cid);
    }

    private void Respond(ulong clientId, byte code, ushort id, float duration = 0f)
    {
        NetworkManager nm = regNm != null ? regNm : NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (clientId == nm.LocalClientId)
        {
            ApplyResult(code, id, duration); // host: yerel
            return;
        }
        if (regCmm == null) return;
        using (var w = new FastBufferWriter(16, Allocator.Temp))
        {
            w.WriteValueSafe(code);
            w.WriteValueSafe(id);
            w.WriteValueSafe(duration);
            regCmm.SendNamedMessage(ResMsg, clientId, w, Delivery);
        }
    }

    // =====================================================================
    //  Arayüz
    // =====================================================================

    private void OnGUI()
    {
        PlayerInventory inv = PlayerInventory.Local;
        if (phase != Phase.Idle)
        {
            DrawRacePanel(inv != null ? inv.LockpickCount : 0);
            return;
        }
        if (target == null || StaffDoor.Unlocked) return;
        bool has = inv != null && inv.HasLockpick;

        if (!has)
        {
            UIUtil.DrawPrompt(Loc.T("lockpick.need", UpgradeCatalog.LockpickPrice.ToString("N0")), UIUtil.CenterSlot(), new Color(1f, 0.55f, 0.45f));
            return;
        }
        UIUtil.DrawPrompt(Loc.T("lockpick.prompt", inv.LockpickCount), UIUtil.CenterSlot(), Color.white);
    }

    private GUIStyle titleStyle, barStyle;

    /// <summary>Başarı (mavi) ve Kırılma (turuncu) çubukları — önce dolan kazanır.</summary>
    private void DrawRacePanel(int count)
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = new Color(0.55f, 1f, 0.55f);
            barStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            barStyle.normal.textColor = Color.white;
        }

        const float W = 340f, H = 86f;
        float y = UIUtil.CenterSlot(H);
        Rect panel = new Rect((Screen.width - W) * 0.5f, y + 10f, W, H);
        UIUtil.DrawRect(panel, new Color(0.04f, 0.08f, 0.12f, 0.82f));
        UIUtil.DrawRect(new Rect(panel.x, panel.y, panel.width, 1f), new Color(0.4f, 0.6f, 0.75f, 0.7f));
        GUI.Label(new Rect(panel.x, panel.y + 4f, W, 22f), Loc.T("lockpick.title", count), titleStyle);

        DrawBar(new Rect(panel.x + 14f, panel.y + 32f, W - 28f, 18f), successBar, new Color(0.25f, 0.55f, 0.95f), Loc.T("lockpick.success"));
        DrawBar(new Rect(panel.x + 14f, panel.y + 56f, W - 28f, 18f), failBar, new Color(0.95f, 0.55f, 0.12f), Loc.T("lockpick.fail"));
    }

    private void DrawBar(Rect r, float v, Color fill, string label)
    {
        UIUtil.DrawRect(r, new Color(0f, 0f, 0f, 0.6f));
        UIUtil.DrawRect(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * Mathf.Clamp01(v), r.height - 2f), fill);
        GUI.Label(new Rect(r.x + 5f, r.y, r.width, r.height), label, barStyle);
    }
}
