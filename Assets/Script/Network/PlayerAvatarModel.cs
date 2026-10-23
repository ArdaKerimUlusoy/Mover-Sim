using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ANA KARAKTER MODELİ (BitirmeChar): PlayerAvatar'ın kodla kurulan gövdesinin yerine tasarımcının karakteri görünür.
/// Model "Assets/Resources/Characters/BitirmeChar" (FBX) içinden yüklenir; bulunamazsa eski kodla kurulan gövde kalır.
///
/// Animasyonlar kodla, kemiklere uygulanır (ayrı animasyon dosyası / Animator gerekmez, ağda ek veri gitmez:
/// her bilgisayar zaten senkron olan hız, eğilme, elde eşya, bakış ve baygınlık bilgisinden hesaplar):
///   - Durma: nefes alma (göğüs / omuz hafif iner kalkar), kollar yanlarda, ara sıra göz kırpma
///   - Yürüme / koşma: hıza göre bacak - kol salınımı, diz bükülmesi, gövde zıplaması ve öne eğilme
///     (Shift ile koşarken adımlar büyür, gövde daha çok eğilir, dirsekler bükülür)
///   - Zıplama: havadayken dizler toplanır, kollar hafifçe yukarı açılır; inişte gövde yaylanır
///   - Eğilme (çömelme): kalça iner, dizler bükülür (ayaklar yerde kalır), gövde öne eğilir; eğilerek yürüme de olur
///   - Tutma: elinde eser / eşya varken iki kol öne uzanır, dirsekler bükülür, eller önde birleşir
///   - Bakış: kafa, oyuncunun baktığı yöne (yukarı / aşağı) eğilir
///   - Baygınlık: sendeler, kollarını savurur, yüzüstü düşer; gözler kapalı, ağız açık (PlayerAvatar'ın düşme pozu)
/// Eski gövdenin eklem dönüşleri (kol, bacak, kafa, gövde) her karede hesaplanmaya devam eder; model kemikleri bunları
/// izler. Böylece baygınlık gibi eski hareketler de aynen modele geçer.
/// Oyuncu rengi karakterin beresine işlenir (ekipte kim kim belli olsun).
/// TEST: F5 → kendi karakterini arkadan gör (üçüncü şahıs önizleme kamerası; oyun mantığı birinci şahıs kamerayla
/// çalışmaya devam eder, sadece ekrana arkadan görüntü gelir). Tekrar F5 → normal görünüm.
/// Önizlemede ORTA TUŞU basılı tutup fareyi oynat → kamera karakterin etrafında döner (yüzüne de bakılır; bu sırada
/// yürümeye devam edebilirsin). Alt + tekerlek: uzaklık. Bırakınca açı kalır; F5 kapat-aç sıfırlar.
/// </summary>
public partial class PlayerAvatar
{
    private const string ModelResource = "Characters/BitirmeChar";
    private const float ModelHeight = 2.0f; // CharacterController boyu (kamera ~1.6 m: karakterin göz hizası)

    private class Bone
    {
        public Transform t;
        public Quaternion restLocal;
        public Quaternion restRel;     // kökün (root) uzayında dinlenme dönüşü
        public Vector3 restLocalPos;
    }

    private Transform model;
    private SkinnedMeshRenderer modelSkin;
    private Material modelBeretMat;
    private int blinkShape = -1, mouthShape = -1;
    private float nextBlink, blinkT;
    private Color pendingColor = Color.white;
    private bool hasPendingColor;

    private Bone mHips, mSpine, mChest, mNeck, mHead;
    private Bone mUpArmL, mLoArmL, mHandL, mUpArmR, mLoArmR, mHandR;
    private Bone mUpLegL, mLoLegL, mFootL, mUpLegR, mLoLegR, mFootR;
    private Quaternion armFixL = Quaternion.identity, armFixR = Quaternion.identity;
    private float thighLen = 0.3f, shinLen = 0.3f, armReach = 0.4f;
    private float airSmooth, runSmooth, brushSmooth, paintSmooth;
    private float strafeSmooth, legRollL, legRollR; // yana yürüme (PlayerAvatar.Update yazar)
    private Material modelOverallMat;
    private bool ownerHiddenApplied;
    private bool brushHold; // fırça elde: sağ kol öne kalkar (PlayerAvatar.Update yazar)

    // F5 önizleme (sadece kendi oyuncunda)
    private Camera previewCam;
    private bool previewOn;
    private float previewDist = 2.8f;
    private float orbitYaw, orbitPitch = 12f;
    private bool previewHiddenApplied;
    /// <summary>F5 önizleme açık mı? (Açıkken orta tuş işaret koymaz, yörünge kamerası içindir.)</summary>
    public static bool PreviewActive { get; private set; }

