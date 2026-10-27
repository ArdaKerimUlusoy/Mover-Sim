using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Garajdaki BOY AYNASI: kendini, kıyafetini ve karakter rengini görmek için gerçek düzlem yansıması.
/// Objenin ileri yönü (+Z) aynanın odaya baktığı yöndür; görsel (çerçeve + cam) çalışma anında kurulur.
/// Yansıma kamerası, bakan oyuncunun gözünün aynaya göre simetriğinde durur ve eğik (off-axis) izdüşümle
/// tam ayna camını görür → gerçek ayna gibi bakış açısına göre değişir.
/// Kendi gövden normalde yalnızca gölge olarak çizilir; ayna kamerası çizerken geçici olarak görünür yapılır,
/// birinci şahıs kollar / fırça gizlenir.
/// Performans: yalnızca oyuncu aynanın önünde, yakınında (12 m) ve ayna ekranda görünürken çizilir; tek kamera, ~1170x768 (3.2x2.1 m ayna).
/// </summary>
[DefaultExecutionOrder(10000)] // oyuncunun kamerası bu karede yerine oturduktan SONRA
public class GarageMirror : MonoBehaviour
{
    [SerializeField, Min(0.3f)] private float width = 1.0f;
    [SerializeField, Min(0.5f)] private float height = 2.0f;

    private const float RenderRange = 12f;
    private const int RtHeight = 768; // genişlik aynanın en / boy oranına göre
    private static readonly Plane[] Planes = new Plane[6];

    private RenderTexture rt;
    private Camera mirrorCam;
    private Material glassMat, frameMat;
    private Renderer glassRenderer;
    private bool built;

    // Ayna kamerası çizerken geçici değişiklikler
    private readonly List<Renderer> showBody = new List<Renderer>();
    private readonly List<Renderer> hideFp = new List<Renderer>();
    private float nextCollect;
    private bool brushCollected;
    private PlayerStealth collectedFor;

    public void Setup(float w, float h)
    {
        width = w;
        height = h;
    }

