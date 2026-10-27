using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// FIRÇA — ressamın zorunlu aleti. Herkesin çantasında HER ZAMAN bir fırça vardır (bırakılamaz, satılamaz, çalınamaz;
/// kaybolursa sunucu yenisini koyar). B ile ele alınır / kaldırılır (ya da Tab çantasından tıklanarak).
///
/// Fırça eldeyken:
///  - DUVARA KAPI ÇİZ: duvara bak, SOL TIK basılı tutup fareyle çiz. Çizgiler duvarda kalır. Bir kapının iki yan kenarını
///    (eğri / yamuk olabilir, boy ≥ ~1 m) ve üst kenarını çizince kapı tamamlanır → duvarda sahte kapı belirir (E ile içine
///    saklanılır; SprayPaint). Sağ tık: çizimi sil. Ekranda hangi kenarların bittiği görünür.
///  - KAMERAYI KÖR ET: SAĞ TIK ile BOYA TOPU fırlat (kapı çizerken değilken; ~1 sn bekleme, menzil ~12 m, yerçekimli).
///    Top kamera lensine (~0,35 m) isabet ederse kamera kör olur (SprayPaint.BlindSeconds). İsabeti sunucu hesaplar (PaintProjectile.Trace).
///    Her atış bir kutu harcar; duvara/zemine çarpan top leke bırakır. Fırlatmak suçtur.
///  - Her bitmiş iş bir BOYA kutusu harcar (garaj mağazası). Çizerken / boyarken suç işliyorsun: biri görürse şüphelenir.
/// Görsel: kendi ekranında sağ altta fırça (çizerken sallanır), diğer oyuncularda karakterin sağ elinde.
/// Çizim yerel görünür (yarım çizimi diğerleri görmez); biten kapı herkeste belirir.
/// Sahneye bir şey eklemeye gerek yok. Fırça eşyası: Assets/Items/tool_brush.asset (ItemDatabase'de).
/// </summary>
public class PaintBrush : MonoBehaviour
{
    public const string ItemId = "tool_brush";
    public static PaintBrush Instance { get; private set; }

    private const float WallReach = 4f;                       // duvara çizme menzili (eskisi 2,6 m)
    private const float PointStep = 0.035f;
    // Boy / en sınırları gevşek: duvara küçük ya da yüksekte çizilen kapı da olur (kapı yine duvarın dibine yerleşir)
    // Kapı tanıma bilerek AFFEDİCİ: elle çizilen yamuk / eğri / alt kenarı da çizilmiş kapılar kabul edilir
    private const float DoorMinTop = 0f, DoorMinHeight = 0.6f, DoorMinWidth = 0.3f, DoorMaxWidth = 2.6f;
    private const float DoorRatioMin = 0.75f, DoorRatioMax = 5f;   // yükseklik / genişlik: kareye yakın / biraz geniş kapı da olur
    // "b" / "h" gibi şekiller kapı değil: üst kenar iki yanı birleştirmeli, iki yan da boyun çoğunu kaplamalı (aşağıda)
    private const float DoorMaxInterior = 0.45f;                  // iç bölgedeki nokta oranı (karalama / dolgu kapı sayılmaz)
    private const float DoorSideVertical = 0.5f;                  // yan kenar noktalarının köşe şeridine yakın olma oranı (dikey kenar)
    private const float EdgeNeed = 0.65f;  // üst kenar
    private const float SideNeed = 0.75f;  // yan kenarlar (kenar şeridinde, boyun %75'i)

    private static ItemDefinition brushDef;
    private static int brushIndex = -2;

    // ---------------- Yerel çizim ----------------
    private class Stroke { public LineRenderer line; public readonly List<Vector3> world = new List<Vector3>(); }
    private readonly List<Vector2> pts = new List<Vector2>();     // duvar düzleminde (u = yatay, v = yerden yükseklik)
    private readonly List<Stroke> strokes = new List<Stroke>();
    private Stroke current;
    private bool hasPlane;
    private float lastDrawTime;                                    // son çizilen nokta (kapı olmayan çizim 10 sn sonra silinir)
    private const float DoodleSeconds = 10f;
    private struct Leftover { public LineRenderer line; public float expire; public float width; }
    private readonly List<Leftover> leftovers = new List<Leftover>(); // bırakılmış çizimler (süresi dolunca silinir)
    private Vector3 planeBase, planeNormal, planeRight;
    private float edgeL, edgeR, edgeTop;
    private bool sizeOk;
    private float submitAt = -1f;
    private Vector2 doneCenter;

    private float nextThrow;   // boya topu bekleme süresi (unscaled)
    private float nextHiss;

    private bool holding, aimWall;
    private Vector3 aimHit, aimBase, aimNormal;

