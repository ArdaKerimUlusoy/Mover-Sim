using UnityEngine;

/// <summary>
/// Elde tutulan eşyanın görüntüsü. VARSAYILAN: GERÇEKTEN ELDE — karakterin iki elinin arasında durur
/// (diğer oyuncular karakterin eserle yürüdüğünü görür); kendi ekranında ise ekranın altında, iki eldivenli elinle
/// tutuyormuş gibi görünür. (Eski R.E.P.O tarzı süzülme: inHands = false.)
/// Eski açıklama — R.E.P.O'daki gibi: ele yapışık değil, oyuncunun ÖNÜNDE süzülür.
///  - Yay + sönüm ile hedef noktaya çekilir: dönünce geriden gelir, ağır eserler daha hantal izler
///  - Yürürken ve dönerken ataletle yana yatar, hafifçe salınır
///  - Duvara girmez: önünde engel varsa oyuncuya doğru çekilir
///  - Tekerlek = yaklaştır / uzaklaştır, R = çevir (PlayerInventory yazar, herkese senkron)
/// Eşya gerçekte ÇANTADADIR (sunucuda); bu sadece görüntüdür, bu yüzden çoğalma / sahiplik sorunu olmaz.
/// Her bilgisayar aynı simülasyonu zaten senkron olan verilerle (oyuncu konumu, bakış eğimi, uzaklık) çalıştırır.
/// PlayerInventory tarafından otomatik eklenir.
/// </summary>
public class HeldItemView : MonoBehaviour
{
    private const float Stiffness = 150f;
    private const float Damping = 14f;

    private PlayerInventory inventory;
    private PlayerNetworkSetup netSetup;
    private PlayerStealth stealth;
    private Transform cam;

    private GameObject visual;
    private float mass = 1f;
    private float radius = 0.2f;
    private Vector3 pos, vel;
    private Quaternion rot = Quaternion.identity;
    private Vector3 lastRootPos;
    private float lastRootYaw;
    private Vector3 smoothedMove;
    private float smoothedTurn;
    private bool snap;

    public bool IsShowing => visual != null;
    /// <summary>Elde gösterilen eşya görüntüsü (yoksa null).</summary>
    public GameObject VisualObject => visual;
    /// <summary>Eşyanın yarı genişliği (m): karakterin elleri bu kadar iki yanından kavrar.</summary>
    public float HalfWidth => visualHalfWidth;
    /// <summary>Eşyanın yarı yüksekliği (m).</summary>
    public float HalfHeight => visualHalfHeight;
    /// <summary>Nasıl taşınır (karakter animasyonu): 1 tablo (iki elle göğüs önünde), 2 büyük eşya (iki elle), 3 küçük eşya (sağ elde).</summary>
    public int CarryKind { get; private set; } = 2;

