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
    /// <summary>Eşyanın yarı genişliği (m): karakterin elleri bu kadar iki yanından kavrar.</summary>
    public float HalfWidth => visualHalfWidth;

    [Tooltip("Açık: eşya karakterin elinde (kendi ekranında elinle tutarken). Kapalı: eski R.E.P.O tarzı önde süzülme.")]
    [SerializeField] private bool inHands = true;
    private PlayerAvatar avatar;
    private float shownYaw;
    private FirstPersonArms fpArms;
    private Transform fpHands, fpHandL, fpHandR, fpSleeveL, fpSleeveR;
    private float visualHalfWidth = 0.2f, visualHalfHeight = 0.2f;
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
        if (ItemVisuals.TryGetBounds(visual, out Bounds b))
        {
            radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * 0.6f, 0.08f, 0.3f);
            visualHalfWidth = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 0.06f, 0.45f);
            visualHalfHeight = Mathf.Clamp(b.extents.y, 0.04f, 0.45f);
        }
        ItemVisuals.SetShadows(visual, true);
        snap = true;
        lastRootPos = transform.position;
        lastRootYaw = transform.eulerAngles.y;
    }

    public void Hide()
    {
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
        bool firstPerson = IsOwnerView && !PlayerAvatar.PreviewActive;
        // R: eşya sadece ön / arka çevrilir (karşındakine gösterir). Kısa animasyonla döner, dönerken hafifçe kalkar.
        float yawTarget = inventory != null ? inventory.HoldYaw : 0f;
        shownYaw = Mathf.MoveTowardsAngle(shownYaw, yawTarget, 560f * dt);
        float turning = Mathf.Abs(Mathf.DeltaAngle(shownYaw, yawTarget)) / 180f;     // 1 → 0
        float flipLift = Mathf.Sin(Mathf.Clamp01(turning) * Mathf.PI) * 0.07f;
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
            // Eşya ekranın ortasına daha yakın ve biraz ileride: ne yaptığın (çizim, sahte, eser) görünsün
            Vector3 local = new Vector3(0f, -0.18f - visualHalfHeight * 0.25f + bob + flipLift, 0.7f + visualHalfWidth * 0.35f);
            target = cam.TransformPoint(local);
            // Eşya kameraya hafif dönük (resim yüzü sana baksın), yürürken hafif yatar
            targetRot = cam.rotation * Quaternion.Euler(-12f, yaw, Mathf.Clamp(-smoothedMove.x * 2f, -8f, 8f));
            pos = target; rot = targetRot; vel = Vector3.zero; snap = false;
            visual.transform.SetPositionAndRotation(pos, rot);
            // Karakterin GERÇEK kolları (FirstPersonArms); model okunamazsa basit eldivenli eller
            if (fpArms == null) fpArms = GetComponent<FirstPersonArms>();
            if (fpArms == null) fpArms = gameObject.AddComponent<FirstPersonArms>();
            // Eller tabloyla birlikte DÖNMEZ: R ile tablo ellerin arasında kendi etrafında döner, eller yerinde kalır
            Vector3 handsLocal = new Vector3(local.x, local.y - flipLift, local.z);
            Quaternion handsRot = Quaternion.Euler(-12f, 0f, Mathf.Clamp(-smoothedMove.x * 2f, -8f, 8f));
            if (fpArms.Show(cam, handsLocal, handsRot, visualHalfWidth, visualHalfHeight))
                SetFpHands(false);
            else UpdateFpHands(local);
            return;
        }

        SetFpHands(false);
        // Diğer oyuncular (ve F5 önizleme): karakterin iki elinin arasında
        if (avatar != null && avatar.TryGetHoldPoint(out Vector3 hp, out float gap))
        {
            target = hp + Vector3.up * flipLift; // eller bu noktanın iki yanını kavrar (PlayerAvatarModel IK)
        }
        else
        {
            bool crouch = stealth != null && stealth.IsCrouching;
            target = transform.TransformPoint(0f, crouch ? -0.35f : 0.05f, 0.45f);
        }
        targetRot = Quaternion.Euler(0f, transform.eulerAngles.y + yaw, 0f);
        if (turning > 0.001f) rot = targetRot; // çevirme animasyonu shownYaw ile yapılır
        if (snap || (target - pos).sqrMagnitude > 4f) { snap = false; pos = target; rot = targetRot; }
        else
        {
            pos = Vector3.Lerp(pos, target, 1f - Mathf.Exp(-25f * dt)); // ele yapışık (çok az gecikme)
            rot = Quaternion.Slerp(rot, targetRot, 1f - Mathf.Exp(-20f * dt));
        }
        vel = Vector3.zero;
        visual.transform.SetPositionAndRotation(pos, rot);
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
