using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SOYGUN KARAKTERİ ANİMASYONLARI (Resources/Characters/SoygunChar.fbx, Legacy Animation).
/// Tasarımcının hazır animasyonları duruma göre oynar; ağda ek veri gitmez (hız, eğilme, elde eşya, bakış, baygınlık zaten senkron):
///   Idle / Walk / Run                     : durma, yürüme (geri yürürken ters oynar), Shift ile koşu — adım hızı gerçek hıza göre
///   Itme                                  : F ile itme (yalnızca üst gövde; yürürken bacaklar yürümeye devam eder) — herkes görür
///   Tasima_Tablo_Idle / Walk              : elde tablo (iki elle göğüs önünde)
///   Tasima_Buyuk_Idle / Walk              : elde büyük eşya (vazo, büst...) iki elle
///   Tasima_Kucuk_Idle / Walk              : elde küçük eşya (yüzük, küçük ganimet, alet) sağ elde
/// Animasyonun ÜSTÜNE (LateUpdate) eklenenler: eğilme (kalça iner, dizler bükülür), zıplama (bacaklar toplanır),
/// yana yürüme (kalça döner), fırça kolu, baygınlık (eski düşme pozu) ve KAFA / GÖZ BAKIŞI:
///   - Biri sesli konuşunca (12 m içinde) kafa hafifçe ona döner, gözler kalan açıyı kapatır (konuşma bitince 2 sn daha bakar)
///   - Yakında (6 m) biri varsa ona göz atar; kimse yoksa oyuncunun baktığı yöne (yukarı / aşağı) bakar
/// </summary>
/// <summary>
/// SOYGUN KARAKTERİ ANİMASYONLARI (Resources/Characters/SoygunChar.fbx).
/// Klipler LateUpdate'te elle örneklenir (AnimationClip.SampleAnimation) — sıra her zaman aynı: önce animasyon, sonra
/// üstüne katmanlar. Böylece fırça kolu, eşya tutan eller, eğilme, bakış animasyon tarafından ezilmez.
/// Ağda ek veri gitmez (hız, eğilme, elde eşya, bakış, baygınlık zaten senkron):
///   Idle / Walk / Run                     : durma, yürüme (geri yürürken ters oynar), Shift ile koşu — adım hızı gerçek hıza göre
///   Itme                                  : F ile itme (yalnızca üst gövde; yürürken bacaklar yürümeye devam eder) — herkes görür
///   Tasima_Tablo_Idle / Walk              : elde tablo — tablo göğüs önündeki taşıma noktasında, eller iki kenarını kavrar
///   Tasima_Buyuk_Idle / Walk              : elde büyük eşya (vazo, büst...) — eller iki yanından kavrar
///   Tasima_Kucuk_Idle / Walk              : elde küçük eşya (yüzük, küçük ganimet, alet) sağ avuçta
/// Animasyonun ÜSTÜNE: eğilme, zıplama, yana yürüme, fırça kolu, baygınlık (eski düşme pozu), ayakların yere basması
/// (zemine ışın: karakter havada durmaz) ve KAFA / GÖZ BAKIŞI:
///   - Biri sesli konuşunca (12 m içinde) kafa hafifçe ona döner, gözler kalan açıyı kapatır (konuşma bitince 2 sn daha bakar)
///   - Yakında (6 m) biri varsa ona göz atar; kimse yoksa oyuncunun baktığı yöne (yukarı / aşağı) bakar
/// </summary>
public partial class PlayerAvatar
{
    private bool animated;
    private Animation modelAnim;
    private GameObject animRoot;
    private readonly Dictionary<string, AnimationClip> clipMap = new Dictionary<string, AnimationClip>();
    private string cIdle, cWalk, cRun, cPush, cTabloIdle, cTabloWalk, cBigIdle, cBigWalk, cSmallIdle, cSmallWalk;
    private string curLoop, prevLoop;
    private float curT, prevT, curSpeed = 1f, prevSpeed = 1f, fade = 1f;
    private const float FadeTime = 0.2f;
    private float pushT = -1f, pushW;
    private bool movingBack; // PlayerAvatar.Update yazar (geri yürüme)
    private Transform gripR, eyeL, eyeR, upperNeck, carryPainting, carryBig;
    private Quaternion eyeLRest, eyeRRest;
    private Vector3 headFwdLocal = Vector3.forward;
    private int poseFrame = -1;
    private Vector3 modelBasePos;
    private float groundShift;

