using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// YAKA KAMERASI + KAMYON MONİTÖRÜ (ressam ekip):
///  - Müzedeki oyuncu H ile KAYIT açar: kamyondaki monitöre onun BAKIŞ AÇISI canlı gelir. Atölyede çizen kişi orijinali
///    artık doğrudan görmez; monitördeki görüntüye (canlı ya da o eserin son kaydına) bakarak çizer.
///    (Görüntü ağdan video olarak gitmez: herkes müzeyi aynı kurduğu için kayıt yapanın konumundan kendisi çizer → çok ucuz.)
///  - Kayıt yapan bir esere ~1 sn bakınca o eserin "son kaydı" saklanır: canlı yayın kapansa da ressam ona bakarak çizer.
///  - PİL: sınırlı (taban 60 sn, garajdan "Kamera Pili" ile uzar). Kamyonda şarj olur.
///  - KAYIT IŞIĞI: kayıttayken göğsünde kırmızı ışık yanıp söner. Kaydın YASAK olduğu odada (Staff Only, güvenlik odası,
///    bazı galeriler; tabelası var) ışığı gören ziyaretçi güvenliğe gidip şikayet eder (şüphe artar), güvenlik / kamera
///    görürse şüphelenir. Serbest odalarda ziyaretçiler de fotoğraf çeker: aralarına karışırsın.
///  - TABLO BAŞINDA ÇOK DURMAK şüphe çeker (~8 sn sonra biraz, ~15 sn sonra çok).
///  - SOLUK ETİKETLER: bazı eserlerin isim etiketi soluk: adı ancak çok yakından (≤1.4 m) okununca herkes için açılır
///    (o zamana kadar listelerde / planda "G?n B?t??" gibi görünür).
///  - KAMYON MONİTÖRÜ: E ile bakınca büyük ekran: canlı görüntü + müzenin KAT PLANI (odalar, kayıt yasak odalar, eserler,
///    takım arkadaşları, kayıt yapan). Kamyondaki kişi müzedekini yönetir: haritaya tıklayınca ekibe işaret gider.
///  - KAMYONDA MİNİ HARİTA: kamyondayken sağ üstte müze haritası hep açık; atölyede tuvalin altında da görünür.
/// OPTİMİZASYON: canlı görüntü sadece monitör ekranda görünürken (ya da büyük ekran / atölye açıkken) çizilir;
/// gölge, post-process, derinlik dokusu kapalı; 640x360, 10–15 kare/sn. Kimse bakmıyorsa hiç çizilmez.
/// BİRDEN FAZLA KAYIT: aynı anda en fazla 4 kişi kayıt açabilir. Her bilgisayar monitörde / atölyede HANGİ kaydı izleyeceğini
/// kendisi seçer (monitöre bakıp E → büyük ekranda "Kayıtlar" listesi, atölyede tuvalin altındaki liste): canlı yayınlardan
/// biri ya da bir eserin son kaydı. Ben A'yı seçince bende A, arkadaşım B'yi seçince onda B görünür (seçim yereldir).
/// Optimizasyon: her bilgisayar yalnızca İZLEDİĞİ canlı yayını çizer (tek kamera); diğer kayıt yapanlar için yalnızca
/// "son kayıt" anında tek kare çizilir (aynı kamera, ayrı doku).
/// Ağ: "SS_CamReq" (istemci → sunucu: kayıt aç/kapa), "SS_CamState" (sunucu → herkes: kimler kayıtta + okunmuş etiketler),
/// "SS_PlaqueRead" (istemci → sunucu). Sahneye bir şey eklemeye gerek yok.
/// </summary>
public class BodyCam : MonoBehaviour
{
    public static BodyCam Instance { get; private set; }

    private const string ReqMsg = "SS_CamReq", StateMsg = "SS_CamState", ReadMsg = "SS_PlaqueRead";
    public const float BaseBatterySeconds = 60f;
    public const float BatteryPerLevel = 45f;
    private const float ChargePerSecond = 0.06f;   // kamyonda: tam pilin %6'sı / sn
    private const float LiveFps = 15f;
    private const float EyeHeight = 0.6f;           // PlayerController.standingCameraY ile aynı
    private const float ReadDistance = 1.4f;
    private const float FeedForward = 0.12f;        // kayıt kamerası gözün bu kadar önünde (kafanın içinden çekmesin)
    private const ulong Nobody = ulong.MaxValue;

    // ---------------- Herkeste: ortak durum ----------------
    public const int MaxRecorders = 4;
    /// <summary>Şu an kayıt yapan oyuncular (sıralı, herkeste aynı).</summary>
    private static readonly List<ulong> recorders = new List<ulong>();
    public static IReadOnlyList<ulong> Recorders => recorders;
    public static bool Live => recorders.Count > 0;
    public static bool IsRecording(ulong client) => recorders.Contains(client);
    /// <summary>Bu bilgisayarın İZLEDİĞİ canlı yayın (seçilmişse o, yoksa ilk kayıt yapan; kimse yoksa ulong.MaxValue).</summary>
    public static ulong Recorder => WatchedRecorder;
    /// <summary>Bu bilgisayarın oyuncusu kayıtta mı?</summary>
    public static bool LocalRecording
    {
        get
        {
            NetworkManager nm = NetworkManager.Singleton;
            return Live && nm != null && nm.IsListening && recorders.Contains(nm.LocalClientId);
        }
    }

    // ---------------- Yerel: hangi kayıt izleniyor (her bilgisayar kendi seçer) ----------------
    private static ulong watchLive = Nobody;     // seçilen canlı yayın
    private static string watchStill;            // seçilen "son kayıt" (eser objesi adı)

    public static ulong WatchedRecorder
    {
        get
        {
            if (recorders.Count == 0) return Nobody;
            if (watchLive != Nobody && recorders.Contains(watchLive)) return watchLive;
            // Varsayılan: kendim dışındaki ilk kayıt yapan (kendi yayınımı izlemem anlamsız), yoksa ilk
            NetworkManager nm = NetworkManager.Singleton;
            ulong me = nm != null ? nm.LocalClientId : Nobody;
            foreach (ulong id in recorders) if (id != me) return id;
            return recorders[0];
        }
    }

    /// <summary>Yerel seçim: canlı yayın.</summary>
    public static void WatchLive(ulong client) { watchLive = client; watchStill = null; }
    /// <summary>Yerel seçim: bir eserin son kaydı.</summary>
    public static void WatchStill(string displayName) { watchStill = displayName; }

    private static readonly HashSet<string> readPlaques = new HashSet<string>(); // okunmuş soluk etiketler (eser objesi adı)
    private static int plaqueSeed;

    // ---------------- Yerel ----------------
    private CustomMessagingManager regCmm;
    private NetworkManager regNm;
    private float battery = -1f;   // sn
    private float lastMaxBattery;
    private int lastSeed;
    private float nextReadCheck;
    private static int plaqueVersion;

    // Kare başına önbellek (OnGUI karede birkaç kez çağrılır: ışın / döngü tekrarlanmasın)
    private bool inTruck, nearMonitor, lookingAtMonitor;
    private float nextLookCheck, nextTargetCheck;
    private ArtworkDisplay cachedTarget;
    private Renderer screenRenderer;
    private readonly Dictionary<ArtworkDisplay, string> nameCache = new Dictionary<ArtworkDisplay, string>();
    private float nameCacheTime;
    private PingSystem ping;
    private float nextPingFind;

    // Görüntü
    private Camera feedCam;
    private RenderTexture liveRT;
    private bool liveHasFrame;      // canlı dokuya bu oturumda gerçekten kare çizildi mi (eski/boş kare gösterilmesin)
    private bool wasLive;
    // Kayıt yapanın kendi avatarı: kayıt kamerası çizilirken geçici gizlenir (kafa / burun / göz görünmesin)
    private readonly List<Renderer> hideBuf = new List<Renderer>();
    private readonly List<Renderer> hiddenNow = new List<Renderer>();
    private Texture2D noSignal;
    private float nextLiveRender;
    private bool renderedThisFrame;
    private string pendingStill;     // bu karede çizilen görüntü hangi eserin son kaydı olacak
    private RenderTexture pendingSrc; // o görüntünün çizildiği doku (izlenen yayın: liveRT, diğer kayıt yapanlar: captureRT)
    private RenderTexture captureRT;  // izlenmeyen kayıt yapanların "son kayıt" karesi buraya çizilir
    private ulong feedFor = Nobody;   // feed kamerası bu karede kimin gözünden çiziyor (avatarı gizlenir)
    private ulong lastWatched = Nobody;
    private sealed class LookTrack { public ArtworkDisplay target; public float since; }
    private readonly Dictionary<ulong, LookTrack> looks = new Dictionary<ulong, LookTrack>();
    private readonly Dictionary<string, RenderTexture> stills = new Dictionary<string, RenderTexture>();
    private readonly Dictionary<string, float> stillTime = new Dictionary<string, float>();
    private ArtworkDisplay lookTarget;
    private float lookSince;

