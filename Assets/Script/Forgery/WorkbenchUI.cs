using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Sahte atölyesinin yerel arayüzü (sadece atölyeyi kullanan oyuncunun ekranında).
///  1) Hedef seç (müzedeki aktif kaideler: tablo, vazo, büst, yüzük)
///  2a) Vazo  : kil, hedefle aynı boyda oluşur; yeşil saydam rehbere göre şekillendir → SPACE → boya
///  2b) Tablo : ekrana tuval gelir, yanında orijinal; yeşil kontur rehberi eşliğinde çiz
///  2c) Büst  : kalıptan şekillendirme — kaydırıcılarla yont, taşını ve kaidesini seç
///  2d) Yüzük : kuyumcu tezgahı — gövde, metal ve taşları seç
///  3) ESC: bitir → yaptığın eser sunucuya gider, benzerliği sunucu hesaplar, sahte çantana girer
///  DEVAM ET: yarım kalan / boş tablo, büst ya da yüzük sahtesi çantadaysa listede en üstte "Devam et" olarak çıkar
///  (elinde tutarken tezgaha E basarsan doğrudan o açılır). Bitince eski yarım sahte çantadan çıkar, yenisi girer.
///  İtilirsen (PushSystem) Esc'ye basmış gibi kapanır: yaptığın kadarı çantaya girer, sonra devam edebilirsin.
/// </summary>
public class WorkbenchUI : MonoBehaviour
{
    /// <summary>true: orijinal eser yanda GÖSTERİLMEZ; müzedeki arkadaşın kayıt kamerasından gelen canlı yayına
    /// (kamyon ekranı, LiveFeed) bakarak çalışırsın. Yanında kat planı da durur. false: eski usul yanda orijinal.</summary>
    // Birleştirme: kayıt / monitör için BodyCam kullanılıyor (CanvasPainter.ReferenceProvider). Arkadaşın LiveFeed'i FeatureMerge ile açılır.
    public static readonly bool UseLiveFeed = FeatureMerge.FriendLiveFeed;

    public static bool IsOpen { get; private set; }
    private static WorkbenchUI current;

    /// <summary>Şu an üzerinde çalışılan müze eseri (seçim ekranındaysa null). BodyCam monitör görüntüsü için.</summary>
    public static ArtworkDisplay CurrentTarget => current != null && current.phase != Phase.Choose ? current.target.display : null;
    /// <summary>Vazo / büst / yüzük üzerinde mi çalışıyor? (Orijinal masada durmaz; monitör görüntüsü köşede çıkar.)</summary>
    public static bool IsCrafting3D => current != null && current.phase != Phase.Choose && current.phase != Phase.Canvas;

    private enum Phase { Choose, Sculpt, Paint, Canvas, Bust, Jewel }

    private struct Option
    {
        public ArtworkDisplay display;
        public ArtKind kind;
        public string name;
        public ushort resumeId;  // != 0: çantadaki yarım sahteyi sürdür (ForgeryRegistry kaydı)
        public byte resumeSim;
    }

    private const float ShapeThreshold = 0.75f;

    private ForgeryWorkbench bench;
    private Camera cam;
    private Transform camParent;
    private Vector3 camLocalPos;
    private Quaternion camLocalRot;
    private bool camMoved;
    private PlayerController controller;
    private bool restored;

    private Phase phase;
    private readonly List<Option> options = new List<Option>();
    private Option target;

    // Vazo
    private VaseLook vase;
    private ClaySculptor clay;
    private GameObject ghost;
    private Material ghostMaterial;
    private float shapeScore, paintScore, scoreTimer;
    private Color[] palette;
    private int paletteIndex;

    // Tablo
    private PaintingLook painting;
    private CanvasPainter painter;

    // Büst
    private BustLook bust;
    private readonly float[] bustSliders = new float[BustLook.SliderCount];
    private int bustStone, bustBase;
    private readonly int[] bustDetails = new int[BustLook.DetailCount]; // saç, bıyık, gözlük, kulak, sakal
    // Büst sekmeleri: 0 şekil (ölçüler), 1 detaylar, 2 boya
    private int bustTab;
    // Boya: seçili boya (0..taş sayısı-1: heykel boyası, sonrası kaide boyası), fırçanın o bölgedeki doluluğu
    private int paintPick = -1;
    private float paintCover;
    private int paintRegion = -1;      // 0 heykel, 1 kaide (şu an boyanan bölge)
    private Material sculptMat, baseMat;   // ön izlemedeki malzemeler (boyarken renk geçişi gösterilir)
    private Color sculptBase, baseBase;
    private Renderer pedestalRenderer;
    private readonly List<Renderer> sculptRenderers = new List<Renderer>();
    private bool paintHover;
    private int paintHoverRegion = -1;

    // Yüzük
    private JewelLook jewel;
    // Kuyumcu tezgahı: 0 döküm, 1 taşlar, 2 cila
    private JewelCraft craft;
    private int jewelStep;
    private int heldGem = -1;
    private bool spilled;
    private bool rubbing;
    private Vector2 lastRub;
    private float previewTimer;
    private Vector2 ringCenter;
    private float ringRadius = 150f;
    private Rect gaugeRect, workRect;
    private static readonly Dictionary<int, Texture2D> ringTextures = new Dictionary<int, Texture2D>();
    private static Texture2D dotTexture;

    // Büst / yüzük ön izlemesi ve yandaki orijinal
    private GameObject preview, reference;
    private bool previewDirty;
    private float previewYaw;
    private Vector3 previewPos;


    /// <summary>Eser türünün atölyedeki yapım yöntemi (çeviri anahtarı).</summary>
    public static string MethodKey(ArtKind kind)
    {
        switch (kind)
        {
            case ArtKind.Vase: return "bench.wheel";
            case ArtKind.Painting: return "bench.canvas";
            case ArtKind.Bust: return "bench.mold";
            case ArtKind.Jewelry: return "bench.jeweler";
            default: return "brief.noforge";
        }
    }

    // ---------------------------------------------------------------
    public static void Open(ForgeryWorkbench bench, PlayerInteractor player)
    {
        if (IsOpen || bench == null || player == null) return;
        var go = new GameObject("WorkbenchUI");
        current = go.AddComponent<WorkbenchUI>();
        current.Init(bench, player.gameObject);
    }

    /// <summary>İtildi: atölyeden Esc'ye basmış gibi çıkar (tablo / büst / yüzük / boyanmaya başlanmış vazo yarım haliyle
    /// çantaya girer; sonra tezgahta "Devam et" ile sürdürülür). Şekli bitmemiş kil kaybolur.</summary>
    public static void Interrupt()
    {
        if (current != null) current.Close(current.phase != Phase.Choose && current.phase != Phase.Sculpt);
    }

    /// <summary>Yakalanma / ışınlanma gibi durumlarda atölyeyi sahte üretmeden kapatır.</summary>
    public static void ForceClose()
    {
        if (current != null) current.Close(false);
    }

    private void Init(ForgeryWorkbench b, GameObject p)
    {
        bench = b;
        cam = p.GetComponentInChildren<Camera>();
        controller = p.GetComponent<PlayerController>();

        IsOpen = true;
        if (controller != null) controller.enabled = false; // hareket ve kamera dursun
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        BuildOptions();
        phase = Phase.Choose;
        if (options.Count == 0) { Close(false); return; }

        // Elinde yarım kalmış bir sahte varsa doğrudan onu sürdür
        PlayerInventory inv = PlayerInventory.Local;
        if (inv != null && inv.HasHeldItem)
        {
            ushort heldId = inv.GetSlot(inv.HeldSlot).ForgeryId;
            if (heldId != 0)
                foreach (Option o in options)
                    if (o.resumeId == heldId) { StartWork(o); return; }
        }
    }

    /// <summary>Çantadaki, bu görevin aktif bir eseri için yapılmış tablo / büst / yüzük sahteleri: sürdürülebilir.</summary>
    private void AddResumeOptions()
    {
        PlayerInventory inv = PlayerInventory.Local;
        MuseumSession ms = MuseumSession.Instance;
        ItemDatabase db = ItemDatabase.Instance;
        if (inv == null || ms == null || db == null) return;

        var resume = new List<Option>();
        var seen = new HashSet<ushort>();
        for (int i = 0; i < inv.SlotCount; i++)
        {
            InventorySlot s = inv.GetSlot(i);
            if (s.ForgeryId == 0 || !seen.Add(s.ForgeryId)) continue;
            ItemDefinition def = db.Get(s.ItemIndex);
            if (def == null || def.type != ItemType.Forgery) continue;
            if (!ForgeryRegistry.TryGet(s.ForgeryId, out ForgeryData data) || data == null) continue;
            if (data.kind != ArtKind.Painting && data.kind != ArtKind.Bust && data.kind != ArtKind.Jewelry) continue;
            if (data.seed != ms.Seed) continue;
            ArtworkDisplay d = ArtworkDisplay.FindByKey(s.LookKey);
            if (d == null || d.Artwork == null || !MuseumSession.IsDisplayActive(d) || d.Kind != data.kind) continue;
            resume.Add(new Option
            {
                display = d, kind = data.kind, name = ms.ArtworkName(d),
                resumeId = s.ForgeryId, resumeSim = s.HasSimilarity ? s.Similarity : (byte)0
            });
        }
        options.InsertRange(0, resume);
    }

