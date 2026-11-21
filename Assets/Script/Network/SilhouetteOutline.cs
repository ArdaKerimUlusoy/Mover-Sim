using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// KARAKTER DIŞ ÇİZGİSİ (yalnızca siluet): modeldeki ters kabuk ("G_Kontur" / "SG_Kontur" alt mesh'i) eskiden yüzün,
/// burnun, kulağın, şapka siperliğinin, çantanın İÇİNDE de siyah çizgi bırakıyordu (burnun kabuğu yüzün önünde kalıyor).
/// Çözüm: stencil maskesi.
///   1) Maske: karakterin kendi (kabuk OLMAYAN) üçgenleri, renk / derinlik yazmadan stencil'e karakterin numarasını yazar.
///   2) Kabuk: yalnızca stencil'de AYNI karakterin numarası OLMAYAN yerlere çizilir → sadece siluetin dışında kalan halka görünür.
///      Başka karakterin önündeki kenar (farklı numara) yine çizilir.
/// Maske ayrı bir renderer DEĞİL: mesh'in kopyasına (kaynak başına bir kez, herkes paylaşır) tüm gövde üçgenlerini tutan
/// ek bir alt mesh eklenir ve aynı SkinnedMeshRenderer'a maske malzemesi olarak verilir. Böylece açık / kapalı,
/// "yalnızca gölge" (sahibinin kafası), forceRenderingOff (kayıt kameraları), katman, ayna / F5 geçişleri ve
/// blend shape'ler (göz kırpma) kendiliğinden aynı kalır; maske gölge geçişi olmadığından gölgede hiç çizilmez.
/// Gerekli: Resources/Shaders/SilhouetteMask + SilhouetteOutline shader'ları ve FBX'te Read/Write açık.
/// Biri eksikse hiçbir şey değişmez (eski ters kabuk davranışı).
/// </summary>
public static class SilhouetteOutline
{
    public const string DefaultPrefix = "G_Kontur";

    // Stencil'in kullanıcıya ayrılmış alt 4 biti (URP'nin kendi bitlerine dokunmaz). Her karaktere 1..15 arası bir numara.
    private const int MaxRef = 15;
    private const int MaskQueue = 2501;    // Transparent-499: opak çizimden (derinlik önceleme dahil) sonra, saydamlardan önce
    private const int OutlineQueue = 2502; // maskeden hemen sonra
    private const int ClearQueue = 2503;   // çizgiden sonra: maskenin stencil'ini sıfırla (kalıcı RT'de — aynada — birikmesin)

    private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int CullId = Shader.PropertyToID("_Cull");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int WidenId = Shader.PropertyToID("_Widen");
    private static readonly HashSet<Mesh> widened = new HashSet<Mesh>(); // kabuk normalleri "dışa yön"e çevrildi