    // Elle örnekleme / karıştırma için iskelet
    private Transform[] skel;
    private bool[] skelUpper;
    private Vector3[] bufPos;
    private Quaternion[] bufRot;

    // Baygınlık / fırça: kodla animasyonun karıştırıldığı kemikler
    private readonly List<Transform> blendT = new List<Transform>();
    private readonly List<bool> blendRightArm = new List<bool>();
    private Quaternion[] blendCache;

    // Bakış
    private Transform lookTarget, speakTarget;
    private float speakUntil, nextLookScan, lookW;
    private Vector3 lookDir = Vector3.forward;

    private void SetupClips(Dictionary<string, Transform> bones)
    {
        animated = false;
        modelAnim = model != null ? model.GetComponentInChildren<Animation>(true) : null;
        if (modelAnim == null) return;
        clipMap.Clear();
        foreach (AnimationState s in modelAnim)
        {
            if (s.clip == null) continue;
            string n = s.name; int bar = n.LastIndexOf('|'); string k = bar >= 0 ? n.Substring(bar + 1) : n;
            clipMap[k] = s.clip;
        }
        string C(string k) => clipMap.ContainsKey(k) ? k : null;
        cIdle = C("Idle"); cWalk = C("Walk"); cRun = C("Run"); cPush = C("Itme");
        cTabloIdle = C("Tasima_Tablo_Idle"); cTabloWalk = C("Tasima_Tablo_Walk");
        cBigIdle = C("Tasima_Buyuk_Idle"); cBigWalk = C("Tasima_Buyuk_Walk");
        cSmallIdle = C("Tasima_Kucuk_Idle"); cSmallWalk = C("Tasima_Kucuk_Walk");
        // Klipleri biz örnekliyoruz: Animation bileşeni kendi kendine poz yazmasın
        modelAnim.Stop();
        modelAnim.enabled = false;
        if (cIdle == null || cWalk == null) return; // animasyonsuz model: eski kodla animasyon
        if (cRun == null) cRun = cWalk;
        animated = true;
        animRoot = modelAnim.gameObject;

        bones.TryGetValue("Grip.R", out gripR);
        bones.TryGetValue("Eye.L", out eyeL);
        bones.TryGetValue("Eye.R", out eyeR);
        bones.TryGetValue("UpperNeck", out upperNeck);
        bones.TryGetValue("CarryPainting", out carryPainting);
        bones.TryGetValue("CarryBig", out carryBig);
        if (eyeL != null) eyeLRest = eyeL.localRotation;
        if (eyeR != null) eyeRRest = eyeR.localRotation;
        if (mHead != null) headFwdLocal = Quaternion.Inverse(mHead.t.rotation) * root.forward;
        lookDir = root.forward;
        modelBasePos = model.localPosition;

        // İskelet: SkinnedMeshRenderer'ın kemikleri (+ taşıma noktaları); itme maskesi = göğüs ve altı (kollar, kafa)
        var list = new List<Transform>();
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (t != model && t != (modelSkin != null ? modelSkin.transform : null)) list.Add(t);
        skel = list.ToArray();
        skelUpper = new bool[skel.Length];
        for (int i = 0; i < skel.Length; i++) skelUpper[i] = mChest != null && skel[i].IsChildOf(mChest.t);
        bufPos = new Vector3[skel.Length];
        bufRot = new Quaternion[skel.Length];

        blendT.Clear(); blendRightArm.Clear();
        foreach (Bone b in new[] { mHips, mSpine, mChest, mNeck, mHead, mUpArmL, mLoArmL, mHandL, mUpLegL, mLoLegL, mFootL, mUpLegR, mLoLegR, mFootR })
            if (b != null && b.t != null) { blendT.Add(b.t); blendRightArm.Add(false); }
        foreach (Bone b in new[] { mUpArmR, mLoArmR, mHandR })
            if (b != null && b.t != null) { blendT.Add(b.t); blendRightArm.Add(true); }
        foreach (Transform t in fingersL) { blendT.Add(t); blendRightArm.Add(false); }
        foreach (Transform t in fingersR) { blendT.Add(t); blendRightArm.Add(true); }
        blendCache = new Quaternion[blendT.Count];

        curLoop = cIdle; curT = 0f; fade = 1f;
    }