    private void BuildOptions()
    {
        options.Clear();
        MuseumSession ms = MuseumSession.Instance;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !MuseumSession.IsDisplayActive(d)) continue;
            ArtKind kind = d.Kind;
            if (kind == ArtKind.None) continue;
            options.Add(new Option { display = d, kind = kind, name = ms != null ? ms.ArtworkName(d) : d.Artwork.DisplayName });
        }
        options.Sort((a, b) => string.CompareOrdinal(a.display.name, b.display.name));
        AddResumeOptions();
    }

    private void StartWork(Option o)
    {
        target = o;
        BenchGuide.OnWorkStart(o.kind); // türün ilk açılışında öğretici kutu (H: yeniden aç)
        MuseumSession ms = MuseumSession.Instance;
        int seed = ms != null ? ms.Seed : 0;
        int theme = ms != null ? ms.ThemeIndex : 0;

        // Kamerayı tezgaha al (oyuncudan ayır; ölçekli objelerin çocuğu yapmıyoruz)
        if (cam != null)
        {
            camParent = cam.transform.parent;
            camLocalPos = cam.transform.localPosition;
            camLocalRot = cam.transform.localRotation;
            camMoved = true;
            cam.transform.SetParent(null, true);
            cam.transform.position = bench.CameraPoint.position;
        }

        Vector3 work = bench.WorkPosition;
        Vector3 side = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;

        switch (o.kind)
        {
            case ArtKind.Painting:
                painting = ArtworkLook.Painting(seed, theme, o.display.name);
                painter = new CanvasPainter(painting) { UseLiveFeed = WorkbenchUI.UseLiveFeed };
                if (o.resumeId != 0 && ForgeryRegistry.TryGet(o.resumeId, out ForgeryData rp)) painter.LoadFrom(rp);
                if (cam != null) cam.transform.LookAt(work + Vector3.up * 0.3f);
                phase = Phase.Canvas;
                return;

            case ArtKind.Bust:
                bust = ArtworkLook.Bust(seed, theme, o.display.name);
                for (int i = 0; i < bustSliders.Length; i++) bustSliders[i] = 0.5f;
                // Yeni büst boyanmamış başlar (ham alçı + boyasız kaide): rengi oyuncu boyayarak verir
                bustStone = ArtworkLook.RawStone;
                bustBase = ArtworkLook.RawBase;
                bustTab = 0; paintPick = -1; paintCover = 0f; paintRegion = -1;
                System.Array.Clear(bustDetails, 0, bustDetails.Length);
                if (o.resumeId != 0 && ForgeryRegistry.TryGet(o.resumeId, out ForgeryData rb) && rb.pars.Length >= BustLook.SliderCount + 2)
                {
                    for (int i = 0; i < bustSliders.Length; i++) bustSliders[i] = rb.pars[i] / 255f;
                    bustStone = rb.pars[BustLook.SliderCount];
                    bustBase = rb.pars[BustLook.SliderCount + 1];
                    System.Array.Copy(BustLook.DetailsFromPars(rb.pars), bustDetails, bustDetails.Length);
                }
                if (cam != null) cam.transform.LookAt(work + Vector3.up * 0.3f);
                // Tezgah üstünde yan yana: solda senin kalıbın, sağda orijinal
                side = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;
                previewPos = work - side * 0.28f;
                if (!UseLiveFeed)
                {
                    reference = ArtworkLook.BuildBustVisual(bust);
                    reference.transform.position = work + side * 0.28f;
                }
                previewDirty = true;
                phase = Phase.Bust;
                return;

            case ArtKind.Jewelry:
                jewel = ArtworkLook.Jewel(seed, theme, o.display.name);
                craft = new JewelCraft();
                jewelStep = 0;
                heldGem = -1;
                spilled = false;
                if (o.resumeId != 0 && ForgeryRegistry.TryGet(o.resumeId, out ForgeryData rj) && JewelCraft.TryFromPars(rj.pars, out JewelCraft saved))
                {
                    craft = saved;
                    jewelStep = saved.polish > 0f ? 2 : saved.fill > 0f ? 1 : 0;
                    spilled = saved.fill > 1f;
                }
                if (cam != null) cam.transform.LookAt(work + Vector3.up * 0.3f);
                side = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;
                previewPos = work - side * 0.3f;
                if (!UseLiveFeed)
                {
                    reference = ArtworkLook.BuildJewelVisual(jewel);
                    reference.transform.position = work + side * 0.3f;
                    reference.transform.localScale = Vector3.one * 2.2f; // küçük eşya: büyüteçle bakar gibi
                }
                previewDirty = true;
                phase = Phase.Jewel;
                return;
        }

        // ---- Vazo ----
        vase = ArtworkLook.Vase(seed, theme, o.display.name);
        palette = vase.palette;
        if (cam != null) cam.transform.LookAt(work + Vector3.up * vase.height * 0.5f);
        side = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;

        var clayGo = new GameObject("Clay");
        clayGo.transform.position = work;
        clayGo.AddComponent<MeshFilter>();
        clayGo.AddComponent<MeshRenderer>();
        clayGo.AddComponent<MeshCollider>();
        clay = clayGo.AddComponent<ClaySculptor>();
        clay.cam = cam;
        clay.Setup(vase);

        // Yeşil saydam rehber kaldırıldı: vazo da monitördeki görüntüye bakarak şekillendirilir
        if (CanvasPainter.ReferenceProvider == null) {
        ghost = new GameObject("ForgeryGhost");
        ghost.transform.position = work;
        ghost.AddComponent<MeshFilter>().sharedMesh = ArtworkLook.Lathe(t => vase.Radius(t), vase.height, 40, 48);
        var mr = ghost.AddComponent<MeshRenderer>();
        Shader sh = Shader.Find("SahteSaheser/ForgeryGhost");
        if (sh != null) { ghostMaterial = new Material(sh); ghostMaterial.SetColor("_Color", new Color(0.2f, 1f, 0.35f, 1f)); }
        else ghostMaterial = ArtworkLook.MakeLit(new Color(0.2f, 1f, 0.35f));
        mr.sharedMaterial = ghostMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ghost.SetActive(false); // rehber hiç gösterilmez
        }

        // Yanına orijinalin görsel kopyası (referans)
        if (!UseLiveFeed)
        {
            reference = ArtworkLook.BuildVaseVisual(vase);
            reference.transform.position = work + side * (vase.maxRadius * 3.6f);
        }

        phase = Phase.Sculpt;
        shapeScore = clay.ShapeScore(vase);
        scoreTimer = 0f;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (UseLiveFeed && phase != Phase.Choose) LiveFeed.Request();

        // Onay diyaloğu açıkken Esc / Enter diyaloğa gider; çalışma girdileri durur
        if (confirmCancel || confirmFinish)
        {
            if (clay != null) clay.enabled = false;
            return;
        }

        // Öğretici kutu (H aç/kapat): açıkken çalışma girdileri ve Esc kutuya gider
        bool modal = phase != Phase.Choose && BenchGuide.Tick(kb);
        if (clay != null) clay.enabled = !modal; // kil de öğretici açıkken şekillenmesin
        if (modal) return;

        if (kb.escapeKey.wasPressedThisFrame)
        {
            // Şekil bitmeden ESC = vazgeç (boyanmamış kil çantaya girmesin)
            Close(phase != Phase.Choose && phase != Phase.Sculpt);
            return;
        }

        // Ressam orijinali masada görmez: kamyon monitörüne (BodyCam) bakarak yapar
        if (reference != null && reference.activeSelf && CanvasPainter.ReferenceProvider != null) reference.SetActive(false);

        switch (phase)
        {
            case Phase.Choose:
                for (int i = 0; i < options.Count && i < 9; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame) { StartWork(options[i]); return; }
                break;

            case Phase.Sculpt:
                scoreTimer -= Time.deltaTime;
                if (scoreTimer <= 0f) { scoreTimer = 0.15f; shapeScore = clay.ShapeScore(vase); }
                if (shapeScore >= ShapeThreshold && kb.spaceKey.wasPressedThisFrame)
                {
                    phase = Phase.Paint;
                    clay.mode = ClaySculptor.Mode.Paint;
                    if (ghost != null) ghost.SetActive(false);
                    SelectColor(0);
                    paintScore = clay.PaintScore(vase.pattern);
                }
                break;

            case Phase.Paint:
                for (int i = 0; i < palette.Length && i < 9; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame) SelectColor(i);
                scoreTimer -= Time.deltaTime;
                if (scoreTimer <= 0f) { scoreTimer = 0.2f; paintScore = clay.PaintScore(vase.pattern); }
                break;

            case Phase.Canvas:
                painter.Tick(Mouse.current, kb);
                break;

            case Phase.Bust:
            case Phase.Jewel:
                if (phase == Phase.Jewel) TickJewel(kb);
                if (kb.aKey.isPressed) previewYaw += 90f * Time.deltaTime;
                if (kb.dKey.isPressed) previewYaw -= 90f * Time.deltaTime;
                // Büst: sağ ya da orta tuşla sürükleyerek de çevrilir (yüzükte sağ tık taş sökmeye ayrılı)
                if (phase == Phase.Bust && Mouse.current != null && (Mouse.current.rightButton.isPressed || Mouse.current.middleButton.isPressed))
                    previewYaw -= Mouse.current.delta.ReadValue().x * 0.4f;
                if (previewDirty) RebuildPreview();
                if (phase == Phase.Bust) TickBustPaint();
                Quaternion rot = Quaternion.Euler(0f, previewYaw, 0f);
                if (preview != null) preview.transform.rotation = rot;
                if (reference != null) reference.transform.rotation = rot;
                break;
        }

        if (ghost != null && clay != null) ghost.transform.rotation = clay.transform.rotation;
    }

    private void RebuildPreview()
    {
        previewDirty = false;
        if (preview != null) Destroy(preview);
        ForgeryData d = phase == Phase.Bust ? BustData() : JewelData();
        preview = ArtworkLook.BuildForgeryVisual(d);
        if (preview == null) return;
        preview.transform.position = previewPos;
        if (phase == Phase.Jewel) preview.transform.localScale = Vector3.one * 2.2f;
        if (phase == Phase.Bust) CollectBustParts();
    }

    // ---------------------------------------------------------------
    // Büst boyama: Boya sekmesinde bir boya seç, sol tuşu basılı tutup büstün üstünde gezdir.
    // Heykel boyası heykele, kaide boyası kaideye sürülür; bölge yavaşça yeni renge döner, tamamen kaplanınca renk o olur.
    // ---------------------------------------------------------------

    /// <summary>Ön izlemedeki heykel / kaide parçaları (Bust görseli: ilk parça kaide, ikinci parça heykelin taş malzemesi).</summary>
    private void CollectBustParts()
    {
        sculptRenderers.Clear();
        pedestalRenderer = null;
        sculptMat = baseMat = null;
        if (preview == null) return;
        Transform root = preview.transform;
        // BuildForgeryVisual görseli bir köke sarabilir: parçaların asıl ebeveynini bul
        Renderer[] all = preview.GetComponentsInChildren<Renderer>();
        if (all.Length < 2) return;
        pedestalRenderer = all[0];
        baseMat = pedestalRenderer.sharedMaterial;
        sculptMat = all[1].sharedMaterial;
        foreach (Renderer r in all) if (r != pedestalRenderer && r.sharedMaterial == sculptMat) sculptRenderers.Add(r);
        if (baseMat != null) baseBase = baseMat.HasProperty("_BaseColor") ? baseMat.GetColor("_BaseColor") : baseMat.color;
        if (sculptMat != null) sculptBase = sculptMat.HasProperty("_BaseColor") ? sculptMat.GetColor("_BaseColor") : sculptMat.color;
    }

    private static int StoneCount => ArtworkLook.BustStoneChoices;
    private static int BaseCount => ArtworkLook.BustBaseChoices;
    private bool PickIsBase => paintPick >= StoneCount;
    private Color PickColor => PickIsBase ? ArtworkLook.BustBases[paintPick - StoneCount] : ArtworkLook.BustStones[Mathf.Clamp(paintPick, 0, StoneCount - 1)];

    /// <summary>Fare altındaki bölge: 0 heykel, 1 kaide, -1 hiçbiri (parçaların sınır kutularıyla).</summary>
    private int RegionUnderMouse()
    {
        if (cam == null || preview == null || Mouse.current == null) return -1;
        Vector2 mp = Mouse.current.position.ReadValue();
        Vector2 gp = new Vector2(mp.x, Screen.height - mp.y);
        if (LeftPanelRect().Contains(gp) || BenchKit.Footer().Contains(gp)) return -1;
        Ray ray = cam.ScreenPointToRay(mp);
        float best = float.MaxValue;
        int region = -1;
        if (pedestalRenderer != null && pedestalRenderer.bounds.IntersectRay(ray, out float d0)) { best = d0; region = 1; }
        foreach (Renderer r in preview.GetComponentsInChildren<Renderer>())
        {
            if (r == pedestalRenderer) continue;
            if (r.bounds.IntersectRay(ray, out float d) && d < best) { best = d; region = 0; }
        }
        return region;
    }

    private void TickBustPaint()
    {
        paintHover = false;
        paintHoverRegion = -1;
        if (bustTab != 2 || paintPick < 0 || Mouse.current == null) { ResetPaintTint(); return; }
        int region = RegionUnderMouse();
        paintHoverRegion = region;
        int want = PickIsBase ? 1 : 0;
        paintHover = region == want;
        bool holding = Mouse.current.leftButton.isPressed && paintHover;
        int target = want == 0 ? bustStone : bustBase;
        int pickIdx = PickIsBase ? paintPick - StoneCount : paintPick;
        if (!holding || pickIdx == target)
        {
            // Bırakınca yarım kalan boya yavaşça geri çekilir (fırçayı tekrar sürmek gerekir)
            if (!holding) paintCover = Mathf.MoveTowards(paintCover, 0f, Time.deltaTime * 0.6f);
            ApplyPaintTint();
            return;
        }
        if (paintRegion != want) { paintRegion = want; paintCover = 0f; }
        float moved = Mouse.current.delta.ReadValue().magnitude;
        paintCover += Time.deltaTime * 0.35f + moved / 900f; // gezdirdikçe hızlı, bekledikçe yavaş dolar
        if (paintCover >= 1f)
        {
            paintCover = 0f;
            if (want == 0) bustStone = pickIdx; else bustBase = pickIdx;
            previewDirty = true;
            GameAudio.Play2D(SoundId.UiClick, 0.5f);
            return;
        }
        ApplyPaintTint();
    }

    private void ApplyPaintTint()
    {
        if (paintPick < 0 || paintCover <= 0f) { ResetPaintTint(); return; }
        Color c = PickColor;
        if (!PickIsBase && sculptMat != null) SetMatColor(sculptMat, Color.Lerp(sculptBase, c, paintCover));
        if (PickIsBase && baseMat != null) SetMatColor(baseMat, Color.Lerp(baseBase, c, paintCover));
    }

    private void ResetPaintTint()
    {
        if (sculptMat != null) SetMatColor(sculptMat, sculptBase);
        if (baseMat != null) SetMatColor(baseMat, baseBase);
    }

    private static void SetMatColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
    }

    private void SelectColor(int i)
    {
        paletteIndex = Mathf.Clamp(i, 0, palette.Length - 1);
        if (clay != null) clay.brushColor = palette[paletteIndex];
    }

    private ForgeryData BustData()
    {
        var pars = new byte[BustLook.SliderCount + 2 + BustLook.DetailCount];
        for (int i = 0; i < BustLook.SliderCount; i++) pars[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(bustSliders[i]) * 255f);
        pars[BustLook.SliderCount] = (byte)bustStone;
        pars[BustLook.SliderCount + 1] = (byte)bustBase;
        for (int i = 0; i < BustLook.DetailCount; i++) pars[BustLook.SliderCount + 2 + i] = (byte)bustDetails[i];
        return new ForgeryData { kind = ArtKind.Bust, pars = pars };
    }

    private ForgeryData JewelData()
    {
        return new ForgeryData { kind = ArtKind.Jewelry, pars = (craft ?? new JewelCraft()).ToPars() };
    }

    /// <summary>Ekranda gösterilen tahmini benzerlik (kesin değeri sunucu hesaplar).</summary>
    private float Similarity
    {
        get
        {
            switch (phase)
            {
                case Phase.Canvas: return painter != null ? painter.Score : 0f;
                case Phase.Bust: return ForgeryScorer.Bust(bustSliders, bustStone, bustBase, bust, bustDetails);
                case Phase.Jewel: return JewelCraft.Score(craft, jewel);
                case Phase.Sculpt:
                case Phase.Paint:
                    return ForgeryScorer.VaseShapeWeight * shapeScore + (1f - ForgeryScorer.VaseShapeWeight) * paintScore;
                default: return 0f;
            }
        }
    }

    private ForgeryData BuildData()
    {
        switch (phase)
        {
            case Phase.Canvas: return painter != null ? painter.ToData() : null;
            case Phase.Paint: return clay != null ? clay.Bake(palette) : null;
            case Phase.Bust: return BustData();
            case Phase.Jewel: return JewelData();
            default: return null;
        }
    }

    /// <summary>Atölyeyi kapat. craft=true ise yapılan eser sunucuya gönderilir.</summary>
    private void Close(bool craft)
    {
        if (craft && bench != null && target.display != null)
        {
            ForgeryData data = BuildData();
            MuseumSession ms = MuseumSession.Instance;
            if (data != null && ms != null)
            {
                data.targetKey = target.display.LookKey;
                data.seed = ms.Seed;
                bench.RequestCraft(target.display, data, target.resumeId);
            }
        }

        Restore();
        Destroy(gameObject);
    }

    /// <summary>Kamerayı, kontrolü ve imleci geri verir; çalışma objelerini siler. Birden fazla çağrılabilir.</summary>
    private void Restore()
    {
        if (restored) return;
        restored = true;

        if (cam != null && camMoved && camParent != null)
        {
            cam.transform.SetParent(camParent, false);
            cam.transform.localPosition = camLocalPos;
            cam.transform.localRotation = camLocalRot;
        }
        if (clay != null) Destroy(clay.gameObject);
        if (ghost != null)
        {
            MeshFilter mf = ghost.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(ghost);
        }
        if (ghostMaterial != null) Destroy(ghostMaterial);
        if (reference != null) Destroy(reference);
        if (preview != null) Destroy(preview);
        if (painter != null) painter.Dispose();

        if (controller != null) controller.enabled = true;
        if (!SessionMenuUI.IsPaused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        IsOpen = false;
        BenchGuide.OnWorkEnd();
        if (current == this) current = null;
    }

    /// <summary>Atölyede çalışılabilecek hedef var mı? (müzede aktif, sahtesi yapılabilen kaide)</summary>
    public static bool HasTargets()
    {
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
            if (d != null && d.Artwork != null && MuseumSession.IsDisplayActive(d) && d.Kind != ArtKind.None) return true;
        return false;
    }

    private void OnDestroy() => Restore();

    // ---------------------------------------------------------------
    // Sunum: tezgah = eskiz kâğıdı notları (sol) + çalışma alanı + plaket alt çubuk. Mantık değişmez.
    // ---------------------------------------------------------------

    private bool confirmCancel, confirmFinish;   // onay diyalogları (Esc bu sırada diyaloğa gider)
    private Vector2 leftScroll;                   // sol not panelinin kaydırması
    private float leftContentH = 600f;            // önceki karedeki içerik yüksekliği (kaydırma alanı için)
    private Vector2 chooseScroll2;
    private int chooseIndex, chooseLast = -1;

    private void OnGUI()
    {
        UIUtil.ApplySkin();
        GUI.depth = -20; // atölye paneli oda kodu / para / çanta kutularının ÜSTÜNDE çizilsin (onlar da atölye açıkken gizlenir)

        // Öğretici kutu ya da onay diyaloğu açıkken altındaki arayüz tıklanamaz
        bool dialogOpen = confirmCancel || confirmFinish;
        bool modal = phase != Phase.Choose && BenchGuide.Showing;
        bool oldEnabled = GUI.enabled;
        if (modal || dialogOpen) GUI.enabled = false;
        DrawMain();
        GUI.enabled = oldEnabled;
        if (current != this) return; // DrawMain içinde kapandı
        if (dialogOpen) DrawConfirm();
        else if (modal) BenchGuide.DrawTutorial();
    }

    /// <summary>Vazgeç / Bitir onayı (Dialog: Enter onay, Esc geri).</summary>
    private void DrawConfirm()
    {
        if (confirmCancel)
        {
            DialogChoice c = UIUtil.Dialog(Loc.T("bench.cancel.title"), Loc.T("bench.cancel.body"), Loc.T("bench.cancel"), Loc.T("bench.back"), true);
            if (c == DialogChoice.Confirm) { confirmCancel = false; Close(false); }
            else if (c == DialogChoice.Cancel) confirmCancel = false;
        }
        else if (confirmFinish)
        {
            DialogChoice c = UIUtil.Dialog(Loc.T("bench.finish.title"), Loc.T("bench.finish.body"), Loc.T("bench.btn.finish"), Loc.T("bench.back"), false);
            if (c == DialogChoice.Confirm) { confirmFinish = false; Close(true); }
            else if (c == DialogChoice.Cancel) confirmFinish = false;
        }
    }

    /// <summary>Sol not paneli (eskiz kâğıdı): üst-sol, alt çubuğun üstüne kadar.</summary>
    private static Rect LeftPanelRect()
    {
        float m = BenchKit.M;
        float lw = Mathf.Clamp(Screen.width * 0.28f, 270f, 440f);
        Rect f = BenchKit.Footer();
        return new Rect(m, m, lw, Mathf.Max(200f, f.y - m * 2f));
    }

    /// <summary>Sol paneli çizer, başlığı yazar; kaydırılabilir gövde alanı ile (isteğe bağlı) alt benzerlik bloğunu döner.</summary>
    private void BeginLeft(string name, bool withSim, out Rect body, out Flow simFlow)
    {
        Rect p = LeftPanelRect();
        UIUtil.Panel(p, PanelKind.Plaque);
        float px = BenchKit.Px(22f, 18f), py = BenchKit.Px(20f, 16f);
        Rect inner = new Rect(p.x + px, p.y + py, p.width - px * 2f, p.height - py * 2f);
        var head = new Flow(inner.x, inner.y, inner.width);
        BenchKit.Heading(head, name);
        head.Gap(8f);
        float simH = withSim ? BenchKit.SimilarityHeight() + 8f : 0f;
        body = new Rect(inner.x, head.Y, inner.width, Mathf.Max(60f, inner.yMax - simH - head.Y));
        simFlow = withSim ? new Flow(inner.x, inner.yMax - simH + 8f, inner.width) : null;
        if (withSim) UIUtil.DrawRect(new Rect(inner.x, inner.yMax - simH + 2f, inner.width, 1f), new Color(1f, 1f, 1f, 0.07f));
    }

    /// <summary>
    /// Kamyon monitöründe görüntü var mı? (Müzedeki arkadaş kayıt açtı ya da eserin son kaydı var.) Monitör sistemi yoksa true.
    /// Sinyal yokken benzerlik %0 görünür; yayın gelince gerçek değer görünür (sonuç yine sunucuda gerçek benzerlikle hesaplanır).
    /// </summary>
    public static bool HasSignal => CanvasPainter.ReferenceProvider == null || CanvasPainter.ReferenceProvider(out _) != null;

    private void DrawSimBlock(Flow f)
    {
        if (f == null) return;
        int s = HasSignal ? Mathf.Clamp(Mathf.RoundToInt(Similarity * 100f), 0, 100) : 0;
        BenchKit.SimilarityBlock(f, s, -1f, null);
    }

    /// <summary>Sade tezgah: kural şeridi ve "arkadaşların" notu artık çizilmez (H ile yardım açılır).</summary>
    private void DrawBenchHints(ArtKind kind) { }

    private void DrawMain()
    {
        switch (phase)
        {
            case Phase.Choose: DrawChoose(); return;
            case Phase.Canvas:
                painter.Draw(Loc.T("bench.canvastitle", painting.Name));
                DrawFooter(true, ArtKind.Painting);
                return;
            case Phase.Bust: DrawBust(); DrawSideFeed(); DrawBenchHints(ArtKind.Bust); DrawFooter(true, ArtKind.Bust); return;
            case Phase.Jewel: DrawJewel(); DrawFooter(true, ArtKind.Jewelry); return;
        }
        DrawVase();
        DrawSideFeed();
        DrawBenchHints(ArtKind.Vase);
        DrawFooter(phase == Phase.Paint, ArtKind.Vase);
    }

    /// <summary>Vazo: sol eskiz notu — şekil adımı (kil) ve boya adımı (palet, fırça boyutu).</summary>
    private void DrawVase()
    {
        bool paint = phase == Phase.Paint;
        BeginLeft(vase.Name, paint, out Rect body, out Flow simFlow);
        DrawSimBlock(simFlow);
        float cw = body.width - 16f;
        leftScroll = GUI.BeginScrollView(body, leftScroll, new Rect(0f, 0f, cw, leftContentH));
        var f = new Flow(0f, 0f, cw);
        if (!paint)
        {
            f.Section(Loc.T("bench.sculpt"), PanelKind.Plaque);
            f.Text(Loc.T("bench.sculpthelp"), TextRole.Body, UITheme.IvoryDim);
            f.Gap(10f);
            BenchKit.Meter(f, Loc.T("bench.shape"), shapeScore, ShapeThreshold);
            if (shapeScore >= ShapeThreshold) f.Text(Loc.T("bench.toPaint"), TextRole.BodyStrong, UITheme.Sage);
        }
        else
        {
            f.Section(Loc.T("bench.paint"), PanelKind.Plaque);
            f.Text(Loc.T("bench.painthelp", palette.Length), TextRole.Body, UITheme.IvoryDim);
            f.Gap(10f);
            int pick = BenchKit.PaletteGrid(f, palette, paletteIndex, true, 4);
            if (pick >= 0) SelectColor(pick);
            f.Gap(10f);
            if (clay != null)
            {
                f.Text(Loc.T("bench.brushsize", clay.paintBrushSize), TextRole.Caption, UITheme.IvoryDim);
                f.Gap(4f);
                float v = clay.paintBrushSize;
                v = BenchKit.SizeRow(f, v, 2f, 64f, 2f);
                clay.paintBrushSize = Mathf.Clamp(Mathf.RoundToInt(v), 2, 64);
                f.Gap(10f);
            }
            BenchKit.Meter(f, Loc.T("bench.shape"), shapeScore, -1f);
            BenchKit.Meter(f, Loc.T("bench.paintscore"), paintScore, -1f);
        }
        GUI.EndScrollView();
        leftContentH = f.Y + 8f;
    }

    /// <summary>Sağ üstte kamyon ekranı (canlı yayın), altında kat planı. Vazo ve büstte orijinal yerine bu kullanılır.</summary>
    private void DrawSideFeed()
    {
        if (!UseLiveFeed) return;
        float m = BenchKit.M;
        float top = 6f + BenchGuide.NoteHeight(Mathf.Min(380f, Screen.width * 0.34f)) + 6f;
        float w = Mathf.Clamp(Screen.width * 0.3f, 240f, 520f);
        Rect feed = new Rect(Screen.width - w - m, top, w, w * 0.75f);
        LiveFeed.Draw(feed);
        float planH = Mathf.Min(w * 0.7f, BenchKit.Footer().y - feed.yMax - 120f);
        if (planH > 120f) FloorPlanUI.Draw(new Rect(feed.x, feed.yMax + 14f, w, planH), w >= 360f);
    }

    /// <summary>Hedef seçimi: koyu, sade liste (↑/↓ + Enter + Esc da çalışır).</summary>
    private void DrawChoose()
    {
        float m = BenchKit.M, sc = UITheme.Scale;
        UIUtil.Scrim(new Color(0f, 0f, 0f, 0.62f));

        float w = Mathf.Min(Mathf.Clamp(Screen.width * 0.42f, 460f, 700f), Screen.width - m * 2f);
        float pad = BenchKit.Px(24f, 18f);
        float rowH = BenchKit.Px(60f, 48f);
        float titleH = BenchKit.Px(36f, 30f);
        float btnH = Mathf.Max(32f, 38f * sc);
        float headH = pad + titleH + 10f;
        float footH = btnH + pad * 1.4f;
        float h = Mathf.Min(headH + options.Count * (rowH + 4f) + footH, Screen.height - m * 2f);
        Rect p = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        UIUtil.Panel(p, PanelKind.Plaque);

        BenchKit.FitText(new Rect(p.x + pad, p.y + pad, w - pad * 2f, titleH), Loc.T("bench.choose"), TextRole.Title, UITheme.Ivory);

        Option? picked = null;
        chooseIndex = Mathf.Clamp(UIUtil.NavigateVertical(chooseIndex, options.Count), 0, Mathf.Max(0, options.Count - 1));
        Event ev = Event.current;
        if (ev != null && ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) && options.Count > 0)
        {
            ev.Use();
            picked = options[chooseIndex];
        }

        Rect view = new Rect(p.x + pad, p.y + headH, w - pad * 2f, p.height - headH - footH);
        float step = rowH + 4f;
        float contentH = options.Count * step;
        if (chooseIndex != chooseLast)
        {
            float top = chooseIndex * step, bot = top + rowH;
            if (top < chooseScroll2.y) chooseScroll2.y = top;
            else if (bot > chooseScroll2.y + view.height) chooseScroll2.y = bot - view.height;
            chooseLast = chooseIndex;
        }
        bool scrolls = contentH > view.height;
        float rowW = view.width - (scrolls ? 16f : 0f);
        chooseScroll2 = GUI.BeginScrollView(view, chooseScroll2, new Rect(0f, 0f, rowW, contentH));
        for (int i = 0; i < options.Count; i++)
        {
            Option o = options[i];
            Rect row = new Rect(0f, i * step, rowW, rowH);
            bool hover = row.Contains(Event.current.mousePosition) && GUI.enabled;
            bool focus = i == chooseIndex;
            UIUtil.Round(row, focus || hover ? UIUtil.WellHover : UIUtil.Well, UIUtil.RadiusSmall);
            if (focus) UIUtil.RoundLine(row, new Color(1f, 1f, 1f, 0.35f), 1f, UIUtil.RadiusSmall);

            float isz = Mathf.Round(row.height * 0.46f);
            float ix = row.x + 14f;
            UIUtil.Icon(new Rect(ix, row.y + (row.height - isz) * 0.5f, isz, isz), KindIcon(o.kind), focus ? UITheme.Ivory : UITheme.IvoryDim);
            float tx = ix + isz + 14f;
            float tw = row.xMax - tx - 14f;
            string name = o.resumeId != 0 ? Loc.T("bench.resume", o.name, o.resumeSim) : o.name;
            float ty = row.y + row.height * 0.5f;
            if (o.display.IsSwapped)
            {
                string st = Loc.T("bench.alreadyswapped").Trim();
                float bw = UIUtil.Measure(st, TextRole.Caption).x + 4f;
                UIUtil.Text(new Rect(row.xMax - bw - 14f, row.y, bw, row.height), st, TextRole.Caption, UITheme.Ochre, TextAnchor.MiddleRight);
                tw -= bw + 10f;
            }
            UIUtil.Text(new Rect(tx, ty - BenchKit.Px(22f, 18f), tw, BenchKit.Px(22f, 18f)), name, TextRole.BodyStrong, UITheme.Ivory, TextAnchor.LowerLeft);
            UIUtil.Text(new Rect(tx, ty + 1f, tw, BenchKit.Px(20f, 16f)), Loc.T(MethodKey(o.kind)), TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperLeft);
            if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { picked = o; chooseIndex = i; }
        }
        GUI.EndScrollView();

        string closeT = Loc.T("bench.cancel");
        float closeW = Mathf.Max(110f, UIUtil.Measure(closeT, TextRole.Button).x + 36f);
        if (UIUtil.Button(new Rect(p.xMax - pad - closeW, p.yMax - pad - btnH, closeW, btnH), closeT, ButtonKind.Secondary))
        {
            Close(false);
            return;
        }
        if (picked.HasValue) StartWork(picked.Value);
    }

    private static IconId KindIcon(ArtKind k)
    {
        switch (k)
        {
            case ArtKind.Vase: return IconId.Vase;
            case ArtKind.Bust: return IconId.Bust;
            case ArtKind.Jewelry: return IconId.Ring;
            default: return IconId.Painting;
        }
    }

    private static string[][] Slice(string[][] a, int n)
    {
        var r = new string[Mathf.Min(n, a.Length)][];
        for (int i = 0; i < r.Length; i++) r[i] = a[i];
        return r;
    }

    private static Color[] Slice(Color[] a, int n)
    {
        var r = new Color[Mathf.Min(n, a.Length)];
        for (int i = 0; i < r.Length; i++) r[i] = a[i];
        return r;
    }

    /// <summary>Büst: üç sekme — Şekil (ölçüler), Detaylar (saç, bıyık…), Boya (boyayı seç, büstün üstünde basılı tutup sür). Altta benzerlik.</summary>
    private void DrawBust()
    {
        BeginLeft(bust.Name, true, out Rect body, out Flow simFlow);
        DrawSimBlock(simFlow);

        // Sekmeler kaydırma alanının dışında, gövdenin üstünde sabit
        float th = BenchKit.Px(34f, 30f);
        string[] tabs = { Loc.T("bench.tab.shape"), Loc.T("bench.tab.details"), Loc.T("bench.tab.paint") };
        int t = UIUtil.Tabs(new Rect(body.x, body.y, body.width, th), tabs, bustTab);
        if (t != bustTab) { bustTab = t; leftScroll = Vector2.zero; GameAudio.Play2D(SoundId.UiClick, 0.4f); }
        body = new Rect(body.x, body.y + th + 12f, body.width, Mathf.Max(40f, body.height - th - 12f));

        float cw = body.width - 16f;
        leftScroll = GUI.BeginScrollView(body, leftScroll, new Rect(0f, 0f, cw, leftContentH));
        var f = new Flow(0f, 0f, cw);
        float bh = BenchKit.Px(34f, 30f);

        switch (bustTab)
        {
            case 0: // Şekil
                f.Text(Loc.T("bench.moldhelp"), TextRole.Body, UITheme.IvoryDim);
                f.Gap(10f);
                for (int i = 0; i < BustLook.SliderCount; i++)
                {
                    f.Text(MuseumTheme.Pick(ArtworkLook.BustSliderNames[i]), TextRole.Caption, UITheme.Ivory);
                    float v = BenchKit.SliderRow(f, bustSliders[i], 0f, 1f);
                    if (!Mathf.Approximately(v, bustSliders[i])) { bustSliders[i] = v; previewDirty = true; }
                    f.Gap(6f);
                }
                f.Gap(6f);
                if (UIUtil.Button(f.Row(bh), Loc.T("bench.bustreset"), ButtonKind.Secondary))
                {
                    for (int i = 0; i < bustSliders.Length; i++) bustSliders[i] = 0.5f;
                    previewDirty = true;
                }
                break;

            case 1: // Detaylar
                for (int i = 0; i < BustLook.DetailCount; i++)
                {
                    f.Text(MuseumTheme.Pick(ArtworkLook.BustDetailTitles[i]), TextRole.Caption, UITheme.Ivory);
                    f.Gap(3f);
                    int pick = BenchKit.Choices(f, ArtworkLook.BustDetailNames[i], bustDetails[i]);
                    if (pick != bustDetails[i]) { bustDetails[i] = pick; previewDirty = true; GameAudio.Play2D(SoundId.UiClick, 0.5f); }
                    f.Gap(10f);
                }
                if (UIUtil.Button(f.Row(bh), Loc.T("bench.bustreset"), ButtonKind.Secondary))
                {
                    System.Array.Clear(bustDetails, 0, bustDetails.Length);
                    previewDirty = true;
                }
                break;

            default: // Boya
            {
                f.Text(Loc.T("bench.bustpaint.help"), TextRole.Body, UITheme.IvoryDim);
                f.Gap(10f);
                f.Section(Loc.T("bench.bustpaint.stone"), PanelKind.Plaque);
                int sp = BenchKit.SwatchPick(f, Slice(ArtworkLook.BustStones, StoneCount), Slice(ArtworkLook.BustStoneNames, StoneCount), paintPick < StoneCount ? paintPick : -1);
                if (sp >= 0) { paintPick = sp == paintPick ? -1 : sp; paintCover = 0f; }
                f.Gap(10f);
                f.Section(Loc.T("bench.bustpaint.base"), PanelKind.Plaque);
                int selB = paintPick >= StoneCount ? paintPick - StoneCount : -1;
                int bp = BenchKit.SwatchPick(f, Slice(ArtworkLook.BustBases, BaseCount), Slice(ArtworkLook.BustBaseNames, BaseCount), selB);
                if (bp >= 0) { int v = StoneCount + bp; paintPick = v == paintPick ? -1 : v; paintCover = 0f; }
                f.Gap(10f);
                if (paintPick >= 0)
                {
                    BenchKit.Meter(f, Loc.T("bench.bustpaint.cover"), paintCover, -1f);
                    if (paintHoverRegion >= 0 && !paintHover)
                        f.Text(Loc.T(PickIsBase ? "bench.bustpaint.onbase" : "bench.bustpaint.onbust"), TextRole.Caption, UITheme.Ochre);
                }
                f.Gap(6f);
                if (UIUtil.Button(f.Row(bh), Loc.T("bench.bustpaint.strip"), ButtonKind.Secondary))
                {
                    bustStone = ArtworkLook.RawStone;
                    bustBase = ArtworkLook.RawBase;
                    paintCover = 0f;
                    previewDirty = true;
                }
                break;
            }
        }
        f.Gap(10f);
        f.Text(Loc.T("bench.bustturn"), TextRole.Caption, UITheme.IvoryDim);
        GUI.EndScrollView();
        leftContentH = f.Y + 8f;
    }

    // ---------------------------------------------------------------
    // Kuyumcu tezgahı (yüzük): 1) döküm  2) taşları yerleştir  3) cila
    // ---------------------------------------------------------------

    private static Vector2 GuiMouse()
    {
        Mouse m = Mouse.current;
        if (m == null) return new Vector2(-1000f, -1000f);
        Vector2 p = m.position.ReadValue();
        return new Vector2(p.x, Screen.height - p.y);
    }

    private void TickJewel(Keyboard kb)
    {
        Mouse m = Mouse.current;
        if (m == null || craft == null) return;
        Vector2 gp = GuiMouse();
        float dt = Time.deltaTime;

        switch (jewelStep)
        {
            case 0: // Döküm: basılı tut → eriyik metal kalıba akar
            {
                bool pour = (m.leftButton.isPressed && gaugeRect.Contains(gp)) || kb.spaceKey.isPressed;
                if (pour && craft.fill < 1.25f)
                {
                    craft.fill += 0.3f * dt;
                    if (craft.fill > 1f) spilled = true;
                    MarkPreview();
                }
                break;
            }
            case 1: // Taşlar: seçili taşı halkanın üstüne tıkla; sağ tık söker
            {
                float d = Vector2.Distance(gp, ringCenter);
                if (m.leftButton.wasPressedThisFrame && heldGem >= 0 && Mathf.Abs(d - ringRadius) < 42f && craft.count < JewelCraft.Max)
                {
                    float ang = Mathf.Atan2(-(gp.y - ringCenter.y), gp.x - ringCenter.x) * Mathf.Rad2Deg;
                    craft.gems[craft.count] = heldGem;
                    craft.angles[craft.count] = Mathf.Repeat(ang, 360f);
                    craft.count++;
                    heldGem = -1;
                    GameAudio.Play2D(SoundId.UiClick, 0.6f);
                    MarkPreview();
                }
                if (m.rightButton.wasPressedThisFrame)
                {
                    int best = -1;
                    float bestD = 30f;
                    for (int i = 0; i < craft.count; i++)
                    {
                        float dd = Vector2.Distance(gp, GemPos(ringCenter, ringRadius, craft.angles[i]));
                        if (dd < bestD) { bestD = dd; best = i; }
                    }
                    if (best >= 0) RemoveGem(best);
                    else heldGem = -1;
                }
                break;
            }
            case 2: // Cila: halkanın üstünde basılı tutup ov
            {
                float d = Vector2.Distance(gp, ringCenter);
                bool onRing = Mathf.Abs(d - ringRadius) < 55f;
                if (m.leftButton.isPressed && onRing)
                {
                    if (rubbing)
                    {
                        float moved = Vector2.Distance(gp, lastRub);
                        craft.polish = Mathf.Clamp01(craft.polish + moved / (2f * Mathf.PI * ringRadius * 5f));
                        if (moved > 0.5f) MarkPreview();
                    }
                    rubbing = true;
                    lastRub = gp;
                }
                else rubbing = false;
                break;
            }
        }

        previewTimer -= dt;
    }

    /// <summary>3B ön izlemeyi en fazla saniyede ~7 kez yenile.</summary>
    private void MarkPreview()
    {
        if (previewTimer > 0f) return;
        previewTimer = 0.15f;
        previewDirty = true;
    }

    private void RemoveGem(int index)
    {
        for (int i = index; i < craft.count - 1; i++)
        {
            craft.gems[i] = craft.gems[i + 1];
            craft.angles[i] = craft.angles[i + 1];
        }
        craft.count = Mathf.Max(0, craft.count - 1);
        previewDirty = true;
    }

    private static Vector2 GemPos(Vector2 center, float radius, float angleDeg)
    {
        float r = angleDeg * Mathf.Deg2Rad;
        return center + new Vector2(Mathf.Cos(r), -Mathf.Sin(r)) * radius;
    }

    private int lastJewelStepShown = -1;

    private void DrawJewel()
    {
        if (craft == null) return;
        if (lastJewelStepShown != jewelStep) { lastJewelStepShown = jewelStep; leftScroll = Vector2.zero; }

        BeginLeft(jewel.Name, true, out Rect body, out Flow simFlow);
        DrawSimBlock(simFlow);
        float cw = body.width - 16f;
        leftScroll = GUI.BeginScrollView(body, leftScroll, new Rect(0f, 0f, cw, leftContentH));
        var f = new Flow(0f, 0f, cw);

        // Adım sekmeleri (istediğin zaman geri dönebilirsin)
        string[] steps = { Loc.T("ring.step.cast"), Loc.T("ring.step.stones"), Loc.T("ring.step.polish") };
        float bh = BenchKit.Px(34f, 30f);
        int st = UIUtil.Tabs(f.Row(bh), steps, jewelStep);
        if (st != jewelStep) { jewelStep = st; heldGem = -1; }
        f.Gap(12f);

        switch (jewelStep)
        {
            case 0:
            {
                f.Text(Loc.T("ring.cast.help"), TextRole.Body, UITheme.IvoryDim);
                f.Gap(8f);
                f.Section(Loc.T("ring.mold"), PanelKind.Plaque);
                int style = BenchKit.Choices(f, ArtworkLook.JewelStyleNames, craft.style);
                if (style != craft.style) { craft.style = style; craft.fill = 0f; spilled = false; previewDirty = true; }
                f.Gap(6f);
                f.Section(Loc.T("bench.metal"), PanelKind.Plaque);
                int metal = BenchKit.Swatches(f, ArtworkLook.JewelMetals, ArtworkLook.JewelMetalNames, craft.metal);
                if (metal != craft.metal) { craft.metal = metal; previewDirty = true; }
                f.Gap(8f);
                // Doluluk: işaret = bu kalıbın doğru doluluğu
                f.Text(Loc.T("ring.fill", Mathf.RoundToInt(Mathf.Min(craft.fill, 1f) * 100f)), TextRole.Caption, UITheme.IvoryDim);
                f.Gap(3f);
                UIUtil.ProgressBar(f.Row(BenchKit.Px(10f, 10f)), Mathf.Clamp01(craft.fill), spilled ? UITheme.Danger : UITheme.Ochre, JewelCraft.TargetFill(craft.style));
                f.Gap(6f);
                if (spilled) { f.Text(Loc.T("ring.spilled"), TextRole.BodyStrong, UITheme.Danger); f.Gap(4f); }
                if (UIUtil.Button(f.Row(bh), Loc.T("ring.emptymold"), ButtonKind.Secondary)) { craft.fill = 0f; spilled = false; previewDirty = true; }
                f.Gap(6f);
                if (UIUtil.Button(f.Row(bh), Loc.T("ring.next.stones"), ButtonKind.Secondary)) jewelStep = 1;
                break;
            }
            case 1:
            {
                f.Text(Loc.T("ring.stones.help"), TextRole.Body, UITheme.IvoryDim);
                f.Gap(6f);
                f.Text(Loc.T("ring.stonecount", craft.count, JewelCraft.Max), TextRole.BodyStrong, UITheme.Ivory);
                f.Gap(6f);
                int g = BenchKit.SwatchPick(f, ArtworkLook.JewelGems, ArtworkLook.JewelGemNames, heldGem);
                if (g >= 0) heldGem = g == heldGem ? -1 : g;
                f.Gap(8f);
                if (UIUtil.Button(f.Row(bh), Loc.T("ring.removeall"), ButtonKind.Secondary)) { craft.count = 0; previewDirty = true; }
                f.Gap(6f);
                if (UIUtil.Button(f.Row(bh), Loc.T("ring.next.polish"), ButtonKind.Secondary)) { jewelStep = 2; heldGem = -1; }
                break;
            }
            default:
            {
                f.Text(Loc.T("ring.polish.help"), TextRole.Body, UITheme.IvoryDim);
                f.Gap(10f);
                BenchKit.Meter(f, Loc.T("ring.shine"), craft.polish, 0.9f);
                break;
            }
        }
        GUI.EndScrollView();
        leftContentH = f.Y + 8f;

        DrawJewelWorkspace();
    }

    /// <summary>Sağdaki çalışma alanı: döküm kalıbı ya da büyük halka. Orijinal gösterilmez; müzedeki arkadaşlar kamerayla gösterir.</summary>
    private void DrawJewelWorkspace()
    {
        float m = BenchKit.M;
        Rect lp = LeftPanelRect();
        float x0 = lp.xMax + m;
        Rect foot = BenchKit.Footer();
        // Sağ üstteki kamyon/beden kamerası görüntüsüne yer bırak: yüzüğün nasıl olacağını müzedeki arkadaşlar gösterir
        float feedW = Mathf.Min(420f, Screen.width * 0.32f) + 32f;
        float right = Screen.width - m - (UseLiveFeed ? 0f : feedW);
        workRect = new Rect(x0, m, Mathf.Max(300f, right - x0), Mathf.Max(200f, foot.y - m * 2f));
        UIUtil.Round(workRect, new Color(0.06f, 0.06f, 0.07f, 0.72f), UIUtil.RadiusBig);

        float areaW = workRect.width;
        if (UseLiveFeed)
        {
            float fw = Mathf.Clamp(workRect.width * 0.34f, 200f, 420f);
            LiveFeed.Draw(new Rect(workRect.xMax - fw - 10f, workRect.y + 10f, fw, fw * 0.75f));
            areaW = Mathf.Max(160f, workRect.width - fw - 20f);
        }
        ringRadius = Mathf.Clamp(Mathf.Min(areaW * 0.30f, workRect.height * 0.28f), 56f, 190f);
        ringCenter = new Vector2(workRect.x + areaW * 0.5f, workRect.y + workRect.height * 0.5f);
        Color metalC = ArtworkLook.JewelMetals[Mathf.Clamp(craft.metal, 0, ArtworkLook.JewelMetals.Length - 1)];
        Color promptC = UITheme.IvoryDim;

        if (jewelStep == 0)
        {
            // Kalıp: alttan dolar; beyaz çizgi bu kalıbın doğru doluluk seviyesi
            float gh = Mathf.Min(340f, workRect.height - 120f);
            gaugeRect = new Rect(ringCenter.x - 60f, ringCenter.y - gh * 0.5f - 10f, 120f, gh);
            UIUtil.Round(new Rect(gaugeRect.x - 6f, gaugeRect.y - 6f, gaugeRect.width + 12f, gaugeRect.height + 12f), new Color(1f, 1f, 1f, 0.10f), UIUtil.RadiusSmall);
            UIUtil.DrawRect(gaugeRect, new Color(0.03f, 0.03f, 0.04f, 1f));
            float fl = Mathf.Clamp01(craft.fill);
            Color molten = Color.Lerp(new Color(1f, 0.55f, 0.15f), metalC, 0.45f); // eriyik metal (oyun verisi rengi)
            UIUtil.DrawRect(new Rect(gaugeRect.x, gaugeRect.yMax - gaugeRect.height * fl, gaugeRect.width, gaugeRect.height * fl), molten);
            float ty = gaugeRect.yMax - gaugeRect.height * JewelCraft.TargetFill(craft.style);
            UIUtil.DrawRect(new Rect(gaugeRect.x - 14f, ty - 1.5f, gaugeRect.width + 28f, 3f), UITheme.Ivory);
            if (spilled)
                for (int i = 0; i < 4; i++)
                    UIUtil.DrawRect(new Rect(gaugeRect.x + 10f + i * 26f, gaugeRect.yMax + 8f, 10f, 14f + (i % 2) * 10f), molten);
            WorkPrompt(Loc.T("ring.pour"), gaugeRect.yMax + 34f, areaW, promptC);
            return;
        }

        DrawRingDiagram(ringCenter, ringRadius, craft, jewelStep == 2 ? craft.polish : 1f);

        if (jewelStep == 1)
        {
            Vector2 gp = GuiMouse();
            if (heldGem >= 0) DrawDot(gp, 22f, ArtworkLook.JewelGems[heldGem]);
            WorkPrompt(Loc.T("ring.place"), ringCenter.y + ringRadius + 50f, areaW, promptC);
        }
        else if (jewelStep == 2)
        {
            // Parladıkça yıldızlar
            if (craft.polish > 0.5f)
            {
                float t = Time.unscaledTime;
                for (int i = 0; i < 6; i++)
                {
                    float a = t * 0.7f + i * 1.05f;
                    Vector2 p = ringCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a * 1.3f)) * ringRadius;
                    float s = 6f + 6f * Mathf.Abs(Mathf.Sin(t * 3f + i)) * craft.polish;
                    DrawDot(p, s, new Color(1f, 1f, 1f, 0.8f * craft.polish));
                }
            }
            WorkPrompt(Loc.T("ring.rub"), ringCenter.y + ringRadius + 50f, areaW, promptC);
        }
    }

    /// <summary>Çalışma alanının altında, ortalanmış kısa yönerge (alan genişliğine sığar, kırpılmaz).</summary>
    private void WorkPrompt(string text, float y, float areaW, Color c)
    {
        float maxY = workRect.yMax - 8f;
        float h = UIUtil.MeasureHeight(text, TextRole.BodyStrong, areaW - 24f);
        y = Mathf.Min(y, maxY - h);
        BenchKit.Wrapped(new Rect(workRect.x + 12f, y, areaW - 24f, h), text, TextRole.BodyStrong, c, TextAnchor.UpperCenter);
    }

    /// <summary>Halkayı (kalınlık = döküm), mühür tablasını ve taşları 2B çizer. shine: 0 mat/donuk … 1 parlak.</summary>
    private static void DrawRingDiagram(Vector2 c, float r, JewelCraft j, float shine)
    {
        int style = Mathf.Clamp(j.style, 0, 2);
        float thick = r * (style == 1 ? 0.24f : style == 2 ? 0.18f : 0.12f) * Mathf.Clamp(j.fill / JewelCraft.TargetFill(style), 0.15f, 1.7f);
        if (j.fill <= 0.01f) thick = r * 0.02f;
        Color metalC = ArtworkLook.JewelMetals[Mathf.Clamp(j.metal, 0, ArtworkLook.JewelMetals.Length - 1)];
        Color col = Color.Lerp(metalC * 0.55f, metalC, Mathf.Clamp01(shine));
        col.a = 1f;

        float outer = r + thick * 0.5f, inner = Mathf.Max(1f, r - thick * 0.5f);
        Texture2D ring = RingTexture(inner / outer);
        Color old = GUI.color;
        GUI.color = col;
        GUI.DrawTexture(new Rect(c.x - outer, c.y - outer, outer * 2f, outer * 2f), ring);
        if (style == 2) UIUtil.DrawRect(new Rect(c.x - r * 0.42f, c.y - outer - r * 0.12f, r * 0.84f, r * 0.16f), col * 0.92f);
        GUI.color = old;

        for (int i = 0; i < j.count; i++)
        {
            int gem = Mathf.Clamp(j.gems[i], 0, ArtworkLook.JewelGems.Length - 1);
            DrawDot(GemPos(c, outer + r * 0.06f, j.angles[i]), Mathf.Max(10f, r * 0.16f), ArtworkLook.JewelGems[gem]);
        }
    }

    private static void DrawDot(Vector2 p, float size, Color c)
    {
        if (dotTexture == null) dotTexture = MakeRingTexture(0f);
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.5f * c.a);
        GUI.DrawTexture(new Rect(p.x - size * 0.5f - 2f, p.y - size * 0.5f - 2f, size + 4f, size + 4f), dotTexture);
        GUI.color = c;
        GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), dotTexture);
        GUI.color = old;
    }

    private static Texture2D RingTexture(float innerRatio)
    {
        int key = Mathf.RoundToInt(Mathf.Clamp01(innerRatio) * 50f);
        if (ringTextures.TryGetValue(key, out Texture2D t) && t != null) return t;
        t = MakeRingTexture(key / 50f);
        ringTextures[key] = t;
        return t;
    }

    /// <summary>Beyaz halka dokusu (innerRatio = 0 ise dolu daire), kenarları yumuşak.</summary>
    private static Texture2D MakeRingTexture(float innerRatio)
    {
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "RingUI" };
        var px = new Color[N * N];
        float half = N * 0.5f;
        float innerPx = innerRatio * half;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
                float a = Mathf.Clamp01(half - d) * (innerRatio > 0f ? Mathf.Clamp01(d - innerPx) : 1f);
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>Alt köşe: yalnızca Vazgeç (onaylı) + Bitir düğmeleri (tuş ipuçları yok; H yardımı açar).</summary>
    private void DrawFooter(bool escFinishes, ArtKind kind)
    {
        Rect f = BenchKit.Footer();
        float sc = UITheme.Scale;
        float bh = Mathf.Min(f.height - 10f, Mathf.Max(30f, 40f * sc));
        float by = f.y + (f.height - bh) * 0.5f;
        float x = f.xMax;

        if (escFinishes)
        {
            string ft = Loc.T("bench.btn.finish");
            float fw = Mathf.Max(110f, UIUtil.Measure(ft, TextRole.Button).x + 52f);
            x -= fw;
            if (UIUtil.Button(new Rect(x, by, fw, bh), ft, ButtonKind.Primary, true, false, IconId.Check))
            {
                confirmFinish = true;
                return;
            }
            x -= 8f;
        }
        string ct = Loc.T("bench.cancel");
        float cw = Mathf.Max(100f, UIUtil.Measure(ct, TextRole.Button).x + 36f);
        x -= cw;
        if (UIUtil.Button(new Rect(x, by, cw, bh), ct, ButtonKind.Secondary))
        {
            if (phase == Phase.Sculpt) Close(false); else confirmCancel = true;
        }
    }
}

