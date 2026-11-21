using System;
using UnityEngine;

/// <summary>
/// Emote çarkının ikonları: dosya gerektirmeden, ilk kullanımda kodla çizilir (yumuşak kenarlı beyaz şekiller; çarkta
/// renkle boyanır). Şekiller işaret mesafe fonksiyonlarıyla (kapsül, daire, halka, yuvarlak kutu) kurulur.
/// Ayrıca çarkın dilim dokusu ve merkez dairesi de burada üretilir.
/// </summary>
public static class EmoteIcons
{
    private const int N = 128;
    private static Texture2D[] icons;
    private static Texture2D slice, disc;

    // -------------------------------------------------- şekiller (koordinat: -1..1, y yukarı)
    private static float Cap(float x, float y, float ax, float ay, float bx, float by, float r)
    {
        float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
        float h = Mathf.Clamp01((px * dx + py * dy) / (dx * dx + dy * dy));
        float ex = px - dx * h, ey = py - dy * h;
        return Mathf.Sqrt(ex * ex + ey * ey) - r;
    }
    private static float Circ(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
    private static float Ring(float x, float y, float cx, float cy, float r, float t) => Mathf.Abs(Circ(x, y, cx, cy, r)) - t;
    private static float Box(float x, float y, float cx, float cy, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(x - cx) - hx + r, qy = Mathf.Abs(y - cy) - hy + r;
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }
    private static float Arc(float x, float y, float cx, float cy, float r, float t, float a0, float a1)
    {
        float a = Mathf.Atan2(y - cy, x - cx) * Mathf.Rad2Deg;
        return a >= a0 && a <= a1 ? Ring(x, y, cx, cy, r, t) : 9f;
    }
    private static float Sub(float a, float b) => Mathf.Max(a, -b);

    private static float Thumbs(float x, float y)
    {
        float d = Box(x, y, 0.08f, -0.22f, 0.36f, 0.34f, 0.14f);
        d = Mathf.Min(d, Cap(x, y, -0.12f, 0.02f, -0.08f, 0.52f, 0.14f));
        d = Mathf.Min(d, Box(x, y, 0.12f, -0.68f, 0.26f, 0.1f, 0.04f));
        for (int i = 0; i < 3; i++) { float yy = -0.06f - 0.16f * i; d = Sub(d, Cap(x, y, 0.02f, yy, 0.44f, yy, 0.022f)); }
        return d;
    }

    private static float Shape(EmoteId id, float x, float y)
    {
        float d;
        switch (id)
        {
            case EmoteId.Ok:
                d = Ring(x, y, -0.22f, -0.12f, 0.22f, 0.08f);
                d = Mathf.Min(d, Box(x, y, 0.18f, -0.38f, 0.26f, 0.24f, 0.14f));
                d = Mathf.Min(d, Cap(x, y, 0.06f, -0.1f, 0.06f, 0.6f, 0.085f));
                d = Mathf.Min(d, Cap(x, y, 0.26f, -0.12f, 0.32f, 0.5f, 0.085f));
                d = Mathf.Min(d, Cap(x, y, 0.42f, -0.2f, 0.55f, 0.32f, 0.08f));
                return Mathf.Min(d, Box(x, y, 0.14f, -0.76f, 0.2f, 0.1f, 0.04f));
            case EmoteId.MiddleFinger:
                // Elin sırtı: yumruk, ortada yukarı uzanan orta parmak, iki yanda bükük parmak boğumları
                d = Box(x, y, 0f, -0.22f, 0.40f, 0.30f, 0.15f);
                d = Mathf.Min(d, Cap(x, y, 0f, -0.05f, 0f, 0.70f, 0.11f));
                d = Mathf.Min(d, Circ(x, y, -0.26f, 0.08f, 0.11f));
                d = Mathf.Min(d, Circ(x, y, 0.24f, 0.08f, 0.11f));
                d = Mathf.Min(d, Circ(x, y, 0.40f, 0.0f, 0.09f));
                d = Sub(d, Cap(x, y, -0.13f, 0.12f, -0.13f, -0.25f, 0.022f));
                d = Sub(d, Cap(x, y, 0.13f, 0.12f, 0.13f, -0.25f, 0.022f));
                return Mathf.Min(d, Box(x, y, 0f, -0.68f, 0.26f, 0.1f, 0.04f));
            case EmoteId.ThumbsUp: return Thumbs(x, y);
            case EmoteId.ThumbsDown: return Thumbs(x, -y);
            case EmoteId.Wave:
                d = Box(x, y, 0.02f, -0.3f, 0.28f, 0.26f, 0.15f);
                d = Mathf.Min(d, Cap(x, y, -0.18f, -0.15f, -0.24f, 0.42f, 0.075f));
                d = Mathf.Min(d, Cap(x, y, -0.03f, -0.15f, -0.04f, 0.55f, 0.075f));
                d = Mathf.Min(d, Cap(x, y, 0.12f, -0.15f, 0.16f, 0.52f, 0.075f));
                d = Mathf.Min(d, Cap(x, y, 0.26f, -0.15f, 0.36f, 0.36f, 0.075f));
                d = Mathf.Min(d, Cap(x, y, -0.22f, -0.32f, -0.5f, -0.06f, 0.08f));
                d = Mathf.Min(d, Arc(x, y, 0f, -0.1f, 0.78f, 0.035f, 20f, 55f));
                d = Mathf.Min(d, Arc(x, y, 0f, -0.1f, 0.78f, 0.035f, 125f, 160f));
                return Mathf.Min(d, Box(x, y, 0.02f, -0.72f, 0.2f, 0.08f, 0.04f));
            case EmoteId.PointLaugh:
                d = Box(x, y, -0.32f, -0.3f, 0.26f, 0.24f, 0.13f);
                d = Mathf.Min(d, Cap(x, y, -0.16f, -0.14f, 0.46f, -0.14f, 0.085f));
                d = Mathf.Min(d, Box(x, y, -0.66f, -0.3f, 0.08f, 0.18f, 0.04f));
                d = Sub(d, Cap(x, y, -0.42f, -0.32f, -0.12f, -0.32f, 0.02f));
                d = Sub(d, Cap(x, y, -0.42f, -0.46f, -0.12f, -0.46f, 0.02f));
                // "HA"
                d = Mathf.Min(d, Cap(x, y, -0.36f, 0.26f, -0.36f, 0.66f, 0.045f));
                d = Mathf.Min(d, Cap(x, y, -0.12f, 0.26f, -0.12f, 0.66f, 0.045f));
                d = Mathf.Min(d, Cap(x, y, -0.36f, 0.46f, -0.12f, 0.46f, 0.045f));
                d = Mathf.Min(d, Cap(x, y, 0.04f, 0.26f, 0.16f, 0.66f, 0.045f));
                d = Mathf.Min(d, Cap(x, y, 0.28f, 0.26f, 0.16f, 0.66f, 0.045f));
                return Mathf.Min(d, Cap(x, y, 0.08f, 0.4f, 0.24f, 0.4f, 0.045f));
        }
        return 9f;
    }

    private static Texture2D Raster(int size, Func<float, float, float> sdf, float soft = 1f)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        var px = new Color32[size * size];
        float pix = 2f / size;
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = (i + 0.5f) / size * 2f - 1f, y = (j + 0.5f) / size * 2f - 1f; // dokuda j=0 alt satır
                float a = Mathf.Clamp01(0.5f - sdf(x, y) / (pix * soft));
                px[j * size + i] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return tex;
    }

