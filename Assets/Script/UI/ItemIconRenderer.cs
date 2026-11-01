using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OYUN İÇİ GÖRÜNTÜDEN İKON: eşyanın gerçek 3B görselini (çantadaki sahte, müzedeki orijinal, fırça, maymuncuk, boya kutusu,
/// mağaza ürünleri...) sahneden çok uzakta, ayrı bir katmanda küçük bir kamerayla çeker ve şeffaf arka planlı dokuya çevirir.
/// Hızlı envanter, çanta, karaborsa ve garaj mağazası çizimden değil bu dokulardan beslenir.
///
/// Çalışma: Get(...) önbellekte yoksa işi kuyruğa ekler ve null döner (çağıran o kare yedek simge çizer). Her iş iki karede
/// tamamlanır: siyah ve beyaz zeminle iki kez çekilir, iki görüntüden saydamlık hesaplanır (kenar yumuşatması korunur).
/// Kendini kurar; sahneye bir şey eklemek gerekmez.
/// </summary>
public class ItemIconRenderer : MonoBehaviour
{
    public const int Size = 128;
    private const int RtSize = Size * 2; // 2 kat büyük çekilip küçültülür (kenarlar yumuşak)
    private const int Layer = 31;
    private static readonly Vector3 Stage = new Vector3(0f, -4000f, 0f);

    private static ItemIconRenderer instance;
    private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
    private static readonly HashSet<string> failed = new HashSet<string>();
    private static readonly Queue<Job> queue = new Queue<Job>();
    private static readonly HashSet<string> queued = new HashSet<string>();

    private struct Job
    {
        public string key;
        public Func<GameObject> build;
        public Vector3 viewDir;
    }

    private Camera cam;
    private Light keyLight;
    private RenderTexture rt;
    private GameObject current;
    private Job job;
    private int stage;           // 0 boş, 1 siyah zemin çekiliyor, 2 beyaz zemin çekiliyor
    private Color[] black;

    /// <summary>Önden, hafif yukarıdan ve soldan bakış (varsayılan).</summary>
    public static readonly Vector3 DefaultView = new Vector3(-0.42f, 0.38f, -1f);

    /// <summary>
    /// key için ikon (yoksa kuyruğa ekler ve null döner). build: görseli kuran fonksiyon (pivot önemsiz; sınırlara göre ortalanır).
    /// viewDir: kameranın nesneye BAKTIĞI yönün tersi (nesneden kameraya), dünya uzayında.
    /// </summary>
    public static Texture2D Get(string key, Func<GameObject> build, Vector3? viewDir = null)
    {
        if (string.IsNullOrEmpty(key) || build == null) return null;
        if (cache.TryGetValue(key, out Texture2D t) && t != null) return t;
        if (failed.Contains(key) || queued.Contains(key)) return null;
        Ensure();
        queued.Add(key);
        queue.Enqueue(new Job { key = key, build = build, viewDir = (viewDir ?? DefaultView).normalized });
        return null;
    }

    /// <summary>Önbellekteki ikonu siler (görünümü değişen eşya için: ör. sahtenin verisi yeni geldi).</summary>
    public static void Invalidate(string key)
    {
        if (cache.TryGetValue(key, out Texture2D t) && t != null) Destroy(t);
        cache.Remove(key);
        failed.Remove(key);
    }

    private static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("ItemIconRenderer");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ItemIconRenderer>();
    }

    private void Awake()
    {
        rt = new RenderTexture(RtSize, RtSize, 24, RenderTextureFormat.ARGB32) { name = "ItemIconRT", antiAliasing = 1 };
        rt.Create();

        var cgo = new GameObject("IconCamera");
        cgo.transform.SetParent(transform, false);
        cam = cgo.AddComponent<Camera>();
        cam.enabled = false;
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.fieldOfView = 24f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 50f;
        cam.targetTexture = rt;
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.useOcclusionCulling = false;

        // Kendi ışığı: sahnede güneş olmasa da (garaj, kamyon) ikon aydınlık çıksın. Menzili sahneye ulaşmaz.
        var lgo = new GameObject("IconLight");
        lgo.transform.SetParent(cgo.transform, false);
        lgo.transform.localPosition = new Vector3(-0.6f, 0.8f, 0.1f);
        keyLight = lgo.AddComponent<Light>();
        keyLight.type = LightType.Point;
        keyLight.range = 30f;
        keyLight.intensity = 2.2f;
        keyLight.color = new Color(1f, 0.97f, 0.92f);
        keyLight.cullingMask = 1 << Layer;
        keyLight.shadows = LightShadows.None;
        keyLight.enabled = false;
    }

    private void OnDestroy()
    {
        if (current != null) Destroy(current);
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (instance == this) instance = null;
    }

    private void LateUpdate()
    {
        try { Step(); }
        catch (Exception e)
        {
            Debug.LogWarning("[ItemIcon] " + job.key + ": " + e.Message);
            if (!string.IsNullOrEmpty(job.key)) { failed.Add(job.key); queued.Remove(job.key); }
            Cleanup();
        }
    }

    private void Step()
    {
        if (stage == 1)
        {
            black = ReadPixels();
            cam.backgroundColor = Color.white;
            stage = 2; // bu kare beyaz zeminle çekilir
            return;
        }
        if (stage == 2)
        {
            Color[] white = ReadPixels();
            var px = new Color[black.Length];
            for (int i = 0; i < px.Length; i++)
            {
                Color b = black[i], w = white[i];
                // Siyah ve beyaz zeminde fark = saydamlık; renk siyah zemindeki görüntünün alfaya bölümü
                float a = Mathf.Clamp01(1f - ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / 3f);
                px[i] = a > 0.003f ? new Color(Mathf.Clamp01(b.r / a), Mathf.Clamp01(b.g / a), Mathf.Clamp01(b.b / a), a) : new Color(0f, 0f, 0f, 0f);
            }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Icon_" + job.key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply(false, false);
            cache[job.key] = tex;
            queued.Remove(job.key);
            Cleanup();
            return;
        }

        // Boşta: sıradaki işi kur (bu kare siyah zeminle çekilir)
        while (queue.Count > 0)
        {
            job = queue.Dequeue();
            GameObject go = null;
            try { go = job.build(); } catch (Exception e) { Debug.LogWarning("[ItemIcon] " + job.key + ": " + e.Message); }
            if (go == null) { queued.Remove(job.key); continue; } // görsel henüz hazır değil: sonra yeniden istenir
            if (!Frame(go)) { Destroy(go); failed.Add(job.key); queued.Remove(job.key); continue; }
            current = go;
            cam.backgroundColor = Color.black;
            cam.enabled = true;
            keyLight.enabled = true;
            stage = 1;
            return;
        }
    }

    /// <summary>Görseli sahneye koyar, katmanını ayarlar ve kamerayı sınırlarına göre yerleştirir.</summary>
    private bool Frame(GameObject go)
    {
        go.transform.position = Stage;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return false;
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs)
        {
            b.Encapsulate(r.bounds);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }
        float radius = Mathf.Max(0.01f, b.extents.magnitude);
        float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
        Vector3 dir = job.viewDir;
        cam.transform.position = b.center + dir * dist;
        cam.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
        cam.nearClipPlane = Mathf.Max(0.005f, dist - radius * 1.5f);
        cam.farClipPlane = dist + radius * 2f;
        return true;
    }

    /// <summary>Kameranın son çektiği kareyi okur ve 2x2 ortalamayla ikon boyutuna küçültür.</summary>
    private Color[] ReadPixels()
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tmp = new Texture2D(RtSize, RtSize, TextureFormat.RGBA32, false);
        tmp.ReadPixels(new Rect(0, 0, RtSize, RtSize), 0, 0, false);
        tmp.Apply(false, false);
        RenderTexture.active = prev;
        Color[] big = tmp.GetPixels();
        Destroy(tmp);
        var px = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = (y * 2) * RtSize + x * 2;
                px[y * Size + x] = (big[i] + big[i + 1] + big[i + RtSize] + big[i + RtSize + 1]) * 0.25f;
            }
        return px;
    }

    private void Cleanup()
    {
        if (current != null) Destroy(current);
        current = null;
        cam.enabled = false;
        keyLight.enabled = false;
        stage = 0;
        black = null;
    }

    // =====================================================================
    //  Eşyalar
    // =====================================================================

    /// <summary>
    /// Çanta / hızlı envanter eşyasının ikonu (oyun içi görünüm). Sahte: oyuncunun yaptığı eser; orijinal: müzedeki eserin kendisi;
    /// fırça: eldeki fırça modeli; diğerleri: eşyanın dünyadaki prefab görüntüsü. Hazır değilse null.
    /// </summary>
    public static Texture2D For(ItemDefinition def, InventorySlot slot)
    {
        if (def == null) return null;
        if (PaintBrush.IsBrush(def)) return Get("brush", () => PaintBrush.BuildBrushModel("IconBrush").gameObject, new Vector3(-0.8f, 0.25f, -1f));

        string key;
        if (def.type == ItemType.Forgery && slot.ForgeryId != 0)
        {
            if (!ForgeryRegistry.TryGet(slot.ForgeryId, out _)) return null; // veri bekleniyor
            key = "forgery:" + slot.ForgeryId;
        }
        else if (def.type == ItemType.Original && slot.LookKey != 0)
        {
            MuseumSession ms = MuseumSession.Instance;
            if (ms == null || !ms.Ready) return null;
            key = "original:" + ms.Seed + ":" + ms.ThemeIndex + ":" + slot.LookKey;
        }
        else key = "def:" + def.id;

        InventorySlot s = slot;
        return Get(key, () =>
        {
            GameObject g = ItemVisuals.BuildFor(def, s, out bool pending);
            if (pending) { if (g != null) Destroy(g); return null; }
            return g;
        }, ViewFor(def));
    }

    /// <summary>Tablo önden; diğerleri hafif yukarıdan ve yandan.</summary>
    private static Vector3 ViewFor(ItemDefinition def)
    {
        ArtKind k = ArtworkLook.KindOf(def);
        if (k == ArtKind.Painting) return new Vector3(-0.18f, 0.1f, -1f);
        if (k == ArtKind.Jewelry) return new Vector3(-0.3f, 0.9f, -1f);
        return DefaultView;
    }

    // ---------------- Aletler ve mağaza ürünleri ----------------

    public enum Prop { Lockpick, PaintPot, Coin, Battery, Bag, Gloves, TeamBags }

    /// <summary>Alet / mağaza ürünü ikonu (oyundaki modelden ya da aynı tarzda kurulan küçük modelden).</summary>
    public static Texture2D ForProp(Prop p)
    {
        int color = p == Prop.PaintPot ? SprayPaint.LocalColor : 0;
        return Get("prop:" + p + ":" + color, () => IconProps.Build(p), p == Prop.Coin ? new Vector3(-0.3f, 0.75f, -1f) : DefaultView);
    }

    /// <summary>Mağaza geliştirmesinin ikonu.</summary>
    public static Texture2D ForUpgrade(UpgradeType t)
    {
        switch (t)
        {
            case UpgradeType.Lockpick: return ForProp(Prop.Lockpick);
            case UpgradeType.SprayPaint: return ForProp(Prop.PaintPot);
            case UpgradeType.CameraBattery: return ForProp(Prop.Battery);
            case UpgradeType.SwiftGloves: return ForProp(Prop.Gloves);
            case UpgradeType.TeamBackpacks: return ForProp(Prop.TeamBags);
            default: return ForProp(Prop.Bag);
        }
    }
}

