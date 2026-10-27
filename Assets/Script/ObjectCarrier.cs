using UnityEngine;
using UnityEngine.InputSystem;

public class ObjectCarrier : MonoBehaviour
{
    [Header("Referanslar")]
    [SerializeField] private Transform playerCamera;

    [Header("Kaldırma")]
    [SerializeField] private float pickupRange = 3f;
    [SerializeField] private float maxPickupMass = 40f;
    [SerializeField] private LayerMask pickupMask = ~0;

    [Header("Tutma")]
    [SerializeField] private float holdDistance = 1.8f;
    [SerializeField] private float minHoldDistance = 1.0f;
    [SerializeField] private float maxHoldDistance = 2.8f;
    [SerializeField] private float scrollSensitivity = 0.002f;
    [SerializeField] private float followStrength = 22f; // Hedefe çekiş gücü artırıldı
    [SerializeField] private float maxFollowSpeed = 35f;  // Ani dönüşlerde yetişmesi için hız limiti yükseltildi
    [SerializeField] private float rotateStrength = 16f;

    [Header("Güçlü Fırlatma (Charge Throw)")]
    [SerializeField] private float maxThrowForce = 14f;
    [SerializeField] private float minThrowForce = 2.5f;
    [SerializeField] private float baseChargeDuration = 0.8f;
    [SerializeField] private float heavyChargeDuration = 1.8f;

    [Header("Ağırlık & Hissiyat")]
    [Range(0.2f, 0.8f)]
    [SerializeField] private float minSpeedMultiplier = 0.35f;
    [Range(0.2f, 1f)]
    [SerializeField] private float minTurnMultiplier = 0.45f;
    [SerializeField] private float heavyMassThreshold = 20f;

    [Header("Arayüz & Halka")]
    [SerializeField] private bool showCrosshair = true;
    [SerializeField] private float ringRadius = 22f;

    public bool IsHolding => heldBody != null;
    public float SpeedMultiplier { get; private set; } = 1f;
    public float TurnSensitivityMultiplier { get; private set; } = 1f;
    public float WeightRatio { get; private set; } = 0f;
    public bool IsCarryingHeavy => heldBody != null && heldBody.mass >= heavyMassThreshold;

    private Rigidbody heldBody;
    private NetworkPickupable heldNet;
    private Collider[] heldColliders;
    private Collider[] playerColliders;
    private Quaternion heldRotationOffset;

    private bool originalUseGravity;
    private float originalLinearDamping;
    private float originalAngularDamping;
    private RigidbodyInterpolation originalInterpolation;
    private CollisionDetectionMode originalCollisionMode;

    private Rigidbody lookTarget;

    private bool isChargingThrow = false;
    private float throwChargeTimer = 0f;
    private Texture2D whitePixel;

    private void Awake()
    {
        if (playerCamera == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) playerCamera = cam.transform;
            else if (Camera.main != null) playerCamera = Camera.main.transform;
        }
        playerColliders = GetComponentsInChildren<Collider>();

