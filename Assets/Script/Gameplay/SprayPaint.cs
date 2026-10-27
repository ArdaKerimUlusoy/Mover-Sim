using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// BOYA ETKİLERİ — ressamların silahı (boya garajdan kutu kutu alınır; her iş bir kutu). Boyamayı FIRÇA yapar (PaintBrush.cs):
///  - Fırçayla fırlatılan BOYA TOPU kamera lensine isabet ederse → kamera BlindSeconds sn kör (PaintProjectile; isabet sunucuda).
///  - Bir DUVARA fırçayla kapı çiz → duvarda sahte bir kapı / gölge belirir. Önünde (≈0.75 m) duran
///    oyuncuyu 3 m'den uzaktaki güvenlik, ziyaretçi ve kamera görmez (yakına gelen görür). Görev boyunca kalır.
///  - Boyalı kapıya bakıp E → eski dolaplar gibi İÇİNE SAKLANIRSIN (kimse görmez; güvenlik ararken açıp bakabilir).
///    Dolaplar kaldırıldı; sahnedeki saklanma noktaları (HidingSpot) görünmez bekler, boyanan kapılara atanır.
///  - Boyarken suç işliyorsun: biri görürse şüphelenir. Boyanırken hafif "pss" sesi çıkar.
/// Ağ: "SS_SprayReq" (istemci → sunucu: kapı çizimi / boya topu, kutu kontrolü), "SS_SprayFx" (sunucu → herkes: görsel + etki).
/// Sahneye bir şey eklemeye gerek yok.
/// </summary>
public class SprayPaint : MonoBehaviour
{
    public static SprayPaint Instance { get; private set; }

    /// <summary>Yerel oyuncu şu an boya sıkıyor mu? (PlayerInteractor suç bayrağına ekler.)</summary>
    public static bool IsSpraying => spraying || Time.time < throwCrimeUntil;
    private static bool spraying;
    private static float throwCrimeUntil;   // boya topu fırlatmak da suç: atıştan sonra kısa süre "boyuyor" sayılır
    private const float ThrowCrimeSeconds = 2.5f;

    private const string ReqMsg = "SS_SprayReq", FxMsg = "SS_SprayFx";
    public const float BlindSeconds = 45f;
    private const float HoldSeconds = 1f;
    private const float CamRange = 9f, CamAngle = 7f, WallRange = 3.2f;
    private const float ShadowWidth = 1.1f, ShadowHeight = 2.0f;
    private const float ConcealRadius = 0.75f, ConcealDepth = 0.95f, SeeThroughDistance = 3f;
    private const byte KindCamera = 1, KindShadow = 2, KindRemove = 3, KindThrow = 4;
    private readonly Dictionary<ulong, float> serverNextThrow = new Dictionary<ulong, float>(); // sunucu: oyuncu başına bekleme
    /// <summary>Boyalı kapı tek kullanımlık: içinden çıkıldıktan bu kadar sn sonra duvardan silinir.</summary>
    public const float DoorVanishSeconds = 4.5f;
    private readonly Dictionary<ulong, bool> doorWasOccupied = new Dictionary<ulong, bool>();
    private readonly Dictionary<ulong, float> doorRemoveAt = new Dictionary<ulong, float>();
    private readonly List<ulong> doorTmp = new List<ulong>();

    private struct Shadow { public Vector3 pos; public Vector3 normal; public Transform visual; public HidingSpot spot; public float createdAt; }
    /// <summary>İçine hiç girilmeyen boyalı kapı bu kadar sn sonra kendiliğinden silinir.</summary>
    public const float UnusedDoorSeconds = 5f;
    private const ulong NoSpot = ulong.MaxValue;

    private static readonly Dictionary<string, float> blinded = new Dictionary<string, float>();
    private static readonly List<Shadow> shadows = new List<Shadow>();

    private CustomMessagingManager regCmm;
    private NetworkManager regNm;
    private int layoutVersion = -1;
    private Transform fxRoot;