// ===================================================================
// Tezgah ortak sunum yardımcıları (WorkbenchUI / CanvasPainter / BenchGuide)
// ===================================================================

/// <summary>Dikey akış imleci: yazı / satır ekledikçe Y ilerler (OnGUI içinde, hesaplanmış yükseklikle).</summary>
internal sealed class Flow
{
    public float X, Y, W;
    public Flow(float x, float y, float w) { X = x; Y = y; W = w; }

    /// <summary>Sarılı yazı; harcanan yüksekliği döner.</summary>
    public float Text(string text, TextRole role, Color color, bool wrap = true)
    {
        GUIStyle s = UITheme.Style(role, color, TextAnchor.UpperLeft);
        bool old = s.wordWrap;
        s.wordWrap = wrap;
        float h = wrap ? s.CalcHeight(new GUIContent(text), W) : s.CalcSize(new GUIContent(text)).y;
        GUI.Label(new Rect(X, Y, W, h), text, s);
        s.wordWrap = old;
        Y += h;
        return h;
    }

    public void Gap(float h) { Y += h; }
    public Rect Row(float h) { Rect r = new Rect(X, Y, W, h); Y += h; return r; }
    public void Section(string text, PanelKind on) { Y += UIUtil.SectionHeader(new Rect(X, Y, W, 0f), text, PanelKind.Plaque) + 2f; } // tezgah panelleri koyu
}