    /// <summary>Model yüklendi mi? (Yüklenmediyse eski kodla kurulan gövde görünür.)</summary>
    public bool HasCharacterModel => model != null;

    /// <summary>Elde tutulan eşyanın duracağı yer: karakterin iki elinin ortası (HeldItemView). handGap = eller arası mesafe.</summary>
    public bool TryGetHoldPoint(out Vector3 point, out float handGap)
    {
        point = Vector3.zero;
        handGap = 0f;
        if (model == null || mUpArmL == null || mUpArmR == null || mUpArmL.t == null || mUpArmR.t == null) return false;
        // Eşya göğsün önünde, kollar öne uzanmış: iki omzun ortasından biraz aşağı ve öne (eller onun iki yanını kavrar — IK)
        Vector3 sh = (mUpArmL.t.position + mUpArmR.t.position) * 0.5f;
        handGap = Vector3.Distance(mUpArmL.t.position, mUpArmR.t.position);
        // Kollar kısa (sevimli karakter): eşyayı kolların uzanabileceği en öne koy (eller iki yanına yetişsin)
        if (heldView == null) heldView = GetComponent<HeldItemView>();
        float half = heldView != null ? Mathf.Min(heldView.HalfWidth + 0.015f, armReach * 0.8f) : 0.15f;
        float lat = Mathf.Abs(half - handGap * 0.5f);
        float down = armReach * 0.38f;
        float reach = armReach * 0.93f;
        float fwd = Mathf.Sqrt(Mathf.Max(0.01f, reach * reach - down * down - lat * lat));
        point = sh + Vector3.down * down + body.forward * fwd;
        return true;
    }

    /// <summary>Fırçanın duracağı yer: sağ avucun içi; dir = fırçanın ucunun baktığı yön (öne-yukarı, boyarken daha öne).</summary>
    public bool TryGetBrushGrip(out Vector3 pos, out Vector3 dir)
    {
        pos = Vector3.zero; dir = Vector3.up;
        if (model == null || mHandR == null || mHandR.t == null || brushSmooth < 0.05f) return false;
        Transform h = mHandR.t;
        pos = midFingerR != null ? h.position + (midFingerR.position - h.position) * 0.55f : h.position + body.forward * 0.05f;
        dir = (body.forward * Mathf.Lerp(0.55f, 0.9f, paintSmooth) + Vector3.up * Mathf.Lerp(0.85f, 0.45f, paintSmooth)).normalized;
        return true;
    }

    // ---------------- Tutma: kollar eşyanın iki yanına uzanır (IK), parmaklar kenarı kavrar ----------------
    private HeldItemView heldView;
    private readonly List<Transform> fingersL = new List<Transform>(), fingersR = new List<Transform>();
    private Transform midFingerL, midFingerR, thumbL, thumbR;
    private readonly Dictionary<Transform, Quaternion> fingerRest = new Dictionary<Transform, Quaternion>();

    private void CollectFingers(Dictionary<string, Transform> bones, string side, List<Transform> list, out Transform mid, out Transform thumb)
    {
        list.Clear();
        foreach (string f in new[] { "Index", "Middle", "Ring", "Little", "Thumb" })
            foreach (string seg in new[] { "_Proximal", "_Intermediate", "_Distal" })
                if (bones.TryGetValue(f + seg + side, out Transform t) && t != null) { list.Add(t); fingerRest[t] = t.localRotation; }
        bones.TryGetValue("Middle_Proximal" + side, out mid);
        bones.TryGetValue("Thumb_Proximal" + side, out thumb);
    }

    /// <summary>Elde eşya varken: eller eşyanın sol / sağ kenarına uzanır (iki kemikli IK), avuç içe, parmaklar kenarı sarar.</summary>
    private void ApplyHoldIK(float weight)
    {
        if (heldView == null) heldView = GetComponent<HeldItemView>();
        foreach (KeyValuePair<Transform, Quaternion> kv in fingerRest) if (kv.Key != null) kv.Key.localRotation = kv.Value;
        if (weight <= 0.01f || heldView == null || !heldView.IsShowing) return;
        if (!TryGetHoldPoint(out Vector3 center, out _)) return;
        Vector3 right = body.right;
        float half = Mathf.Min(heldView.HalfWidth + 0.015f, armReach * 0.8f);
        ArmIK(mUpArmL, mLoArmL, mHandL, center - right * half, -1f, weight);
        ArmIK(mUpArmR, mLoArmR, mHandR, center + right * half, 1f, weight);
        OrientHand(mHandL, midFingerL, thumbL, center, -1f, weight);
        OrientHand(mHandR, midFingerR, thumbR, center, 1f, weight);
        CurlFingers(fingersL, mHandL, center, weight);
        CurlFingers(fingersR, mHandR, center, weight);
    }