/// <summary>
/// Mağaza / alet ikonları için küçük modeller. Mümkünse oyuncu karakterinin üstündeki gerçek parçalar kullanılır
/// (çanta, eldiven, maymuncuk kesesi — Resources/Characters/BitirmeChar), yoksa aynı tarzda basit parçalardan kurulur.
/// Üretilen malzemeler görselle birlikte yok edilir (RuntimeResources).
/// </summary>
public static class IconProps
{
    public static GameObject Build(ItemIconRenderer.Prop p)
    {
        var root = new GameObject("IconProp_" + p);
        RuntimeResources res = RuntimeResources.For(root);
        switch (p)
        {
            case ItemIconRenderer.Prop.Lockpick:
                if (!CharacterPart(root, "CHR_Lockpick_Case", Vector3.zero, Quaternion.Euler(0f, 160f, 0f), 0.11f))
                    Box(root, res, new Vector3(0f, 0f, 0f), new Vector3(0.08f, 0.05f, 0.02f), new Color(0.2f, 0.08f, 0.03f));
                // Kesenin üstünden çıkan iki ince maşa
                Cyl(root, res, new Vector3(-0.012f, 0.045f, 0f), new Vector3(0.004f, 0.035f, 0.004f), Quaternion.Euler(0f, 0f, 12f), new Color(0.75f, 0.75f, 0.8f), 0.6f);
                Cyl(root, res, new Vector3(0.01f, 0.05f, 0f), new Vector3(0.004f, 0.04f, 0.004f), Quaternion.Euler(0f, 0f, -8f), new Color(0.8f, 0.8f, 0.85f), 0.6f);
                break;

            case ItemIconRenderer.Prop.PaintPot:
            {
                Color paint = SprayPaint.ColorOf(SprayPaint.LocalColor);
                Color tin = new Color(0.62f, 0.63f, 0.66f);
                Cyl(root, res, new Vector3(0f, 0.05f, 0f), new Vector3(0.09f, 0.05f, 0.09f), Quaternion.identity, tin, 0.55f);
                Cyl(root, res, new Vector3(0f, 0.052f, 0f), new Vector3(0.0915f, 0.026f, 0.0915f), Quaternion.identity, paint, 0f); // etiket bandı
                Cyl(root, res, new Vector3(0f, 0.101f, 0f), new Vector3(0.094f, 0.004f, 0.094f), Quaternion.identity, new Color(0.5f, 0.5f, 0.53f), 0.6f); // kapak
                Cyl(root, res, new Vector3(0f, 0.104f, 0f), new Vector3(0.07f, 0.002f, 0.07f), Quaternion.identity, paint, 0f); // kapaktaki boya
                Sph(root, res, new Vector3(0.03f, 0.092f, -0.042f), new Vector3(0.014f, 0.022f, 0.012f), paint); // akan damla
                // Tel sap
                Cyl(root, res, new Vector3(-0.046f, 0.112f, 0f), new Vector3(0.004f, 0.02f, 0.004f), Quaternion.Euler(0f, 0f, -20f), new Color(0.3f, 0.3f, 0.32f), 0.5f);
                Cyl(root, res, new Vector3(0.046f, 0.112f, 0f), new Vector3(0.004f, 0.02f, 0.004f), Quaternion.Euler(0f, 0f, 20f), new Color(0.3f, 0.3f, 0.32f), 0.5f);
                Cyl(root, res, new Vector3(0f, 0.132f, 0f), new Vector3(0.004f, 0.04f, 0.004f), Quaternion.Euler(0f, 0f, 90f), new Color(0.3f, 0.3f, 0.32f), 0.5f);
                break;
            }

            case ItemIconRenderer.Prop.Coin:
                Cyl(root, res, Vector3.zero, new Vector3(0.05f, 0.004f, 0.05f), Quaternion.identity, new Color(0.85f, 0.66f, 0.25f), 0.8f);
                Cyl(root, res, new Vector3(0f, 0.0045f, 0f), new Vector3(0.036f, 0.0005f, 0.036f), Quaternion.identity, new Color(0.95f, 0.78f, 0.35f), 0.8f);
                break;

            case ItemIconRenderer.Prop.Battery:
            {
                Color body = new Color(0.13f, 0.13f, 0.15f);
                Box(root, res, Vector3.zero, new Vector3(0.07f, 0.045f, 0.03f), body);
                Box(root, res, new Vector3(0f, 0f, -0.0151f), new Vector3(0.05f, 0.012f, 0.001f), new Color(0.95f, 0.65f, 0.15f)); // şerit
                Box(root, res, new Vector3(-0.018f, 0.0235f, 0f), new Vector3(0.012f, 0.003f, 0.012f), new Color(0.75f, 0.7f, 0.5f)); // uçlar
                Box(root, res, new Vector3(0.018f, 0.0235f, 0f), new Vector3(0.012f, 0.003f, 0.012f), new Color(0.75f, 0.7f, 0.5f));
                Box(root, res, new Vector3(0.024f, 0.006f, -0.0152f), new Vector3(0.006f, 0.002f, 0.001f), Color.white); // + işareti
                Box(root, res, new Vector3(0.024f, 0.006f, -0.0152f), new Vector3(0.002f, 0.006f, 0.001f), Color.white);
                break;
            }

            case ItemIconRenderer.Prop.Bag:
                if (!CharacterPart(root, "CHR_Satchel", Vector3.zero, Quaternion.Euler(0f, 200f, 0f), 0.2f))
                    Box(root, res, Vector3.zero, new Vector3(0.12f, 0.1f, 0.05f), new Color(0.32f, 0.14f, 0.06f));
                break;

            case ItemIconRenderer.Prop.Gloves:
                if (!CharacterPart(root, "CHR_Glove.L", Vector3.zero, Quaternion.Euler(0f, 0f, 45f), 0.13f))
                    Sph(root, res, Vector3.zero, new Vector3(0.08f, 0.1f, 0.04f), new Color(0.2f, 0.09f, 0.04f));
                if (!CharacterPart(root, "CHR_Glove.R", new Vector3(0.09f, -0.01f, 0.03f), Quaternion.Euler(0f, 0f, -45f), 0.13f))
                    Sph(root, res, new Vector3(0.09f, 0f, 0.03f), new Vector3(0.08f, 0.1f, 0.04f), new Color(0.2f, 0.09f, 0.04f));
                break;

            default: // TeamBags: yan yana iki çanta
                if (!CharacterPart(root, "CHR_Satchel", new Vector3(-0.07f, 0f, 0.02f), Quaternion.Euler(0f, 210f, 0f), 0.2f))
                    Box(root, res, new Vector3(-0.06f, 0f, 0.02f), new Vector3(0.12f, 0.1f, 0.05f), new Color(0.32f, 0.14f, 0.06f));
                if (!CharacterPart(root, "CHR_Satchel", new Vector3(0.09f, -0.01f, -0.03f), Quaternion.Euler(0f, 180f, 0f), 0.18f))
                    Box(root, res, new Vector3(0.07f, -0.01f, -0.02f), new Vector3(0.11f, 0.09f, 0.05f), new Color(0.38f, 0.17f, 0.07f));
                break;
        }
        return root;
    }

