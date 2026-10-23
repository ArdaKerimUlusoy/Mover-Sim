using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BİRİNCİ ŞAHIS KOLLAR: kendi ekranında eşyayı tutan kollar, karakterin GERÇEK kollarıdır (aynı model, aynı kazak,
/// aynı eldivenler). Karakter modelinin bir kopyasından SADECE kollar (üst kol, ön kol, el, parmaklar) çizilir;
/// gövde / kafa / bacak üçgenleri atılır. Kollar iki kemikli IK ile eşyanın iki kenarına uzanır, parmaklar kenarı sarar.
/// Sadece kendi bilgisayarında, kendi kameranın altında vardır (başkaları görmez, ağ yükü yok).
/// Gerekli: Assets/Resources/Characters/BitirmeChar.fbx içe aktarma ayarında Read/Write açık (meta: isReadable 1).
/// Model yoksa / okunamıyorsa false döner → HeldItemView basit eldivenli ellere döner.
/// </summary>
public class FirstPersonArms : MonoBehaviour
{
    private const string ModelResource = "Characters/BitirmeChar";
    // Oyuncu modeline göre ölçek: kollar karakterin kendi kolu gibi dursun (çok büyük olmasın) ama eşyaya yetişsin
    private const float ViewScale = 1.35f;
    // Omuzlar ekranın altında (görünmez); sevimli modelin kolları kısa olduğu için biraz önde: eller eşyanın kenarına yetişsin
    private static readonly Vector3 ShoulderLocal = new Vector3(0f, -0.42f, 0.47f);
    private int lastPoseFrame = -10;
    private const float HandScale = 0.55f;

    private static Mesh armsMesh;
    private static bool meshFailed;

    private Transform root;
    private Transform upL, loL, handL, upR, loR, handR, midL, midR, thumbL, thumbR, indexL, indexR, littleL, littleR;
    private readonly List<Transform> fingersL = new List<Transform>(), fingersR = new List<Transform>();
    // Parmak kemikleri parmak/segment sırasıyla (5 parmak × 3 segment; Index, Middle, Ring, Little, Thumb)
    private readonly Transform[] digL = new Transform[15], digR = new Transform[15];
    private float palmSignL = 1f, palmSignR = 1f; // avuç içi yönünün işareti (modelden bir kez hesaplanır)

    // Kavrama açıları (derece; Proximal, Intermediate, Distal) — Blender'da bu modelle denenip ayarlandı
    private static readonly float[] PaintCurl = { 55f, 60f, 40f, 55f, 60f, 40f, 55f, 60f, 40f, 55f, 60f, 40f, 30f, 25f, 15f };
    private static readonly float[] PenCurl = { 15f, 25f, 15f, 40f, 50f, 30f, 70f, 80f, 50f, 75f, 80f, 50f, 15f, 20f, 10f };
    private readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
    private bool built, failed;
    private Transform cam;

    /// <summary>Kolları göster ve eşyanın iki kenarını tutacak şekilde poz ver (kamera uzayında). Yapılamazsa false.</summary>
    public bool Show(Transform camera, Vector3 itemLocal, Quaternion itemLocalRot, float halfWidth, float halfHeight)
    {
        if (!Begin(camera)) return false;

        // Avuçla kavrama: avuç içi tablonun yan kenarına yaslanır, parmaklar kenarı sarıp arkaya geçer,
        // başparmak ön yüzde durur (kendi ekranında görünen kısım başparmak + elin dışı).
        Vector3 center = cam.TransformPoint(itemLocal);
        Vector3 right = cam.TransformDirection(itemLocalRot * Vector3.right);
        for (int k = 0; k < 2; k++)
        {
            float side = k == 0 ? -1f : 1f;
            Vector3 inward = -right * side;
            Vector3 edge = center + right * (halfWidth * side);
            Vector3 wrist = edge + right * (side * 0.035f) + cam.forward * 0.01f;
            Vector3 wf = (cam.forward * 0.6f + cam.up * 0.75f + inward * 0.1f).normalized;
            if (k == 0) { ArmIK(upL, loL, handL, wrist, -1f); OrientPalm(handL, midL, indexL, littleL, palmSignL, wf, inward); CurlTo(digL, handL, midL, indexL, littleL, palmSignL, PaintCurl); }
            else { ArmIK(upR, loR, handR, wrist, 1f); OrientPalm(handR, midR, indexR, littleR, palmSignR, wf, inward); CurlTo(digR, handR, midR, indexR, littleR, palmSignR, PaintCurl); }
        }
        return true;
    }

    /// <summary>Fırça: sağ el fırçanın sapını tutar (grip = sapın ortası, dünya), sol kol ekranın dışında aşağıda kalır.</summary>
    public bool ShowBrush(Transform camera, Vector3 grip, Vector3 tipDir)
    {
        if (!Begin(camera)) return false;
        // Kalem tutuşu: başparmak ile işaret parmağının uçları sapı metal halkanın hemen altından kıstırır,
        // orta parmak destekler, yüzük / serçe avuca kıvrılır, sapın arkası avucun üstüne yaslanır.
        Vector3 tip = tipDir.normalized;
        Vector3 wf = (tip * 0.7f - cam.right * 0.2f - cam.up * 0.1f + cam.forward * 0.3f).normalized;
        Vector3 wn = (-cam.right * 0.9f - cam.up * 0.3f).normalized;
        ArmIK(upR, loR, handR, grip, 1f);
        OrientPalm(handR, midR, indexR, littleR, palmSignR, wf, wn);
        CurlTo(digR, handR, midR, indexR, littleR, palmSignR, PenCurl);
        // Parmak uçlarının buluştuğu nokta tam sapın üstüne gelsin diye bilek kaydırılır (yön değişmez)
        Vector3 off = Vector3.zero;
        if (digR[14] != null && digR[2] != null) off = (TipOf(digR[14]) + TipOf(digR[2])) * 0.5f - handR.position;
        ArmIK(upR, loR, handR, grip - off, 1f);
        OrientPalm(handR, midR, indexR, littleR, palmSignR, wf, wn);
        ArmIK(upL, loL, handL, cam.TransformPoint(new Vector3(-0.3f, -0.95f, 0.25f)), -1f); // sol kol aşağıda (görünmez)
        return true;
    }

    private bool Begin(Transform camera)
    {
        if (failed || camera == null) return false;
        cam = camera;
        if (!built && !Build()) { failed = true; return false; }
        if (root.parent != cam) root.SetParent(cam, false);
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        lastPoseFrame = Time.frameCount;
        // Dinlenme pozuna dön (her kare IK baştan)
        foreach (KeyValuePair<Transform, Quaternion> kv in rest) if (kv.Key != null) kv.Key.localRotation = kv.Value;
        return true;
    }

    /// <summary>Bu kare (ya da bir önceki) kimse poz vermediyse kollar gizlenir (eşya / fırça elden bırakıldı).</summary>
    private void Update()
    {
        if (root != null && root.gameObject.activeSelf && Time.frameCount - lastPoseFrame > 1) root.gameObject.SetActive(false);
    }

    public void Hide()
    {
        if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    private bool Build()
    {
        built = true;
        GameObject prefab = null;
        try { prefab = Resources.Load<GameObject>(ModelResource); } catch (System.Exception) { }
        if (prefab == null) return false;

        GameObject go = Instantiate(prefab);
        go.name = "FirstPersonArms";
        root = go.transform;
        foreach (Animator a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        SkinnedMeshRenderer smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null || smr.sharedMesh == null) { Destroy(go); root = null; return false; }

        Mesh arms = ArmsOnly(smr);
        if (arms == null) { Destroy(go); root = null; return false; }
        smr.sharedMesh = arms;
        smr.updateWhenOffscreen = true;
        smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        smr.receiveShadows = false;

        var bones = new Dictionary<string, Transform>();
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
        System.Func<string, Transform> B = n => bones.TryGetValue(n, out Transform bt) ? bt : null;

        // Kameraya göre: model önü = kamera önü; omuzların ortası ekranın altında
        root.SetParent(cam, false);
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
        Transform foot = B("Foot.L"), toes = B("Toes.L");
        if (foot != null && toes != null)
        {
            Vector3 f = cam.InverseTransformDirection(toes.position - foot.position);
            f.y = 0f;
            if (f.sqrMagnitude > 1e-6f) root.localRotation = Quaternion.FromToRotation(f.normalized, Vector3.forward);
        }
        // Boy: oyuncu modeliyle aynı ölçek × ViewScale (mesh sınırından)
        Bounds mb = smr.sharedMesh.bounds;
        float h = Mathf.Max(mb.size.x, mb.size.y, mb.size.z) * Mathf.Max(0.0001f, smr.transform.lossyScale.y / Mathf.Max(0.0001f, root.lossyScale.y));
        float s = h > 0.01f ? 2.0f / h * ViewScale : ViewScale;
        root.localScale = Vector3.one * s;

        // Kol kemikleri (kameranın solundaki "sol")
        Transform ul = B("UpperArm.L"), ur = B("UpperArm.R");
        bool swap = ul != null && ur != null && cam.InverseTransformPoint(ul.position).x > cam.InverseTransformPoint(ur.position).x;
        string L = swap ? ".R" : ".L", R = swap ? ".L" : ".R";
        upL = B("UpperArm" + L); loL = B("LowerArm" + L); handL = B("Hand" + L);
        upR = B("UpperArm" + R); loR = B("LowerArm" + R); handR = B("Hand" + R);
        midL = B("Middle_Proximal" + L); midR = B("Middle_Proximal" + R);
        thumbL = B("Thumb_Proximal" + L); thumbR = B("Thumb_Proximal" + R);
        indexL = B("Index_Proximal" + L); indexR = B("Index_Proximal" + R);
        littleL = B("Little_Proximal" + L); littleR = B("Little_Proximal" + R);
        string[] dn = { "Index", "Middle", "Ring", "Little", "Thumb" }, sn = { "_Proximal", "_Intermediate", "_Distal" };
        for (int f = 0; f < 5; f++) for (int g = 0; g < 3; g++) { digL[f * 3 + g] = B(dn[f] + sn[g] + L); digR[f * 3 + g] = B(dn[f] + sn[g] + R); }
        if (upL == null || loL == null || handL == null || upR == null || loR == null || handR == null) { Destroy(go); root = null; return false; }
        foreach (string f in new[] { "Index", "Middle", "Ring", "Little", "Thumb" })
            foreach (string seg in new[] { "_Proximal", "_Intermediate", "_Distal" })
            {
                Transform tl = B(f + seg + L), tr = B(f + seg + R);
                if (tl != null) fingersL.Add(tl);
                if (tr != null) fingersR.Add(tr);
            }
        foreach (Transform t in new[] { upL, loL, handL, upR, loR, handR }) rest[t] = t.localRotation;
        // Sevimli karakterin eldivenleri çok büyük: birinci şahısta ekranı kapatmasın diye eller (parmaklarla) küçültülür
        handL.localScale = handL.localScale * HandScale;
        handR.localScale = handR.localScale * HandScale;
        foreach (Transform t in fingersL) rest[t] = t.localRotation;
        foreach (Transform t in fingersR) rest[t] = t.localRotation;
        // Avuç içi: dinlenme (A) pozunda gövdeye bakar → işaret buna göre seçilir (model eksenlerinden bağımsız)
        palmSignL = PalmSign(handL, midL, indexL, littleL, cam.right);
        palmSignR = PalmSign(handR, midR, indexR, littleR, -cam.right);

        // Omuzların ortasını kamera uzayında ShoulderLocal'a taşı
        Vector3 shMid = cam.InverseTransformPoint((upL.position + upR.position) * 0.5f);
        root.localPosition += ShoulderLocal - shMid;
        return true;
    }

    /// <summary>Modelin mesh'inden sadece kol / el / parmak üçgenlerini tutan kopya (bir kez, herkes paylaşır).</summary>
    private static Mesh ArmsOnly(SkinnedMeshRenderer smr)
    {
        if (armsMesh != null) return armsMesh;
        if (meshFailed) return null;
        Mesh src = smr.sharedMesh;
        if (!src.isReadable)
        {
            meshFailed = true;
            Debug.LogWarning("[FirstPersonArms] BitirmeChar.fbx Read/Write kapalı: birinci şahıs kollar için Import Settings → Model → Read/Write açılmalı.");
            return null;
        }
        Transform[] bones = smr.bones;
        var armBone = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            string n = bones[i] != null ? bones[i].name : "";
            armBone[i] = n.StartsWith("UpperArm") || n.StartsWith("LowerArm") || n.StartsWith("Hand") || n.StartsWith("Grip") ||
                         n.StartsWith("Index") || n.StartsWith("Middle") || n.StartsWith("Ring") || n.StartsWith("Little") || n.StartsWith("Thumb");
        }
        BoneWeight[] w = src.boneWeights;
        if (w == null || w.Length != src.vertexCount) { meshFailed = true; return null; }
        var keepV = new bool[w.Length];
        for (int v = 0; v < w.Length; v++)
        {
            BoneWeight b = w[v];
            float arm = 0f;
            if (b.boneIndex0 < armBone.Length && armBone[b.boneIndex0]) arm += b.weight0;
            if (b.boneIndex1 < armBone.Length && armBone[b.boneIndex1]) arm += b.weight1;
            if (b.boneIndex2 < armBone.Length && armBone[b.boneIndex2]) arm += b.weight2;
            if (b.boneIndex3 < armBone.Length && armBone[b.boneIndex3]) arm += b.weight3;
            keepV[v] = arm >= 0.5f;
        }
        Mesh m = Instantiate(src);
        m.name = "BitirmeChar_ArmsOnly";
        m.ClearBlendShapes();
        for (int sm = 0; sm < src.subMeshCount; sm++)
        {
            int[] tris = src.GetTriangles(sm);
            var kept = new List<int>(tris.Length / 4);
            for (int i = 0; i + 2 < tris.Length; i += 3)
                if (keepV[tris[i]] && keepV[tris[i + 1]] && keepV[tris[i + 2]]) { kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]); }
            m.SetTriangles(kept, sm);
        }
        armsMesh = m;
        return m;
    }

    private static Vector3 PalmNormal(Transform hand, Transform mid, Transform index, Transform little, float sign, out Vector3 finger)
    {
        finger = (mid.position - hand.position).normalized;
        Vector3 across = little.position - index.position;
        Vector3 n = Vector3.ProjectOnPlane(Vector3.Cross(across, finger) * sign, finger);
        return n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up;
    }

    private static float PalmSign(Transform hand, Transform mid, Transform index, Transform little, Vector3 towardBody)
    {
        if (mid == null || index == null || little == null) return 1f;
        Vector3 n = PalmNormal(hand, mid, index, little, 1f, out _);
        return Vector3.Dot(n, towardBody) >= 0f ? 1f : -1f;
    }

    /// <summary>Eli tam yönlendir: parmaklar wantFinger'a, avuç içi wantPalm'a baksın (tek dönüş, bilek konumu değişmez).</summary>
    private static void OrientPalm(Transform hand, Transform mid, Transform index, Transform little, float sign, Vector3 wantFinger, Vector3 wantPalm)
    {
        if (mid == null) return;
        if (index == null || little == null) { hand.rotation = Quaternion.FromToRotation((mid.position - hand.position).normalized, wantFinger) * hand.rotation; return; }
        Vector3 n = PalmNormal(hand, mid, index, little, sign, out Vector3 f);
        hand.rotation = Quaternion.LookRotation(wantFinger, wantPalm) * Quaternion.Inverse(Quaternion.LookRotation(f, n)) * hand.rotation;
    }

    /// <summary>Parmakları avuç içine doğru, parmak/segment başına verilen açılarla kıvır (eklemler doğal yönde bükülür).</summary>
    private static void CurlTo(Transform[] dig, Transform hand, Transform mid, Transform index, Transform little, float sign, float[] ang)
    {
        if (mid == null || index == null || little == null) return;
        Vector3 n = PalmNormal(hand, mid, index, little, sign, out _);
        for (int i = 0; i < dig.Length; i++)
        {
            Transform t = dig[i];
            if (t == null) continue;
            Vector3 d = t.childCount > 0 ? t.GetChild(0).position - t.position : t.position - t.parent.position;
            Vector3 axis = Vector3.Cross(d, n);
            if (axis.sqrMagnitude < 1e-10f) continue;
            t.rotation = Quaternion.AngleAxis(ang[i], axis.normalized) * t.rotation;
        }
    }

    /// <summary>Parmak ucunun yaklaşık konumu (son segment bir boy daha uzatılır).</summary>
    private static Vector3 TipOf(Transform distal)
    {
        if (distal == null) return Vector3.zero;
        return distal.parent != null ? distal.position + (distal.position - distal.parent.position) : distal.position;
    }

    private void ArmIK(Transform upper, Transform lower, Transform hand, Vector3 target, float side)
    {
        Vector3 S = upper.position, E = lower.position, W = hand.position;
        float a = Vector3.Distance(S, E), b = Vector3.Distance(E, W);
        if (a < 1e-4f || b < 1e-4f) return;
        Vector3 toT = target - S;
        float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
        Vector3 dir = toT.normalized;
        Vector3 pole = -cam.up + cam.right * (0.7f * side);
        Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = -cam.up;
        bend.Normalize();
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
        Vector3 elbow = S + dir * (a * cosA) + bend * (a * Mathf.Sqrt(1f - cosA * cosA));
        upper.rotation = Quaternion.FromToRotation(E - S, elbow - S) * upper.rotation;
        Vector3 E2 = lower.position, W2 = hand.position;
        lower.rotation = Quaternion.FromToRotation(W2 - E2, (S + dir * d) - E2) * lower.rotation;
    }

}
