using System.Collections.Generic;
using UnityEngine;

/// <summary>Atölyede kopyalanacak bir büstün görünümü (kalıptan şekillendirme, GDD Bölüm 3).</summary>
public sealed class BustLook
{
    public const int SliderCount = 6; // baş genişliği, baş yüksekliği, burun, çene, omuz, kaide

    public string[] name;
    public float[] sliders = new float[SliderCount];
    public int stone, baseColor;

    public string Name => MuseumTheme.Pick(name);
}

/// <summary>Atölyede kopyalanacak bir yüzüğün görünümü (kuyumcu tezgahı, GDD Bölüm 3).</summary>
public sealed class JewelLook
{
    public const int MaxStones = 4;

    public string[] name;
    public int style, metal, stoneCount;
    public int[] stones = new int[MaxStones];
    /// <summary>Taşların halka üzerindeki açıları (derece; 90 = tepe).</summary>
    public float[] angles = new float[MaxStones];

    public string Name => MuseumTheme.Pick(name);
}

/// <summary>Çalışma anında üretilen mesh / doku / malzemeleri görselle birlikte yok eder (bellek sızmasın).</summary>
public sealed class RuntimeResources : MonoBehaviour
{
    private readonly List<Object> owned = new List<Object>();

    public void Own(Object o) { if (o != null) owned.Add(o); }

    private void OnDestroy()
    {
        foreach (Object o in owned) if (o != null) Destroy(o);
        owned.Clear();
    }
}

public static partial class ArtworkLook
{
    public static readonly Color[] BustStones =
    {
        new Color(0.92f, 0.92f, 0.90f), // mermer
        new Color(0.55f, 0.40f, 0.20f), // bronz
        new Color(0.76f, 0.46f, 0.32f), // pişmiş toprak
        new Color(0.26f, 0.26f, 0.30f), // bazalt
        new Color(0.42f, 0.70f, 0.52f), // yeşim
        new Color(0.92f, 0.76f, 0.32f), // altın varak
    };
    public static readonly string[][] BustStoneNames = { N("Marble", "Mermer"), N("Bronze", "Bronz"), N("Terracotta", "Pişmiş toprak"), N("Basalt", "Bazalt"), N("Jade", "Yeşim"), N("Gilded", "Altın varak") };

    public static readonly Color[] BustBases =
    {
        new Color(0.30f, 0.20f, 0.12f), // koyu ahşap
        new Color(0.12f, 0.12f, 0.14f), // siyah mermer
        new Color(0.90f, 0.90f, 0.88f), // beyaz
        new Color(0.20f, 0.42f, 0.32f), // yeşil taş
        new Color(0.58f, 0.18f, 0.18f), // kırmızı mermer
        new Color(0.18f, 0.28f, 0.58f), // lacivert
    };
    public static readonly string[][] BustBaseNames = { N("Wood", "Ahşap"), N("Black", "Siyah"), N("White", "Beyaz"), N("Green", "Yeşil"), N("Red", "Kırmızı"), N("Navy", "Lacivert") };

    public static readonly string[][] BustSliderNames =
    {
        N("Head width", "Baş genişliği"), N("Head height", "Baş yüksekliği"), N("Nose", "Burun"),
        N("Jaw", "Çene"), N("Shoulders", "Omuzlar"), N("Base", "Kaide"),
    };

    static readonly string[][] BustNames =
    {
        N("Bust of a Philosopher", "Filozof Büstü"), N("Emperor's Bust", "İmparator Büstü"),
        N("Bust of a Poet", "Şair Büstü"), N("Bust of a Queen", "Kraliçe Büstü"),
        N("General's Bust", "General Büstü"), N("Bust of a Scholar", "Âlim Büstü"), N("Bust of a Dancer", "Dansçı Büstü"),
        N("Bust of a Sultan", "Sultan Büstü"),
    };

    public static readonly Color[] JewelMetals =
    {
        new Color(1.00f, 0.80f, 0.25f), // altın
        new Color(0.85f, 0.87f, 0.90f), // gümüş
        new Color(0.80f, 0.50f, 0.30f), // bakır
        new Color(0.95f, 0.65f, 0.60f), // roze
        new Color(0.22f, 0.22f, 0.25f), // oksitli (siyah)
        new Color(0.98f, 0.95f, 0.80f), // beyaz altın
    };
    public static readonly string[][] JewelMetalNames = { N("Gold", "Altın"), N("Silver", "Gümüş"), N("Copper", "Bakır"), N("Rose", "Roze"), N("Black", "Siyah"), N("White gold", "Beyaz altın") };

    public static readonly Color[] JewelGems =
    {
        new Color(0.85f, 0.10f, 0.15f), // yakut
        new Color(0.12f, 0.25f, 0.80f), // safir
        new Color(0.10f, 0.65f, 0.30f), // zümrüt
        new Color(0.90f, 0.95f, 1.00f), // elmas
        new Color(0.60f, 0.25f, 0.78f), // ametist
        new Color(1.00f, 0.62f, 0.12f), // topaz
        new Color(0.40f, 0.86f, 0.90f), // akuamarin
    };
    public static readonly string[][] JewelGemNames = { N("Ruby", "Yakut"), N("Sapphire", "Safir"), N("Emerald", "Zümrüt"), N("Diamond", "Elmas"), N("Amethyst", "Ametist"), N("Topaz", "Topaz"), N("Aquamarine", "Akuamarin") };