    private void Start() => Build();

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        if (mirrorCam != null) mirrorCam.enabled = false;
        RestoreRenderers();
    }

    private void OnDestroy()
    {
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (glassMat != null) Destroy(glassMat);
        if (frameMat != null) Destroy(frameMat);
    }

    // ---------------- Görsel ----------------

    private void Build()
    {
        if (built) return;
        built = true;

        frameMat = ArtworkLook.MakeLit(new Color(0.62f, 0.45f, 0.2f)); // eskitilmiş pirinç / ceviz çerçeve
        const float fw = 0.07f, fd = 0.05f;
        Part("MirrorFrameTop", new Vector3(0f, height * 0.5f + fw * 0.5f, -fd * 0.5f), new Vector3(width + fw * 2f, fw, fd));
        Part("MirrorFrameBottom", new Vector3(0f, -height * 0.5f - fw * 0.5f, -fd * 0.5f), new Vector3(width + fw * 2f, fw, fd));
        Part("MirrorFrameLeft", new Vector3(-width * 0.5f - fw * 0.5f, 0f, -fd * 0.5f), new Vector3(fw, height, fd));
        Part("MirrorFrameRight", new Vector3(width * 0.5f + fw * 0.5f, 0f, -fd * 0.5f), new Vector3(fw, height, fd));
        Part("MirrorBack", new Vector3(0f, 0f, -fd - 0.005f), new Vector3(width + fw * 2f, height + fw * 2f, 0.01f));

        // Cam: Unity Quad'ı -Z'ye bakar → 180° çevir ki odaya (+Z) baksın
        GameObject glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glass.name = "MirrorGlass";
        Collider gc = glass.GetComponent<Collider>();
        if (gc != null) Destroy(gc);
        glass.transform.SetParent(transform, false);
        glass.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        glass.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        glass.transform.localScale = new Vector3(width, height, 1f);
        glassRenderer = glass.GetComponent<Renderer>();
        glassRenderer.shadowCastingMode = ShadowCastingMode.Off;
        glassRenderer.receiveShadows = false;
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Texture");
        glassMat = new Material(sh) { name = "GarageMirrorGlass" };
        Color idle = new Color(0.55f, 0.62f, 0.68f); // çizilmezken soluk gümüş
        if (glassMat.HasProperty("_BaseColor")) glassMat.SetColor("_BaseColor", idle);
        if (glassMat.HasProperty("_Color")) glassMat.SetColor("_Color", idle);
        glassRenderer.sharedMaterial = glassMat;
    }

    private void Part(string name, Vector3 localPos, Vector3 size)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = frameMat;
    }

    private void EnsureCamera()
    {
        if (mirrorCam != null) return;
        int rtw = Mathf.Clamp(Mathf.RoundToInt(RtHeight * width / Mathf.Max(0.1f, height)), 256, 1536);
        rt = new RenderTexture(rtw, RtHeight, 16) { name = "GarageMirrorRT", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        rt.Create();
        GameObject go = new GameObject("GarageMirrorCamera");
        go.transform.SetParent(transform, false);
        mirrorCam = go.AddComponent<Camera>();
        mirrorCam.enabled = false;
        mirrorCam.targetTexture = rt;
        mirrorCam.farClipPlane = 30f;
        mirrorCam.allowMSAA = false;
        mirrorCam.allowHDR = false;
        mirrorCam.clearFlags = CameraClearFlags.SolidColor;
        mirrorCam.backgroundColor = new Color(0.08f, 0.08f, 0.09f);
        mirrorCam.depth = -12;
        UniversalAdditionalCameraData data = mirrorCam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            data.antialiasing = AntialiasingMode.None;
        }
        // Ayna görüntüsü: yatay çevrilir (sağdaki şey aynada da sağda görünsün)
        if (glassMat.HasProperty("_BaseMap"))
        {
            glassMat.SetTexture("_BaseMap", rt);
            glassMat.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
            glassMat.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
        }
        if (glassMat.HasProperty("_MainTex"))
        {
            glassMat.SetTexture("_MainTex", rt);
            glassMat.SetTextureScale("_MainTex", new Vector2(-1f, 1f));
            glassMat.SetTextureOffset("_MainTex", new Vector2(1f, 0f));
        }
        if (glassMat.HasProperty("_BaseColor")) glassMat.SetColor("_BaseColor", Color.white);
        if (glassMat.HasProperty("_Color")) glassMat.SetColor("_Color", Color.white);
    }

    // ---------------- Her kare: çizilsin mi + yansıma kamerası ----------------

    private void LateUpdate()
    {
        if (!built) return;
        Camera main = Camera.main;
        bool on = main != null && ShouldRender(main);
        if (!on)
        {
            if (mirrorCam != null && mirrorCam.enabled) mirrorCam.enabled = false;
            return;
        }
        EnsureCamera();
        PlaceCamera(main);
        mirrorCam.enabled = true;
    }

    private bool ShouldRender(Camera main)
    {
        Vector3 n = transform.forward;
        Vector3 eye = main.transform.position;
        float d = Vector3.Dot(eye - transform.position, n);
        if (d < 0.05f || d > RenderRange) return false;                       // aynanın arkasında / uzakta
        if ((eye - transform.position).sqrMagnitude > RenderRange * RenderRange) return false;
        if (glassRenderer == null) return false;
        GeometryUtility.CalculateFrustumPlanes(main, Planes); // tahsis yok
        return GeometryUtility.TestPlanesAABB(Planes, glassRenderer.bounds); // ayna ekranda değilse çizme
    }

    private void PlaceCamera(Camera main)
    {
        Vector3 n = transform.forward, c = transform.position;
        Vector3 eye = main.transform.position;
        float d = Vector3.Dot(eye - c, n);
        Vector3 reflected = eye - 2f * d * n;                                  // gözün aynaya göre simetriği (duvarın arkası)
        Transform t = mirrorCam.transform;
        t.SetPositionAndRotation(reflected, Quaternion.LookRotation(n, transform.up));

        // Eğik izdüşüm: yakın düzlem = ayna camı; çerçeve tam olarak camın köşeleri
        Vector3 right = transform.right * (width * 0.5f), up = transform.up * (height * 0.5f);
        Vector3 a = t.InverseTransformPoint(c - right - up);
        Vector3 b = t.InverseTransformPoint(c + right + up);
        float near = Mathf.Max(0.02f, d);
        float l = Mathf.Min(a.x, b.x), r = Mathf.Max(a.x, b.x);
        float bo = Mathf.Min(a.y, b.y), tp = Mathf.Max(a.y, b.y);
        float scale = near / Mathf.Max(0.0001f, (a.z + b.z) * 0.5f);
        mirrorCam.nearClipPlane = near;
        mirrorCam.projectionMatrix = Matrix4x4.Frustum(l * scale, r * scale, bo * scale, tp * scale, near, mirrorCam.farClipPlane);
    }

    // ---------------- Ayna kamerası çizerken: kendi gövden görünsün, FP kollar gizlensin ----------------

    private void CollectRenderers()
    {
        PlayerStealth me = PlayerStealth.Local;
        if (me == collectedFor && Time.unscaledTime < nextCollect && (PaintBrush.LocalHandBrush == null) == !brushCollected) return;
        collectedFor = me;
        nextCollect = Time.unscaledTime + 1.5f;
        showBody.Clear();
        hideFp.Clear();
        brushCollected = PaintBrush.LocalHandBrush != null;
        if (glassRenderer != null) hideFp.Add(glassRenderer);

        Camera main = Camera.main;
        Transform camT = main != null ? main.transform : null;
        if (camT != null) hideFp.AddRange(camT.GetComponentsInChildren<Renderer>(true));
        foreach (string fp in new[] { "FirstPersonArms", "FirstPersonHands", "FirstPersonBrush" })
        {
            GameObject go = GameObject.Find(fp);
            if (go != null) hideFp.AddRange(go.GetComponentsInChildren<Renderer>(true));
        }
        if (me != null)
        {
            foreach (Renderer r in me.GetComponentsInChildren<Renderer>(true))
                if (r != null && !hideFp.Contains(r)) showBody.Add(r);
        }
        // Elindeki fırça (üçüncü şahıs, kendi ekranında sadece gölge): aynada elinde görünsün
        Transform hb = PaintBrush.LocalHandBrush;
        if (hb != null) showBody.AddRange(hb.GetComponentsInChildren<Renderer>(true));
    }

    private readonly List<Renderer> changedBody = new List<Renderer>();
    private readonly List<Renderer> changedFp = new List<Renderer>();

    private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != mirrorCam || mirrorCam == null) return;
        CollectRenderers();
        changedBody.Clear();
        changedFp.Clear();
        foreach (Renderer r in showBody)
        {
            if (r == null || r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
            r.shadowCastingMode = ShadowCastingMode.On;
            changedBody.Add(r);
        }
        foreach (Renderer r in hideFp)
        {
            if (r == null || r.forceRenderingOff) continue;
            r.forceRenderingOff = true;
            changedFp.Add(r);
        }
    }

    private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != mirrorCam) return;
        RestoreRenderers();
    }

    private void RestoreRenderers()
    {
        foreach (Renderer r in changedBody) if (r != null) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        foreach (Renderer r in changedFp) if (r != null) r.forceRenderingOff = false;
        changedBody.Clear();
        changedFp.Clear();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.6f, 0.85f, 1f, 0.7f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(width, height, 0.02f));
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.5f);
    }
}