internal static class BenchKit
{
    /// <summary>Yerleşim ölçeği: yükseklik VE genişliğe göre (dar Game view'da küçülür).</summary>
    public static float Ls => Mathf.Clamp(Mathf.Min(Screen.height / 1080f, Screen.width / 1500f), 0.6f, 1.5f);
    public static float M => Mathf.Max(8f, Mathf.Round(14f * Ls));
    public static float Px(float v, float min) => Mathf.Max(min, Mathf.Round(v * Ls));
    public static float FooterH => Mathf.Max(46f, Mathf.Round(56f * Ls));

    public static Rect Footer()
    {
        float m = M, h = FooterH;
        return new Rect(m, Screen.height - m - h, Screen.width - m * 2f, h);
    }

    /// <summary>Sarılı yazı (verilen Rect içinde).</summary>
    public static void Wrapped(Rect r, string text, TextRole role, Color color, TextAnchor anchor = TextAnchor.UpperLeft)
    {
        GUIStyle s = UITheme.Style(role, color, anchor);
        bool old = s.wordWrap;
        s.wordWrap = true;
        GUI.Label(r, text, s);
        s.wordWrap = old;
    }

    /// <summary>Tek satır sığıyorsa verilen rolde, sığmıyorsa küçük rolde sarılı yazar.</summary>
    public static void FitText(Rect r, string text, TextRole role, Color color)
    {
        float tw = UIUtil.Measure(text, role).x;
        if (tw <= r.width) UIUtil.Text(r, text, role, color, TextAnchor.MiddleLeft);
        else Wrapped(r, text, TextRole.BodyStrong, color, TextAnchor.MiddleLeft);
    }