    public static readonly string[][] JewelStyleNames = { N("Thin band", "İnce halka"), N("Thick band", "Kalın halka"), N("Signet", "Mühür") };

    static readonly string[][] JewelNames =
    {
        N("Signet Ring", "Mühür Yüzüğü"), N("Royal Ring", "Kraliyet Yüzüğü"),
        N("Heirloom Ring", "Yadigâr Yüzük"), N("Coronation Ring", "Taç Giyme Yüzüğü"),
        N("Duchess's Ring", "Düşes Yüzüğü"), N("Sultan's Ring", "Sultan Yüzüğü"), N("Pharaoh's Ring", "Firavun Yüzüğü"),
    };

    static readonly Dictionary<string, BustLook> bustCache = new Dictionary<string, BustLook>();
    static readonly Dictionary<string, JewelLook> jewelCache = new Dictionary<string, JewelLook>();

    public static BustLook Bust(int seed, int themeIndex, string key)
    {
        string ck = seed + "|" + themeIndex + "|" + key;
        if (bustCache.TryGetValue(ck, out BustLook cached)) return cached;

        var rng = new System.Random(seed ^ Hash("bust:" + key));
        var b = new BustLook { name = BustNames[rng.Next(BustNames.Length)] };
        for (int i = 0; i < BustLook.SliderCount; i++) b.sliders[i] = rng.Next(1, 10) / 10f; // 0.1 .. 0.9
        b.stone = rng.Next(BustStones.Length);
        b.baseColor = rng.Next(BustBases.Length);
        bustCache[ck] = b;
        return b;
    }

    public static JewelLook Jewel(int seed, int themeIndex, string key)
    {
        string ck = seed + "|" + themeIndex + "|" + key;
        if (jewelCache.TryGetValue(ck, out JewelLook cached)) return cached;

        var rng = new System.Random(seed ^ Hash("jewel:" + key));
        var j = new JewelLook
        {
            name = JewelNames[rng.Next(JewelNames.Length)],
            style = rng.Next(JewelStyleNames.Length),
            metal = rng.Next(JewelMetals.Length),
            stoneCount = rng.Next(1, JewelLook.MaxStones + 1),
        };
        for (int i = 0; i < JewelLook.MaxStones; i++) j.stones[i] = rng.Next(JewelGems.Length);

        // Taş açıları: tepe yayında (40°–140°), aralarında en az 22°
        float span = 100f, gap = 22f;
        float free = span - gap * Mathf.Max(0, j.stoneCount - 1);
        var offs = new float[j.stoneCount];
        for (int i = 0; i < offs.Length; i++) offs[i] = (float)rng.NextDouble() * free;
        System.Array.Sort(offs);
        for (int i = 0; i < JewelLook.MaxStones; i++)
            j.angles[i] = i < j.stoneCount ? 140f - (offs[i] + gap * i) : 90f;
        if (j.stoneCount == 1) j.angles[0] = 90f + ((float)rng.NextDouble() - 0.5f) * 40f;
        jewelCache[ck] = j;
        return j;
    }

    // ---------------------------------------------------------------
    // Büst / yüzük görselleri (orijinal ve sahte aynı yerden kurulur). Pivot alt-orta, ön yüz -Z.
    // ---------------------------------------------------------------

    public static GameObject BuildBustVisual(float[] s, int stone, int baseColor)
    {
        var root = new GameObject("Bust");
        Material stoneMat = MakeLit(BustStones[Mathf.Clamp(stone, 0, BustStones.Length - 1)]);
        Material baseMat = MakeLit(BustBases[Mathf.Clamp(baseColor, 0, BustBases.Length - 1)]);

        float bh = Mathf.Lerp(0.06f, 0.18f, s[5]);
        float headW = Mathf.Lerp(0.17f, 0.27f, s[0]);
        float headH = Mathf.Lerp(0.20f, 0.32f, s[1]);
        float noseL = Mathf.Lerp(0.03f, 0.12f, s[2]);
        float jawW = Mathf.Lerp(0.12f, 0.24f, s[3]);
        float shoulderW = Mathf.Lerp(0.30f, 0.50f, s[4]);
        float headY = bh + 0.24f + headH * 0.5f;

        Part(root, PrimitiveType.Cube, new Vector3(0f, bh * 0.5f, 0f), new Vector3(0.34f, bh, 0.26f), baseMat);
        Part(root, PrimitiveType.Sphere, new Vector3(0f, bh + 0.07f, 0f), new Vector3(shoulderW, 0.17f, 0.22f), stoneMat);
        Part(root, PrimitiveType.Cylinder, new Vector3(0f, bh + 0.19f, 0f), new Vector3(0.10f, 0.07f, 0.10f), stoneMat);
        Part(root, PrimitiveType.Sphere, new Vector3(0f, headY, 0f), new Vector3(headW, headH, 0.22f), stoneMat);
        Part(root, PrimitiveType.Sphere, new Vector3(0f, headY - headH * 0.32f, -0.015f), new Vector3(jawW, 0.13f, 0.19f), stoneMat);
        Part(root, PrimitiveType.Cube, new Vector3(0f, headY - 0.01f, -(0.10f + noseL * 0.5f)), new Vector3(0.035f, 0.06f, noseL), stoneMat);
        return root;
    }

