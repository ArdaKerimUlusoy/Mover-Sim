using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Atölyedeki çömlek çarkı (vazo sahtesi). WorkbenchUI çalışma anında oluşturur.
///   Şekillendirme: Sol tık = tüm halkayı içeri it, Sağ tık = dışarı çek, Shift + tık = tek nokta,
///                  Orta tuş = çevir, Tekerlek = fırça boyutu
///   Boyama:        Sol tık = boya, Shift + sol tık = bant (tüm halka), B = hepsini doldur
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class ClaySculptor : MonoBehaviour
{
    public enum Mode { Sculpt, Paint }

    public Mode mode = Mode.Sculpt;
    public Camera cam;
    public Color brushColor = Color.blue;
    public int paintBrushSize = 12;

    private int radialSegments = 40, heightSegments = 50;
    private float height = 0.5f, startRadius = 0.15f;
    private float brushRadius = 0.06f, deformStrength = 0.12f, minRadius = 0.02f, maxRadius = 0.3f;
    private const float RotateSensitivity = 0.25f;
    private const int TexRes = 512;

    private Mesh mesh;
    private Vector3[] vertices;
    private MeshCollider meshCollider;
    private Texture2D tex;
    private Color[] pixels;

    public Texture2D PaintTexture => tex;

    private void Awake()
    {
        meshCollider = GetComponent<MeshCollider>();
        GetComponent<MeshRenderer>().sharedMaterial = ArtworkLook.MakeLit(Color.white);
    }

    /// <summary>Hedefle aynı yükseklikte, biraz kalın bir kil silindiri hazırlar.</summary>
    public void Setup(VaseLook target)
    {
        height = target.height;
        startRadius = target.maxRadius * 1.15f;
        brushRadius = height * 0.12f;
        deformStrength = target.maxRadius * 0.8f;
        minRadius = target.maxRadius * 0.15f;
        maxRadius = target.maxRadius * 1.6f;
        mode = Mode.Sculpt;
        transform.rotation = Quaternion.identity;
        BuildCylinder();
        SetupTexture();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        Keyboard kb = Keyboard.current;
        if (mouse == null || cam == null) return;

        bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        Vector2 pos = mouse.position.ReadValue();

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float dir = Mathf.Sign(scroll);
            if (mode == Mode.Sculpt) brushRadius = Mathf.Clamp(brushRadius * (dir > 0 ? 1.15f : 0.87f), height * 0.03f, height * 0.6f);
            else paintBrushSize = Mathf.Clamp(paintBrushSize + (int)dir * 2, 2, 64);
        }

        if (mouse.middleButton.isPressed)
        {
            transform.Rotate(0f, -mouse.delta.ReadValue().x * RotateSensitivity, 0f, Space.World);
            return;
        }

        if (mode == Mode.Sculpt)
        {
            if (mouse.leftButton.isPressed) Deform(-1f, pos, !shift);
            else if (mouse.rightButton.isPressed) Deform(1f, pos, !shift);
        }
        else
        {
            if (mouse.leftButton.isPressed) Paint(pos, shift);
            if (kb != null && kb.bKey.wasPressedThisFrame) Fill(brushColor);
        }
    }

    // ---------------- Mesh ----------------

    private void BuildCylinder()
    {
        if (mesh == null) mesh = new Mesh { name = "Clay" };
        else mesh.Clear();

        int rowStride = radialSegments + 1;
        int body = rowStride * (heightSegments + 1);
        vertices = new Vector3[body + 2];
        var uvs = new Vector2[body + 2];
        float dA = Mathf.PI * 2f / radialSegments;
        int vi = 0;
        for (int y = 0; y <= heightSegments; y++)
            for (int r = 0; r <= radialSegments; r++)
            {
                float a = r * dA;
                vertices[vi] = new Vector3(Mathf.Cos(a) * startRadius, y * height / heightSegments, Mathf.Sin(a) * startRadius);
                uvs[vi] = new Vector2((float)r / radialSegments, (float)y / heightSegments);
                vi++;
            }
        int bottom = vi++, top = vi++;
        vertices[bottom] = Vector3.zero;
        vertices[top] = new Vector3(0f, height, 0f);
        uvs[bottom] = new Vector2(0.5f, 0.02f);
        uvs[top] = new Vector2(0.5f, 0.98f);

        var tris = new int[radialSegments * heightSegments * 6 + radialSegments * 6];
        int ti = 0;
        for (int y = 0; y < heightSegments; y++)
            for (int r = 0; r < radialSegments; r++)
            {
                int cur = y * rowStride + r, next = cur + rowStride;
                tris[ti++] = cur; tris[ti++] = next; tris[ti++] = cur + 1;
                tris[ti++] = cur + 1; tris[ti++] = next; tris[ti++] = next + 1;
            }
        for (int r = 0; r < radialSegments; r++) { tris[ti++] = bottom; tris[ti++] = r + 1; tris[ti++] = r; }
        int topRow = heightSegments * rowStride;
        for (int r = 0; r < radialSegments; r++) { tris[ti++] = top; tris[ti++] = topRow + r; tris[ti++] = topRow + r + 1; }

        mesh.vertices = vertices;
        mesh.triangles = tris;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().sharedMesh = mesh;
        meshCollider.convex = false;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }

    private void Deform(float direction, Vector2 screenPos, bool wholeRing)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (!meshCollider.Raycast(ray, out RaycastHit hit, 50f)) return;
        Vector3 local = transform.InverseTransformPoint(hit.point);

        bool changed = false;
        int body = (radialSegments + 1) * (heightSegments + 1);
        for (int i = 0; i < body; i++)
        {
            Vector3 v = vertices[i];
            float dist = wholeRing ? Mathf.Abs(v.y - local.y) : Vector3.Distance(v, local);
            if (dist >= brushRadius) continue;

            float falloff = Mathf.Cos(dist / brushRadius * Mathf.PI * 0.5f);
            Vector2 h = new Vector2(v.x, v.z);
            float cur = h.magnitude;
            if (cur < 0.0001f) continue;
            float nr = Mathf.Clamp(cur + direction * deformStrength * falloff * Time.deltaTime, minRadius, maxRadius);
            Vector2 d = h / cur;
            vertices[i] = new Vector3(d.x * nr, v.y, d.y * nr);
            changed = true;
        }
        if (!changed) return;
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }

    // ---------------- Boya ----------------

    private void SetupTexture()
    {
        if (tex == null)
        {
            tex = new Texture2D(TexRes, TexRes, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Material m = GetComponent<MeshRenderer>().sharedMaterial;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
        }
        Fill(ArtworkLook.ClayColor);
    }

    public void Fill(Color c)
    {
        if (pixels == null) pixels = new Color[TexRes * TexRes];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        tex.SetPixels(pixels);
        tex.Apply();
    }

    private void Paint(Vector2 screenPos, bool band)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);
        if (!meshCollider.Raycast(ray, out RaycastHit hit, 50f)) return;
        Vector2 uv = hit.textureCoord;
        int cx = (int)(uv.x * TexRes), cy = (int)(uv.y * TexRes);

        if (band)
        {
            int r = paintBrushSize / 2;
            for (int py = Mathf.Max(0, cy - r); py <= Mathf.Min(TexRes - 1, cy + r); py++)
                for (int px = 0; px < TexRes; px++) pixels[py * TexRes + px] = brushColor;
        }
        else
        {
            int r = paintBrushSize;
            for (int y = -r; y <= r; y++)
            {
                int py = cy + y;
                if (py < 0 || py >= TexRes) continue;
                for (int x = -r; x <= r; x++)
                {
                    if (x * x + y * y > r * r) continue;
                    int px = (cx + x + TexRes) % TexRes; // silindir etrafında sarmala
                    pixels[py * TexRes + px] = brushColor;
                }
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
    }

    // ---------------- Benzerlik (GDD Bölüm 4'ün basit karşılığı) ----------------

    /// <summary>Her yükseklik halkasının ortalama yarıçapı hedef profille karşılaştırılır. 0..1</summary>
    public float ShapeScore(VaseLook target)
    {
        if (vertices == null || target == null) return 0f;
        int rowStride = radialSegments + 1;
        float total = 0f;
        for (int y = 0; y <= heightSegments; y++)
        {
            float sum = 0f;
            for (int r = 0; r < radialSegments; r++)
            {
                Vector3 v = vertices[y * rowStride + r];
                sum += Mathf.Sqrt(v.x * v.x + v.z * v.z);
            }
            float clayR = sum / radialSegments;
            total += Mathf.Abs(clayR - target.Radius((float)y / heightSegments)) / Mathf.Max(0.0001f, target.maxRadius);
        }
        return Mathf.Clamp01(1f - total / (heightSegments + 1) / 0.25f);
    }

    /// <summary>Boyanan doku orijinal desenle 32x32 örnekte karşılaştırılır. 0..1</summary>
    public float PaintScore(Texture2D reference)
    {
        if (pixels == null || reference == null) return 0f;
        const int N = 32;
        float total = 0f;
        for (int y = 0; y < N; y++)
        {
            float v = (y + 0.5f) / N;
            int py = Mathf.Min(TexRes - 1, (int)(v * TexRes));
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N;
                Color a = pixels[py * TexRes + Mathf.Min(TexRes - 1, (int)(u * TexRes))];
                Color b = reference.GetPixelBilinear(u, v);
                total += Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b)) / 1.732f;
            }
        }
        return Mathf.Clamp01(1f - total / (N * N) / 0.3f);
    }
}
