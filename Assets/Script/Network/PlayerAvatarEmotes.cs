using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Emote'lar (T çarkı — EmoteWheel). Karakterin animasyonu klip örnekleme + kodla katmanlarla yapıldığı için (Animator yok)
/// emote'lar da LateAnimate'te, el / kol katmanlarının ÜSTÜNE kodla kurulan bir katmandır: kol IK ile hedefe gider, el
/// yönlenir, parmaklar avuca doğru bükülür (Blender'da her poz önden / yandan / üstten denendi).
///
/// Neden klip yok: Blender'daki "El_Kapali" klibinin dönüşleri çoğunlukla parmağın kendi ekseni etrafında; tam yumruk
/// olmuyor. Bu yüzden yumruk, işaret parmağı, OK halkası, orta parmak ve başparmak kodla bükülür.
///
/// Her istemci aynı emote'u kendi ekranında oynatır (EmoteWheel ağ mesajıyla başlatır); süre ve poz tamamen
/// zamandan hesaplanır, ağ trafiği yalnızca başlangıç mesajıdır.
/// </summary>
public partial class PlayerAvatar
{
    private int emoteId = -1;
    private float emoteStart, emoteDur;

    /// <summary>Şu an bir emote oynuyor mu (bitiş solması dahil)?</summary>
    public bool IsEmoting => emoteId >= 0 && Time.time - emoteStart < emoteDur;

    /// <summary>Emote'u başlat (EmoteWheel çağırır; ağdan gelen de yerelde basılan da).</summary>
    public void PlayEmote(int id)
    {
        EmoteDef d = EmoteCatalog.Get(id);
        if (d == null) return;
        emoteId = id;
        emoteStart = Time.time;
        emoteDur = d.Duration;
    }

    public void StopEmote() => emoteId = -1;

    /// <summary>Eller dolu / baygın: emote oynamaz (eşya elden ayrı kalmasın).</summary>
    public bool HandsBusyForEmote => CarryKindNow() != 0 || brushHold || knockSmooth > 0.3f;