    public static GameObject BuildBustVisual(BustLook look) => BuildBustVisual(look.sliders, look.stone, look.baseColor);

    /// <summary>Yüzük görseli: kalınlık dökümdeki doluluktan, parlaklık cilâdan, taşlar açılarından gelir.</summary>
    public static GameObject BuildJewelVisual(JewelCraft c)
    {
        var root = new GameObject("Ring");
        Color metalC = JewelMetals[Mathf.Clamp(c.metal, 0, JewelMetals.Length - 1)];
        float pol = Mathf.Clamp01(c.polish);
        Material metalMat = MakeLit(Color.Lerp(metalC * 0.7f, metalC, pol));
        if (metalMat.HasProperty("_Metallic")) metalMat.SetFloat("_Metallic", Mathf.Lerp(0.4f, 0.9f, pol));
        if (metalMat.HasProperty("_Smoothness")) metalMat.SetFloat("_Smoothness", Mathf.Lerp(0.15f, 0.85f, pol));

        const float R = 0.09f;
        int style = Mathf.Clamp(c.style, 0, 2);
        float baseR = style == 1 ? 0.022f : style == 2 ? 0.016f : 0.011f;
        float r = baseR * Mathf.Clamp(c.fill / JewelCraft.TargetFill(style), 0.45f, 1.7f);
        Vector3 center = new Vector3(0f, R + r, 0f);

        var band = new GameObject("Band");
        band.transform.SetParent(root.transform, false);
        band.transform.localPosition = center;
        band.AddComponent<MeshFilter>().sharedMesh = Torus(R, r, 40, 12);
        band.AddComponent<MeshRenderer>().sharedMaterial = metalMat;

        float top = R + r;
        if (style == 2) // mühür: üstte yassı tabla
        {
            Part(root, PrimitiveType.Cube, center + new Vector3(0f, top + 0.004f, 0f), new Vector3(0.085f, 0.022f, 0.06f), metalMat);
            top += 0.014f;
        }

        int n = Mathf.Clamp(c.count, 0, JewelLook.MaxStones);
        for (int i = 0; i < n; i++)
        {
            float rad = c.angles[i] * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            int gem = Mathf.Clamp(c.gems[i], 0, JewelGems.Length - 1);
            Material gemMat = MakeLit(JewelGems[gem]);
            if (gemMat.HasProperty("_Smoothness")) gemMat.SetFloat("_Smoothness", 0.9f);
            float rr = Mathf.Abs(c.angles[i] - 90f) < 30f && style == 2 ? top : R + r;
            Part(root, PrimitiveType.Sphere, center + dir * (rr + 0.012f), Vector3.one * 0.038f, gemMat);
        }
        return root;
    }

    public static GameObject BuildJewelVisual(JewelLook look) => BuildJewelVisual(JewelCraft.FromLook(look));