    /// <summary>Elde ne var: 0 yok, 1 tablo, 2 büyük eşya, 3 küçük eşya.</summary>
    private int CarryKindNow()
    {
        if (inventory == null || !inventory.IsSpawned || !inventory.HasHeldItem || brushHold) return 0;
        if (heldView == null) heldView = GetComponent<HeldItemView>();
        return heldView != null ? heldView.CarryKind : 2;
    }

    private static bool IsWalkClip(string c) => c != null && (c.EndsWith("Walk") || c == "Run");

    /// <summary>Update: hangi döngü oynasın, ne hızda (PlayerAvatar.Update → AnimateModel). Zamanlar burada ilerler.</summary>
    private void UpdateClips(float dt)
    {
        bool knocked = knockSmooth > 0.5f;
        int kind = knocked ? 0 : CarryKindNow();
        bool moving = !knocked && speedSmooth > 0.35f;
        bool run = moving && kind == 0 && speedSmooth > 5.2f;
        string idle = kind == 1 ? cTabloIdle : kind == 2 ? cBigIdle : kind == 3 ? cSmallIdle : cIdle;
        string walk = kind == 1 ? cTabloWalk : kind == 2 ? cBigWalk : kind == 3 ? cSmallWalk : cWalk;
        string want = run ? cRun : moving ? (walk ?? cWalk) : (idle ?? cIdle);

        float speed = 1f;
        if (run) speed = Mathf.Clamp(speedSmooth / 7f * 1.1f, 0.7f, 1.3f);
        else if (moving) speed = Mathf.Clamp(speedSmooth / 4.5f * 1.8f, 0.6f, 2f) * (movingBack ? -1f : 1f);

        if (want != curLoop)
        {
            // Yürüyüşten yürüyüşe geçerken adım fazı korunur (ayak kayması olmasın)
            float phase = clipMap.TryGetValue(curLoop, out AnimationClip oc) && oc.length > 0f ? Mathf.Repeat(curT, oc.length) / oc.length : 0f;
            prevLoop = curLoop; prevT = curT; prevSpeed = curSpeed;
            curLoop = want;
            curT = IsWalkClip(prevLoop) && IsWalkClip(want) && clipMap.TryGetValue(want, out AnimationClip nc) ? phase * nc.length : 0f;
            fade = 0f;
        }
        curSpeed = speed;
        curT += dt * curSpeed;
        prevT += dt * prevSpeed;
        fade = Mathf.Min(1f, fade + dt / FadeTime);

        // İtme: hızlı girer, sonda yumuşak söner
        if (pushT >= 0f && cPush != null)
        {
            float len = clipMap[cPush].length;
            pushT += dt * 1.15f;
            if (pushT >= len) { pushT = -1f; pushW = 0f; }
            else pushW = Mathf.Min(Mathf.Clamp01(pushT / 0.08f), Mathf.Clamp01((len - pushT) / 0.2f));
        }
    }

    /// <summary>F ile itme animasyonu (PlayerStealth.PushAnimRpc → herkes görür).</summary>
    public void PlayPushAnim()
    {
        if (!animated || cPush == null) return;
        pushT = 0f;
        pushW = 0f;
    }

    private void SampleClip(string name, float t)
    {
        if (name == null || !clipMap.TryGetValue(name, out AnimationClip c) || c == null) return;
        float len = Mathf.Max(0.001f, c.length);
        c.SampleAnimation(animRoot, name == cPush ? Mathf.Clamp(t, 0f, len) : Mathf.Repeat(t, len));
    }

    private void CachePose()
    {
        for (int i = 0; i < skel.Length; i++) { bufPos[i] = skel[i].localPosition; bufRot[i] = skel[i].localRotation; }
    }