    private static GameObject characterPrefab;
    private static bool characterTried;

    /// <summary>
    /// Karakter modelindeki parçanın mesh'ini (dinlenme pozunda, modeldeki yönüyle) ve malzemelerini kopyalar; parça
    /// verilen noktaya ortalanır ve en büyük köşegeni size (metre) olacak şekilde ölçeklenir. Parça yoksa false.
    /// </summary>
    private static bool CharacterPart(GameObject root, string name, Vector3 pos, Quaternion rot, float size)
    {
        if (!characterTried)
        {
            characterTried = true;
            try { characterPrefab = Resources.Load<GameObject>("Characters/BitirmeChar"); } catch (Exception) { characterPrefab = null; }
        }
        if (characterPrefab == null) return false;
        Mesh mesh = null;
        Material[] mats = null;
        Transform src = null;
        foreach (SkinnedMeshRenderer smr in characterPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.name == name) { mesh = smr.sharedMesh; mats = smr.sharedMaterials; src = smr.transform; break; }
        if (mesh == null)
            foreach (MeshFilter mf in characterPrefab.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name == name) { mesh = mf.sharedMesh; MeshRenderer mr = mf.GetComponent<MeshRenderer>(); mats = mr != null ? mr.sharedMaterials : null; src = mf.transform; break; }
        if (mesh == null || mats == null || src == null) return false;

        var holder = new GameObject(name);
        holder.transform.SetParent(root.transform, false);
        holder.transform.localPosition = pos;
        holder.transform.localRotation = rot;
        var part = new GameObject("Mesh");
        part.transform.SetParent(holder.transform, false);
        // Modeldeki yönü ve ölçeği koru (FBX eksen dönüşümü parçanın kendi dönüşündedir)
        part.transform.localRotation = Quaternion.Inverse(characterPrefab.transform.rotation) * src.rotation;
        part.transform.localScale = src.lossyScale;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer rend = part.AddComponent<MeshRenderer>();
        rend.sharedMaterials = mats;

        // Ortala ve boyutlandır
        Bounds b = rend.bounds;
        float diag = b.size.magnitude;
        if (diag > 0.000001f) holder.transform.localScale = Vector3.one * (size / diag);
        b = rend.bounds;
        part.transform.position -= b.center - holder.transform.position;
        return true;
    }

    private static void Box(GameObject root, RuntimeResources res, Vector3 pos, Vector3 size, Color c) =>
        Prim(root, res, PrimitiveType.Cube, pos, size, Quaternion.identity, c, 0.2f);

    private static void Cyl(GameObject root, RuntimeResources res, Vector3 pos, Vector3 size, Quaternion rot, Color c, float metal) =>
        Prim(root, res, PrimitiveType.Cylinder, pos, size, rot, c, metal);

    private static void Sph(GameObject root, RuntimeResources res, Vector3 pos, Vector3 size, Color c) =>
        Prim(root, res, PrimitiveType.Sphere, pos, size, Quaternion.identity, c, 0f);

    private static void Prim(GameObject root, RuntimeResources res, PrimitiveType type, Vector3 pos, Vector3 size, Quaternion rot, Color c, float metal)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Collider col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = size;
        Material m = ArtworkLook.MakeLit(c);
        if (metal > 0f && m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", metal > 0f ? 0.6f : 0.3f);
        res.Own(m);
        go.GetComponent<Renderer>().sharedMaterial = m;
    }
}
