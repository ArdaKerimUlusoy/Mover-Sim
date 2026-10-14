using System.Collections.Generic;
using UnityEngine;

/// <summary>Eserin atölyede nasıl yapıldığı: vazo (çömlek çarkı) / tablo (tuval).</summary>
public enum ArtKind { None, Vase, Painting, Bust, Jewelry }

/// <summary>Müze teması (GDD Bölüm 8: gerçek müzelerden esinlenilmiş, stilize adlar).</summary>
public class MuseumTheme
{
    public string name;
    public string[] city;              // { İngilizce, Türkçe }
    public Color wall, floor, trim, accent, light;
    public Color[] vaseColors;
    public string[][] vaseNames;       // her biri { İngilizce, Türkçe }
    public int paintingStyle;

    public string City => Pick(city);
    public static string Pick(string[] pair) => pair == null ? "" : (Loc.Current == Language.Turkish && pair.Length > 1 ? pair[1] : pair[0]);
}

/// <summary>Atölyede kopyalanacak bir vazonun görünümü (tüm oyuncularda aynı tohumdan üretilir).</summary>
public sealed class VaseLook
{
    public string[] name;
    public float[] profile;
    public float height, maxRadius;
    public Texture2D pattern;
    public Color[] palette;

    public string Name => MuseumTheme.Pick(name);

    public float Radius01(float t)
    {
        if (profile == null || profile.Length < 2) return 1f;
        float f = Mathf.Clamp01(t) * (profile.Length - 1);
        int i = Mathf.Min(Mathf.FloorToInt(f), profile.Length - 2);
        float k = f - i;
        k = k * k * (3f - 2f * k);
        return Mathf.Lerp(profile[i], profile[i + 1], k);
    }

    public float Radius(float t) => Radius01(t) * maxRadius;
}

/// <summary>Atölyede kopyalanacak bir tablonun görünümü.</summary>
public sealed class PaintingLook
{
    public string[] name;
    public Texture2D image;
    public Color[] palette;
    public float width = 0.9f, height = 0.675f;

    public string Name => MuseumTheme.Pick(name);
}

/// <summary>
/// Müze temaları + vazo / tablo üretimi. Aynı (tohum, kaide adı) her bilgisayarda aynı eseri üretir,
/// böylece ağdan mesh/doku göndermeye gerek kalmaz: sunucu sadece tema ve tohumu paylaşır (MuseumSession).
/// </summary>
public static partial class ArtworkLook
{
    static Color C(float r, float g, float b) => new Color(r, g, b);
    static string[] N(string en, string tr) => new[] { en, tr };

