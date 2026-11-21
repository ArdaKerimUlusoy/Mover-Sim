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

    /// <summary>Soygun modelinin göz yüksekliği (ayak hizasından, m); kamera bu hizaya iner (PlayerController.EyeOffset). -1 = bilinmiyor.</summary>
    public static float ModelEyeY = -1f;

    // Bakış
    private Transform lookTarget, speakTarget;
    private float speakUntil, nextLookScan, lookW, bodyW;
    private float bodyYaw;

    // Baygınken yere oturtma (pişirilmiş mesh'in en alt noktası zemine)
    private Mesh knockBake;
    private float knockFit, knockFitTarget, nextKnockBake;
    // Bakış eğimi (aşağı +): eldeki eşya / fırça / küçük eşya kollarla birlikte bakışı izler (birinci şahısta görünür kalır,
    // diğer oyuncular da kolların aşağı-yukarı indiğini görür — iki görüntü aynı)
    private float armPitch, carryTilt;
    /// <summary>Tablo / büyük eşyanın bakışla öne-arkaya eğimi (derece, aşağı +). HeldItemView eşyayı bu kadar eğer.</summary>
    public float CarryTilt => carryTilt;
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
        bones.TryGetValue("BrushGrip", out brushGrip);
        bones.TryGetValue("BrushTip", out brushTip);
        SetupBrushPose(bones);
        SetupEmotePose(bones);
        SetupHandPoses();
        if (eyeL != null) eyeLRest = eyeL.localRotation;
        if (eyeR != null) eyeRRest = eyeR.localRotation;
        if (eyeL != null && eyeR != null)
        {
            // Bağlama pozunda gözlerin ayak hizasından yüksekliği (zemin payı ~5 cm düşülür: model yere oturtulur)
            float ey = root.InverseTransformPoint((eyeL.position + eyeR.position) * 0.5f).y * root.lossyScale.y;
            if (ey > 0.8f && ey < 2.2f) ModelEyeY = ey - 0.05f;
        }
        if (mHead != null) headFwdLocal = Quaternion.Inverse(mHead.t.rotation) * root.forward;
        lookDir = root.forward;
        modelBasePos = model.localPosition;

        // İskelet: SkinnedMeshRenderer'ın kemikleri (+ taşıma noktaları); itme maskesi = göğüs ve altı (kollar, kafa)
        var list = new List<Transform>();
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (t != model && t != (modelSkin != null ? modelSkin.transform : null) && t != (headSkin != null ? headSkin.transform : null)) list.Add(t);
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

    // Fırça (üçüncü şahıs): Blender'da tasarlanmış "Firca" pozu — sağ kol öne uzanır, fırça kalem gibi başparmak ile işaret
    // parmağı arasında; fırçanın yeri BrushGrip (parmak uçlarının buluştuğu nokta) ve BrushTip kemiklerinden
    private Transform brushGrip, brushTip;
    private Transform[] brushBones;
    private Quaternion[] brushRot;

    /// <summary>Fırça kökü doğrudan BrushGrip noktası mı (Blender pozu)? Değilse eski yumruk tutuşu (PaintBrush kaydırır).</summary>
    public bool BrushGripIsRoot => animated && brushBones != null && brushGrip != null && brushTip != null;

    private void SetupBrushPose(Dictionary<string, Transform> bones)
    {
        brushBones = null;
        if (!clipMap.TryGetValue("Firca", out AnimationClip fc) || fc == null) return;
        var list = new List<Transform>();
        foreach (string n in new[] { "UpperArm.R", "LowerArm.R", "Hand.R" }) if (bones.TryGetValue(n, out Transform t) && t != null) list.Add(t);
        foreach (string f in new[] { "Index", "Middle", "Ring", "Little", "Thumb" })
            foreach (string sg in new[] { "_Proximal", "_Intermediate", "_Distal" })
                if (bones.TryGetValue(f + sg + ".R", out Transform t) && t != null) list.Add(t);
        if (list.Count < 3) return;
        fc.SampleAnimation(animRoot, 0f);
        brushBones = list.ToArray();
        brushRot = new Quaternion[brushBones.Length];
        for (int i = 0; i < brushBones.Length; i++) brushRot[i] = brushBones[i].localRotation;
        if (clipMap.TryGetValue(cIdle, out AnimationClip ic) && ic != null) ic.SampleAnimation(animRoot, 0f);
    }

    // El pozları (Blender'da tasarlandı, yalnız parmaklar): IK eli yerleştirir, parmaklar bu pozu alır
    private Quaternion[] handTabloL, handTabloR, handKavraL, handKavraR, handAvucL, handAvucR, handKapaliL, handKapaliR, handCupR, handYaziR, handFiskeR, handCimdikR;
    private float gripY; // vazo / büst: ellerin kavradığı yükseklik (eşya çerçevesinde, m)
    private Vector3 carryLocal; private int carryFrame = -1, carryKindLast = -1; // eşyanın yumuşatılmış yeri (oyuncuya göre)

    private void SetupHandPoses()
    {
        handTabloL = handTabloR = handKavraL = handKavraR = handAvucL = handAvucR = handKapaliL = handKapaliR = handCupR = handYaziR = handFiskeR = handCimdikR = null;
        BuildChains(fingersL, chainsL);
        BuildChains(fingersR, chainsR);
        bool any = false;
        foreach (string n in new[] { "El_Tablo", "El_Kavra", "El_Avuc", "El_Kapali", "El_Yazitura", "El_Fiske", "El_Cimdik" })
        {
            if (!clipMap.TryGetValue(n, out AnimationClip c) || c == null) continue;
            c.SampleAnimation(animRoot, 0f);
            any = true;
            Quaternion[] l = Capture(fingersL), r = Capture(fingersR);
            if (n == "El_Tablo") { handTabloL = l; handTabloR = r; }
            else if (n == "El_Kavra") { handKavraL = l; handKavraR = r; }
            else if (n == "El_Kapali") { handKapaliL = l; handKapaliR = r; }
            else if (n == "El_Yazitura") handYaziR = r;
            else if (n == "El_Fiske") handFiskeR = r;
            else if (n == "El_Cimdik") handCimdikR = r;
            else { handAvucL = l; handAvucR = r; }
        }
        // Parmak menteşe eksenleri: "El_Kavra"da bütün boğumlar avuca doğru aynı yönde bükülür → her kemiğin bükülme ekseni
        // buradan okunur (FBX eksenlerinden bağımsız). Emote parmakları bu eksenlerde bükülür.
        fingerHinge.Clear();
        ReadHinges(fingersL, handKavraL);
        ReadHinges(fingersR, handKavraR);
        // Vazo / büst sarma: dört parmak "El_Kavra", başparmak "El_Kapali" (karşı taraftan bastırır)
        handWrapL = Mix(fingersL, handKavraL, handKapaliL);
        handWrapR = Mix(fingersR, handKavraR, handKapaliR);
        // El kamerası: kavrama ile yumruk arası
        if (handKavraL != null && handKapaliL != null && handKavraL.Length == handKapaliL.Length)
        {
            handCamL = new Quaternion[handKavraL.Length];
            for (int i = 0; i < handCamL.Length; i++) handCamL[i] = Quaternion.Slerp(handKavraL[i], handKapaliL[i], 0.3f);
        }
        else handCamL = null;
        handBagL = Blend(handAvucL, handKavraL, 0.35f);
        handBagR = Blend(handAvucR, handKavraR, 0.35f);
        // Küçük eşya için en kapalı hal: avuç ile kavrama arası (eşyayı örtmeden çukur avuç)
        if (handAvucR != null && handKavraR != null && handAvucR.Length == handKavraR.Length)
        {
            handCupR = new Quaternion[handAvucR.Length];
            for (int i = 0; i < handCupR.Length; i++) handCupR[i] = Quaternion.Slerp(handAvucR[i], handKavraR[i], 0.7f);
        }
        if (any && clipMap.TryGetValue(cIdle, out AnimationClip ic) && ic != null) ic.SampleAnimation(animRoot, 0f);
    }

    private Quaternion[] handWrapL, handWrapR;
    private readonly Dictionary<Transform, Vector3> fingerHinge = new Dictionary<Transform, Vector3>();

    private void ReadHinges(List<Transform> list, Quaternion[] kavra)
    {
        if (kavra == null || kavra.Length != list.Count) return;
        for (int i = 0; i < list.Count; i++)
        {
            Transform t = list[i];
            if (t == null || !fingerRest.TryGetValue(t, out Quaternion rest)) continue;
            Quaternion d = Quaternion.Inverse(rest) * kavra[i];
            d.ToAngleAxis(out float ang, out Vector3 ax);
            if (ang > 180f) { ang = 360f - ang; ax = -ax; }
            if (ang > 3f && ax.sqrMagnitude > 0.5f) fingerHinge[t] = ax.normalized;
        }
    }


    private static Quaternion[] Blend(Quaternion[] a, Quaternion[] b, float t)
    {
        if (a == null || b == null || a.Length != b.Length) return null;
        var r = new Quaternion[a.Length];
        for (int i = 0; i < r.Length; i++) r[i] = Quaternion.Slerp(a[i], b[i], t);
        return r;
    }

    private static Quaternion[] Mix(List<Transform> list, Quaternion[] fingers, Quaternion[] thumb)
    {
        if (fingers == null || thumb == null || fingers.Length != list.Count || thumb.Length != list.Count) return null;
        var r = new Quaternion[list.Count];
        for (int i = 0; i < r.Length; i++) r[i] = list[i] != null && list[i].name.StartsWith("Thumb") ? thumb[i] : fingers[i];
        return r;
    }

    private static Quaternion[] Capture(List<Transform> list)
    {
        var a = new Quaternion[list.Count];
        for (int i = 0; i < list.Count; i++) a[i] = list[i] != null ? list[i].localRotation : Quaternion.identity;
        return a;
    }

    /// <summary>Parmakları tasarlanmış poza getir (w ile karışım). Poz yoksa false (eski kıvırma kullanılır).</summary>
    private static bool HandPose(List<Transform> list, Quaternion[] pose, float w)
    {
        if (pose == null || pose.Length != list.Count) return false;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) list[i].localRotation = Quaternion.Slerp(list[i].localRotation, pose[i], w);
        return true;
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
        // 4) Baygınken gövde yere tam otursun (havada kalmasın)
        KnockGroundFit(Mathf.Max(Time.deltaTime, 0.0001f));
    }

    /// <summary>Baygın yatarken: ara ara (0,2 sn) mesh pişirilip en alt noktası bulunur, model dünya yukarısı boyunca
    /// zemine indirilir / kaldırılır. Yalnızca baygınlıkta çalışır (normalde hiç maliyeti yok).</summary>
    private void KnockGroundFit(float dt)
    {
        float knock = Mathf.Clamp01(knockSmooth);
        if (knock < 0.02f || modelSkin == null)
        {
            knockFit = Mathf.MoveTowards(knockFit, 0f, dt * 2f);
            knockFitTarget = 0f;
            if (Mathf.Abs(knockFit) > 0.0005f) model.position += Vector3.up * knockFit * knock;
            return;
        }
        if (Time.time >= nextKnockBake)
        {
            nextKnockBake = Time.time + 0.2f;
            if (knockBake == null) knockBake = new Mesh { name = "KnockBake" };
            // Önce bu karenin düzeltmesi uygulanmış haliyle ölç
            model.position += Vector3.up * knockFit * knock;
            try
            {
                float minY = float.MaxValue;
                for (int k = 0; k < 2; k++)
                {
                    SkinnedMeshRenderer sk = k == 0 ? modelSkin : headSkin; // kafa da yere değebilir (iri kafa)
                    if (sk == null) continue;
                    sk.BakeMesh(knockBake, true);
                    Vector3[] v = knockBake.vertices;
                    Matrix4x4 m = sk.transform.localToWorldMatrix;
                    for (int i = 0; i < v.Length; i += 3) { float y = m.MultiplyPoint3x4(v[i]).y; if (y < minY) minY = y; }
                }
                if (minY < float.MaxValue &&
                    Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit gh, 3f, ~0, QueryTriggerInteraction.Ignore) &&
                    !gh.collider.transform.IsChildOf(transform))
                    knockFitTarget = Mathf.Clamp(knockFit + (gh.point.y + 0.01f - minY) / Mathf.Max(0.05f, knock), -0.8f, 0.4f);
            }
            catch (System.Exception) { }
            model.position -= Vector3.up * knockFit * knock;
        }
        knockFit = Mathf.Lerp(knockFit, knockFitTarget, 1f - Mathf.Exp(-8f * dt));
        model.position += Vector3.up * knockFit * knock;
    }

    /// <summary>Kayıt ışığının yeri: sol göğüs (askının karşı tarafı), göğüs kemiğiyle birlikte hareket eder.</summary>
    public bool TryGetRecLightPoint(out Vector3 pos)
    {
        pos = Vector3.zero;
        if (!animated || model == null || mChest == null) return false;
        float s = transform.lossyScale.y;
        Vector3 f = transform.forward, r = transform.right, u = transform.up;
        pos = mChest.t.position + (f * 0.2f - r * 0.09f + u * 0.09f) * s;
        return true;
    }

    /// <summary>Örneklenmiş animasyonun üstüne baygınlık / fırça / eğilme / zıplama / yana yürüme / eşya tutma / bakış / zemin.</summary>
    private void LateAnimate(float dt)
    {
        float knock = Mathf.Clamp01(knockSmooth), nk = 1f - knock;
        float wBrush = brushSmooth * nk;
        armPitch = Mathf.Lerp(armPitch, LookPitchNow(), 1f - Mathf.Exp(-18f * dt));

        // 1) Baygınlık (tüm gövde) ve fırça (sağ kol): kodla poz hesaplanır, animasyonla ağırlıklı karıştırılır.
        //    Soygun'da fırça Blender pozuyla (aşağıda) yapılır; kodla poz yalnızca baygınlık için.
        bool authoredBrush = brushBones != null;
        float wProcBrush = authoredBrush ? 0f : wBrush;
        if (knock > 0.005f || wProcBrush > 0.005f)
        {
            for (int i = 0; i < blendT.Count; i++) blendCache[i] = blendT[i].localRotation;
            Vector3 hipPos = mHips != null ? mHips.t.localPosition : Vector3.zero;
            AnimateProcedural(dt);
            for (int i = 0; i < blendT.Count; i++)
            {
                float w = blendRightArm[i] ? Mathf.Max(knock, wProcBrush) : knock;
                blendT[i].localRotation = Quaternion.Slerp(blendCache[i], blendT[i].localRotation, w);
            }
            if (mHips != null) mHips.t.localPosition = Vector3.Lerp(hipPos, mHips.t.localPosition, knock);
            // Fırça tutan bilek animasyondaki halinden çok bükülmesin (kolun ucunda boşluk / yırtık olmasın)
            int hi = mHandR != null ? blendT.IndexOf(mHandR.t) : -1;
            if (hi >= 0 && knock < 0.5f && wProcBrush > 0.005f) ClampLocal(mHandR.t, blendCache[hi], 55f);
        }
        if (authoredBrush && wBrush > 0.005f)
        {
            for (int i = 0; i < brushBones.Length; i++)
                brushBones[i].localRotation = Quaternion.Slerp(brushBones[i].localRotation, brushRot[i], wBrush);
            // Boyarken: kol küçük daireler çizerek fırçayı sürer
            float pw = paintSmooth * wBrush;
            if (pw > 0.005f && brushBones[0] != null)
            {
                float tt = Time.time;
                brushBones[0].rotation = Quaternion.AngleAxis(Mathf.Sin(tt * 11f) * 7f * pw, root.up) *
                                         Quaternion.AngleAxis(Mathf.Cos(tt * 8.5f) * 6f * pw, root.right) * brushBones[0].rotation;
            }
        }

        FitBag();
        Vector3 bagRel = Vector3.zero;
        if (bagBone != null && mHips != null) bagRel = bagBone.position - mHips.t.position;

        // Zemin: ayaklar yere bassın (CharacterController'ın deri payı karakteri havada gösterir)
        float want = 0f;
        if (knock < 0.01f && wasGroundedGuess &&
            Physics.Raycast(root.position + Vector3.up * 0.6f, Vector3.down, out RaycastHit gh, 1.0f, ~0, QueryTriggerInteraction.Ignore) &&
            !gh.collider.transform.IsChildOf(transform))
            want = Mathf.Clamp(gh.point.y - root.position.y, -0.25f, 0.05f);
        groundShift = Mathf.Lerp(groundShift, want, 1f - Mathf.Exp(-12f * dt));
        // Birinci şahıs (yalnızca kendi ekranında): gövde biraz geride → kamera yakanın önünde kalır
        fpBack = Mathf.MoveTowards(fpBack, ownerBodyShown && !previewOn ? FpBodyBack : 0f, dt * 0.6f);
        model.localPosition = modelBasePos + Vector3.up * (groundShift / Mathf.Max(0.0001f, root.lossyScale.y))
                                           - Vector3.forward * (fpBack / Mathf.Max(0.0001f, root.lossyScale.z));
        if (nk < 0.01f) return;

        // 2) Yana yürüme: kalça gidilen yöne, göğüs öne
        Vector3 up = root.up;
        float hipTurn = strafeSmooth * nk;
        if (mHips != null) mHips.t.rotation = Quaternion.AngleAxis(hipTurn, up) * mHips.t.rotation;
        // Çanta kalçayla birlikte yana dönmesin: gövdenin yönünde kalsın (yoksa yana yürürken çanta öne / arkaya kayıyor,
        // onu tutan el de kolu ters bükerek peşinden gidiyordu). Askı omuzdan geçtiği için çanta gövdeyi izler.
        if (bagBone != null && mHips != null && Mathf.Abs(hipTurn) > 0.1f)
        {
            bagBone.position = mHips.t.position + bagRel;
            Quaternion undo = Quaternion.AngleAxis(-hipTurn, up);
            bagBone.rotation = undo * bagBone.rotation;
        }
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
        // 4a) Zıplama: kollar yanlara-yukarı kalkar (çıkarken biraz öne, düşerken denge için daha açık), inişte iner
        if (air > 0.01f) JumpArms(air, wBrush, CarryKindNow());

        // 4b) Bakış — gövde kısmı (omurga / göğüs hedefe doğru biraz döner). Kollar göğsün çocuğu olduğu için bu,
        //     el IK'sından ÖNCE yapılmalı: eskiden en sonda yapılıyordu → eller hesaplanan noktadan kayıyor, elde tutulan
        //     bozuk para / yüzük / tablo elden ayrı duruyordu (özellikle yanında arkadaşın varken başını çevirince).
        LookBody(dt, nk);

        // 5) Tablo / büyük eşya: eller eşyanın iki kenarını kavrar (taşıma animasyonunun üstüne IK + parmaklar)
        int kind = CarryKindNow();
        if ((kind == 1 || kind == 2) && holdSmooth > 0.01f) ApplyCarryIK(holdSmooth * nk, kind);

        // 5b) Küçük eşya: sağ el öne-yukarı uzanır, avuç yukarı bakar, eşya avucun içinde (göz hizasının biraz altında:
        //     birinci şahısta sağ altta görünür, dışarıdan "avucunda gösteriyor" gibi). Bakışla birlikte iner-kalkar.
        smallPalmOk = false;
        if (kind == 3 && holdSmooth > 0.01f && wBrush < 0.05f) ApplySmallIK(holdSmooth * nk);
        // Fırça: sağ kol bakışla birlikte iner-kalkar (omuzdan)
        if (wBrush > 0.005f && mUpArmR != null)
        {
            float ang = (Mathf.Clamp(armPitch, -35f, 55f) * 0.5f - 6f) * wBrush;
            mUpArmR.t.rotation = Quaternion.AngleAxis(ang, root.right) * mUpArmR.t.rotation;
        }
        // Fırça — DIŞARIDAN görünüş (diğer oyuncuların ekranı, F5, ayna): boyamıyorken kol öne dümdüz uzanmaz; ressam gibi
        // dirsek bükük, el göğsün sağ önünde, fırça yumruktan öne-yukarı çıkar. Boyarken öne uzanan Blender pozuna geçer.
        // Kendi birinci şahıs görüntün DEĞİŞMEZ (sahibinin ekranında eski poz).
        float relaxW = authoredBrush && !OwnFirstPersonView ? wBrush * (1f - Mathf.Clamp01(paintSmooth)) : 0f;
        if (relaxW > 0.005f) BrushRelaxPose(relaxW);

        // 5c) Kayıt (H): sol elde el kamerası, göğüs hizasında öne doğru tutulur (iki elle tutulan eşya varken
        //     kamera göğüste durur — BodyCam). Herkes görür; kayıt bitince kol yavaşça iner.
        bool rec = netSetup != null && netSetup.IsSpawned && BodyCam.IsRecording(netSetup.OwnerClientId);
        camHandW = Mathf.MoveTowards(camHandW, rec && kind != 1 && kind != 2 ? 1f : 0f, dt * 5f);
        if (camHandW > 0.005f) CamcorderArm(camHandW * nk);

        // 5d) Boşta: çantanın olduğu taraftaki el çantanın kapağına dayanır (yürürken / dururken; koşarken kol sallanır)
        bool bagHandFree = kind != 1 && kind != 2 && camHandW < 0.01f && speedSmooth < 5.2f && airSmooth < 0.3f; // havada iki kol da açılır
        bagHandW = Mathf.MoveTowards(bagHandW, bagHandFree ? 1f : 0f, dt * 3f);
        if (bagHandW > 0.005f) BagHandPose(bagHandW * nk);

        // 5e) Emote (T çarkı): kol / el / parmak katmanlarının üstüne (PlayerAvatarEmotes)
        EmoteLayer(nk);

        // 6) Kafa / göz (kollar etkilenmez)
        LookHead(dt, nk);
        EmoteHeadLayer(nk);
    }

    /// <summary>
    /// Zıplarken kollar sabit durmasın: üst kollar yanlara doğru kalkar (aşağı sarkan halinden en çok ~55°), dirsekler
    /// hafif bükülür. Çıkarken kollar biraz öne savrulur, tepede / düşerken denge için en açık hali; yere değince
    /// airSmooth düştükçe kollar yumuşakça iner. Fırçayı tutan sağ kol fırça pozunda kalır.
    /// </summary>
    private void JumpArms(float air, float wBrush, int kind)
    {
        Vector3 fwd = body.forward, right = body.right, up = Vector3.up;
        float rising = Mathf.Clamp01(vySmooth / 3f), falling = Mathf.Clamp01(-vySmooth / 3f);
        for (int k = 0; k < 2; k++)
        {
            Bone ua = k == 0 ? mUpArmL : mUpArmR, la = k == 0 ? mLoArmL : mLoArmR, hd = k == 0 ? mHandL : mHandR;
            if (ua == null || la == null || hd == null) continue;
            // Sonraki katmanların sürdüğü kollar atlanır (IK mevcut dönüşün üstüne kurulur, burkulma sızmasın):
            // tablo / büyük eşya iki kol, küçük eşya sağ kol, kayıt sol kol
            if (kind == 1 || kind == 2 || (k == 1 && kind == 3) || (k == 0 && camHandW > 0.01f)) continue;
            float w = air * (k == 1 ? 1f - Mathf.Clamp01(wBrush) : 1f);
            if (w < 0.01f) continue;
            Vector3 outw = right * (k == 0 ? -1f : 1f);
            float amt = w * (0.7f + 0.3f * falling); // 0..1: hedef yöne ne kadar
            Vector3 dir = (la.t.position - ua.t.position).normalized;
            // Çıkarken ~40° öne-yana, düşerken ~48° yana (yatay olmaz)
            Vector3 want = (-up * (0.72f - 0.1f * falling) + outw * (0.55f + 0.2f * falling) + fwd * (0.35f * rising + 0.05f)).normalized;
            ua.t.rotation = Quaternion.FromToRotation(dir, Vector3.Slerp(dir, want, amt)) * ua.t.rotation;
            // Dirsek: önkol üst koldan biraz yukarı bükülür
            Vector3 upDir = (la.t.position - ua.t.position).normalized;
            Vector3 lo = (hd.t.position - la.t.position).normalized;
            Vector3 loWant = (upDir + up * 0.55f + fwd * 0.15f).normalized;
            la.t.rotation = Quaternion.FromToRotation(lo, Vector3.Slerp(lo, loWant, amt * 0.3f)) * la.t.rotation;
        }
    }

    private bool smallPalmOk;
    private Vector3 smallPalm;
    private Transform bagBone;
    private float bagHandW;
    private Quaternion[] handBagL, handBagR;

    /// <summary>
    /// Çanta tarafındaki el çantanın kapağının üstüne dayanır, parmaklar kapağın önünden aşağı sarkar (Blender'da önden,
    /// yandan, arkadan bakıldı). Kol çantanın altına uzanamıyor (çanta kol boyundan ~10 cm aşağıda), bu yüzden "alttan
    /// tutma" yerine kapağı tutma. Çantaya göre: dışa 10 cm, öne 6,5 cm, 3 cm aşağı.
    /// </summary>
    private void BagHandPose(float w)
    {
        if (bagBone == null || mHips == null) return;
        Vector3 fwd = body.forward, right = body.right, up = Vector3.up;
        float side = Mathf.Sign(Vector3.Dot(bagBone.position - mHips.t.position, right));
        if (side == 0f) side = -1f;
        bool leftArm = side < 0f;
        Bone ua = leftArm ? mUpArmL : mUpArmR, la = leftArm ? mLoArmL : mLoArmR, hd = leftArm ? mHandL : mHandR;
        Transform mid = leftArm ? midFingerL : midFingerR, th = leftArm ? thumbL : thumbR;
        if (ua == null || la == null || hd == null || mid == null) return;
        float s = root.lossyScale.y;
        Vector3 outw = right * side;
        Vector3 target = bagBone.position + (outw * 0.10f + fwd * 0.065f - up * 0.029f) * s;
        Vector3 wantFinger = (fwd * 0.55f - up * 0.75f).normalized;
        Vector3 wantSide = (fwd * 0.8f + up * 0.2f).normalized;
        float armSide = leftArm ? -1f : 1f;
        for (int pass = 0; pass < 2; pass++)
        {
            Vector3 palmOff = pass == 0 ? Vector3.zero : (mid.position - hd.t.position) * 0.55f;
            ArmIK(ua, la, hd, target - palmOff, armSide, w);
            Vector3 fa = (hd.t.position - la.t.position).normalized;
            Vector3 ct0 = Vector3.ProjectOnPlane(ThumbSide(hd, th), fa), wt0 = Vector3.ProjectOnPlane(wantSide, fa);
            if (ct0.sqrMagnitude > 1e-6f && wt0.sqrMagnitude > 1e-6f)
                la.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(ct0, wt0, fa) * 0.6f * w, fa) * la.t.rotation;
            OrientHandTo(hd, mid, th, wantFinger, wantSide, w);
        }
        Quaternion[] pose = leftArm ? handBagL : handBagR;
        if (pose != null) HandPose(leftArm ? fingersL : fingersR, pose, w);
    }

    /// <summary>
    /// Çanta: Blender kliplerinde çanta kalçanın arkasında, alt ucu dışa ~15° yatık duruyordu (dışarıdan gövdeden
    /// ayrılmış gibi, askının ucu boşta). Her kare klipten sonra: alt ucu içe döndürülür (dik asılır), 10 cm öne
    /// (kalçanın yanına, askının bittiği yere) ve 1,5 cm içe alınır. Blender'da önden / arkadan / yandan bakıldı.
    /// </summary>
    private void FitBag()
    {
        if (bagBone == null || mHips == null) return;
        Vector3 fwd = root.forward, right = root.right, up = root.up;
        float side = Mathf.Sign(Vector3.Dot(bagBone.position - mHips.t.position, right));
        if (side == 0f) side = -1f;
        // Hangi yöne dönünce alt uç içe gelir: iki yönü dene (sol / sağ el kuralından bağımsız)
        Vector3 test = -up + right * (side * 0.26f);
        Quaternion qa = Quaternion.AngleAxis(15f, fwd), qb = Quaternion.AngleAxis(-15f, fwd);
        Quaternion q = Vector3.Dot(qa * test, right * side) < Vector3.Dot(qb * test, right * side) ? qa : qb;
        bagBone.rotation = q * bagBone.rotation;
        float s = root.lossyScale.y;
        bagBone.position += (fwd * 0.10f - right * (side * 0.015f)) * s;
    }
    private float camHandW;
    private Quaternion[] handCamL;

    /// <summary>Kayıt kamerasını tutan sol kol (Blender'da aynı IK ile denendi): bilek omzun ~%16 kol boyu altında,
    /// ~%74 önünde, biraz içeride; parmaklar öne, işaret tarafı yukarı; parmaklar kamerayı sarar.</summary>
    private void CamcorderArm(float w)
    {
        if (mUpArmL == null || mLoArmL == null || mHandL == null) return;
        Vector3 right = body.right, fwd = body.forward, up = Vector3.up;
        float R = armReach;
        Vector3 target = mUpArmL.t.position - up * (0.16f * R) + fwd * (0.74f * R) + right * (0.16f * R);
        ArmIK(mUpArmL, mLoArmL, mHandL, target, -1f, w);
        Vector3 fa = (mHandL.t.position - mLoArmL.t.position).normalized;
        Vector3 ct0 = Vector3.ProjectOnPlane(ThumbSide(mHandL, thumbL), fa), wt0 = Vector3.ProjectOnPlane(up, fa);
        if (ct0.sqrMagnitude > 1e-6f && wt0.sqrMagnitude > 1e-6f)
            mLoArmL.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(ct0, wt0, fa) * 0.6f * w, fa) * mLoArmL.t.rotation;
        OrientHandTo(mHandL, midFingerL, thumbL, (fwd * 0.9f + right * 0.25f).normalized, up, w);
        HandPose(fingersL, handCamL, w);
    }

    /// <summary>
    /// El kamerasının yeri ve dönüşü (BodyCam yerleştirir). handW: 1 = sol elde, 0 = göğüste (iki el doluyken);
    /// arası geçiş (kol kalkarken / inerken kamera elle göğüs arasında yumuşak gider).
    /// </summary>
    public bool TryGetCamcorderPose(out Vector3 pos, out Quaternion rot, out float handW)
    {
        pos = Vector3.zero; rot = Quaternion.identity; handW = 0f;
        if (!animated || model == null || mChest == null) return false;
        PoseNow();
        Vector3 right = body.right, fwd = body.forward, up = Vector3.up;
        float ks = armReach / 0.44f;
        rot = Quaternion.LookRotation(fwd, up);
        float s = transform.lossyScale.y;
        Vector3 chest = mChest.t.position + (fwd * 0.20f - right * 0.12f + up * 0.12f) * s; // sol omuz önü (taşınan vazonun içine girmesin)
        pos = chest;
        if (camHandW > 0.001f && mHandL != null && midFingerL != null)
        {
            Vector3 h = mHandL.t.position, m = midFingerL.position;
            Vector3 hand = h + (m - h) * 0.55f + (right * 0.055f + up * 0.005f + fwd * 0.015f) * ks;
            handW = Mathf.SmoothStep(0f, 1f, camHandW);
            pos = Vector3.Lerp(chest, hand, handW);
        }
        return true;
    }

    /// <summary>Sahibinin kendi birinci şahıs görüntüsü mü (F5 önizleme kapalı)? Her bilgisayar pozu kendisi hesapladığı
    /// için birinci şahıs ile dışarıdan görünüş ayrı ayarlanabilir.</summary>
    private bool OwnFirstPersonView => netSetup != null && netSetup.IsSpawned && netSetup.IsOwner && !previewOn && !GarageMirror.ReflectingLocal;

    /// <summary>Fırçayı rahat tutuş (Blender'da aynı IK ile denendi): bilek omzun ~%42 kol boyu altında, ~%62 önünde;
    /// fırça yumruktan öne-yukarı (biraz içe), başparmak yukarı-içe.</summary>
    private void BrushRelaxPose(float w)
    {
        if (mUpArmR == null || mLoArmR == null || mHandR == null) return;
        Vector3 right = body.right, fwd = body.forward, up = Vector3.up;
        float R = armReach;
        Vector3 target = mUpArmR.t.position - up * (0.42f * R) + fwd * (0.62f * R) + right * (0.06f * R);
        ArmIK(mUpArmR, mLoArmR, mHandR, target, 1f, w);
        if (brushGrip == null || brushTip == null) return;
        Quaternion from = mHandR.t.rotation;
        Vector3 want = (fwd * 0.62f + up * 0.75f - right * 0.18f).normalized;
        Vector3 cur = brushTip.position - brushGrip.position;
        if (cur.sqrMagnitude < 1e-8f) return;
        mHandR.t.rotation = Quaternion.FromToRotation(cur, want) * mHandR.t.rotation;
        if (thumbR != null)
        {
            Vector3 ax = (brushTip.position - brushGrip.position).normalized;
            Vector3 ct = Vector3.ProjectOnPlane(thumbR.position - mHandR.t.position, ax), wt = Vector3.ProjectOnPlane(up * 0.5f - right * 0.8f, ax);
            if (ct.sqrMagnitude > 1e-6f && wt.sqrMagnitude > 1e-6f)
                mHandR.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(ct, wt, ax), ax) * mHandR.t.rotation;
        }
        mHandR.t.rotation = Quaternion.Slerp(from, mHandR.t.rotation, w);
        // Parmaklar sapı sarar ("El_Kavra"): Blender'daki fırça yumruğu dışarıdan parmaksız bir top gibi görünüyordu;
        // bu duruşta dört parmak sapın etrafında, başparmak önde görünür (Blender'da iki yandan bakıldı)
        if (handKavraR != null) HandPose(fingersR, handKavraR, w);
    }

    /// <summary>Göz hizası (dünya y): göz kemikleri (eğilince / zıplayınca birlikte iner), yoksa kamera hizası.</summary>
    private float EyeY()
    {
        if (eyeL != null && eyeR != null) return (eyeL.position.y + eyeR.position.y) * 0.5f;
        return transform.position.y + PlayerController.EyeOffset;
    }

    private void ApplySmallIK(float w)
    {
        if (mUpArmR == null || mLoArmR == null || mHandR == null) return;
        // Yüzük bozuk paradan TAMAMEN ayrı: kendi kol yeri + kendi el duruşu (RingPose)
        if (heldView != null && heldView.IsRing && handCimdikR != null && midFingerR != null) { RingPose(w); return; }
        Vector3 right = body.right, fwd = body.forward, up = Vector3.up;
        Vector3 sh = mUpArmR.t.position;
        float R = armReach;
        // Hedef: omzun önünde-üstünde, biraz dışarıda (göz hizasının ~22 cm altı; yüzün önüne gelmez)
        float upAmt = Mathf.Clamp(EyeY() - 0.19f * (R / 0.44f) - sh.y, 0.05f * R, 0.55f * R);
        Vector3 off = fwd * (R * 0.86f) + up * upAmt + right * (R * 0.36f); // yüzün önüne değil, yana-öne
        if (off.magnitude > R * 0.95f) off = off.normalized * (R * 0.95f);
        off = Quaternion.AngleAxis(Mathf.Clamp(armPitch, -30f, 50f) * 0.5f, right) * off;
        foreach (Transform f in fingersR) if (f != null && fingerRest.TryGetValue(f, out Quaternion fr)) f.localRotation = fr;
        ArmIK(mUpArmR, mLoArmR, mHandR, sh + off, 1f, w);
        if (heldView != null && heldView.IsCoin && handYaziR != null && handFiskeR != null && midFingerR != null)
        {
            CoinHand(w, right, fwd, up, R);
            return;
        }
        // Avuç yukarı: önce ön kol kendi ekseninde döner (bilek burulmasın), sonra el; parmaklar öne, başparmak dışa
        if (midFingerR != null && thumbR != null)
        {
            Vector3 fa = (mHandR.t.position - mLoArmR.t.position).normalized;
            Vector3 ct0 = Vector3.ProjectOnPlane(ThumbSide(mHandR, thumbR), fa), wt0 = Vector3.ProjectOnPlane(right * 0.9f + up * 0.15f, fa);
            if (ct0.sqrMagnitude > 1e-6f && wt0.sqrMagnitude > 1e-6f)
                mLoArmR.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(ct0, wt0, fa) * 0.6f * w, fa) * mLoArmR.t.rotation;
        }
        if (midFingerR != null)
        {
            Quaternion from = mHandR.t.rotation;
            Vector3 wantFinger = (fwd * 0.85f - right * 0.3f + up * 0.12f).normalized;
            mHandR.t.rotation = Quaternion.FromToRotation((midFingerR.position - mHandR.t.position).normalized, wantFinger) * mHandR.t.rotation;
            if (thumbR != null)
            {
                Vector3 axis = (midFingerR.position - mHandR.t.position).normalized;
                Vector3 ct = Vector3.ProjectOnPlane(ThumbSide(mHandR, thumbR), axis), wt = Vector3.ProjectOnPlane(right * 0.9f + up * 0.15f, axis);
                if (ct.sqrMagnitude > 1e-6f && wt.sqrMagnitude > 1e-6f)
                    mHandR.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(ct, wt, axis), axis) * mHandR.t.rotation;
            }
            mHandR.t.rotation = Quaternion.Slerp(from, mHandR.t.rotation, w);
            Vector3 h = mHandR.t.position, m = midFingerR.position;
            smallPalm = h + (m - h) * 0.55f + (up * 0.9f - right * 0.16f).normalized * (0.045f * (R / 0.44f));
            smallPalmOk = true;
            // Avuç açık; parmaklar çukurlaşırken eşyaya (yüzük / ganimet) değince durur. Yazı-tura sırasında düz avuç.
            HoldShape hsh = heldView != null ? heldView.Shape : null;
            if (hsh != null && handCupR != null && handAvucR != null && !heldView.CoinFlipping)
            {
                Vector3 hp = mHandR.t.position, mp = midFingerR.position;
                Vector3 itemAt = hp + (mp - hp) * 0.55f + (up * 0.9f - right * 0.16f).normalized * (0.045f * (R / 0.44f));
                AdaptFingers(fingersR, chainsR, handAvucR, handCupR, hsh, itemAt, ItemRotation(3), 0.02f * (R / 0.44f), w);
            }
            else if (!HandPose(fingersR, handAvucR, w)) CurlFingers(fingersR, mHandR, smallPalm + up * (0.05f * (R / 0.44f)), w * 0.35f);
        }
        if (smallPalmOk) { Vector3 h = mHandR.t.position, m = midFingerR.position; smallPalm = h + (m - h) * 0.55f + (up * 0.9f - right * 0.16f).normalized * (0.045f * (R / 0.44f)); }
    }

    /// <summary>
    /// Yüzük (bozuk paradan ayrı tutuş): kol dirsekten bükük, el göğsün sağ önünde, omzun ~%86 kol boyu önünde, göz
    /// hizasının ~34 cm altında; parmaklar öne-yukarı, işaret parmağı üstte. Yüzük işaret ile başparmağın uçları
    /// arasında, alt bandından tutulur ve dik durur; orta / yüzük / serçe parmak avuca kıvrık. Birinci şahısta sağ altta
    /// yüzük net görünür, dışarıdan "yüzüğü gösteriyor" gibi. Blender'da aynı IK hesabıyla birinci / üçüncü şahıs denendi.
    /// Bakışla biraz iner (aşağı bakınca ekranda kalır); yüzüğün deliği göze bakar (HeldItemView).
    /// </summary>
    private void RingPose(float w)
    {
        Vector3 right = body.right, fwd = body.forward, up = Vector3.up;
        Vector3 sh = mUpArmR.t.position;
        float R = armReach, ks = R / 0.44f;
        float upAmt = Mathf.Clamp(EyeY() - 0.34f * ks - sh.y, 0.05f * R, 0.6f * R);
        Vector3 off = fwd * (0.86f * R) + up * upAmt + right * (0.04f * R);
        if (off.magnitude > R * 0.95f) off = off.normalized * (R * 0.95f);
        off = Quaternion.AngleAxis(Mathf.Clamp(armPitch, -30f, 50f) * 0.35f, right) * off;
        foreach (Transform f in fingersR) if (f != null && fingerRest.TryGetValue(f, out Quaternion fr)) f.localRotation = fr;
        ArmIK(mUpArmR, mLoArmR, mHandR, sh + off, 1f, w);
        Vector3 wantFinger = (fwd * 0.80f + up * 0.42f - right * 0.22f).normalized;
        Vector3 wantSide = (up * 0.9f + right * 0.1f).normalized;   // işaret parmağı tarafı yukarı
        Vector3 fa = (mHandR.t.position - mLoArmR.t.position).normalized;
        Vector3 ct0 = Vector3.ProjectOnPlane(ThumbSide(mHandR, thumbR), fa), wt0 = Vector3.ProjectOnPlane(wantSide, fa);
        if (ct0.sqrMagnitude > 1e-6f && wt0.sqrMagnitude > 1e-6f)
            mLoArmR.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(ct0, wt0, fa) * 0.6f * w, fa) * mLoArmR.t.rotation;
        OrientHandTo(mHandR, midFingerR, thumbR, wantFinger, wantSide, w);
        HandPose(fingersR, handCimdikR, w);
        // Diğer üç parmak avuca kıvrık (çimdik pozunda gevşek açık duruyordu)
        if (handKapaliR != null && handKapaliR.Length == fingersR.Count)
            for (int i = 0; i < fingersR.Count; i++)
            {
                Transform t = fingersR[i];
                if (t == null) continue;
                string n = t.name;
                if (n.StartsWith("Middle") || n.StartsWith("Ring") || n.StartsWith("Little"))
                    t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.Slerp(handCimdikR[i], handKapaliR[i], 0.9f), w);
            }
        Transform id = null, ii = null, td = null, ti = null;
        foreach (Transform t in fingersR)
        {
            if (t == null) continue;
            if (t.name.StartsWith("Index_Distal")) id = t; else if (t.name.StartsWith("Index_Intermediate")) ii = t;
            else if (t.name.StartsWith("Thumb_Distal")) td = t; else if (t.name.StartsWith("Thumb_Intermediate")) ti = t;
        }
        if (id == null || ii == null || td == null || ti == null) return;
        // Kalın yüzük: işaret ile başparmak bant kalınlığı kadar aralanır (eskiden uçlar birbirine değecek kadar kapalıydı,
        // kalın bant parmakların içinden geçip parmaklar kırık görünüyordu). Açıklık "El_Avuc"a doğru adım adım.
        float tube = heldView != null ? heldView.RingTube : 0.004f;
        if (handAvucR != null && handAvucR.Length == fingersR.Count)
        {
            for (int st = 1; st <= 10; st++)
            {
                Vector3 ta = id.position + (id.position - ii.position) * 0.6f, tb = td.position + (td.position - ti.position) * 0.6f;
                if (Vector3.Distance(ta, tb) >= 2.2f * tube) break;
                float u = st / 10f;
                for (int i = 0; i < fingersR.Count; i++)
                {
                    Transform t = fingersR[i];
                    if (t != null && (t.name.StartsWith("Index") || t.name.StartsWith("Thumb")))
                        t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.Slerp(handCimdikR[i], handAvucR[i], u), w);
                }
            }
        }
        Vector3 pinch = (id.position + (id.position - ii.position) * 0.6f + td.position + (td.position - ti.position) * 0.6f) * 0.5f;
        // parmaklar halkanın alt bandını (tüpün ortasından) tutar: halkanın ortası bandın orta yarıçapı kadar yukarıda
        float rr = heldView != null ? heldView.RingRadius - tube : 0.026f;
        // Halka göze dönük (HeldItemView): "yukarı" halkanın kendi düzleminde ölçülür
        Vector3 ringUp = Vector3.ProjectOnPlane(up, (pinch - EyeWorldPoint()).normalized);
        if (ringUp.sqrMagnitude < 1e-4f) ringUp = up;
        smallPalm = pinch + ringUp.normalized * rr;
        smallPalmOk = true;
    }

    /// <summary>Yazı-tura tutuşu: yumruk öne bakar (avuç içe, işaret tarafı yukarı), para başparmak tırnağı ile işaret
    /// parmağının kıvrımının üstünde yatar. [R] ile atışta başparmak yukarı fiske vurur (Blender "El_Fiske"), para
    /// dönerek havalanır, geri düşer.</summary>
    private void CoinHand(float w, Vector3 right, Vector3 fwd, Vector3 up, float R)
    {
        // Ön kol kendi ekseninde döner (bilek burulmasın): işaret tarafı yukarı
        Vector3 fa = (mHandR.t.position - mLoArmR.t.position).normalized;
        Vector3 ct0 = Vector3.ProjectOnPlane(ThumbSide(mHandR, thumbR), fa), wt0 = Vector3.ProjectOnPlane(up, fa);
        if (ct0.sqrMagnitude > 1e-6f && wt0.sqrMagnitude > 1e-6f)
            mLoArmR.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(ct0, wt0, fa) * 0.6f * w, fa) * mLoArmR.t.rotation;
        OrientHandTo(mHandR, midFingerR, thumbR, (fwd * 0.9f - up * 0.15f - right * 0.15f).normalized, (up * 0.95f + right * 0.1f).normalized, w);
        HandPose(fingersR, handYaziR, w);
        // Paranın yeri: başparmak ucunun hemen üstü (fiske başlamadan önceki pozdan)
        Transform td = null, ti = null;
        for (int i = 0; i < fingersR.Count; i++)
        {
            Transform t = fingersR[i];
            if (t == null) continue;
            if (t.name.StartsWith("Thumb_Distal")) td = t; else if (t.name.StartsWith("Thumb_Intermediate")) ti = t;
        }
        float ks = R / 0.44f;
        if (td != null && ti != null) smallPalm = td.position + (td.position - ti.position) + up * (0.012f * ks);
        else { Vector3 h = mHandR.t.position, m = midFingerR.position; smallPalm = h + (m - h) * 0.6f + up * (0.05f * ks); }
        smallPalmOk = true;
        // Fiske: atışın başında başparmak hızla yukarı fırlar, para havadayken öyle kalır, düşerken geri kıvrılır
        float u = heldView.CoinFlipProgress;
        if (u >= 0f)
        {
            float f = u < 0.08f ? u / 0.08f : u < 0.35f ? 1f : u < 0.6f ? 1f - (u - 0.35f) / 0.25f : 0f;
            for (int i = 0; i < fingersR.Count; i++)
            {
                Transform t = fingersR[i];
                if (t != null && t.name.StartsWith("Thumb")) t.localRotation = Quaternion.Slerp(t.localRotation, handFiskeR[i], f * w);
            }
        }
    }

    /// <summary>Bakış eğimi (aşağı +). Sahibinde kameranın kendisi (gecikmesiz), diğerlerinde senkron eğim.</summary>
    private float LookPitchNow()
    {
        if (netSetup == null || !netSetup.IsSpawned) return 0f;
        if (netSetup.IsOwner)
        {
            Camera c = Camera.main;
            if (c != null && c.transform.IsChildOf(transform)) return Mathf.DeltaAngle(0f, c.transform.localEulerAngles.x);
        }
        return netSetup.LookPitch;
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
        float ks = armReach / 0.44f;
        HoldShape shape = heldView.Shape;
        bool shaped = shape != null && handKapaliL != null && handAvucL != null;
        Quaternion irot = ItemRotation(kind);
        Quaternion animL = mHandL.t.localRotation, animR = mHandR.t.localRotation;
        // Klibin (Tasima_*) kendi dirsek yönü: IK dirseği bu yöne büker → kol ve kol kıyafeti klipteki gibi düzgün kalır
        Vector3 clipElbL = mLoArmL.t.position - mUpArmL.t.position, clipElbR = mLoArmR.t.position - mUpArmR.t.position;
        bool bustBase = kind == 2 && shaped && heldView.IsBust && shape.HasBase;
        for (int k = 0; k < 2; k++)
        {
            float side = k == 0 ? -1f : 1f;
            Bone up = k == 0 ? mUpArmL : mUpArmR, lo = k == 0 ? mLoArmL : mLoArmR, hand = k == 0 ? mHandL : mHandR;
            Transform mid = k == 0 ? midFingerL : midFingerR, thumb = k == 0 ? thumbL : thumbR;
            Vector3 inward = -right * side;
            Vector3 wrist, wantFinger, wantThumb;
            if (shaped)
            {
                // Eşyanın gerçek şekline göre: avucun ortası yüzeyin üstüne (yüzey normali yönünde avuç kalınlığı kadar dışarıda)
                Vector3 palmT, n;
                if ((kind == 1 || shape.IsBox) && EdgeGrip(shape))
                {
                    // Tablo (insan gibi): avuçlar iki YAN KENARDA birbirine bakar, parmaklar kenarı dolanıp tablonun arkasına
                    // (karşı yüzüne) kapanır, başparmak sana bakan yüzde
                    Vector3 xA = irot * Vector3.right, yA = irot * Vector3.up;
                    float sx = Mathf.Sign(Vector3.Dot(xA, right)) * side;
                    Vector3 bc = shape.BoxCenter, bh = shape.BoxHalf;
                    Vector3 loc = new Vector3(bc.x + sx * bh.x, bc.y + Mathf.Min(bh.y * 0.15f, 0.05f * ks), bc.z); // ortanın biraz üstünden: birinci şahısta eller ekranda
                    n = xA * sx;
                    palmT = center + irot * loc + n * (0.018f * ks);
                    wantFinger = (fwd * 0.7f + yA * 0.25f - n * 0.1f).normalized;
                    wantThumb = (yA * 0.8f - fwd * 0.25f).normalized;
                }
                else if (kind == 1 || shape.IsBox)
                {
                    // Çok geniş tablo (kollar kenarlara yetişmez): avuç arka yüzde, yan kenarın hemen içinde; parmaklar kenarı öne sarar
                    Vector3 xA = irot * Vector3.right, yA = irot * Vector3.up, zA = irot * Vector3.forward;
                    float sx = Mathf.Sign(Vector3.Dot(xA, right)) * side;
                    float sz = -Mathf.Sign(Vector3.Dot(zA, fwd));
                    Vector3 bc = shape.BoxCenter, bh = shape.BoxHalf;
                    Vector3 loc = new Vector3(bc.x + sx * Mathf.Max(0.02f, bh.x - 0.035f * ks), bc.y - Mathf.Min(bh.y * 0.3f, 0.1f * ks), bc.z + sz * bh.z);
                    n = zA * sz;
                    palmT = center + irot * loc + n * (0.018f * ks);
                    wantFinger = (xA * sx * 0.55f + yA * 0.6f - n * 0.15f).normalized;
                    wantThumb = (-xA * sx * 0.8f + yA * 0.3f).normalized;
                }
                else if (bustBase)
                {
                    // Büst: alttaki dikdörtgen kaidenin iki yan yüzünden; avuçlar kaidenin yanlarına yaslanır, parmaklar
                    // yan yüz boyunca öne-hafif aşağı, başparmak üstte
                    Vector3 xA = irot * Vector3.right;
                    float sx = Mathf.Sign(Vector3.Dot(xA, right)) * side;
                    Vector3 bc = shape.BaseCenter, bh = shape.BaseHalf;
                    n = xA * sx;
                    palmT = center + irot * new Vector3(bc.x + sx * bh.x, bc.y, bc.z) + n * (0.018f * ks);
                    Vector3 tang = Vector3.ProjectOnPlane(fwd, n);
                    tang = tang.sqrMagnitude > 1e-6f ? tang.normalized : fwd;
                    wantFinger = (tang * 0.8f - Vector3.up * 0.25f - n * 0.15f).normalized;
                    wantThumb = (Vector3.up * 0.85f + tang * 0.2f).normalized;
                }
                else
                {
                    // Vazo / büst / heykel: en uygun yükseklikte (TryGetCarryPoint seçti) iki yanından; avuç yüzeye yaslanır.
                    // Avuçlar yanların biraz ARKASINDA (gövdeye yakın taraf) → parmaklar öne doğru eşyanın yüzeyini sarar
                    // (eskiden avuç tam yanda, parmaklar havaya öne uzanıyordu; karşıdan bakınca kötü duruyordu).
                    Vector3 dl = Quaternion.Inverse(irot) * (right * side - fwd * 0.45f).normalized; dl.y = 0f;
                    if (dl.sqrMagnitude < 1e-6f) dl = Vector3.right * side;
                    dl.Normalize();
                    float a = Mathf.Atan2(dl.x, dl.z);
                    float rr = shape.Radius(gripY, a);
                    const float e = 0.02f;
                    float slope = (shape.Radius(gripY + e, a) - shape.Radius(gripY - e, a)) / (2f * e);
                    Vector3 surf = center + irot * (dl * rr + Vector3.up * gripY);
                    n = (irot * dl - (irot * Vector3.up) * slope).normalized;
                    palmT = surf + n * (0.03f * ks);
                    Vector3 tang = Vector3.ProjectOnPlane(fwd, n);
                    tang = tang.sqrMagnitude > 1e-6f ? tang.normalized : fwd;
                    wantFinger = (tang * 0.85f + Vector3.up * 0.05f - n * 0.35f).normalized; // yüzey boyunca öne, hafif içe
                    wantThumb = (Vector3.up * 0.85f + tang * 0.3f).normalized;
                }
                // İki geçiş: önce eli yönlendir, avuç ortasının bileğe göre yerini ölç, sonra bileği ona göre koy.
                // Vazo / büst: dirsek klibin yönüne (biraz aşağı) bükülür, bilek burulmasının çoğunu önkol burulma kemiği alır
                // (eskiden dirsek aşağı-içe düşüyor, bilek tek başına dönüyordu: kol kıyafeti buruşup bozuluyordu)
                bool big = kind == 2 && !shape.IsBox;
                Vector3 pole = big ? (k == 0 ? clipElbL : clipElbR).normalized + Vector3.down * 0.8f : Vector3.zero;
                Transform twist = big ? (k == 0 ? loTwistL : loTwistR) : null;
                ArmIK(up, lo, hand, palmT, side, w, pole);
                ForearmTwist(lo, hand, thumb, twist, wantFinger, wantThumb, mid, w);
                OrientHandTo(hand, mid, thumb, wantFinger, wantThumb, w);
                Vector3 v = PalmPoint(hand, mid) - hand.t.position;
                ArmIK(up, lo, hand, palmT - v, side, w, pole);
                ForearmTwist(lo, hand, thumb, twist, wantFinger, wantThumb, mid, w);
                OrientHandTo(hand, mid, thumb, wantFinger, wantThumb, w);
                continue;
            }
            if (kind == 1)
            {
                // Tablo: eller tablonun ARKASINDA, yan kenarlara yakın (kollar tablonun içinden geçmez); avuçlar öne bakar,
                // parmaklar yukarı-dışa uzanıp yan kenarı öne doğru sarar
                wrist = center + right * side * (half * 0.82f) - fwd * (0.05f * ks) - Vector3.up * Mathf.Min(hh * 0.3f, 0.1f * ks);
                wantFinger = (Vector3.up * 0.65f + right * side * 0.5f + fwd * 0.25f).normalized;
                wantThumb = (inward * 0.8f + Vector3.up * 0.3f).normalized;
            }
            else
            {
                // Vazo / büst: iki yandan alt yarısından kavrar (avuçlar içe, parmaklar öne-içe sarar)
                wrist = center + right * side * half - Vector3.up * (hh * 0.45f);
                wantFinger = (fwd * 0.6f + inward * 0.5f + Vector3.up * 0.15f).normalized;
                wantThumb = (Vector3.up * 0.8f + fwd * 0.3f).normalized;
            }
            ArmIK(up, lo, hand, wrist, side, w);
            OrientHandTo(hand, mid, thumb, wantFinger, wantThumb, w);
        }
        // Bilekler aşırı bükülmesin (kol ile el arasında boşluk / yırtık kalmasın)
        ClampLocal(mHandL.t, animL, shaped ? 85f : 70f);
        ClampLocal(mHandR.t, animR, shaped ? 85f : 70f);
        if (shaped)
        {
            // Parmaklar açıktan yumruğa doğru eklem eklem kapanır, eşyanın yüzeyine değince durur (şekle göre sarar)
            float rad = 0.02f * ks; // parmak + dış çizgi kalınlığı: çizgi ince duvarın içinden görünmesin
            // Vazo / büst: parmaklar "El_Kavra"ya (gövdeyi saran poz) doğru kapanır; tabloda yumruğa (El_Kapali)
            bool wrap = !(kind == 1 || shape.IsBox) && handWrapL != null && handWrapR != null;
            AdaptFingers(fingersL, chainsL, handAvucL, wrap ? handWrapL : handKapaliL, shape, center, irot, rad, w);
            AdaptFingers(fingersR, chainsR, handAvucR, wrap ? handWrapR : handKapaliR, shape, center, irot, rad, w);
            return;
        }
        if (kind == 1)
        {
            // tablo: parmaklar kanca gibi yan kenarın önüne kıvrılır (Blender pozu "El_Tablo")
            if (!HandPose(fingersL, handTabloL, w)) CurlFingers(fingersL, mHandL, center - right * (half + 0.02f) + fwd * 0.03f, w);
            if (!HandPose(fingersR, handTabloR, w)) CurlFingers(fingersR, mHandR, center + right * (half + 0.02f) + fwd * 0.03f, w);
        }
        else
        {
            // vazo / büst: parmaklar gövdeyi sarar ("El_Kavra")
            Vector3 grip = center - Vector3.up * (hh * 0.35f);
            if (!HandPose(fingersL, handKavraL, w)) CurlFingers(fingersL, mHandL, grip, w);
            if (!HandPose(fingersR, handKavraR, w)) CurlFingers(fingersR, mHandR, grip, w);
        }
    }

    /// <summary>Eşyanın bu karedeki dönüşü — HeldItemView ile aynı hesap (tablo / büyük: eğim + yön; küçük: yön; para: düz).</summary>
    private Quaternion ItemRotation(int kind)
    {
        float y = transform.eulerAngles.y, sy = heldView != null ? heldView.ShownYaw : 0f;
        if (kind == 3) return heldView != null && heldView.IsCoin ? Quaternion.Euler(0f, y, 0f) : Quaternion.Euler(0f, y + sy, 0f);
        return Quaternion.AngleAxis(carryTilt + (heldView != null ? heldView.ExtraTilt : 0f), transform.right) * Quaternion.Euler(0f, y + sy, 0f);
    }

    /// <summary>Tablo yan kenarlarından tutulabilir mi (kollar kenarlara yetişir).</summary>
    private bool EdgeGrip(HoldShape shape) => shape != null && shape.IsBox && shape.BoxHalf.x + 0.03f <= armReach * 0.85f;

    private static Vector3 PalmPoint(Bone hand, Transform mid)
        => mid != null ? hand.t.position + (mid.position - hand.t.position) * 0.55f : hand.t.position;

    /// <summary>Başparmak tarafını wantThumb'a çevirmek için gereken burulmanın %60'ı önkol burulma kemiğine (kalanı bilek).</summary>
    private void ForearmTwist(Bone lo, Bone hand, Transform thumb, Transform twist, Vector3 wantFinger, Vector3 wantThumb, Transform mid, float w)
    {
        if (twist == null || thumb == null || mid == null) return;
        Quaternion h0 = hand.t.rotation;
        hand.t.rotation = Quaternion.FromToRotation((mid.position - hand.t.position).normalized, wantFinger) * hand.t.rotation;
        Vector3 fa = (hand.t.position - lo.t.position).normalized;
        Vector3 c = Vector3.ProjectOnPlane(ThumbSide(hand, thumb), fa), wt = Vector3.ProjectOnPlane(wantThumb, fa);
        hand.t.rotation = h0;
        if (c.sqrMagnitude > 1e-8f && wt.sqrMagnitude > 1e-8f)
            twist.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(c, wt, fa) * 0.6f * w, fa) * twist.rotation;
    }

    /// <summary>Eli çevir: parmaklar (orta parmak kökü) wantFinger yönüne, başparmak wantThumb tarafına.</summary>
    private void OrientHandTo(Bone hand, Transform mid, Transform thumb, Vector3 wantFinger, Vector3 wantThumb, float w)
    {
        if (hand == null || mid == null) return;
        Quaternion from = hand.t.rotation;
        hand.t.rotation = Quaternion.FromToRotation((mid.position - hand.t.position).normalized, wantFinger) * hand.t.rotation;
        if (thumb != null)
        {
            Vector3 axis = (mid.position - hand.t.position).normalized;
            Vector3 ct = Vector3.ProjectOnPlane(ThumbSide(hand, thumb), axis), wt = Vector3.ProjectOnPlane(wantThumb, axis);
            if (ct.sqrMagnitude > 1e-6f && wt.sqrMagnitude > 1e-6f)
                hand.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(ct, wt, axis), axis) * hand.t.rotation;
        }
        hand.t.rotation = Quaternion.Slerp(from, hand.t.rotation, w);
    }

    // Parmak zincirleri (kök → orta → uç), fingersL / fingersR içindeki sıralar
    private readonly List<int[]> chainsL = new List<int[]>(), chainsR = new List<int[]>();
    private Quaternion[] fingerTmp;

    private static void BuildChains(List<Transform> list, List<int[]> chains)
    {
        chains.Clear();
        int i = 0;
        while (i < list.Count)
        {
            string f = list[i] != null ? list[i].name.Split('_')[0] : "";
            int j = i;
            while (j < list.Count && list[j] != null && list[j].name.Split('_')[0] == f) j++;
            if (j == i) j = i + 1;
            var c = new int[j - i];
            for (int q = 0; q < c.Length; q++) c[q] = i + q;
            chains.Add(c);
            i = j;
        }
    }

    /// <summary>Şekle göre kavrama: her parmak zincirinde eklemler sırayla açık pozdan kapalı poza doğru (6 adım) kapanır;
    /// bu eklemden sonraki eklemler / parmak ucu eşyanın yüzeyine değerse (işaretli uzaklık &lt; parmak kalınlığı) orada durur.</summary>
    private void AdaptFingers(List<Transform> list, List<int[]> chains, Quaternion[] open, Quaternion[] closed, HoldShape shape,
                              Vector3 origin, Quaternion irot, float rad, float w)
    {
        if (open == null || closed == null || open.Length != list.Count || closed.Length != list.Count) return;
        if (fingerTmp == null || fingerTmp.Length < list.Count) fingerTmp = new Quaternion[list.Count];
        for (int i = 0; i < list.Count; i++) fingerTmp[i] = list[i] != null ? list[i].localRotation : Quaternion.identity;
        Quaternion inv = Quaternion.Inverse(irot);
        foreach (int[] c in chains)
        {
            foreach (int j in c) if (list[j] != null) list[j].localRotation = open[j];
            for (int jj = 0; jj < c.Length; jj++)
            {
                int j = c[jj];
                Transform t = list[j];
                if (t == null) continue;
                float ok = 0f;
                // Açık pozda bile eşyaya değiyorsa açık pozda kalır. (Eskiden geriye doğru -60°'ye kadar açılıyordu: parmak /
                // başparmak ters bükülüp avuç içi şişik, parmaklar kırık görünüyordu.)
                if (FingerHits(list, c, jj, shape, origin, inv, rad)) continue;
                for (int step = 1; step <= 6; step++)
                {
                    float u = step / 6f;
                    t.localRotation = Quaternion.Slerp(open[j], closed[j], u);
                    if (FingerHits(list, c, jj, shape, origin, inv, rad)) { t.localRotation = Quaternion.Slerp(open[j], closed[j], ok); break; }
                    ok = u;
                }
            }
        }
        if (w < 0.999f)
            for (int i = 0; i < list.Count; i++) if (list[i] != null) list[i].localRotation = Quaternion.Slerp(fingerTmp[i], list[i].localRotation, w);
    }

    private static bool FingerHits(List<Transform> list, int[] c, int jj, HoldShape shape, Vector3 origin, Quaternion inv, float rad)
    {
        for (int kk = jj + 1; kk < c.Length; kk++)
        {
            Transform t = list[c[kk]];
            if (t != null && shape.Sdf(inv * (t.position - origin)) < rad) return true;
        }
        // parmak ucu: son eklemden bir boğum ileride
        Transform last = list[c[c.Length - 1]], prev = c.Length > 1 ? list[c[c.Length - 2]] : null;
        if (last != null)
        {
            Vector3 tip = prev != null ? last.position + (last.position - prev.position) * 0.9f : last.position;
            if (shape.Sdf(inv * (tip - origin)) < rad) return true;
            if (prev != null && shape.Sdf(inv * ((tip + last.position) * 0.5f - origin)) < rad) return true;
        }
        return false;
    }

    private static void LegLayer(Bone thigh, Bone shin, Bone foot, Vector3 right, float thighFwd, float knee, float footFix)
    {
        if (thigh == null || shin == null) return;
        if (thighFwd > 0.01f) thigh.t.rotation = Quaternion.AngleAxis(-thighFwd, right) * thigh.t.rotation;
        if (knee > 0.01f) shin.t.rotation = Quaternion.AngleAxis(knee, right) * shin.t.rotation;
        if (foot != null && Mathf.Abs(footFix) > 0.01f) foot.t.rotation = Quaternion.AngleAxis(footFix, right) * foot.t.rotation;
    }

    private PlayerAvatar lookAvatar;
    private float eyeYaw, eyePitch; // gözlerin dinlenme pozundan sapması (derece, yumuşatılmış)

    /// <summary>Konuşan (sesli) oyuncu → yoksa yakındaki oyuncu → yoksa yok. Koşarken yalnızca konuşana bakılır
    /// (koşarken etrafa göz atmak gözleri sürekli oynatıyordu).</summary>
    private void ScanLookTarget()
    {
        nextLookScan = Time.time + 0.25f;
        VoiceChat vc = VoiceChat.Instance;
        Transform speaker = null, near = null;
        float bestS = 144f, bestN = 36f; // 12 m / 6 m
        bool running = speedSmooth > 5.2f;
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p == null || p == netSetup || !p.IsSpawned) continue;
            if (p.TryGetComponent(out PlayerStealth ps) && ps.IsSpawned && (ps.IsHidden || ps.IsJailed)) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (vc != null && vc.IsSpeaking(p.OwnerClientId) && d < bestS) { bestS = d; speaker = p.transform; }
            if (!running && d < bestN) { bestN = d; near = p.transform; }
        }
        if (speaker != null) { speakTarget = speaker; speakUntil = Time.time + 2f; }
        if (speakTarget != null && Time.time > speakUntil) speakTarget = null;
        Transform t = speakTarget != null ? speakTarget : near;
        if (t != lookTarget) lookAvatar = t != null ? t.GetComponent<PlayerAvatar>() : null;
        lookTarget = t;
    }

    /// <summary>Bu karakterin gözlerinin ortası (dünya). Başkaları ona bakarken buraya bakar.</summary>
    public Vector3 EyeWorldPoint()
    {
        if (eyeL != null && eyeR != null) return (eyeL.position + eyeR.position) * 0.5f;
        if (mHead != null) return mHead.t.position + Vector3.up * 0.1f;
        return transform.position + Vector3.up * PlayerController.EyeOffset;
    }

    /// <summary>Bakılacak nokta: hedefin GÖZLERİ (eskiden hedefin kökü + 1,4 m'ye bakılıyordu; kök kapsülün ortasında
    /// olduğu için bu, kafanın ~1 m üstüydü → karakterler birbirinin yüzüne değil havaya bakıyordu).</summary>
    private Vector3 LookPoint()
    {
        if (lookAvatar != null && lookAvatar.isActiveAndEnabled) return lookAvatar.EyeWorldPoint();
        return lookTarget.position + Vector3.up * PlayerController.EyeOffset;
    }

    /// <summary>Bakış: hedef seçimi, bakış yönü ve gövdenin (omurga / göğüs) hedefe doğru hafifçe dönmesi.
    /// Kol IK'sından önce çalışır (eller sonradan kendi noktalarına oturur).</summary>
    private void LookBody(float dt, float nk)
    {
        if (mHead == null) return;
        Transform head = mHead.t;
        Vector3 up = root.up, fwd = root.forward;
        if (Time.time >= nextLookScan) ScanLookTarget();

        bool look = false;
        Vector3 dir = head.rotation * headFwdLocal;
        if (lookTarget != null)
        {
            dir = (LookPoint() - head.position).normalized;
            look = Vector3.Angle(fwd, dir) < 110f;
        }
        float k = 1f - Mathf.Exp(-6f * dt);
        lookW = Mathf.Lerp(lookW, look ? nk : 0f, k);
        if (look) lookDir = Vector3.Slerp(lookDir, dir, k).normalized;
        // Gövde: kafa döndükten SONRA, yavaşça ve az (hedef yandaysa bir miktar o yana döner)
        bodyW = Mathf.Lerp(bodyW, look && lookW > 0.6f ? nk : 0f, 1f - Mathf.Exp(-1.8f * dt));
        float wantYaw = 0f;
        if (look)
        {
            Vector3 flat = Vector3.ProjectOnPlane(lookDir, up);
            if (flat.sqrMagnitude > 1e-4f)
            {
                float a = Vector3.SignedAngle(fwd, flat, up);
                wantYaw = Mathf.Sign(a) * Mathf.Clamp((Mathf.Abs(a) - 25f) * 0.35f, 0f, 22f);
            }
        }
        bodyYaw = Mathf.Lerp(bodyYaw, wantYaw * bodyW, 1f - Mathf.Exp(-3f * dt));
        if (Mathf.Abs(bodyYaw) > 0.05f)
        {
            if (mSpine != null) mSpine.t.rotation = Quaternion.AngleAxis(bodyYaw * 0.6f, up) * mSpine.t.rotation;
            if (mChest != null) mChest.t.rotation = Quaternion.AngleAxis(bodyYaw * 0.4f, up) * mChest.t.rotation;
        }
    }

    /// <summary>Bakış: kafa (ve boyun) hedefe döner, gözler kalan açıyı kapatır. Kimseye bakmıyorken gözler HER KARE
    /// dinlenme pozunda tutulur (yürüme / koşu kliplerindeki göz hareketi ve küçük titremeler görünmez).</summary>
    private void LookHead(float dt, float nk)
    {
        if (mHead == null) return;
        Transform head = mHead.t;
        Vector3 up = root.up;
        Vector3 headFwd = head.rotation * headFwdLocal;

        // Kendi bakışı (yukarı / aşağı): kimseye bakmıyorken kafa oyuncunun baktığı yöne eğilir (diğerleri nereye baktığını görür)
        float pitch = netSetup != null && netSetup.IsSpawned ? Mathf.Clamp(netSetup.LookPitch, -40f, 40f) : 0f;
        Vector3 right = Vector3.Cross(up, headFwd).normalized;
        if (right.sqrMagnitude < 0.5f) right = root.right;
        float selfPitch = pitch * 0.55f * (1f - lookW) * nk;
        if (Mathf.Abs(selfPitch) > 0.05f) head.rotation = Quaternion.AngleAxis(selfPitch, right) * head.rotation;

        float wantYaw = 0f, wantPit = 0f;
        if (lookW >= 0.001f)
        {
            // Kafa önce ve belirgin döner (kalan açının %60'ı, en çok 45°), gözler gerisini kapatır
            headFwd = head.rotation * headFwdLocal;
            Quaternion full = Quaternion.FromToRotation(headFwd, lookDir);
            full.ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            float headAng = Mathf.Clamp(ang * 0.6f, -45f, 45f) * lookW;
            if (upperNeck != null) upperNeck.rotation = Quaternion.AngleAxis(headAng * 0.35f, axis) * upperNeck.rotation;
            head.rotation = Quaternion.AngleAxis(headAng * (upperNeck != null ? 0.65f : 1f), axis) * head.rotation;

            headFwd = head.rotation * headFwdLocal;
            Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(headFwd, up)) * lookDir;
            wantYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -5.5f, 5.5f) * lookW;
            wantPit = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -6f, 6f) * lookW;
        }
        // Gözler yumuşak hareket eder (adım sallanmasıyla titremez)
        float ek = 1f - Mathf.Exp(-10f * dt);
        eyeYaw = Mathf.Lerp(eyeYaw, wantYaw, ek);
        eyePitch = Mathf.Lerp(eyePitch, wantPit, ek);
        Vector3 side = Vector3.Cross(up, head.rotation * headFwdLocal).normalized;
        if (side.sqrMagnitude < 0.5f) side = root.right;
        Quaternion eyeRot = Quaternion.AngleAxis(eyeYaw, up) * Quaternion.AngleAxis(-eyePitch, side);
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
        if (kind == 3) { point = smallPalmOk ? smallPalm : (gripR != null ? gripR.position : mHandR.t.position); return true; } // küçük eşya: avuçta
        // Bu karede zaten hesaplandıysa aynısı (eller ve eşya aynı noktayı kullanır)
        if (carryFrame == Time.frameCount && carryKindLast == kind) { point = transform.TransformPoint(carryLocal); return true; }
        Vector3 fwd = body.forward, right = body.right;
        float k = armReach / 0.44f;
        // Üst kenar göz hizasının biraz altında (birinci şahısta düz bakınca ekranın altında görünür; dışarıdan çene / ağız hizası)
        // Tablo: kendi ekranında göz hizasına yakın (ekranda görünsün); diğerlerinin ekranında biraz daha aşağıda, göğüs
        // önünde (yüz açıkta kalır, dışarıdan doğal durur). Her bilgisayar kendi hesabını yapar; eller her ikisinde tabloda.
        bool ownView = netSetup != null && netSetup.IsSpawned && netSetup.IsOwner && !previewOn;
        float top = EyeY() - (kind == 1 ? (ownView ? 0.14f : 0.24f) : 0.17f) * k;
        // Vazo / büst kalın: üst kenarı çenenin altında (iri kafanın / burnun önüne girmesin), kollar öne uzanmış
        if (kind == 2 && mHead != null) top = Mathf.Min(top, mHead.t.position.y - 0.03f * k);
        point = new Vector3(0f, top - hh * 0.85f, 0f);
        if (mHips != null) point.y = Mathf.Max(point.y, mHips.t.position.y + 0.05f);
        Vector3 shL = mUpArmL != null ? mUpArmL.t.position : transform.position, shR = mUpArmR != null ? mUpArmR.t.position : transform.position;
        Vector3 sh = (shL + shR) * 0.5f;
        // Ne kadar önde: bilekler (tabloda arka yüzün kenarlarına, büyük eşyada yanlarının alt yarısına) rahat yetişsin
        float half = Mathf.Min(hw + 0.02f, armReach * 0.85f), shHalf = Vector3.Distance(shL, shR) * 0.5f;
        float wristLat = kind == 1 ? half * 0.82f : half, wristDown = kind == 1 ? (EdgeGrip(heldView != null ? heldView.Shape : null) ? -Mathf.Min(hh * 0.15f, 0.05f * k) : Mathf.Min(hh * 0.3f, 0.1f * k)) : hh * 0.45f;
        HoldShape shape = heldView != null ? heldView.Shape : null;
        gripY = -hh * 0.45f;
        bool edge = kind == 1 && EdgeGrip(shape);
        if (edge) wristLat = shape.BoxHalf.x + 0.03f * k; // avuçlar yan kenarlarda
        if (kind == 2 && shape != null && shape.HasBase && heldView.IsBust)
        {
            // Büst: alttaki dikdörtgen kaideden tutulur (eller kaidenin iki yan yüzünde)
            gripY = shape.BaseCenter.y;
            wristDown = -gripY;
            wristLat = Mathf.Min(shape.BaseHalf.x + 0.03f * k, armReach * 0.85f);
        }
        else if (kind == 2 && shape != null && !shape.IsBox)
        {
            // Şekle göre: alt-orta bölgede iki yanın en geniş olduğu yükseklik (vazonun karnı, büstün omuz altı)
            Quaternion yr = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y + heldView.ShownYaw, 0f));
            Vector3 dr = yr * right, dlft = yr * -right;
            float aR = Mathf.Atan2(dr.x, dr.z), aL = Mathf.Atan2(dlft.x, dlft.z);
            gripY = shape.BestGripY(aR, aL, 0.35f, 0.78f);
            wristDown = -gripY;
            wristLat = Mathf.Min((shape.Radius(gripY, aR) + shape.Radius(gripY, aL)) * 0.5f + 0.03f * k, armReach * 0.85f);
        }
        float dx = wristLat - shHalf, dy = point.y - wristDown - sh.y, reach = armReach * (kind == 2 ? 0.92f : 0.88f); // kollar tam gerilmez: eller kenardan kopmaz
        float dz = Mathf.Sqrt(Mathf.Max(0.0025f, reach * reach - dx * dx - dy * dy));
        float ahead = Mathf.Max(dz + (kind == 1 && !edge ? 0.05f * k : 0f), kind == 1 ? 0.34f * k : hw + 0.2f * k); // büyük eşya gövdeden uzakta (göbeğe girmesin, ekranda ortada)
        point = new Vector3(sh.x, point.y, sh.z) + fwd * ahead;
        // Bakışla birlikte omuzdan biraz aşağı / yukarı (aşağı bakınca biraz iner, yine ekranda kalır)
        carryTilt = Mathf.Clamp(armPitch, 0f, 55f) * 0.12f * Mathf.Clamp01(holdSmooth); // yukarı bakınca eşya kalkmaz (yüzüne / kameraya girmesin)
        if (Mathf.Abs(carryTilt) > 0.01f) point = sh + Quaternion.AngleAxis(carryTilt, right) * (point - sh);
        // Üst kenar hiçbir zaman göz hizasına yaklaşmasın (kamera eşyanın / vazonun içine girmesin)
        float topMax = EyeY() - 0.12f * k;
        if (point.y + hh * 0.85f > topMax) point.y = topMax - hh * 0.85f;
        // Sabit tut: nefes alma / adım sallanması eşyayı oynatmasın (oyuncuya göre yumuşatılır; kollar IK ile eşyayı izler)
        Vector3 local = transform.InverseTransformPoint(point);
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        if (carryKindLast != kind || (local - carryLocal).sqrMagnitude > 0.0625f) carryLocal = local;
        else carryLocal = Vector3.Lerp(carryLocal, local, 1f - Mathf.Exp(-5f * dt));
        carryFrame = Time.frameCount; carryKindLast = kind;
        point = transform.TransformPoint(carryLocal);
        if (heldView != null) point += Vector3.up * heldView.TurnLift; // [R] ile çevirirken eşya ve eller hafifçe kalkıp iner
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