    private void ArmIK(Bone upper, Bone lower, Bone hand, Vector3 target, float side, float w)
    {
        if (upper == null || lower == null || hand == null) return;
        Vector3 S = upper.t.position, E = lower.t.position, W = hand.t.position;
        float a = Vector3.Distance(S, E), b = Vector3.Distance(E, W);
        if (a < 1e-4f || b < 1e-4f) return;
        Vector3 toT = target - S;
        float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
        Vector3 dir = toT.normalized;
        // Dirsek aşağı ve hafif dışa baksın
        Vector3 pole = Vector3.down + body.right * (0.6f * side) - body.forward * 0.2f;
        Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.down;
        bend.Normalize();
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
        Vector3 elbow = S + dir * (a * cosA) + bend * (a * Mathf.Sqrt(1f - cosA * cosA));
        Vector3 wrist = S + dir * d;

        Quaternion upFrom = upper.t.rotation;
        Quaternion upTo = Quaternion.FromToRotation(E - S, elbow - S) * upFrom;
        upper.t.rotation = Quaternion.Slerp(upFrom, upTo, w);
        Vector3 E2 = lower.t.position, W2 = hand.t.position;
        Quaternion loFrom = lower.t.rotation;
        Quaternion loTo = Quaternion.FromToRotation(W2 - E2, wrist - E2) * loFrom;
        lower.t.rotation = Quaternion.Slerp(loFrom, loTo, w);
    }