    // Yerel
    private enum Aim { None, Camera, Wall }
    private Aim aim;
    private RoomCamera aimCam;
    private Vector3 aimPos, aimNormal;
    private float progress;
    private bool waitRelease;
    private float nextHiss;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("SprayPaint");
        DontDestroyOnLoad(go);
        go.AddComponent<SprayPaint>();
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        Unregister();
        if (Instance == this) Instance = null;
        spraying = false;
    }

    // =====================================================================
    //  Etkiler (herkes)
    // =====================================================================

    /// <summary>Bu oda kamerası boyandı mı (kör)?</summary>
    public static bool IsBlinded(RoomCamera c) => c != null && blinded.TryGetValue(c.name, out float until) && Time.time < until;

    /// <summary>Hedef, boyanmış bir gölgenin önünde mi ve bakan yeterince uzakta mı? (Vision.CanSee kullanır.)</summary>
    public static bool Conceals(PlayerStealth target, Vector3 eye)
    {
        if (target == null || shadows.Count == 0) return false;
        Vector3 p = target.transform.position;
        foreach (Shadow s in shadows)
        {
            Vector3 d = p - s.pos;
            float depth = Vector3.Dot(d, s.normal);              // duvardan ne kadar önde
            if (depth < -0.2f || depth > ConcealDepth) continue;
            Vector3 lateral = d - s.normal * depth;
            lateral.y = 0f;
            if (lateral.magnitude > ConcealRadius) continue;
            Vector3 toEye = eye - p;
            toEye.y = 0f;
            if (toEye.magnitude < SeeThroughDistance) return false; // yakından fark edilir
            return true;
        }
        return false;
    }

    /// <summary>Yerel oyuncu bir gölgenin önünde gizli mi? (Ekran ipucu için.)</summary>
    public static bool LocalConcealed()
    {
        PlayerStealth me = PlayerStealth.Local;
        return me != null && Conceals(me, me.transform.position + new Vector3(SeeThroughDistance + 1f, 0f, 0f));
    }

    // =====================================================================
    //  Döngü
    // =====================================================================

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool active = nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient) && nm.CustomMessagingManager != null;
        if (!active)
        {
            if (regCmm != null) Unregister();
            ClearAll();
            spraying = false;
            return;
        }
        if (regCmm != nm.CustomMessagingManager) Register(nm);

        // Yeni harita: boyalar temizlenir (kameralar zaten yeniden kurulur)
        MuseumLayout l = MuseumLayout.Instance;
        int v = l != null ? l.Version : -1;
        if (v != layoutVersion) { layoutVersion = v; ClearAll(); }

        if (nm.IsServer) ServerTickDoors();
        ExpireSpotlessDoors();
        LocalTick(nm);
    }

    /// <summary>Sunucu: biri kapıdan çıkınca DoorVanishSeconds sn sonra kapıyı herkeste sil (tek kullanımlık).</summary>
    private void ServerTickDoors()
    {
        foreach (Shadow s in shadows)
        {
            if (s.spot == null || !s.spot.IsSpawned) continue;
            ulong id = s.spot.NetworkObjectId;
            bool occ = s.spot.IsOccupied;
            // Yeni kapı: içine girilmezse UnusedDoorSeconds sn sonra silinir
            if (!doorWasOccupied.ContainsKey(id) && !occ && !doorRemoveAt.ContainsKey(id))
                doorRemoveAt[id] = s.createdAt + UnusedDoorSeconds;
            doorWasOccupied.TryGetValue(id, out bool was);
            if (occ) doorRemoveAt.Remove(id);                                         // içeride biri var: silinmez
            else if (was && !doorRemoveAt.ContainsKey(id)) doorRemoveAt[id] = Time.time + DoorVanishSeconds; // az önce çıkıldı
            doorWasOccupied[id] = occ;
            // HidingSpot'un kendi süresi bizim zamanlayıcıyla aynı kalsın (E ipucundaki "kalan sn" doğru görünür)
            if (occ) { if (s.spot.PaintRemaining < 300f) s.spot.ServerSetRemaining(600f); }
            else if (doorRemoveAt.TryGetValue(id, out float removeAt)) s.spot.ServerSetRemaining(removeAt - Time.time + 0.3f);
        }
        if (doorRemoveAt.Count == 0) return;
        doorTmp.Clear();
        foreach (KeyValuePair<ulong, float> kv in doorRemoveAt) if (Time.time >= kv.Value) doorTmp.Add(kv.Key);
        foreach (ulong id in doorTmp)
        {
            doorRemoveAt.Remove(id);
            doorWasOccupied.Remove(id);
            BroadcastRemove(id);
            RemoveDoor(id);
        }
    }

    /// <summary>Herkes: saklanma noktası atanamamış (içine girilemeyen) kapılar UnusedDoorSeconds sn sonra silinir.</summary>
    private void ExpireSpotlessDoors()
    {
        for (int i = shadows.Count - 1; i >= 0; i--)
        {
            Shadow s = shadows[i];
            if (s.spot != null || Time.time - s.createdAt < UnusedDoorSeconds) continue;
            if (s.visual != null) Destroy(s.visual.gameObject);
            shadows.RemoveAt(i);
        }
    }

    private void BroadcastRemove(ulong spotId)
    {
        List<ulong> ids = Others();
        if (ids.Count == 0) return;
        using (var w = new FastBufferWriter(16, Allocator.Temp))
        {
            w.WriteValueSafe(KindRemove);
            w.WriteValueSafe(spotId);
            regCmm.SendNamedMessage(FxMsg, ids, w, NetworkDelivery.Reliable);
        }
    }

    /// <summary>Herkes: bu saklanma noktasına bağlı boyalı kapıyı duvardan sil (çizgiler kaybolur, nokta yeniden boşa çıkar).</summary>
    private void RemoveDoor(ulong spotId)
    {
        for (int i = shadows.Count - 1; i >= 0; i--)
        {
            Shadow s = shadows[i];
            if (s.spot == null || s.spot.NetworkObjectId != spotId) continue;
            if (s.visual != null) Destroy(s.visual.gameObject);
            if (s.spot.IsSpawned && s.spot.IsServer && !s.spot.IsOccupied) s.spot.ServerErase(); // kapı yuvası boşa çıkar
            GameAudio.PlayAt(SoundId.CoinThrow, s.pos + Vector3.up, 0.3f, 0.2f);
            shadows.RemoveAt(i);
        }
    }

    private void ClearAll()
    {
        blinded.Clear();
        doorWasOccupied.Clear();
        doorRemoveAt.Clear();
        foreach (Shadow s in shadows)
        {
            if (s.visual != null) Destroy(s.visual.gameObject);
            if (s.spot != null && s.spot.IsSpawned && s.spot.IsServer) s.spot.ServerErase();
        }
        shadows.Clear();
    }

    private void LocalTick(NetworkManager nm)
    {
        // Boyama artık FIRÇA ile (PaintBrush.cs): çantadaki fırçayı eline al, duvara kapıyı çiz / lensi boya.
        // Bu sınıf boyanın ETKİLERİNİ (kör kamera, sahte kapı, saklanma) ve ağ isteklerini yönetir.
        aim = Aim.None;
    }

    /// <summary>Yerel: fırça çiziyor / boyuyor (suç bayrağı — biri görürse şüphelenir).</summary>
    public static void SetLocalPainting(bool on) => spraying = on;

    /// <summary>Yerel: boya topu fırlat (görsel hemen yerelde; isabet sunucuda belirlenir). Boya yoksa/menzil dışıysa sunucu yok sayar.</summary>
    public void RequestThrow(Vector3 origin, Vector3 dir)
    {
        if (regNm == null || regCmm == null) return;
        dir.Normalize();
        byte color = (byte)LocalColor;
        PlayerStealth me = PlayerStealth.Local;
        PaintProjectile.Launch(origin, dir * PaintProjectile.Speed, color, me != null ? me.transform : null);
        throwCrimeUntil = Time.time + ThrowCrimeSeconds;
        if (regNm.IsServer) { ServerThrow(regNm.LocalClientId, origin, dir, color); return; }
        using (var w = new FastBufferWriter(64, Allocator.Temp))
        {
            w.WriteValueSafe(KindThrow);
            w.WriteValueSafe(origin);
            w.WriteValueSafe(dir);
            w.WriteValueSafe(color);
            regCmm.SendNamedMessage(ReqMsg, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
    }

    /// <summary>Yerel: çizilen kapıyı sunucuya gönder (boya kutusu harcanır, kapı herkeste belirir).</summary>
    public void RequestWall(Vector3 basePos, Vector3 normal, List<Vector2[]> drawn = null)
    {
        if (regNm != null && regCmm != null) SendWall(regNm, basePos, normal, drawn, (byte)LocalColor);
    }

    // ---------------- Çizilen kapının çizgileri (ağ) ----------------
    // Kapı, oyuncunun ÇİZDİĞİ haliyle kalır: çizgiler kapının dibine göre (u = yatay, v = yükseklik) cm cinsinden gönderilir.
    private const int MaxStrokes = 24, MaxPoints = 360;

    private static void WriteStrokes(ref FastBufferWriter w, List<Vector2[]> s)
    {
        int count = s != null ? Mathf.Min(s.Count, MaxStrokes) : 0;
        w.WriteValueSafe((byte)count);
        for (int i = 0; i < count; i++)
        {
            Vector2[] st = s[i];
            int n = Mathf.Min(st.Length, 255);
            w.WriteValueSafe((byte)n);
            for (int k = 0; k < n; k++)
            {
                w.WriteValueSafe((short)Mathf.Clamp(Mathf.RoundToInt(st[k].x * 100f), -300, 300));
                w.WriteValueSafe((short)Mathf.Clamp(Mathf.RoundToInt(st[k].y * 100f), -50, 400));
            }
        }
    }

    private static List<Vector2[]> ReadStrokes(ref FastBufferReader r)
    {
        var list = new List<Vector2[]>();
        r.ReadValueSafe(out byte count);
        int total = 0;
        for (int i = 0; i < count && i < MaxStrokes; i++)
        {
            r.ReadValueSafe(out byte n);
            var st = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                r.ReadValueSafe(out short u);
                r.ReadValueSafe(out short v);
                st[k] = new Vector2(Mathf.Clamp(u, -300, 300) / 100f, Mathf.Clamp(v, -50, 400) / 100f);
            }
            total += n;
            if (n >= 2 && total <= MaxPoints) list.Add(st);
        }
        return list;
    }

    private static int StrokeBytes(List<Vector2[]> s)
    {
        int b = 1;
        if (s != null) foreach (Vector2[] st in s) b += 1 + Mathf.Min(st.Length, 255) * 4;
        return b;
    }

    /// <summary>Bu kamera boyanabilir mi (görevde, henüz kör değil)?</summary>
    public static bool CanPaintCamera(RoomCamera rc) => rc != null && !IsBlinded(rc) && !rc.Blinded && MuseumSession.IsLevelActive(rc.requiredLevel);

    /// <summary>Burada zaten boyalı bir kapı var mı? (Üst üste çizilmesin.)</summary>
    public static bool NearExistingDoor(Vector3 basePos)
    {
        foreach (Shadow s in shadows) if (Vector3.Distance(s.pos, basePos) < 1.5f) return true;
        return false;
    }

    /// <summary>Bakılan yer kapı çizilebilecek gerçek bir duvar mı? hit = fırçanın değdiği nokta, basePos = duvarın dibi.</summary>
    public static bool WallCandidate(Vector3 eye, Vector3 fwd, float range, PlayerStealth me, out Vector3 hitPoint, out Vector3 basePos, out Vector3 normal)
    {
        hitPoint = basePos = normal = Vector3.zero;
        // Kendi gövdeni yok say, ilk çarpılan şey dik bir yüzey olmalı. Müzenin DIŞ cephelerindeki kaplamalar (çarpıştırıcısız
        // süs kutuları, duvarın önünde durur) da sayılır: çizgi görünen yüzeye çizilir, arkadaki gizli duvara değil.
        if (!RaySurface(eye, fwd, range, me, out Vector3 point, out Vector3 hn, out Collider col)) return false;
        if (Mathf.Abs(hn.y) > 0.35f) return false;
        if (col != null && (col.GetComponentInParent<PlayerStealth>() != null || col.GetComponentInParent<GuardAI>() != null ||
            col.GetComponentInParent<Interactable>() != null || col.GetComponentInParent<ArtworkDisplay>() != null)) return false;
        Vector3 n = new Vector3(hn.x, 0f, hn.z).normalized;
        Vector3 b = point;
        if (Physics.Raycast(point + n * 0.3f + Vector3.up * 0.2f, Vector3.down, out RaycastHit floor, 3.5f, ~0, QueryTriggerInteraction.Ignore))
            b.y = floor.point.y;
        else b.y = (me != null ? me.transform.position.y : point.y) - 1f;
        // Gerçek bir duvar mı? (kaide / masa değil): dipten ve boy hizasından aynı yüzeye ışın at
        if (!WallAt(b + Vector3.up * 0.35f, n, me) || !WallAt(b + Vector3.up * 1.9f, n, me)) return false;
        hitPoint = point;
        basePos = b;
        normal = n;
        return true;
    }

    /// <summary>Kendi oyuncunun çarpıştırıcılarını ve tetikleyicileri atlayarak ilk çarpılan şey.</summary>
    private static readonly RaycastHit[] hitBuf = new RaycastHit[16];
    private static bool FirstHit(Vector3 from, Vector3 dir, float range, PlayerStealth me, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(from, dir, hitBuf, range, ~0, QueryTriggerInteraction.Ignore);
        float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hitBuf[i];
            if (h.distance >= bestD) continue;
            if (me != null && h.collider.transform.IsChildOf(me.transform)) continue;
            bestD = h.distance;
            best = h;
        }
        return bestD < float.MaxValue;
    }

    /// <summary>Bu yükseklikte duvar yüzeyi var mı (önünden geriye doğru kısa ışın, yüzey dik).</summary>
    private static bool WallAt(Vector3 p, Vector3 normal, PlayerStealth me)
    {
        if (!RaySurface(p + normal * 0.45f, -normal, 0.9f, me, out _, out Vector3 n, out Collider col)) return false;
        return Mathf.Abs(n.y) < 0.35f && (col == null || col.GetComponentInParent<PlayerStealth>() == null);
    }

    // ---------------- Görünen yüzeyler (çarpıştırıcısız cephe kaplamaları) ----------------
    // Dış cephelerin çoğu çarpıştırıcısız süs kutusu: fizik ışını onlardan geçip arkadaki duvara çarpar.
    // Bu yüzden müze düzenindeki büyük, dik, eksene hizalı süs kutularının sınırları ayrıca taranır (düzen değişince yenilenir).
    private static readonly List<Bounds> visualSurfaces = new List<Bounds>();
    private static int visualVersion = int.MinValue;

    private static void EnsureVisualSurfaces()
    {
        MuseumLayout l = MuseumLayout.Instance;
        int v = l != null ? l.Version : -1;
        if (v == visualVersion) return;
        visualVersion = v;
        visualSurfaces.Clear();
        if (l == null) return;
        foreach (MeshRenderer r in l.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (r == null || !r.enabled || r.GetComponent<Collider>() != null) continue;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name != "Cube") continue;   // sadece kutu (sınırı tam oturur)
            Vector3 f = r.transform.forward;
            if (Mathf.Abs(f.x) > 0.02f && Mathf.Abs(f.z) > 0.02f) continue;                     // eğik / dönük olanlar değil
            Bounds b = r.bounds;
            if (b.size.y < 1.2f || Mathf.Max(b.size.x, b.size.z) < 0.6f) continue;               // küçük süsler değil
            if (r.GetComponentInParent<ArtworkDisplay>() != null || r.GetComponentInParent<Interactable>() != null ||
                r.GetComponentInParent<RoomCamera>() != null) continue;
            visualSurfaces.Add(b);
        }
    }

    /// <summary>Fizik ışını + görünen süs kutuları: hangisi daha yakınsa o yüzey. col = fiziksel çarpıştırıcı (süs ise null).</summary>
    private static bool RaySurface(Vector3 o, Vector3 d, float max, PlayerStealth me, out Vector3 point, out Vector3 normal, out Collider col)
    {
        point = normal = Vector3.zero;
        col = null;
        float best = float.MaxValue;
        if (FirstHit(o, d, max, me, out RaycastHit hit)) { best = hit.distance; point = hit.point; normal = hit.normal; col = hit.collider; }
        EnsureVisualSurfaces();
        foreach (Bounds b in visualSurfaces)
        {
            if (!RayBox(o, d, b, Mathf.Min(max, best), out float t, out Vector3 n)) continue;
            best = t; point = o + d * t; normal = n; col = null;
        }
        return best < float.MaxValue;
    }

    /// <summary>Işın - kutu kesişimi (dışarıdan girerken). n = girilen yüzün normali.</summary>
    private static bool RayBox(Vector3 o, Vector3 d, Bounds b, float max, out float t, out Vector3 n)
    {
        t = 0f; n = Vector3.zero;
        if (b.Contains(o)) return false;
        float tmin = 0f, tmax = max;
        int axis = -1;
        for (int i = 0; i < 3; i++)
        {
            float oi = o[i], di = d[i], lo = b.min[i], hi = b.max[i];
            if (Mathf.Abs(di) < 1e-6f) { if (oi < lo || oi > hi) return false; continue; }
            float t1 = (lo - oi) / di, t2 = (hi - oi) / di;
            if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
            if (t1 > tmin) { tmin = t1; axis = i; }
            if (t2 < tmax) tmax = t2;
            if (tmin > tmax) return false;
        }
        if (axis < 0 || tmin <= 0.02f) return false;
        t = tmin;
        n[axis] = d[axis] > 0f ? -1f : 1f;
        return true;
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
        regCmm.RegisterNamedMessageHandler(FxMsg, OnFx);
    }

    private void Unregister()
    {
        if (regCmm != null)
        {
            try { regCmm.UnregisterNamedMessageHandler(ReqMsg); regCmm.UnregisterNamedMessageHandler(FxMsg); }
            catch (Exception) { }
        }
        regCmm = null;
        regNm = null;
    }

    private void SendWall(NetworkManager nm, Vector3 pos, Vector3 normal, List<Vector2[]> drawn, byte color)
    {
        if (nm.IsServer) { ServerWall(nm.LocalClientId, pos, normal, drawn, color); return; }
        var w = new FastBufferWriter(64 + StrokeBytes(drawn), Allocator.Temp);
        try
        {
            w.WriteValueSafe(KindShadow);
            w.WriteValueSafe(pos);
            w.WriteValueSafe(normal);
            WriteStrokes(ref w, drawn);
            w.WriteValueSafe(color);
            regCmm.SendNamedMessage(ReqMsg, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally { w.Dispose(); }
    }

    private void OnRequest(ulong sender, FastBufferReader r)
    {
        if (regNm == null || !regNm.IsServer) return;
        try
        {
            r.ReadValueSafe(out byte kind);
            if (kind == KindThrow)
            {
                r.ReadValueSafe(out Vector3 o); r.ReadValueSafe(out Vector3 d); r.ReadValueSafe(out byte col);
                ServerThrow(sender, o, d, col);
            }
            else if (kind == KindShadow)
            {
                r.ReadValueSafe(out Vector3 p); r.ReadValueSafe(out Vector3 nrm);
                List<Vector2[]> drawn = ReadStrokes(ref r);
                r.ReadValueSafe(out byte col);
                ServerWall(sender, p, nrm, drawn, col);
            }
        }
        catch (Exception) { }
    }

    private bool ServerTakeSpray(ulong client, Vector3 near, float maxDist, out PlayerInventory inv)
    {
        inv = null;
        NetworkManager nm = regNm != null ? regNm : NetworkManager.Singleton;
        if (!nm.ConnectedClients.TryGetValue(client, out NetworkClient c) || c.PlayerObject == null) return false;
        if (Vector3.Distance(c.PlayerObject.transform.position, near) > maxDist) return false;
        inv = c.PlayerObject.GetComponent<PlayerInventory>();
        return inv != null && inv.ServerUseSpray();
    }

    /// <summary>Sunucu: fırlatılan boya topu. Her atış bir kutu harcar; aynı balistik sunucuda simüle edilir, lense değerse kamera kör olur.</summary>
    private void ServerThrow(ulong client, Vector3 origin, Vector3 dir, byte color)
    {
        if (regNm == null || !regNm.IsServer) return;
        if (float.IsNaN(origin.x) || float.IsNaN(origin.y) || float.IsNaN(origin.z) || float.IsNaN(dir.x) || float.IsNaN(dir.y) || float.IsNaN(dir.z)) return;
        if (dir.sqrMagnitude < 0.25f) return;
        dir.Normalize();
        if (serverNextThrow.TryGetValue(client, out float nextOk) && Time.time < nextOk) return; // bekleme süresi
        if (!regNm.ConnectedClients.TryGetValue(client, out NetworkClient c) || c.PlayerObject == null) return;
        PlayerInventory pinv = c.PlayerObject.GetComponent<PlayerInventory>();
        if (pinv == null || !PaintBrush.IsHoldingBrush(pinv)) return; // fırça elde değilse atılamaz
        if (!ServerTakeSpray(client, origin, 4f, out PlayerInventory inv)) return; // boya yoksa / başlangıç oyuncudan uzaksa: atış yok
        serverNextThrow[client] = Time.time + PaintProjectile.Cooldown * 0.8f;
        color = SenderColor(client, color); // boya = atanın karakter rengi

        Vector3 vel = dir * PaintProjectile.Speed;
        Transform root = c.PlayerObject.transform;
        PaintProjectile.TraceResult tr = PaintProjectile.Trace(origin, vel, root);
        BroadcastThrow(client, origin, dir, color);
        if (client != regNm.LocalClientId) PaintProjectile.Launch(origin, vel, color, root); // sunucudaki görsel (atan yerelde zaten gördü)
        if (tr.surface) NoiseSystem.Emit(tr.point, 3f);
        if (tr.camera != null) StartCoroutine(DelayedBlind(tr.camera.name, color, tr.time, inv, tr.point));
    }

    /// <summary>Top uçuş süresi kadar bekler, sonra lens boyanır (görsel çarpmayla aynı anda).</summary>
    private System.Collections.IEnumerator DelayedBlind(string camName, byte color, float delay, PlayerInventory inv, Vector3 at)
    {
        yield return new WaitForSeconds(delay);
        RoomCamera rc = null;
        foreach (RoomCamera c in RoomCamera.All) if (c != null && c.name == camName) { rc = c; break; }
        if (rc == null || !CanPaintCamera(rc)) yield break; // bu arada başkası boyadı / görev bitti
        if (inv != null) inv.ServerPlaySound(SoundId.CoinLand, at);
        BroadcastCamera(camName, color);
        ApplyCamera(camName, color);
    }

    private void BroadcastThrow(ulong thrower, Vector3 origin, Vector3 dir, byte color)
    {
        List<ulong> ids = Others();
        ids.Remove(thrower); // atan zaten yerelde gördü
        if (ids.Count == 0) return;
        using (var w = new FastBufferWriter(64, Allocator.Temp))
        {
            w.WriteValueSafe(KindThrow);
            w.WriteValueSafe(thrower);
            w.WriteValueSafe(origin);
            w.WriteValueSafe(dir);
            w.WriteValueSafe(color);
            regCmm.SendNamedMessage(FxMsg, ids, w, NetworkDelivery.Reliable);
        }
    }

    private void ServerWall(ulong client, Vector3 pos, Vector3 normal, List<Vector2[]> drawn, byte color)
    {
        color = SenderColor(client, color); // boya = çizenin karakter rengi
        if (shadows.Count >= 12) return;
        normal.y = 0f;
        if (normal.sqrMagnitude < 0.5f) return;
        normal.Normalize();
        if (!ServerTakeSpray(client, pos, WallRange + 3f, out PlayerInventory inv)) return;
        NoiseSystem.Emit(pos, 2f);
        // Boştaki bir saklanma noktası bu kapıya atanır (E ile içine saklanılır)
        HidingSpot spot = null;
        foreach (HidingSpot h in HidingSpot.All)
        {
            if (h == null || !h.IsSpawned || !h.Parked || h.IsOccupied) continue;
            bool used = false;
            foreach (Shadow s in shadows) if (s.spot == h) { used = true; break; }
            if (used) continue;
            if (spot == null || h.NetworkObjectId < spot.NetworkObjectId) spot = h;
        }
        ulong spotId = spot != null ? spot.NetworkObjectId : NoSpot;
        // Kapı yuvası arkadaşın HidingSpot altyapısıyla duvara yerleşir (konum / süre herkese senkron); görüntüyü biz çizeriz
        if (spot != null) spot.ServerPaint(pos + normal * 0.03f + Vector3.up * 1.05f, Mathf.Atan2(normal.x, normal.z) * Mathf.Rad2Deg, UnusedDoorSeconds + 0.5f);
        BroadcastShadow(pos, normal, spotId, drawn, color);
        ApplyShadow(pos, normal, spotId, drawn, color);
    }

    private List<ulong> Others()
    {
        var ids = new List<ulong>();
        foreach (ulong id in regNm.ConnectedClientsIds) if (id != regNm.LocalClientId) ids.Add(id);
        return ids;
    }

    private void BroadcastCamera(string camName, byte color)
    {
        List<ulong> ids = Others();
        if (ids.Count == 0) return;
        using (var w = new FastBufferWriter(96, Allocator.Temp))
        {
            w.WriteValueSafe(KindCamera);
            w.WriteValueSafe(camName);
            w.WriteValueSafe(color);
            regCmm.SendNamedMessage(FxMsg, ids, w, NetworkDelivery.Reliable);
        }
    }

    private void BroadcastShadow(Vector3 pos, Vector3 normal, ulong spotId, List<Vector2[]> drawn, byte color)
    {
        List<ulong> ids = Others();
        if (ids.Count == 0) return;
        var w = new FastBufferWriter(72 + StrokeBytes(drawn), Allocator.Temp);
        try
        {
            w.WriteValueSafe(KindShadow);
            w.WriteValueSafe(pos);
            w.WriteValueSafe(normal);
            w.WriteValueSafe(spotId);
            WriteStrokes(ref w, drawn);
            w.WriteValueSafe(color);
            regCmm.SendNamedMessage(FxMsg, ids, w, NetworkDelivery.ReliableFragmentedSequenced);
        }
        finally { w.Dispose(); }
    }

    private void OnFx(ulong sender, FastBufferReader r)
    {
        if (regNm == null || regNm.IsServer || sender != NetworkManager.ServerClientId) return;
        try
        {
            r.ReadValueSafe(out byte kind);
            if (kind == KindCamera) { r.ReadValueSafe(out string n); r.ReadValueSafe(out byte col); ApplyCamera(n, col); }
            else if (kind == KindRemove) { r.ReadValueSafe(out ulong sid); RemoveDoor(sid); }
            else if (kind == KindThrow)
            {
                r.ReadValueSafe(out ulong thrower); r.ReadValueSafe(out Vector3 o); r.ReadValueSafe(out Vector3 d); r.ReadValueSafe(out byte col);
                if (thrower == regNm.LocalClientId || d.sqrMagnitude < 0.25f) return;
                Transform root = null;
                if (regNm.SpawnManager != null) { NetworkObject po = regNm.SpawnManager.GetPlayerNetworkObject(thrower); if (po != null) root = po.transform; }
                PaintProjectile.Launch(o, d.normalized * PaintProjectile.Speed, col, root);
            }
            else if (kind == KindShadow)
            {
                r.ReadValueSafe(out Vector3 p); r.ReadValueSafe(out Vector3 nrm); r.ReadValueSafe(out ulong spotId);
                List<Vector2[]> drawn = ReadStrokes(ref r);
                r.ReadValueSafe(out byte col);
                ApplyShadow(p, nrm, spotId, drawn, col);
            }
        }
        catch (Exception) { }
    }

    // =====================================================================
    //  Görseller
    // =====================================================================

    private static readonly Color[] PaintColors =
    {
        new Color(0.85f, 0.15f, 0.55f), new Color(0.15f, 0.55f, 0.95f), new Color(0.95f, 0.75f, 0.1f), new Color(0.2f, 0.75f, 0.35f),
    };

    // ---------------- Boya rengi = KARAKTER RENGİ ----------------
    // Boya kutusu alınınca karakterin renginde gelir (menüde seçtiğin renk). Ağda renk sırası (byte) gider;
    // sunucu, isteği gönderen oyuncunun karakter rengini kullanır.
    /// <summary>Boya renkleri: karakter renkleriyle aynı dizi (PlayerNetworkSetup.Colors).</summary>
    public static Color[] Palette => PlayerNetworkSetup.Colors;
    private static PlayerNetworkSetup localSetup;
    /// <summary>Bu oyuncunun boya rengi = karakter rengi.</summary>
    public static int LocalColor
    {
        get
        {
            PlayerStealth me = PlayerStealth.Local;
            if (me != null && (localSetup == null || localSetup.gameObject != me.gameObject)) localSetup = me.GetComponent<PlayerNetworkSetup>();
            if (me != null && localSetup != null && localSetup.IsSpawned) return localSetup.ColorIndex;
            return Mathf.Clamp(PlayerNetworkSetup.SavedColorIndex, 0, Palette.Length - 1);
        }
    }

    /// <summary>Sunucu: isteği gönderen oyuncunun karakter rengi (bulunamazsa gelen değer).</summary>
    private byte SenderColor(ulong client, byte fallback)
    {
        NetworkManager nm = regNm != null ? regNm : NetworkManager.Singleton;
        if (nm != null && nm.ConnectedClients.TryGetValue(client, out NetworkClient c) && c.PlayerObject != null)
        {
            PlayerNetworkSetup ps = c.PlayerObject.GetComponent<PlayerNetworkSetup>();
            if (ps != null) return (byte)ps.ColorIndex;
        }
        return fallback;
    }

    public static Color ColorOf(int i) => Palette[Mathf.Clamp(i, 0, Palette.Length - 1)];
    private static readonly Material[] colorMats = new Material[Palette.Length];
    /// <summary>Renk başına tek malzeme (her boyamada yeni malzeme açılmaz).</summary>
    public static Material ColorMat(int i)
    {
        i = Mathf.Clamp(i, 0, Palette.Length - 1);
        if (colorMats[i] == null) colorMats[i] = ArtworkLook.MakeLit(Palette[i]);
        return colorMats[i];
    }
    private static readonly Material[] colorLineMats = new Material[Palette.Length];
    /// <summary>Fırça çizgisi malzemesi (ışıksız, renk başına bir tane).</summary>
    public static Material ColorLineMat(int i)
    {
        i = Mathf.Clamp(i, 0, Palette.Length - 1);
        if (colorLineMats[i] != null) return colorLineMats[i];
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        Material m = sh != null ? new Material(sh) : ArtworkLook.MakeLit(Palette[i]);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Palette[i]);
        colorLineMats[i] = m;
        return m;
    }

    private void ApplyCamera(string camName, byte color)
    {
        blinded[camName] = Time.time + BlindSeconds;
        foreach (RoomCamera c in RoomCamera.All)
        {
            if (c == null || c.name != camName) continue;
            Transform head = c.transform.Find("Head");
            Transform parent = head != null ? head : c.transform;
            Transform old = parent.Find("BrushPaintBlob");
            if (old != null) Destroy(old.gameObject);
            GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            blob.name = "BrushPaintBlob";
            Destroy(blob.GetComponent<Collider>());
            blob.transform.SetParent(parent, false);
            blob.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            blob.transform.localScale = new Vector3(0.24f, 0.24f, 0.08f);
            Material pm = ColorMat(color); // boyayanın seçtiği renk
            Renderer br = blob.GetComponent<Renderer>();
            br.sharedMaterial = pm;
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Sıçrama: lensin çevresine birkaç küçük damla / akıntı (aynı ömür, ortak malzeme)
            var rng = new System.Random(camName.GetHashCode() ^ Mathf.RoundToInt(Time.time * 10f));
            for (int k = 0; k < 6; k++)
            {
                GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "PaintSplat";
                Destroy(s.GetComponent<Collider>());
                s.transform.SetParent(blob.transform, false);
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 0.55f + (float)rng.NextDouble() * 0.5f;
                bool drip = k < 2; // aşağı akan iki damla
                s.transform.localPosition = drip ? new Vector3((float)rng.NextDouble() - 0.5f, -0.6f - (float)rng.NextDouble() * 0.5f, 0.1f)
                                                 : new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0.15f);
                float sz = 0.18f + (float)rng.NextDouble() * 0.22f;
                s.transform.localScale = drip ? new Vector3(sz * 0.7f, sz * 1.8f, 0.6f) : new Vector3(sz, sz, 0.6f);
                Renderer sr = s.GetComponent<Renderer>();
                sr.sharedMaterial = pm;
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Destroy(blob, BlindSeconds); // lens kendiliğinden temizlenir (güvenlik siler)
            GameAudio.PlayAt(SoundId.CoinLand, c.transform.position, 0.5f);
        }
    }

    private void ApplyShadow(Vector3 pos, Vector3 normal, ulong spotId, List<Vector2[]> drawn, byte color)
    {
        HidingSpot spot = null;
        NetworkManager nm = regNm != null ? regNm : NetworkManager.Singleton;
        if (spotId != NoSpot && nm != null && nm.SpawnManager != null &&
            nm.SpawnManager.SpawnedObjects.TryGetValue(spotId, out NetworkObject no) && no != null)
        {
            spot = no.GetComponent<HidingSpot>();
            if (spot != null) spot.LocalPlaceAsPaintedDoor(pos, normal, ShadowWidth, ShadowHeight);
        }

        if (fxRoot == null) { fxRoot = new GameObject("SprayFx").transform; fxRoot.SetParent(transform, false); }
        Quaternion rot = Quaternion.LookRotation(-normal, Vector3.up); // ön yüz odaya baksın
        GameObject g = new GameObject("PaintedShadow");
        g.transform.SetParent(fxRoot, true);
        g.transform.SetPositionAndRotation(pos + normal * 0.02f, rot);

        if (drawn != null && drawn.Count > 0)
        {
            // Kapı ÇİZİLDİĞİ haliyle kalır: oyuncunun fırça çizgileri (birkaç çizgi = çok ucuz)
            Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
            foreach (Vector2[] st in drawn)
            {
                GameObject lg = new GameObject("DrawnDoorLine");
                lg.transform.SetParent(g.transform, false);
                lg.transform.rotation = Quaternion.LookRotation(-normal);
                LineRenderer lr = lg.AddComponent<LineRenderer>();
                lr.sharedMaterial = ColorLineMat(color);
                lr.widthMultiplier = 0.07f;
                lr.numCapVertices = 4;
                lr.numCornerVertices = 2;
                lr.useWorldSpace = true;
                lr.alignment = LineAlignment.TransformZ;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.positionCount = st.Length;
                for (int k = 0; k < st.Length; k++) lr.SetPosition(k, pos + right * st[k].x + Vector3.up * st[k].y + normal * 0.014f);
            }
            shadows.Add(new Shadow { pos = pos, normal = normal, visual = g.transform, spot = spot, createdAt = Time.time });
            GameAudio.PlayAt(SoundId.CoinLand, pos, 0.4f);
            return;
        }

        // (Çizgi verisi yoksa) "kapı": dışta seçilen boya renginde çerçeve, içte aynı rengin koyusu (kapı kanadı), altta gölge
        Color pc = ColorOf(color);
        Part(g.transform, new Vector3(0f, ShadowHeight * 0.5f, 0f), new Vector3(ShadowWidth + 0.12f, ShadowHeight + 0.08f, 0.01f), pc);
        Part(g.transform, new Vector3(0f, ShadowHeight * 0.5f - 0.02f, -0.006f), new Vector3(ShadowWidth, ShadowHeight, 0.01f), Color.Lerp(pc, Color.black, 0.45f));
        Part(g.transform, new Vector3(ShadowWidth * 0.3f, ShadowHeight * 0.45f, -0.012f), new Vector3(0.06f, 0.06f, 0.01f), new Color(0.35f, 0.3f, 0.2f)); // sahte kapı kolu
        Transform floor = Part(g.transform, new Vector3(0f, 0.004f, -0.45f), new Vector3(ShadowWidth, 0.006f, 0.9f), new Color(0.05f, 0.05f, 0.06f));
        floor.name = "FloorShadow";

        shadows.Add(new Shadow { pos = pos, normal = normal, visual = g.transform, spot = spot, createdAt = Time.time });
        GameAudio.PlayAt(SoundId.CoinLand, pos, 0.4f);
    }

    private static readonly Material[] paintMats = new Material[4];
    private static Material PaintMat(int i)
    {
        i = Mathf.Clamp(i, 0, PaintColors.Length - 1);
        if (paintMats[i] == null) paintMats[i] = ArtworkLook.MakeLit(PaintColors[i]); // her boyamada yeni malzeme açılmasın
        return paintMats[i];
    }

    private static Material lineMat;
    private static Material LineMat()
    {
        if (lineMat != null) return lineMat;
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        lineMat = sh != null ? new Material(sh) : ArtworkLook.MakeLit(new Color(0.06f, 0.05f, 0.07f));
        if (lineMat.HasProperty("_BaseColor")) lineMat.SetColor("_BaseColor", new Color(0.06f, 0.05f, 0.07f));
        return lineMat;
    }

    private static Transform Part(Transform parent, Vector3 localPos, Vector3 scale, Color c)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = ArtworkLook.MakeLit(c);
        return go.transform;
    }

    // =====================================================================
    //  Arayüz
    // =====================================================================

    private void OnGUI()
    {
        if (regCmm == null || PlayerStealth.Local == null || SessionMenuUI.IsPaused || WorkbenchUI.IsOpen || BodyCam.PanelOpen) return;

        // Boyalı gölgede gizli: küçük olumlu durum satırı (Sage)
        if (PlayerStealth.Local.CurrentHidingSpot == null && LocalConcealed())
        {
            string conc = Loc.T("spray.concealed");
            float cw = UIUtil.Measure(conc, TextRole.Caption).x + 24f;
            Rect cr = new Rect((Screen.width - cw) * 0.5f, Screen.height * 0.5f + 70f * UITheme.Scale, cw, Mathf.Round(24f * UITheme.Scale));
            UIUtil.Panel(cr, PanelKind.Plaque, 0.85f);
            UIUtil.Text(cr, conc, TextRole.Caption, UITheme.Sage, TextAnchor.MiddleCenter);
        }

        if (aim == Aim.None) { PromptHud.Idle(5); return; }
        PlayerInventory inv = PlayerInventory.Local;
        int count = inv != null ? inv.SprayCount : 0;
        string text = count <= 0 ? Loc.T("spray.need", UpgradeCatalog.SprayPrice)
                    : Loc.T(aim == Aim.Camera ? "spray.camera" : "spray.wall", count);
        // Mevcut / kullanılamaz (sprey yok: neden) / aktif (basılı tutma ilerlemesi)
        float hold = count > 0 ? Mathf.Clamp01(progress / HoldSeconds) : -1f;
        PromptHud.Show(text, count > 0 ? PromptTone.Normal : PromptTone.Danger, hold, 5);
    }
}
