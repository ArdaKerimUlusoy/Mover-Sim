using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Güvenlik aynası: koridor / kapı yanındaki, tavana yakın, aşağı-içe dönük dışbükey ayna.
/// Ağ gerektirmez; MuseumLayout her istemcide aynı tohumla aynı yere kurar (Runtime_Interior altında, düzen yenilenince silinir).
/// Performans: yalnızca yerel oyuncu yakındaysa ve aynaya bakıyorsa, en fazla BİR ayna (en yakın) çizilir (256x256);
/// diğerleri son kareyi tutar. RenderTexture / kamera ilk kez çizileceği zaman oluşturulur.
/// Etkileşim: aynaya bakıp E basılı tutunca ekranın ortasında büyük ayna görüntüsü açılır.
/// </summary>
public class SecurityMirror : MonoBehaviour
{
    private const float ViewRange = 8f;        // bu mesafeden yakında + bakıyorsa çizilir
    private const float ZoomRange = 4f;        // E ile yakınlaştırma mesafesi
    private const float ViewCos = 0.64f;       // bakış konisi (~50°)
    private const int RtSize = 256;
    private const float Radius = 0.45f;

    private static readonly List<SecurityMirror> All = new List<SecurityMirror>();
    private static int evalFrame = -1;
    private static SecurityMirror zoomed;      // bu karede yakın görünüm açık olan ayna
    private static SecurityMirror hinted;      // bu karede "E: yakından bak" ipucu gösterilecek ayna

    private Vector3 normal;                    // dünya, aynanın baktığı yön
    private bool fixedProbe;                   // true: kamera sabit bir noktadan bakar (bodrum merdiveni)
    private Vector3 probePos;
    private Quaternion probeRot;

    private RenderTexture rt;
    private Camera mirrorCam;
    private Material faceMat;
    private Material frameMat;
    private Mesh faceMesh;
    private bool rendered;                     // en az bir kare çizildi mi

    /// <summary>Ayna kurar. pos: ayna merkezi (dünya), nrm: yüzey normali (dünya, odaya doğru).
    /// probe verilirse kamera o noktadan o yöne bakar (merdivenden bodrumu göstermek için).</summary>
    public static SecurityMirror Create(Transform parent, Vector3 pos, Vector3 nrm, Vector3? probeWorldPos = null, Vector3? probeWorldDir = null)
    {
        nrm.Normalize();
        var go = new GameObject("SecurityMirror");
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        // Yerel +Z duvara/arkaya, görünen yüz -Z (normal yönü)
        go.transform.rotation = Quaternion.LookRotation(-nrm, Vector3.up);
        SecurityMirror m = go.AddComponent<SecurityMirror>();
        m.normal = nrm;
        if (probeWorldPos.HasValue && probeWorldDir.HasValue)
        {
            m.fixedProbe = true;
            m.probePos = probeWorldPos.Value;
            m.probeRot = Quaternion.LookRotation(probeWorldDir.Value, Vector3.up);
        }
        m.BuildVisual();
        return m;
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }

    private void OnDisable() { All.Remove(this); if (zoomed == this) zoomed = null; }

    private void OnDestroy()
    {
        All.Remove(this);
        if (zoomed == this) zoomed = null;
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (faceMat != null) Destroy(faceMat);
        if (frameMat != null) Destroy(frameMat);
        if (faceMesh != null) Destroy(faceMesh);
    }

    // ---------------- Görsel ----------------