    public static readonly MuseumTheme[] Themes =
    {
        new MuseumTheme { name = "Le Grand Musée", city = N("Paris", "Paris"),
            wall = C(0.93f, 0.89f, 0.80f), floor = C(0.62f, 0.62f, 0.64f), trim = C(0.78f, 0.66f, 0.38f), accent = C(0.6f, 0.08f, 0.12f), light = C(1f, 0.92f, 0.78f),
            vaseColors = new[] { C(0.13f, 0.27f, 0.62f), C(0.95f, 0.93f, 0.86f), C(0.93f, 0.72f, 0.20f), C(0.72f, 0.36f, 0.20f) },
            vaseNames = new[] { N("Sèvres Vase", "Sèvres Vazosu"), N("Royal Amphora", "Kraliyet Amforası"), N("Blue Porcelain", "Mavi Porselen") },
            paintingStyle = 0 },
        new MuseumTheme { name = "Royal Antiquities Museum", city = N("London", "Londra"),
            wall = C(0.55f, 0.16f, 0.15f), floor = C(0.38f, 0.25f, 0.15f), trim = C(0.82f, 0.68f, 0.35f), accent = C(0.15f, 0.2f, 0.4f), light = C(1f, 0.9f, 0.75f),
            vaseColors = new[] { C(0.1f, 0.1f, 0.1f), C(0.78f, 0.42f, 0.2f), C(0.95f, 0.85f, 0.6f), C(0.55f, 0.25f, 0.12f) },
            vaseNames = new[] { N("Greek Amphora", "Yunan Amforası"), N("Black-Figure Krater", "Siyah Figürlü Krater"), N("Etruscan Jug", "Etrüsk Testisi") },
            paintingStyle = 1 },
        new MuseumTheme { name = "Rijks Galerij", city = N("Amsterdam", "Amsterdam"),
            wall = C(0.2f, 0.32f, 0.27f), floor = C(0.56f, 0.41f, 0.26f), trim = C(0.8f, 0.7f, 0.45f), accent = C(0.75f, 0.45f, 0.1f), light = C(1f, 0.93f, 0.82f),
            vaseColors = new[] { C(0.97f, 0.97f, 0.95f), C(0.16f, 0.28f, 0.65f), C(0.45f, 0.6f, 0.85f), C(0.85f, 0.5f, 0.15f) },
            vaseNames = new[] { N("Delft Vase", "Delft Vazosu"), N("Tulip Vase", "Lale Vazosu"), N("Blue-White Jug", "Mavi-Beyaz Sürahi") },
            paintingStyle = 4 },
        new MuseumTheme { name = "Galleria Fiorentina", city = N("Florence", "Floransa"),
            wall = C(0.78f, 0.45f, 0.32f), floor = C(0.8f, 0.74f, 0.62f), trim = C(0.95f, 0.9f, 0.8f), accent = C(0.2f, 0.3f, 0.55f), light = C(1f, 0.9f, 0.75f),
            vaseColors = new[] { C(0.85f, 0.55f, 0.2f), C(0.2f, 0.4f, 0.7f), C(0.95f, 0.9f, 0.7f), C(0.3f, 0.55f, 0.3f) },
            vaseNames = new[] { N("Maiolica Vase", "Maiolika Vazo"), N("Medici Amphora", "Medici Amforası"), N("Renaissance Jug", "Rönesans Testisi") },
            paintingStyle = 5 },
        new MuseumTheme { name = "Museo Real", city = N("Madrid", "Madrid"),
            wall = C(0.48f, 0.12f, 0.2f), floor = C(0.5f, 0.5f, 0.53f), trim = C(0.85f, 0.7f, 0.3f), accent = C(0.85f, 0.7f, 0.3f), light = C(1f, 0.88f, 0.72f),
            vaseColors = new[] { C(0.95f, 0.85f, 0.2f), C(0.75f, 0.12f, 0.12f), C(0.1f, 0.25f, 0.5f), C(0.95f, 0.95f, 0.9f) },
            vaseNames = new[] { N("Talavera Vase", "Talavera Vazosu"), N("Palace Amphora", "Saray Amforası"), N("Andalusian Jug", "Endülüs Testisi") },
            paintingStyle = 3 },
        new MuseumTheme { name = "Saray Hazinesi", city = N("Istanbul", "İstanbul"),
            wall = C(0.18f, 0.5f, 0.55f), floor = C(0.5f, 0.1f, 0.11f), trim = C(0.95f, 0.92f, 0.85f), accent = C(0.9f, 0.75f, 0.3f), light = C(1f, 0.9f, 0.72f),
            vaseColors = new[] { C(0.97f, 0.96f, 0.92f), C(0.12f, 0.3f, 0.7f), C(0.8f, 0.15f, 0.15f), C(0.15f, 0.55f, 0.55f) },
            vaseNames = new[] { N("Iznik Vase", "İznik Vazosu"), N("Tulip Ewer", "Lale Desenli Testi"), N("Sultan's Cup", "Sultan Kupası") },
            paintingStyle = 2 },
        new MuseumTheme { name = "Nile Antiquities Museum", city = N("Cairo", "Kahire"),
            wall = C(0.85f, 0.72f, 0.5f), floor = C(0.74f, 0.62f, 0.42f), trim = C(0.6f, 0.45f, 0.25f), accent = C(0.15f, 0.3f, 0.6f), light = C(1f, 0.85f, 0.62f),
            vaseColors = new[] { C(0.9f, 0.72f, 0.2f), C(0.12f, 0.22f, 0.55f), C(0.2f, 0.55f, 0.55f), C(0.1f, 0.1f, 0.1f) },
            vaseNames = new[] { N("Canopic Jar", "Kanopik Kavanoz"), N("Pharaoh's Vase", "Firavun Vazosu"), N("Lapis Jug", "Lapis Testi") },
            paintingStyle = 5 },
        new MuseumTheme { name = "Museum of Eastern Arts", city = N("Tokyo", "Tokyo"),
            wall = C(0.92f, 0.88f, 0.78f), floor = C(0.67f, 0.65f, 0.46f), trim = C(0.25f, 0.18f, 0.12f), accent = C(0.75f, 0.15f, 0.15f), light = C(1f, 0.95f, 0.85f),
            vaseColors = new[] { C(0.55f, 0.72f, 0.6f), C(0.95f, 0.95f, 0.92f), C(0.15f, 0.2f, 0.4f), C(0.75f, 0.2f, 0.2f) },
            vaseNames = new[] { N("Celadon Vase", "Seladon Vazo"), N("Imari Vase", "Imari Vazosu"), N("Tea Jar", "Çay Kavanozu") },
            paintingStyle = 1 },
    };

    public static readonly string[][] PaintingNames =
    {
        N("Country House", "Kır Evi"), N("Stormy Sea", "Fırtınalı Deniz"), N("Moonlit Mountains", "Ay Işığında Dağlar"),
        N("Color Blocks", "Renk Blokları"), N("Still Life", "Natürmort"), N("Sunset", "Gün Batımı"),
        N("Lighthouse", "Deniz Feneri"), N("Tulip Fields", "Lale Tarlaları"), N("Desert Pyramids", "Çöl ve Piramitler"),
        N("Cherry Blossom", "Kiraz Çiçeği"), N("Portrait of a Lady", "Bir Hanımın Portresi"), N("Circles", "Daireler"),
    };