    /// <summary>Emote'un ikonu (beyaz; GUI.color ile boyanır).</summary>
    public static Texture2D Get(EmoteId id)
    {
        if (icons == null) icons = new Texture2D[16];
        int i = (int)id;
        if (i < 0 || i >= icons.Length) return null;
        if (icons[i] == null) icons[i] = Raster(N, (x, y) => Shape(id, x, y));
        return icons[i];
    }

    /// <summary>
    /// Çark dilimi: halka parçası (iç 0.36, dış 0.98), yukarı bakan 45°'lik dilim, aralarda ince boşluk.
    /// Döndürülerek 8 dilim çizilir.
    /// </summary>
    public static Texture2D Slice
    {
        get
        {
            if (slice != null) return slice;
            const float half = 22.5f, gapPx = 0.012f;
            slice = Raster(512, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float dRing = Mathf.Max(0.36f - r, r - 0.98f);
                float ang = Mathf.Abs(Mathf.Atan2(x, y)) * Mathf.Rad2Deg; // yukarıdan sapma
                float dAng = (ang - half) * Mathf.Deg2Rad * Mathf.Max(r, 0.01f) + gapPx;
                return Mathf.Max(dRing, dAng);
            });
            return slice;
        }
    }

    /// <summary>Dolu daire (merkez ve imleç noktası).</summary>
    public static Texture2D Disc
    {
        get
        {
            if (disc == null) disc = Raster(256, (x, y) => Mathf.Sqrt(x * x + y * y) - 0.97f);
            return disc;
        }
    }
}