    /// <summary>Bu karenin pozunu bir kez kurar (LateUpdate; eli / eşyayı okuyan başka scriptler önce çağırırsa onlar kurar).</summary>
    private void PoseNow()
    {
        if (!animated || model == null || poseFrame == Time.frameCount) return;
        poseFrame = Time.frameCount;
        // 1) Döngü klibi (geçişte önceki klipten yumuşak karışım)
        if (fade < 1f && prevLoop != null && prevLoop != curLoop)
        {
            SampleClip(prevLoop, prevT);
            CachePose();
            SampleClip(curLoop, curT);
            float wPrev = 1f - Mathf.SmoothStep(0f, 1f, fade);
            for (int i = 0; i < skel.Length; i++)
            {
                skel[i].localPosition = Vector3.Lerp(skel[i].localPosition, bufPos[i], wPrev);
                skel[i].localRotation = Quaternion.Slerp(skel[i].localRotation, bufRot[i], wPrev);
            }
        }
        else SampleClip(curLoop, curT);
        // 2) İtme: yalnızca üst gövde (bacaklar döngüde kalır)
        if (pushW > 0.001f && pushT >= 0f)
        {
            CachePose();
            SampleClip(cPush, pushT);
            for (int i = 0; i < skel.Length; i++)
            {
                if (skelUpper[i])
                {
                    skel[i].localPosition = Vector3.Lerp(bufPos[i], skel[i].localPosition, pushW);
                    skel[i].localRotation = Quaternion.Slerp(bufRot[i], skel[i].localRotation, pushW);
                }
                else { skel[i].localPosition = bufPos[i]; skel[i].localRotation = bufRot[i]; }
            }
        }
        // 3) Katmanlar
        LateAnimate(Mathf.Max(Time.deltaTime, 0.0001f));
    }

    /// <summary>Örneklenmiş animasyonun üstüne baygınlık / fırça / eğilme / zıplama / yana yürüme / eşya tutma / bakış / zemin.</summary>
    private void LateAnimate(float dt)
    {
        float knock = Mathf.Clamp01(knockSmooth), nk = 1f - knock;
        float wBrush = brushSmooth * nk;

        // 1) Baygınlık (tüm gövde) ve fırça (sağ kol): kodla poz hesaplanır, animasyonla ağırlıklı karıştırılır
        if (knock > 0.005f || wBrush > 0.005f)
        {
            for (int i = 0; i < blendT.Count; i++) blendCache[i] = blendT[i].localRotation;
            Vector3 hipPos = mHips != null ? mHips.t.localPosition : Vector3.zero;
            AnimateProcedural(dt);
            for (int i = 0; i < blendT.Count; i++)
            {
                float w = blendRightArm[i] ? Mathf.Max(knock, wBrush) : knock;
                blendT[i].localRotation = Quaternion.Slerp(blendCache[i], blendT[i].localRotation, w);
            }
            if (mHips != null) mHips.t.localPosition = Vector3.Lerp(hipPos, mHips.t.localPosition, knock);
            // Fırça tutan bilek animasyondaki halinden çok bükülmesin (kolun ucunda boşluk / yırtık olmasın)
            int hi = mHandR != null ? blendT.IndexOf(mHandR.t) : -1;
            if (hi >= 0 && knock < 0.5f) ClampLocal(mHandR.t, blendCache[hi], 55f);
        }

        // Zemin: ayaklar yere bassın (CharacterController'ın deri payı karakteri havada gösterir)
        float want = 0f;
        if (knock < 0.01f && wasGroundedGuess &&
            Physics.Raycast(root.position + Vector3.up * 0.6f, Vector3.down, out RaycastHit gh, 1.0f, ~0, QueryTriggerInteraction.Ignore) &&
            !gh.collider.transform.IsChildOf(transform))
            want = Mathf.Clamp(gh.point.y - root.position.y, -0.25f, 0.05f);
        groundShift = Mathf.Lerp(groundShift, want, 1f - Mathf.Exp(-12f * dt));
        model.localPosition = modelBasePos + Vector3.up * (groundShift / Mathf.Max(0.0001f, root.lossyScale.y));
        if (nk < 0.01f) return;

        // 2) Yana yürüme: kalça gidilen yöne, göğüs öne
        Vector3 up = root.up;
        float hipTurn = strafeSmooth * nk;
        if (mHips != null) mHips.t.rotation = Quaternion.AngleAxis(hipTurn, up) * mHips.t.rotation;
        Vector3 legRight = Quaternion.AngleAxis(hipTurn, up) * root.right;

        // 3) Eğilme: kalça iner, dizler bükülür (ayaklar yerde), gövde öne eğilir
        float drop = crouchSmooth * (thighLen + shinLen) * 0.38f * nk;
        float cThigh = 0f, cKnee = 0f;
        if (drop > 0.001f)
        {
            float a = thighLen, b = shinLen, d = Mathf.Max(0.05f, a + b - drop);
            float alpha = Mathf.Acos(Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f)) * Mathf.Rad2Deg;
            float gamma = Mathf.Asin(Mathf.Clamp(a * Mathf.Sin(alpha * Mathf.Deg2Rad) / b, -1f, 1f)) * Mathf.Rad2Deg;
            cThigh = alpha; cKnee = alpha + gamma;
            if (mHips != null) mHips.t.position -= up * drop;
        }
        if (mSpine != null) mSpine.t.rotation = Quaternion.AngleAxis(-hipTurn * 0.7f, up) * Quaternion.AngleAxis(crouchSmooth * 14f * nk, root.right) * mSpine.t.rotation;
        if (mChest != null) mChest.t.rotation = Quaternion.AngleAxis(-hipTurn * 0.3f, up) * mChest.t.rotation;