    /// <summary>Parmaklar öne-içe uzansın, başparmak yukarı baksın (avuç eşyanın kenarına döner).</summary>
    private void OrientHand(Bone hand, Transform mid, Transform thumb, Vector3 center, float side, float w)
    {
        if (hand == null || mid == null) return;
        Vector3 inward = -body.right * side;
        Vector3 wantFinger = (body.forward * 0.85f + inward * 0.35f + Vector3.down * 0.1f).normalized;
        Vector3 curFinger = (mid.position - hand.t.position).normalized;
        Quaternion from = hand.t.rotation;
        Quaternion r1 = Quaternion.FromToRotation(curFinger, wantFinger) * from;
        hand.t.rotation = r1;
        if (thumb != null)
        {
            // Parmak ekseni etrafında çevir: başparmak yukarı
            Vector3 axis = (mid.position - hand.t.position).normalized;
            Vector3 curThumb = Vector3.ProjectOnPlane(thumb.position - hand.t.position, axis);
            Vector3 wantThumb = Vector3.ProjectOnPlane(Vector3.up, axis);
            if (curThumb.sqrMagnitude > 1e-6f && wantThumb.sqrMagnitude > 1e-6f)
                hand.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(curThumb, wantThumb, axis), axis) * hand.t.rotation;
        }
        hand.t.rotation = Quaternion.Slerp(from, hand.t.rotation, w);
    }

    /// <summary>Parmakları eşyanın ortasına doğru kıvır (kenarı sarar). Kemik eksenlerinden bağımsız: her eklem kendi
    /// yönünü merkeze doğru döndürür.</summary>
    private static void CurlFingers(List<Transform> list, Bone hand, Vector3 center, float w)
    {
        if (hand == null) return;
        foreach (Transform t in list)
        {
            if (t == null || t.childCount == 0) continue;
            Vector3 dir = (t.GetChild(0).position - t.position).normalized;
            Vector3 toC = (center - t.position).normalized;
            Vector3 axis = Vector3.Cross(dir, toC);
            if (axis.sqrMagnitude < 1e-6f) continue;
            bool isThumb = t.name.StartsWith("Thumb");
            float ang = Mathf.Min(Vector3.Angle(dir, toC), isThumb ? 25f : 45f) * w;
            t.rotation = Quaternion.AngleAxis(ang, axis.normalized) * t.rotation;
        }
    }

    private void TryBuildModel()
    {
        GameObject prefab = null;
        try { prefab = Resources.Load<GameObject>(ModelResource); }
        catch (System.Exception) { prefab = null; }
        if (prefab == null) return; // model yoksa eski gövde

        GameObject go = Instantiate(prefab, root, false);
        go.name = "CharacterModel";
        model = go.transform;
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one;
        foreach (Animator a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false; // kemikleri kod yönetir
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);       // ışınlara / taşımaya takılmasın

        modelSkin = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            if (!bones.ContainsKey(t.name)) bones[t.name] = t;
        System.Func<string, Transform> B = n => bones.TryGetValue(n, out Transform bt) ? bt : null;

        // 1) Yön: ayak parmakları ayağın önündedir → karakterin önünü oyuncunun önüne (+Z) çevir
        Transform footA = B("Foot.L"), toesA = B("Toes.L");
        if (footA != null && toesA != null)
        {
            Vector3 f = root.InverseTransformDirection(toesA.position - footA.position);
            f.y = 0f;
            if (f.sqrMagnitude > 1e-6f) model.localRotation = Quaternion.FromToRotation(f.normalized, Vector3.forward);
        }

        // 2) Boy: dinlenme pozundaki mesh sınırlarından ölç, oyuncu boyuna ölçekle, ayakları yere (kök y=0) oturt
        if (MeasureModel(out float minY, out float maxY) && maxY - minY > 0.01f)
        {
            float s = ModelHeight / (maxY - minY);
            model.localScale = Vector3.one * s;
            if (MeasureModel(out minY, out maxY)) model.localPosition = new Vector3(0f, -minY, 0f);
        }

        // 3) Kemikler (sol / sağ: kökün uzayında -X tarafı "sol" — eski gövdeyle aynı kural)
        Transform ul = B("UpperArm.L"), ur = B("UpperArm.R");
        bool swapArms = ul != null && ur != null && root.InverseTransformPoint(ul.position).x > root.InverseTransformPoint(ur.position).x;
        string L = swapArms ? ".R" : ".L", R = swapArms ? ".L" : ".R";
        mHips = MakeBone(B("Hips")); mSpine = MakeBone(B("Spine")); mChest = MakeBone(B("Chest"));
        mNeck = MakeBone(B("Neck")); mHead = MakeBone(B("Head"));
        mUpArmL = MakeBone(B("UpperArm" + L)); mLoArmL = MakeBone(B("LowerArm" + L)); mHandL = MakeBone(B("Hand" + L));
        mUpArmR = MakeBone(B("UpperArm" + R)); mLoArmR = MakeBone(B("LowerArm" + R)); mHandR = MakeBone(B("Hand" + R));
        CollectFingers(bones, L, fingersL, out midFingerL, out thumbL);
        CollectFingers(bones, R, fingersR, out midFingerR, out thumbR);
        mUpLegL = MakeBone(B("UpperLeg" + L)); mLoLegL = MakeBone(B("LowerLeg" + L)); mFootL = MakeBone(B("Foot" + L));
        mUpLegR = MakeBone(B("UpperLeg" + R)); mLoLegR = MakeBone(B("LowerLeg" + R)); mFootR = MakeBone(B("Foot" + R));

        // Kollar dinlenmede A-pozunda (yana açık): önce dümdüz aşağı sarkacak düzeltmeyi bul, salınım bunun üstüne gelir
        if (mUpArmL != null && mLoArmL != null && mHandL != null)
            armReach = Vector3.Distance(mUpArmL.t.position, mLoArmL.t.position) + Vector3.Distance(mLoArmL.t.position, mHandL.t.position);
        armFixL = ArmDownFix(mUpArmL, mLoArmL);
        armFixR = ArmDownFix(mUpArmR, mLoArmR);
        if (mUpLegL != null && mLoLegL != null && mFootL != null)
        {
            thighLen = Vector3.Distance(mUpLegL.t.position, mLoLegL.t.position);
            shinLen = Vector3.Distance(mLoLegL.t.position, mFootL.t.position);
        }

        // 4) Yüz ifadeleri (blend shape): göz kırpma, ağız açma
        if (modelSkin != null && modelSkin.sharedMesh != null)
        {
            blinkShape = modelSkin.sharedMesh.GetBlendShapeIndex("Blink");
            mouthShape = modelSkin.sharedMesh.GetBlendShapeIndex("MouthOpen");
        }
        nextBlink = Time.time + Random.Range(2f, 5f);

        // 5) Eski kodla kurulan gövdenin GÖRÜNTÜSÜNÜ kaldır (eklem objeleri kalır: hareketleri modele aktarılır).
        // Baygınlık yıldızları kalır. Renderer'lar silinir (PlayerStealth saklanınca / çıkınca tekrar açmasın).
        for (int i = renderers.Count - 1; i >= 0; i--)
        {
            Renderer r = renderers[i];
            if (r == null) { renderers.RemoveAt(i); continue; }
            if (stars != null && r.transform.IsChildOf(stars)) continue;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            Destroy(r);
            if (mf != null) Destroy(mf);
            renderers.RemoveAt(i);
        }
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            renderers.Add(r);
            r.shadowCastingMode = ShadowCastingMode.On;
        }
        if (modelSkin != null) modelSkin.updateWhenOffscreen = false;

        // Oyuncu rengi: berenin malzemesi (sadece bu karakterin kopyası)
        if (modelSkin != null)
        {
            Material[] mats = modelSkin.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (mats[i].name.StartsWith("MAT_Overalls_Yellow"))
                {
                    modelOverallMat = new Material(mats[i]); // tulum da oyuncu renginde
                    mats[i] = modelOverallMat;
                    continue;
                }
                if (!mats[i].name.StartsWith("MAT_Beret") || mats[i].name.StartsWith("MAT_Beret_Band")) continue;
                modelBeretMat = new Material(mats[i]);
                mats[i] = modelBeretMat;
            }
            modelSkin.sharedMaterials = mats;
        }
        if (hasPendingColor) TintModel(pendingColor);
    }

    /// <summary>Mesh'in dinlenme pozundaki sınırları, kökün (ayak hizası) uzayında.</summary>
    private bool MeasureModel(out float minY, out float maxY)
    {
        minY = float.MaxValue; maxY = float.MinValue;
        if (model == null) return false;
        bool any = false;
        foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            Bounds b = smr.sharedMesh.bounds;
            Transform mt = smr.transform;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float y = root.InverseTransformPoint(mt.TransformPoint(c)).y;
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                any = true;
            }
        }
        return any;
    }

    private Bone MakeBone(Transform t)
    {
        if (t == null) return null;
        return new Bone
        {
            t = t,
            restLocal = t.localRotation,
            restRel = Quaternion.Inverse(root.rotation) * t.rotation,
            restLocalPos = t.localPosition,
        };
    }

    private Quaternion ArmDownFix(Bone upper, Bone lower)
    {
        if (upper == null || lower == null) return Quaternion.identity;
        Vector3 cur = root.InverseTransformDirection(lower.t.position - upper.t.position);
        if (cur.sqrMagnitude < 1e-8f) return Quaternion.identity;
        return Quaternion.FromToRotation(cur.normalized, Vector3.down);
    }

    /// <summary>Kemiği, KÖKÜN uzayında verilen dönüş kadar (dinlenme pozuna göre) çevirir; çocuk kemikler onu izler.</summary>
    private static void Pose(Bone b, Quaternion rootSpace)
    {
        if (b == null || b.t == null) return;
        b.t.localRotation = b.restLocal * (Quaternion.Inverse(b.restRel) * rootSpace * b.restRel);
    }

    /// <summary>PlayerAvatar.Update sonunda: eski gövdenin eklem dönüşlerini ve durumları modele uygular.</summary>
    private void AnimateModel(float dt)
    {
        if (model == null) return;

        float knock = Mathf.Clamp01(knockSmooth);
        float notKnock = 1f - knock;
        float move = Mathf.Clamp01(speedSmooth / 4.5f);
        runSmooth = Mathf.Lerp(runSmooth, Mathf.Clamp01((speedSmooth - 4.8f) / 2f), 1f - Mathf.Exp(-8f * dt)); // Shift ile koşu
        airSmooth = Mathf.Lerp(airSmooth, wasGroundedGuess ? 0f : 1f, 1f - Mathf.Exp(-12f * dt));      // havada
        float run = runSmooth * notKnock, air = airSmooth * notKnock;

        // Gövde: eski gövdenin eğilmesi / sallanması (öne eğilme, eğilince daha çok)
        Quaternion lean = body.localRotation;
        float breathe = Mathf.Sin(Time.time * 2.1f) * (1f - move) * notKnock; // durunca nefes
        // Yana yürüme: kalça gidilen yöne döner (bacaklar o yöne adım atar), omurga ters dönerek göğsü öne çevirir
        float hipTurn = strafeSmooth * notKnock;
        Pose(mHips, Quaternion.Euler(0f, hipTurn, 0f));
        Pose(mSpine, Quaternion.Euler(0f, -hipTurn * 0.7f, 0f) * Quaternion.Slerp(Quaternion.identity, lean, 0.55f) * Quaternion.Euler(breathe * 1.5f + run * 8f, 0f, 0f));
        Pose(mChest, Quaternion.Euler(0f, -hipTurn * 0.3f, 0f) * Quaternion.Slerp(Quaternion.identity, lean, 0.45f) * Quaternion.Euler(-breathe * 2.5f, Mathf.Sin(phase) * 6f * move * notKnock, 0f));
        Pose(mNeck, Quaternion.identity);
        Pose(mHead, head.localRotation);

        // Kalça: yürürken zıplama + eğilince iner (ayaklar yerde kalsın diye bacaklar aşağıda bükülür)
        float drop = crouchSmooth * (thighLen + shinLen) * 0.38f * notKnock;
        float bob = (body.localPosition.y + crouchSmooth * 0.32f) * notKnock; // eski gövdenin zıplaması (eğilme payı hariç)
        if (mHips != null && mHips.t.parent != null)
        {
            Vector3 offRoot = new Vector3(0f, bob - drop, 0f);
            mHips.t.localPosition = mHips.restLocalPos + mHips.t.parent.InverseTransformVector(root.TransformVector(offRoot));
        }

        // Bacaklar: eski gövdenin bacak salınımı (baygınlıkta da) + eğilme için diz bükme (basit IK) + yürürken diz
        float crouchThigh = 0f, crouchKnee = 0f;
        if (drop > 0.001f)
        {
            float a = thighLen, b = shinLen, d = Mathf.Max(0.05f, a + b - drop);
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float alpha = Mathf.Acos(cosA) * Mathf.Rad2Deg;                      // uyluk öne
            float gamma = Mathf.Asin(Mathf.Clamp(a * Mathf.Sin(alpha * Mathf.Deg2Rad) / b, -1f, 1f)) * Mathf.Rad2Deg;
            crouchThigh = alpha;
            crouchKnee = alpha + gamma;                                          // baldır geriye
        }
        PoseLeg(mUpLegL, mLoLegL, mFootL, LegSwing(legL.localRotation, knock, run, air, 1f), crouchThigh + air * 10f, crouchKnee + air * 55f, notKnock);
        PoseLeg(mUpLegR, mLoLegR, mFootR, LegSwing(legR.localRotation, knock, run, air, -1f), crouchThigh + air * 25f, crouchKnee + air * 35f, notKnock);

        // Kollar: eski gövdenin kol dönüşleri (salınım, tutma, baygınlıkta savrulma) — önce dümdüz aşağı sarkıt
        Quaternion idleL = Quaternion.Euler(breathe * 1.2f, 0f, -breathe * 1.5f);
        Quaternion idleR = Quaternion.Euler(breathe * 1.2f, 0f, breathe * 1.5f);
        float freeArms = (1f - holdSmooth);
        Quaternion airL = Quaternion.Euler(-25f * air * freeArms, 0f, -35f * air * freeArms);
        Quaternion airR = Quaternion.Euler(-25f * air * freeArms, 0f, 35f * air * freeArms);
        Pose(mUpArmL, airL * armL.localRotation * idleL * armFixL);
        brushSmooth = Mathf.Lerp(brushSmooth, brushHold ? 1f : 0f, 1f - Mathf.Exp(-10f * dt));
        float brushArm = brushSmooth * notKnock;
        // Fırça: sağ kol öne kalkar; boyarken (duvar / lens) fırçayla sürme hareketi yapar
        bool paintingNow = netSetup != null && netSetup.IsSpawned && netSetup.IsOwner ? SprayPaint.IsSpraying
                         : stealth != null && stealth.IsSpawned && stealth.IsPaintingAnything;
        paintSmooth = Mathf.Lerp(paintSmooth, paintingNow && brushHold ? 1f : 0f, 1f - Mathf.Exp(-10f * dt));
        float pt = Time.time;
        Quaternion brushR = Quaternion.Euler(-60f * brushArm - 18f * paintSmooth + Mathf.Sin(pt * 9f) * 12f * paintSmooth,
                                             -12f * brushArm + Mathf.Cos(pt * 7f) * 14f * paintSmooth, 0f);
        Pose(mUpArmR, brushR * airR * armR.localRotation * idleR * armFixR);
        // Dirsek: hafif bükük; yürürken / koşarken biraz daha, tutarken eller önde birleşsin diye çok bükük
        float elbow = (12f + move * 22f + run * 45f) * freeArms + holdSmooth * 48f;
        Pose(mLoArmL, Quaternion.Euler(-elbow * notKnock, 0f, 0f));
        Pose(mLoArmR, Quaternion.Euler(-Mathf.Lerp(elbow, 40f, brushArm) * notKnock, 0f, 0f));
        // Eller: tutarken avuçlar içe döner (eşyayı kavrar)
        Pose(mHandL, Quaternion.Euler(0f, holdSmooth * 25f * notKnock, 0f));
        Pose(mHandR, Quaternion.Euler(0f, -holdSmooth * 25f * notKnock, 0f));

        // Tutma: eller eşyanın iki kenarını kavrar (IK + parmaklar)
        ApplyHoldIK(holdSmooth * notKnock);
        // Fırça elde: avuç fırçanın sapına döner, parmaklar sapı sarar
        if (brushSmooth > 0.05f && notKnock > 0.5f && mHandR != null && midFingerR != null)
        {
            float w = brushSmooth * notKnock;
            OrientHand(mHandR, midFingerR, thumbR, Vector3.zero, 1f, w);
            Vector3 palm = mHandR.t.position + (midFingerR.position - mHandR.t.position) * 0.5f - body.right * (0.05f * model.lossyScale.y);
            CurlFingers(fingersR, mHandR, palm, w * 1.4f);
        }

        // Yüz: ara sıra göz kırpar; baygınken gözler kapalı, ağız açık
        if (modelSkin != null)
        {
            float blink = 0f;
            if (Time.time >= nextBlink) { blinkT = 0.16f; nextBlink = Time.time + Random.Range(2.5f, 6f); }
            if (blinkT > 0f) { blinkT -= dt; blink = Mathf.Sin(Mathf.Clamp01(1f - blinkT / 0.16f) * Mathf.PI); }
            if (blinkShape >= 0) modelSkin.SetBlendShapeWeight(blinkShape, Mathf.Max(blink, knock) * 100f);
            if (mouthShape >= 0) modelSkin.SetBlendShapeWeight(mouthShape, knock * 70f);
        }
    }

    /// <summary>Bacak salınımı: normalde eski gövdenin açısı; koşarken adım büyür. Baygınken aynen kopyalanır.</summary>
    private static Quaternion LegSwing(Quaternion old, float knock, float run, float air, float side)
    {
        if (knock > 0.01f) return old;
        float p = old.eulerAngles.x;
        if (p > 180f) p -= 360f;
        float z = old.eulerAngles.z;
        if (z > 180f) z -= 360f; // yana adım (A / D)
        return Quaternion.Euler(p * (1f + 0.4f * run) * (1f - air * 0.7f), 0f, z * (1f - air));
    }

    private void PoseLeg(Bone thigh, Bone shin, Bone foot, Quaternion swing, float crouchThigh, float crouchKnee, float notKnock)
    {
        if (thigh == null) return;
        // Salınım açısı (eski gövde: -swing ileri). Arkadaki bacakta diz bükülür, öndeki bacak uzanır.
        float pitch = swing.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        float roll = swing.eulerAngles.z;
        if (roll > 180f) roll -= 360f;
        float walkKnee = Mathf.Max(0f, pitch) * 1.1f * notKnock + Mathf.Max(0f, -pitch) * 0.25f * notKnock
                       + Mathf.Abs(roll) * 0.9f * notKnock; // yana adımda bacak hafif kalkar
        Pose(thigh, swing * Quaternion.Euler(-crouchThigh, 0f, 0f));
        float knee = crouchKnee + walkKnee;
        Pose(shin, Quaternion.Euler(knee, 0f, 0f));
        // Ayak yere paralel kalsın
        // (uyluk + diz + ayak açıları toplamı ~0; yürürken biraz topuk-parmak hareketi kalsın diye salınımın %70'i)
        Pose(foot, Quaternion.Euler((crouchThigh - knee - pitch * 0.7f) * notKnock, 0f, 0f));
    }

    /// <summary>Oyuncu rengi → karakterin beresi.</summary>
    private void TintModel(Color c)
    {
        pendingColor = c;
        hasPendingColor = true;
        Color tint = Color.Lerp(c, Color.white, 0.15f);
        if (modelBeretMat != null)
        {
            if (modelBeretMat.HasProperty("_BaseColor")) modelBeretMat.SetColor("_BaseColor", tint);
            modelBeretMat.color = tint;
        }
        if (modelOverallMat != null)
        {
            Color o = Color.Lerp(c, Color.black, 0.08f); // tulum: biraz daha koyu ton
            if (modelOverallMat.HasProperty("_BaseColor")) modelOverallMat.SetColor("_BaseColor", o);
            modelOverallMat.color = o;
        }
    }

    private void DestroyModelMaterials()
    {
        if (modelBeretMat != null) Destroy(modelBeretMat);
        if (modelOverallMat != null) Destroy(modelOverallMat);
        if (previewCam != null) Destroy(previewCam.gameObject);
        if (previewOn) { PreviewActive = false; PlayerController.LookFrozen = false; }
    }

    /// <summary>F5: kendi karakterini arkadan gör (test için). Oyunun kamerasına dokunmaz; üstüne ayrı bir kamera çizer.</summary>
    private void LateUpdate()
    {
        if (model == null || netSetup == null || !netSetup.IsSpawned || !netSetup.IsOwner) return;
        UnityEngine.InputSystem.Keyboard kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f5Key.wasPressedThisFrame && !ChatUI.IsTyping) SetPreview(!previewOn);

        // Saklanırken (boyalı kapı / dolap) kendi gövden hiç çizilmesin: güneşte yere düşen gölgen de kalmasın
        // (diğer oyuncularda PlayerStealth zaten gizler; sahibinde gövde "sadece gölge" modundaydı)
        bool hiddenNow = stealth != null && stealth.IsSpawned && stealth.IsHidden;
        if (hiddenNow != ownerHiddenApplied)
        {
            ownerHiddenApplied = hiddenNow;
            foreach (Renderer r in renderers)
                if (r != null && (stars == null || !r.transform.IsChildOf(stars))) r.enabled = !hiddenNow;
        }

        if (!previewOn || previewCam == null) return;


        Camera main = Camera.main;
        if (main == null || main == previewCam) return;
        UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && kb != null && kb.leftAltKey.isPressed)
            previewDist = Mathf.Clamp(previewDist - mouse.scroll.ReadValue().y * 0.002f, 1.2f, 6f); // Alt + tekerlek: uzaklık

        // Orta tuş basılıyken: fare kamerayı karakterin etrafında döndürür (oyuncunun bakışı bu sırada donar)
        bool orbiting = mouse != null && mouse.middleButton.isPressed && Cursor.lockState == CursorLockMode.Locked;
        PlayerController.LookFrozen = orbiting;
        if (orbiting)
        {
            Vector2 d = mouse.delta.ReadValue();
            orbitYaw += d.x * 0.25f;
            orbitPitch = Mathf.Clamp(orbitPitch - d.y * 0.25f, -30f, 75f);
        }

        Vector3 pivot = transform.position + Vector3.up * 0.55f;
        Quaternion orbit = Quaternion.Euler(orbitPitch, transform.eulerAngles.y + orbitYaw, 0f);
        Vector3 want = pivot - orbit * Vector3.forward * previewDist;
        // Duvara girmesin
        Vector3 to = want - pivot;
        if (Physics.Raycast(pivot, to.normalized, out RaycastHit hit, to.magnitude, ~0, QueryTriggerInteraction.Ignore) &&
            !hit.collider.transform.IsChildOf(transform))
            want = pivot + to.normalized * Mathf.Max(0.4f, hit.distance - 0.2f);
        previewCam.transform.position = want;
        previewCam.transform.rotation = Quaternion.LookRotation(pivot - want, Vector3.up);
        previewCam.fieldOfView = main.fieldOfView;
    }

    private void SetPreview(bool on)
    {
        previewOn = on;
        PreviewActive = on;
        previewHiddenApplied = false;
        orbitYaw = 0f;
        orbitPitch = 12f;
        if (!on) PlayerController.LookFrozen = false;
        if (on && previewCam == null)
        {
            GameObject go = new GameObject("CharacterPreviewCamera (F5)");
            previewCam = go.AddComponent<Camera>();
            previewCam.nearClipPlane = 0.05f;
            previewCam.farClipPlane = 300f;
        }
        if (previewCam != null)
        {
            Camera main = Camera.main;
            previewCam.depth = (main != null ? main.depth : 0f) + 5f; // oyunun kamerasının üstüne çizilir
            previewCam.enabled = on;
        }
        // Kendi gövden: önizlemede görünsün, normalde sadece gölgesi
        foreach (Renderer r in renderers)
            if (r != null && (stars == null || !r.transform.IsChildOf(stars)))
                r.shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
        if (PlayerInventory.Local != null) PlayerInventory.Local.ShowLocalToast(on ? "Karakter önizleme: AÇIK (orta tuş + fare: etrafında dön, Alt+tekerlek: uzaklık, F5: kapat)" : "Karakter önizleme: KAPALI");
    }
}