    static void Part(GameObject parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(type);
        KillCollider(go);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    /// <summary>XY düzleminde duran halka (ekseni Z).</summary>
    public static Mesh Torus(float R, float r, int seg, int sides)
    {
        var mesh = new Mesh { name = "Torus" };
        var verts = new Vector3[(seg + 1) * (sides + 1)];
        var tris = new int[seg * sides * 6];
        int vi = 0;
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            Vector3 c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            for (int j = 0; j <= sides; j++)
            {
                float b = j * Mathf.PI * 2f / sides;
                verts[vi++] = c * (R + r * Mathf.Cos(b)) + Vector3.forward * (r * Mathf.Sin(b));
            }
        }
        int ti = 0;
        for (int i = 0; i < seg; i++)
            for (int j = 0; j < sides; j++)
            {
                int cur = i * (sides + 1) + j, next = cur + sides + 1;
                tris[ti++] = cur; tris[ti++] = next; tris[ti++] = cur + 1;
                tris[ti++] = cur + 1; tris[ti++] = next; tris[ti++] = next + 1;
            }
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------
    // Orijinal eser görseli (kaide, el, yer — hepsi buradan)
    // ---------------------------------------------------------------

    /// <summary>Bir kaidenin orijinal eseri. similarity verilirse (eski, verisi olmayan sahteler) bozuk görünür.</summary>
    public static GameObject BuildOriginalVisual(ArtKind kind, int seed, int theme, string key, byte similarity = InventorySlot.NoSimilarity)
    {
        switch (kind)
        {
            case ArtKind.Vase: return BuildVaseVisual(Vase(seed, theme, key), similarity);
            case ArtKind.Painting: return BuildPaintingVisual(Painting(seed, theme, key), similarity);
            case ArtKind.Bust: return BuildBustVisual(Bust(seed, theme, key));
            case ArtKind.Jewelry: return BuildJewelVisual(Jewel(seed, theme, key));
            default: return null;
        }
    }

    // ---------------------------------------------------------------
    // Sahte görseli: oyuncunun atölyede yaptığının AYNISI
    // ---------------------------------------------------------------

    public static GameObject BuildForgeryVisual(ForgeryData d)
    {
        if (d == null) return null;
        GameObject go = null;
        Texture2D tex = null;
        Mesh mesh = null;

        switch (d.kind)
        {
            case ArtKind.Painting:
                tex = d.BuildTexture();
                if (tex == null) return null;
                go = BuildFramedPicture(tex, 0.9f, 0.675f);
                break;

            case ArtKind.Vase:
            {
                int radial = d.VaseRadial, rows = d.VaseRows;
                if (radial < 3 || rows < 1 || d.shape.Length < (radial + 1) * (rows + 1))
                {
                    Debug.LogWarning($"[Forgery] Vazo verisi eksik: dilim {radial}, satır {rows}, şekil {d.shape.Length} bayt.");
                    return null;
                }
                tex = d.BuildTexture();
                mesh = GridLathe((row, seg) => d.VaseRadiusAt(row, seg), radial, rows, d.VaseHeight);
                go = new GameObject("ForgedVase");
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = MakeLit(Color.white, tex);
                break;
            }

            case ArtKind.Bust:
            {
                if (d.pars.Length < BustLook.SliderCount + 2) return null;
                var s = new float[BustLook.SliderCount];
                for (int i = 0; i < s.Length; i++) s[i] = d.pars[i] / 255f;
                go = BuildBustVisual(s, d.pars[BustLook.SliderCount], d.pars[BustLook.SliderCount + 1]);
                break;
            }

            case ArtKind.Jewelry:
            {
                if (!JewelCraft.TryFromPars(d.pars, out JewelCraft jc)) return null;
                go = BuildJewelVisual(jc);
                break;
            }
        }
        if (go == null) return null;

        // Bu görsel için üretilen her şey onunla birlikte yok olsun
        var res = go.AddComponent<RuntimeResources>();
        res.Own(tex);
        res.Own(mesh);
        var seen = new HashSet<Material>();
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            if (r.sharedMaterial != null && seen.Add(r.sharedMaterial)) res.Own(r.sharedMaterial);
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.sharedMesh.name == "Torus") res.Own(mf.sharedMesh);
        return go;
    }

    // ---------------------------------------------------------------
    // 2D ikonlar (çanta, karaborsa, brifing): eserin kendi görünümünden çizilir
    // ---------------------------------------------------------------
    const int IconSize = 96;
    static readonly Color IconBg = new Color(0.16f, 0.17f, 0.2f, 1f);
    static readonly Dictionary<string, Texture2D> originalIcons = new Dictionary<string, Texture2D>();

    /// <summary>Müzedeki orijinal eserin ikonu (tohum + kaide adına göre; önbellekli).</summary>
    public static Texture2D OriginalIcon(ArtKind kind, int seed, int theme, string key)
    {
        string ck = (int)kind + "|" + seed + "|" + theme + "|" + key;
        if (originalIcons.TryGetValue(ck, out Texture2D t) && t != null) return t;
        switch (kind)
        {
            case ArtKind.Vase: t = VaseIcon(Vase(seed, theme, key), IconSize); break;
            case ArtKind.Painting: t = Painting(seed, theme, key).image; break;
            case ArtKind.Bust: { BustLook b = Bust(seed, theme, key); t = BustIcon(b.sliders, b.stone, b.baseColor); break; }
            case ArtKind.Jewelry: t = JewelIcon(JewelCraft.FromLook(Jewel(seed, theme, key))); break;
            default: return null;
        }
        originalIcons[ck] = t;
        return t;
    }

    static Color[] NewIcon()
    {
        var px = new Color[IconSize * IconSize];
        for (int i = 0; i < px.Length; i++) px[i] = IconBg;
        return px;
    }