    private static Material woodMat, metalMat, hairMat, tipMat;
    private Transform fpBrush;                                           // kendi ekranındaki fırça
    private readonly Dictionary<ulong, Transform> remoteBrush = new Dictionary<ulong, Transform>();
    private readonly Dictionary<ulong, Transform> handCache = new Dictionary<ulong, Transform>();
    private float nextServerCheck;
    private GUIStyle tipStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("PaintBrush");
        DontDestroyOnLoad(go);
        go.AddComponent<PaintBrush>();
    }

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Fırça eşyasının tanımı (ItemDatabase'de id "tool_brush").</summary>
    public static ItemDefinition BrushDef
    {
        get
        {
            if (brushIndex == -2 || (brushIndex >= 0 && brushDef == null))
            {
                brushIndex = -1;
                ItemDatabase db = ItemDatabase.Instance;
                if (db != null)
                    for (int i = 0; i < db.Count; i++)
                    {
                        ItemDefinition d = db.Get(i);
                        if (d != null && d.id == ItemId) { brushDef = d; brushIndex = i; break; }
                    }
            }
            return brushDef;
        }
    }

    public static bool IsBrush(ItemDefinition d) => d != null && d.id == ItemId;

    private int paintCode;

    /// <summary>Bu oyuncu şu an fırçayı elinde tutuyor mu?</summary>
    public static bool IsHoldingBrush(PlayerInventory inv)
    {
        if (inv == null || !inv.IsSpawned || !inv.HasHeldItem || BrushDef == null) return false;
        return inv.GetSlot(inv.HeldSlot).ItemIndex == brushIndex;
    }

    private static int FindBrushSlot(PlayerInventory inv)
    {
        if (inv == null || BrushDef == null) return -1;
        for (int i = 0; i < inv.SlotCount; i++) if (inv.GetSlot(i).ItemIndex == brushIndex) return i;
        return -1;
    }

    // =====================================================================
    //  Döngü
    // =====================================================================

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool active = nm != null && nm.IsListening && (nm.IsServer || nm.IsConnectedClient);
        if (!active)
        {
            ClearSketch();
            ClearLeftovers();
            SprayPaint.SetLocalPainting(false);
            if (fpBrush != null) fpBrush.gameObject.SetActive(false);
            return;
        }
        if (nm.IsServer && Time.unscaledTime >= nextServerCheck) { nextServerCheck = Time.unscaledTime + 1f; ServerEnsureBrushes(); }

        LocalTick();
        TickDoodles();
        UpdateVisuals();
        handBrushActive = true;
    }

    private bool handBrushActive;

    /// <summary>
    /// Eldeki (üçüncü şahıs) fırçalar: karakter iskeleti BU KAREDE pozlandıktan SONRA (LateUpdate) avuca oturtulur.
    /// Update'te yapılınca el bir kare geriden geliyor, fırça elden kayıyor / titriyordu (dışarıdan ve aynada).
    /// </summary>
    private void LateUpdate()
    {
        if (!handBrushActive) return;
        handBrushActive = false;
        PlaceHandBrushes();
    }

    /// <summary>Sunucu: herkesin çantasında tam bir fırça olsun (yeni oyuncu, sıfırlama, el koyma sonrası da).</summary>
    private static void ServerEnsureBrushes()
    {
        if (BrushDef == null) return;
        foreach (PlayerStealth p in PlayerStealth.All)
        {
            if (p == null || !p.IsSpawned) continue;
            PlayerInventory inv = p.GetComponent<PlayerInventory>();
            if (inv == null || !inv.IsSpawned) continue;
            int count = 0;
            for (int i = inv.SlotCount - 1; i >= 0; i--)
            {
                if (inv.GetSlot(i).ItemIndex != brushIndex) continue;
                count++;
                if (count > 1) inv.ServerRemoveAt(i, inv.GetSlot(i).Count); // fazlası olmasın
            }
            if (count == 0) inv.ServerAddItem(BrushDef);
        }
    }

    // ---------------- Yerel oyuncu ----------------

    private void LocalTick()
    {
        PlayerInventory inv = PlayerInventory.Local;
        PlayerStealth me = PlayerStealth.Local;
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        bool canAct = inv != null && me != null && me.IsSpawned && !me.IsJailed && me.CurrentHidingSpot == null &&
                      !ChatUI.IsTyping && !SessionMenuUI.IsPaused && !PlayerInventory.IsPanelOpen && !WorkbenchUI.IsOpen &&
                      !BodyCam.PanelOpen && !PlayerController.InputLocked && Cursor.lockState == CursorLockMode.Locked &&
                      !PushSystem.LocalKnockedOut;

        // B: fırçayı ele al / kaldır
        if (canAct && kb != null && kb.bKey.wasPressedThisFrame)
        {
            int slot = FindBrushSlot(inv);
            if (slot >= 0) inv.ToggleHoldSlot(slot);
        }

        holding = IsHoldingBrush(inv);
        aimWall = false;
        Camera cam = Camera.main;
        if (!holding || !canAct || cam == null)
        {
            SprayPaint.SetLocalPainting(false);
            if (paintCode != 0 && me != null && me.IsSpawned) { paintCode = 0; me.SetPaintTarget(0); }
            EndStroke();
            if (!holding) AbandonSketch(); // fırça kaldırıldı: çizim duvarda süresi dolana kadar kalır
            return;
        }

        Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
        bool lmb = mouse != null && mouse.leftButton.isPressed;
        bool painting = false;
        // Boya yoksa çizilmez / boyanmaz (basınca uyarı)
        bool hasPaint = inv.SprayCount > 0;
        if (!hasPaint && lmb)
        {
            if (mouse.leftButton.wasPressedThisFrame) inv.ShowLocalToast(Loc.T("brush.needpaint", UpgradeCatalog.SprayPrice));
            lmb = false;
        }

        // Çizim çok uzakta kaldıysa bırakılır (duvarda süresi dolana kadar kalır)
        if (hasPlane && Vector3.Distance(me.transform.position, planeBase) > 5f) AbandonSketch();
        // Sağ tık: kapı çizerken (son 3 sn içinde çizgi çizildiyse) çizimi siler; aksi halde BOYA TOPU fırlatır
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            bool sketching = hasPlane && (current != null || (strokes.Count > 0 && Time.time - lastDrawTime < 3f));
            if (sketching) ClearSketch();
            else TryThrow(inv, cam, me);
        }

        // Duvar (kapı çizme)
        if (SprayPaint.WallCandidate(eye, fwd, WallReach, me, out aimHit, out aimBase, out aimNormal))
        {
            aimWall = !SprayPaint.NearExistingDoor(aimBase) || hasPlane;
            if (aimWall && lmb && submitAt < 0f)
            {
                if (!hasPlane) StartPlane(aimBase, aimNormal);
                if (OnPlane(aimHit, aimNormal))
                {
                    painting = true;
                    AddPoint(aimHit);
                    Hiss(aimHit);
                }
                else EndStroke();
            }
            else EndStroke();
        }
        else EndStroke();

        if (mouse != null && mouse.leftButton.wasReleasedThisFrame) { EndStroke(); Evaluate(); TrySubmit(inv); }
        if (submitAt > 0f && Time.unscaledTime >= submitAt) { submitAt = -1f; ClearSketch(); }
        // Kapı olmayan çizim: son çizgiden 10 sn sonra duvardan silinir
        if (hasPlane && current == null && pts.Count > 0 && Time.time - lastDrawTime > DoodleSeconds) AbandonSketch();

        SprayPaint.SetLocalPainting(painting);
        int code = painting ? 1 : 0;
        if (code != paintCode) { paintCode = code; me.SetPaintTarget(code); }
    }

    /// <summary>Boya topu fırlat: boya kutusu gerekir, ~1 sn bekleme. Isabeti sunucu belirler (SprayPaint.ServerThrow).</summary>
    private void TryThrow(PlayerInventory inv, Camera cam, PlayerStealth me)
    {
        if (Time.unscaledTime < nextThrow || SprayPaint.Instance == null) return;
        if (inv.SprayCount <= 0) { inv.ShowLocalToast(Loc.T("spray.none")); nextThrow = Time.unscaledTime + 0.4f; return; }
        nextThrow = Time.unscaledTime + PaintProjectile.Cooldown;
        Vector3 fwd = cam.transform.forward;
        Vector3 origin = cam.transform.position + fwd * 0.35f - cam.transform.up * 0.1f;
        Vector3 dir = (fwd + Vector3.up * 0.07f).normalized; // çok hafif yukarı: yerçekimi payı
        SprayPaint.Instance.RequestThrow(origin, dir);
    }

    private void Hiss(Vector3 at)
    {
        if (Time.unscaledTime < nextHiss) return;
        nextHiss = Time.unscaledTime + 0.22f;
        GameAudio.PlayAt(SoundId.CoinThrow, at, 0.18f, 0.35f);
    }

    // ---------------- Kapı çizimi ----------------

    private void StartPlane(Vector3 basePos, Vector3 normal)
    {
        AbandonSketch(); // önceki çizim (başka duvar) süresi dolana kadar kalır
        hasPlane = true;
        planeBase = basePos;
        planeNormal = normal;
        planeRight = Vector3.Cross(Vector3.up, normal).normalized;
    }

    private bool OnPlane(Vector3 hit, Vector3 normal)
    {
        if (Vector3.Angle(normal, planeNormal) > 15f) return false;
        return Mathf.Abs(Vector3.Dot(hit - planeBase, planeNormal)) < 0.25f;
    }

    private Vector2 ToPlane(Vector3 w)
    {
        Vector3 d = w - planeBase;
        return new Vector2(Vector3.Dot(d, planeRight), d.y);
    }

    private void AddPoint(Vector3 hit)
    {
        Vector2 p = ToPlane(hit);
        lastDrawTime = Time.time;
        if (current == null)
        {
            current = new Stroke { line = NewLine() };
            strokes.Add(current);
        }
        Vector2 last = current.world.Count > 0 ? ToPlane(current.world[current.world.Count - 1] - planeNormal * 0.012f) : p;
        float gap = Vector2.Distance(p, last);
        if (current.world.Count > 0 && gap < PointStep) return;
        // Fare hızlı oynatılınca noktalar seyrek düşer: aradaki boşluğu da çizilmiş say (çizgi zaten düz bağlanıyor)
        if (current.world.Count > 0 && gap > PointStep * 2f && gap < 1.5f)
        {
            int steps = Mathf.Min(40, Mathf.FloorToInt(gap / PointStep));
            for (int i = 1; i < steps; i++) pts.Add(Vector2.Lerp(last, p, (float)i / steps));
        }
        Vector3 onWall = planeBase + planeRight * p.x + Vector3.up * p.y + planeNormal * 0.012f;
        current.world.Add(onWall);
        current.line.positionCount = current.world.Count;
        current.line.SetPosition(current.world.Count - 1, onWall);
        pts.Add(p);
        Evaluate();
    }

    private void EndStroke() => current = null;

    private LineRenderer NewLine()
    {
        GameObject go = new GameObject("BrushStroke");
        go.transform.SetParent(transform, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = SprayPaint.ColorLineMat(SprayPaint.LocalColor); // seçilen boya renginde
        lr.widthMultiplier = 0.07f;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 2;
        lr.useWorldSpace = true;
        lr.positionCount = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.alignment = LineAlignment.TransformZ;
        go.transform.rotation = Quaternion.LookRotation(-planeNormal); // çizgi duvarın yüzüne yatık dursun
        return lr;
    }

    /// <summary>Çizim bir kapıya benziyor mu? Sol, sağ ve üst kenarın ne kadarı çizildi + boy / en uygun mu.</summary>
    private void Evaluate()
    {
        // Esnek kapı tanıma: kenarların dümdüz olması gerekmez (eğri / yamuk çizim de olur).
        //  - Sol yarıda ve sağ yarıda aşağıdan yukarı uzanan birer çizgi (yükseklik boyunca her 10 cm'de çizgi var mı)
        //  - Üstte soldan sağa uzanan bir çizgi
        //  - Boy en az ~1 m, en 0,4–2,2 m; alt kenar gerekmez (kapının altı yerdir)
        edgeL = edgeR = edgeTop = 0f;
        sizeOk = false;
        if (pts.Count < 6) return;
        float umin = float.MaxValue, umax = float.MinValue, vmin = float.MaxValue, vmax = float.MinValue;
        foreach (Vector2 p in pts)
        {
            umin = Mathf.Min(umin, p.x); umax = Mathf.Max(umax, p.x);
            vmin = Mathf.Min(vmin, p.y); vmax = Mathf.Max(vmax, p.y);
        }
        float w = umax - umin, h = vmax - vmin;
        float ratio = w > 0.001f ? h / w : 0f;
        sizeOk = h >= DoorMinHeight && vmax >= DoorMinTop && w >= DoorMinWidth && w <= DoorMaxWidth && ratio >= DoorRatioMin && ratio <= DoorRatioMax;
        if (sizeOk) sizeOk = LooksRectangular(umin, umax, vmin, vmax); // dikdörtgene yakın, dikey kenarlı, içi dolu değil

        float mid = (umin + umax) * 0.5f;
        float topBand = Mathf.Min(0.35f, h * 0.25f);
        // Yanlar: üst bandın altından çizimin altına kadar (en alttaki %12 sayılmaz: iki yan aynı yerde bitmek zorunda değil)
        float vFrom = vmin + Mathf.Max(0.05f, h * 0.12f), vTo = vmax - topBand;
        // Yan kenar = şeklin KENARINA yakın (en/boyun %28'i) dikey çizgi; ortaya doğru kıvrılan "B" / "D" göbekleri sayılmaz
        edgeL = SideCoverage(true, umin, umax, vFrom, vTo);
        edgeR = SideCoverage(false, umin, umax, vFrom, vTo);
        // İçi boş olmalı: ortadan geçen yatay çizgi ("B"nin beli, "H"nin çizgisi) kapı değil
        if (sizeOk && CrossesInterior(umin, umax, vmin, vmax)) sizeOk = false;
        // Üst: üst banttaki noktalar soldan sağa ne kadar uzanıyor
        edgeTop = TopCoverage(vmax - topBand, umin + w * 0.15f, umax - w * 0.15f);
        doneCenter = new Vector2(mid, 0f);
    }

    /// <summary>Şekil dikdörtgene yakın mı: yan kenarlar dikey (köşe şeridine yakın), iç bölge boş.</summary>
    private bool LooksRectangular(float umin, float umax, float vmin, float vmax)
    {
        float w = umax - umin, h = vmax - vmin;
        float band = w * 0.3f;
        float mid = (umin + umax) * 0.5f;
        float sideFrom = vmin + h * 0.1f, sideTo = vmax - h * 0.2f;
        int sideN = 0, sideNear = 0, inner = 0, total = 0;
        foreach (Vector2 p in pts)
        {
            total++;
            bool nearL = p.x <= umin + band, nearR = p.x >= umax - band;
            if (p.y >= sideFrom && p.y <= sideTo)
            {
                sideN++;
                if (nearL || nearR) sideNear++;
            }
            // iç bölge: yanlardan, üstten ve alttan içeride kalan alan (alt kenarı da çizen kapıyı reddetmesin)
            if (!nearL && !nearR && p.y < vmax - h * 0.2f && p.y > vmin + h * 0.15f) inner++;
        }
        if (total < 6 || sideN < 4) return false;
        if ((float)sideNear / sideN < DoorSideVertical) return false;
        if ((float)inner / total > DoorMaxInterior) return false;
        return true;
    }

    /// <summary>Sol / sağ kenar şeridinde (şeklin dış kenarına en/boyun %28'i kadar yakın), yükseklik boyunca her 10 cm'lik
    /// dilimde en az bir nokta var mı. Yamuk / hafif eğri yan kenar olur; içe kıvrılan göbek ("B") olmaz.</summary>
    private float SideCoverage(bool left, float umin, float umax, float from, float to)
    {
        if (to - from < 0.2f) return 0f;
        float band = Mathf.Max(0.08f, (umax - umin) * 0.28f);
        int n = 0, hit = 0;
        for (float s = from; s <= to + 0.001f; s += 0.1f)
        {
            n++;
            foreach (Vector2 p in pts)
            {
                if (left ? p.x > umin + band : p.x < umax - band) continue;
                if (Mathf.Abs(p.y - s) < 0.09f) { hit++; break; }
            }
        }
        return n > 0 ? (float)hit / n : 0f;
    }

    /// <summary>Şeklin ortasından (yanlardan ve üstten / alttan uzakta) geçen çizgi var mı? Kapının içi boştur.</summary>
    private bool CrossesInterior(float umin, float umax, float vmin, float vmax)
    {
        float w = umax - umin, h = vmax - vmin;
        float x0 = umin + w * 0.3f, x1 = umax - w * 0.3f, y0 = vmin + h * 0.2f, y1 = vmax - h * 0.25f;
        int inside = 0;
        foreach (Vector2 p in pts)
            if (p.x > x0 && p.x < x1 && p.y > y0 && p.y < y1) inside++;
        // ~8 nokta ≈ 28 cm çizgi: ortadan geçen bir çizgi parçası (yanlıştan değen tek tük nokta sayılmaz)
        return inside >= Mathf.Max(8, pts.Count / 14);
    }

    /// <summary>Üst bantta (vAbove'un üstü), soldan sağa her 10 cm'lik dilimde nokta var mı.</summary>
    private float TopCoverage(float vAbove, float from, float to)
    {
        if (to - from < 0.15f) return 0f;
        int n = 0, hit = 0;
        for (float s = from; s <= to + 0.001f; s += 0.1f)
        {
            n++;
            foreach (Vector2 p in pts)
                if (p.y >= vAbove && Mathf.Abs(p.x - s) < 0.09f) { hit++; break; }
        }
        return n > 0 ? (float)hit / n : 0f;
    }

    private bool DoorComplete => sizeOk && edgeL >= SideNeed && edgeR >= SideNeed && edgeTop >= EdgeNeed;

    private void TrySubmit(PlayerInventory inv)
    {
        if (!hasPlane || submitAt > 0f || !DoorComplete) return;
        if (inv.SprayCount <= 0) { inv.ShowLocalToast(Loc.T("brush.needpaint", UpgradeCatalog.SprayPrice)); return; }
        Vector3 basePos = planeBase + planeRight * doneCenter.x;
        if (SprayPaint.NearExistingDoor(basePos)) { inv.ShowLocalToast(Loc.T("brush.toonear")); return; }
        // Kapı eski görünümüyle (koyu kapı boşluğu + çerçeve + kol) belirir: çizgi verisi gönderilmez (ağ da daha hafif)
        if (SprayPaint.Instance != null) SprayPaint.Instance.RequestWall(basePos, planeNormal, null);
        GameAudio.Play2D(SoundId.Ping, 0.5f);
        ClearSketch(); // kapı belirdi: çizgiler hemen silinir (arkada iz kalmasın)
    }

    /// <summary>Çizilen çizgiler, kapının dibine göre (u = ortadan yatay, v = yükseklik), seyreltilmiş (ağ için küçük).</summary>
    private List<Vector2[]> DrawnStrokes(float centerU)
    {
        var result = new List<Vector2[]>();
        int total = 0;
        foreach (Stroke s in strokes) total += s.world.Count;
        float spacing = Mathf.Max(0.05f, total * 0.035f / 320f); // toplam ~320 noktayı geçmesin
        var tmp = new List<Vector2>();
        foreach (Stroke s in strokes)
        {
            if (s.world.Count < 2) continue;
            tmp.Clear();
            for (int i = 0; i < s.world.Count; i++)
            {
                Vector2 p = ToPlane(s.world[i] - planeNormal * 0.012f);
                p.x -= centerU;
                if (tmp.Count == 0 || i == s.world.Count - 1 || Vector2.Distance(p, tmp[tmp.Count - 1]) >= spacing) tmp.Add(p);
            }
            if (tmp.Count >= 2) result.Add(tmp.ToArray());
            if (result.Count >= 24) break;
        }
        return result;
    }

    /// <summary>Çizimi bırak: çizgiler duvarda kalır, son çizgiden 10 sn sonra (sonda incelerek) silinir.</summary>
    private void AbandonSketch()
    {
        float expire = lastDrawTime + DoodleSeconds;
        foreach (Stroke s in strokes)
            if (s.line != null)
            {
                if (Time.time >= expire) Destroy(s.line.gameObject);
                else leftovers.Add(new Leftover { line = s.line, expire = expire, width = s.line.widthMultiplier });
            }
        strokes.Clear();
        ClearSketch();
    }

    private void TickDoodles()
    {
        for (int i = leftovers.Count - 1; i >= 0; i--)
        {
            Leftover l = leftovers[i];
            float left = l.expire - Time.time;
            if (l.line == null || left <= 0f)
            {
                if (l.line != null) Destroy(l.line.gameObject);
                leftovers.RemoveAt(i);
            }
            else if (left < 0.6f) l.line.widthMultiplier = l.width * (left / 0.6f); // son anda incelerek kaybolur
        }
    }

    private void ClearLeftovers()
    {
        foreach (Leftover l in leftovers) if (l.line != null) Destroy(l.line.gameObject);
        leftovers.Clear();
    }

    private void ClearSketch()
    {
        foreach (Stroke s in strokes) if (s.line != null) Destroy(s.line.gameObject);
        strokes.Clear();
        pts.Clear();
        current = null;
        hasPlane = false;
        edgeL = edgeR = edgeTop = 0f;
        sizeOk = false;
        submitAt = -1f;
    }

    // =====================================================================
    //  Fırça görseli
    // =====================================================================

    private static void EnsureBrushMats()
    {
        if (woodMat != null) return;
        woodMat = ArtworkLook.MakeLit(new Color(0.5f, 0.3f, 0.15f));
        metalMat = ArtworkLook.MakeLit(new Color(0.75f, 0.75f, 0.8f));
        hairMat = ArtworkLook.MakeLit(new Color(0.22f, 0.15f, 0.1f));
        tipMat = ArtworkLook.MakeLit(new Color(0.78f, 0.15f, 0.38f));
    }

    /// <summary>Fırça modeli: sap (Y ekseni boyunca), metal bilezik, kıllar, boyalı uç. Uç +Y yönündedir.</summary>
    private static Transform BuildBrush(string name, bool shadows)
    {
        EnsureBrushMats();
        Transform root = new GameObject(name).transform;
        Part(root, PrimitiveType.Cylinder, new Vector3(0f, -0.06f, 0f), new Vector3(0.028f, 0.11f, 0.028f), woodMat, shadows);
        Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.065f, 0f), new Vector3(0.034f, 0.025f, 0.03f), metalMat, shadows);
        Part(root, PrimitiveType.Cube, new Vector3(0f, 0.11f, 0f), new Vector3(0.04f, 0.05f, 0.022f), hairMat, shadows);
        Part(root, PrimitiveType.Cube, new Vector3(0f, 0.142f, 0f), new Vector3(0.036f, 0.022f, 0.02f), tipMat, shadows);
        return root;
    }

    private static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m, bool shadows)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void UpdateVisuals()
    {
        // Kendi ekranın: sağ altta fırça; çizerken ucu baktığın yere doğru uzanır ve sallanır
        Camera cam = Camera.main;
        PlayerStealth meVis = PlayerStealth.Local;
        bool showFp = holding && cam != null && !WorkbenchUI.IsOpen && !BodyCam.PanelOpen && !PlayerAvatar.PreviewActive &&
                      (meVis == null || meVis.CurrentHidingSpot == null); // kapının içindeyken fırça da görünmez
        if (showFp)
        {
            if (fpBrush == null) fpBrush = BuildBrush("FirstPersonBrush", false);
            if (fpBrush.parent != cam.transform) fpBrush.SetParent(cam.transform, false);
            if (!fpBrush.gameObject.activeSelf) fpBrush.gameObject.SetActive(true);
            bool working = SprayPaint.IsSpraying;
            float t = Time.time;
            // Fırça sağ altta, kalem gibi tutulur; boyarken ucu ortaya doğru uzanır ama el ekranın ortasını kapatmaz
            Vector3 rest = new Vector3(0.3f, -0.24f, 0.8f);
            Vector3 work = new Vector3(0.25f, -0.21f, 0.8f) + new Vector3(Mathf.Sin(t * 17f) * 0.02f, Mathf.Cos(t * 13f) * 0.015f, 0f);
            Vector3 want = working ? work : rest + new Vector3(0f, Mathf.Sin(t * 1.7f) * 0.004f, 0f);
            fpBrush.localPosition = Vector3.Lerp(fpBrush.localPosition, want, 1f - Mathf.Exp(-14f * Time.deltaTime));
            Quaternion wantRot = Quaternion.Euler(working ? 55f : 50f, working ? -20f : -25f, working ? 20f : 25f);
            fpBrush.localRotation = Quaternion.Slerp(fpBrush.localRotation, wantRot, 1f - Mathf.Exp(-12f * Time.deltaTime));
            // Fırçayı karakterin kendi eli tutar (birinci şahıs kollar: sağ el sapı kavrar)
            PlayerInventory li = PlayerInventory.Local;
            if (li != null)
            {
                if (fpArms == null || fpArms.gameObject != li.gameObject)
                {
                    fpArms = li.GetComponent<FirstPersonArms>();
                    if (fpArms == null) fpArms = li.gameObject.AddComponent<FirstPersonArms>();
                }
                fpArms.ShowBrush(cam.transform, fpBrush.TransformPoint(0f, 0f, 0f), fpBrush.up); // parmak uçları metal halkanın altında (kalem tutuşu)
            }
        }
        else if (fpBrush != null && fpBrush.gameObject.activeSelf) fpBrush.gameObject.SetActive(false);

        // Diğer oyuncular: fırçayı sağ ellerinde tutar (F5 önizlemede kendi elinde de görünür)
        foreach (PlayerStealth p in PlayerStealth.All)
        {
            if (p == null || !p.IsSpawned) continue;
            ulong id = p.OwnerClientId;
            PlayerInventory inv = p.GetComponent<PlayerInventory>();
            // Kendi elindeki fırça da kurulur ama yalnızca GÖLGE olarak çizilir (birinci şahısta kendi gövden gibi);
            // garajdaki boy aynası çizerken görünür yapar → aynada fırça elinde görünür (F5 önizlemede de)
            bool show = IsHoldingBrush(inv) && !p.IsHidden;
            remoteBrush.TryGetValue(id, out Transform b);
            if (!show) { if (b != null && b.gameObject.activeSelf) b.gameObject.SetActive(false); continue; }
            if (b == null) { b = BuildBrush("HandBrush_" + id, true); remoteBrush[id] = b; }
            if (!b.gameObject.activeSelf) b.gameObject.SetActive(true);
            SetShadowOnly(id, b, p.IsOwner && !PlayerAvatar.PreviewActive);
        }
    }

    /// <summary>Görünen eldeki fırçaları avuca yerleştirir (LateUpdate: iskelet pozundan sonra).</summary>
    private void PlaceHandBrushes()
    {
        foreach (PlayerStealth p in PlayerStealth.All)
        {
            if (p == null || !p.IsSpawned) continue;
            ulong id = p.OwnerClientId;
            if (!remoteBrush.TryGetValue(id, out Transform b) || b == null || !b.gameObject.activeSelf) continue;
            // Karakterin avucunda: sap avuçta, uç öne-yukarı (boyarken daha öne) — parmaklar sapı sarar (PlayerAvatarModel)
            if (!avatars.TryGetValue(id, out PlayerAvatar av) || av == null) { av = p.GetComponent<PlayerAvatar>(); avatars[id] = av; }
            if (av != null && av.TryGetBrushGrip(out Vector3 grip, out Vector3 tipDir))
            {
                // Fırçanın kökü sapın üst ucu: yumruk sapın ortasını kavrasın (sap 0.155 m → yarısı yukarı)
                b.SetPositionAndRotation(grip + tipDir * 0.065f, Quaternion.FromToRotation(Vector3.up, tipDir) * Quaternion.Euler(0f, p.transform.eulerAngles.y, 0f));
                b.localScale = Vector3.one * 1.35f;
                continue;
            }
            Transform hand = FindHand(p, id);
            Vector3 at = hand != null ? hand.position : p.transform.position + p.transform.rotation * new Vector3(0.3f, 0.15f, 0.45f);
            b.SetPositionAndRotation(at + p.transform.forward * 0.03f, p.transform.rotation * Quaternion.Euler(70f, 0f, 0f));
        }
    }

    private readonly Dictionary<ulong, PlayerAvatar> avatars = new Dictionary<ulong, PlayerAvatar>();
    private readonly Dictionary<ulong, bool> brushShadowOnly = new Dictionary<ulong, bool>();

    /// <summary>Eldeki fırçanın renderer'ları: sahibinin kendi ekranında sadece gölge (değişince bir kez ayarlanır).</summary>
    private void SetShadowOnly(ulong id, Transform b, bool shadowOnly)
    {
        if (brushShadowOnly.TryGetValue(id, out bool cur) && cur == shadowOnly) return;
        brushShadowOnly[id] = shadowOnly;
        var mode = shadowOnly ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
        foreach (Renderer r in b.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = mode;
    }

    /// <summary>Yerel oyuncunun elindeki (üçüncü şahıs) fırça — ayna gibi dış kameralar için. Yoksa null.</summary>
    public static Transform LocalHandBrush
    {
        get
        {
            if (Instance == null || PlayerStealth.Local == null) return null;
            Instance.remoteBrush.TryGetValue(PlayerStealth.Local.OwnerClientId, out Transform b);
            return b != null && b.gameObject.activeInHierarchy ? b : null;
        }
    }
    private FirstPersonArms fpArms;

    private Transform FindHand(PlayerStealth p, ulong id)
    {
        if (handCache.TryGetValue(id, out Transform h) && h != null && h.IsChildOf(p.transform)) return h;
        h = null;
        foreach (Transform t in p.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Hand.R") { h = t; break; }
            if (h == null && t.name == "Hand" && t.parent != null && t.parent.name == "ArmR") h = t;
        }
        handCache[id] = h;
        return h;
    }

    // =====================================================================
    //  Arayüz
    // =====================================================================

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (PlayerInventory.Local == null || SessionMenuUI.IsPaused || WorkbenchUI.IsOpen || BodyCam.PanelOpen) return;
        if (!holding) return;
        if (PlayerStealth.Local != null && PlayerStealth.Local.CurrentHidingSpot != null) return; // saklanırken fırça yazısı yok
        // Mağaza / çanta / karaborsa gibi bir panel açıkken (imleç serbest) fırça kutusu ve nişangah panelin üstüne çizilmesin
        if (PlayerInventory.IsPanelOpen || Cursor.lockState != CursorLockMode.Locked) { PromptHud.Idle(4); return; }
        if (tipStyle == null) tipStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, richText = true };
        int paint = PlayerInventory.Local.SprayCount;
        float sc = UITheme.Scale;

        // Alt orta: fırça bilgisi (köşe yığını: ses kutusu + hızlı envanterin üstünde, çakışmaz)
        string hudText = Loc.T("brush.hud", paint), throwText = Loc.T("brush.throw");
        float swatch = Mathf.Round(16f * sc);
        float maxW = Mathf.Min(Screen.width - 60f, 760f * sc);
        Vector2 s1 = PromptHud.HintSize(throwText, maxW), s2 = PromptHud.HintSize(hudText, maxW - swatch - 10f);
        float bw = Mathf.Max(s1.x, s2.x + swatch + 10f) + 28f;
        float bh = s1.y + s2.y + 14f;
        Rect box = UIUtil.HudRect(UIUtil.HudCorner.BottomCenter, UIUtil.HudOrder.Brush, bw, bh);
        UIUtil.Panel(box, PanelKind.Plaque, 0.9f);
        PromptHud.HintDraw(new Rect(box.x + 14f, box.y + 5f, bw - 28f, s1.y), throwText, UITheme.IvoryDim);
        // Boya rengi (karakter rengi) + fırça durumu
        float rowY = box.y + 5f + s1.y + 4f;
        float rowW = s2.x + swatch + 10f;
        float rx = box.center.x - rowW * 0.5f;
        Rect sw = new Rect(rx, rowY + (s2.y - swatch) * 0.5f, swatch, swatch);
        UIUtil.DrawRect(new Rect(sw.x - 1f, sw.y - 1f, sw.width + 2f, sw.height + 2f), UITheme.CharDeep);
        UIUtil.DrawRect(sw, SprayPaint.ColorOf(SprayPaint.LocalColor));
        PromptHud.HintDraw(new Rect(rx + swatch + 10f, rowY, s2.x, s2.y), hudText, UITheme.Ivory);

        // Nişangah: oyunun normal küçük beyaz noktası kalır (fırça için ayrıca pembe halka çizilmez).
        // Sadece boya topu attıktan sonra kısa bekleme süresinde ince, soluk bir halka dolar.
        Vector2 mid = new Vector2(Screen.width / 2f, Screen.height / 2f);
        float cd = nextThrow - Time.unscaledTime;
        if (cd > 0f) UIUtil.DrawRing(mid, 12f, 1f - Mathf.Clamp01(cd / PaintProjectile.Cooldown), UITheme.WithAlpha(UITheme.Ivory, 0.55f));
        bool wallTip = aimWall || hasPlane;
        if (wallTip)
        {
            // Uzun "[Sol tık basılı]" ifadesi tuş kapağına sığmayıp düz (soluk, basılı-tut çubuklu) metne dönüşüyordu: tuş ve metin ayrı verilir
            PromptHud.Show(Loc.T("brush.wall.key"), Loc.T("brush.wall.label"), PromptTone.Normal, -1f, false, 4); // kendi kanalı (maymuncuk 2'de)
            // (Sol / sağ / üst kenar yüzdesi gösteren ilerleme satırı kaldırıldı: kapı tamamlanınca kendiliğinden belirir)
        }
        else PromptHud.Idle(4);
    }
}