        whitePixel = new Texture2D(1, 1);
        whitePixel.SetPixel(0, 0, Color.white);
        whitePixel.Apply();
    }

    private void Update()
    {
        if (playerCamera == null || Keyboard.current == null) return;

        lookTarget = heldBody == null ? FindPickupTarget() : null;

        // Menü açıkken (imleç serbest) oyun girdilerini alma.
        if (Cursor.lockState != CursorLockMode.Locked || ChatUI.IsTyping || PlayerController.InputLocked)
        {
            CancelThrowCharge();
            return;
        }

        // E ile Kaldır / Bırak
        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (heldBody != null)
            {
                CancelThrowCharge();
                Drop();
            }
            else if (lookTarget != null)
            {
                if (lookTarget.mass <= maxPickupMass)
                {
                    Pickupable p = lookTarget.GetComponent<Pickupable>();
                    if (p == null || p.CanBePickedUp)
                    {
                        NetworkPickupable np = lookTarget.GetComponent<NetworkPickupable>();
                        if (np != null && np.IsSpawned)
                        {
                            // Online: önce sunucudan izin iste, sahiplik gelince PickUp otomatik çağrılır.
                            np.RequestPickup(this);
                        }
                        else
                        {
                            PickUp(lookTarget); // Tek kişilik mod
                        }
                    }
                }
            }
        }

        if (heldBody == null) return;

        HandleThrowInput();

        if (Mouse.current != null)
        {
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

    private void HandleThrowInput()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            isChargingThrow = true;
            throwChargeTimer = 0f;
        }

        if (isChargingThrow && Mouse.current.leftButton.isPressed)
        {
            float chargeDuration = Mathf.Lerp(baseChargeDuration, heavyChargeDuration, WeightRatio);
            throwChargeTimer += Time.deltaTime / chargeDuration;
            throwChargeTimer = Mathf.Clamp01(throwChargeTimer);
        }

        if (isChargingThrow && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            ExecuteThrow(throwChargeTimer);
            CancelThrowCharge();
        }
    }

    private void CancelThrowCharge()
    {
        isChargingThrow = false;
        throwChargeTimer = 0f;
    }

    private void ExecuteThrow(float chargePercent)
    {
        Rigidbody body = heldBody;
        Drop();

        float massEfficiency = Mathf.Clamp01(1f - (body.mass / (maxPickupMass * 1.1f)));
        massEfficiency = Mathf.Pow(massEfficiency, 1.3f);

        float chosenForce = Mathf.Lerp(minThrowForce, maxThrowForce, chargePercent) * massEfficiency;
        Vector3 throwDir = (playerCamera.forward + Vector3.up * 0.12f).normalized;

        body.linearVelocity = throwDir * chosenForce;
        body.AddTorque(playerCamera.right * (chosenForce * 0.5f), ForceMode.Impulse);
    }

    private void FixedUpdate()
    {
        if (heldBody == null) return;

        // Online'da sahiplik bir şekilde elimizden çıktıysa (obje kinematic olduysa) bırak.
        if (heldBody.isKinematic)
        {
            Drop();
            return;
        }

        Vector3 targetPos = GetHoldPoint();
        Vector3 toTarget = targetPos - heldBody.worldCenterOfMass;

        // Ani fare hareketinde breakDistance kontrolü objeyi elden düşürmez;
        // Obje ne kadar geride kalırsa kalsın hedefine doğru hızlanır.
        Vector3 desiredVelocity = Vector3.ClampMagnitude(toTarget * followStrength, maxFollowSpeed);
        heldBody.linearVelocity = desiredVelocity;

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
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.distance >= closest) continue;

            closest = hit.distance;
            result = hit.rigidbody;
        }

        if (result == null) return null;

        // Eserler/aletler çantaya girer (PlayerInventory), elde taşınmaz.
        if (result.GetComponentInParent<WorldItem>() != null) return null;
        // Kapı, dolap gibi etkileşim noktaları ve oyuncular elde taşınmaz.
        if (result.GetComponentInParent<Interactable>() != null) return null;
        if (result.GetComponentInParent<PlayerInventory>() != null) return null;

        // Online'da başkasına ait objeler bizde kinematic görünür; NetworkPickupable varsa yine hedeflenebilir.
        if (result.isKinematic && result.GetComponent<NetworkPickupable>() == null) return null;

        return result;
    }

    public void PickUp(Rigidbody body)
    {
        if (heldBody != null) return;

        heldBody = body;
        heldNet = body.GetComponent<NetworkPickupable>();
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

        float relativeYaw = body.rotation.eulerAngles.y - transform.eulerAngles.y;
        relativeYaw = Mathf.Round(relativeYaw / 90f) * 90f;
        heldRotationOffset = Quaternion.Euler(0f, relativeYaw, 0f);

        float currentDist = Vector3.Distance(playerCamera.position, body.worldCenterOfMass);
        holdDistance = Mathf.Clamp(currentDist, minHoldDistance, maxHoldDistance);

        SetPlayerCollisionIgnored(true);

        float linearRatio = Mathf.Clamp01(body.mass / maxPickupMass);
        WeightRatio = Mathf.Pow(linearRatio, 1.4f);

        SpeedMultiplier = Mathf.Lerp(1f, minSpeedMultiplier, WeightRatio);
        TurnSensitivityMultiplier = Mathf.Lerp(1f, minTurnMultiplier, WeightRatio);
    }

    public void Drop()
    {
        if (heldBody == null) return;

        heldBody.useGravity = originalUseGravity;
        heldBody.linearDamping = originalLinearDamping;
        heldBody.angularDamping = originalAngularDamping;
        heldBody.interpolation = originalInterpolation;
        heldBody.collisionDetectionMode = originalCollisionMode;

        heldBody.linearVelocity = Vector3.ClampMagnitude(heldBody.linearVelocity, 3f);
        heldBody.angularVelocity = Vector3.zero;

        SetPlayerCollisionIgnored(false);

        if (heldNet != null) heldNet.NotifyReleased();
        heldNet = null;

        heldBody = null;
        heldColliders = null;
        SpeedMultiplier = 1f;
        TurnSensitivityMultiplier = 1f;
        WeightRatio = 0f;
        CancelThrowCharge();
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
            // Nişangah: 2 px fildişi nokta; hedefte 6 px pirinç halka (yumuşak geçiş)
            bool aimed = lookTarget != null || heldBody != null;
            float aimT = UIUtil.Ease.OutCubic(UIUtil.Tween(0x43525348, aimed, UITheme.MotionFast));
            UIUtil.DrawRect(new Rect(cx - 1f, cy - 1f, 2f, 2f), UITheme.WithAlpha(UITheme.Ivory, aimed ? 1f : 0.55f));
            if (aimT > 0.01f)
                UIUtil.DrawRing(new Vector2(cx, cy), Mathf.Lerp(3f, 6f, aimT), 1f, UITheme.WithAlpha(UITheme.BrassLight, aimT));
        }

        // Fırlatma yüklemesi: pirinç halka dolar
        if (isChargingThrow)
        {
            UIUtil.DrawRing(new Vector2(cx, cy), ringRadius, throwChargeTimer, UITheme.BrassLight);
        }

        string text = null;
        PromptTone tone = PromptTone.Normal;

        if (heldBody != null)
        {
            PromptHud.Idle(1);
            PromptHud.HintLine(Loc.T("carry.holding", Mathf.RoundToInt(heldBody.mass)), UITheme.Ivory);
            return;
        }
        else if (lookTarget != null)
        {
            Pickupable p = lookTarget.GetComponent<Pickupable>();
            string objName = (p != null && !string.IsNullOrEmpty(p.DisplayName)) ? p.DisplayName : lookTarget.name;

            NetworkPickupable np = lookTarget.GetComponent<NetworkPickupable>();

            if (np != null && np.IsHeldByOther)
            {
                text = Loc.T("carry.heldbyother", objName);
                tone = PromptTone.Dim;
            }
            else if (lookTarget.mass > maxPickupMass)
            {
                text = Loc.T("carry.tooheavy", objName, Mathf.RoundToInt(lookTarget.mass));
                tone = PromptTone.Danger;
            }
            else if (p != null && !p.CanBePickedUp)
            {
                text = Loc.T("carry.locked", objName);
                tone = PromptTone.Dim;
            }
            else
            {
                text = Loc.T("carry.pickup", objName, Mathf.RoundToInt(lookTarget.mass));
            }
        }

        // Etkileşim yazısıyla üst üste binmesin: kendi kanalı (1), CenterSlot'ta alt alta dizilir
        if (text == null) { PromptHud.Idle(1); return; }
        PromptHud.Show(text, tone, -1f, 1);
    }
}