    /// <summary>Panel başlığı (Title; sığmazsa BodyStrong sarılı).</summary>
    public static void Heading(Flow f, string text)
    {
        float tw = UIUtil.Measure(text, TextRole.Title).x;
        if (tw <= f.W) f.Text(text, TextRole.Title, UITheme.Ivory, false);
        else f.Text(text, TextRole.BodyStrong, UITheme.Ivory, true);
    }

    /// <summary>Alt çubuk ipucu: KeyCap + etiket; sığmazsa atlanır.</summary>
    public static void Hint(ref float x, float maxX, float y, float h, string key, string label)
    {
        float kw = UIUtil.KeyCapWidth(key);
        float lw = UIUtil.Measure(label, TextRole.Caption).x + 6f;
        if (x + kw + 6f + lw > maxX) return;
        UIUtil.KeyCap(new Rect(x, y, kw, h), key);
        GUIStyle s = UITheme.Style(TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleLeft);
        bool old = s.wordWrap; s.wordWrap = false;
        GUI.Label(new Rect(x + kw + 6f, y, lw + 4f, h), label, s);
        s.wordWrap = old;
        x += kw + 6f + lw + 16f;
    }

    // ---------------- Benzerlik ----------------

    public static float SimilarityHeight() => 26f * UITheme.Scale + Px(30f, 26f) + Px(10f, 10f) + 6f;