    // Kayıt ışıkları (kayıt yapan başına bir tane, havuzdan)
    private readonly Dictionary<ulong, GameObject> recLights = new Dictionary<ulong, GameObject>();
    private readonly List<GameObject> recLightPool = new List<GameObject>();
    private GameObject recLight; // ön hazırlık (ilk kayıtta donma olmasın)
    // Kayıt yasak yerde kayıt: hemen fark edilmez, IllegalGraceSeconds sn sonra (sunucu sayar; yasak yerden çıkınca / kayıt
    // kapanınca süre sıfırlanır: yeniden açınca 8 sn baştan sayar)
    public const float IllegalGraceSeconds = 8f;
    private readonly Dictionary<ulong, float> illegalTime = new Dictionary<ulong, float>();
    private float localIllegalTime; // sadece ekrandaki geri sayım için (sahibi kendi hesaplar)

    // Açılışta donma olmasın: görüntü kamerası, kayıt ışığı ve ilk çizim müze kurulunca önceden hazırlanır
    private float prewarmAt = -1f;
    private int prewarmFrames; // kalan ön çizim karesi (müzenin ortasından 4 yöne + oyuncunun gözü)

    // Monitör
    private Transform monitor;
    private Material monitorMat;
    // Kamyonda monitörün sağındaki HARİTA EKRANI (kat planı ekrana çizilir; sadece görünürken, saniyede birkaç kez)
    private const int MapW = 768, MapH = 480;
    private const float MapFps = 4f;
    private RenderTexture mapRT;
    private Renderer mapRenderer;
    private float nextMapRender;
    private bool panelOpen;
    private bool cursorWasLocked;

    // Sunucu
    private float nextBroadcast;
    private bool stateDirty;
    private readonly Dictionary<ulong, float> stare = new Dictionary<ulong, float>();
    private readonly Dictionary<ulong, ArtworkDisplay> stareAt = new Dictionary<ulong, ArtworkDisplay>();
    private float nextStareTick;

