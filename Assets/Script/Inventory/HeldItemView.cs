using UnityEngine;

/// <summary>
/// Elde tutulan eşyanın görüntüsü — R.E.P.O'daki gibi: ele yapışık değil, oyuncunun ÖNÜNDE süzülür.
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
        if (ItemVisuals.TryGetBounds(visual, out Bounds b)) radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * 0.6f, 0.08f, 0.3f);
        ItemVisuals.SetShadows(visual, true);
        snap = true;
        lastRootPos = transform.position;
        lastRootYaw = transform.eulerAngles.y;
    }

    public void Hide()
    {
        if (visual != null) Destroy(visual);
        visual = null;
    }

    private void OnDestroy() => Hide();

    private bool IsOwnerView => netSetup != null && netSetup.IsSpawned && netSetup.IsOwner && cam != null;

    private void LateUpdate()
    {
        if (visual == null) return;

        // Dolapta saklanırken ya da atölyede çalışırken eşya görünmesin
        bool hide = (stealth != null && stealth.IsHidden) || (IsOwnerView && WorkbenchUI.IsOpen);
        if (visual.activeSelf == hide) visual.SetActive(!hide);
        if (hide) { snap = true; return; }

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;

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
}