    private static Shader maskShader, outlineShader, clearShader;
    private static Material clearMat;
    private static bool shadersTried, shadersOk, warnedReadable;
    private static int nextRef;
    private static readonly Material[] maskMats = new Material[MaxRef + 1];
    private static readonly Dictionary<(Mesh, int), Mesh> meshCache = new Dictionary<(Mesh, int), Mesh>();
    private static readonly HashSet<Mesh> madeMeshes = new HashSet<Mesh>();
    private static readonly Dictionary<(Material, int), Material> outlineCache = new Dictionary<(Material, int), Material>();
    private static readonly List<int> triBuf = new List<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Etki alanı yeniden yüklenmeden Play'e girilirse (Enter Play Mode Options) eski önbellek kalmasın
        maskShader = null; outlineShader = null; clearShader = null; clearMat = null;
        shadersTried = false; shadersOk = false; warnedReadable = false;
        nextRef = 0;
        for (int i = 0; i < maskMats.Length; i++) maskMats[i] = null;
        meshCache.Clear(); madeMeshes.Clear(); outlineCache.Clear(); widened.Clear();
    }

    /// <summary>
    /// Kökün altındaki tüm SkinnedMeshRenderer'larda adı <paramref name="konturPrefix"/> ile başlayan kabuk alt mesh'ini
    /// siluet çizgisine çevirir (aynı köke tek stencil numarası: kafa + gövde birlikte tek siluet).
    /// Malzemeler hazırlandıktan SONRA çağrılmalı. Tekrar çağrılırsa zaten çevrilmiş renderer'ları atlar.
    /// Dönüş: çevrilen renderer sayısı (0 = eski davranış sürüyor).
    /// </summary>
    public static int Apply(GameObject characterRoot, string konturPrefix = DefaultPrefix)
    {
        if (characterRoot == null || string.IsNullOrEmpty(konturPrefix) || !LoadShaders()) return 0;
        int stencilRef = -1, done = 0;
        foreach (SkinnedMeshRenderer sk in characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (sk == null) continue;
            Mesh src = sk.sharedMesh;
            if (src == null || madeMeshes.Contains(src)) continue; // mesh yok / zaten çevrilmiş
            Material[] mats = sk.sharedMaterials;
            int subCount = src.subMeshCount;
            if (mats == null || mats.Length != subCount) continue; // fazla / eksik malzeme: alt mesh eşlemesi belirsiz, dokunma

            int konturBits = 0, bodyCount = 0;
            for (int i = 0; i < subCount && i < 31; i++)
            {
                if (mats[i] != null && mats[i].name.StartsWith(konturPrefix)) konturBits |= 1 << i;
                else if (src.GetTopology(i) == MeshTopology.Triangles) bodyCount++;
            }
            if (konturBits == 0 || bodyCount == 0 || subCount > 31) continue;

            if (!src.isReadable)
            {
                if (!warnedReadable)
                {
                    warnedReadable = true;
                    Debug.LogWarning("[SilhouetteOutline] '" + src.name + "' mesh'inde Read/Write kapalı: yalnızca dış çizgi için " +
                                     "FBX Import Settings → Model → Read/Write açılmalı. Şimdilik eski çizgi (iç çizgiler dahil) kalıyor.");
                }
                continue;
            }

            Mesh masked = MaskedMesh(src, konturBits);
            if (masked == null) continue;
            if (stencilRef < 0) stencilRef = NextRef();

            // Malzemeler: kabuk → siluet çizgisi, sona maske (yeni alt mesh'in indeksi = eski alt mesh sayısı)
            // +1 maske alt mesh'i, +1 fazla malzeme: Unity son alt mesh'i (maske) bu malzemeyle bir kez daha çizer → stencil temizliği
            bool clear = clearShader != null && clearShader.isSupported;
            var newMats = new Material[subCount + (clear ? 2 : 1)];
            for (int i = 0; i < subCount; i++)
                newMats[i] = (konturBits & (1 << i)) != 0 && mats[i] != null ? OutlineMat(mats[i], stencilRef, widened.Contains(masked)) : mats[i];
            newMats[subCount] = MaskMat(stencilRef);
            if (clear) newMats[subCount + 1] = ClearMat();

            // Mesh değişince sınırlar / blend shape ağırlıkları sıfırlanabilir: koru
            Bounds lb = sk.localBounds;
            int shapes = src.blendShapeCount;
            float[] weights = shapes > 0 ? new float[shapes] : null;
            for (int i = 0; i < shapes; i++) weights[i] = sk.GetBlendShapeWeight(i);
            sk.sharedMesh = masked;
            sk.localBounds = lb;
            for (int i = 0; i < shapes; i++) sk.SetBlendShapeWeight(i, weights[i]);
            sk.sharedMaterials = newMats;
            done++;
        }
        return done;
    }

    private static bool LoadShaders()
    {
        if (shadersTried) return shadersOk;
        shadersTried = true;
        try
        {
            maskShader = Resources.Load<Shader>("Shaders/SilhouetteMask");
            outlineShader = Resources.Load<Shader>("Shaders/SilhouetteOutline");
            clearShader = Resources.Load<Shader>("Shaders/SilhouetteMaskClear");
        }
        catch (System.Exception) { maskShader = null; outlineShader = null; }
        if (maskShader == null) maskShader = Shader.Find("Soygun/SilhouetteMask");
        if (outlineShader == null) outlineShader = Shader.Find("Soygun/SilhouetteOutline");
        if (clearShader == null) clearShader = Shader.Find("Soygun/SilhouetteMaskClear");
        shadersOk = maskShader != null && outlineShader != null && maskShader.isSupported && outlineShader.isSupported;
        if (!shadersOk) Debug.LogWarning("[SilhouetteOutline] Resources/Shaders/SilhouetteMask / SilhouetteOutline bulunamadı ya da desteklenmiyor: eski dış çizgi kullanılıyor.");
        return shadersOk;
    }

    private static int NextRef()
    {
        nextRef = nextRef % MaxRef + 1; // 1..15 sırayla (üst üste binen iki karakterin aynı numarayı alması nadir)
        return nextRef;
    }

    /// <summary>Kaynağın kopyası + sonuna kabuk dışındaki tüm üçgenleri tutan maske alt mesh'i (kaynak başına bir kez).</summary>
    private static Mesh MaskedMesh(Mesh src, int konturBits)
    {
        if (meshCache.TryGetValue((src, konturBits), out Mesh cached) && cached != null) return cached;
        int subCount = src.subMeshCount;
        var tris = new List<int>(src.vertexCount * 3);
        for (int sm = 0; sm < subCount; sm++)
        {
            if ((konturBits & (1 << sm)) != 0 || src.GetTopology(sm) != MeshTopology.Triangles) continue;
            src.GetTriangles(triBuf, sm); // taban köşe uygulanmış (mutlak) indeksler
            tris.AddRange(triBuf);
        }
        if (tris.Count == 0) return null;
        Mesh m = Object.Instantiate(src); // köşeler, kemik ağırlıkları, bindpose'lar, blend shape'ler aynen
        m.name = src.name + "_Siluet";
        m.subMeshCount = subCount + 1;
        m.SetTriangles(tris, subCount, false); // sınırlar değişmez (aynı köşeler)
        if (OutwardNormals(src, m, konturBits)) widened.Add(m);
        meshCache[(src, konturBits)] = m;
        madeMeshes.Add(m);
        return m;
    }

    private static Material ClearMat()
    {
        if (clearMat == null) clearMat = new Material(clearShader) { name = "SiluetMaskeTemizle", renderQueue = ClearQueue };
        return clearMat;
    }

    private static Material MaskMat(int stencilRef)
    {
        Material m = maskMats[stencilRef];
        if (m != null) return m;
        m = new Material(maskShader) { name = "SiluetMaske_" + stencilRef, renderQueue = MaskQueue };
        m.SetFloat(StencilRefId, stencilRef);
        maskMats[stencilRef] = m;
        return m;
    }

    /// <summary>Kabuk malzemesinin rengi ve cull ayarıyla siluet çizgisi malzemesi (kaynak malzeme + numara başına bir kez).</summary>
    /// <summary>
    /// Kabuk (yalnızca kabuğa ait) köşelerin normalini "dışa doğru" yönle değiştirir: en yakın gövde köşesinden kabuk
    /// köşesine giden yön (kabuk gövdenin ~1,5 cm dışında). Modeldeki kabuk normalleri karışık (ters çevrilmiş kabuk),
    /// bu yüzden shader'ın uzakta çizgiyi kalınlaştırması (aynada / uzaktaki oyuncuda çizgi kaybolmasın) için gerekli.
    /// Normaller deri ile birlikte eğilir (SkinnedMeshRenderer). Bir kez, kaynak mesh başına.
    /// </summary>
    private static bool OutwardNormals(Mesh src, Mesh dst, int konturBits)
    {
        try
        {
            Vector3[] v = src.vertices;
            Vector3[] n = src.normals;
            int vc = v.Length;
            if (vc == 0) return false;
            if (n == null || n.Length != vc) n = new Vector3[vc];
            var isK = new bool[vc];
            var isB = new bool[vc];
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                if (src.GetTopology(sm) != MeshTopology.Triangles) continue;
                bool k = (konturBits & (1 << sm)) != 0;
                src.GetTriangles(triBuf, sm);
                foreach (int i in triBuf) { if (k) isK[i] = true; else isB[i] = true; }
            }
            Bounds bb = src.bounds;
            float cell = Mathf.Max(1e-4f, Mathf.Max(bb.size.x, Mathf.Max(bb.size.y, bb.size.z)) / 90f);
            var grid = new Dictionary<long, List<int>>();
            long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
            for (int i = 0; i < vc; i++)
            {
                if (!isB[i]) continue;
                Vector3 q = (v[i] - bb.min) / cell;
                long key = Key((int)q.x, (int)q.y, (int)q.z);
                if (!grid.TryGetValue(key, out List<int> list)) { list = new List<int>(4); grid[key] = list; }
                list.Add(i);
            }
            int changed = 0;
            for (int i = 0; i < vc; i++)
            {
                if (!isK[i] || isB[i]) continue;
                Vector3 q = (v[i] - bb.min) / cell;
                int cx = (int)q.x, cy = (int)q.y, cz = (int)q.z;
                float best = float.MaxValue;
                int bi = -1;
                for (int ring = 1; ring <= 3 && bi < 0; ring++)
                    for (int x = cx - ring; x <= cx + ring; x++)
                        for (int y = cy - ring; y <= cy + ring; y++)
                            for (int z = cz - ring; z <= cz + ring; z++)
                            {
                                if (!grid.TryGetValue(Key(x, y, z), out List<int> list)) continue;
                                foreach (int j in list)
                                {
                                    float d = (v[j] - v[i]).sqrMagnitude;
                                    if (d < best) { best = d; bi = j; }
                                }
                            }
                if (bi < 0) continue;
                Vector3 dir = v[i] - v[bi];
                if (dir.sqrMagnitude < 1e-12f) continue;
                n[i] = dir.normalized;
                changed++;
            }
            if (changed == 0) return false;
            dst.normals = n;
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[SilhouetteOutline] Kabuk normalleri hesaplanamadı (çizgi uzakta kalınlaşmaz): " + e.Message);
            return false;
        }
    }

    private static Material OutlineMat(Material src, int stencilRef, bool widen)
    {
        if (outlineCache.TryGetValue((src, stencilRef), out Material cached) && cached != null)
        {
            if (widen) cached.SetFloat(WidenId, 1f);
            return cached;
        }
        Color c = src.HasProperty(BaseColorId) ? src.GetColor(BaseColorId) : src.HasProperty(ColorId) ? src.GetColor(ColorId) : new Color(0.02f, 0.015f, 0.015f);
        c.a = 1f;
        var m = new Material(outlineShader) { name = src.name.Split(' ')[0] + "_Siluet_" + stencilRef, renderQueue = OutlineQueue };
        m.SetColor(ColorId, c);
        m.SetFloat(CullId, src.HasProperty(CullId) ? src.GetFloat(CullId) : 2f); // ters kabuk: arka yüzler çizilmez (eskisi gibi)
        m.SetFloat(StencilRefId, stencilRef);
        m.SetFloat(WidenId, widen ? 1f : 0f);
        outlineCache[(src, stencilRef)] = m;
        return m;
    }
}