    private GUIStyle hudStyle, smallStyle, titleStyle, planStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("BodyCam");
        DontDestroyOnLoad(go);
        go.AddComponent<BodyCam>();
    }

    private void Awake()
    {
        Instance = this;
        CanvasPainter.ReferenceProvider = PaintingReference;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginFeedCamera;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnEndFeedCamera;
        CanvasPainter.ExtraPanelDrawer = DrawCanvasPlan;
    }

    private void OnDestroy()
    {
        Unregister();
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginFeedCamera;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnEndFeedCamera;
        RestoreHidden();
        if (Instance == this) Instance = null;
        CanvasPainter.ReferenceProvider = null;
        CanvasPainter.ExtraPanelDrawer = null;
        if (liveRT != null) liveRT.Release();
        if (captureRT != null) captureRT.Release();
        if (mapRT != null) mapRT.Release();
        foreach (RenderTexture rt in stills.Values) if (rt != null) rt.Release();
    }

    // =====================================================================
    //  Döngü
    // =====================================================================

    private void Update()
    {
        renderedThisFrame = false;
        NetworkManager nm = NetworkManager.Singleton;
        bool active = nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient) && nm.CustomMessagingManager != null;
        if (!active)
        {
            if (regCmm != null) Unregister();
            recorders.Clear();
            watchLive = Nobody;
            watchStill = null;
            readPlaques.Clear();
            plaqueVersion++;
            ClosePanel();
            UpdateRecLight();
            ClearFeed(); // oturum bitti: monitör / önizleme temizlenir, feed kamerası kapanır
            wasLive = false;
            return;
        }
        if (regCmm != nm.CustomMessagingManager) Register(nm);

        MuseumSession ms = MuseumSession.Instance;
        int seed = ms != null && ms.Ready ? ms.Seed : 0;
        if (seed != lastSeed)
        {
            // Yeni müze: pil dolar, eski kayıtlar ve okunmuş etiketler silinir
            lastSeed = seed;
            battery = MaxBattery();
            foreach (RenderTexture rt in stills.Values) if (rt != null) rt.Release();
            stills.Clear();
            stillTime.Clear();
            looks.Clear();
            watchStill = null;
            if (nm.IsServer) { readPlaques.Clear(); plaqueSeed = seed; stateDirty = true; recorders.Clear(); }
            plaqueVersion++;
            illegalTime.Clear();
            prewarmAt = Time.unscaledTime + 1.5f; // müze kurulduktan biraz sonra
        }
        if (prewarmAt > 0f && Time.unscaledTime >= prewarmAt && Camera.main != null)
        {
            prewarmAt = -1f;
            EnsureFeed();
            if (recLight == null) recLight = MakeRecLight();
            prewarmFrames = 5; // UpdateFeed birkaç kare görünmez çizer: shader varyantları yükleme sırasında derlenir
        }

        PlayerStealth meNow = PlayerStealth.Local;
        inTruck = meNow != null && TruckZone.Instance != null && TruckZone.Instance.Contains(meNow.transform.position);
        if (Time.unscaledTime >= nextLookCheck)
        {
            nextLookCheck = Time.unscaledTime + 0.1f;
            nearMonitor = NearMonitor();
            lookingAtMonitor = nearMonitor && ComputeLookingAtMonitor();
            PhotoSign.Tick();
        }

        if (nm.IsServer) ServerTick(nm);
        LocalTick(nm);
        UpdateRecLight();
        EnsureMonitor();
        UpdateFeed();
    }

    private float MaxBattery()
    {
        PlayerInventory inv = PlayerInventory.Local;
        int lv = inv != null ? inv.GetUpgradeLevel(UpgradeType.CameraBattery) : 0;
        return UpgradeCatalog.BatteryCapacity(lv); // garajdaki "Kamera Pili" (arkadaşın kataloğu: 60 sn + 30 sn / seviye)
    }

    // ---------------- Yerel oyuncu: kayıt tuşu, pil, etiket okuma ----------------

    private void LocalTick(NetworkManager nm)
    {
        float max = MaxBattery();
        if (battery < 0f) battery = max;
        if (max > lastMaxBattery && lastMaxBattery > 0f) battery += max - lastMaxBattery; // garajdan pil alındı
        lastMaxBattery = max;
        battery = Mathf.Clamp(battery, 0f, max);

        PlayerStealth me = PlayerStealth.Local;
        bool rec = LocalRecording;
        {
            MuseumLayout ll = MuseumLayout.Instance;
            bool illegalNow = rec && me != null && ll != null && ll.IsNoPhotoAt(me.transform.position);
            localIllegalTime = illegalNow ? localIllegalTime + Time.deltaTime : 0f;
        }
        if (rec)
        {
            battery -= Time.deltaTime;
            if (battery <= 0f || me == null || me.IsJailed || me.CurrentHidingSpot != null)
            {
                battery = Mathf.Max(0f, battery);
                SendRequest(nm, false);
                if (battery <= 0f && PlayerInventory.Local != null) PlayerInventory.Local.ShowLocalToast(Loc.T("cam.empty"));
            }
        }
        else if (inTruck)
        {
            battery = Mathf.Min(max, battery + max * ChargePerSecond * Time.deltaTime); // kamyonda şarj
        }

        // H: kayıt aç / kapa
        Keyboard kb = Keyboard.current;
        bool canAct = kb != null && !ChatUI.IsTyping && !SessionMenuUI.IsPaused && !WorkbenchUI.IsOpen && me != null && !me.IsJailed;
        if (canAct && kb.hKey.wasPressedThisFrame)
        {
            if (rec) SendRequest(nm, false);
            else if (battery < 1f) { if (PlayerInventory.Local != null) PlayerInventory.Local.ShowLocalToast(Loc.T("cam.empty")); }
            else if (recorders.Count >= MaxRecorders) { if (PlayerInventory.Local != null) PlayerInventory.Local.ShowLocalToast(Loc.T("cam.busy")); }
            else SendRequest(nm, true); // birden fazla kişi aynı anda kayıt açabilir
        }

        // Monitör paneli
        if (panelOpen)
        {
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) || !NearMonitor()) ClosePanel();
        }
        else if (canAct && kb.eKey.wasPressedThisFrame && LookingAtMonitor()) OpenPanel();

        // Soluk etiket okuma (yakından)
        if (me != null && Time.unscaledTime >= nextReadCheck)
        {
            nextReadCheck = Time.unscaledTime + 0.25f;
            Camera cam = Camera.main;
            foreach (ArtworkDisplay d in ArtworkDisplay.All)
            {
                if (d == null || !IsFaded(d) || readPlaques.Contains(d.name)) continue;
                Vector3 p = d.transform.position + Vector3.up * 0.4f;
                if (Vector3.Distance(me.transform.position, p) > ReadDistance + 0.6f) continue;
                if (cam != null && Vector3.Angle(cam.transform.forward, p - cam.transform.position) > 40f) continue;
                SendRead(nm, d.name);
            }
        }
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
        regCmm.RegisterNamedMessageHandler(StateMsg, OnState);
        regCmm.RegisterNamedMessageHandler(ReadMsg, OnRead);
        stateDirty = true;
    }

    private void Unregister()
    {
        if (regCmm != null)
        {
            try
            {
                regCmm.UnregisterNamedMessageHandler(ReqMsg);
                regCmm.UnregisterNamedMessageHandler(StateMsg);
                regCmm.UnregisterNamedMessageHandler(ReadMsg);
            }
            catch (Exception) { }
        }
        regCmm = null;
        regNm = null;
    }

    private void SendRequest(NetworkManager nm, bool on)
    {
        if (nm.IsServer) { ServerRequest(nm.LocalClientId, on); return; }
        using (var w = new FastBufferWriter(4, Allocator.Temp))
        {
            w.WriteValueSafe((byte)(on ? 1 : 0));
            regCmm.SendNamedMessage(ReqMsg, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
    }

    private void SendRead(NetworkManager nm, string displayName)
    {
        if (nm.IsServer) { ServerRead(displayName); return; }
        using (var w = new FastBufferWriter(64, Allocator.Temp))
        {
            w.WriteValueSafe(displayName);
            regCmm.SendNamedMessage(ReadMsg, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
        if (readPlaques.Add(displayName)) plaqueVersion++; // kendi ekranında hemen açılsın
    }

    private void OnRequest(ulong sender, FastBufferReader r)
    {
        if (regNm == null || !regNm.IsServer) return;
        try { r.ReadValueSafe(out byte on); ServerRequest(sender, on == 1); } catch (Exception) { }
    }

    private void OnRead(ulong sender, FastBufferReader r)
    {
        if (regNm == null || !regNm.IsServer) return;
        try { r.ReadValueSafe(out string n); ServerRead(n); } catch (Exception) { }
    }

    private void OnState(ulong sender, FastBufferReader r)
    {
        if (regNm == null || regNm.IsServer || sender != NetworkManager.ServerClientId) return;
        try
        {
            r.ReadValueSafe(out byte rc);
            recorders.Clear();
            for (int i = 0; i < rc; i++) { r.ReadValueSafe(out ulong id); recorders.Add(id); }
            r.ReadValueSafe(out int seed);
            r.ReadValueSafe(out ushort n);
            readPlaques.Clear();
            plaqueSeed = seed;
            for (int i = 0; i < n; i++) { r.ReadValueSafe(out string s); readPlaques.Add(s); }
            plaqueVersion++;
        }
        catch (Exception) { }
    }

    // =====================================================================
    //  Sunucu
    // =====================================================================

    private void ServerRequest(ulong client, bool on)
    {
        if (on)
        {
            if (recorders.Contains(client) || recorders.Count >= MaxRecorders) return;
            recorders.Add(client);
            recorders.Sort();
        }
        else if (!recorders.Remove(client)) return;
        stateDirty = true;
    }

    private void ServerRead(string displayName)
    {
        if (string.IsNullOrEmpty(displayName) || displayName.Length > 48) return;
        if (readPlaques.Add(displayName)) { stateDirty = true; plaqueVersion++; }
    }

    private void ServerTick(NetworkManager nm)
    {
        // Kayıt yapan çıktıysa / hapse girdiyse kayıt kapanır
        for (int i = recorders.Count - 1; i >= 0; i--)
        {
            ulong id = recorders[i];
            PlayerStealth rs = FindStealth(id);
            if (!nm.ConnectedClients.ContainsKey(id) || rs == null || rs.IsJailed) { recorders.RemoveAt(i); stateDirty = true; }
        }

        if (stateDirty || Time.unscaledTime >= nextBroadcast)
        {
            stateDirty = false;
            nextBroadcast = Time.unscaledTime + 2f;
            var ids = new List<ulong>();
            foreach (ulong id in nm.ConnectedClientsIds) if (id != nm.LocalClientId) ids.Add(id);
            if (ids.Count > 0)
            {
                using (var w = new FastBufferWriter(1024, Allocator.Temp))
                {
                    w.WriteValueSafe((byte)recorders.Count);
                    foreach (ulong id in recorders) w.WriteValueSafe(id);
                    w.WriteValueSafe(plaqueSeed);
                    w.WriteValueSafe((ushort)readPlaques.Count);
                    foreach (string s in readPlaques) w.WriteValueSafe(s);
                    regCmm.SendNamedMessage(StateMsg, ids, w, NetworkDelivery.Reliable);
                }
            }
        }

        // Tablo başında çok durma (4 kez / sn)
        if (Time.time >= nextStareTick)
        {
            float dt = Time.time - (nextStareTick - 0.25f);
            nextStareTick = Time.time + 0.25f;
            foreach (PlayerStealth p in PlayerStealth.All)
            {
                if (p == null || !p.IsSpawned) continue;
                ArtworkDisplay d = p.IsHidden || p.IsJailed ? null : StaredDisplay(p);
                stare.TryGetValue(p.OwnerClientId, out float t);
                stareAt.TryGetValue(p.OwnerClientId, out ArtworkDisplay last);
                if (d != null && d == last) t += Mathf.Clamp(dt, 0f, 0.5f);
                else if (d != null) t = 0f;
                else t = Mathf.Max(0f, t - dt * 3f);
                stare[p.OwnerClientId] = t;

                // Yasak yerde kayıt süresi
                bool raw = RawIllegal(p);
                illegalTime.TryGetValue(p.OwnerClientId, out float it);
                it = raw ? it + Mathf.Clamp(dt, 0f, 0.5f) : 0f; // kayıt kapanınca / yasak yerden çıkınca baştan sayar
                illegalTime[p.OwnerClientId] = it;
                stareAt[p.OwnerClientId] = d != null ? d : (t > 0f ? last : null);
            }
        }
    }

    /// <summary>Oyuncunun yakından (≤3 m) yüzünü döndüğü eser.</summary>
    private static ArtworkDisplay StaredDisplay(PlayerStealth p)
    {
        Vector3 pos = p.transform.position;
        Vector3 fwd = p.transform.forward;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || !d.IsActiveForMission) continue;
            Vector3 to = d.transform.position - pos;
            to.y = 0f;
            if (to.magnitude > 3f) continue;
            if (Vector3.Angle(fwd, to) > 35f) continue;
            return d;
        }
        return null;
    }

    private static PlayerStealth FindStealth(ulong client)
    {
        foreach (PlayerStealth p in PlayerStealth.All) if (p != null && p.OwnerClientId == client) return p;
        return null;
    }

    /// <summary>Sunucu: bu oyuncu kaydın yasak olduğu yerde kayıt mı yapıyor? (Işığı görülürse şikayet / şüphe.)</summary>
    public static bool ServerIsRecordingIllegally(PlayerStealth p)
    {
        if (!RawIllegal(p) || Instance == null) return false;
        // Kayıt açılır açılmaz değil: yasak yerde IllegalGraceSeconds sn kayıt yaptıktan sonra dikkat çeker
        return Instance.illegalTime.TryGetValue(p.OwnerClientId, out float t) && t >= IllegalGraceSeconds;
    }

    private static bool RawIllegal(PlayerStealth p)
    {
        if (p == null || !IsRecording(p.OwnerClientId) || p.IsHidden || p.IsJailed) return false;
        MuseumLayout l = MuseumLayout.Instance;
        return l != null && l.IsNoPhotoAt(p.transform.position);
    }

    /// <summary>Sunucu: PlayerStealth.SuspiciousLevel'e eklenen pay: yasak yerde kayıt + tablo başında uzun durma.</summary>
    public static float ServerExtraSuspicion(PlayerStealth p)
    {
        if (p == null || Instance == null) return 0f;
        float level = 0f;
        if (ServerIsRecordingIllegally(p)) level += 1f;
        if (Instance.stare.TryGetValue(p.OwnerClientId, out float t))
        {
            if (t > 15f) level += 1.25f;
            else if (t > 8f) level += 0.6f;
        }
        return level;
    }

    // =====================================================================
    //  Soluk etiketler
    // =====================================================================

    /// <summary>Bu eserin isim etiketi soluk mu? (Tohumdan, herkeste aynı; ~%35.)</summary>
    public static bool IsFaded(ArtworkDisplay d)
    {
        MuseumSession ms = MuseumSession.Instance;
        if (d == null || ms == null || !ms.Ready || d.Kind == ArtKind.None) return false;
        int h = ArtworkLook.Hash(d.name) ^ (ms.Seed * 7919);
        return (h & 0x7fffffff) % 100 < 35;
    }

    /// <summary>MuseumSession.ArtworkName: soluk ve henüz kimsenin yakından okumadığı etiket → harflerin çoğu "?".</summary>
    private struct MaskEntry { public int version; public string real, masked; }
    private static readonly Dictionary<ArtworkDisplay, MaskEntry> maskCache = new Dictionary<ArtworkDisplay, MaskEntry>();

    public static string MaskName(ArtworkDisplay d, string real)
    {
        if (string.IsNullOrEmpty(real) || !IsFaded(d) || readPlaques.Contains(d.name)) return real;
        // Önbellek (isim her karede birçok yerden istenir; tekrar tekrar dizi ayırmasın)
        if (maskCache.TryGetValue(d, out MaskEntry e) && e.version == plaqueVersion && e.real == real) return e.masked;
        if (maskCache.Count > 256) maskCache.Clear();
        string m = BuildMask(d, real);
        maskCache[d] = new MaskEntry { version = plaqueVersion, real = real, masked = m };
        return m;
    }

    private static string BuildMask(ArtworkDisplay d, string real)
    {
        var chars = real.ToCharArray();
        int h = ArtworkLook.Hash(d.name);
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsWhiteSpace(chars[i]) || chars[i] == '\'' || chars[i] == '-') continue;
            h = h * 1103515245 + 12345;
            if (((h >> 16) & 0x7fff) % 100 < 60) chars[i] = '?';
        }
        return new string(chars);
    }

    // =====================================================================
    //  Görüntü (her bilgisayar kendi çizer)
    // =====================================================================

    private void EnsureFeed()
    {
        if (feedCam != null) return;
        liveRT = new RenderTexture(640, 360, 16) { name = "BodyCamLive" };
        liveRT.Create();
        GameObject go = new GameObject("BodyCamFeedCamera");
        go.transform.SetParent(transform, false);
        feedCam = go.AddComponent<Camera>();
        feedCam.enabled = false;
        feedCam.targetTexture = liveRT;
        feedCam.fieldOfView = 70f;
        feedCam.nearClipPlane = 0.12f; // kafa avatar gizlemeyle çıkarılır; kamera ayrıca göz önüne alınır
        feedCam.farClipPlane = 80f;
        feedCam.allowMSAA = false;
        feedCam.allowHDR = false;
        feedCam.useOcclusionCulling = true;
        feedCam.farClipPlane = 45f; // müze odaları için yeter; uzak dış mekân çizilmez
        feedCam.depth = -10;
        // URP: ikinci kamera ucuz olsun (gölge haritası, post-process, derinlik / renk kopyası yok)
        UniversalAdditionalCameraData data = feedCam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            data.antialiasing = AntialiasingMode.None;
        }

        noSignal = new Texture2D(64, 36, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color[64 * 36];
        var rng = new System.Random(7);
        for (int i = 0; i < px.Length; i++) { float g = 0.1f + (float)rng.NextDouble() * 0.25f; px[i] = new Color(g, g, g * 1.1f); }
        noSignal.SetPixels(px);
        noSignal.Apply();
    }

    /// <summary>İzlenen kayıt yapanın göz konumu ve bakışı (herkeste senkron veriden).</summary>
    private static bool RecorderEye(out Vector3 pos, out Quaternion rot, out PlayerNetworkSetup who)
        => RecorderEye(WatchedRecorder, out pos, out rot, out who);

    /// <summary>Verilen kayıt yapanın göz konumu ve bakışı.</summary>
    private static bool RecorderEye(ulong client, out Vector3 pos, out Quaternion rot, out PlayerNetworkSetup who)
    {
        pos = Vector3.zero; rot = Quaternion.identity; who = null;
        if (client == Nobody || !recorders.Contains(client)) return false;
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p == null || !p.IsSpawned || p.OwnerClientId != client) continue;
            who = p;
            if (p.IsOwner && Camera.main != null && !WorkbenchUI.IsOpen)
            {
                pos = Camera.main.transform.position;
                rot = Camera.main.transform.rotation;
            }
            else
            {
                pos = p.transform.position + Vector3.up * EyeHeight;
                rot = Quaternion.Euler(Mathf.Clamp(p.LookPitch, -80f, 80f), p.transform.eulerAngles.y, 0f);
            }
            return true;
        }
        return false;
    }

    /// <summary>Canlı görüntü bu bilgisayarda kaç kare/sn çizilmeli? 0 = hiç (kimse bakmıyor).</summary>
    private float LiveFpsNeeded()
    {
        if (SelectedStill != null) return 0f; // bu bilgisayarda bir eserin son kaydı seçili: canlı yayın hiç gösterilmez, çizme
        if (panelOpen || WorkbenchUI.IsOpen || FloorPlanUI.IsOpen) return LiveFps; // M kat planında da görüntü var
        if (monitor == null || screenRenderer == null || !screenRenderer.isVisible) return 0f; // monitör ekranda değil
        Camera cam = Camera.main;
        if (cam == null) return 0f;
        float d = Vector3.Distance(cam.transform.position, monitor.position);
        // Yakında akıcı, uzaktan seyrek (uzaktan ekrandaki fark zaten görünmez); 25 m'den uzaksa hiç çizilmez
        return d < 3.5f ? LiveFps : d < 8f ? 10f : d < 25f ? 4f : 0f;
    }

    private void UpdateFeed()
    {
        // Bir önceki karede çizilen görüntü bir eserin son kaydı olacaksa kopyala
        if (pendingStill != null && pendingSrc != null)
        {
            if (!stills.TryGetValue(pendingStill, out RenderTexture st) || st == null)
            {
                st = new RenderTexture(pendingSrc.width, pendingSrc.height, 0) { name = "BodyCamStill_" + pendingStill };
                st.Create();
                stills[pendingStill] = st;
            }
            Graphics.Blit(pendingSrc, st);
            stillTime[pendingStill] = Time.unscaledTime;
        }
        pendingStill = null;
        pendingSrc = null;

        bool render = false;
        RenderTexture target = liveRT;
        ulong renderFor = Nobody;
        if (prewarmFrames > 0 && feedCam != null && Camera.main != null)
        {
            prewarmFrames--;
            MuseumLayout ml = MuseumLayout.Instance;
            if (prewarmFrames == 0 || ml == null || ml.Plan.Count == 0)
                feedCam.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
            else
            {
                Vector2 c = ml.PlanArea.center;
                feedCam.transform.SetPositionAndRotation(new Vector3(c.x, ml.PlanFloorY + 1.6f, c.y), Quaternion.Euler(8f, prewarmFrames * 90f, 0f));
            }
            render = true;
        }

        ulong watched = WatchedRecorder;
        if (watched != lastWatched)
        {
            // Başka yayına geçildi: eski kişinin son karesi gösterilmesin, hemen yeni kare çizilsin
            lastWatched = watched;
            liveHasFrame = false;
            nextLiveRender = 0f;
        }

        // Kayıt yapanların baktığı eser (0.15 sn'de bir, hepsi için ucuz bir döngü)
        if (Live && Time.unscaledTime >= nextTargetCheck)
        {
            nextTargetCheck = Time.unscaledTime + 0.15f;
            foreach (ulong id in recorders)
            {
                if (!RecorderEye(id, out Vector3 ep, out Quaternion er, out _)) continue;
                ArtworkDisplay d = FeedTarget(ep, er * Vector3.forward);
                if (!looks.TryGetValue(id, out LookTrack lt)) { lt = new LookTrack(); looks[id] = lt; }
                if (d != lt.target) { lt.target = d; lt.since = Time.unscaledTime; }
            }
        }

        // 1) "Son kayıt" zamanı gelen biri var mı? (kayıt yapan bir esere ~0.8 sn baktı; herkes kendi bilgisayarında saklar)
        ulong stillFor = Nobody;
        string stillName = null;
        if (Live)
        {
            foreach (ulong id in recorders)
            {
                if (!looks.TryGetValue(id, out LookTrack lt) || lt.target == null) continue;
                if (Time.unscaledTime - lt.since <= 0.8f) continue;
                string n = lt.target.name;
                if (stillTime.TryGetValue(n, out float t) && Time.unscaledTime - t <= 2.5f) continue;
                if (stillFor == Nobody || id == watched) { stillFor = id; stillName = n; }
                if (id == watched) break;
            }
        }

        if (stillFor != Nobody && RecorderEye(stillFor, out Vector3 sp, out Quaternion sr, out _))
        {
            EnsureFeed();
            feedCam.transform.SetPositionAndRotation(sp + sr * Vector3.forward * FeedForward, sr);
            renderFor = stillFor;
            render = true;
            if (stillFor == watched) { target = liveRT; liveHasFrame = true; }
            else
            {
                // İzlenmeyen kayıt yapan: tek kare ayrı dokuya (canlı yayın bu kare atlanır, sonraki karede devam eder)
                if (captureRT == null) { captureRT = new RenderTexture(640, 360, 16) { name = "BodyCamCapture" }; captureRT.Create(); }
                target = captureRT;
            }
            pendingStill = stillName;
            pendingSrc = target;
            if (stillFor == watched) nextLiveRender = Time.unscaledTime + 1f / LiveFps;
        }
        // 2) Canlı: sadece İZLENEN yayın ve sadece biri bakıyorsa (monitör ekranda / büyük ekran / atölye)
        else if (RecorderEye(watched, out Vector3 pos, out Quaternion rot, out _))
        {
            EnsureFeed();
            float fps = LiveFpsNeeded();
            if (fps > 0f && Time.unscaledTime >= nextLiveRender)
            {
                nextLiveRender = Time.unscaledTime + 1f / fps;
                feedCam.transform.SetPositionAndRotation(pos + rot * Vector3.forward * FeedForward, rot); // göz önüne biraz ileri
                renderFor = watched;
                target = liveRT;
                render = true;
                liveHasFrame = true;
            }
        }

        // Kayıt kapandı / yok: önizleme ve monitör temizlenir (eski görüntü kalmasın)
        if (!Live && (wasLive || liveHasFrame)) ClearFeed();
        wasLive = Live;
        feedFor = renderFor;
        if (feedCam != null)
        {
            if (render && target != null && feedCam.targetTexture != target) feedCam.targetTexture = target;
            if (feedCam.enabled != render) feedCam.enabled = render;
        }
        renderedThisFrame = render;

        ApplyMonitorTexture();
    }

    /// <summary>Canlı doku gösterilebilir mi: kayıt var, doku var ve en az bir kare çizilmiş.</summary>
    private bool LiveShown => Live && liveRT != null && liveHasFrame;

    /// <summary>Yerel olarak seçilmiş "son kayıt" (yoksa null).</summary>
    private RenderTexture SelectedStill => watchStill != null && stills.TryGetValue(watchStill, out RenderTexture st) ? st : null;

    private static bool RecorderEyeExists() => RecorderEye(out _, out _, out _);

    private void ApplyMonitorTexture()
    {
        if (monitorMat == null) return;
        RenderTexture sel = SelectedStill;
        Texture tex = sel != null ? sel : LiveShown ? (Texture)liveRT : noSignal; // monitörde de benim seçtiğim kayıt
        if (monitorMat.mainTexture == tex) return;
        if (monitorMat.HasProperty("_BaseMap")) monitorMat.SetTexture("_BaseMap", tex);
        monitorMat.mainTexture = tex;
    }

    /// <summary>Kayıt bitince / oturum kapanınca: canlı doku silinir, feed kamerası kapanır, monitör "sinyal yok" olur.</summary>
    private void ClearFeed()
    {
        if (feedCam != null && feedCam.enabled) feedCam.enabled = false;
        pendingStill = null;
        pendingSrc = null;
        looks.Clear();
        lookTarget = null;
        if (liveHasFrame && liveRT != null)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = liveRT;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }
        liveHasFrame = false;
        ApplyMonitorTexture();
    }

    // Kayıt kamerası çizilirken kayıt yapanın avatar renderer'larını geçici kapat (herkesin bilgisayarında aynı)
    private void OnBeginFeedCamera(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
    {
        if (cam == null || cam != feedCam || feedFor == Nobody) return;
        RestoreHidden();
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p == null || !p.IsSpawned || p.OwnerClientId != feedFor) continue;
            p.GetComponentsInChildren(true, hideBuf);
            foreach (Renderer r in hideBuf)
                if (r != null && r.enabled) { r.enabled = false; hiddenNow.Add(r); }
            hideBuf.Clear();
            break;
        }
    }

    private void OnEndFeedCamera(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != null && cam == feedCam) RestoreHidden();
    }

    private void RestoreHidden()
    {
        for (int i = 0; i < hiddenNow.Count; i++) if (hiddenNow[i] != null) hiddenNow[i].enabled = true;
        hiddenNow.Clear();
    }

    /// <summary>Kayıt yapanın baktığı eser (≤5 m, ±18°, arada duvar yok).</summary>
    private static ArtworkDisplay FeedTarget(Vector3 eye, Vector3 fwd)
    {
        ArtworkDisplay best = null;
        float bestA = 18f;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || !d.IsActiveForMission) continue;
            Vector3 p = d.transform.position + Vector3.up * (d.WallMounted ? 0f : 0.4f);
            Vector3 to = p - eye;
            if (to.magnitude > 5f) continue;
            float a = Vector3.Angle(fwd, to);
            if (a > bestA) continue;
            if (Physics.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude - 0.35f, ~0, QueryTriggerInteraction.Ignore) &&
                !hit.collider.transform.IsChildOf(d.transform) && hit.collider.GetComponentInParent<PlayerStealth>() == null) continue;
            bestA = a;
            best = d;
        }
        return best;
    }

    /// <summary>Atölye (tablo): orijinal yerine gösterilecek görüntü. Canlı yayın varsa o, yoksa bu eserin son kaydı.</summary>
    private Texture PaintingReference(out string label)
    {
        ArtworkDisplay target = WorkbenchUI.CurrentTarget;
        RenderTexture sel = SelectedStill; // bu bilgisayarda seçilen kayıt (arkadaşın başka kaydı seçebilir)
        if (sel != null)
        {
            label = Loc.T("cam.still.named", StillName(watchStill));
            return sel;
        }
        if (LiveShown)
        {
            PlayerNetworkSetup who = null;
            RecorderEye(out _, out _, out who);
            label = Loc.T("cam.live", who != null ? who.PlayerName : "?");
            return liveRT;
        }
        if (target != null && stills.TryGetValue(target.name, out RenderTexture st) && st != null)
        {
            label = Loc.T("cam.still");
            return st;
        }
        label = Loc.T("cam.nosignal");
        return null;
    }

    // =====================================================================
    //  Kayıt ışığı (herkes görür)
    // =====================================================================

    private readonly List<ulong> recLightRemove = new List<ulong>();

    /// <summary>Her kayıt yapanın göğsünde yanıp sönen kırmızı ışık (havuzdan; kayıt bitince geri döner).</summary>
    private void UpdateRecLight()
    {
        // Artık kayıtta olmayanların ışığı havuza
        recLightRemove.Clear();
        foreach (var kv in recLights) if (!recorders.Contains(kv.Key)) recLightRemove.Add(kv.Key);
        foreach (ulong id in recLightRemove)
        {
            GameObject g = recLights[id];
            recLights.Remove(id);
            if (g == null) continue;
            g.SetActive(false);
            g.transform.SetParent(transform, false);
            recLightPool.Add(g);
        }
        if (recorders.Count == 0) return;

        bool blink = Mathf.Repeat(Time.time, 1f) < 0.6f;
        foreach (ulong id in recorders)
        {
            PlayerNetworkSetup who = null;
            foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
                if (p != null && p.IsSpawned && p.OwnerClientId == id) { who = p; break; }
            recLights.TryGetValue(id, out GameObject light);
            if (who == null) { if (light != null && light.activeSelf) light.SetActive(false); continue; }
            if (light == null)
            {
                if (recLight != null) { light = recLight; recLight = null; }                  // önceden hazırlanmış olan
                else if (recLightPool.Count > 0) { light = recLightPool[recLightPool.Count - 1]; recLightPool.RemoveAt(recLightPool.Count - 1); }
                else light = MakeRecLight();
                recLights[id] = light;
            }
            if (light.transform.parent != who.transform)
            {
                light.transform.SetParent(who.transform, false);
                light.transform.localPosition = new Vector3(0.14f, 0.35f, 0.33f); // göğüs cebinde
            }
            if (!light.activeSelf) light.SetActive(true);
            Renderer r = light.GetComponent<Renderer>();
            if (r != null) r.enabled = blink && !(who.IsOwner && !WorkbenchUI.IsOpen && Camera.main != null);
        }
    }

    private Material recLightMat;

    /// <summary>Kayıt ışığı nesnesi. Gerçek ışık kaynağı YOK: ilk kayıtta sahnedeki bütün shader'lar
    /// yeni ışık için yeniden derlenmesin (donma buydu); parlak kırmızı küre yeterince görünür.</summary>
    private GameObject MakeRecLight()
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "BodyCamRecLight";
        Destroy(g.GetComponent<Collider>());
        Renderer r = g.GetComponent<Renderer>();
        if (recLightMat == null)
        {
            // Işıktan bağımsız parlak kırmızı (monitörle aynı Unlit shader: yeni shader derlenmez)
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            recLightMat = unlit != null ? new Material(unlit) : ArtworkLook.MakeLit(new Color(1f, 0.08f, 0.05f));
            if (unlit != null && recLightMat.HasProperty("_BaseColor")) recLightMat.SetColor("_BaseColor", new Color(1f, 0.1f, 0.06f));
        }
        r.sharedMaterial = recLightMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        g.transform.localScale = Vector3.one * 0.09f;
        g.SetActive(false);
        g.transform.SetParent(transform, false); // kayıt yokken BodyCam altında bekler
        return g;
    }

    // =====================================================================
    //  Kamyon monitörü
    // =====================================================================

    private void EnsureMonitor()
    {
        if (monitor != null) return;
        ForgeryWorkbench bench = ForgeryWorkbench.NearestToLocal(); // birden fazla masa olabilir: yerel oyuncuya en yakını
        if (bench == null) return;
        EnsureFeed();

        Vector3 work = bench.WorkPosition;
        Vector3 away = work - bench.CameraPoint.position;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;

        GameObject root = new GameObject("TruckMonitor");
        root.transform.position = work + away * 0.45f + Vector3.up * 1.05f;
        root.transform.rotation = Quaternion.LookRotation(-away, Vector3.up); // ekran tezgaha baksın
        monitor = root.transform;

        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "MonitorFrame";
        frame.transform.SetParent(root.transform, false);
        frame.transform.localPosition = new Vector3(0f, 0f, -0.03f); // ekranın arkasında
        frame.transform.localScale = new Vector3(1.32f, 0.78f, 0.05f);
        frame.GetComponent<Renderer>().sharedMaterial = ArtworkLook.MakeLit(new Color(0.08f, 0.08f, 0.09f));

        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "MonitorScreen";
        Destroy(screen.GetComponent<Collider>());
        screen.transform.SetParent(root.transform, false);
        screen.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        screen.transform.localScale = new Vector3(1.22f, 0.69f, 1f);
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        monitorMat = unlit != null ? new Material(unlit) : ArtworkLook.MakeLit(Color.white);
        if (monitorMat.HasProperty("_BaseColor")) monitorMat.SetColor("_BaseColor", Color.white);
        screenRenderer = screen.GetComponent<Renderer>();
        screenRenderer.sharedMaterial = monitorMat;
        screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm.name = "MonitorArm";
        Destroy(arm.GetComponent<Collider>());
        arm.transform.SetParent(root.transform, false);
        arm.transform.localPosition = new Vector3(0f, -0.62f, -0.05f);
        arm.transform.localScale = new Vector3(0.08f, 0.5f, 0.08f);
        arm.GetComponent<Renderer>().sharedMaterial = frame.GetComponent<Renderer>().sharedMaterial;

        // Sağında ikinci ekran: MÜZE HARİTASI (monitörün çocuğu: ona bakıp E de büyük ekranı açar)
        mapRT = new RenderTexture(MapW, MapH, 0) { name = "TruckMapRT", filterMode = FilterMode.Bilinear };
        mapRT.Create();
        GameObject mroot = new GameObject("TruckMapScreen");
        mroot.transform.SetParent(root.transform, false);
        mroot.transform.localPosition = new Vector3(-1.42f, 0f, 0f); // tezgahtan bakana göre monitörün sağı
        GameObject mframe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mframe.name = "MapFrame";
        mframe.transform.SetParent(mroot.transform, false);
        mframe.transform.localPosition = new Vector3(0f, 0f, -0.03f);
        mframe.transform.localScale = new Vector3(1.32f, 0.86f, 0.05f);
        mframe.GetComponent<Renderer>().sharedMaterial = frame.GetComponent<Renderer>().sharedMaterial;
        GameObject mscreen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        mscreen.name = "MapScreen";
        Destroy(mscreen.GetComponent<Collider>());
        mscreen.transform.SetParent(mroot.transform, false);
        mscreen.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        mscreen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        mscreen.transform.localScale = new Vector3(1.22f, 1.22f * MapH / MapW, 1f);
        Material mapMat = unlit != null ? new Material(unlit) : ArtworkLook.MakeLit(Color.white);
        if (mapMat.HasProperty("_BaseColor")) mapMat.SetColor("_BaseColor", Color.white);
        if (mapMat.HasProperty("_BaseMap")) mapMat.SetTexture("_BaseMap", mapRT);
        mapMat.mainTexture = mapRT;
        mapRenderer = mscreen.GetComponent<Renderer>();
        mapRenderer.sharedMaterial = mapMat;
        mapRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        GameObject marm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marm.name = "MapArm";
        Destroy(marm.GetComponent<Collider>());
        marm.transform.SetParent(mroot.transform, false);
        marm.transform.localPosition = new Vector3(0f, -0.66f, -0.05f);
        marm.transform.localScale = new Vector3(0.08f, 0.46f, 0.08f);
        marm.GetComponent<Renderer>().sharedMaterial = frame.GetComponent<Renderer>().sharedMaterial;
        nextMapRender = 0f;
    }

    /// <summary>Harita ekranının görüntüsü: kat planı ekran dışı bir dokuya çizilir (OnGUI Repaint içinde).
    /// Sadece ekran kameraya görünürken ve yakındayken, saniyede MapFps kez (optimizasyon).</summary>
    private void RenderMapScreen()
    {
        if (mapRT == null || mapRenderer == null || !mapRenderer.isVisible) return;
        if (Time.unscaledTime < nextMapRender) return;
        Camera cam = Camera.main;
        if (cam == null || Vector3.Distance(cam.transform.position, mapRenderer.transform.position) > 12f) return;
        nextMapRender = Time.unscaledTime + 1f / MapFps;

        RenderTexture prev = RenderTexture.active;
        Matrix4x4 prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        RenderTexture.active = mapRT;
        GL.Clear(true, true, new Color(0.04f, 0.05f, 0.07f, 1f));
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, MapW, MapH, 0f);
        try
        {
            hudStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(14f, 6f, MapW - 28f, 22f), Loc.T("cam.minimap"), hudStyle);
            DrawPlan(new Rect(8f, 30f, MapW - 16f, MapH - 58f), false, false);
            smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(14f, MapH - 24f, MapW - 28f, 18f), Loc.T("cam.minimap.help"), smallStyle);
        }
        finally
        {
            GL.PopMatrix();
            RenderTexture.active = prev;
            GUI.matrix = prevMatrix;
        }
    }

    private bool LookingAtMonitor() => lookingAtMonitor;

    private bool ComputeLookingAtMonitor()
    {
        Camera cam = Camera.main;
        if (monitor == null || cam == null) return false;
        if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore)) return false;
        return hit.collider.transform.IsChildOf(monitor);
    }

    private bool NearMonitor()
    {
        PlayerStealth me = PlayerStealth.Local;
        return monitor != null && me != null && Vector3.Distance(me.transform.position, monitor.position) < 4f;
    }

    private void OpenPanel()
    {
        panelOpen = true;
        cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ClosePanel()
    {
        if (!panelOpen) return;
        panelOpen = false;
        if (cursorWasLocked && !WorkbenchUI.IsOpen && !SessionMenuUI.IsPaused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>Monitör paneli açıkken oyuncu hareketi / etkileşimi dursun.</summary>
    public static bool PanelOpen => Instance != null && Instance.panelOpen;

    // =====================================================================
    //  Arayüz
    // =====================================================================

    private void EnsureStyles()
    {
        if (hudStyle != null) return;
        hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        titleStyle.normal.textColor = Color.white;
        planStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow, wordWrap = false };
    }

    private void OnGUI()
    {
        if (regCmm == null || PlayerStealth.Local == null || SessionMenuUI.IsPaused) return;
        EventType et = Event.current.type;
        if (et != EventType.Repaint && et != EventType.MouseDown) return; // düzen / klavye olaylarında çizim yok (ucuz)
        EnsureStyles();

        if (panelOpen) { DrawPanel(); return; }
        if (WorkbenchUI.IsOpen)
        {
            if (WorkbenchUI.IsCrafting3D) DrawWorkbenchFeed(); // Repaint + MouseDown (kayıt seçimi tıklanabilir)
            return;
        }
        if (et != EventType.Repaint) return;
        PhotoSign.DrawAll();
        DrawHud();
        // Müze haritası artık kamyondaki ikinci ekranda (monitörün sağı); ekranın köşesinde değil
        if (inTruck || nearMonitor) RenderMapScreen();
        if (LookingAtMonitor()) UIUtil.DrawPrompt(Loc.T("cam.monitor.prompt"), UIUtil.CenterSlot(), Color.white);
    }

    /// <summary>Sol alt: kamera pili ve kayıt durumu.</summary>
    private void DrawHud()
    {
        float max = MaxBattery();
        bool rec = LocalRecording;
        // Sol alt köşe: ipucu satırının üstü, sohbetin altı (köşe yığını: çakışmaz)
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.BottomLeft, UIUtil.HudOrder.CamBattery, 200f, 44f);
        UIUtil.Panel(r, rec ? UIUtil.Coral : Color.clear, rec ? 1f : 0.8f);
        bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
        if (rec && blink) UIUtil.DrawRect(new Rect(r.x + 14f, r.y + 10f, 10f, 10f), new Color(1f, 0.15f, 0.1f));
        hudStyle.normal.textColor = rec ? new Color(1f, 0.45f, 0.4f) : UIUtil.Cream;
        string state = rec ? Loc.T("cam.hud.rec") : Live ? Loc.T("cam.hud.other") : Loc.T("cam.hud.off");
        GUI.Label(new Rect(r.x + 30f, r.y + 4f, r.width - 36f, 20f), state, hudStyle);
        float f = max > 0f ? Mathf.Clamp01(battery / max) : 0f;
        UIUtil.Bar(new Rect(r.x + 14f, r.y + 28f, r.width - 26f, 6f), f, f < 0.2f ? UIUtil.Coral : UIUtil.Mint);

        if (rec)
        {
            MuseumLayout l = MuseumLayout.Instance;
            PlayerStealth me = PlayerStealth.Local;
            if (l != null && me != null && l.IsNoPhotoAt(me.transform.position))
            {
                float left = IllegalGraceSeconds - localIllegalTime;
                if (left > 0f)
                    UIUtil.DrawPrompt(Loc.T("cam.illegal.grace", Mathf.CeilToInt(left)), UIUtil.TopSlot(26f), new Color(1f, 0.7f, 0.3f), 17);
                else if (blink)
                    UIUtil.DrawPrompt(Loc.T("cam.illegal"), UIUtil.TopSlot(26f), new Color(1f, 0.35f, 0.3f), 17);
            }
        }
    }

    /// <summary>Atölyede (büst / yüzük / vazo): orijinal masada durmaz, sağ üstte monitör görüntüsü.</summary>
    private void DrawWorkbenchFeed()
    {
        Texture tex = PaintingReference(out string label);
        float w = Mathf.Min(420f, Screen.width * 0.32f), h = w * 9f / 16f;
        Rect r = new Rect(Screen.width - w - 16f, 16f, w, h + 24f);
        UIUtil.Panel(r);
        GUI.Label(new Rect(r.x + 8f, r.y + 2f, w - 16f, 20f), label, hudStyle);
        Rect img = new Rect(r.x + 4f, r.y + 22f, w - 8f, h - 2f);
        if (tex != null) GUI.DrawTexture(img, tex, ScaleMode.ScaleToFit, false);
        else DrawNoSignal(img);

        // Altında kayıt seçimi + müze haritası
        float sh = DrawSourceSelector(new Rect(r.x, r.yMax + 6f, w, 60f));
        float mh = Mathf.Min(w * 0.75f, Screen.height - r.yMax - 26f - sh);
        if (mh > 80f) DrawPlan(new Rect(r.x, r.yMax + 12f + sh, w, mh), false, true);
    }

    /// <summary>Kamyondayken sağ üstte hep açık küçük müze haritası (kamyondaki kişi müzedekini yönetir).</summary>
    private void DrawTruckMinimap()
    {
        float w = Mathf.Min(330f, Screen.width * 0.27f), h = w * 0.78f;
        Rect r = new Rect(Screen.width - w - 12f, 104f, w, h + 40f);
        UIUtil.Panel(r);
        hudStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
        GUI.Label(new Rect(r.x + 8f, r.y + 2f, w - 16f, 20f), Loc.T("cam.minimap"), hudStyle);
        DrawPlan(new Rect(r.x + 4f, r.y + 22f, w - 8f, h), false, true);
        smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        GUI.Label(new Rect(r.x + 8f, r.yMax - 18f, w - 16f, 16f), Loc.T("cam.minimap.help"), smallStyle);
    }

    // ---------------- Kayıt seçimi (yerel) ----------------

    private readonly Dictionary<string, ArtworkDisplay> displayByName = new Dictionary<string, ArtworkDisplay>();
    private readonly List<string> stillOrder = new List<string>();
    private float nextStillOrder;

    /// <summary>Son kaydın gösterilecek adı (eserin müzedeki adı).</summary>
    private string StillName(string objName)
    {
        if (string.IsNullOrEmpty(objName)) return "?";
        if (!displayByName.TryGetValue(objName, out ArtworkDisplay d) || d == null)
        {
            displayByName.Clear();
            foreach (ArtworkDisplay a in ArtworkDisplay.All) if (a != null) displayByName[a.name] = a;
            displayByName.TryGetValue(objName, out d);
        }
        return d != null ? PlanName(d, MuseumSession.Instance) : objName;
    }

    /// <summary>
    /// "Kayıt seç" satırı: canlı yayınlar (kendim hariç) ve eserlerin son kayıtları; tıklayınca bu bilgisayarda o kayıt izlenir
    /// (monitör, büyük ekran ve atölye). Sadece Repaint / MouseDown'da iş yapar. Kullandığı yüksekliği döner.
    /// </summary>
    private float DrawSourceSelector(Rect area)
    {
        Event ev = Event.current;
        EventType et = ev.type;
        if (et != EventType.Repaint && et != EventType.MouseDown) return LastSelectorHeight;
        EnsureStyles();

        // Son kayıtlar: en yeni önce (yarım saniyede bir sıralanır)
        if (Time.unscaledTime >= nextStillOrder)
        {
            nextStillOrder = Time.unscaledTime + 0.5f;
            stillOrder.Clear();
            foreach (string k in stills.Keys) stillOrder.Add(k);
            stillOrder.Sort((a, b) => (stillTime.TryGetValue(b, out float tb) ? tb : 0f).CompareTo(stillTime.TryGetValue(a, out float ta) ? ta : 0f));
        }
        if (watchStill != null && !stills.ContainsKey(watchStill)) watchStill = null;

        NetworkManager nm = NetworkManager.Singleton;
        ulong me = nm != null ? nm.LocalClientId : Nobody;
        ulong watched = WatchedRecorder;
        const float bh = 24f, gap = 6f;
        float x = area.x, y = area.y;
        smallStyle.alignment = TextAnchor.MiddleCenter;

        hudStyle.normal.textColor = UIUtil.Gold;
        string title = Loc.T("cam.src.title");
        float tw = hudStyle.CalcSize(new GUIContent(title)).x + 8f;
        if (et == EventType.Repaint) GUI.Label(new Rect(x, y, tw, bh), title, hudStyle);
        x += tw;

        int shown = 0;
        foreach (ulong id in recorders)
        {
            if (id == me) continue;
            PlayerNetworkSetup who = null;
            foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All) if (p != null && p.OwnerClientId == id) { who = p; break; }
            string text = Loc.T("cam.src.live", who != null ? who.PlayerName : "?");
            bool sel = watchStill == null && watched == id;
            if (SourceButton(ref x, ref y, area, bh, gap, text, sel, new Color(1f, 0.35f, 0.3f))) WatchLive(id);
            shown++;
        }
        foreach (string n in stillOrder)
        {
            bool sel = watchStill == n;
            if (SourceButton(ref x, ref y, area, bh, gap, StillName(n), sel, UIUtil.Cream)) WatchStill(sel ? null : n);
            shown++;
        }
        if (shown == 0 && et == EventType.Repaint)
        {
            smallStyle.alignment = TextAnchor.MiddleLeft;
            smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(x, y, area.xMax - x, bh), Loc.T("cam.src.none"), smallStyle);
        }
        smallStyle.alignment = TextAnchor.MiddleLeft;
        LastSelectorHeight = y + bh - area.y;
        return LastSelectorHeight;
    }

    private float LastSelectorHeight = 24f;

    /// <summary>Seçim düğmesi (kendi çizimi: OnGUI düzen olaylarına bağlı değil). Tıklandıysa true.</summary>
    private bool SourceButton(ref float x, ref float y, Rect area, float bh, float gap, string text, bool selected, Color textColor)
    {
        float w = Mathf.Min(area.width, smallStyle.CalcSize(new GUIContent(text)).x + 20f);
        if (x + w > area.xMax && x > area.x) { x = area.x; y += bh + 4f; } // satır sonu: alta geç
        Rect b = new Rect(x, y, w, bh);
        x += w + gap;
        Event ev = Event.current;
        if (ev.type == EventType.Repaint)
        {
            if (selected)
            {
                UIUtil.DrawRect(b, new Color(0.86f, 0.62f, 0.25f, 1f));
                smallStyle.normal.textColor = new Color(0.14f, 0.08f, 0.06f);
            }
            else
            {
                bool hover = b.Contains(ev.mousePosition);
                UIUtil.DrawRect(b, UIUtil.Frame);
                UIUtil.DrawRect(new Rect(b.x + 1f, b.y + 1f, b.width - 2f, b.height - 2f), hover ? new Color(0.3f, 0.12f, 0.15f, 0.97f) : new Color(0.1f, 0.07f, 0.08f, 0.95f));
                smallStyle.normal.textColor = textColor;
            }
            GUI.Label(b, text, smallStyle);
            return false;
        }
        if (ev.type == EventType.MouseDown && ev.button == 0 && b.Contains(ev.mousePosition))
        {
            ev.Use();
            GameAudio.Play2D(SoundId.UiClick, 0.5f);
            return true;
        }
        return false;
    }

    /// <summary>Atölyede tuvalin altı (CanvasPainter çağırır): tıklanabilir müze haritası.</summary>
    private void DrawCanvasPlan(Rect r)
    {
        EventType et = Event.current.type;
        if (et != EventType.Repaint && et != EventType.MouseDown) return;
        EnsureStyles();
        if (et == EventType.Repaint)
        {
            UIUtil.Panel(r);
            hudStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 18f), Loc.T("cam.minimap") + "   —   " + Loc.T("cam.minimap.click"), hudStyle);
        }
        // Hangi kaydı izlediğimi buradan seçerim (iki kişi aynı masada farklı eserleri çizebilir)
        float sh = DrawSourceSelector(new Rect(r.x + 10f, r.y + 24f, r.width - 20f, 60f));
        DrawPlan(new Rect(r.x + 4f, r.y + 28f + sh, r.width - 8f, r.height - 32f - sh), true, true);
    }

    /// <summary>Haritaya tıklandı: o noktaya (ya da yakındaki esere) ekibin gördüğü işaret.</summary>
    private void PingFromMap(Vector2 world)
    {
        if (ping == null && Time.unscaledTime >= nextPingFind)
        {
            nextPingFind = Time.unscaledTime + 2f;
            ping = FindFirstObjectByType<PingSystem>();
        }
        if (ping == null) return;
        MuseumLayout l = MuseumLayout.Instance;
        ArtworkDisplay best = null;
        float bestD = 1.6f;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Kind == ArtKind.None || !d.IsActiveForMission) continue;
            Vector3 p = d.transform.position;
            float dd = Vector2.Distance(world, new Vector2(p.x, p.z));
            if (dd < bestD) { bestD = dd; best = d; }
        }
        int item = -1;
        Vector3 at;
        if (best != null)
        {
            at = best.transform.position + Vector3.up * (best.WallMounted ? 0f : 0.6f);
            if (ItemDatabase.Instance != null && best.Artwork != null) item = ItemDatabase.Instance.IndexOf(best.Artwork);
        }
        else at = new Vector3(world.x, (l != null ? l.PlanFloorY : 0f) + 0.05f, world.y);
        if (ping.RequestPingAt(at, item)) GameAudio.Play2D(SoundId.Ping, 0.4f);
    }

    private string PlanName(ArtworkDisplay d, MuseumSession ms)
    {
        if (Time.unscaledTime >= nameCacheTime) { nameCache.Clear(); nameCacheTime = Time.unscaledTime + 0.5f; }
        if (!nameCache.TryGetValue(d, out string n))
        {
            n = ms != null ? ms.ArtworkName(d) : d.name;
            nameCache[d] = n;
        }
        return n;
    }

    private void DrawNoSignal(Rect r)
    {
        if (noSignal != null) GUI.DrawTexture(r, noSignal, ScaleMode.StretchToFill, false);
        smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        smallStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(r, Loc.T("cam.nosignal.help"), smallStyle);
        smallStyle.alignment = TextAnchor.MiddleLeft;
    }

    /// <summary>Monitör paneli: solda canlı görüntü, sağda kat planı.</summary>
    private void DrawPanel()
    {
        UIUtil.DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
        float pw = Mathf.Min(Screen.width - 40f, 1200f), ph = Mathf.Min(Screen.height - 60f, 560f);
        Rect p = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
        UIUtil.Panel(p);
        GUI.Label(new Rect(p.x + 14f, p.y + 6f, pw - 28f, 24f), Loc.T("cam.panel.title"), titleStyle);

        // Sol: görüntü
        float fw = pw * 0.52f, fh = fw * 9f / 16f;
        Rect feed = new Rect(p.x + 14f, p.y + 40f, fw, fh);
        UIUtil.DrawRect(new Rect(feed.x - 2f, feed.y - 2f, feed.width + 4f, feed.height + 4f), new Color(0.2f, 0.2f, 0.22f));
        RenderTexture selStill = SelectedStill;
        if (selStill != null)
        {
            GUI.DrawTexture(feed, selStill, ScaleMode.ScaleToFit, false);
            hudStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(feed.x + 10f, feed.y + 6f, feed.width - 20f, 20f), Loc.T("cam.still.named", StillName(watchStill)), hudStyle);
        }
        else if (LiveShown)
        {
            GUI.DrawTexture(feed, liveRT, ScaleMode.ScaleToFit, false);
            RecorderEye(out _, out _, out PlayerNetworkSetup who);
            if (Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f) UIUtil.DrawRect(new Rect(feed.x + 10f, feed.y + 10f, 12f, 12f), new Color(1f, 0.1f, 0.1f));
            hudStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(feed.x + 28f, feed.y + 6f, feed.width - 40f, 20f), Loc.T("cam.live", who != null ? who.PlayerName : "?"), hudStyle);
        }
        else DrawNoSignal(feed);
        // Kayıt seçimi: canlı yayınlar + eserlerin son kayıtları (seçim sadece bu bilgisayarda)
        float sh = DrawSourceSelector(new Rect(feed.x, feed.yMax + 8f, feed.width, 80f));
        smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
        smallStyle.wordWrap = true;
        GUI.Label(new Rect(feed.x, feed.yMax + 14f + sh, feed.width, 60f), Loc.T("cam.panel.help"), smallStyle);
        smallStyle.wordWrap = false;

        // Sağ: kat planı
        Rect map = new Rect(feed.xMax + 16f, p.y + 40f, p.xMax - feed.xMax - 30f, ph - 70f);
        DrawPlan(map, true, false);
        GUI.Label(new Rect(p.x + 14f, p.yMax - 24f, pw - 28f, 20f), Loc.T("cam.panel.close"), smallStyle);
    }

    /// <param name="clickable">Tıklanınca işaret koyar (fare serbestken: büyük ekran, atölye).</param>
    /// <param name="compact">Küçük harita: yazılar küçük, oda etiketleri sadece önemli odalarda.</param>
    private void DrawPlan(Rect area, bool clickable, bool compact)
    {
        MuseumLayout l = MuseumLayout.Instance;
        if (l == null || l.Plan.Count == 0) { if (Event.current.type == EventType.Repaint) UIUtil.DrawRect(area, new Color(0.1f, 0.11f, 0.13f)); return; }
        Rect b = l.PlanArea;
        if (b.width < 1f || b.height < 1f) return;
        float pad = compact ? 8f : 20f;
        float scale = Mathf.Min((area.width - pad) / b.width, (area.height - pad) / b.height);
        Vector2 origin = new Vector2(area.x + (area.width - b.width * scale) * 0.5f, area.y + (area.height - b.height * scale) * 0.5f);
        Func<Vector2, Vector2> ToScreen = w => new Vector2(origin.x + (w.x - b.xMin) * scale, origin.y + (b.yMax - w.y) * scale); // kuzey yukarıda

        // Tıklama: haritadaki noktaya işaret
        Event ev = Event.current;
        if (ev.type == EventType.MouseDown)
        {
            if (clickable && ev.button == 0 && area.Contains(ev.mousePosition))
            {
                Vector2 m = ev.mousePosition;
                PingFromMap(new Vector2(b.xMin + (m.x - origin.x) / scale, b.yMax - (m.y - origin.y) / scale));
                ev.Use();
            }
            return;
        }
        if (ev.type != EventType.Repaint) return;
        UIUtil.DrawRect(area, new Color(0.1f, 0.11f, 0.13f));
        planStyle.fontSize = compact ? 9 : 10;

        foreach (MuseumLayout.PlanRoom r in l.Plan)
        {
            Vector2 a = ToScreen(new Vector2(r.rect.xMin, r.rect.yMax));
            Rect rr = new Rect(a.x, a.y, r.rect.width * scale, r.rect.height * scale);
            Color fill = r.role == "staff" ? new Color(0.45f, 0.12f, 0.12f) : r.role == "security" ? new Color(0.45f, 0.38f, 0.1f)
                       : r.role == "lobby" ? new Color(0.2f, 0.24f, 0.3f) : r.noPhoto ? new Color(0.3f, 0.16f, 0.18f) : new Color(0.17f, 0.25f, 0.2f);
            UIUtil.DrawRect(rr, new Color(0.75f, 0.75f, 0.8f));
            UIUtil.DrawRect(new Rect(rr.x + 2f, rr.y + 2f, rr.width - 4f, rr.height - 4f), fill);
            planStyle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            string tag = r.role == "staff" ? Loc.T("cam.plan.staff") : r.role == "security" ? Loc.T("cam.plan.security")
                       : r.role == "lobby" ? Loc.T("cam.plan.lobby") : r.noPhoto ? Loc.T("cam.plan.nophoto") : "";
            if (compact && r.role != "staff" && r.role != "security") tag = "";
            if (tag.Length > 0) GUI.Label(new Rect(rr.x, rr.y + 2f, rr.width, 14f), tag, planStyle);
        }
        foreach (Vector2 d in l.PlanDoorPoints)
        {
            Vector2 s = ToScreen(d);
            UIUtil.DrawRect(new Rect(s.x - 4f, s.y - 4f, 8f, 8f), new Color(0.1f, 0.11f, 0.13f));
        }

        // Eserler
        MuseumSession ms = MuseumSession.Instance;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Kind == ArtKind.None || !d.IsActiveForMission) continue;
            Vector3 w = d.transform.position;
            Vector2 s = ToScreen(new Vector2(w.x, w.z));
            Color c = d.IsSwapped ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.82f, 0.3f);
            UIUtil.DrawRect(new Rect(s.x - 4f, s.y - 4f, 8f, 8f), c);
            planStyle.normal.textColor = c;
            GUI.Label(new Rect(s.x - 70f, s.y + 4f, 140f, 14f), PlanName(d, ms), planStyle);
        }

        // Hapishane hücresi (ikon + etiket)
        if (JailCell.TryGetPlanPosition(out Vector3 jailW))
        {
            Vector2 js = ToScreen(new Vector2(jailW.x, jailW.z));
            TextAnchor oldAl = planStyle.alignment;
            planStyle.alignment = TextAnchor.UpperCenter;
            FloorPlanUI.DrawJailMarker(js, area, planStyle);
            planStyle.alignment = oldAl;
        }

        // Takım arkadaşları
        foreach (PlayerNetworkSetup pl in PlayerNetworkSetup.All)
        {
            if (pl == null || !pl.IsSpawned) continue;
            PlayerStealth st = pl.GetComponent<PlayerStealth>();
            if (st != null && st.IsJailed) continue;
            Vector3 w = pl.transform.position;
            if (!b.Contains(new Vector2(w.x, w.z))) continue;
            Vector2 s = ToScreen(new Vector2(w.x, w.z));
            bool rec = IsRecording(pl.OwnerClientId);
            UIUtil.DrawRect(new Rect(s.x - 5f, s.y - 5f, 10f, 10f), Color.black);
            UIUtil.DrawRect(new Rect(s.x - 4f, s.y - 4f, 8f, 8f), pl.PlayerColor);
            Vector3 f = pl.transform.forward;
            Vector2 tip = s + new Vector2(f.x, -f.z) * 12f;
            UIUtil.DrawRect(new Rect(tip.x - 2f, tip.y - 2f, 4f, 4f), pl.PlayerColor);
            planStyle.normal.textColor = rec ? new Color(1f, 0.35f, 0.3f) : Color.white;
            GUI.Label(new Rect(s.x - 70f, s.y - 18f, 140f, 14f), (rec ? "● " : "") + pl.PlayerName, planStyle);
        }
    }
}
