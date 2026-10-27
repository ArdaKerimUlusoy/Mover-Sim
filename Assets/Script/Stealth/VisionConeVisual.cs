using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Yerde yarı saydam görüş konisi (GDD: "görüş alanları yerde koni şeklinde gösterilir").
/// Duvarlara çarpınca kesilir. Herkesin ekranında çizilir; ayarları sahibi (GuardAI / SecurityCamera) verir.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class VisionConeVisual : MonoBehaviour
{
    [SerializeField] private Material material;
    [SerializeField] private int segments = 28;
    [Tooltip("Duvar kontrolü için ışınların atıldığı yükseklik (yerden).")]
    [SerializeField] private float rayHeight = 1.2f;
    [SerializeField] private float floorOffset = 0.03f;

    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock mpb;
    private Vector3[] vertices;
    private int[] triangles;

    private float range = 10f;
    private float angle = 90f;
    private Vector3 forward = Vector3.forward;
    private Color color = new Color(1f, 0.9f, 0.3f, 0.25f);
    private float floorY;
    private bool hasFloor;

    public void Set(float newRange, float newAngle, Vector3 newForward, Color newColor)
    {
        range = newRange;
        angle = newAngle;
        newForward.y = 0f;
        if (newForward.sqrMagnitude > 0.0001f) forward = newForward.normalized;
        color = newColor;
    }

    private void Awake() => Init();

    /// <summary>Mesh / malzeme hazırlığı. Awake çalışmadan LateUpdate gelirse (sahne yeniden yüklenirken vb.) burada yapılır.</summary>
    private void Init()
    {
        if (mesh != null && mpb != null && meshRenderer != null && vertices != null) return;
        mesh = new Mesh { name = "VisionCone" };
        mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        if (material == null) material = CreateFallbackMaterial();
        meshRenderer.sharedMaterial = material;
        mpb = new MaterialPropertyBlock();

        vertices = new Vector3[segments + 2];
        triangles = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }
    }

    private void LateUpdate()
    {
        if (mesh == null || mpb == null || meshRenderer == null || vertices == null || vertices.Length != segments + 2) { mesh = null; Init(); }
        if (meshRenderer == null) return;
        Vector3 origin = transform.position;

        // Zemin yüksekliğini bir kez bul (kamera tavanda olsa bile koni yerde çizilsin)
        if (!hasFloor)
        {
            // Işın gözün hemen üstünden başlar: +0.5 m'den başlayınca bodrumdaki (tavanı alçak) kamerada üst katın
            // döşemesine çarpıp koniyi yukarıdaki zemine çiziyordu. Kendi gövdesine (üst nesnenin altı) çarpan atlanır.
            Transform self = transform.parent != null ? transform.parent : transform;
            float best = float.NegativeInfinity;
            foreach (RaycastHit h in Physics.RaycastAll(origin + Vector3.up * 0.05f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(self)) continue;
                if (h.point.y > best) best = h.point.y;
            }
            if (best > float.NegativeInfinity) { floorY = best; hasFloor = true; }
            else floorY = origin.y;
        }

        Vector3 ground = new Vector3(origin.x, floorY + floorOffset, origin.z);
        Vector3 rayOrigin = new Vector3(origin.x, floorY + rayHeight, origin.z);

        vertices[0] = transform.InverseTransformPoint(ground);
        for (int i = 0; i <= segments; i++)
        {
            float a = -angle * 0.5f + angle * i / segments;
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * forward;
            float dist = range;
            if (Physics.Raycast(rayOrigin, dir, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<PlayerStealth>() == null)
            {
                dist = hit.distance;
            }
            vertices[i + 1] = transform.InverseTransformPoint(ground + dir * dist);
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        mpb.SetColor("_BaseColor", color);
        mpb.SetColor("_Color", color);
        meshRenderer.SetPropertyBlock(mpb);
    }

    /// <summary>Malzeme verilmemişse çalışma anında yarı saydam bir URP malzemesi üretir.</summary>
    public static Material CreateFallbackMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material m = new Material(shader) { name = "VisionCone (runtime)" };
        MakeTransparent(m);
        return m;
    }

    public static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.SetInt("_Cull", (int)CullMode.Off);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", new Color(1f, 0.9f, 0.3f, 0.25f));
    }
}
