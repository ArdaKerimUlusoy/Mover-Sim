using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tezgahta tablo sahtesi çizmek için ekran tuvali.
/// Solda tuval, sağda referans: kamyon monitörü (BodyCam — müzedeki arkadaşın kaydı); BodyCam yoksa canlı yayın
/// (LiveFeed, arkadaşın sistemi; FeatureMerge ile açılır) ya da eski usul orijinal. Yeşil rehber kaldırıldı.
///   Sol tık sürükle : fırça
///   Shift + sol tık : kova (alanı doldur)
///   Tekerlek        : fırça boyutu
///   1-7             : renk
///   B               : tüm tuvali seçili renkle doldur
///   E               : silgi aç/kapat (tuvalin başlangıç rengiyle boyar)
///   R               : fırça şekli (yuvarlak / kare / yatay / dikey çizgi)
///   Ctrl+Z, Z       : son darbeyi geri al (en çok 20 adım)
/// </summary>
public class CanvasPainter
{
    public const int W = 256, H = 192;

    public float Score { get; private set; }
    /// <summary>Bitmiş önemli bölge sayısı / toplam (gökyüzü, deniz, tekne, bulut...).</summary>
    public int RegionsDone { get; private set; }
    public int RegionsTotal { get; private set; }
    public bool ShowGuide = false; // yeşil rehber kaldırıldı (her zaman kapalı)
    public int BrushSize = 8;
    /// <summary>Fırça şekilleri (R tuşu ya da araç çubuğu).</summary>
    public enum BrushShape { Round, Square, HLine, VLine }
    public BrushShape Shape { get; private set; } = BrushShape.Round;
    /// <summary>true: silgi — tuvalin başlangıç rengiyle boyar.</summary>
    public bool Eraser { get; private set; }
    private const int MaxUndo = 20;
    private readonly List<Color[]> undo = new List<Color[]>(MaxUndo + 1);
    private readonly Color[] grid = new Color[PaintingRegions.GW * PaintingRegions.GH]; // skor için küçültülmüş tuval
    /// <summary>true: orijinal yerine canlı yayın (kamyon ekranı) gösterilir.</summary>
    public bool UseLiveFeed = true;
    public int PaletteIndex { get; private set; }

    private readonly Texture2D reference;
    private readonly PaintingLook look;
    private readonly Texture2D canvas;
    private readonly Texture2D guide;
    private readonly Color[] px;
    private readonly Color[] palette;
    private Vector2? lastPix;
    private float scoreTimer;
    private bool dirty;

    private Rect canvasRect;

    public static readonly Color CanvasBase = new Color(0.96f, 0.93f, 0.85f);

    /// <summary>Orijinalin yerine gösterilecek görüntü (BodyCam: kamyon monitörü — canlı yayın ya da son kayıt).
    /// Atanmışsa ressam orijinali doğrudan görmez; null dönerse "SİNYAL YOK".</summary>
    public delegate Texture ReferenceSource(out string label);
    public static ReferenceSource ReferenceProvider;
    /// <summary>Tuvalin altındaki boş alana ek panel çizer (BodyCam: kat planı).</summary>
    public static System.Action<Rect> ExtraPanelDrawer;