    /// <summary>Benzerlik bloğu (koyu panel): Section + büyük % + derece + ince çubuk. pct &lt; 0 ise %0 gösterilir.</summary>
    public static void SimilarityBlock(Flow f, int pct, float marker, string extra)
    {
        f.Section(Loc.T("bench.simtitle"), PanelKind.Sketch);
        Rect row = f.Row(Px(30f, 26f));
        if (pct < 0) pct = 0;
        string big = Loc.T("bench.pct", pct);
        float bw = UIUtil.Measure(big, TextRole.Numeric).x + 6f;
        UIUtil.Text(new Rect(row.x, row.y, bw, row.height), big, TextRole.Numeric, UITheme.Ivory);
        string grade = ItemDefinition.GradeName((byte)pct);
        UIUtil.Text(new Rect(row.x + bw + 6f, row.y, row.width - bw - 6f, row.height), grade, TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleRight);
        Rect bar = f.Row(Px(6f, 6f));
        float r = pct < 0 ? 0f : pct / 100f;
        Color tone = pct >= 85 ? UITheme.Sage : pct >= 60 ? UITheme.Ochre : UITheme.Danger;
        UIUtil.ProgressBar(bar, r, tone, marker);
        if (!string.IsNullOrEmpty(extra)) { f.Gap(4f); f.Text(extra, TextRole.Caption, UITheme.IvoryDim); }
        f.Gap(6f);
    }