    private void BuildVisual()
    {
        // Çerçeve: yassı silindir (eksen Z), arkada
        frameMat = ArtworkLook.MakeLit(new Color(0.12f, 0.12f, 0.14f));
        GameObject frame = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        frame.name = "MirrorFrame";
        Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(transform, false);
        frame.transform.localPosition = new Vector3(0f, 0f, 0.035f);
        frame.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        frame.transform.localScale = new Vector3(Radius * 2f + 0.14f, 0.035f, Radius * 2f + 0.14f);
        frame.GetComponent<Renderer>().sharedMaterial = frameMat;

        // Duvar kolu
        GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm.name = "MirrorArm";
        Destroy(arm.GetComponent<Collider>());
        arm.transform.SetParent(transform, false);
        arm.transform.localPosition = new Vector3(0f, 0f, 0.1f);
        arm.transform.localScale = new Vector3(0.07f, 0.07f, 0.14f);
        arm.GetComponent<Renderer>().sharedMaterial = frameMat;

        // Ayna yüzü: yuvarlak disk, UV yatay çevrili (ayna görüntüsü)
        faceMesh = BuildDisc(Radius, 36);
        var face = new GameObject("MirrorFace");
        face.transform.SetParent(transform, false);
        face.transform.localPosition = new Vector3(0f, 0f, -0.002f);
        face.AddComponent<MeshFilter>().sharedMesh = faceMesh;
        var mr = face.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Texture");
        faceMat = new Material(sh) { name = "SecurityMirrorMat" };
        Color grey = new Color(0.45f, 0.5f, 0.55f);
        if (faceMat.HasProperty("_BaseColor")) faceMat.SetColor("_BaseColor", grey);
        if (faceMat.HasProperty("_Color")) faceMat.SetColor("_Color", grey);
        mr.sharedMaterial = faceMat;
    }

    private static Mesh BuildDisc(float r, int seg)
    {
        var verts = new Vector3[seg + 1];
        var uvs = new Vector2[seg + 1];
        var nrms = new Vector3[seg + 1];
        verts[0] = Vector3.zero; uvs[0] = new Vector2(0.5f, 0.5f); nrms[0] = Vector3.back;
        for (int i = 0; i < seg; i++)
        {
            float a = -i * Mathf.PI * 2f / seg;           // saat yönü (görünen yüz -Z)
            float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
            verts[i + 1] = new Vector3(cx * r, cy * r, 0f);
            uvs[i + 1] = new Vector2(0.5f - cx * 0.5f, 0.5f + cy * 0.5f); // yatay çevrili
            nrms[i + 1] = Vector3.back;
        }
        var tris = new int[seg * 3];
        for (int i = 0; i < seg; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % seg + 1;
        }
        var m = new Mesh { name = "MirrorDisc" };
        m.vertices = verts; m.uv = uvs; m.normals = nrms; m.triangles = tris;
        m.RecalculateBounds();
        return m;
    }