    static Texture2D FinishIcon(Color[] px, string name)
    {
        var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = name };
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>İçi dolu elips (piksel koordinatı, y yukarı). inner > 0 ise halka çizer.</summary>
    static void Ellipse(Color[] px, float cx, float cy, float rx, float ry, Color c, float inner = 0f)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx)), x1 = Mathf.Min(IconSize - 1, Mathf.CeilToInt(cx + rx));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry)), y1 = Mathf.Min(IconSize - 1, Mathf.CeilToInt(cy + ry));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - cx) / Mathf.Max(0.5f, rx), dy = (y - cy) / Mathf.Max(0.5f, ry);
                float d = dx * dx + dy * dy;
                if (d > 1f || d < inner * inner) continue;
                float shade = 1f - Mathf.Clamp01(dx * 0.5f + 0.5f) * 0.35f; // soldan ışık
                px[y * IconSize + x] = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
            }
    }

    static void Block(Color[] px, float cx, float cy, float w, float h, Color c)
    {
        int x0 = Mathf.Max(0, Mathf.RoundToInt(cx - w * 0.5f)), x1 = Mathf.Min(IconSize - 1, Mathf.RoundToInt(cx + w * 0.5f));
        int y0 = Mathf.Max(0, Mathf.RoundToInt(cy - h * 0.5f)), y1 = Mathf.Min(IconSize - 1, Mathf.RoundToInt(cy + h * 0.5f));
        c.a = 1f;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) px[y * IconSize + x] = c;
    }

    /// <summary>Büstün önden görünüşü (BuildBustVisual ile aynı ölçüler).</summary>
    public static Texture2D BustIcon(float[] s, int stone, int baseColor)
    {
        Color[] px = NewIcon();
        Color st = BustStones[Mathf.Clamp(stone, 0, BustStones.Length - 1)];
        Color bc = BustBases[Mathf.Clamp(baseColor, 0, BustBases.Length - 1)];
        const float M = 112f;            // metre → piksel
        float cx = IconSize * 0.5f, y0 = 6f;

        float bh = Mathf.Lerp(0.06f, 0.18f, s[5]);
        float headW = Mathf.Lerp(0.17f, 0.27f, s[0]);
        float headH = Mathf.Lerp(0.20f, 0.32f, s[1]);
        float noseL = Mathf.Lerp(0.03f, 0.12f, s[2]);
        float jawW = Mathf.Lerp(0.12f, 0.24f, s[3]);
        float shoulderW = Mathf.Lerp(0.30f, 0.50f, s[4]);
        float headY = bh + 0.24f + headH * 0.5f;

        Block(px, cx, y0 + bh * 0.5f * M, 0.34f * M, bh * M, bc);
        Ellipse(px, cx, y0 + (bh + 0.07f) * M, shoulderW * 0.5f * M, 0.085f * M, st);
        Block(px, cx, y0 + (bh + 0.19f) * M, 0.10f * M, 0.14f * M, st * 0.9f);
        Ellipse(px, cx, y0 + (headY - headH * 0.32f) * M, jawW * 0.5f * M, 0.065f * M, st);
        Ellipse(px, cx, y0 + headY * M, headW * 0.5f * M, headH * 0.5f * M, st);
        // Burun: uzadıkça önden daha belirgin görünür
        Block(px, cx, y0 + (headY - 0.01f) * M, 0.035f * M, (0.05f + noseL * 0.4f) * M, st * 0.78f);
        return FinishIcon(px, "BustIcon");
    }

    /// <summary>Yüzüğün önden görünüşü (BuildJewelVisual ile aynı düzen).</summary>
    public static Texture2D JewelIcon(JewelCraft c)
    {
        Color[] px = NewIcon();
        Color m = JewelMetals[Mathf.Clamp(c.metal, 0, JewelMetals.Length - 1)];
        int style = Mathf.Clamp(c.style, 0, 2);
        float cx = IconSize * 0.5f, cy = 40f, R = 28f;
        float thick = (style == 1 ? 8f : style == 2 ? 6f : 4f) * Mathf.Clamp(c.fill / JewelCraft.TargetFill(style), 0.45f, 1.7f);

        Ellipse(px, cx, cy, R + thick * 0.5f, R + thick * 0.5f, m, (R - thick * 0.5f) / (R + thick * 0.5f));
        float top = R + thick * 0.5f;
        if (style == 2) Block(px, cx, cy + top + 2f, 26f, 7f, m * 0.9f);

        int n = Mathf.Clamp(c.count, 0, JewelLook.MaxStones);
        for (int i = 0; i < n; i++)
        {
            float rad = c.angles[i] * Mathf.Deg2Rad;
            int gem = Mathf.Clamp(c.gems[i], 0, JewelGems.Length - 1);
            Ellipse(px, cx + Mathf.Cos(rad) * (top + 3f), cy + Mathf.Sin(rad) * (top + 3f), 6.5f, 6.5f, JewelGems[gem]);
        }
        return FinishIcon(px, "RingIcon");
    }

    /// <summary>Çanta ikonu: oyuncunun yaptığı sahtenin kendi görünümü (tablo: resim, vazo: silüet, büst / yüzük: önden çizim).</summary>
    public static Texture2D ForgeryIcon(ForgeryData d)
    {
        if (d == null) return null;
        if (d.kind == ArtKind.Painting) return d.BuildTexture();
        if (d.kind == ArtKind.Bust && d.pars.Length >= BustLook.SliderCount + 2)
        {
            var s = new float[BustLook.SliderCount];
            for (int i = 0; i < s.Length; i++) s[i] = d.pars[i] / 255f;
            return BustIcon(s, d.pars[BustLook.SliderCount], d.pars[BustLook.SliderCount + 1]);
        }
        if (d.kind == ArtKind.Jewelry && JewelCraft.TryFromPars(d.pars, out JewelCraft jc)) return JewelIcon(jc);
        if (d.kind != ArtKind.Vase || d.VaseRows < 1 || d.VaseRadial < 3) return null;

        const int size = IconSize;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ForgeryIcon" };
        var px = new Color[size * size];
        float h = d.VaseHeight, scale = d.VaseRadiusScale;
        float drawH = size * 0.88f;
        float pxPerMeter = drawH / Mathf.Max(0.01f, h);
        for (int y = 0; y < size; y++)
        {
            float t = (y - size * 0.06f) / drawH;
            int row = Mathf.Clamp(Mathf.RoundToInt(t * d.VaseRows), 0, d.VaseRows);
            float radius = 0f;
            for (int s = 0; s < d.VaseRadial; s++) radius += d.VaseRadiusAt(row, s);
            radius /= d.VaseRadial;
            float half = Mathf.Min(size * 0.48f, radius * pxPerMeter);
            for (int x = 0; x < size; x++)
            {
                Color c = IconBg;
                float dx = Mathf.Abs(x - size * 0.5f);
                if (t >= 0f && t <= 1f && dx <= half && scale > 0f)
                {
                    c = d.width > 0 ? d.PixelColor(d.width / 2, Mathf.Clamp((int)(t * d.height), 0, d.height - 1)) : ClayColor;
                    float shade = 1f - Mathf.Pow(dx / Mathf.Max(1f, half), 3f) * 0.45f;
                    c = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
                }
                px[y * size + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>Her noktası ayrı yarıçaplı döner gövde (kilin atölyedeki hali; ClaySculptor ile aynı düzen).</summary>
    public static Mesh GridLathe(System.Func<int, int, float> radiusAt, int radial, int rows, float height)
    {
        var mesh = new Mesh { name = "ForgedLathe" };
        int stride = radial + 1;
        int body = stride * (rows + 1);
        var verts = new Vector3[body + 2];
        var uvs = new Vector2[body + 2];
        float dA = Mathf.PI * 2f / radial;
        int vi = 0;
        for (int y = 0; y <= rows; y++)
            for (int s = 0; s <= radial; s++)
            {
                float a = s * dA;
                float r = radiusAt(y, s % radial == 0 ? 0 : s); // dikiş kapalı kalsın
                verts[vi] = new Vector3(Mathf.Cos(a) * r, y * height / rows, Mathf.Sin(a) * r);
                uvs[vi] = new Vector2((float)s / radial, (float)y / rows);
                vi++;
            }
        int bottom = vi++, top = vi++;
        verts[bottom] = Vector3.zero;
        verts[top] = new Vector3(0f, height, 0f);
        uvs[bottom] = new Vector2(0.5f, 0.02f);
        uvs[top] = new Vector2(0.5f, 0.98f);

        var tris = new int[radial * rows * 6 + radial * 6];
        int ti = 0;
        for (int y = 0; y < rows; y++)
            for (int s = 0; s < radial; s++)
            {
                int cur = y * stride + s, next = cur + stride;
                tris[ti++] = cur; tris[ti++] = next; tris[ti++] = cur + 1;
                tris[ti++] = cur + 1; tris[ti++] = next; tris[ti++] = next + 1;
            }
        for (int s = 0; s < radial; s++) { tris[ti++] = bottom; tris[ti++] = s + 1; tris[ti++] = s; }
        int topRow = rows * stride;
        for (int s = 0; s < radial; s++) { tris[ti++] = top; tris[ti++] = topRow + s; tris[ti++] = topRow + s + 1; }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}

/// <summary>
/// Benzerlik puanı (GDD Bölüm 4'ün basit karşılığı). SUNUCU sahtenin verisinden hesaplar;
/// istemcinin gönderdiği bir yüzdeye güvenilmez. Atölyedeki canlı göstergeler de aynı formülleri kullanır.
/// </summary>
public static class ForgeryScorer
{
    public const float VaseShapeWeight = 0.7f;

    /// <summary>0–100. Veri hedefe uymuyorsa 0.</summary>
    public static byte Score(ForgeryData d, int seed, int theme, string displayName)
    {
        if (d == null) return 0;
        float s;
        switch (d.kind)
        {
            case ArtKind.Painting: s = Painting(d, ArtworkLook.Painting(seed, theme, displayName)); break;
            case ArtKind.Vase:
            {
                VaseLook v = ArtworkLook.Vase(seed, theme, displayName);
                s = VaseShapeWeight * VaseShape(d, v) + (1f - VaseShapeWeight) * Pixels(d, v.pattern, 32, 32, 0.3f);
                break;
            }
            case ArtKind.Bust:
            {
                if (d.pars.Length < BustLook.SliderCount + 2) return 0;
                var sl = new float[BustLook.SliderCount];
                for (int i = 0; i < sl.Length; i++) sl[i] = d.pars[i] / 255f;
                s = Bust(sl, d.pars[BustLook.SliderCount], d.pars[BustLook.SliderCount + 1], ArtworkLook.Bust(seed, theme, displayName));
                break;
            }
            case ArtKind.Jewelry:
            {
                if (!JewelCraft.TryFromPars(d.pars, out JewelCraft jc)) return 0;
                s = JewelCraft.Score(jc, ArtworkLook.Jewel(seed, theme, displayName));
                break;
            }
            default: return 0;
        }
        return (byte)Mathf.Clamp(Mathf.RoundToInt(s * 100f), 0, 100);
    }

    /// <summary>Tablo: BÖLGE BÖLGE karşılaştırma (PaintingRegions). Sadece büyük alanları (gökyüzü, deniz) boyamak yetmez;
    /// tekne, bulut, güneş gibi küçük şekillerin her biri ayrı ayrı sayılır.</summary>
    public static float Painting(ForgeryData d, PaintingLook look)
    {
        if (d == null || look == null || d.width <= 0 || d.height <= 0) return 0f;
        return PaintingRegions.Score((u, v) => d.PixelColor(Mathf.Min(d.width - 1, (int)(u * d.width)), Mathf.Min(d.height - 1, (int)(v * d.height))),
                                     look, out _, out _);
    }

    /// <summary>Sahtenin pikselleri referansla nx×ny örnekte karşılaştırılır.</summary>
    static float Pixels(ForgeryData d, Texture2D reference, int nx, int ny, float tolerance)
    {
        if (reference == null || d.width <= 0 || d.height <= 0) return 0f;
        float total = 0f;
        for (int y = 0; y < ny; y++)
        {
            float v = (y + 0.5f) / ny;
            int py = Mathf.Min(d.height - 1, (int)(v * d.height));
            for (int x = 0; x < nx; x++)
            {
                float u = (x + 0.5f) / nx;
                Color a = d.PixelColor(Mathf.Min(d.width - 1, (int)(u * d.width)), py);
                Color b = reference.GetPixelBilinear(u, v);
                total += Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b)) / 1.732f;
            }
        }
        return Mathf.Clamp01(1f - total / (nx * ny) / tolerance);
    }

    static float VaseShape(ForgeryData d, VaseLook target)
    {
        int rows = d.VaseRows, radial = d.VaseRadial;
        if (rows < 1 || radial < 3 || d.shape.Length < (radial + 1) * (rows + 1)) return 0f;
        float total = 0f;
        for (int y = 0; y <= rows; y++)
        {
            float sum = 0f;
            for (int s = 0; s < radial; s++) sum += d.VaseRadiusAt(y, s);
            total += Mathf.Abs(sum / radial - target.Radius((float)y / rows)) / Mathf.Max(0.0001f, target.maxRadius);
        }
        float heightPenalty = Mathf.Abs(d.VaseHeight - target.height) / Mathf.Max(0.01f, target.height);
        return Mathf.Clamp01(1f - total / (rows + 1) / 0.25f - heightPenalty);
    }

    public static float Bust(float[] sliders, int stone, int baseColor, BustLook target)
    {
        float diff = 0f;
        for (int i = 0; i < BustLook.SliderCount; i++) diff += Mathf.Abs(sliders[i] - target.sliders[i]);
        float shape = Mathf.Clamp01(1f - diff / BustLook.SliderCount / 0.3f);
        return 0.7f * shape + (stone == target.stone ? 0.2f : 0f) + (baseColor == target.baseColor ? 0.1f : 0f);
    }

    public static float Jewel(int style, int metal, int stoneCount, int[] stones, JewelLook target)
    {
        float s = 0f;
        if (style == target.style) s += 0.3f;
        if (metal == target.metal) s += 0.25f;
        if (stoneCount == target.stoneCount) s += 0.15f;
        int match = 0;
        for (int i = 0; i < target.stoneCount; i++)
            if (i < stoneCount && stones[i] == target.stones[i]) match++;
        s += 0.3f * match / Mathf.Max(1, target.stoneCount);
        return Mathf.Clamp01(s);
    }
}

/// <summary>
/// Tablo benzerliği için referans resmin BÖLGELERİ: aynı renkteki bitişik alanlar (gökyüzü, deniz, tekne gövdesi,
/// yelken, her bir bulut...). Her bölgenin ne kadarının doğru renkle boyandığı ayrı ölçülür.
///   - Bölgenin ağırlığı alanının 0.4. kuvveti: büyük alan yine daha önemli ama küçük şekiller de ciddi pay alır
///     (ör. tekneli resimde gökyüzü + deniz tek başına ~%60, tekne/yelken/bulutlar olmadan "İyi"ye çıkamaz).
///   - Önemli bölgelerin (%1'den büyük) hepsi bitmeden puan ayrıca kırpılır.
///   - Renk eşleşmesi tablonun paletine göre yapılır: boş tuval rengi beyaz yelkeni "boyanmış" saymaz.
/// Atölyedeki canlı gösterge ve sunucudaki gerçek puan aynı hesabı kullanır.
/// </summary>
public sealed class PaintingRegions
{
    public const int GW = 64, GH = 48;
    private const float MinSignificantShare = 0.01f;
    private const float WeightPower = 0.4f;

    public int Count;
    private int[] label;          // hücre → bölge (-1 = çok küçük kırıntı, sayılmaz)
    private int[] regionPalette;  // bölge → paletteki renk
    private int[] regionSize;
    private float[] weight;
    private bool[] significant;
    private Color[] palette;      // tablonun paleti + boş tuval rengi (en sonda)

    private static readonly Dictionary<Texture2D, PaintingRegions> cache = new Dictionary<Texture2D, PaintingRegions>();

    public static PaintingRegions For(PaintingLook look)
    {
        if (look == null || look.image == null) return null;
        if (cache.TryGetValue(look.image, out PaintingRegions r) && r != null) return r;
        r = Build(look);
        cache[look.image] = r;
        return r;
    }

    private static PaintingRegions Build(PaintingLook look)
    {
        Texture2D tex = look.image;
        Color[] src = tex.GetPixels();
        int tw = tex.width, th = tex.height;

        var pal = new List<Color>();
        if (look.palette != null) pal.AddRange(look.palette);
        pal.Add(CanvasPainter.CanvasBase);

        var r = new PaintingRegions { palette = pal.ToArray(), label = new int[GW * GH] };
        var cellPal = new int[GW * GH];
        for (int y = 0; y < GH; y++)
            for (int x = 0; x < GW; x++)
            {
                int sx = Mathf.Min(tw - 1, (int)((x + 0.5f) / GW * tw));
                int sy = Mathf.Min(th - 1, (int)((y + 0.5f) / GH * th));
                cellPal[y * GW + x] = Nearest(r.palette, r.palette.Length - 1, src[sy * tw + sx]); // referansı boş tuvale eşleme
                r.label[y * GW + x] = -2;
            }

        // Aynı renkteki bitişik hücreleri tek bölge yap (4 komşu)
        var sizes = new List<int>();
        var pals = new List<int>();
        var stack = new Stack<int>();
        var members = new List<int>();
        for (int start = 0; start < cellPal.Length; start++)
        {
            if (r.label[start] != -2) continue;
            int p = cellPal[start];
            members.Clear();
            stack.Push(start);
            r.label[start] = -3;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                members.Add(i);
                int cx = i % GW, cy = i / GW;
                TryPush(r.label, cellPal, p, cx - 1, cy, stack);
                TryPush(r.label, cellPal, p, cx + 1, cy, stack);
                TryPush(r.label, cellPal, p, cx, cy - 1, stack);
                TryPush(r.label, cellPal, p, cx, cy + 1, stack);
            }

            int id = members.Count >= 3 ? sizes.Count : -1; // 1-2 hücrelik kırıntılar (kenar pürüzü) sayılmaz
            foreach (int i in members) r.label[i] = id;
            if (id >= 0) { sizes.Add(members.Count); pals.Add(p); }
        }

        r.Count = sizes.Count;
        r.regionSize = sizes.ToArray();
        r.regionPalette = pals.ToArray();
        r.weight = new float[r.Count];
        r.significant = new bool[r.Count];
        int cells = GW * GH;
        for (int i = 0; i < r.Count; i++)
        {
            r.weight[i] = Mathf.Pow(r.regionSize[i], WeightPower);
            r.significant[i] = r.regionSize[i] >= cells * MinSignificantShare;
        }
        return r;
    }

    private static void TryPush(int[] label, int[] cellPal, int p, int x, int y, Stack<int> stack)
    {
        if (x < 0 || y < 0 || x >= GW || y >= GH) return;
        int i = y * GW + x;
        if (label[i] != -2 || cellPal[i] != p) return;
        label[i] = -3;
        stack.Push(i);
    }

    /// <summary>Paletteki en yakın renk. skip: bu sıradaki renk aday sayılmaz (-1 = hepsi aday).</summary>
    private static int Nearest(Color[] pal, int skip, Color c)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < pal.Length; i++)
        {
            if (i == skip) continue;
            float dr = pal[i].r - c.r, dg = pal[i].g - c.g, db = pal[i].b - c.b;
            float d = dr * dr + dg * dg + db * db;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// 0..1 benzerlik. sample(u, v): sahte tablonun o noktadaki rengi (u, v 0..1, sol alt köşe 0,0).
    /// done / total: bitmiş önemli bölge sayısı / toplam önemli bölge (ekranda gösterilir).
    /// </summary>
    public static float Score(System.Func<float, float, Color> sample, PaintingLook look, out int done, out int total)
    {
        done = 0;
        total = 0;
        PaintingRegions r = For(look);
        if (r == null || r.Count == 0 || sample == null) return 0f;

        var hit = new int[r.Count];
        for (int y = 0; y < GH; y++)
            for (int x = 0; x < GW; x++)
            {
                int id = r.label[y * GW + x];
                if (id < 0) continue;
                Color c = sample((x + 0.5f) / GW, (y + 0.5f) / GH);
                if (Nearest(r.palette, -1, c) == r.regionPalette[id]) hit[id]++;
            }

        float sum = 0f, wsum = 0f;
        for (int i = 0; i < r.Count; i++)
        {
            float acc = hit[i] / (float)r.regionSize[i];
            float credit = Mathf.Clamp01((acc - 0.15f) / 0.75f); // %90 doğru boyanan bölge tam puan
            sum += r.weight[i] * credit;
            wsum += r.weight[i];
            if (r.significant[i])
            {
                total++;
                if (acc >= 0.8f) done++;
            }
        }
        float score = wsum > 0f ? sum / wsum : 0f;
        float completeness = total > 0 ? done / (float)total : 1f;
        return Mathf.Clamp01(score * Mathf.Lerp(0.75f, 1f, completeness));
    }
}