    /// <summary>Etiket + ince ilerleme çubuğu (eşik işaretli).</summary>
    public static void Meter(Flow f, string name, float value, float threshold)
    {
        f.Text($"{name}: %{Mathf.RoundToInt(value * 100f)}", TextRole.Caption, UITheme.IvoryDim);
        f.Gap(3f);
        bool ok = threshold > 0f && value >= threshold;
        UIUtil.ProgressBar(f.Row(Px(6f, 6f)), value, ok ? UITheme.Sage : UITheme.Ochre, threshold > 0f ? threshold : -1f);
        f.Gap(8f);
    }

    // ---------------- Kontroller ----------------

    /// <summary>Temalı yatay kaydırıcı satırı.</summary>
    public static float SliderRow(Flow f, float v, float min, float max)
    {
        Rect r = f.Row(Px(20f, 18f));
        return GUI.HorizontalSlider(new Rect(r.x, r.y + 2f, r.width, r.height - 4f), v, min, max);
    }

    /// <summary>[−] kaydırıcı [+] satırı (adım: step).</summary>
    public static float SizeRow(Flow f, float v, float min, float max, float step)
    {
        float b = Px(28f, 26f);
        Rect r = f.Row(b);
        if (UIUtil.Button(new Rect(r.x, r.y, b, b), "−", ButtonKind.Tool)) v = Mathf.Clamp(v - step, min, max);
        if (UIUtil.Button(new Rect(r.xMax - b, r.y, b, b), "+", ButtonKind.Tool)) v = Mathf.Clamp(v + step, min, max);
        v = GUI.HorizontalSlider(new Rect(r.x + b + 8f, r.y + (b - 14f) * 0.5f, r.width - b * 2f - 16f, 14f), v, min, max);
        return v;
    }