    private void EnsureCamera()
    {
        if (mirrorCam != null) return;
        rt = new RenderTexture(RtSize, RtSize, 16) { name = "SecurityMirrorRT", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        rt.Create();
        var go = new GameObject("MirrorCamera");
        go.transform.SetParent(transform, false);
        mirrorCam = go.AddComponent<Camera>();
        mirrorCam.enabled = false;
        mirrorCam.targetTexture = rt;
        mirrorCam.fieldOfView = 100f;          // dışbükey ayna hissi: geniş açı
        mirrorCam.nearClipPlane = 0.1f;
        mirrorCam.farClipPlane = 40f;
        mirrorCam.aspect = 1f;
        mirrorCam.allowMSAA = false;
        mirrorCam.allowHDR = false;
        mirrorCam.clearFlags = CameraClearFlags.SolidColor;
        mirrorCam.backgroundColor = new Color(0.05f, 0.05f, 0.07f);
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
        if (faceMat.HasProperty("_BaseMap")) faceMat.SetTexture("_BaseMap", rt);
        if (faceMat.HasProperty("_MainTex")) faceMat.SetTexture("_MainTex", rt);
        if (faceMat.HasProperty("_BaseColor")) faceMat.SetColor("_BaseColor", Color.white);
        if (faceMat.HasProperty("_Color")) faceMat.SetColor("_Color", Color.white);
    }

    // ---------------- Karar: hangi ayna çizilsin / yakın görünüm ----------------

    private static bool InputBlocked =>
        ChatUI.IsTyping || SessionMenuUI.IsPaused || PlayerInventory.IsPanelOpen || PlayerController.InputLocked ||
        Cursor.lockState != CursorLockMode.Locked || PushSystem.LocalKnockedOut || BodyCam.PanelOpen;

    /// <summary>Oyuncu bu aynanın önünde ve ona mı bakıyor? dist: mesafe.</summary>
    private bool IsLookedAt(Transform cam, float range, out float dist)
    {
        Vector3 to = transform.position - cam.position;
        dist = to.magnitude;
        if (dist > range || dist < 0.05f) return false;
        Vector3 dir = to / dist;
        if (Vector3.Dot(cam.forward, dir) < ViewCos) return false;          // bakmıyor
        if (Vector3.Dot(normal, -dir) <= 0.05f) return false;                // aynanın arkasında
        return true;
    }

    private static void Evaluate()
    {
        zoomed = null;
        hinted = null;
        Camera main = Camera.main;
        SecurityMirror best = null;
        if (main != null && All.Count > 0)
        {
            Transform ct = main.transform;
            float bestD = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                SecurityMirror m = All[i];
                if (m == null) continue;
                if (m.IsLookedAt(ct, ViewRange, out float d) && d < bestD) { bestD = d; best = m; }
            }

            // E basılı tut: yakın görünüm (yalnızca etkileşilebilir bir şeye bakmıyorsa)
            if (best != null && bestD <= ZoomRange && !InputBlocked && Keyboard.current != null && Keyboard.current.eKey.isPressed)
            {
                bool otherTarget = false;
                if (Physics.Raycast(ct.position, ct.forward, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                    otherTarget = hit.collider.GetComponentInParent<Interactable>() != null || hit.collider.GetComponentInParent<WorldItem>() != null;
                if (!otherTarget) zoomed = best;
            }
            else if (best != null && bestD <= ZoomRange && !InputBlocked) hinted = best;
        }

        for (int i = 0; i < All.Count; i++)
        {
            SecurityMirror m = All[i];
            if (m == null) continue;
            bool on = m == best;
            if (on) m.PrepareAndEnable(main != null ? main.transform : null);
            else if (m.mirrorCam != null) m.mirrorCam.enabled = false;
        }
    }

    private void PrepareAndEnable(Transform viewer)
    {
        EnsureCamera();
        Transform t = mirrorCam.transform;
        if (fixedProbe)
        {
            t.SetPositionAndRotation(probePos, probeRot);
        }
        else if (viewer != null)
        {
            // Düzlem yansıması: kamera ayna yüzeyinden, bakış ışınının yansıma yönüne bakar (duvar arkasında kalmaz)
            Vector3 incoming = (transform.position - viewer.position).normalized;
            Vector3 r = Vector3.Reflect(incoming, normal);
            t.SetPositionAndRotation(transform.position + normal * 0.12f, Quaternion.LookRotation(r, Vector3.up));
        }
        mirrorCam.enabled = true;
        rendered = true;
    }

    private void LateUpdate()
    {
        // Kare başına bir kez (ilk ayna) hesaplanır
        if (evalFrame == Time.frameCount) return;
        evalFrame = Time.frameCount;
        Evaluate();
    }

    // ---------------- Yakın görünüm ----------------

    private void OnGUI()
    {
        if (hinted == this && Event.current.type == EventType.Repaint)
            UIUtil.DrawPrompt(Loc.T("mirror.hint"), Screen.height * 0.62f, new Color(1f, 1f, 1f, 0.9f), 16);
        if (zoomed != this || !rendered || rt == null || Event.current.type != EventType.Repaint) return;
        float size = Mathf.Min(Screen.height * 0.72f, Screen.width * 0.7f);
        var r = new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f - 10f, size, size);
        UIUtil.Panel(new Rect(r.x - 10f, r.y - 10f, r.width + 20f, r.height + 20f));
        // Ayna görüntüsü yatay çevrili çizilir
        GUI.DrawTextureWithTexCoords(r, rt, new Rect(1f, 0f, -1f, 1f), false);
        UIUtil.DrawPrompt(Loc.T("mirror.zoom"), r.yMax + 18f, new Color(0.85f, 0.9f, 1f), 18);
    }
}
