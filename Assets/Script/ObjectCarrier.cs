using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// E ile Rigidbody'li objeleri kaldırıp taşıma mekaniği.
/// Player objesine eklenir. Obje fizik ile tutulur (duvarlardan geçmez).
///
/// Kontroller:
///   E          : Kaldır / Bırak
///   Sol Tık    : Fırlat
///   R          : Objeyi 90° döndür
///   Tekerlek   : Tutma mesafesini ayarla
/// </summary>
public class ObjectCarrier : MonoBehaviour
{
    [Header("Referanslar")]
    [Tooltip("Boş bırakılırsa PlayerController'daki kamera / Camera.main kullanılır")]
    [SerializeField] private Transform playerCamera;

    [Header("Kaldırma")]
    [SerializeField] private float pickupRange = 3f;
    [Tooltip("Bu kütleden (kg) ağır objeler kaldırılamaz")]
    [SerializeField] private float maxPickupMass = 40f;
    [SerializeField] private LayerMask pickupMask = ~0;

    [Header("Tutma")]
    [SerializeField] private float holdDistance = 1.8f;
    [SerializeField] private float minHoldDistance = 1.0f;
    [SerializeField] private float maxHoldDistance = 2.8f;
    [SerializeField] private float scrollSensitivity = 0.002f;
    [Tooltip("Objenin hedef noktaya ne kadar hızlı geldiği")]
    [SerializeField] private float followStrength = 15f;
    [SerializeField] private float maxFollowSpeed = 12f;
    [SerializeField] private float rotateStrength = 12f;
    [Tooltip("Obje hedef noktadan bu kadar uzaklaşırsa (bir yere sıkışırsa) düşer")]
    [SerializeField] private float breakDistance = 2.0f;

    [Header("Fırlatma")]
    [SerializeField] private float throwForce = 8f;

    [Header("Ağırlık Etkisi")]
    [Tooltip("En ağır objeyi taşırken oyuncu hızı bu oranla çarpılır")]
    [Range(0.2f, 1f)]
    [SerializeField] private float heaviestSpeedMultiplier = 0.55f;
    [Tooltip("Bu kütlenin üstündeki objelerle koşulamaz ve zıplanamaz")]
    [SerializeField] private float heavyMassThreshold = 20f;

    [Header("Arayüz")]
    [SerializeField] private bool showCrosshair = true;

    // Dışarıdan okunan durumlar (PlayerController kullanır)
    public bool IsHolding => heldBody != null;
    public float SpeedMultiplier { get; private set; } = 1f;
    public bool IsCarryingHeavy => heldBody != null && heldBody.mass >= heavyMassThreshold;

    private Rigidbody heldBody;
    private Collider[] heldColliders;
    private Collider[] playerColliders;
    private Quaternion heldRotationOffset; // oyuncunun yönüne göre objenin dönüşü

    // Bırakınca geri yüklenecek orijinal ayarlar
    private bool originalUseGravity;
    private float originalLinearDamping;
    private float originalAngularDamping;
    private RigidbodyInterpolation originalInterpolation;
    private CollisionDetectionMode originalCollisionMode;

    private Rigidbody lookTarget; // nişan alınan obje (arayüz için)