    public CanvasPainter(PaintingLook look)
    {
        this.look = look;
        this.reference = look.image;
        this.palette = look.palette;

        canvas = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SahteTuval" };
        px = new Color[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = CanvasBase;
        canvas.SetPixels(px);
        canvas.Apply();

        guide = null; // rehber yok: monitöre bakarak çizilir
        Score = ComputeScore();
    }

    /// <summary>Referanstaki renk sınırlarından yeşil kontur dokusu üretir.</summary>
    static Texture2D BuildGuide(Texture2D reference)
    {
        int w = reference.width, h = reference.height;
        var src = reference.GetPixels();
        var dst = new Color[w * h];
        Color line = new Color(0.2f, 1f, 0.35f, 0.9f);
        Color clear = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = src[y * w + x];
                bool edge = false;
                if (x + 1 < w && Diff(c, src[y * w + x + 1]) > 0.08f) edge = true;
                if (y + 1 < h && Diff(c, src[(y + 1) * w + x]) > 0.08f) edge = true;
                dst[y * w + x] = edge ? line : clear;
            }
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "TabloRehberi" };
        t.SetPixels(dst);
        t.Apply();
        return t;
    }

    static float Diff(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

    // Yerleşim: [araç çubuğu] [tuval + altında ek panel] [sağda eskiz kâğıdı panosu: referans, benzerlik, palet, fırça].
    // Yalnızca ekran boyutuna bağlı hesap (metin ölçmez): Tick (Update) içinden de güvenle çağrılır.
    private Rect toolRect, sideRect, extraRect, titleRect, noteRect, rulesRect;
    private float toolBtn, toolY;
    private string hoverTip;

    /// <summary>Geri alınabilir adım sayısı (alt çubuk ipucu için).</summary>
    public int UndoCount => undo.Count;

    void Layout()
    {
        float sw = Screen.width, sh = Screen.height;
        float m = BenchKit.M;
        Rect foot = BenchKit.Footer();
        float topH = BenchKit.Px(44f, 36f);
        titleRect = new Rect(m, m, sw * 0.55f - m, topH);
        float noteW = Mathf.Min(380f, sw * 0.4f - m);
        noteRect = new Rect(sw - m - noteW, m, noteW, topH);
        float rulesH = BenchGuide.RulesHeight(ArtKind.Painting, sw - m * 2f);
        rulesRect = new Rect(m, m + topH + 4f, sw - m * 2f, rulesH);
        float y0 = rulesRect.yMax + m * 0.7f;
        float y1 = foot.y - m * 0.7f;
        float midH = Mathf.Max(160f, y1 - y0);

        // Sol araç çubuğu (8 düğme: fırça, silgi, geri al, doldur + 4 fırça şekli)
        const float pad = 8f, gap = 4f, sep = 10f;
        toolBtn = Mathf.Clamp((midH - pad * 2f - gap * 7f - sep) / 8f, 28f, BenchKit.Px(44f, 32f));
        float tbH = pad * 2f + toolBtn * 8f + gap * 7f + sep;
        toolRect = new Rect(m, y0, toolBtn + pad * 2f, Mathf.Min(tbH, midH));

        // Sağ panel
        float rw = Mathf.Clamp(sw * 0.27f, 210f, 440f);
        sideRect = new Rect(sw - m - rw, y0, rw, midH);

        // Orta: tuval (4:3) + altında ek panel (kat planı) yeterli yer varsa
        float ax = toolRect.xMax + m, aw = Mathf.Max(160f, sideRect.x - m - ax);
        float extraH = (ExtraPanelDrawer != null && midH >= 420f && aw >= 280f) ? Mathf.Clamp(midH * 0.28f, 110f, 220f) : 0f;
        float ah = midH - extraH;
        float cw = Mathf.Max(120f, Mathf.Min(aw - 24f, (ah - 24f) * W / H));
        float ch = cw * H / W;
        canvasRect = new Rect(ax + (aw - cw) * 0.5f, y0 + 12f, cw, ch);
        if (extraH > 0f)
        {
            float ey = canvasRect.yMax + 18f;
            float eh = y1 - ey;
            float ew = Mathf.Min(aw, Mathf.Max(cw + 20f, 260f), eh * 1.9f);
            extraRect = (eh >= 100f && ew >= 200f) ? new Rect(ax + (aw - ew) * 0.5f, ey, ew, eh) : Rect.zero;
        }
        else extraRect = Rect.zero;
    }

    public void Tick(Mouse mouse, Keyboard kb)
    {
        Layout();
        if (UseLiveFeed) LiveFeed.Request();
        if (kb != null)
        {
            for (int i = 0; i < palette.Length && i < 9; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame) { PaletteIndex = i; Eraser = false; }
            if (kb.eKey.wasPressedThisFrame) Eraser = !Eraser;
            if (kb.rKey.wasPressedThisFrame) Shape = (BrushShape)(((int)Shape + 1) % 4);
            if (kb.zKey.wasPressedThisFrame) Undo(); // Ctrl+Z de zKey'i tetikler
            // Fırça boyutu: [ ] ya da - + tuşları (tekerleğe ek olarak)
            if (kb.leftBracketKey.wasPressedThisFrame || kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)
                BrushSize = Mathf.Clamp(BrushSize - 1, 1, 30);
            if (kb.rightBracketKey.wasPressedThisFrame || kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)
                BrushSize = Mathf.Clamp(BrushSize + 1, 1, 30);
            if (kb.bKey.wasPressedThisFrame) { PushUndo(); for (int i = 0; i < px.Length; i++) px[i] = CurColor(); dirty = true; }
        }

        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) BrushSize = Mathf.Clamp(BrushSize + (scroll > 0 ? 1 : -1), 1, 30);

            Vector2 m = mouse.position.ReadValue();
            Vector2 gp = new Vector2(m.x, Screen.height - m.y);
            bool inside = canvasRect.Contains(gp);
            bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

            if (inside && mouse.leftButton.isPressed)
            {
                Vector2 p = ToPixel(gp);
                if (shift)
                {
                    if (mouse.leftButton.wasPressedThisFrame) { PushUndo(); FloodFill((int)p.x, (int)p.y, CurColor()); }
                }
                else
                {
                    if (lastPix.HasValue) StrokeLine(lastPix.Value, p);
                    else { PushUndo(); Stamp((int)p.x, (int)p.y); } // darbe başı: geri alma noktası
                    lastPix = p;
                }
            }
            else lastPix = null;
        }

        if (dirty)
        {
            canvas.SetPixels(px);
            canvas.Apply();
            dirty = false;
        }

        scoreTimer -= Time.deltaTime;
        if (scoreTimer <= 0f) { scoreTimer = 0.2f; Score = ComputeScore(); }
    }

    Vector2 ToPixel(Vector2 gp)
    {
        float u = (gp.x - canvasRect.x) / canvasRect.width;
        float v = 1f - (gp.y - canvasRect.y) / canvasRect.height;
        return new Vector2(Mathf.Clamp(u * W, 0, W - 1), Mathf.Clamp(v * H, 0, H - 1));
    }

    void StrokeLine(Vector2 a, Vector2 b)
    {
        float dist = Vector2.Distance(a, b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(1f, BrushSize * 0.35f))); // hızlı harekette boşluk kalmasın
        for (int i = 1; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, (float)i / steps);
            Stamp((int)p.x, (int)p.y);
        }
    }

    void Stamp(int cx, int cy)
    {
        Color c = CurColor();
        int r = BrushSize;
        int rx = r, ry = r;
        if (Shape == BrushShape.HLine) ry = Mathf.Max(1, r / 4);
        else if (Shape == BrushShape.VLine) rx = Mathf.Max(1, r / 4);
        for (int y = -ry; y <= ry; y++)
        {
            int py = cy + y;
            if (py < 0 || py >= H) continue;
            for (int x = -rx; x <= rx; x++)
            {
                int pxl = cx + x;
                if (pxl < 0 || pxl >= W) continue;
                if (Shape != BrushShape.Round || x * x + y * y <= r * r) px[py * W + pxl] = c;
            }
        }
        dirty = true;
    }

    /// <summary>Aktif boya rengi (silgiyse tuvalin başlangıç rengi).</summary>
    Color CurColor() => Eraser ? CanvasBase : palette[PaletteIndex];

    /// <summary>Darbe / doldurma öncesi tuval kopyasını yığına alır (en çok MaxUndo adım).</summary>
    void PushUndo()
    {
        Color[] snap;
        if (undo.Count >= MaxUndo) { snap = undo[0]; undo.RemoveAt(0); } // en eski kopyanın dizisini yeniden kullan
        else snap = new Color[px.Length];
        System.Array.Copy(px, snap, px.Length);
        undo.Add(snap);
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        Color[] snap = undo[undo.Count - 1];
        undo.RemoveAt(undo.Count - 1);
        System.Array.Copy(snap, px, px.Length);
        dirty = true;
        lastPix = null;
    }

    void FloodFill(int sx, int sy, Color c)
    {
        Color target = px[sy * W + sx];
        if (Diff(target, c) < 0.01f) return;
        var stack = new Stack<int>();
        stack.Push(sy * W + sx);
        var visited = new bool[W * H];
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (visited[i]) continue;
            visited[i] = true;
            if (Diff(px[i], target) > 0.06f) continue;
            px[i] = c;
            int x = i % W, y = i / W;
            if (x > 0) stack.Push(i - 1);
            if (x < W - 1) stack.Push(i + 1);
            if (y > 0) stack.Push(i - W);
            if (y < H - 1) stack.Push(i + W);
        }
        dirty = true;
    }

    /// <summary>Sunucuyla aynı bölge bölge hesap (ForgeryScorer.Painting → PaintingRegions).</summary>
    float ComputeScore()
    {
        // Tuvali bölge ızgarasına (64x48) küçült: her hücre = blok ortalaması. Hafif renk farkı ve
        // 1-2 piksellik kayma ortalamada erir; en yakın palet rengi eşlemesi renk toleransını sağlar.
        int gw = PaintingRegions.GW, gh = PaintingRegions.GH;
        int bw = W / gw, bh = H / gh;
        int painted = 0;
        for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                float r = 0f, g = 0f, b = 0f;
                for (int y = 0; y < bh; y++)
                {
                    int row = (gy * bh + y) * W + gx * bw;
                    for (int x = 0; x < bw; x++) { Color c = px[row + x]; r += c.r; g += c.g; b += c.b; }
                }
                float inv = 1f / (bw * bh);
                var avg = new Color(r * inv, g * inv, b * inv, 1f);
                grid[gy * gw + gx] = avg;
                if (Diff(avg, CanvasBase) > 0.09f) painted++;
            }

        float score = PaintingRegions.Score((u, v) => grid[Mathf.Min(gh - 1, (int)(v * gh)) * gw + Mathf.Min(gw - 1, (int)(u * gw))],
                                            look, out int done, out int total);
        RegionsDone = done;
        RegionsTotal = total;
        // Boş (ya da neredeyse boş) tuval yüksek puan almasın
        float coverage = painted / (float)grid.Length;
        if (coverage < 0.04f) score *= coverage / 0.04f;
        return score;
    }

    public Texture2D Bake()
    {
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SahteTablo" };
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    // ---------------------------------------------------------------
    public void Draw(string title)
    {
        Layout();

        // Çalışma yüzeyi: koyu perde + vinyet
        UIUtil.Scrim(UITheme.WithAlpha(UITheme.CharDeep, 0.82f));
        UIUtil.Vignette();

        // Üst çubuk: başlık (sol) + arkadaş notu (sağ), altında kural şeridi
        BenchKit.FitText(titleRect, title, TextRole.Title, UITheme.Ivory);
        UIUtil.DrawRect(new Rect(titleRect.x, titleRect.yMax - 2f, 56f, 2f), UITheme.Brass);
        BenchGuide.DrawOthersNote(noteRect);
        BenchGuide.DrawRules(ArtKind.Painting, rulesRect);

        hoverTip = null;
        DrawToolbar();

        // Tuval: pirinç çerçeve içinde
        Rect frame = new Rect(canvasRect.x - 10f, canvasRect.y - 10f, canvasRect.width + 20f, canvasRect.height + 20f);
        UIUtil.DrawRect(frame, UITheme.CharDeep);
        GUI.DrawTexture(canvasRect, canvas, ScaleMode.StretchToFill, false);
        UIUtil.BrassFrame(frame);
        if (ShowGuide && guide != null) GUI.DrawTexture(canvasRect, guide, ScaleMode.StretchToFill, true);

        // Tuvalin altındaki boşluk: kat planı (BodyCam) — ressam çizerken müzedekini yönetebilsin
        if (extraRect.width > 0f)
        {
            if (ExtraPanelDrawer != null) ExtraPanelDrawer(extraRect);
            else if (UseLiveFeed && extraRect.width >= 180f) FloorPlanUI.Draw(extraRect, extraRect.width >= 300f);
        }

        DrawSidePanel();

        // Araç ipucu (fare düğmenin üstündeyken)
        if (hoverTip != null && Event.current.type == EventType.Repaint)
        {
            Vector2 mp = Event.current.mousePosition;
            float tw = UIUtil.Measure(hoverTip, TextRole.Caption).x + 20f;
            Rect tr = new Rect(toolRect.xMax + 6f, Mathf.Clamp(mp.y - 12f, toolRect.y, Screen.height - 30f), tw, 24f);
            UIUtil.Panel(tr, PanelKind.Plaque);
            UIUtil.Text(tr, hoverTip, TextRole.Caption, UITheme.Ivory, TextAnchor.MiddleCenter);
        }

        DrawBrushCursor();
    }

    /// <summary>Sol araç çubuğu: fırça / silgi / geri al / doldur, ayraç, fırça şekilleri.</summary>
    void DrawToolbar()
    {
        UIUtil.Panel(toolRect, PanelKind.Plaque);
        const float pad = 8f, gap = 4f;
        float x = toolRect.x + pad;
        toolY = toolRect.y + pad;

        if (ToolBtn(x, IconId.Brush, 0, Loc.T("bench.tool.brush"), !Eraser, true)) Eraser = false;
        if (ToolBtn(x, IconId.Eraser, 0, Loc.T("bench.tool.eraser"), Eraser, true)) Eraser = true;
        if (ToolBtn(x, IconId.Undo, 0, Loc.T("bench.undo", undo.Count), false, undo.Count > 0)) Undo();
        if (ToolBtn(x, IconId.Painting, 0, Loc.T("bench.tool.fill"), false, true)) { PushUndo(); FillAll(); }

        float sepY = toolY + 1f;
        UIUtil.DrawRect(new Rect(x + 4f, sepY + 3f, toolBtn - 8f, 1f), UITheme.WithAlpha(UITheme.Brass, 0.6f));
        toolY += 10f;

        string[] shapeKeys = { "bench.shape.round", "bench.shape.square", "bench.shape.hline", "bench.shape.vline" };
        for (int i = 0; i < 4; i++)
            if (ToolBtn(x, null, i + 1, Loc.T(shapeKeys[i]), (int)Shape == i, true)) Shape = (BrushShape)i;
    }

    /// <summary>Araç çubuğu düğmesi (simge ya da fırça şekli simgesi). Tıklandıysa true.</summary>
    bool ToolBtn(float x, IconId? icon, int glyph, string tip, bool selected, bool enabled)
    {
        Rect r = new Rect(x, toolY, toolBtn, toolBtn);
        toolY += toolBtn + 4f;
        bool click = UIUtil.Button(r, "", ButtonKind.Tool, enabled, false, icon, selected);
        if (glyph > 0 && Event.current.type == EventType.Repaint)
        {
            Color c = !enabled ? UITheme.Graphite : selected ? UITheme.Charcoal : UITheme.IvoryDim;
            float s = toolBtn * 0.46f;
            Vector2 ctr = r.center;
            if (glyph == 1) GUI.DrawTexture(new Rect(ctr.x - s * 0.5f, ctr.y - s * 0.5f, s, s), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, 0, s * 0.5f);
            else if (glyph == 2) UIUtil.DrawRect(new Rect(ctr.x - s * 0.5f, ctr.y - s * 0.5f, s, s), c);
            else if (glyph == 3) UIUtil.DrawRect(new Rect(ctr.x - s * 0.7f, ctr.y - s * 0.14f, s * 1.4f, s * 0.28f), c);
            else UIUtil.DrawRect(new Rect(ctr.x - s * 0.14f, ctr.y - s * 0.7f, s * 0.28f, s * 1.4f), c);
        }
        if (enabled && Event.current != null && r.Contains(Event.current.mousePosition)) hoverTip = tip;
        return click;
    }

    /// <summary>Tüm tuvali seçili renkle doldurur (B tuşuyla aynı).</summary>
    void FillAll()
    {
        Color c = CurColor();
        for (int i = 0; i < px.Length; i++) px[i] = c;
        dirty = true;
    }

    /// <summary>Sağ panel (eskiz kâğıdı): panoya tutturulmuş referans, benzerlik, boya lekeleri, fırça boyutu.</summary>
    void DrawSidePanel()
    {
        Rect p = sideRect;
        UIUtil.Panel(p, PanelKind.Sketch);
        float sc = UITheme.Scale;
        float padX = BenchKit.Px(22f, 18f), padY = BenchKit.Px(20f, 16f);
        var f = new Flow(p.x + padX, p.y + padY, p.width - padX * 2f);
        float iw = f.W;

        // Referans kaynağı: monitör (BodyCam) → canlı yayın → eski usul orijinal
        Texture feed = null;
        string feedLabel = Loc.T("bench.original");
        bool provider = ReferenceProvider != null;
        if (provider) { feed = ReferenceProvider(out string lbl); feedLabel = lbl ?? ""; }
        bool showScore = !provider || feed != null; // kamera sinyali yokken benzerlik gizlenir ("--")

        // Alttaki bloklar için gereken yükseklik (referans kalan yere sığar)
        float secH = 26f * sc + 2f;
        float gap = 6f;
        float bs = Mathf.Min(BenchKit.Px(40f, 30f), (iw - gap * 3f) / 4f);
        int rows = (palette.Length + 3) / 4;
        float paletteH = secH + rows * (bs + gap) + 8f;
        float brushCap = UIUtil.MeasureHeight(Loc.T("bench.brushsize", BrushSize), TextRole.Caption, iw);
        float brushH = secH + brushCap + 4f + BenchKit.Px(28f, 26f) + 8f;
        float simH = BenchKit.SimilarityHeight() + (showScore ? UIUtil.MeasureHeight(Loc.T("bench.regions", RegionsDone, RegionsTotal), TextRole.Caption, iw) + 4f : 0f) + 8f;
        float avail = p.height - padY * 2f - secH - 10f - paletteH - brushH - simH - 6f;
        float rh = Mathf.Clamp(avail, 56f, iw * 0.75f);
        float rwid = Mathf.Min(iw, rh * 4f / 3f);

        f.Section(feedLabel, PanelKind.Sketch);
        f.Gap(8f);
        Rect row = f.Row(rh);
        Rect rr = new Rect(row.x + (row.width - rwid) * 0.5f, row.y, rwid, rh);
        UIUtil.DrawRect(rr, Color.black);
        if (feed != null) GUI.DrawTexture(rr, feed, ScaleMode.ScaleToFit, false);
        else if (provider) DrawNoSignal(rr);
        else if (UseLiveFeed) LiveFeed.Draw(rr);
        else GUI.DrawTexture(rr, reference, ScaleMode.StretchToFill, false);
        UIUtil.BrassFrame(new Rect(rr.x - 3f, rr.y - 3f, rr.width + 6f, rr.height + 6f));
        // Kâğıda tutturan iki klips
        DrawClip(new Rect(rr.x + 10f, rr.y - 9f, 18f, 12f));
        DrawClip(new Rect(rr.xMax - 28f, rr.y - 9f, 18f, 12f));
        f.Gap(12f);

        // Benzerlik: sinyal yokken "--"
        int pct = showScore ? Mathf.Clamp(Mathf.RoundToInt(Score * 100f), 0, 100) : -1;
        BenchKit.SimilarityBlock(f, pct, -1f, showScore ? Loc.T("bench.regions", RegionsDone, RegionsTotal) : null);
        f.Gap(4f);

        // Boya lekeleri
        f.Section(Loc.T("bench.colors"), PanelKind.Sketch);
        int pick = BenchKit.PaletteGrid(f, palette, PaletteIndex, !Eraser, 4);
        if (pick >= 0) { PaletteIndex = pick; Eraser = false; }
        f.Gap(4f);

        // Fırça boyutu
        f.Section(Loc.T("bench.tool.brush"), PanelKind.Sketch);
        f.Text(Loc.T("bench.brushsize", BrushSize), TextRole.Caption, UITheme.Graphite);
        f.Gap(4f);
        float v = BenchKit.SizeRow(f, BrushSize, 1f, 30f, 1f);
        BrushSize = Mathf.Clamp(Mathf.RoundToInt(v), 1, 30);
    }

    /// <summary>Sinyal yok: tarama dokusu + kamera simgesi + yönerge.</summary>
    void DrawNoSignal(Rect r)
    {
        UIUtil.Hatch(r, UITheme.WithAlpha(UITheme.Graphite, 0.45f));
        string full = Loc.T("cam.nosignal.help");
        int nl = full.IndexOf('\n');
        string head = nl > 0 ? full.Substring(0, nl) : full;
        string rest = nl > 0 ? full.Substring(nl + 1) : "";
        bool roomy = r.height >= 110f && r.width >= 150f;
        float isz = Mathf.Clamp(r.height * 0.22f, 18f, 32f);
        float ty = r.center.y - (roomy ? 36f : 14f);
        UIUtil.Icon(new Rect(r.center.x - isz * 0.5f, ty, isz, isz), IconId.Camera, UITheme.DangerLight);
        UIUtil.Text(new Rect(r.x + 4f, ty + isz + 2f, r.width - 8f, 20f), head, TextRole.BodyStrong, UITheme.DangerLight, TextAnchor.MiddleCenter);
        if (roomy && rest.Length > 0)
            BenchKit.Wrapped(new Rect(r.x + 8f, ty + isz + 24f, r.width - 16f, r.yMax - (ty + isz + 24f) - 4f), rest, TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperCenter);
    }

    static void DrawClip(Rect c)
    {
        UIUtil.DrawRect(c, UITheme.Graphite);
        UIUtil.DrawRect(new Rect(c.x, c.y, c.width, 3f), UITheme.Brass);
        UIUtil.Outline(c, 1f, UITheme.Charcoal);
    }

    /// <summary>Fare tuvalin üstündeyken fırçanın gerçek boyutunu/şeklini gösteren halka (imlecin etrafında).</summary>
    void DrawBrushCursor()
    {
        if (!GUI.enabled) return;
        Vector2 mp = Event.current.mousePosition;
        if (!canvasRect.Contains(mp)) return;
        float sc = canvasRect.width / W;
        float hx = BrushSize * sc, hy = BrushSize * sc;
        if (Shape == BrushShape.HLine) hy = Mathf.Max(1, BrushSize / 4) * sc;
        else if (Shape == BrushShape.VLine) hx = Mathf.Max(1, BrushSize / 4) * sc;
        float dw = Mathf.Max(4f, hx * 2f), dh = Mathf.Max(4f, hy * 2f);
        float rad = Shape == BrushShape.Round ? dw * 0.5f : 0f;
        Rect r = new Rect(mp.x - dw * 0.5f, mp.y - dh * 0.5f, dw, dh);
        Rect outer = new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f);
        // Koyu dış çizgi + iç çizgi: her zeminde görünür
        GUI.DrawTexture(outer, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, UITheme.WithAlpha(UITheme.CharDeep, 0.75f), 1.5f, rad > 0f ? outer.width * 0.5f : 0f);
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Eraser ? UITheme.DangerLight : UITheme.Ivory, 2f, rad);
        Color pc = CurColor();
        GUI.DrawTexture(new Rect(mp.x - 2f, mp.y - 2f, 4f, 4f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(pc.r, pc.g, pc.b, 1f), 0, 2f);
    }

    /// <summary>Yarım kalmış bir sahte tablonun çizimini tuvale geri yükler (atölyede "Devam et").</summary>
    public void LoadFrom(ForgeryData d)
    {
        if (d == null || d.kind != ArtKind.Painting || d.width <= 0 || d.height <= 0 || d.pixels.Length < d.width * d.height) return;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                px[y * W + x] = d.PixelColor(Mathf.Min(d.width - 1, x * d.width / W), Mathf.Min(d.height - 1, y * d.height / H));
        canvas.SetPixels(px);
        canvas.Apply();
        undo.Clear();
        Score = ComputeScore();
    }

    /// <summary>Tuvalin şu anki halini taşınabilir veriye çevirir.</summary>
    public ForgeryData ToData()
    {
        Color32[] pal = ForgeryData.ToPalette(palette, CanvasBase);
        byte[] data = ForgeryData.Quantize(px, W, H, 1, pal, out int w, out int h);
        return new ForgeryData { kind = ArtKind.Painting, palette = pal, width = w, height = h, pixels = data };
    }

    public void Dispose()
    {
        if (canvas != null) Object.Destroy(canvas);
        if (guide != null) Object.Destroy(guide);
    }
}
