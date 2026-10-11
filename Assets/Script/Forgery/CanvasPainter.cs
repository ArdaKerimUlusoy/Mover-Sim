using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tezgahta tablo sahtesi çizmek için ekran tuvali.
/// Solda tuval (üstünde saydam yeşil kontur rehberi), sağda orijinal tablo referansı.
///   Sol tık sürükle : fırça
///   Shift + sol tık : kova (alanı doldur)
///   Tekerlek        : fırça boyutu
///   1-7             : renk
///   B               : tüm tuvali seçili renkle doldur
///   G               : yeşil rehberi aç/kapat
/// </summary>
public class CanvasPainter
{
    public const int W = 256, H = 192;

    public float Score { get; private set; }
    public bool ShowGuide = true;
    public int BrushSize = 5;
    public int PaletteIndex { get; private set; }

    private readonly Texture2D reference;
    private readonly Texture2D canvas;
    private readonly Texture2D guide;
    private readonly Color[] px;
    private readonly Color[] palette;
    private Vector2? lastPix;
    private float scoreTimer;
    private bool dirty;

    private Rect canvasRect, refRect;
    private GUIStyle labelStyle, titleStyle;

    public static readonly Color CanvasBase = new Color(0.96f, 0.93f, 0.85f);

    public CanvasPainter(Texture2D reference, Color[] palette)
    {
        this.reference = reference;
        this.palette = palette;

        canvas = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SahteTuval" };
        px = new Color[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = CanvasBase;
        canvas.SetPixels(px);
        canvas.Apply();

        guide = BuildGuide(reference);
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
        float rw = cw * 0.42f, rh = rw * H / W;
        float total = cw + 28f + rw;
        float x0 = (Screen.width - total) * 0.5f;
        float y0 = Screen.height * 0.17f;
        canvasRect = new Rect(x0, y0, cw, ch);
        refRect = new Rect(x0 + cw + 28f, y0 + 30f, rw, rh);
    }

    public void Tick(Mouse mouse, Keyboard kb)
    {
        Layout();
        if (kb != null)
        {
            for (int i = 0; i < palette.Length && i < 9; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame) PaletteIndex = i;
            if (kb.gKey.wasPressedThisFrame) ShowGuide = !ShowGuide;
            if (kb.bKey.wasPressedThisFrame) { for (int i = 0; i < px.Length; i++) px[i] = palette[PaletteIndex]; dirty = true; }
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
                    if (mouse.leftButton.wasPressedThisFrame) FloodFill((int)p.x, (int)p.y, palette[PaletteIndex]);
                }
                else
                {
                    if (lastPix.HasValue) StrokeLine(lastPix.Value, p);
                    else Stamp((int)p.x, (int)p.y);
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
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(1f, BrushSize * 0.5f)));
        for (int i = 1; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, (float)i / steps);
            Stamp((int)p.x, (int)p.y);
        }
    }

    void Stamp(int cx, int cy)
    {
        Color c = palette[PaletteIndex];
        int r = BrushSize;
        for (int y = -r; y <= r; y++)
        {
            int py = cy + y;
            if (py < 0 || py >= H) continue;
            for (int x = -r; x <= r; x++)
            {
                int pxl = cx + x;
                if (pxl < 0 || pxl >= W) continue;
                if (x * x + y * y <= r * r) px[py * W + pxl] = c;
            }
        }
        dirty = true;
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

    float ComputeScore()
    {
        const int NX = 32, NY = 24;
        float total = 0f;
        for (int y = 0; y < NY; y++)
        {
            float v = (y + 0.5f) / NY;
            int py = Mathf.Min(H - 1, (int)(v * H));
            for (int x = 0; x < NX; x++)
            {
                float u = (x + 0.5f) / NX;
                int pxl = Mathf.Min(W - 1, (int)(u * W));
                Color a = px[py * W + pxl];
                Color b = reference.GetPixelBilinear(u, v);
                float d = Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b)) / 1.732f;
                total += d;
            }
        }
        float mean = total / (NX * NY);
        return Mathf.Clamp01(1f - mean / 0.25f);
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
        if (ShowGuide) GUI.DrawTexture(canvasRect, guide, ScaleMode.StretchToFill, true);

        // Referans
        GUI.Label(new Rect(refRect.x, refRect.y - 28, refRect.width, 24), Loc.T("bench.original"), titleStyle);
        Rect rframe = new Rect(refRect.x - 6, refRect.y - 6, refRect.width + 12, refRect.height + 12);
        GUI.DrawTexture(rframe, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0.55f, 0.38f, 0.12f), 0, 3);
        GUI.DrawTexture(refRect, reference, ScaleMode.StretchToFill, false);

        // Palet
        float by = refRect.yMax + 24;
        GUI.Label(new Rect(refRect.x, by, refRect.width, 22), Loc.T("bench.colors"), labelStyle);
        by += 26;
        float bs = Mathf.Min(40f, (refRect.width - 8f * 3) / 4f);
        for (int i = 0; i < palette.Length; i++)
        {
            int col = i % 4, row = i / 4;
            Rect b = new Rect(refRect.x + col * (bs + 8), by + row * (bs + 8), bs, bs);
            GUI.DrawTexture(b, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, palette[i], 0, 6);
            if (i == PaletteIndex)
                GUI.DrawTexture(new Rect(b.x - 3, b.y - 3, b.width + 6, b.height + 6), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, Color.white, 3, 8);
            GUI.Label(new Rect(b.x, b.y, b.width, b.height), (i + 1).ToString(), labelStyle);
            if (GUI.Button(b, GUIContent.none, GUIStyle.none)) PaletteIndex = i;
        }

        // Puan
        float sy = by + 2 * (bs + 8) + 12;
        GUI.Label(new Rect(refRect.x, sy, refRect.width, 24), Loc.T("bench.similarity", Mathf.RoundToInt(Score * 100), ItemDefinition.GradeName((byte)Mathf.RoundToInt(Score * 100))), titleStyle);
        Rect bar = new Rect(refRect.x, sy + 30, refRect.width, 12);
        GUI.DrawTexture(bar, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(1, 1, 1, 0.15f), 0, 4);
        Color fill = Score >= 0.75f ? new Color(0.35f, 0.95f, 0.45f) : (Score >= 0.5f ? new Color(1f, 0.75f, 0.25f) : new Color(1f, 0.4f, 0.3f));
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Score, bar.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, fill, 0, 4);
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
