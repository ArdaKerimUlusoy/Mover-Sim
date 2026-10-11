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
/// </summary>
public class WorkbenchUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    private static WorkbenchUI current;

    private enum Phase { Choose, Sculpt, Paint, Canvas, Bust, Jewel }

    private struct Option
    {
        public ArtworkDisplay display;
        public ArtKind kind;
        public string name;
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
    private int jewelStyle, jewelMetal, jewelCount = 1;
    private readonly int[] jewelStones = new int[JewelLook.MaxStones];

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
        if (options.Count == 0) Close(false);
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
                painter = new CanvasPainter(painting.image, painting.palette);
                if (cam != null) cam.transform.LookAt(work + Vector3.up * 0.3f);
                phase = Phase.Canvas;
                return;

            case ArtKind.Bust:
                bust = ArtworkLook.Bust(seed, theme, o.display.name);
                for (int i = 0; i < bustSliders.Length; i++) bustSliders[i] = 0.5f;
                bustStone = bustBase = 0;
                if (cam != null) cam.transform.LookAt(work + Vector3.up * 0.3f);
                // Tezgah üstünde yan yana: solda senin kalıbın, sağda orijinal
                side = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;
                previewPos = work - side * 0.28f;
                reference = ArtworkLook.BuildBustVisual(bust);
                reference.transform.position = work + side * 0.28f;
                previewDirty = true;
                phase = Phase.Bust;
                return;

            case ArtKind.Jewelry:
                jewel = ArtworkLook.Jewel(seed, theme, o.display.name);
                jewelStyle = jewelMetal = 0;
                jewelCount = 1;
                for (int i = 0; i < jewelStones.Length; i++) jewelStones[i] = 0;
                if (cam != null) cam.transform.LookAt(work + Vector3.up * 0.3f);
                side = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;
                previewPos = work - side * 0.3f;
                reference = ArtworkLook.BuildJewelVisual(jewel);
                reference.transform.position = work + side * 0.3f;
                reference.transform.localScale = Vector3.one * 2.2f; // küçük eşya: büyüteçle bakar gibi
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

        // Yeşil saydam rehber (kilin içinde kalsa da silüeti görünür)
        ghost = new GameObject("ForgeryGhost");
        ghost.transform.position = work;
        ghost.AddComponent<MeshFilter>().sharedMesh = ArtworkLook.Lathe(t => vase.Radius(t), vase.height, 40, 48);
        var mr = ghost.AddComponent<MeshRenderer>();
        Shader sh = Shader.Find("SahteSaheser/ForgeryGhost");
        if (sh != null) { ghostMaterial = new Material(sh); ghostMaterial.SetColor("_Color", new Color(0.2f, 1f, 0.35f, 1f)); }
        else ghostMaterial = ArtworkLook.MakeLit(new Color(0.2f, 1f, 0.35f));
        mr.sharedMaterial = ghostMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Yanına orijinalin görsel kopyası (referans)
        reference = ArtworkLook.BuildVaseVisual(vase);
        reference.transform.position = work + side * (vase.maxRadius * 3.6f);

        phase = Phase.Sculpt;
        shapeScore = clay.ShapeScore(vase);
        scoreTimer = 0f;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame)
        {
            // Şekil bitmeden ESC = vazgeç (boyanmamış kil çantaya girmesin)
            Close(phase != Phase.Choose && phase != Phase.Sculpt);
            return;
        }

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
        var pars = new byte[3 + JewelLook.MaxStones];
        pars[0] = (byte)jewelStyle;
        pars[1] = (byte)jewelMetal;
        pars[2] = (byte)jewelCount;
        for (int i = 0; i < JewelLook.MaxStones; i++) pars[3 + i] = (byte)jewelStones[i];
        return new ForgeryData { kind = ArtKind.Jewelry, pars = pars };
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
                case Phase.Jewel: return ForgeryScorer.Jewel(jewelStyle, jewelMetal, jewelCount, jewelStones, jewel);
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
                bench.RequestCraft(target.display, data);
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

        switch (phase)
        {
            case Phase.Choose: DrawChoose(); return;
            case Phase.Canvas:
                painter.Draw(Loc.T("bench.canvastitle", painting.Name));
                DrawFooter(true);
                return;
            case Phase.Bust: DrawBust(); DrawFooter(true); return;
            case Phase.Jewel: DrawJewel(); DrawFooter(true); return;
        }

        float w = 400f, h = phase == Phase.Paint ? 330f : 270f;
        UIUtil.DrawRect(new Rect(10, 10, w, h), new Color(0.05f, 0.06f, 0.08f, 0.85f));
        GUILayout.BeginArea(new Rect(22, 18, w - 24, h - 10));
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
            Bar(Loc.T("bench.shape"), shapeScore, -1f);
            Bar(Loc.T("bench.paintscore"), paintScore, -1f);
            SimilarityLabel();
        }
        GUILayout.EndArea();
        DrawFooter(phase == Phase.Paint);
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
            if (GUILayout.Button($"{i + 1}.  {o.name}  —  {Loc.T(MethodKey(o.kind))}{state}", UIUtil.ButtonStyle, GUILayout.Height(40f)))
                picked = o;
        }
        GUILayout.EndScrollView();
        GUILayout.FlexibleSpace();
        GUILayout.Label(Loc.T("bench.choosehint"), UIUtil.SmallStyle);
        UIUtil.EndCenteredPanel();
        if (picked.HasValue) StartWork(picked.Value);
    }

    private void DrawBust()
    {
        float w = 400f, h = 520f;
        UIUtil.DrawRect(new Rect(10, 10, w, h), new Color(0.05f, 0.06f, 0.08f, 0.85f));
        GUILayout.BeginArea(new Rect(22, 18, w - 24, h - 10));
        GUILayout.Label(bust.Name, big);
        GUILayout.Label(Loc.T("bench.moldhelp"), label);
        GUILayout.Space(4f);

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

        GUILayout.Space(6f);
        SimilarityLabel();
        GUILayout.EndArea();
    }

    private void DrawJewel()
    {
        float w = 400f, h = 500f;
        UIUtil.DrawRect(new Rect(10, 10, w, h), new Color(0.05f, 0.06f, 0.08f, 0.85f));
        GUILayout.BeginArea(new Rect(22, 18, w - 24, h - 10));
        GUILayout.Label(jewel.Name, big);
        GUILayout.Label(Loc.T("bench.jewelhelp"), label);
        GUILayout.Space(4f);

        GUILayout.Label(Loc.T("bench.body"), label);
        int style = Choices(ArtworkLook.JewelStyleNames, jewelStyle);
        if (style != jewelStyle) { jewelStyle = style; previewDirty = true; }

        GUILayout.Label(Loc.T("bench.metal"), label);
        int metal = Swatches(ArtworkLook.JewelMetals, ArtworkLook.JewelMetalNames, jewelMetal);
        if (metal != jewelMetal) { jewelMetal = metal; previewDirty = true; }

        GUILayout.Label(Loc.T("bench.stonecount"), label);
        GUILayout.BeginHorizontal();
        for (int n = 1; n <= JewelLook.MaxStones; n++)
        {
            if (GUILayout.Button(n == jewelCount ? "[" + n + "]" : n.ToString(), UIUtil.ButtonStyle, GUILayout.Width(60f), GUILayout.Height(30f)) && n != jewelCount)
            {
                jewelCount = n;
                previewDirty = true;
            }
        }
        GUILayout.EndHorizontal();

        for (int i = 0; i < jewelCount; i++)
        {
            GUILayout.Label(Loc.T("bench.gem", i + 1), label);
            int gem = Swatches(ArtworkLook.JewelGems, ArtworkLook.JewelGemNames, jewelStones[i]);
            if (gem != jewelStones[i]) { jewelStones[i] = gem; previewDirty = true; }
        }

        GUILayout.Space(6f);
        SimilarityLabel();
        GUILayout.EndArea();
    }

    /// <summary>Renkli seçim düğmeleri; seçilen indeksi döner.</summary>
    private static int Swatches(Color[] colors, string[][] names, int selected)
    {
        GUILayout.BeginHorizontal();
        for (int i = 0; i < colors.Length; i++)
        {
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

    private void SimilarityLabel()
    {
        byte s = (byte)Mathf.Clamp(Mathf.RoundToInt(Similarity * 100f), 0, 100);
        GUILayout.Label(Loc.T("bench.similarity", s, ItemDefinition.GradeName(s)), big);
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