    private float EmoteWeight(float t)
    {
        float inW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.25f));
        float outW = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - (emoteDur - 0.35f)) / 0.35f));
        return inW * outW;
    }

    // Emote kol tabanı: Blender'daki "Firca" klibinin sağ kol duruşu (omuz + burulma kemikleri dahil). Kodla hesaplanan
    // kol açıları gömleğin kolunu katlıyor / burguluyordu (omuzda koyu kırışık, dirsekte X); tasarlanmış bu duruşta kumaş
    // pürüzsüz kalıyor. Öne uzanan emote'lar bu duruştan başlar, üstüne yalnızca küçük IK / bilek / parmak ayarı yapılır.
    private Transform[] emoteArmBones;
    private Quaternion[] emoteArmRot;
    private Transform upTwistR, loTwistR;

    private void SetupEmotePose(Dictionary<string, Transform> bones)
    {
        emoteArmBones = null;
        bones.TryGetValue("UpperArmTwist.R", out upTwistR);
        bones.TryGetValue("LowerArmTwist.R", out loTwistR);
        if (!clipMap.TryGetValue("Firca", out AnimationClip fc) || fc == null) return;
        var list = new List<Transform>();
        foreach (string n in new[] { "Shoulder.R", "UpperArm.R", "UpperArmTwist.R", "LowerArm.R", "LowerArmTwist.R", "Hand.R" })
            if (bones.TryGetValue(n, out Transform t) && t != null) list.Add(t);
        if (list.Count < 3) return;
        fc.SampleAnimation(animRoot, 0f);
        emoteArmBones = list.ToArray();
        emoteArmRot = new Quaternion[emoteArmBones.Length];
        for (int i = 0; i < emoteArmBones.Length; i++) emoteArmRot[i] = emoteArmBones[i].localRotation;
        if (clipMap.TryGetValue(cIdle, out AnimationClip ic) && ic != null) ic.SampleAnimation(animRoot, 0f);
    }

    /// <summary>LateAnimate: el / kol katmanlarından sonra, kafadan önce.</summary>
    private void EmoteLayer(float nk)
    {
        if (emoteId < 0) return;
        float t = Time.time - emoteStart;
        if (t >= emoteDur) { emoteId = -1; return; }
        if (HandsBusyForEmote) { emoteId = -1; return; }
        float w = EmoteWeight(t) * nk;
        if (w < 0.005f) return;
        if (mUpArmR == null || mLoArmR == null || mHandR == null || midFingerR == null) return;

        Vector3 fwd = body.forward, right = body.right, up = Vector3.up;
        float R = armReach, s = root.lossyScale.y;

        switch ((EmoteId)emoteId)
        {
            case EmoteId.ThumbsUp:
            {
                if (!FircaArm(w)) break;
                float pop = Mathf.Sin(Mathf.Clamp01((t - 0.15f) / 0.5f) * Mathf.PI) * 0.03f; // gösterirken hafif öne
                KeepIK(mHandR.t.position + (up * 0.05f + right * 0.16f + fwd * pop) * s, w); // gövdenin dışında: karşıdan silüette okunur
                TwistTo(up, 0.3f, 0.6f, w);
                OrientHandTo(mHandR, midFingerR, thumbR, (fwd * 0.9f + up * 0.1f).normalized, up, w);
                FingersToRest(fingersR, w);
                Fist(false, null, w);
                ThumbStraight(false, s, w);
                break;
            }
            case EmoteId.ThumbsDown:
            {
                if (!FircaArm(w)) break;
                float drop = t > 0.3f && t < 1.3f ? Mathf.Max(0f, Mathf.Sin((t - 0.3f) * Mathf.PI * 2f)) * 0.03f : 0f; // iki kez aşağı sallar
                KeepIK(mHandR.t.position + (right * 0.16f - up * drop) * s, w);
                TwistTo(-up, 0f, 0.5f, w);
                OrientHandTo(mHandR, midFingerR, thumbR, (fwd * 0.85f - up * 0.2f).normalized, -up, w);
                FingersToRest(fingersR, w);
                Fist(false, null, w);
                ThumbStraight(false, s, w);
                break;
            }
            case EmoteId.MiddleFinger:
            {
                if (!FircaArm(w)) break;
                // İki kez öne doğru itiş; elin sırtı karşıya, orta parmak yukarı
                float jab = t > 0.35f && t < 1.6f ? Mathf.Max(0f, Mathf.Sin((t - 0.35f) * Mathf.PI * 3.2f)) : 0f;
                KeepIK(mHandR.t.position + (up * 0.05f + right * 0.16f + fwd * (0.04f * jab)) * s, w);
                // Elin SIRTI karşıya (avuç kendine dönük): karşıdaki bükük parmak uçlarını değil, boğumları görür
                TwistTo(right, 0.3f, 0.6f, w);
                OrientHandTo(mHandR, midFingerR, thumbR, (up * 0.85f + fwd * 0.25f).normalized, right, w);
                FingersToRest(fingersR, w);
                Curl(false, "Index", FistP, FistI, FistD, w);
                Curl(false, "Ring", FistP, FistI, FistD, w);
                Curl(false, "Little", FistP, FistI, FistD, w);
                ThumbOver(w);
                break;
            }
            case EmoteId.PointLaugh:
            {
                // Gövde gülmeyle sarsılır (kendi birinci şahsında değil: yaka kameraya girmesin). Önce gövde, sonra kol.
                float lw = w * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.35f) / 0.3f));
                float bw = OwnFirstPersonView ? 0f : lw;
                float shake = Mathf.Sin(t * 17f);
                if (mSpine != null) mSpine.t.rotation = Quaternion.AngleAxis((4f + 3f * shake) * bw, right) * mSpine.t.rotation;
                if (mChest != null) mChest.t.rotation = Quaternion.AngleAxis(2.5f * shake * bw, right) * mChest.t.rotation;
                if (!FircaArm(w)) break;
                TwistTo(up, 0.3f, 0.6f, w);
                OrientHandTo(mHandR, midFingerR, thumbR, fwd, up, w);
                FingersToRest(fingersR, w);
                Fist(false, "Index", w);
                ThumbOver(w);
                break;
            }
            case EmoteId.Wave:
            case EmoteId.Ok:
            {
                // El başın yanında, avuç karşıya (el sallama duruşu). Üst kol 55° dışa döner: koltuk altının önündeki
                // koyu kırışık kalkar (Blender'da 0 / ±35 / 55° denendi).
                bool wave = emoteId == (int)EmoteId.Wave;
                float sw = wave ? Mathf.Sin(Mathf.Max(0f, t - 0.2f) * 9f) : 0f;
                Vector3 tgt = mUpArmR.t.position + up * (0.78f * R) + right * ((0.30f + 0.05f * sw) * R) + fwd * (0.12f * R);
                Quaternion tilt = Quaternion.AngleAxis(sw * 20f, fwd);
                Vector3 wf = tilt * up, wt = tilt * -right;
                EmoteArm(false, tgt, wf, wt, 0.6f, w);
                ExternalTwist(55f, w);
                EmoteArm(false, tgt, wf, wt, 0.6f, w);
                FingersToRest(fingersR, w);
                if (wave)
                {
                    Curl(false, "Index", 6f, 6f, 4f, w);
                    Curl(false, "Middle", 6f, 6f, 4f, w);
                    Curl(false, "Ring", 6f, 6f, 4f, w);
                    Curl(false, "Little", 6f, 6f, 4f, w);
                }
                else
                {
                    Curl(false, "Index", 40f, 50f, 35f, w);
                    Curl(false, "Middle", 8f, 8f, 8f, w);
                    Curl(false, "Ring", 12f, 12f, 12f, w);
                    Curl(false, "Little", 16f, 16f, 16f, w);
                    AimThumb(false, FingerTip(false, "Index"), w);
                }
                break;
            }
        }
    }

    /// <summary>Sağ kolu Firca duruşuna getir (w ile). Duruş yoksa false (emote kolu oynatılmaz).</summary>
    private bool FircaArm(float w)
    {
        if (emoteArmBones == null) return false;
        for (int i = 0; i < emoteArmBones.Length; i++)
            emoteArmBones[i].localRotation = Quaternion.Slerp(emoteArmBones[i].localRotation, emoteArmRot[i], w);
        return true;
    }

    /// <summary>İki kemikli IK; dirseğin ŞU ANKİ bükülme düzlemi korunur (Firca duruşundan küçük oynatmalar kumaşı bozmaz).</summary>
    private void KeepIK(Vector3 target, float w)
    {
        Bone upper = mUpArmR, lower = mLoArmR, hand = mHandR;
        Vector3 S = upper.t.position, E = lower.t.position, W = hand.t.position;
        float a = Vector3.Distance(S, E), b = Vector3.Distance(E, W);
        if (a < 1e-4f || b < 1e-4f) return;
        Vector3 cur = (W - S).normalized;
        Vector3 bend = Vector3.ProjectOnPlane(E - S, cur);
        Vector3 toT = target - S;
        float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
        Vector3 dir = toT.normalized;
        bend = Vector3.ProjectOnPlane(bend.sqrMagnitude > 1e-8f ? bend : Vector3.down, dir);
        if (bend.sqrMagnitude < 1e-8f) return;
        bend.Normalize();
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
        Vector3 elbow = S + dir * (a * cosA) + bend * (a * Mathf.Sqrt(1f - cosA * cosA));
        Vector3 wrist = S + dir * d;
        upper.t.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(E - S, elbow - S), w) * upper.t.rotation;
        Vector3 E2 = lower.t.position, W2 = hand.t.position;
        lower.t.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(W2 - E2, wrist - E2), w) * lower.t.rotation;
    }

    /// <summary>
    /// Başparmak tarafını wantThumb'a çevirmek için gereken burulmayı üst kol burulma kemiği (kUp) ve önkol burulma
    /// kemiği (kLo) arasında paylaştır; kalanını bilek (OrientHandTo) yapar. Tek eklem aşırı burulmaz.
    /// </summary>
    private void TwistTo(Vector3 wantThumb, float kUp, float kLo, float w)
    {
        if (kUp > 0f && upTwistR != null)
        {
            Vector3 ua = (mLoArmR.t.position - mUpArmR.t.position).normalized;
            Vector3 fa = (mHandR.t.position - mLoArmR.t.position).normalized;
            Vector3 c = Vector3.ProjectOnPlane(ThumbSide(mHandR, thumbR), fa), wt = Vector3.ProjectOnPlane(wantThumb, fa);
            if (c.sqrMagnitude > 1e-8f && wt.sqrMagnitude > 1e-8f)
                upTwistR.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(c, wt, fa) * kUp * w, ua) * upTwistR.rotation;
        }
        if (kLo > 0f)
        {
            Transform lt = loTwistR != null ? loTwistR : mLoArmR.t;
            Vector3 fa = (mHandR.t.position - mLoArmR.t.position).normalized;
            Vector3 c = Vector3.ProjectOnPlane(ThumbSide(mHandR, thumbR), fa), wt = Vector3.ProjectOnPlane(wantThumb, fa);
            if (c.sqrMagnitude > 1e-8f && wt.sqrMagnitude > 1e-8f)
                lt.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(c, wt, fa) * kLo * w, fa) * lt.rotation;
        }
    }

    /// <summary>Üst kolu kendi ekseni etrafında dışa döndür (kol yukarıdayken el geriye gider; işaret bundan seçilir).</summary>
    private void ExternalTwist(float deg, float w)
    {
        if (upTwistR == null) return;
        Vector3 S = mUpArmR.t.position, H = mHandR.t.position;
        Vector3 ua = (mLoArmR.t.position - S).normalized;
        Quaternion qa = Quaternion.AngleAxis(deg * w, ua), qb = Quaternion.AngleAxis(-deg * w, ua);
        Vector3 fwd = body.forward;
        Quaternion q = Vector3.Dot(qa * (H - S) - (H - S), fwd) < Vector3.Dot(qb * (H - S) - (H - S), fwd) ? qa : qb;
        upTwistR.rotation = q * upTwistR.rotation;
    }

    private void ThumbOver(float w)
    {
        Transform ii = Finger(fingersR, "Index_Intermediate"), mi = Finger(fingersR, "Middle_Intermediate");
        if (ii != null && mi != null) AimThumb(false, ii.position * 0.6f + mi.position * 0.4f, w);
    }

    /// <summary>LookHead'den sonra: gülerken kafa geriye düşer ve sarsılır.</summary>
    private void EmoteHeadLayer(float nk)
    {
        if (emoteId != (int)EmoteId.PointLaugh || mHead == null) return;
        float t = Time.time - emoteStart;
        if (t >= emoteDur) return;
        float lw = EmoteWeight(t) * nk * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.35f) / 0.3f));
        if (lw < 0.005f) return;
        Transform head = mHead.t;
        Vector3 f = head.rotation * headFwdLocal;
        Vector3 axis = Vector3.Cross(f, Vector3.up);
        if (axis.sqrMagnitude < 1e-4f) return;
        float ang = (12f + 4f * Mathf.Sin(Time.time * 17f)) * lw;
        Quaternion back = Quaternion.AngleAxis(ang, axis.normalized);
        // Doğru yöne mi (burun yukarı kalkmalı): değilse ters çevir
        if (Vector3.Dot(back * f, Vector3.up) < Vector3.Dot(f, Vector3.up)) back = Quaternion.Inverse(back);
        head.rotation = back * head.rotation;
    }

    // ---------------------------------------------------------------- yardımcılar

    /// <summary>Kolu hedefe götür (iki geçiş), önkolu başparmak yönüne çevir, eli yönlendir. left: sol kol.</summary>
    private void EmoteArm(bool left, Vector3 target, Vector3 wantFinger, Vector3 wantThumb, float roll, float w)
    {
        Bone ua = left ? mUpArmL : mUpArmR, la = left ? mLoArmL : mLoArmR, hd = left ? mHandL : mHandR;
        Transform mid = left ? midFingerL : midFingerR, th = left ? thumbL : thumbR;
        if (ua == null || la == null || hd == null || mid == null) return;
        for (int pass = 0; pass < 2; pass++)
        {
            ArmIK(ua, la, hd, target, left ? -1f : 1f, w);
            Vector3 fa = (hd.t.position - la.t.position).normalized;
            Vector3 ct0 = Vector3.ProjectOnPlane(ThumbSide(hd, th), fa), wt0 = Vector3.ProjectOnPlane(wantThumb, fa);
            if (ct0.sqrMagnitude > 1e-6f && wt0.sqrMagnitude > 1e-6f)
                la.t.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(ct0, wt0, fa) * roll * w, fa) * la.t.rotation;
            OrientHandTo(hd, mid, th, wantFinger.normalized, wantThumb.normalized, w);
        }
    }

    private void FingersToRest(List<Transform> list, float w)
    {
        foreach (Transform f in list)
            if (f != null && fingerRest.TryGetValue(f, out Quaternion r)) f.localRotation = Quaternion.Slerp(f.localRotation, r, w);
    }

    private static Transform Finger(List<Transform> list, string prefix)
    {
        foreach (Transform t in list) if (t != null && t.name.StartsWith(prefix)) return t;
        return null;
    }

    /// <summary>Elin yan ekseni (işaret kökü − serçe kökü), birim.</summary>
    private Vector3 HandLateral(bool left)
    {
        Transform i = left ? idxL : idxR, l = left ? litL : litR;
        return i != null && l != null ? (i.position - l.position).normalized : (left ? -body.right : body.right);
    }

    /// <summary>Avucun baktığı yön: parmak ekseni × yan eksen; başparmak kökü avuç tarafında olduğu için işaret ondan.</summary>
    private Vector3 PalmNormal(bool left)
    {
        Bone hd = left ? mHandL : mHandR;
        Transform mid = left ? midFingerL : midFingerR, th = left ? thumbL : thumbR;
        Vector3 fd = (mid.position - hd.t.position).normalized;
        Vector3 n = Vector3.Cross(fd, HandLateral(left)).normalized;
        if (th != null && Vector3.Dot(n, th.position - hd.t.position) < 0f) n = -n;
        return n;
    }

    /// <summary>Bir parmağın üç boğumunu avuca doğru bük (derece, w ile).</summary>
    private void Curl(bool left, string finger, float a0, float a1, float a2, float w)
    {
        List<Transform> list = left ? fingersL : fingersR;
        Transform p = Finger(list, finger + "_Proximal"), m = Finger(list, finger + "_Intermediate"), d = Finger(list, finger + "_Distal");
        if (p == null || m == null) return;
        // Menteşe ekseni (El_Kavra'dan okunan) varsa her boğum kendi ekseninde bükülür: gerçek eklem gibi
        if (fingerHinge.TryGetValue(p, out Vector3 h0) && fingerHinge.TryGetValue(m, out Vector3 h1))
        {
            p.localRotation = p.localRotation * Quaternion.AngleAxis(a0 * w, h0);
            m.localRotation = m.localRotation * Quaternion.AngleAxis(a1 * w, h1);
            if (d != null && fingerHinge.TryGetValue(d, out Vector3 h2)) d.localRotation = d.localRotation * Quaternion.AngleAxis(a2 * w, h2);
            return;
        }
        Vector3 lat = HandLateral(left), pn = PalmNormal(left);
        Vector3 v = (m.position - p.position).normalized;
        float sg = Vector3.Dot(Quaternion.AngleAxis(17f, lat) * v, pn) > Vector3.Dot(Quaternion.AngleAxis(-17f, lat) * v, pn) ? 1f : -1f;
        p.rotation = Quaternion.AngleAxis(sg * a0 * w, lat) * p.rotation;
        m.rotation = Quaternion.AngleAxis(sg * a1 * w, lat) * m.rotation;
        if (d != null) d.rotation = Quaternion.AngleAxis(sg * a2 * w, lat) * d.rotation;
    }

    // Yumruk açıları (kök / orta / uç). SoygunChar'ın tam çözünürlüklü eliyle (Ek 82) Blender'da yuvarlak, ezilmesiz yumruk.
    private const float FistP = 85f, FistI = 95f, FistD = 70f;

    /// <summary>Yumruk: dört parmak (skip hariç) tam bükülür.</summary>
    private void Fist(bool left, string skip, float w)
    {
        foreach (string f in new[] { "Index", "Middle", "Ring", "Little" })
            if (f != skip) Curl(left, f, FistP, FistI, FistD, w);
    }

    /// <summary>Parmak ucu (uç kemiğin ucu yok: son boğumun yönünde ~%90 uzatılır).</summary>
    private Vector3 FingerTip(bool left, string finger)
    {
        List<Transform> list = left ? fingersL : fingersR;
        Transform m = Finger(list, finger + "_Intermediate"), d = Finger(list, finger + "_Distal");
        if (d == null) return m != null ? m.position : (left ? thumbL : thumbR) != null ? (left ? thumbL : thumbR).position : Vector3.zero;
        return m != null ? d.position + (d.position - m.position) * 0.9f : d.position;
    }

    /// <summary>Başparmağı kökünden döndürerek ucunu hedefe götür.</summary>
    private void AimThumb(bool left, Vector3 target, float w)
    {
        Transform tp = left ? thumbL : thumbR;
        if (tp == null || Finger(left ? fingersL : fingersR, "Thumb_Distal") == null) return;
        Vector3 a = FingerTip(left, "Thumb") - tp.position, b = target - tp.position;
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return;
        tp.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(a, b), w) * tp.rotation;
    }

    /// <summary>Başparmak yumruktan dümdüz dışarı (yan eksen yönünde): başparmak yukarı / aşağı.</summary>
    private void ThumbStraight(bool left, float s, float w)
    {
        Transform tp = left ? thumbL : thumbR;
        Bone hd = left ? mHandL : mHandR;
        Transform mid = left ? midFingerL : midFingerR;
        if (tp == null || hd == null || mid == null) return;
        Vector3 fd = (mid.position - hd.t.position).normalized;
        AimThumb(left, tp.position + HandLateral(left) * (0.12f * s) + fd * (0.02f * s), w);
    }
}
