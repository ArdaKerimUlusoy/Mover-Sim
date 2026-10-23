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
    private Vector2 chooseScroll;

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

    private GUIStyle label, big, ready;

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
                bustStone = bustBase = 0;
                if (o.resumeId != 0 && ForgeryRegistry.TryGet(o.resumeId, out ForgeryData rb) && rb.pars.Length >= BustLook.SliderCount + 2)
                {
                    for (int i = 0; i < bustSliders.Length; i++) bustSliders[i] = rb.pars[i] / 255f;
                    bustStone = rb.pars[BustLook.SliderCount];
                    bustBase = rb.pars[BustLook.SliderCount + 1];
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
                if (previewDirty) RebuildPreview();
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
    }

    private void SelectColor(int i)
    {
        paletteIndex = Mathf.Clamp(i, 0, palette.Length - 1);
        if (clay != null) clay.brushColor = palette[paletteIndex];
    }

    private ForgeryData BustData()
    {
        var pars = new byte[BustLook.SliderCount + 2];
        for (int i = 0; i < BustLook.SliderCount; i++) pars[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(bustSliders[i]) * 255f);
        pars[BustLook.SliderCount] = (byte)bustStone;
        pars[BustLook.SliderCount + 1] = (byte)bustBase;
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
                case Phase.Bust: return ForgeryScorer.Bust(bustSliders, bustStone, bustBase, bust);
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
    private void EnsureStyles()
    {
        if (label != null) return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
        label.normal.textColor = Color.white;
        big = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
        ready = new GUIStyle(label) { fontSize = 17, fontStyle = FontStyle.Bold };
        ready.normal.textColor = new Color(0.5f, 1f, 0.55f);
    }

    private void OnGUI()
    {
        EnsureStyles();
        GUI.depth = -20; // atölye paneli oda kodu / para / çanta kutularının ÜSTÜNDE çizilsin (onlar da atölye açıkken gizlenir)

        switch (phase)
        {
            case Phase.Choose: DrawChoose(); return;
            case Phase.Canvas:
                painter.Draw(Loc.T("bench.canvastitle", painting.Name));
                DrawFooter(true);
                return;
            case Phase.Bust: DrawBust(); DrawSideFeed(); DrawFooter(true); return;
            case Phase.Jewel: DrawJewel(); DrawFooter(true); return;
        }

        float w = 400f, h = phase == Phase.Paint ? 420f : 270f;
        UIUtil.DrawRect(new Rect(10, 10, w, h), new Color(0.05f, 0.06f, 0.08f, 0.85f));
        GUILayout.BeginArea(new Rect(22, 18, w - 24, h - 22));
        GUILayout.Label(vase.Name, big);
        if (phase == Phase.Sculpt)
        {
            GUILayout.Label(Loc.T("bench.sculpt"), big);
            GUILayout.Label(Loc.T("bench.sculpthelp"), label);
            Bar(Loc.T("bench.shape"), shapeScore, ShapeThreshold);
            if (shapeScore >= ShapeThreshold) GUILayout.Label(Loc.T("bench.toPaint"), ready);
        }
        else
        {
            GUILayout.Label(Loc.T("bench.paint"), big);
            GUILayout.Label(Loc.T("bench.painthelp", palette.Length), label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < palette.Length; i++)
            {
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = palette[i];
                if (GUILayout.Button(i == paletteIndex ? "[" + (i + 1) + "]" : (i + 1).ToString(), GUILayout.Width(46), GUILayout.Height(32))) SelectColor(i);
                GUI.backgroundColor = prev;
            }
            GUILayout.EndHorizontal();
            // Fırça boyutu (vazo boyama)
            if (clay != null)
            {
                GUILayout.Label(Loc.T("bench.brushsize", clay.paintBrushSize), label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("−", GUILayout.Width(30), GUILayout.Height(22))) clay.paintBrushSize = Mathf.Clamp(clay.paintBrushSize - 2, 2, 64);
                clay.paintBrushSize = Mathf.Clamp(Mathf.RoundToInt(GUILayout.HorizontalSlider(clay.paintBrushSize, 2f, 64f, GUILayout.Height(22))), 2, 64);
                if (GUILayout.Button("+", GUILayout.Width(30), GUILayout.Height(22))) clay.paintBrushSize = Mathf.Clamp(clay.paintBrushSize + 2, 2, 64);
                GUILayout.EndHorizontal();
            }
            Bar(Loc.T("bench.shape"), shapeScore, -1f);
            Bar(Loc.T("bench.paintscore"), paintScore, -1f);
            GUILayout.FlexibleSpace();
            SimilarityLabel();
        }
        GUILayout.EndArea();
        DrawSideFeed();
        DrawFooter(phase == Phase.Paint);
    }

    /// <summary>Sağ üstte kamyon ekranı (canlı yayın), altında kat planı. Vazo ve büstte orijinal yerine bu kullanılır.</summary>
    private void DrawSideFeed()
    {
        if (!UseLiveFeed) return;
        float w = Mathf.Clamp(Screen.width * 0.3f, 260f, 520f);
        Rect feed = new Rect(Screen.width - w - 20f, 40f, w, w * 0.75f);
        LiveFeed.Draw(feed);
        float planH = Mathf.Min(w * 0.7f, Screen.height - feed.yMax - 110f);
        if (planH > 120f) FloorPlanUI.Draw(new Rect(feed.x, feed.yMax + 14f, w, planH), w >= 360f);
    }

    private void DrawChoose()
    {
        float listH = Mathf.Min(options.Count * 46f, Screen.height * 0.6f);
        UIUtil.BeginCenteredPanel(560f, 150f + listH);
        GUILayout.Label(Loc.T("bench.choose"), UIUtil.TitleStyle);
        GUILayout.Space(8f);
        chooseScroll = GUILayout.BeginScrollView(chooseScroll, GUILayout.Height(listH + 8f));
        Option? picked = null;
        for (int i = 0; i < options.Count; i++)
        {
            Option o = options[i];
            string state = o.display.IsSwapped ? Loc.T("bench.alreadyswapped") : "";
            string text = o.resumeId != 0
                ? $"{i + 1}.  {Loc.T("bench.resume", o.name, o.resumeSim)}  —  {Loc.T(MethodKey(o.kind))}{state}"
                : $"{i + 1}.  {o.name}  —  {Loc.T(MethodKey(o.kind))}{state}";
            if (GUILayout.Button(text, UIUtil.ButtonStyle, GUILayout.Height(40f)))
                picked = o;
        }
        GUILayout.EndScrollView();
        GUILayout.FlexibleSpace();
        GUILayout.Label(Loc.T("bench.choosehint"), UIUtil.SmallStyle);
        UIUtil.EndCenteredPanel();
        if (picked.HasValue) StartWork(picked.Value);
    }

    private Vector2 bustScroll;

    /// <summary>Büst paneli: ekran yüksekliğine sığar; benzerlik en üstte sabit, seçimler kaydırılabilir listede.</summary>
    private void DrawBust()
    {
        float w = 420f;
        float h = Mathf.Min(Screen.height - 90f, 640f); // altta Vazgeç / Esc yazısına yer kalsın
        UIUtil.DrawRect(new Rect(10, 10, w, h), new Color(0.05f, 0.06f, 0.08f, 0.88f));
        GUILayout.BeginArea(new Rect(22, 18, w - 24, h - 16));
        GUILayout.Label(bust.Name, big);
        GUILayout.Label(Loc.T("bench.moldhelp"), label);
        GUILayout.Space(4f);

        // Kaydırılabilir seçimler; benzerlik kutusu panelin EN ALTINDA sabit durur
        float scrollH = Mathf.Max(120f, h - 16f - 140f - SimilarityBoxHeight);
        bustScroll = GUILayout.BeginScrollView(bustScroll, false, true, GUILayout.Height(scrollH));
        for (int i = 0; i < BustLook.SliderCount; i++)
        {
            GUILayout.Label(MuseumTheme.Pick(ArtworkLook.BustSliderNames[i]), label);
            float v = GUILayout.HorizontalSlider(bustSliders[i], 0f, 1f, GUILayout.Height(18f));
            if (!Mathf.Approximately(v, bustSliders[i])) { bustSliders[i] = v; previewDirty = true; }
        }

        GUILayout.Label(Loc.T("bench.stone"), label);
        int stone = Swatches(ArtworkLook.BustStones, ArtworkLook.BustStoneNames, bustStone);
        if (stone != bustStone) { bustStone = stone; previewDirty = true; }
        GUILayout.Label(Loc.T("bench.base"), label);
        int b = Swatches(ArtworkLook.BustBases, ArtworkLook.BustBaseNames, bustBase);
        if (b != bustBase) { bustBase = b; previewDirty = true; }
        GUILayout.Space(8f);
        GUILayout.EndScrollView();
        GUILayout.FlexibleSpace();
        SimilarityLabel();
        GUILayout.EndArea();
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

    private void DrawJewel()
    {
        if (craft == null) return;
        float w = 420f;
        float h = Mathf.Min(Screen.height - 90f, 640f);
        UIUtil.DrawRect(new Rect(10, 10, w, h), new Color(0.05f, 0.06f, 0.08f, 0.88f));
        GUILayout.BeginArea(new Rect(22, 18, w - 24, h - 16));
        GUILayout.Label(jewel.Name, big);

        // Adım sekmeleri (istediğin zaman geri dönebilirsin)
        GUILayout.BeginHorizontal();
        string[] steps = { Loc.T("ring.step.cast"), Loc.T("ring.step.stones"), Loc.T("ring.step.polish") };
        for (int i = 0; i < steps.Length; i++)
        {
            string t = (i + 1) + ". " + steps[i];
            if (GUILayout.Button(i == jewelStep ? "[" + t + "]" : t, UIUtil.ButtonStyle, GUILayout.Height(32f))) { jewelStep = i; heldGem = -1; }
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(6f);

        switch (jewelStep)
        {
            case 0:
            {
                GUILayout.Label(Loc.T("ring.cast.help"), label);
                GUILayout.Label(Loc.T("ring.mold"), label);
                int style = Choices(ArtworkLook.JewelStyleNames, craft.style);
                if (style != craft.style) { craft.style = style; craft.fill = 0f; spilled = false; previewDirty = true; }
                GUILayout.Label(Loc.T("bench.metal"), label);
                int metal = Swatches(ArtworkLook.JewelMetals, ArtworkLook.JewelMetalNames, craft.metal);
                if (metal != craft.metal) { craft.metal = metal; previewDirty = true; }
                GUILayout.Space(6f);
                GUILayout.Label(Loc.T("ring.fill", Mathf.RoundToInt(Mathf.Min(craft.fill, 1f) * 100f)), label);
                if (spilled) GUILayout.Label(Loc.T("ring.spilled"), label);
                if (GUILayout.Button(Loc.T("ring.emptymold"), UIUtil.ButtonStyle, GUILayout.Height(30f))) { craft.fill = 0f; spilled = false; previewDirty = true; }
                GUILayout.Space(6f);
                if (GUILayout.Button(Loc.T("ring.next.stones"), UIUtil.ButtonStyle, GUILayout.Height(34f))) jewelStep = 1;
                break;
            }
            case 1:
            {
                GUILayout.Label(Loc.T("ring.stones.help"), label);
                GUILayout.Label(Loc.T("ring.stonecount", craft.count, JewelCraft.Max), label);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < ArtworkLook.JewelGems.Length; i++)
                {
                    if (i > 0 && i % 4 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); } // 4'lü satırlar
                    Color prev = GUI.backgroundColor;
                    GUI.backgroundColor = ArtworkLook.JewelGems[i];
                    string text = MuseumTheme.Pick(ArtworkLook.JewelGemNames[i]);
                    if (GUILayout.Button(i == heldGem ? "[" + text + "]" : text, GUILayout.Height(34f))) heldGem = i == heldGem ? -1 : i;
                    GUI.backgroundColor = prev;
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button(Loc.T("ring.removeall"), UIUtil.ButtonStyle, GUILayout.Height(30f))) { craft.count = 0; previewDirty = true; }
                GUILayout.Space(6f);
                if (GUILayout.Button(Loc.T("ring.next.polish"), UIUtil.ButtonStyle, GUILayout.Height(34f))) { jewelStep = 2; heldGem = -1; }
                break;
            }
            default:
            {
                GUILayout.Label(Loc.T("ring.polish.help"), label);
                Bar(Loc.T("ring.shine"), craft.polish, 0.9f);
                break;
            }
        }
        GUILayout.FlexibleSpace();
        SimilarityLabel();
        GUILayout.EndArea();

        DrawJewelWorkspace();
    }

    /// <summary>Sağdaki çalışma alanı: döküm kalıbı ya da büyük halka + sağ üstte orijinalin şeması.</summary>
    private void DrawJewelWorkspace()
    {
        float x0 = 450f;
        workRect = new Rect(x0, 10f, Mathf.Max(300f, Screen.width - x0 - 10f), Screen.height - 80f);
        UIUtil.DrawRect(workRect, new Color(0.03f, 0.04f, 0.06f, 0.72f));
        ringRadius = Mathf.Clamp(Mathf.Min(workRect.width * 0.24f, workRect.height * 0.28f), 70f, 190f);
        ringCenter = new Vector2(workRect.x + workRect.width * 0.42f, workRect.y + workRect.height * 0.52f);
        Color metalC = ArtworkLook.JewelMetals[Mathf.Clamp(craft.metal, 0, ArtworkLook.JewelMetals.Length - 1)];

        // Orijinalin şeması (sağ üst) — ya da kamyon ekranı (canlı yayın)
        if (UseLiveFeed)
        {
            float fw = Mathf.Clamp(workRect.width * 0.34f, 240f, 420f);
            LiveFeed.Draw(new Rect(workRect.xMax - fw - 12f, workRect.y + 34f, fw, fw * 0.75f));
        }
        else
        {
            Vector2 refC = new Vector2(workRect.xMax - 120f, workRect.y + 130f);
            GUI.Label(new Rect(refC.x - 90f, refC.y - 118f, 180f, 24f), Loc.T("bench.original"), big);
            JewelCraft orig = JewelCraft.FromLook(jewel);
            DrawRingDiagram(refC, 70f, orig, 1f);
        }

        if (jewelStep == 0)
        {
            // Kalıp: alttan dolar; beyaz çizgi bu kalıbın doğru doluluk seviyesi
            gaugeRect = new Rect(ringCenter.x - 70f, ringCenter.y - 170f, 140f, 340f);
            UIUtil.DrawRect(new Rect(gaugeRect.x - 6f, gaugeRect.y - 6f, gaugeRect.width + 12f, gaugeRect.height + 12f), new Color(0.35f, 0.3f, 0.28f));
            UIUtil.DrawRect(gaugeRect, new Color(0.08f, 0.08f, 0.09f));
            float f = Mathf.Clamp01(craft.fill);
            Color molten = Color.Lerp(new Color(1f, 0.55f, 0.15f), metalC, 0.45f);
            UIUtil.DrawRect(new Rect(gaugeRect.x, gaugeRect.yMax - gaugeRect.height * f, gaugeRect.width, gaugeRect.height * f), molten);
            float ty = gaugeRect.yMax - gaugeRect.height * JewelCraft.TargetFill(craft.style);
            UIUtil.DrawRect(new Rect(gaugeRect.x - 14f, ty - 1.5f, gaugeRect.width + 28f, 3f), Color.white);
            if (spilled)
                for (int i = 0; i < 4; i++)
                    UIUtil.DrawRect(new Rect(gaugeRect.x + 10f + i * 34f, gaugeRect.yMax + 8f, 10f, 14f + (i % 2) * 10f), molten);
            UIUtil.DrawPrompt(Loc.T("ring.pour"), gaugeRect.yMax + 40f - Screen.height * 0f, new Color(1f, 0.85f, 0.5f), 16);
            return;
        }

        DrawRingDiagram(ringCenter, ringRadius, craft, jewelStep == 2 ? craft.polish : 1f);

        if (jewelStep == 1)
        {
            Vector2 gp = GuiMouse();
            if (heldGem >= 0) DrawDot(gp, 22f, ArtworkLook.JewelGems[heldGem]);
            UIUtil.DrawPrompt(Loc.T("ring.place"), ringCenter.y + ringRadius + 50f, new Color(1f, 0.85f, 0.5f), 16);
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
            UIUtil.DrawPrompt(Loc.T("ring.rub"), ringCenter.y + ringRadius + 50f, new Color(1f, 0.85f, 0.5f), 16);
        }
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

    /// <summary>Renkli seçim düğmeleri; seçilen indeksi döner.</summary>
    private static int Swatches(Color[] colors, string[][] names, int selected)
    {
        GUILayout.BeginHorizontal();
        for (int i = 0; i < colors.Length; i++)
        {
            if (i > 0 && i % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); } // 3'lü satırlar (çok seçenek sığsın)
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = colors[i];
            string text = MuseumTheme.Pick(names[i]);
            if (GUILayout.Button(i == selected ? "[" + text + "]" : text, GUILayout.Height(30f))) selected = i;
            GUI.backgroundColor = prev;
        }
        GUILayout.EndHorizontal();
        return selected;
    }

    private static int Choices(string[][] names, int selected)
    {
        GUILayout.BeginHorizontal();
        for (int i = 0; i < names.Length; i++)
        {
            string text = MuseumTheme.Pick(names[i]);
            if (GUILayout.Button(i == selected ? "[" + text + "]" : text, UIUtil.ButtonStyle, GUILayout.Height(30f))) selected = i;
        }
        GUILayout.EndHorizontal();
        return selected;
    }

    private const float SimilarityBoxHeight = 64f;

    /// <summary>Benzerlik kutusu: sol panelin altında, renkli şeritle (kırmızı → sarı → yeşil) belirgin şekilde.</summary>
    private void SimilarityLabel()
    {
        byte s = (byte)Mathf.Clamp(Mathf.RoundToInt(Similarity * 100f), 0, 100);
        Rect r = GUILayoutUtility.GetRect(300f, SimilarityBoxHeight, GUILayout.ExpandWidth(true), GUILayout.Height(SimilarityBoxHeight));
        Color tone = s >= 85 ? new Color(0.3f, 0.85f, 0.4f) : s >= 60 ? new Color(0.95f, 0.75f, 0.25f) : new Color(0.95f, 0.4f, 0.3f);
        UIUtil.DrawRect(r, new Color(0f, 0f, 0f, 0.55f));
        UIUtil.DrawRect(new Rect(r.x, r.y, 5f, r.height), tone);
        GUI.Label(new Rect(r.x + 14f, r.y + 6f, r.width - 20f, 30f), Loc.T("bench.similarity", s, ItemDefinition.GradeName(s)), big);
        Rect bar = new Rect(r.x + 14f, r.y + 42f, r.width - 28f, 10f);
        UIUtil.DrawRect(bar, new Color(1f, 1f, 1f, 0.12f));
        UIUtil.DrawRect(new Rect(bar.x, bar.y, bar.width * (s / 100f), bar.height), tone);
    }

    /// <summary>Alt bilgi: Vazgeç düğmesi + ESC'nin ne yapacağı.</summary>
    private void DrawFooter(bool escFinishes)
    {
        if (GUI.Button(new Rect(Screen.width - 170f, Screen.height - 54f, 150f, 36f), Loc.T("bench.cancel"), UIUtil.ButtonStyle))
        {
            Close(false);
            return;
        }
        UIUtil.DrawPrompt(Loc.T(escFinishes ? "bench.finish" : "bench.sculptesc"), Screen.height - 50f, new Color(1f, 0.9f, 0.5f), 15);
    }

    private void Bar(string name, float value, float threshold)
    {
        GUILayout.Label($"{name}: %{Mathf.RoundToInt(value * 100)}", label);
        Rect r = GUILayoutUtility.GetRect(300, 14);
        UIUtil.DrawRect(r, new Color(0, 0, 0, 0.5f));
        Color fill = threshold > 0 && value >= threshold ? new Color(0.35f, 0.95f, 0.45f) : new Color(0.95f, 0.75f, 0.25f);
        UIUtil.DrawRect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), fill);
        if (threshold > 0) UIUtil.DrawRect(new Rect(r.x + r.width * threshold - 1, r.y - 2, 2, r.height + 4), Color.white);
    }
}