    [Tooltip("Açık: eşya karakterin elinde (kendi ekranında elinle tutarken). Kapalı: eski R.E.P.O tarzı önde süzülme.")]
    [SerializeField] private bool inHands = true;
    private PlayerAvatar avatar;
    private float shownYaw;
    /// <summary>Eşyanın gösterilen dönüşü (R ile çevirme dahil, derece). Karakter parmakları eşyanın şekline bu çerçevede oturtur.</summary>
    public float ShownYaw => shownYaw;
    /// <summary>Bozuk para mı (yazı-turada parmaklar parayı izlemez).</summary>
    public bool IsCoin => isCoin;
    /// <summary>Eşyanın şekli (tutuş: eller ve parmaklar yüzeye oturur). Yoksa null.</summary>
    public HoldShape Shape { get; private set; }
    /// <summary>Yazı-tura sürüyor mu.</summary>
    public bool CoinFlipping => coinFlipT >= 0f;
    /// <summary>Yüzük mü (işaret ve başparmak uçları arasında, dik tutulur).</summary>
    public bool IsRing { get; private set; }
    /// <summary>Yüzüğün yarıçapı (m): parmaklar bandından tutar, halka parmak uçlarının üstünde.</summary>
    public float RingRadius { get; private set; } = 0.03f;
    /// <summary>Yüzük bandının kalınlığı (yarıçap, m): kalın yüzükte işaret ile başparmak bu kadar aralanır.</summary>
    public float RingTube { get; private set; } = 0.004f;
    /// <summary>Büst mü (alttaki dikdörtgen kaidesinden tutulur).</summary>
    public bool IsBust { get; private set; }
    /// <summary>[R] ile çevirirken eşyanın kalkma yüksekliği (m; eller de birlikte kalkar).</summary>
    public float TurnLift { get; private set; }
    private float yawVel, turnSpan, lastYawTarget;
    private Quaternion ringFix = Quaternion.identity;
    /// <summary>Ek öne-arkaya eğim (derece): vazo ağzı sana doğru hafif eğik tutulur (içi görünsün).</summary>
    /// <summary>Eşyanın ek öne-arkaya eğimi (derece). Tablo: yalnızca KENDİ ekranında belirgin eğik (aşağı bakınca resmi
    /// görürsün); diğer oyuncuların ekranında neredeyse dik tutulur (dışarıdan yatık / yatay görünmesin). Her bilgisayar
    /// kendi hesabını yaptığı için iki görüntü de kendi içinde tutarlı (eller tablonun kenarında).</summary>
    public float ExtraTilt => CarryKind == 1 ? (IsOwnerView && !PlayerAvatar.PreviewActive ? PaintingTiltOwner : PaintingTiltOthers) : baseTilt;
    private float baseTilt;
    private const float PaintingTiltOwner = 20f, PaintingTiltOthers = 2f;
    /// <summary>Yazı-tura ilerlemesi 0..1 (atış yoksa −1): başparmak fiskesi buna göre.</summary>
    public float CoinFlipProgress => coinFlipT >= 0f ? Mathf.Clamp01(coinFlipT) : -1f;
    private FirstPersonArms fpArms;
    private Transform fpHands, fpHandL, fpHandR, fpSleeveL, fpSleeveR;
    private float visualHalfWidth = 0.2f, visualHalfHeight = 0.2f;
    // Görselin pivotu ile gerçek ortası arasındaki yatay fark (dönmemiş halde). Tablo gibi pivotu arkada / önde olan
    // eşyalar R ile çevrilince ellerin arasından kaymasın diye her zaman ORTASI tutulan noktaya konur.
    private Vector3 centerOff;
    // Bozuk para yazı-tura (birinci şahıs)
    private bool isCoin;
    private float coinFlipT = -1f, lastCoinYaw, nextIdleFlip;
    private const float CoinFlipTime = 0.75f, CoinFlipHeight = 0.2f;
    private float visualHalfThick = 0.02f;
    private static Material gloveMat, sleeveMat, stripeMat;
    /// <summary>Süzülen eşyanın şu anki yeri (bırakırken buradan düşer).</summary>
    public Vector3 Position => pos;
    public Vector3 Velocity => vel;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        netSetup = GetComponent<PlayerNetworkSetup>();
        stealth = GetComponent<PlayerStealth>();
        Camera c = GetComponentInChildren<Camera>(true);
        if (c != null) cam = c.transform;
    }

    /// <summary>Yeni eşyayı göster. visual: pivotu ortada, çarpıştırıcısız görsel (ItemVisuals.BuildFor).</summary>
    public void Show(GameObject newVisual, ItemDefinition def)
    {
        Hide();
        visual = newVisual;
        mass = def != null ? Mathf.Max(0.1f, def.weightKg) : 1f;
        radius = 0.2f;
        bool thin = false;
        if (ItemVisuals.TryGetBounds(visual, out Bounds b))
        {
            float mx = Mathf.Max(b.extents.x, b.extents.y, b.extents.z), mn = Mathf.Min(b.extents.x, b.extents.y, b.extents.z);
            thin = mx > 0.12f && mn < mx * 0.22f; // tuval / çerçeve gibi yassı
            radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * 0.6f, 0.08f, 0.3f);
            visualHalfWidth = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 0.06f, 0.45f);
            visualHalfHeight = Mathf.Clamp(b.extents.y, 0.04f, 0.45f);
        }
        float maxExt = Mathf.Max(visualHalfWidth, visualHalfHeight);
        string id = def != null && def.id != null ? def.id : "";
        if (id.Contains("painting") || id.Contains("tablo") || id.Contains("canvas") || thin) CarryKind = 1;
        else if (id.Contains("ring") || maxExt < 0.13f ||
                 (def != null && (def.IsSmallLoot || def.type == ItemType.Tool || def.type == ItemType.Distraction))) CarryKind = 3;
        else CarryKind = 2;
        // Küçük eşya (bozuk para, yüzük, ganimet, alet) elde gerçek boyunda: dünyadaki görünüşü bilerek büyük (yerde görünsün),
        // elde avuca sığsın (bozuk para ~5,5 cm, diğerleri en çok ~10 cm)
        isCoin = CarryKind == 3 && id.Contains("coin") && !id.Contains("pouch"); // para kesesi yazı-tura atılmaz
        IsRing = CarryKind == 3 && !isCoin && (id.Contains("ring") || id.Contains("yuzuk"));
        coinFlipT = -1f;
        lastCoinYaw = inventory != null ? inventory.HoldYaw : 0f;
        nextIdleFlip = Time.time + Random.Range(3f, 6f);
        if (CarryKind == 3 && visual != null && ItemVisuals.TryGetBounds(visual, out Bounds sb))
        {
            float mx = Mathf.Max(sb.size.x, sb.size.y, sb.size.z);
            float want = isCoin ? 0.055f : IsRing ? 0.08f : 0.1f; // yüzük parmak uçlarında, ekranda rahat görünecek boyda
            if ((mx > want || IsRing) && mx > 0.0001f)
            {
                visual.transform.localScale *= want / mx;
                visualHalfWidth = Mathf.Max(0.02f, visualHalfWidth * want / mx);
                visualHalfHeight = Mathf.Max(0.01f, visualHalfHeight * want / mx);
            }
        }
        // Tablo / vazo / büst elde taşınabilir boyutta (görsel küçültülür): kollar kenarlarına yetişsin, kameraya fazla
        // yaklaşmasın (çok geniş vazonun ağzı birinci şahısta kameranın içinde kalıyordu)
        if ((CarryKind == 1 || CarryKind == 2) && visual != null)
        {
            float maxW = CarryKind == 1 ? 0.36f : 0.18f, maxH = 0.3f;
            float f = Mathf.Min(1f, Mathf.Min(maxW / Mathf.Max(0.01f, visualHalfWidth), maxH / Mathf.Max(0.01f, visualHalfHeight)));
            if (f < 0.999f)
            {
                visual.transform.localScale *= f;
                visualHalfWidth *= f; visualHalfHeight *= f;
            }
        }
        // Yatay orta farkı (dönmemiş halde ölçülür)
        centerOff = Vector3.zero;
        visualHalfThick = 0.02f;
        if (visual != null)
        {
            Quaternion r0 = visual.transform.rotation;
            visual.transform.rotation = Quaternion.identity;
            if (ItemVisuals.TryGetBounds(visual, out Bounds cb))
            {
                Vector3 off = cb.center - visual.transform.position;
                centerOff = new Vector3(off.x, 0f, off.z);
                visualHalfThick = Mathf.Clamp(Mathf.Min(cb.extents.x, cb.extents.y, cb.extents.z), 0.004f, 0.08f);
            }
            visual.transform.rotation = r0;
        }
        ItemVisuals.SetShadows(visual, true);
        IsBust = CarryKind == 2 && def != null && ArtworkLook.KindOf(def) == ArtKind.Bust;
        Shape = visual != null ? HoldShape.Build(visual, centerOff, CarryKind == 1, IsBust) : null;
        // Yüzük: halkanın deliği (en ince ekseni) sana baksın — modelin hangi eksende yattığından bağımsız; yarıçap
        ringFix = Quaternion.identity; RingRadius = visualHalfWidth;
        if (IsRing && Shape != null)
        {
            Vector3 bh = Shape.BoxHalf;
            Vector3 axis = bh.x <= bh.y && bh.x <= bh.z ? Vector3.right : bh.y <= bh.z ? Vector3.up : Vector3.forward;
            ringFix = Quaternion.FromToRotation(axis, Vector3.forward);
            RingRadius = axis == Vector3.right ? Mathf.Max(bh.y, bh.z) : axis == Vector3.up ? Mathf.Max(bh.x, bh.z) : Mathf.Max(bh.x, bh.y);
            // Bandın kalınlığı: halkanın deliği yönündeki yarı kalınlık (torus tüp yarıçapı)
            RingTube = Mathf.Clamp(axis == Vector3.right ? bh.x : axis == Vector3.up ? bh.y : bh.z, 0.0015f, RingRadius * 0.45f);
        }
        // Vazo ağzı hafif, tablo yüzü belirgin sana doğru eğik (resmi görürsün); fazla eğik vazo ağzı yakında oval görünüyordu
        baseTilt = CarryKind == 2 && def != null && ArtworkLook.KindOf(def) == ArtKind.Vase ? -6f : 0f; // tablo eğimi: ExtraTilt
        snap = true;
        lastRootPos = transform.position;
        lastRootYaw = transform.eulerAngles.y;
    }

    public void Hide()
    {
        Shape = null;
        if (visual != null) Destroy(visual);
        visual = null;
        if (fpHands != null) fpHands.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        Hide();
        if (fpHands != null) Destroy(fpHands.gameObject);
    }

    private bool IsOwnerView => netSetup != null && netSetup.IsSpawned && netSetup.IsOwner && cam != null;

    private void LateUpdate()
    {
        if (visual == null) return;

        // Dolapta saklanırken ya da atölyede çalışırken eşya görünmesin
        bool hide = (stealth != null && stealth.IsHidden) || (IsOwnerView && WorkbenchUI.IsOpen);
        if (visual.activeSelf == hide) visual.SetActive(!hide);
        if (hide) { snap = true; SetFpHands(false); return; }

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;

        if (inHands) { UpdateInHands(dt); return; }

        // ---- Göz ve bakış yönü ----
        Vector3 eye, dir;
        Quaternion look;
        if (IsOwnerView)
        {
            eye = cam.position;
            dir = cam.forward;
            look = cam.rotation;
        }
        else
        {
            bool crouch = stealth != null && stealth.IsCrouching;
            float pitch = netSetup != null ? netSetup.LookPitch : 0f;
            eye = transform.TransformPoint(0f, crouch ? 0.1f : 0.6f, 0.15f);
            look = Quaternion.Euler(pitch, transform.eulerAngles.y, 0f);
            dir = look * Vector3.forward;
        }

        // ---- Hedef nokta: önünde, biraz sağda ve aşağıda; duvar varsa geri çekil ----
        float dist = inventory != null ? inventory.HoldDistance : 1.1f;
        if (Physics.SphereCast(eye, radius, dir, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore) &&
            !hit.collider.transform.IsChildOf(transform))
        {
            dist = Mathf.Max(0.45f, hit.distance - 0.03f);
        }
        Vector3 target = eye + dir * dist + look * new Vector3(0.22f, -0.2f, 0f);
        target.y += Mathf.Sin(Time.time * 2.1f) * 0.012f; // havada hafif salınım

        // ---- Oyuncunun hareketi (atalet için) ----
        Vector3 rootMove = (transform.position - lastRootPos) / dt;
        float turn = Mathf.DeltaAngle(lastRootYaw, transform.eulerAngles.y) / dt;
        lastRootPos = transform.position;
        lastRootYaw = transform.eulerAngles.y;
        if (rootMove.sqrMagnitude > 400f) { rootMove = Vector3.zero; snap = true; } // ışınlanma
        smoothedMove = Vector3.Lerp(smoothedMove, transform.InverseTransformDirection(rootMove), 1f - Mathf.Exp(-8f * dt));
        smoothedTurn = Mathf.Lerp(smoothedTurn, turn, 1f - Mathf.Exp(-8f * dt));

        Quaternion targetRot =
            Quaternion.Euler(0f, look.eulerAngles.y, 0f) *
            Quaternion.Euler(Mathf.DeltaAngle(0f, look.eulerAngles.x) * 0.35f, inventory != null ? inventory.HoldYaw : 0f, 0f) *
            Quaternion.Euler(Mathf.Clamp(smoothedMove.z * 2.2f, -14f, 14f), 0f, Mathf.Clamp(-smoothedMove.x * 3f - smoothedTurn * 0.05f, -18f, 18f));

        if (snap || (target - pos).sqrMagnitude > 9f)
        {
            snap = false;
            pos = target;
            vel = Vector3.zero;
            rot = targetRot;
        }
        else
        {
            // Yay + sönüm; ağır eser daha yumuşak (geriden) gelir
            float k = Stiffness / (1f + mass * 0.12f);
            vel += ((target - pos) * k - vel * Damping) * dt;
            pos += vel * dt;
            rot = Quaternion.Slerp(rot, targetRot, 1f - Mathf.Exp(-(11f / (1f + mass * 0.05f)) * dt));
        }

        visual.transform.SetPositionAndRotation(pos, rot);
    }

    // =====================================================================
    //  GERÇEKTEN ELDE
    // =====================================================================

    private void UpdateInHands(float dt)
    {
        if (avatar == null) avatar = GetComponent<PlayerAvatar>();
        // Kendi gövden birinci şahısta görünüyorsa (Soygun) eşyayı diğerlerinin gördüğü gibi kendi ellerin tutar: tek görüntü.
        // Gövde görünmüyorsa (eski karakter / baygın) eski davranış: eşya ekranın altında kendi başına durur.
        bool firstPerson = IsOwnerView && !PlayerAvatar.PreviewActive && !PlayerAvatar.LocalBodyVisible;
        // R: eşya sadece ön / arka çevrilir (karşındakine gösterir). Kısa animasyonla döner, dönerken hafifçe kalkar.
        float yawTarget = inventory != null ? inventory.HoldYaw : 0f;
        // Yumuşak (hızlanıp yavaşlayan) ~0,3 sn'lik dönüş; dönerken eşya kalkıp iner (eller de birlikte) → çevirdiğin belli olur
        if (Mathf.Abs(Mathf.DeltaAngle(yawTarget, lastYawTarget)) > 0.01f)
        {
            lastYawTarget = yawTarget;
            turnSpan = Mathf.Max(1f, Mathf.Abs(Mathf.DeltaAngle(shownYaw, yawTarget)));
        }
        shownYaw = Mathf.SmoothDampAngle(shownYaw, yawTarget, ref yawVel, 0.09f, 900f, dt);
        float rem = Mathf.Abs(Mathf.DeltaAngle(shownYaw, yawTarget));
        if (rem < 0.25f) { shownYaw = yawTarget; yawVel = 0f; rem = 0f; }
        float turning = rem / 180f;     // >0: dönüyor
        float turnPulse = rem > 0f ? Mathf.Sin(Mathf.Clamp01(1f - rem / turnSpan) * Mathf.PI) : 0f;
        float flipLift = turnPulse * 0.06f;
        // Yüzük: [R] hiçbir şey yapmaz (dönmez, kalkmaz). Bozuk para: kendi yazı-tura atışı var, ayrıca kalkmaz.
        if (IsRing) { shownYaw = yawTarget; yawVel = 0f; turning = 0f; flipLift = 0f; }
        else if (isCoin) flipLift = 0f;
        // Tablo / büyük eşya: kalkma karakterin taşıma noktasına eklenir (eller eşyayla birlikte kalkar); küçük eşya avuçta döner
        TurnLift = CarryKind == 1 || CarryKind == 2 ? flipLift * 0.7f : 0f;
        float yaw = shownYaw;
        Vector3 target;
        Quaternion targetRot;

        if (firstPerson)
        {
            // Kendi ekranın: ekranın alt ortasında, iki elinle tutuyorsun (kamerayla birlikte, yürürken hafif sallanır)
            Vector3 v = smoothedMove;
            Vector3 rootMove = (transform.position - lastRootPos) / dt;
            lastRootPos = transform.position;
            lastRootYaw = transform.eulerAngles.y;
            if (rootMove.sqrMagnitude > 400f) rootMove = Vector3.zero;
            smoothedMove = Vector3.Lerp(v, transform.InverseTransformDirection(rootMove), 1f - Mathf.Exp(-8f * dt));
            float walk = Mathf.Clamp01(new Vector2(smoothedMove.x, smoothedMove.z).magnitude / 4.5f);
            float bob = Mathf.Sin(Time.time * 9f) * 0.012f * walk;
            // Birinci şahısta EL YOK: eşya kendi başına ekranda durur (kol / el modeli çizilmez)
            SetFpHands(false);
            if (fpArms == null) fpArms = GetComponent<FirstPersonArms>();
            if (fpArms != null) fpArms.Hide();
            if (CarryKind == 3)
            {
                // Küçük eşya (bozuk para, yüzük, ganimet, alet): ekranın sağ altında, hafif eğik
                Vector3 sl = new Vector3(0.17f, -0.17f + bob, 0.45f);
                float height = 0f, spin = 0f;
                if (isCoin)
                {
                    // Bozuk para: [R] ile yazı-tura — para dönerek havalanır ve yerine düşer
                    if (!Mathf.Approximately(yawTarget, lastCoinYaw)) { lastCoinYaw = yawTarget; if (coinFlipT < 0f) coinFlipT = 0f; }
                    if (coinFlipT >= 0f)
                    {
                        coinFlipT += dt / CoinFlipTime;
                        float u = Mathf.Clamp01(coinFlipT);
                        height = 4f * CoinFlipHeight * u * (1f - u);
                        spin = u * 1080f;
                        if (coinFlipT >= 1f) coinFlipT = -1f;
                    }
                }
                Vector3 up = (cam.up * 0.8f - cam.forward * 0.6f).normalized;   // yüzü hafif sana dönük
                Quaternion baseRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(cam.forward, up).normalized, up);
                rot = Quaternion.AngleAxis(spin, cam.right) * baseRot * Quaternion.Euler(0f, isCoin ? 0f : yaw - 20f, 0f);
                pos = cam.TransformPoint(sl) + Vector3.up * height; vel = Vector3.zero; snap = false;
                visual.transform.SetPositionAndRotation(pos - rot * centerOff, rot);
                return;
            }
            Vector3 local = new Vector3(0f, -0.18f - visualHalfHeight * 0.25f + bob + flipLift, 0.7f + visualHalfWidth * 0.35f);
            target = cam.TransformPoint(local);
            // Eşya kameraya hafif dönük (resim yüzü sana baksın), yürürken hafif yatar
            targetRot = cam.rotation * Quaternion.Euler(-12f, yaw, Mathf.Clamp(-smoothedMove.x * 2f, -8f, 8f));
            pos = target; rot = targetRot; vel = Vector3.zero; snap = false;
            visual.transform.SetPositionAndRotation(pos - rot * centerOff, rot);
            return;
        }

        SetFpHands(false);
        // Herkes (kendi ekranın dahil): karakterin kendi elleri tutar
        bool owner = IsOwnerView;
        if (owner)
        {
            if (fpArms == null) fpArms = GetComponent<FirstPersonArms>();
            if (fpArms != null) fpArms.Hide();
        }
        if (avatar != null && avatar.TryGetHoldPoint(out Vector3 hp, out float gap))
        {
            target = hp; // eller bu noktanın iki yanını kavrar (PlayerAvatarModel IK); [R] kalkması noktanın içinde
        }
        else
        {
            bool crouch = stealth != null && stealth.IsCrouching;
            target = transform.TransformPoint(0f, crouch ? -0.35f : 0.05f, 0.45f);
        }
        // Tablo / büyük eşya kollarla birlikte bakışa göre öne-arkaya eğilir
        float tilt = avatar != null && CarryKind != 3 ? avatar.CarryTilt + ExtraTilt : 0f;
        targetRot = Quaternion.AngleAxis(tilt, transform.right) * Quaternion.Euler(0f, transform.eulerAngles.y + yaw, 0f);
        // Bozuk para: [R] ile yazı-tura — avuçtan dönerek havalanır ve geri düşer (herkes görür)
        float coinHop = 0f, coinSpin = 0f;
        if (isCoin)
        {
            if (!Mathf.Approximately(yawTarget, lastCoinYaw)) { lastCoinYaw = yawTarget; if (coinFlipT < 0f) coinFlipT = 0f; }
            if (coinFlipT >= 0f)
            {
                coinFlipT += dt / CoinFlipTime;
                float u = Mathf.Clamp01(coinFlipT);
                coinHop = 4f * CoinFlipHeight * u * (1f - u);
                coinSpin = u * 1080f;
                if (coinFlipT >= 1f) coinFlipT = -1f;
            }
            targetRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }
        // Yüzük: dik (deliği sana bakar), işaret ile başparmak arasında
        if (IsRing)
        {
            // Yüzüğün deliği tutanın gözüne bakar (birinci şahısta halka tam daire görünür; dışarıdan "yüzüğe bakıyor");
            // [R] döndürmez
            Vector3 toRing = avatar != null ? target - avatar.EyeWorldPoint() : transform.forward;
            if (toRing.sqrMagnitude < 1e-6f) toRing = transform.forward;
            targetRot = Quaternion.LookRotation(toRing.normalized, Vector3.up) * ringFix;
        }
        if (turning > 0.001f || isCoin) rot = targetRot; // çevirme animasyonu shownYaw ile yapılır
        // Herkeste gecikmesiz: parmaklar eşyanın bu karedeki yerine / dönüşüne oturur (eller zaten yumuşak hareket eder)
        if (snap || owner || inHands || (target - pos).sqrMagnitude > 4f) { snap = false; pos = target; rot = targetRot; }
        else
        {
            pos = Vector3.Lerp(pos, target, 1f - Mathf.Exp(-40f * dt)); // ele yapışık
            rot = Quaternion.Slerp(rot, targetRot, 1f - Mathf.Exp(-30f * dt));
        }
        vel = Vector3.zero;
        Quaternion shownRot = coinSpin != 0f ? Quaternion.AngleAxis(coinSpin, transform.right) * rot : rot;
        visual.transform.SetPositionAndRotation(pos + Vector3.up * coinHop - shownRot * centerOff, shownRot);
    }

    /// <summary>Kendi ekranında eşyayı tutan iki el (eldiven + çizgili kol); eşyanın iki yanını kavrar.</summary>
    private void UpdateFpHands(Vector3 itemLocal)
    {
        EnsureFpHands();
        if (fpHands.parent != cam) fpHands.SetParent(cam, false);
        SetFpHands(true);
        float hx = visualHalfWidth + 0.035f;
        for (int s = -1; s <= 1; s += 2)
        {
            Transform hand = s < 0 ? fpHandL : fpHandR;
            Transform sleeve = s < 0 ? fpSleeveL : fpSleeveR;
            Vector3 hpos = itemLocal + new Vector3(s * hx, -visualHalfHeight * 0.15f, -0.02f);
            hand.localPosition = hpos;
            hand.localRotation = Quaternion.Euler(0f, 0f, s * -20f);
            // Kol: ekranın alt köşesinden ele doğru
            Vector3 from = new Vector3(s * 0.32f, -0.62f, 0.18f);
            Vector3 dir = hpos - from;
            sleeve.localPosition = from + dir * 0.5f - dir.normalized * 0.04f;
            sleeve.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            sleeve.localScale = new Vector3(0.085f, dir.magnitude * 0.5f, 0.085f);
        }
    }

    private void SetFpHands(bool on)
    {
        if (fpHands != null && fpHands.gameObject.activeSelf != on) fpHands.gameObject.SetActive(on);
    }

    private void EnsureFpHands()
    {
        if (fpHands != null) return;
        if (gloveMat == null)
        {
            gloveMat = ArtworkLook.MakeLit(new Color(0.24f, 0.11f, 0.05f));   // deri eldiven (karakterle aynı ton)
            sleeveMat = ArtworkLook.MakeLit(new Color(0.06f, 0.2f, 0.14f));  // yeşil çizgili kazak
            stripeMat = ArtworkLook.MakeLit(new Color(0.86f, 0.8f, 0.66f));
        }
        fpHands = new GameObject("FirstPersonHands").transform;
        fpHandL = FpHand("HandL", -1f);
        fpHandR = FpHand("HandR", 1f);
        fpSleeveL = FpSleeve("SleeveL");
        fpSleeveR = FpSleeve("SleeveR");
    }

    private Transform FpHand(string name, float side)
    {
        Transform h = new GameObject(name).transform;
        h.SetParent(fpHands, false);
        FpPart(h, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.075f, 0.1f, 0.085f), gloveMat);                  // avuç
        FpPart(h, PrimitiveType.Capsule, new Vector3(-side * 0.035f, 0.035f, 0.035f), new Vector3(0.03f, 0.035f, 0.03f), gloveMat); // başparmak
        FpPart(h, PrimitiveType.Cylinder, new Vector3(0f, -0.055f, 0f), new Vector3(0.08f, 0.015f, 0.08f), stripeMat); // bilek bandı
        return h;
    }

    private Transform FpSleeve(string name)
    {
        Transform s = new GameObject(name).transform;
        s.SetParent(fpHands, false);
        FpPart(s, PrimitiveType.Cylinder, Vector3.zero, Vector3.one, sleeveMat);
        FpPart(s, PrimitiveType.Cylinder, new Vector3(0f, 0.35f, 0f), new Vector3(1.04f, 0.12f, 1.04f), stripeMat);
        FpPart(s, PrimitiveType.Cylinder, new Vector3(0f, -0.1f, 0f), new Vector3(1.04f, 0.12f, 1.04f), stripeMat);
        return s;
    }

    private static void FpPart(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }
}