    static readonly float[][] VaseProfiles =
    {
        new[] { 0.55f, 0.78f, 0.93f, 1.00f, 0.96f, 0.84f, 0.64f, 0.44f, 0.35f, 0.38f, 0.56f },
        new[] { 0.60f, 0.85f, 1.00f, 0.98f, 0.85f, 0.62f, 0.45f, 0.42f, 0.50f, 0.65f },
        new[] { 0.70f, 0.95f, 1.00f, 1.00f, 0.95f, 0.88f, 0.80f, 0.75f, 0.72f, 0.70f },
        new[] { 0.45f, 0.55f, 0.75f, 0.95f, 1.00f, 0.90f, 0.60f, 0.38f, 0.30f, 0.32f, 0.45f },
        new[] { 0.50f, 0.60f, 0.70f, 0.80f, 0.90f, 0.97f, 1.00f, 0.98f, 0.92f, 0.85f },
        // Yeni şekiller: amfora (dar boyun), kase, testi, şişe, lale ağızlı
        new[] { 0.35f, 0.55f, 0.85f, 1.00f, 0.95f, 0.75f, 0.45f, 0.28f, 0.25f, 0.30f, 0.42f },
        new[] { 0.40f, 0.70f, 0.90f, 1.00f, 1.00f, 1.00f },
        new[] { 0.65f, 0.90f, 1.00f, 0.95f, 0.80f, 0.60f, 0.40f, 0.35f, 0.35f, 0.38f },
        new[] { 0.80f, 1.00f, 1.00f, 0.95f, 0.70f, 0.40f, 0.25f, 0.22f, 0.22f, 0.25f },
        new[] { 0.45f, 0.65f, 0.80f, 0.85f, 0.80f, 0.70f, 0.65f, 0.72f, 0.88f, 1.00f },
    };

    public static readonly Color ClayColor = new Color(0.82f, 0.71f, 0.55f);

    // ---------------------------------------------------------------
    // Tür / tohum yardımcıları
    // ---------------------------------------------------------------

    /// <summary>Eşya vazo mu tablo mu? Sahte eserler orijinaline bakılarak çözülür.</summary>
    public static ArtKind KindOf(ItemDefinition def)
    {
        if (def == null) return ArtKind.None;
        if (def.type == ItemType.Forgery && def.forgeryOf != null) def = def.forgeryOf;
        string id = def.id != null ? def.id.ToLowerInvariant() : "";
        if (id.Contains("vase")) return ArtKind.Vase;
        if (id.Contains("painting")) return ArtKind.Painting;
        if (id.Contains("bust")) return ArtKind.Bust;
        if (id.Contains("ring") || id.Contains("crown") || id.Contains("jewel")) return ArtKind.Jewelry;
        return ArtKind.None;
    }