        // 4) Zıplama: havadayken dizler toplanır
        float air = airSmooth * nk;
        LegLayer(mUpLegL, mLoLegL, mFootL, legRight, cThigh + air * 10f, cKnee + air * 55f, cThigh - cKnee);
        LegLayer(mUpLegR, mLoLegR, mFootR, legRight, cThigh + air * 25f, cKnee + air * 35f, cThigh - cKnee);

        // 5) Tablo / büyük eşya: eller eşyanın iki kenarını kavrar (taşıma animasyonunun üstüne IK + parmaklar)
        int kind = CarryKindNow();
        if ((kind == 1 || kind == 2) && holdSmooth > 0.01f) ApplyCarryIK(holdSmooth * nk, kind);

        // 6) Kafa / göz
        ApplyLook(dt, nk);
    }

    private static void ClampLocal(Transform t, Quaternion refLocal, float maxDeg)
    {
        float a = Quaternion.Angle(refLocal, t.localRotation);
        if (a > maxDeg) t.localRotation = Quaternion.Slerp(refLocal, t.localRotation, maxDeg / a);
    }

    /// <summary>Tablo / büyük eşya: bilekler eşyanın iki yan kenarına; tabloda parmaklar kenarı arkadan sarar, başparmak önde
    /// (önden bakınca gerçekten kavramış görünür); büyük eşyada avuçlar yanlardan sarar.</summary>
    private void ApplyCarryIK(float w, int kind)
    {
        if (heldView == null) heldView = GetComponent<HeldItemView>();
        if (heldView == null || !heldView.IsShowing || mHandL == null || mHandR == null) return;
        if (!TryGetHoldPoint(out Vector3 center, out _)) return;
        foreach (KeyValuePair<Transform, Quaternion> kv in fingerRest) if (kv.Key != null) kv.Key.localRotation = kv.Value;
        Vector3 right = body.right, fwd = body.forward;
        float hh = heldView.HalfHeight;
        float half = Mathf.Min(heldView.HalfWidth + 0.02f, armReach * 0.85f);
        Quaternion animL = mHandL.t.localRotation, animR = mHandR.t.localRotation;
        for (int k = 0; k < 2; k++)
        {
            float side = k == 0 ? -1f : 1f;
            Bone up = k == 0 ? mUpArmL : mUpArmR, lo = k == 0 ? mLoArmL : mLoArmR, hand = k == 0 ? mHandL : mHandR;
            Transform mid = k == 0 ? midFingerL : midFingerR, thumb = k == 0 ? thumbL : thumbR;
            Vector3 inward = -right * side;
            Vector3 wrist, wantFinger, wantThumb;
            if (kind == 1)
            {
                // Tablo: bilek kenarın biraz dışında ve önünde, alt yarıda; parmaklar içe-arkaya (tablonun arkasına) uzanır
                wrist = center + right * side * (half + 0.03f) + fwd * 0.03f - Vector3.up * (hh * 0.2f);
                wantFinger = (inward * 0.75f - fwd * 0.55f - Vector3.up * 0.1f).normalized;
                wantThumb = (fwd * 0.5f + Vector3.up * 0.85f).normalized;
            }
            else
            {
                wrist = center + right * side * half - Vector3.up * (hh * 0.1f);
                wantFinger = (fwd * 0.8f + inward * 0.4f - Vector3.up * 0.1f).normalized;
                wantThumb = Vector3.up;
            }
            ArmIK(up, lo, hand, wrist, side, w);
            if (mid != null)
            {
                Quaternion from = hand.t.rotation;
                hand.t.rotation = Quaternion.FromToRotation((mid.position - hand.t.position).normalized, wantFinger) * hand.t.rotation;
                if (thumb != null)
                {
                    Vector3 axis = (mid.position - hand.t.position).normalized;
                    Vector3 ct = Vector3.ProjectOnPlane(thumb.position - hand.t.position, axis), wt = Vector3.ProjectOnPlane(wantThumb, axis);
                    if (ct.sqrMagnitude > 1e-6f && wt.sqrMagnitude > 1e-6f)
                        hand.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(ct, wt, axis), axis) * hand.t.rotation;
                }
                hand.t.rotation = Quaternion.Slerp(from, hand.t.rotation, w);
            }
        }
        // Bilekler aşırı bükülmesin (kol ile el arasında boşluk / yırtık kalmasın)
        ClampLocal(mHandL.t, animL, 70f);
        ClampLocal(mHandR.t, animR, 70f);
        Vector3 grip = kind == 1 ? center - fwd * 0.05f : center; // tablo: parmaklar arkadaki kenara kıvrılır
        CurlFingers(fingersL, mHandL, grip, w);
        CurlFingers(fingersR, mHandR, grip, w);
    }

    private static void LegLayer(Bone thigh, Bone shin, Bone foot, Vector3 right, float thighFwd, float knee, float footFix)
    {
        if (thigh == null || shin == null) return;
        if (thighFwd > 0.01f) thigh.t.rotation = Quaternion.AngleAxis(-thighFwd, right) * thigh.t.rotation;
        if (knee > 0.01f) shin.t.rotation = Quaternion.AngleAxis(knee, right) * shin.t.rotation;
        if (foot != null && Mathf.Abs(footFix) > 0.01f) foot.t.rotation = Quaternion.AngleAxis(footFix, right) * foot.t.rotation;
    }

    /// <summary>Konuşan (sesli) oyuncu → yoksa yakındaki oyuncu → yoksa yok.</summary>
    private void ScanLookTarget()
    {
        nextLookScan = Time.time + 0.25f;
        VoiceChat vc = VoiceChat.Instance;
        Transform speaker = null, near = null;
        float bestS = 144f, bestN = 36f; // 12 m / 6 m
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p == null || p == netSetup || !p.IsSpawned) continue;
            if (p.TryGetComponent(out PlayerStealth ps) && ps.IsSpawned && (ps.IsHidden || ps.IsJailed)) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (vc != null && vc.IsSpeaking(p.OwnerClientId) && d < bestS) { bestS = d; speaker = p.transform; }
            if (d < bestN) { bestN = d; near = p.transform; }
        }
        if (speaker != null) { speakTarget = speaker; speakUntil = Time.time + 2f; }
        if (speakTarget != null && Time.time > speakUntil) speakTarget = null;
        lookTarget = speakTarget != null ? speakTarget : near;
    }

    private void ApplyLook(float dt, float nk)
    {
        if (mHead == null) return;
        Transform head = mHead.t;
        Vector3 up = root.up, fwd = root.forward;
        if (Time.time >= nextLookScan) ScanLookTarget();

        Vector3 headFwd = head.rotation * headFwdLocal;
        bool look = false;
        Vector3 dir = headFwd;
        if (lookTarget != null)
        {
            dir = (lookTarget.position + up * 1.4f - head.position).normalized;
            look = Vector3.Angle(fwd, dir) < 110f;
        }
        float k = 1f - Mathf.Exp(-6f * dt);
        lookW = Mathf.Lerp(lookW, look ? nk : 0f, k);
        if (look) lookDir = Vector3.Slerp(lookDir, dir, k).normalized;

        // Kendi bakışı (yukarı / aşağı): kimseye bakmıyorken kafa oyuncunun baktığı yöne eğilir (diğerleri nereye baktığını görür)
        float pitch = netSetup != null && netSetup.IsSpawned ? Mathf.Clamp(netSetup.LookPitch, -40f, 40f) : 0f;
        Vector3 right = Vector3.Cross(up, headFwd).normalized;
        if (right.sqrMagnitude < 0.5f) right = root.right;
        float selfPitch = pitch * 0.55f * (1f - lookW) * nk;
        if (Mathf.Abs(selfPitch) > 0.05f) head.rotation = Quaternion.AngleAxis(selfPitch, right) * head.rotation;
        if (lookW < 0.001f) return;

        // Kafa (biraz da boyun) hedefe hafifçe döner (tasarımcının SoygunBakis ayarları: %35, en çok 35°)
        headFwd = head.rotation * headFwdLocal;
        Quaternion full = Quaternion.FromToRotation(headFwd, lookDir);
        full.ToAngleAxis(out float ang, out Vector3 axis);
        if (ang > 180f) ang -= 360f;
        float headAng = Mathf.Clamp(ang * 0.35f, -35f, 35f) * lookW;
        if (upperNeck != null) upperNeck.rotation = Quaternion.AngleAxis(headAng * 0.35f, axis) * upperNeck.rotation;
        head.rotation = Quaternion.AngleAxis(headAng * (upperNeck != null ? 0.65f : 1f), axis) * head.rotation;

        // Gözler kalan açıyı kapatır
        headFwd = head.rotation * headFwdLocal;
        Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(headFwd, up)) * lookDir;
        float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -5.5f, 5.5f) * lookW;
        float pit = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -6f, 6f) * lookW;
        Vector3 side = Vector3.Cross(up, headFwd).normalized;
        Quaternion eyeRot = Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(-pit, side);
        if (eyeL != null && eyeL.parent != null) eyeL.rotation = eyeRot * (eyeL.parent.rotation * eyeLRest);
        if (eyeR != null && eyeR.parent != null) eyeR.rotation = eyeRot * (eyeR.parent.rotation * eyeRRest);
    }

    /// <summary>Soygun: eşyanın yeri — tasarımcının taşıma noktaları (CarryPainting / CarryBig / Grip.R).
    /// Yüksek eşya yüzü kapatmasın diye üst kenarı çene hizasında tutulur, kalın eşya göbeğe girmesin diye öne alınır.</summary>
    private bool TryGetCarryPoint(out Vector3 point, out float handGap)
    {
        point = Vector3.zero; handGap = 0f;
        if (model == null || mHandL == null || mHandR == null) return false;
        PoseNow();
        if (heldView == null) heldView = GetComponent<HeldItemView>();
        int kind = heldView != null ? heldView.CarryKind : 2;
        float hh = heldView != null ? heldView.HalfHeight : 0.15f;
        float hw = heldView != null ? heldView.HalfWidth : 0.15f;
        handGap = Vector3.Distance(mHandL.t.position, mHandR.t.position);
        if (kind == 3 && gripR != null) { point = gripR.position; return true; } // küçük eşya: sağ avuçta
        Transform anchor = kind == 1 ? carryPainting : carryBig;
        Vector3 fwd = transform.forward;
        point = anchor != null ? anchor.position : (mHandL.t.position + mHandR.t.position) * 0.5f;
        if (mNeck != null)
        {
            float top = mNeck.t.position.y - 0.02f;                 // çene hizası
            if (point.y + hh * 0.85f > top) point.y = top - hh * 0.85f;
        }
        if (mHips != null) point.y = Mathf.Max(point.y, mHips.t.position.y + 0.05f);
        point += fwd * (kind == 1 ? 0.03f : Mathf.Max(0f, hw - 0.1f) * 0.6f);
        return true;
    }

    /// <summary>Soygun malzemeleri (G_*): doku içe aktarmada bağlanmadıysa Resources/Characters/Textures'tan bağlanır (beyaz görünmesin).</summary>
    private static void EnsureSoygunTextures(Material[] mats)
    {
        if (mats == null) return;
        foreach (Material m in mats)
        {
            if (m == null || !m.name.StartsWith("G_")) continue;
            if (m.name.StartsWith("G_Kontur"))
            {
                // Dış çizgi (ters kabuk): simsiyah, parlamasız; arka yüzler çizilmez
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.02f, 0.015f, 0.015f));
                m.color = new Color(0.02f, 0.015f, 0.015f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 2f);
                continue;
            }
            bool urp = m.HasProperty("_BaseMap");
            if ((urp ? m.GetTexture("_BaseMap") : m.mainTexture) != null) continue;
            string n = m.name.StartsWith("G_Bere") ? "G_Sac_Sapka" : m.name.Split(' ')[0];
            Texture2D tex = null;
            try { tex = Resources.Load<Texture2D>("Characters/Textures/" + n + "-color"); } catch (System.Exception) { tex = null; }
            if (tex == null) continue;
            if (urp) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            if (m.HasProperty("_BaseColor") && !m.name.StartsWith("G_Bere")) m.SetColor("_BaseColor", Color.white);
        }
    }
}
