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

    private Rect canvasRect, refRect;
    private GUIStyle labelStyle, titleStyle, scoreStyle, smallStyle, toolStyle;

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

    void Layout()
    {
        float availW = Screen.width - 60f;
        float ch = Mathf.Min(Screen.height * 0.6f, availW * 0.66f * H / W);
        float cw = ch * W / H;
        float rw = cw * (UseLiveFeed ? 0.55f : 0.42f), rh = rw * H / W;
        float total = cw + 28f + rw;
        float x0 = (Screen.width - total) * 0.5f;
        float y0 = Screen.height * 0.17f;
        canvasRect = new Rect(x0, y0, cw, ch);
        refRect = new Rect(x0 + cw + 28f, y0 + 30f, rw, rh);
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
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            labelStyle.normal.textColor = Color.white;
            titleStyle = new GUIStyle(labelStyle) { fontSize = 20, fontStyle = FontStyle.Bold };
        }

        // Arka plan karartma
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0f, 0f, 0f, 0.55f), 0, 0);

        GUI.Label(new Rect(0, Screen.height * 0.03f, Screen.width, 30), title, titleStyle);
        GUI.Label(new Rect(0, Screen.height * 0.03f + 30, Screen.width, 44),
            Loc.T("bench.canvashelp", BrushSize, palette.Length, Loc.T(ShowGuide ? "bench.on" : "bench.off")),
            labelStyle);

        // Tuval
        Rect frame = new Rect(canvasRect.x - 10, canvasRect.y - 10, canvasRect.width + 20, canvasRect.height + 20);
        GUI.DrawTexture(frame, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.55f, 0.38f, 0.12f), 0, 4);
        GUI.DrawTexture(canvasRect, canvas, ScaleMode.StretchToFill, false);
        if (ShowGuide && guide != null) GUI.DrawTexture(canvasRect, guide, ScaleMode.StretchToFill, true);

        // Tuvalin altındaki boşluk: kat planı (BodyCam) — ressam çizerken müzedekini yönetebilsin
        if (ExtraPanelDrawer != null)
        {
            float top = canvasRect.yMax + 18f;
            float h = Screen.height - top - 10f;
            if (h > 90f) ExtraPanelDrawer(new Rect(canvasRect.x - 10f, top, Mathf.Min(canvasRect.width + 20f, h * 1.9f), h));
        }

        // Referans: monitör (BodyCam) varsa orijinal yerine kamyon monitörünün görüntüsü
        Rect rframe = new Rect(refRect.x - 6, refRect.y - 6, refRect.width + 12, refRect.height + 12);
        bool showScore = true; // kamera sinyali yokken benzerlik gizlenir
        if (ReferenceProvider != null)
        {
            Texture feed = ReferenceProvider(out string feedLabel);
            showScore = feed != null;
            GUI.Label(new Rect(refRect.x - 60, refRect.y - 28, refRect.width + 120, 24), feedLabel, titleStyle);
            GUI.DrawTexture(rframe, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.12f, 0.12f, 0.14f), 0, 3);
            GUI.DrawTexture(refRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, Color.black, 0, 0);
            if (feed != null) GUI.DrawTexture(refRect, feed, ScaleMode.ScaleToFit, false);
            else GUI.Label(refRect, Loc.T("cam.nosignal.help"), labelStyle);
        }
        else if (UseLiveFeed) // arkadaşın canlı yayını (BodyCam yoksa)
        {
            LiveFeed.Draw(refRect);
            // Solda boş yer varsa kat planı
            float planW = canvasRect.x - 46f;
            if (planW >= 180f)
            {
                float planH = Mathf.Min(planW * 0.72f, canvasRect.height);
                FloorPlanUI.Draw(new Rect(16f, canvasRect.y, planW, planH), planW >= 300f);
            }
        }
        else
        {
            GUI.Label(new Rect(refRect.x, refRect.y - 28, refRect.width, 24), Loc.T("bench.original"), titleStyle);
            GUI.DrawTexture(rframe, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.55f, 0.38f, 0.12f), 0, 3);
            GUI.DrawTexture(refRect, reference, ScaleMode.StretchToFill, false);
        }

        float x0 = refRect.x, w0 = refRect.width;
        float y = refRect.yMax + 12;
        if (smallStyle == null)
        {
            smallStyle = new GUIStyle(labelStyle) { fontSize = 12, alignment = TextAnchor.UpperCenter };
            smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            toolStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, clipping = TextClipping.Clip };
        }

        // Palet
        GUI.Label(new Rect(x0, y, w0, 20), Loc.T("bench.colors"), smallStyle);
        y += 22;
        float bs = Mathf.Min(40f, (w0 - 8f * 3) / 4f);
        for (int i = 0; i < palette.Length; i++)
        {
            int col = i % 4, row = i / 4;
            Rect b = new Rect(x0 + col * (bs + 8), y + row * (bs + 8), bs, bs);
            GUI.DrawTexture(b, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, palette[i], 0, 6);
            if (i == PaletteIndex && !Eraser)
                GUI.DrawTexture(new Rect(b.x - 3, b.y - 3, b.width + 6, b.height + 6), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, Color.white, 3, 8);
            GUI.Label(new Rect(b.x, b.y, b.width, b.height), (i + 1).ToString(), labelStyle);
            if (GUI.Button(b, GUIContent.none, GUIStyle.none)) { PaletteIndex = i; Eraser = false; }
        }
        y += 2 * (bs + 8) + 4;

        // Benzerlik: kamera sinyali yoksa gizli ("--")
        if (scoreStyle == null) scoreStyle = new GUIStyle(titleStyle) { wordWrap = false, fontSize = 18, clipping = TextClipping.Overflow };
        Rect bar = new Rect(x0, y + 26, w0, 10);
        GUI.DrawTexture(bar, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(1, 1, 1, 0.15f), 0, 4);
        if (showScore)
        {
            byte pct = (byte)Mathf.Clamp(Mathf.RoundToInt(Score * 100), 0, 100);
            GUI.Label(new Rect(x0, y, w0, 24), Loc.T("bench.similarity", pct, ItemDefinition.GradeName(pct)), scoreStyle);
            Color fill = Score >= 0.75f ? new Color(0.35f, 0.95f, 0.45f) : (Score >= 0.5f ? new Color(1f, 0.75f, 0.25f) : new Color(1f, 0.4f, 0.3f));
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Score, bar.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, fill, 0, 4);
            GUI.Label(new Rect(x0, bar.yMax + 2, w0, 18), Loc.T("bench.regions", RegionsDone, RegionsTotal), smallStyle);
        }
        else GUI.Label(new Rect(x0, y, w0, 24), "--", scoreStyle);
        y += 62;

        // Fırça boyutu: kaydırıcı + / − düğmeleri (boyut halkası imlecin üstünde çizilir)
        GUI.Label(new Rect(x0, y, w0, 20), Loc.T("bench.brushsize", BrushSize), smallStyle);
        y += 20;
        if (GUI.Button(new Rect(x0, y, 28, 22), "−")) BrushSize = Mathf.Clamp(BrushSize - 1, 1, 30);
        if (GUI.Button(new Rect(refRect.xMax - 28, y, 28, 22), "+")) BrushSize = Mathf.Clamp(BrushSize + 1, 1, 30);
        float slid = GUI.HorizontalSlider(new Rect(x0 + 36, y + 6, w0 - 72, 14), BrushSize, 1f, 30f);
        BrushSize = Mathf.Clamp(Mathf.RoundToInt(slid), 1, 30);
        y += 28;

        // Araç çubuğu: fırça / silgi / geri al
        float gap = 4f, tw = (w0 - gap * 2f) / 3f;
        if (ToolButton(new Rect(x0, y, tw, 24), Loc.T("bench.tool.brush"), !Eraser)) Eraser = false;
        if (ToolButton(new Rect(x0 + tw + gap, y, tw, 24), Loc.T("bench.tool.eraser"), Eraser)) Eraser = true;
        if (ToolButton(new Rect(x0 + (tw + gap) * 2f, y, tw, 24), Loc.T("bench.undo", undo.Count), false)) Undo();
        y += 28;

        // Şekil seçimi
        float sw = (w0 - gap * 3f) / 4f;
        string[] shapeKeys = { "bench.shape.round", "bench.shape.square", "bench.shape.hline", "bench.shape.vline" };
        for (int i = 0; i < 4; i++)
            if (ToolButton(new Rect(x0 + (sw + gap) * i, y, sw, 24), Loc.T(shapeKeys[i]), (int)Shape == i)) Shape = (BrushShape)i;
        y += 28;

        // Kısayol yardımı
        GUI.Label(new Rect(x0 - 20f, y, w0 + 40f, 36), Loc.T("bench.toolhelp"), smallStyle);

        DrawBrushCursor();
    }

    /// <summary>Seçili durumu vurgulanan küçük araç düğmesi.</summary>
    bool ToolButton(Rect r, string text, bool selected)
    {
        if (selected)
            GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(1f, 0.85f, 0.3f), 2, 5);
        return GUI.Button(r, text, toolStyle);
    }

    /// <summary>Fare tuvalin üstündeyken fırçanın gerçek boyutunu/şeklini gösteren halka (imlecin etrafında).</summary>
    void DrawBrushCursor()
    {
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
        GUI.DrawTexture(outer, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0f, 0f, 0f, 0.75f), 1.5f, rad > 0f ? outer.width * 0.5f : 0f);
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Eraser ? new Color(1f, 0.5f, 0.5f) : Color.white, 2f, rad);
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