    /// <summary>Platformdan bağımsız sabit string özeti (FNV-1a).</summary>
    public static int Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }

    public static MuseumTheme Theme(int index) => Themes[((index % Themes.Length) + Themes.Length) % Themes.Length];

    // ---------------------------------------------------------------
    // Önbellek: aynı (tohum, anahtar) için tekrar üretme
    // ---------------------------------------------------------------
    static readonly Dictionary<string, VaseLook> vaseCache = new Dictionary<string, VaseLook>();
    static readonly Dictionary<string, PaintingLook> paintingCache = new Dictionary<string, PaintingLook>();

    public static VaseLook Vase(int seed, int themeIndex, string key)
    {
        string ck = seed + "|" + themeIndex + "|" + key;
        if (vaseCache.TryGetValue(ck, out VaseLook cached)) return cached;

        MuseumTheme th = Theme(themeIndex);
        var rng = new System.Random(seed ^ Hash(key));
        var v = new VaseLook
        {
            name = th.vaseNames[rng.Next(th.vaseNames.Length)],
            profile = (float[])VaseProfiles[rng.Next(VaseProfiles.Length)].Clone(),
            height = Range(rng, 0.45f, 0.6f),
            maxRadius = Range(rng, 0.13f, 0.17f),
        };

        var cols = new List<Color>(th.vaseColors);
        for (int i = 0; i < cols.Count; i++) { int j = rng.Next(i, cols.Count); var t = cols[i]; cols[i] = cols[j]; cols[j] = t; }
        Color body = cols[0], band1 = cols[1 % cols.Count], band2 = cols[2 % cols.Count], baseC = cols[3 % cols.Count];

        float a = Range(rng, 0.36f, 0.56f), aw = Range(rng, 0.08f, 0.13f);
        float b = Range(rng, 0.72f, 0.84f), bw = Range(rng, 0.05f, 0.08f);
        float baseH = Range(rng, 0.06f, 0.12f);
        // Desen türü: 0 düz bantlar, 1 zikzak bant, 2 benekli bant, 3 dikey çizgiler (iki bant arası)
        int patternKind = rng.Next(4);
        int repeats = rng.Next(4, 9);

        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VasePattern" };
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            float vv = (y + 0.5f) / S;
            for (int x = 0; x < S; x++)
            {
                float uu = (x + 0.5f) / S;
                Color c = body;
                if (vv < baseH) c = baseC;
                else if (vv > b && vv < b + bw) c = band2;
                else
                {
                    switch (patternKind)
                    {
                        case 1: // zikzak bant
                        {
                            float tri = Mathf.Abs(Mathf.Repeat(uu * repeats, 1f) - 0.5f) * 2f; // 0..1
                            float mid = a + aw * 0.5f + (tri - 0.5f) * aw * 0.9f;
                            if (Mathf.Abs(vv - mid) < aw * 0.32f) c = band1;
                            break;
                        }
                        case 2: // benekli bant
                        {
                            float du = (Mathf.Repeat(uu * repeats * 2f, 1f) - 0.5f) / (repeats * 2f) * 6.28f;
                            float dv = vv - (a + aw * 0.5f);
                            if (du * du + dv * dv < (aw * 0.42f) * (aw * 0.42f)) c = band1;
                            break;
                        }
                        case 3: // iki bant arasında dikey çizgiler
                            if (vv > a && vv < b && Mathf.Repeat(uu * repeats * 2f, 1f) < 0.28f) c = band1;
                            break;
                        default:
                            if (vv > a && vv < a + aw) c = band1;
                            break;
                    }
                }
                px[y * S + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        v.pattern = tex;
        v.palette = Distinct(new[] { body, band1, band2, baseC, new Color(0.97f, 0.97f, 0.95f), new Color(0.1f, 0.1f, 0.1f) }, 6);

        vaseCache[ck] = v;
        return v;
    }

    public static PaintingLook Painting(int seed, int themeIndex, string key)
    {
        string ck = seed + "|" + themeIndex + "|" + key;
        if (paintingCache.TryGetValue(ck, out PaintingLook cached)) return cached;

        MuseumTheme th = Theme(themeIndex);
        // Yarı yarıya temanın kendi tarzı, yarı yarıya 12 tablonun herhangi biri (her görevde değişir)
        var prng = new System.Random(seed ^ Hash("painting:" + key));
        int style = prng.Next(2) == 0
            ? (th.paintingStyle + (Hash(key) % 2) * 3) % 6
            : prng.Next(PaintingNames.Length);
        var p = new PaintingLook { name = PaintingNames[style], image = CreatePainting(style, out Color[] pal), palette = pal };
        paintingCache[ck] = p;
        return p;
    }

    static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    static Color[] Distinct(Color[] cols, int max)
    {
        var res = new List<Color>();
        foreach (var c in cols)
        {
            bool dup = false;
            foreach (var r in res) if (Mathf.Abs(r.r - c.r) + Mathf.Abs(r.g - c.g) + Mathf.Abs(r.b - c.b) < 0.08f) { dup = true; break; }
            if (!dup) res.Add(c);
            if (res.Count >= max) break;
        }
        return res.ToArray();
    }

    // ---------------------------------------------------------------
    // Görseller (kaide üstü ve atölye)
    // ---------------------------------------------------------------

    /// <summary>Kaide üstünde gösterilecek vazo. similarity &lt; 100 ise sahte: desen kil rengine doğru bozulur.</summary>
    public static GameObject BuildVaseVisual(VaseLook look, byte similarity = InventorySlot.NoSimilarity)
    {
        bool fake = similarity != InventorySlot.NoSimilarity;
        float sim = fake ? similarity / 100f : 1f;

        var go = new GameObject("Vase_" + look.Name);
        System.Func<float, float> radius = t => look.Radius(t);
        if (fake)
        {
            // Kötü sahte: şekil hafif dalgalı
            float wobble = (1f - sim) * 0.18f;
            radius = t => look.Radius(t) * (1f + Mathf.Sin(t * 19f) * wobble);
        }
        go.AddComponent<MeshFilter>().sharedMesh = Lathe(radius, look.height, 32, 32);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = MakeLit(Color.white, fake ? Degrade(look.pattern, sim) : look.pattern);
        return go;
    }

    public static GameObject BuildPaintingVisual(PaintingLook look, byte similarity = InventorySlot.NoSimilarity)
    {
        bool fake = similarity != InventorySlot.NoSimilarity;
        Texture2D img = fake ? Degrade(look.image, similarity / 100f) : look.image;
        return BuildFramedPicture(img, look.width, look.height);
    }

    /// <summary>Çerçeveli tablo. Pivot alt-orta, ön yüz -Z.</summary>
    public static GameObject BuildFramedPicture(Texture2D img, float w, float h)
    {
        var root = new GameObject("Painting");
        float f = 0.05f, d = 0.04f;
        Vector3 c = new Vector3(0f, h * 0.5f + f, 0f);

        var canvas = GameObject.CreatePrimitive(PrimitiveType.Quad);
        canvas.name = "Canvas";
        KillCollider(canvas);
        canvas.transform.SetParent(root.transform, false);
        canvas.transform.localPosition = c + new Vector3(0f, 0f, -d * 0.5f - 0.002f);
        canvas.transform.localScale = new Vector3(w, h, 1f);
        var cm = MakeLit(Color.white, img);
        if (cm.HasProperty("_Smoothness")) cm.SetFloat("_Smoothness", 0.1f);
        canvas.GetComponent<MeshRenderer>().sharedMaterial = cm;

        Material frame = MakeLit(new Color(0.55f, 0.38f, 0.12f));
        Piece(root.transform, c + new Vector3(0f, h * 0.5f + f * 0.5f, 0f), new Vector3(w + 2f * f, f, d + 0.01f), frame);
        Piece(root.transform, c + new Vector3(0f, -h * 0.5f - f * 0.5f, 0f), new Vector3(w + 2f * f, f, d + 0.01f), frame);
        Piece(root.transform, c + new Vector3(-w * 0.5f - f * 0.5f, 0f, 0f), new Vector3(f, h, d + 0.01f), frame);
        Piece(root.transform, c + new Vector3(w * 0.5f + f * 0.5f, 0f, 0f), new Vector3(f, h, d + 0.01f), frame);
        Piece(root.transform, c, new Vector3(w, h, d), MakeLit(new Color(0.3f, 0.22f, 0.15f)));
        return root;
    }

    static void Piece(Transform parent, Vector3 pos, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        KillCollider(go);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    static void KillCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c == null) return;
        if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c);
    }

    /// <summary>Sahte görünümü: benzerlik düştükçe renkler kile ve gürültüye kayar.</summary>
    public static Texture2D Degrade(Texture2D src, float sim)
    {
        if (src == null) return null;
        int w = src.width, h = src.height;
        var px = src.GetPixels();
        float amount = Mathf.Clamp01(1f - sim) * 0.85f;
        var rng = new System.Random(w * 31 + h);
        for (int i = 0; i < px.Length; i++)
        {
            float n = (float)rng.NextDouble() * 0.5f;
            px[i] = Color.Lerp(px[i], ClayColor, amount * (0.5f + n));
        }
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = src.filterMode };
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    /// <summary>Brifing paneli için vazonun 2D silüet resmi.</summary>
    public static Texture2D VaseIcon(VaseLook look, int size = 128)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        Color bg = new Color(0.08f, 0.12f, 0.1f, 1f);
        float aspectH = look.height / (look.maxRadius * 2f);
        float drawH = size * 0.88f;
        float drawMaxHalfW = drawH / aspectH * 0.5f;
        for (int y = 0; y < size; y++)
        {
            float t = (y - size * 0.06f) / drawH;
            for (int x = 0; x < size; x++)
            {
                Color c = bg;
                if (t >= 0f && t <= 1f)
                {
                    float half = look.Radius01(t) * drawMaxHalfW;
                    float dx = Mathf.Abs(x - size * 0.5f);
                    if (dx <= half)
                    {
                        c = look.pattern.GetPixelBilinear(0.5f, t);
                        float shade = 1f - Mathf.Pow(dx / Mathf.Max(1f, half), 3f) * 0.45f;
                        c = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
                    }
                }
                px[y * size + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // ---------------------------------------------------------------
    // Mesh / materyal
    // ---------------------------------------------------------------
    public static Mesh Lathe(System.Func<float, float> radiusAt, float height, int radialSegments, int heightSegments)
    {
        var mesh = new Mesh { name = "LatheMesh" };
        int rowStride = radialSegments + 1;
        int bodyCount = rowStride * (heightSegments + 1);
        var verts = new Vector3[bodyCount + 2];
        var uvs = new Vector2[bodyCount + 2];
        float dA = Mathf.PI * 2f / radialSegments;
        int vi = 0;
        for (int y = 0; y <= heightSegments; y++)
        {
            float t = (float)y / heightSegments;
            float r = radiusAt(t);
            for (int s = 0; s <= radialSegments; s++)
            {
                float a = s * dA;
                verts[vi] = new Vector3(Mathf.Cos(a) * r, t * height, Mathf.Sin(a) * r);
                uvs[vi] = new Vector2((float)s / radialSegments, t);
                vi++;
            }
        }
        int bottom = vi++, top = vi++;
        verts[bottom] = Vector3.zero;
        verts[top] = new Vector3(0f, height, 0f);
        uvs[bottom] = new Vector2(0.5f, 0.02f);
        uvs[top] = new Vector2(0.5f, 0.98f);

        var tris = new int[radialSegments * heightSegments * 6 + radialSegments * 6];
        int ti = 0;
        for (int y = 0; y < heightSegments; y++)
            for (int s = 0; s < radialSegments; s++)
            {
                int cur = y * rowStride + s, next = cur + rowStride;
                tris[ti++] = cur; tris[ti++] = next; tris[ti++] = cur + 1;
                tris[ti++] = cur + 1; tris[ti++] = next; tris[ti++] = next + 1;
            }
        for (int s = 0; s < radialSegments; s++) { tris[ti++] = bottom; tris[ti++] = s + 1; tris[ti++] = s; }
        int topRow = heightSegments * rowStride;
        for (int s = 0; s < radialSegments; s++) { tris[ti++] = top; tris[ti++] = topRow + s; tris[ti++] = topRow + s + 1; }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Shader FindShader(params string[] names)
    {
        foreach (var n in names) { var s = Shader.Find(n); if (s != null) return s; }
        return null;
    }

    public static Material MakeLit(Color color, Texture tex = null)
    {
        var m = new Material(FindShader("Universal Render Pipeline/Lit", "Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        m.color = color;
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
        }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
        return m;
    }

    // ---------------------------------------------------------------
    // Tablo üretimi (büyük, kolay okunur düz renk şekiller)
    // ---------------------------------------------------------------
    public static Texture2D CreatePainting(int style, out Color[] palette)
    {
        const int w = 128, h = 96;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "Painting_" + style };
        var px = new Color[w * h];

        switch (style)
        {
            case 0: // Kır evi
            {
                Color sky = C(0.55f, 0.76f, 0.95f), green = C(0.30f, 0.62f, 0.30f), sun = C(0.98f, 0.84f, 0.25f), red = C(0.82f, 0.25f, 0.20f), brown = C(0.45f, 0.28f, 0.15f), white = C(0.97f, 0.97f, 0.95f);
                palette = new[] { sky, green, sun, red, brown, white, C(0.15f, 0.20f, 0.45f) };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = sky;
                    float du = (u - 0.78f) * w / h, dv = v - 0.76f;
                    if (du * du + dv * dv < 0.11f * 0.11f) c = sun;
                    float cu = (u - 0.3f) * w / h, cv = (v - 0.8f) * 2f;
                    if (cu * cu + cv * cv < 0.09f * 0.09f) c = white;
                    if (v < 0.38f + 0.07f * Mathf.Sin(u * 5.5f + 0.6f)) c = green;
                    if (u > 0.18f && u < 0.42f && v > 0.22f && v < 0.48f) c = red;
                    if (v >= 0.48f && v < 0.64f && Mathf.Abs(u - 0.30f) < 0.14f * (1f - (v - 0.48f) / 0.16f)) c = brown;
                    if (u > 0.27f && u < 0.33f && v > 0.22f && v < 0.36f) c = brown;
                    px[y * w + x] = c;
                }
                break;
            }
            case 1: // Fırtınalı deniz + yelkenli
            {
                Color sky = C(0.62f, 0.75f, 0.88f), sea = C(0.12f, 0.3f, 0.6f), sail = C(0.97f, 0.97f, 0.95f), hull = C(0.45f, 0.28f, 0.15f), cloud = C(0.45f, 0.5f, 0.58f);
                palette = new[] { sky, sea, sail, hull, cloud, C(0.1f, 0.1f, 0.1f) };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = sky;
                    float cu = (u - 0.25f) * 1.333f, cv = (v - 0.82f) * 2.2f;
                    if (cu * cu + cv * cv < 0.12f * 0.12f) c = cloud;
                    float cu2 = (u - 0.7f) * 1.333f, cv2 = (v - 0.86f) * 2.2f;
                    if (cu2 * cu2 + cv2 * cv2 < 0.1f * 0.1f) c = cloud;
                    if (v < 0.4f + 0.03f * Mathf.Sin(u * 18f)) c = sea;
                    if (v > 0.36f && v < 0.44f && u > 0.38f && u < 0.66f && (v - 0.36f) < (u - 0.38f) * 1.5f && (v - 0.36f) < (0.66f - u) * 1.5f + 0.02f) c = hull;
                    if (v >= 0.44f && v < 0.74f && u > 0.50f && u < 0.50f + 0.14f * (1f - (v - 0.44f) / 0.30f)) c = sail;
                    px[y * w + x] = c;
                }
                break;
            }
            case 2: // Ay ışığında dağlar
            {
                Color night = C(0.1f, 0.13f, 0.32f), moon = C(0.98f, 0.94f, 0.7f), mount = C(0.45f, 0.47f, 0.55f), snow = C(0.97f, 0.97f, 0.95f), ground = C(0.15f, 0.35f, 0.25f);
                palette = new[] { night, moon, mount, snow, ground, C(0.1f, 0.1f, 0.1f) };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = night;
                    float mu = (u - 0.75f) * 1.333f, mv = v - 0.78f;
                    if (mu * mu + mv * mv < 0.1f * 0.1f) c = moon;
                    float top = Mathf.Max(0.7f - Mathf.Abs(u - 0.32f) * 1.6f, 0.58f - Mathf.Abs(u - 0.7f) * 1.4f);
                    if (v < top) c = (v > top - 0.08f && top > 0.5f) ? snow : mount;
                    if (v < 0.18f) c = ground;
                    px[y * w + x] = c;
                }
                break;
            }
            case 3: // Renk blokları
            {
                Color white = C(0.96f, 0.95f, 0.9f), black = C(0.08f, 0.08f, 0.08f), red = C(0.82f, 0.15f, 0.12f), blue = C(0.12f, 0.22f, 0.6f), yellow = C(0.98f, 0.82f, 0.12f);
                palette = new[] { white, black, red, blue, yellow };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = white;
                    if (u < 0.38f && v > 0.45f) c = red;
                    if (u > 0.72f && v < 0.3f) c = blue;
                    if (u > 0.72f && v > 0.82f) c = yellow;
                    bool line = Mathf.Abs(u - 0.38f) < 0.02f || Mathf.Abs(u - 0.72f) < 0.02f || Mathf.Abs(v - 0.45f) < 0.027f ||
                                (u > 0.72f && (Mathf.Abs(v - 0.3f) < 0.027f || Mathf.Abs(v - 0.82f) < 0.027f));
                    if (line) c = black;
                    px[y * w + x] = c;
                }
                break;
            }
            case 4: // Natürmort
            {
                Color wallC = C(0.88f, 0.8f, 0.62f), table = C(0.45f, 0.28f, 0.15f), vase = C(0.15f, 0.3f, 0.65f), apple = C(0.82f, 0.18f, 0.15f), leaf = C(0.3f, 0.6f, 0.3f);
                palette = new[] { wallC, table, vase, apple, leaf, C(0.97f, 0.97f, 0.95f) };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = wallC;
                    if (v < 0.3f) c = table;
                    float vu = (u - 0.4f) * 1.333f, vv = (v - 0.5f) * 1.1f;
                    if (vu * vu + vv * vv < 0.17f * 0.17f) c = vase;
                    if (Mathf.Abs(u - 0.4f) < 0.04f && v > 0.6f && v < 0.75f) c = vase;
                    if (v > 0.75f && v < 0.86f && Mathf.Abs(u - 0.4f) < 0.09f - (v - 0.75f) * 0.4f) c = leaf;
                    float a1u = (u - 0.68f) * 1.333f, a1v = v - 0.37f;
                    if (a1u * a1u + a1v * a1v < 0.075f * 0.075f) c = apple;
                    float a2u = (u - 0.8f) * 1.333f, a2v = v - 0.35f;
                    if (a2u * a2u + a2v * a2v < 0.065f * 0.065f) c = apple;
                    px[y * w + x] = c;
                }
                break;
            }
            case 6: // Deniz feneri
            {
                Color sky = C(0.6f, 0.82f, 0.96f), sea = C(0.1f, 0.32f, 0.58f), rock = C(0.4f, 0.38f, 0.36f), white = C(0.97f, 0.97f, 0.95f), red = C(0.82f, 0.18f, 0.15f), lamp = C(0.99f, 0.86f, 0.3f);
                palette = new[] { sky, sea, rock, white, red, lamp };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = sky;
                    if (v < 0.36f + 0.02f * Mathf.Sin(u * 22f)) c = sea;
                    float ru = (u - 0.3f) * 1.333f, rv = (v - 0.1f) * 1.4f;
                    if (ru * ru + rv * rv < 0.26f * 0.26f) c = rock;
                    float half = 0.06f - (v - 0.32f) * 0.04f;
                    if (v > 0.32f && v < 0.74f && Mathf.Abs(u - 0.3f) < half)
                        c = Mathf.Repeat((v - 0.32f) / 0.105f, 1f) < 0.5f ? white : red;
                    if (v >= 0.74f && v < 0.84f && Mathf.Abs(u - 0.3f) < 0.045f) c = lamp;
                    if (v >= 0.84f && v < 0.9f && Mathf.Abs(u - 0.3f) < 0.055f - (v - 0.84f) * 0.8f) c = red;
                    px[y * w + x] = c;
                }
                break;
            }
            case 7: // Lale tarlaları
            {
                Color sky = C(0.62f, 0.8f, 0.97f), red = C(0.86f, 0.15f, 0.2f), yellow = C(0.98f, 0.82f, 0.15f), pink = C(0.95f, 0.5f, 0.68f), green = C(0.3f, 0.6f, 0.28f), sun = C(1f, 0.96f, 0.7f);
                palette = new[] { sky, red, yellow, pink, green, sun };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = sky;
                    float su = (u - 0.2f) * 1.333f, sv = v - 0.8f;
                    if (su * su + sv * sv < 0.09f * 0.09f) c = sun;
                    float hz = 0.5f + 0.04f * Mathf.Sin(u * 4f);
                    if (v < hz) c = green;
                    // Perspektifli şeritler: ufka doğru incelir
                    if (v < hz - 0.04f)
                    {
                        float band = (u - 0.5f) / Mathf.Max(0.05f, (hz - v) * 1.4f);
                        int idx = Mathf.FloorToInt(band * 1.5f + 100f) % 4;
                        c = idx == 0 ? red : idx == 1 ? green : idx == 2 ? yellow : pink;
                    }
                    px[y * w + x] = c;
                }
                break;
            }
            case 8: // Çöl ve piramitler
            {
                Color sky = C(0.98f, 0.78f, 0.45f), sand = C(0.9f, 0.72f, 0.42f), pyr = C(0.78f, 0.58f, 0.3f), shade = C(0.58f, 0.4f, 0.2f), sun = C(1f, 0.95f, 0.75f), dune = C(0.82f, 0.62f, 0.34f);
                palette = new[] { sky, sand, pyr, shade, sun, dune };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = sky;
                    float su = (u - 0.78f) * 1.333f, sv = v - 0.78f;
                    if (su * su + sv * sv < 0.1f * 0.1f) c = sun;
                    float base0 = 0.3f;
                    float t1 = base0 + 0.42f - Mathf.Abs(u - 0.35f) * 1.4f;
                    if (v > base0 && v < t1) c = u < 0.35f ? pyr : shade;
                    float t2 = base0 + 0.26f - Mathf.Abs(u - 0.66f) * 1.3f;
                    if (v > base0 && v < t2) c = u < 0.66f ? pyr : shade;
                    if (v <= base0) c = v < 0.14f + 0.04f * Mathf.Sin(u * 7f) ? dune : sand;
                    px[y * w + x] = c;
                }
                break;
            }
            case 9: // Kiraz çiçeği
            {
                Color sky = C(0.82f, 0.9f, 0.97f), trunk = C(0.35f, 0.22f, 0.14f), bloom = C(0.98f, 0.7f, 0.8f), deep = C(0.9f, 0.45f, 0.6f), ground = C(0.45f, 0.65f, 0.38f), white = C(0.98f, 0.97f, 0.95f);
                palette = new[] { sky, trunk, bloom, deep, ground, white };
                float[] bx = { 0.3f, 0.48f, 0.62f, 0.76f, 0.4f };
                float[] by = { 0.72f, 0.82f, 0.72f, 0.66f, 0.62f };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = sky;
                    if (v < 0.16f) c = ground;
                    if (v >= 0.14f && v < 0.6f && Mathf.Abs(u - 0.42f - (v - 0.14f) * 0.15f) < 0.035f) c = trunk;
                    if (v > 0.45f && v < 0.62f && Mathf.Abs((v - 0.45f) - (u - 0.48f) * 0.6f) < 0.025f && u > 0.48f && u < 0.75f) c = trunk;
                    for (int k = 0; k < bx.Length; k++)
                    {
                        float du = (u - bx[k]) * 1.333f, dv = v - by[k];
                        if (du * du + dv * dv < 0.12f * 0.12f) c = k % 2 == 0 ? bloom : deep;
                    }
                    float pu = (u - 0.86f) * 1.333f, pv = v - 0.25f;
                    if (pu * pu + pv * pv < 0.03f * 0.03f) c = white;
                    px[y * w + x] = c;
                }
                break;
            }
            case 10: // Bir hanımın portresi
            {
                Color bg = C(0.18f, 0.3f, 0.22f), skin = C(0.93f, 0.78f, 0.62f), hair = C(0.32f, 0.2f, 0.12f), dress = C(0.12f, 0.12f, 0.2f), collar = C(0.97f, 0.97f, 0.95f), lips = C(0.78f, 0.25f, 0.25f);
                palette = new[] { bg, skin, hair, dress, collar, lips };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = bg;
                    float hu = (u - 0.5f) * 1.333f, hv = (v - 0.62f) * 0.95f;
                    if (hu * hu + hv * hv < 0.25f * 0.25f) c = hair;
                    float fu = (u - 0.5f) * 1.333f, fv = (v - 0.58f) * 0.85f;
                    if (fu * fu / (0.15f * 0.15f) + fv * fv / (0.17f * 0.17f) < 1f) c = skin;
                    if (Mathf.Abs(u - 0.5f) < 0.035f && v > 0.44f && v < 0.47f) c = lips;
                    if (v < 0.3f && Mathf.Abs(u - 0.5f) < 0.38f - v * 0.3f) c = dress;
                    if (v < 0.3f && v > 0.24f && Mathf.Abs(u - 0.5f) < 0.1f) c = collar;
                    if (v >= 0.3f && v < 0.38f && Mathf.Abs(u - 0.5f) < 0.05f) c = skin; // boyun
                    px[y * w + x] = c;
                }
                break;
            }
            case 11: // Daireler
            {
                Color bg = C(0.94f, 0.9f, 0.8f), red = C(0.85f, 0.2f, 0.15f), blue = C(0.15f, 0.3f, 0.7f), yellow = C(0.98f, 0.8f, 0.15f), black = C(0.08f, 0.08f, 0.08f), green = C(0.2f, 0.55f, 0.35f);
                palette = new[] { bg, red, blue, yellow, black, green };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = bg;
                    float ax = (u - 0.32f) * 1.333f, ay = v - 0.55f, ad = ax * ax + ay * ay;
                    if (ad < 0.3f * 0.3f) c = ad > 0.25f * 0.25f ? black : blue;
                    float bx2 = (u - 0.68f) * 1.333f, by2 = v - 0.42f;
                    if (bx2 * bx2 + by2 * by2 < 0.22f * 0.22f) c = red;
                    float cx2 = (u - 0.7f) * 1.333f, cy2 = v - 0.8f;
                    if (cx2 * cx2 + cy2 * cy2 < 0.1f * 0.1f) c = yellow;
                    float dx2 = (u - 0.15f) * 1.333f, dy2 = v - 0.15f;
                    if (dx2 * dx2 + dy2 * dy2 < 0.08f * 0.08f) c = green;
                    px[y * w + x] = c;
                }
                break;
            }
            default: // 5: Gün batımı
            {
                Color orange = C(0.98f, 0.6f, 0.2f), red = C(0.85f, 0.25f, 0.18f), sun = C(0.99f, 0.88f, 0.3f), sea = C(0.35f, 0.18f, 0.4f), dark = C(0.15f, 0.1f, 0.15f);
                palette = new[] { orange, red, sun, sea, dark, C(0.97f, 0.97f, 0.95f) };
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = v > 0.72f ? orange : red;
                    float su = (u - 0.5f) * 1.333f, sv = v - 0.42f;
                    if (su * su + sv * sv < 0.18f * 0.18f && v > 0.42f) c = sun;
                    if (v < 0.42f) c = sea;
                    if (Mathf.Abs(u - 0.18f) < 0.02f && v < 0.7f && v > 0.2f) c = dark;
                    float lu = (u - 0.18f) * 1.333f, lv = (v - 0.72f) * 2.5f;
                    if (lu * lu + lv * lv < 0.1f * 0.1f) c = dark;
                    px[y * w + x] = c;
                }
                break;
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
