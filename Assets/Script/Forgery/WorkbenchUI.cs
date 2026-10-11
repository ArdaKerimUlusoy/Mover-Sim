using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Sahte atölyesinin yerel arayüzü (sadece atölyeyi kullanan oyuncunun ekranında).
///  1) Hedef seç (müzedeki aktif vazo/tablo kaideleri)
///  2a) Vazo: kil, hedefle aynı boyda oluşur; yeşil saydam rehbere göre şekillendir → SPACE → boya
///  2b) Tablo: ekrana tuval gelir, yanında orijinal; yeşil kontur rehberi eşliğinde çiz
///  3) ESC: bitir → sahte benzerlik yüzdesiyle çantana girer (sunucu ekler)
/// </summary>
public class WorkbenchUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    private static WorkbenchUI current;

    private enum Phase { Choose, Sculpt, Paint, Canvas }

    private struct Option
    {
        public ArtworkDisplay display;
        public ArtKind kind;
        public string name;
        public int originalIndex;
    }

    private const float ShapeThreshold = 0.75f;
    private const float ShapeWeight = 0.7f;

    private ForgeryWorkbench bench;
    private GameObject player;
    private Camera cam;
    private Transform camParent;
    private Vector3 camLocalPos;
    private Quaternion camLocalRot;
    private PlayerController controller;

    private Phase phase;
    private readonly List<Option> options = new List<Option>();
    private Option target;
    private VaseLook vase;
    private PaintingLook painting;

    private ClaySculptor clay;
    private GameObject ghost, reference;
    private CanvasPainter painter;
    private float shapeScore, paintScore, scoreTimer;
    private Color[] palette;
    private int paletteIndex;

    private GUIStyle label, big, ready;

    // ---------------------------------------------------------------
    public static void Open(ForgeryWorkbench bench, PlayerInteractor player)
    {
        if (IsOpen || bench == null || player == null) return;
        var go = new GameObject("WorkbenchUI");
        current = go.AddComponent<WorkbenchUI>();
        current.Init(bench, player.gameObject);
    }

    private void Init(ForgeryWorkbench b, GameObject p)
    {
        bench = b;
        player = p;
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
        ItemDatabase db = ItemDatabase.Instance;
        MuseumSession ms = MuseumSession.Instance;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !MuseumSession.IsDisplayActive(d)) continue;
            ArtKind kind = ArtworkLook.KindOf(d.Artwork);
            if (kind == ArtKind.None) continue;
            options.Add(new Option
            {
                display = d,
                kind = kind,
                name = ms != null ? ms.ArtworkName(d) : d.Artwork.DisplayName,
                originalIndex = db != null ? db.IndexOf(d.Artwork) : -1,
            });
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
            cam.transform.SetParent(null, true);
            cam.transform.position = bench.CameraPoint.position;
        }

        Vector3 work = bench.WorkPosition;

        if (o.kind == ArtKind.Painting)
        {
            painting = ArtworkLook.Painting(seed, theme, o.display.name);
            painter = new CanvasPainter(painting.image, painting.palette);
            if (cam != null) cam.transform.LookAt(work + Vector3.up * 0.3f);
            phase = Phase.Canvas;
            return;
        }

        vase = ArtworkLook.Vase(seed, theme, o.display.name);
        palette = vase.palette;
        if (cam != null) cam.transform.LookAt(work + Vector3.up * vase.height * 0.5f);

        // Kil
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
        Material gm;
        if (sh != null) { gm = new Material(sh); gm.SetColor("_Color", new Color(0.2f, 1f, 0.35f, 1f)); }
        else gm = ArtworkLook.MakeLit(new Color(0.2f, 1f, 0.35f));
        mr.sharedMaterial = gm;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Yanına orijinalin görsel kopyası (referans)
        reference = ArtworkLook.BuildVaseVisual(vase);
        Vector3 side = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;
        reference.transform.position = work + side * (vase.maxRadius * 3.6f);

        phase = Phase.Sculpt;
        shapeScore = clay.ShapeScore(vase);
        scoreTimer = 0f;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        // Sohbet/ESC menüsü bizim ekranımızı bozmasın
        if (kb.escapeKey.wasPressedThisFrame)
        {
            Close(phase != Phase.Choose);
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
                paintScore = painter.Score;
                break;
        }

        if (ghost != null && clay != null) ghost.transform.rotation = clay.transform.rotation;
    }

    private void SelectColor(int i)
    {
        paletteIndex = Mathf.Clamp(i, 0, palette.Length - 1);
        if (clay != null) clay.brushColor = palette[paletteIndex];
    }

    private float Similarity
    {
        get
        {
            if (phase == Phase.Canvas) return painter != null ? painter.Score : 0f;
            if (clay == null) return 0f;
            return ShapeWeight * clay.ShapeScore(vase) + (1f - ShapeWeight) * clay.PaintScore(vase.pattern);
        }
    }

    /// <summary>Atölyeyi kapat. craft=true ise sahte sunucuya gönderilir.</summary>
    private void Close(bool craft)
    {
        if (craft && phase != Phase.Choose && bench != null && target.originalIndex >= 0)
        {
            byte sim = (byte)Mathf.Clamp(Mathf.RoundToInt(Similarity * 100f), 0, 100);
            bench.RequestCraft(target.originalIndex, sim);
        }

        if (cam != null && camParent != null)
        {
            cam.transform.SetParent(camParent, false);
            cam.transform.localPosition = camLocalPos;
            cam.transform.localRotation = camLocalRot;
        }
        if (clay != null) Destroy(clay.gameObject);
        if (ghost != null) Destroy(ghost);
        if (reference != null) Destroy(reference);

        if (controller != null) controller.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        IsOpen = false;
        current = null;
        Destroy(gameObject);
    }

    /// <summary>Atölyede çalışılabilecek hedef var mı? (müzede aktif vazo/tablo kaidesi)</summary>
    public static bool HasTargets()
    {
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
            if (d != null && d.Artwork != null && MuseumSession.IsDisplayActive(d) && ArtworkLook.KindOf(d.Artwork) != ArtKind.None) return true;
        return false;
    }

    private void OnDestroy()
    {
        if (current == this) { current = null; IsOpen = false; }
    }

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

        if (phase == Phase.Choose)
        {
            UIUtil.BeginCenteredPanel(540f, 120f + options.Count * 52f);
            GUILayout.Label(Loc.T("bench.choose"), UIUtil.TitleStyle);
            GUILayout.Space(8f);
            for (int i = 0; i < options.Count; i++)
            {
                Option o = options[i];
                string how = Loc.T(o.kind == ArtKind.Painting ? "bench.canvas" : "bench.wheel");
                string state = o.display.IsSwapped ? Loc.T("bench.alreadyswapped") : "";
                if (GUILayout.Button($"{i + 1}.  {o.name}  —  {how}{state}", UIUtil.ButtonStyle, GUILayout.Height(42f)))
                {
                    StartWork(o);
                    UIUtil.EndCenteredPanel();
                    return;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.T("bench.choosehint"), UIUtil.SmallStyle);
            UIUtil.EndCenteredPanel();
            return;
        }

        if (phase == Phase.Canvas)
        {
            painter.Draw(Loc.T("bench.canvastitle", painting.Name));
            DrawCancel();
            return;
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
            float sim = ShapeWeight * shapeScore + (1f - ShapeWeight) * paintScore;
            byte s = (byte)Mathf.RoundToInt(sim * 100f);
            GUILayout.Label(Loc.T("bench.similarity", s, ItemDefinition.GradeName(s)), big);
        }
        GUILayout.EndArea();
        DrawCancel();
    }

    private void DrawCancel()
    {
        if (GUI.Button(new Rect(Screen.width - 170f, Screen.height - 54f, 150f, 36f), Loc.T("bench.cancel"), UIUtil.ButtonStyle))
            Close(false);
        UIUtil.DrawPrompt(Loc.T("bench.finish"), Screen.height - 50f, new Color(1f, 0.9f, 0.5f), 15);
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