/// <summary>
/// Tutulan eşyanın kaba şekli (eşya çerçevesinde, metre): tablo = kutu; diğerleri = dikey eksen etrafında yükseklik × açı
/// yarıçap zarfı (vazo, büst, heykel, küçük eşya). Eller en uygun yere konur, parmaklar yüzeye değene kadar kapanır.
/// Çerçeve: köken = tutulan nokta (eşyanın ortası), eksenler = eşyanın dönüşü. Eşya alınınca bir kez kurulur.
/// </summary>
public sealed class HoldShape
{
    public const int NY = 14, NA = 16;
    public bool IsBox;
    public Vector3 BoxCenter, BoxHalf;
    public float Y0, Y1;
    /// <summary>Büst kaidesi (alttaki dikdörtgen; çerçevede). HasBase değilse yok.</summary>
    public bool HasBase;
    public Vector3 BaseCenter, BaseHalf;
    private readonly float[] r = new float[NY * NA];

    public static HoldShape Build(GameObject visual, Vector3 centerOff, bool box, bool findBase = false)
    {
        if (visual == null) return null;
        Transform root = visual.transform;
        Quaternion r0 = root.rotation; Vector3 p0 = root.position;
        root.rotation = Quaternion.identity;
        // Çerçeve kökeni (dünyada): HeldItemView görseli pos - rot*centerOff'a koyar → köken = p0 + centerOff
        Vector3 origin = p0 + centerOff;
        var pts = new System.Collections.Generic.List<Vector3>(4096);
        foreach (MeshFilter mf in visual.GetComponentsInChildren<MeshFilter>())
        {
            Mesh m = mf.sharedMesh;
            if (m == null || !m.isReadable) continue;
            Vector3[] v = m.vertices;
            int step = Mathf.Max(1, v.Length / 6000);
            Matrix4x4 l2w = mf.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i += step) pts.Add(l2w.MultiplyPoint3x4(v[i]) - origin);
        }
        Bounds wb = default; bool hasB = ItemVisuals.TryGetBounds(visual, out wb);
        var hs = new HoldShape();
        if (findBase)
        {
            // Kaide: en altta duran küp parça (büst: BuildBustVisual'daki ilk parça)
            float lowest = float.MaxValue;
            foreach (MeshFilter mf in visual.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.name.StartsWith("Cube")) continue;
                Renderer rd = mf.GetComponent<Renderer>();
                if (rd == null) continue;
                Bounds bb = rd.bounds;
                if (bb.min.y < lowest && bb.extents.x > 0.02f && bb.extents.z > 0.02f)
                {
                    lowest = bb.min.y;
                    hs.HasBase = true; hs.BaseCenter = bb.center - origin; hs.BaseHalf = bb.extents;
                }
            }
        }
        root.rotation = r0; root.position = p0;
        if (pts.Count < 8)
        {
            if (!hasB) return null;
            pts.Clear(); // okunamayan mesh: kutu
            box = true;
            hs.BoxCenter = wb.center - origin; hs.BoxHalf = wb.extents;
        }
        Vector3 mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
        foreach (Vector3 q in pts) { mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q); }
        if (pts.Count >= 8) { hs.BoxCenter = (mn + mx) * 0.5f; hs.BoxHalf = (mx - mn) * 0.5f; }
        hs.IsBox = box;
        hs.Y0 = hs.BoxCenter.y - hs.BoxHalf.y; hs.Y1 = hs.BoxCenter.y + hs.BoxHalf.y;
        if (box) return hs;
        // Yükseklik × açı hücrelerinde en büyük yarıçap
        for (int i = 0; i < hs.r.Length; i++) hs.r[i] = -1f;
        float h = Mathf.Max(1e-4f, hs.Y1 - hs.Y0);
        foreach (Vector3 q in pts)
        {
            int yi = Mathf.Clamp((int)((q.y - hs.Y0) / h * NY), 0, NY - 1);
            int ai = Mathf.Clamp((int)((Mathf.Atan2(q.x, q.z) + Mathf.PI) / (2f * Mathf.PI) * NA), 0, NA - 1);
            float rr = Mathf.Sqrt(q.x * q.x + q.z * q.z);
            int k = yi * NA + ai;
            if (rr > hs.r[k]) hs.r[k] = rr;
        }
        // Boş hücreler: komşulardan doldur
        for (int pass = 0; pass < 4; pass++)
            for (int yi = 0; yi < NY; yi++)
                for (int ai = 0; ai < NA; ai++)
                {
                    int k = yi * NA + ai;
                    if (hs.r[k] >= 0f) continue;
                    float sum = 0f; int n = 0;
                    int[] nb = { yi * NA + (ai + 1) % NA, yi * NA + (ai + NA - 1) % NA, Mathf.Max(0, yi - 1) * NA + ai, Mathf.Min(NY - 1, yi + 1) * NA + ai };
                    foreach (int j in nb) if (hs.r[j] >= 0f) { sum += hs.r[j]; n++; }
                    if (n > 0) hs.r[k] = sum / n;
                }
        for (int i = 0; i < hs.r.Length; i++) if (hs.r[i] < 0f) hs.r[i] = Mathf.Max(hs.BoxHalf.x, hs.BoxHalf.z);
        return hs;
    }

    /// <summary>Yükseklik y'de (çerçeve), a açısında (atan2(x, z)) yüzeyin eksene uzaklığı.</summary>
    public float Radius(float y, float a)
    {
        if (IsBox) return Mathf.Min(BoxHalf.x, BoxHalf.z);
        float fy = Mathf.Clamp((y - Y0) / Mathf.Max(1e-4f, Y1 - Y0) * NY - 0.5f, 0f, NY - 1);
        float fa = (a + Mathf.PI) / (2f * Mathf.PI) * NA - 0.5f;
        int y0 = Mathf.Min((int)fy, NY - 2 < 0 ? 0 : NY - 2), y1 = Mathf.Min(y0 + 1, NY - 1);
        float ty = fy - y0;
        int a0 = ((int)Mathf.Floor(fa) % NA + NA) % NA, a1 = (a0 + 1) % NA;
        float ta = fa - Mathf.Floor(fa);
        float r0 = Mathf.Lerp(r[y0 * NA + a0], r[y0 * NA + a1], ta), r1 = Mathf.Lerp(r[y1 * NA + a0], r[y1 * NA + a1], ta);
        return Mathf.Lerp(r0, r1, ty);
    }

    /// <summary>İşaretli uzaklık (çerçevede, m): dışarıda +, içeride −.</summary>
    public float Sdf(Vector3 p)
    {
        if (IsBox)
        {
            Vector3 q = p - BoxCenter;
            Vector3 d = new Vector3(Mathf.Abs(q.x) - BoxHalf.x, Mathf.Abs(q.y) - BoxHalf.y, Mathf.Abs(q.z) - BoxHalf.z);
            Vector3 o = Vector3.Max(d, Vector3.zero);
            return o.magnitude + Mathf.Min(Mathf.Max(d.x, Mathf.Max(d.y, d.z)), 0f);
        }
        float rr = Mathf.Sqrt(p.x * p.x + p.z * p.z);
        float dr = rr - Radius(p.y, Mathf.Atan2(p.x, p.z));
        float dy = Mathf.Max(Y0 - p.y, p.y - Y1);
        return Mathf.Max(dr, dy);
    }

    /// <summary>İki yandan kavramak için en uygun yükseklik: [lo, hi] (0 alt, 1 üst) aralığında iki yanın ortalama yarıçapı en büyük.</summary>
    public float BestGripY(float aRight, float aLeft, float lo, float hi)
    {
        if (IsBox) return BoxCenter.y;
        float best = -1f, by = (Y0 + Y1) * 0.5f;
        for (int i = 0; i <= 12; i++)
        {
            float y = Mathf.Lerp(Y0, Y1, Mathf.Lerp(lo, hi, i / 12f));
            float v = Radius(y, aRight) + Radius(y, aLeft);
            if (v > best + 1e-4f) { best = v; by = y; }
        }
        return by;
    }
}