    /// <summary>Boya lekesi kutuları (numaralı). Tıklanan indeksi döner (-1: yok).</summary>
    public static int PaletteGrid(Flow f, Color[] pal, int selected, bool showSelected, int cols)
    {
        float gap = 6f;
        float bs = Mathf.Min(Px(40f, 30f), (f.W - gap * (cols - 1)) / cols);
        int rows = (pal.Length + cols - 1) / cols;
        int clicked = -1;
        for (int i = 0; i < pal.Length; i++)
        {
            Rect b = new Rect(f.X + (i % cols) * (bs + gap) + 5f, f.Y + (i / cols) * (bs + gap) + 5f, bs - 4f, bs - 4f);
            if (UIUtil.Swatch(b, pal[i], showSelected && i == selected)) clicked = i;
        }
        f.Y += rows * (bs + gap) + 4f;
        return clicked;
    }

    /// <summary>Yuva (Slot) biçiminde seçenek ızgarası; renk çipi olabilir. Tıklanan indeksi döner (-1: yok).</summary>
    public static int OptionGrid(Flow f, string[] labels, Color[] chips, int selected, int cols)
    {
        float gap = 6f, ch = Px(32f, 30f);
        float cw = (f.W - gap * (cols - 1)) / cols;
        int rows = (labels.Length + cols - 1) / cols;
        int clicked = -1;
        Event e = Event.current;
        for (int i = 0; i < labels.Length; i++)
        {
            Rect c = new Rect(f.X + (i % cols) * (cw + gap), f.Y + (i / cols) * (ch + gap), cw, ch);
            bool sel = i == selected;
            bool hover = GUI.enabled && e != null && c.Contains(e.mousePosition);
            UIUtil.Slot(c, sel ? SlotState.Selected : hover ? SlotState.Hover : SlotState.Filled);
            float tx = c.x + 10f;
            if (chips != null)
            {
                Rect chip = new Rect(c.x + 10f, c.y + (c.height - 14f) * 0.5f, 14f, 14f);
                UIUtil.Round(chip, chips[i], 3f);
                tx = chip.xMax + 6f;
            }
            GUIStyle s = UITheme.Style(TextRole.Caption, sel ? Color.white : UITheme.IvoryDim, chips != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            bool old = s.wordWrap; s.wordWrap = true;
            GUI.Label(new Rect(tx, c.y, c.xMax - tx - 8f, c.height), labels[i], s);
            s.wordWrap = old;
            if (GUI.Button(c, GUIContent.none, GUIStyle.none)) clicked = i;
        }
        f.Y += rows * (ch + gap);
        return clicked;
    }

    private static string[] Names(string[][] names)
    {
        var a = new string[names.Length];
        for (int i = 0; i < a.Length; i++) a[i] = MuseumTheme.Pick(names[i]);
        return a;
    }

    /// <summary>Renk çipli seçim; seçilen indeksi döner.</summary>
    public static int Swatches(Flow f, Color[] colors, string[][] names, int selected)
    {
        int c = OptionGrid(f, Names(names), colors, selected, 2);
        return c >= 0 ? c : selected;
    }

    /// <summary>Renk çipli seçim; tıklanan indeksi döner (-1: yok) — çağıran açma/kapama yapar.</summary>
    public static int SwatchPick(Flow f, Color[] colors, string[][] names, int selected)
        => OptionGrid(f, Names(names), colors, selected, 2);

    /// <summary>Düz seçenekler (az sayıda: tek satır, çoksa 2 sütun).</summary>
    public static int Choices(Flow f, string[][] names, int selected)
    {
        int cols = names.Length <= 3 ? names.Length : 2;
        int c = OptionGrid(f, Names(names), null, selected, Mathf.Max(1, cols));
        return c >= 0 ? c : selected;
    }
}