    private void Awake()
    {
        if (playerCamera == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) playerCamera = cam.transform;
            else if (Camera.main != null) playerCamera = Camera.main.transform;
        }
        playerColliders = GetComponentsInChildren<Collider>();
    }

    private void Update()
    {
        if (playerCamera == null || Keyboard.current == null) return;

        lookTarget = heldBody == null ? FindPickupTarget() : null;

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (heldBody != null) Drop();
            else if (lookTarget != null) PickUp(lookTarget);
        }

        if (heldBody == null) return;

        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Throw();
                return;
            }

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                holdDistance = Mathf.Clamp(holdDistance + scroll * scrollSensitivity, minHoldDistance, maxHoldDistance);
            }
        }

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            heldRotationOffset = Quaternion.Euler(0f, 90f, 0f) * heldRotationOffset;
        }
    }

    private void FixedUpdate()
    {
        if (heldBody == null) return;

        Vector3 targetPos = GetHoldPoint();
        Vector3 toTarget = targetPos - heldBody.worldCenterOfMass;

        // Bir yere sıkıştıysa bırak
        if (toTarget.magnitude > breakDistance)
        {
            Drop();
            return;
        }

        // Konum: hedefe doğru hız ver (fizik çarpışmaları korunur)
        Vector3 desiredVelocity = Vector3.ClampMagnitude(toTarget * followStrength, maxFollowSpeed);
        heldBody.linearVelocity = desiredVelocity;

        // Dönüş: oyuncunun baktığı yöne göre sabit kalsın
        Quaternion targetRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * heldRotationOffset;
        Quaternion delta = targetRot * Quaternion.Inverse(heldBody.rotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (Mathf.Abs(angle) > 0.01f && !float.IsNaN(axis.x))
        {
            heldBody.angularVelocity = axis.normalized * (angle * Mathf.Deg2Rad * rotateStrength);
        }
        else
        {
            heldBody.angularVelocity = Vector3.zero;
        }
    }

    private Vector3 GetHoldPoint()
    {
        return playerCamera.position + playerCamera.forward * holdDistance;
    }

    private Rigidbody FindPickupTarget()
    {
        Ray ray = new Ray(playerCamera.position, playerCamera.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, pickupRange, pickupMask, QueryTriggerInteraction.Ignore);
        float closest = float.MaxValue;
        Rigidbody result = null;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue; // oyuncunun kendisi
            if (hit.distance >= closest) continue;

            closest = hit.distance;
            result = hit.rigidbody;
        }

        if (result == null || result.isKinematic) return null;
        if (result.mass > maxPickupMass) return null;

        Pickupable p = result.GetComponent<Pickupable>();
        if (p != null && !p.CanBePickedUp) return null;

        return result;
    }

    private void PickUp(Rigidbody body)
    {
        heldBody = body;
        heldColliders = body.GetComponentsInChildren<Collider>();

        originalUseGravity = body.useGravity;
        originalLinearDamping = body.linearDamping;
        originalAngularDamping = body.angularDamping;
        originalInterpolation = body.interpolation;
        originalCollisionMode = body.collisionDetectionMode;

        body.useGravity = false;
        body.linearDamping = 1f;
        body.angularDamping = 1f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Objenin oyuncuya göre mevcut yatay dönüşünü koru, 90°'ye yuvarla
        float relativeYaw = body.rotation.eulerAngles.y - transform.eulerAngles.y;
        relativeYaw = Mathf.Round(relativeYaw / 90f) * 90f;
        heldRotationOffset = Quaternion.Euler(0f, relativeYaw, 0f);

        // Başlangıç mesafesi: objenin şu anki uzaklığı (makul aralıkta)
        float currentDist = Vector3.Distance(playerCamera.position, body.worldCenterOfMass);
        holdDistance = Mathf.Clamp(currentDist, minHoldDistance, maxHoldDistance);

        SetPlayerCollisionIgnored(true);

        float massRatio = Mathf.Clamp01(body.mass / maxPickupMass);
        SpeedMultiplier = Mathf.Lerp(1f, heaviestSpeedMultiplier, massRatio);
    }

    public void Drop()
    {
        if (heldBody == null) return;

        heldBody.useGravity = originalUseGravity;
        heldBody.linearDamping = originalLinearDamping;
        heldBody.angularDamping = originalAngularDamping;
        heldBody.interpolation = originalInterpolation;
        heldBody.collisionDetectionMode = originalCollisionMode;

        // Bırakırken çok hızlı uçmasın
        heldBody.linearVelocity = Vector3.ClampMagnitude(heldBody.linearVelocity, 3f);
        heldBody.angularVelocity = Vector3.zero;

        SetPlayerCollisionIgnored(false);

        heldBody = null;
        heldColliders = null;
        SpeedMultiplier = 1f;
    }

    private void Throw()
    {
        Rigidbody body = heldBody;
        Drop();
        // Ağır objeler daha az uzağa gider
        float massFactor = Mathf.Clamp(1f / Mathf.Max(body.mass, 0.1f), 0.1f, 1f);
        body.AddForce(playerCamera.forward * throwForce * massFactor * body.mass, ForceMode.Impulse);
    }

    private void SetPlayerCollisionIgnored(bool ignore)
    {
        if (heldColliders == null || playerColliders == null) return;
        foreach (Collider pc in playerColliders)
        {
            if (pc == null) continue;
            foreach (Collider oc in heldColliders)
            {
                if (oc != null) Physics.IgnoreCollision(pc, oc, ignore);
            }
        }
    }

    private void OnDisable()
    {
        Drop();
    }

    private void OnGUI()
    {
        if (playerCamera == null) return;

        float cx = Screen.width / 2f;
        float cy = Screen.height / 2f;

        if (showCrosshair)
        {
            GUI.color = (lookTarget != null || heldBody != null) ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        string text = null;
        if (heldBody != null)
        {
            text = "[E] Bırak    [Sol Tık] Fırlat    [R] Döndür    [Tekerlek] Mesafe";
        }
        else if (lookTarget != null)
        {
            Pickupable p = lookTarget.GetComponent<Pickupable>();
            string name = (p != null && !string.IsNullOrEmpty(p.DisplayName)) ? p.DisplayName : lookTarget.name;
            text = $"[E] Kaldır: {name}";
        }

        if (text == null) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold
        };
        Rect rect = new Rect(0f, cy + 30f, Screen.width, 30f);

        // Okunabilirlik için gölge
        style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, style);
        style.normal.textColor = Color.white;
        GUI.Label(rect, text, style);
    }
}
