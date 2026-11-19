using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Hareket Hızları")]
    [SerializeField] private float walkSpeed = 4.5f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float crouchSpeed = 2.2f;

    [Header("Atalet & İvmelenme (Momentum)")]
    [Tooltip("Tuşa basarken hızlanma çevikliği")]
    [SerializeField] private float baseAcceleration = 18f;
    [Tooltip("Ağır yük taşırken hızlanma çevikliği")]
    [SerializeField] private float heavyAcceleration = 4f;

    [Tooltip("Tuşu bıraktığında durma (fren) çevikliği - Yüksek olması drift/kaymayı önler")]
    [SerializeField] private float baseDeceleration = 30f;
    [Tooltip("Ağır yük taşırken durma çevikliği")]
    [SerializeField] private float heavyDeceleration = 12f;

    [Header("İnsan Zıplaması & Yerçekimi")]
    [SerializeField] private float jumpHeight = 0.55f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float fallMultiplier = 1.8f;
    [SerializeField] private float landBobAmount = 0.08f;

    [Header("Eğilme (Crouch) Ayarları")]
    [SerializeField] private float standingHeight = 2f;
    [SerializeField] private float crouchHeight = 1f;
    [SerializeField] private float standingCameraY = 0.6f;
    [SerializeField] private float crouchCameraY = 0.1f;
    [SerializeField] private float crouchTransitionSpeed = 10f;

    [Header("Kamera & Bakış")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private float lookSensitivity = 0.05f;
    [SerializeField] private float lookXLimit = 85f;

    [Header("Merdiven Adım Hissi")]
    [SerializeField] private string stairsTag = "Stairs";
    [SerializeField] private float stepFrequency = 11f;
    [SerializeField] private float stepDropAmount = 0.08f;
    [SerializeField] private float stepTiltAmount = 1.2f;
    [SerializeField] private float recoverySpeed = 12f;

    private CharacterController characterController;
    private ObjectCarrier carrier;
    private PlayerInventory inventory;
    private Vector3 verticalVelocity;
    private Vector3 currentHorizontalVelocity;
    private Vector3 pushVelocity; // başka biri itti (PushSystem): yavaşça söner
    private float rotationX = 0f;
    private bool isCrouching = false;

    /// <summary>Eğiliyor mu? (Güvenlikler eğilen oyuncuyu daha geç fark eder.)</summary>
    public bool IsCrouching => isCrouching;

    /// <summary>Kamyon kalkış / polis / "hepsi yakalandı" sekansı sırasında true: hareket, bakış ve etkileşim durur (MuseumSession ayarlar).</summary>
    public static bool InputLocked { get; set; }
    /// <summary>Sadece fareyle bakış dursun (yürüme devam eder). F5 karakter önizlemesinde orta tuşla etrafında dönerken.</summary>
    public static bool LookFrozen { get; set; }
    /// <summary>Kamera sarsıntısı şiddeti (0..1, metre cinsinden küçük konum sapması); sekans sırasında MuseumSession ayarlar.</summary>
    public static float ShakeAmount { get; set; }
    private Vector3 lastShakeOffset;
    private bool wasGrounded = true;
    private float currentBaseCamY;
    private float currentCamTilt = 0f;
    private float stepTimer = 0f;
    private float landOffset = 0f;

    /// <summary>Ayakta göz (kamera) yüksekliği, oyuncu kökünden (kapsül merkezi). Soygun karakterinde kamera karakterin
    /// GERÇEK göz hizasına iner (1,75 m karakter: gözler ~1,37 m); böylece oyuncular birbirini göz hizasında görür.</summary>
    public static float EyeOffset => PlayerAvatar.ModelEyeY > 0.5f ? Mathf.Clamp(PlayerAvatar.ModelEyeY - 1f + 0.03f, 0.2f, 0.6f) : 0.6f;
    private float StandCamY => PlayerAvatar.ModelEyeY > 0.5f ? Mathf.Min(standingCameraY, EyeOffset) : standingCameraY;
    private float knockStart = -10f;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        carrier = GetComponent<ObjectCarrier>();
        inventory = GetComponent<PlayerInventory>();
        currentBaseCamY = StandCamY;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Dolapta saklanırken: yürüyemez ama fareyle etrafa bakabilir (dolabın baktığı yönün sağına / soluna sınırlı)
    private bool lookOnly;
    private float lookCenterYaw;
    private const float HideYawLimit = 110f;

    // Güvenlik jopla vurdu: birkaç saniye yerde baygın (PushSystem)
    private float knockedUntil = -1f;
    private float knockAmount;

    /// <summary>Baygın mı (yerde)?</summary>
    public bool IsKnockedOut => Time.time < knockedUntil;

    /// <summary>Saklanma (PlayerStealth): true = sadece fareyle bakış, merkez yön verilir.</summary>
    public void SetLookOnly(bool on, float centerYaw)
    {
        lookOnly = on;
        lookCenterYaw = centerYaw;
        rotationX = 0f;
    }

    /// <summary>Jopla vuruldu: birkaç saniye yere yığılır (hareket ve bakış yok).</summary>
    public void KnockOut(float seconds)
    {
        if (!IsKnockedOut) knockStart = Time.time; // düşüş anı: kamera karakterin sendeleme + düşüş zamanlamasını izler
        knockedUntil = Mathf.Max(knockedUntil, Time.time + seconds);
        pushVelocity = Vector3.zero;
        currentHorizontalVelocity = Vector3.zero;
    }

    private void Update()
    {
        if (IsKnockedOut || knockAmount > 0.001f)
        {
            HandleKnockedOut();
            return;
        }
        if (InputLocked)
        {
            // Sekans: yerinde dur (yer çekimi uygulanır), bakış ve girdi yok; kamera sarsıntısı sürer
            currentHorizontalVelocity = Vector3.zero;
            pushVelocity = Vector3.zero;
            if (characterController != null && characterController.enabled)
            {
                verticalVelocity.y = characterController.isGrounded ? -2f : verticalVelocity.y + gravity * Time.deltaTime;
                characterController.Move(verticalVelocity * Time.deltaTime);
            }
            HandleStairStepEffect();
            return;
        }
        ApplyFieldOfView();
        HandleMouseLook();
        if (lookOnly) return; // dolapta: sadece bakış
        HandleCrouch();
        HandleMovement();
        HandleStairStepEffect();
    }

    private Camera fovCam;

    /// <summary>Ayarlardaki görüş alanı (oyuncu seçtiyse) kendi kamerana uygulanır.</summary>
    private void ApplyFieldOfView()
    {
        if (playerCamera == null || !GameSettings.HasCustomFov) return;
        if (fovCam == null || fovCam.transform != playerCamera) fovCam = playerCamera.GetComponent<Camera>();
        if (fovCam != null && !Mathf.Approximately(fovCam.fieldOfView, GameSettings.FieldOfView)) fovCam.fieldOfView = GameSettings.FieldOfView;
    }

    /// <summary>Baygınken kamera yere düşer ve yana yatar; süre bitince yavaşça kalkar.</summary>
    private void HandleKnockedOut()
    {
        bool down = IsKnockedOut;
        knockAmount = Mathf.MoveTowards(knockAmount, down ? 1f : 0f, Time.deltaTime * (down ? 4f : 1.6f));
        if (characterController != null && characterController.enabled)
        {
            verticalVelocity.y += Physics.gravity.y * Time.deltaTime;
            if (characterController.isGrounded && verticalVelocity.y < 0f) verticalVelocity.y = -2f;
            characterController.Move(verticalVelocity * Time.deltaTime);
        }
        if (playerCamera == null) return;
        // Karakterle aynı zamanlama (PlayerAvatar: 0,55 sn sendeleme, 0,28 sn düşüş): önce sallanır, sonra kamera yere
        // yığılır (yan yatmış, yerden ~20 cm), yere çarpınca küçük sekme; kalkarken yavaşça doğrulur
        const float Stagger = 0.55f, Fall = 0.28f;
        float t = Time.time - knockStart;
        if (down) knockAmount = Mathf.Min(knockAmount, Mathf.Clamp01((t - Stagger) / Fall)); // sendelerken henüz düşmedi
        float k = Mathf.SmoothStep(0f, 1f, knockAmount);
        float floorY = characterController != null ? characterController.center.y - characterController.height * 0.5f : -1f;
        float stag = down && t < Stagger ? 1f - Mathf.Clamp01((t - (Stagger - 0.12f)) / 0.12f) : 0f;
        float tb = t - Stagger - Fall;
        float bounce = down && tb > 0f ? Mathf.Abs(Mathf.Sin(tb * 13f)) * 0.06f * Mathf.Exp(-tb * 4.5f) : 0f;
        Vector3 lp = playerCamera.localPosition;
        lp.y = Mathf.Lerp(currentBaseCamY, floorY + 0.2f, k) + bounce * k;
        playerCamera.localPosition = lp;
        float wobbleRoll = Mathf.Sin(t * 11f) * 10f * stag, wobblePitch = Mathf.Sin(t * 8f + 1f) * 6f * stag;
        playerCamera.localRotation = Quaternion.Euler(Mathf.Lerp(rotationX, 22f, k) + wobblePitch, 0f, Mathf.Lerp(0f, 80f, k) + wobbleRoll);
    }

    private void HandleMovement()
    {
        if (Keyboard.current == null) return;

        bool isGrounded = characterController.isGrounded;

        if (isGrounded && !wasGrounded)
        {
            landOffset = -landBobAmount;
        }
        wasGrounded = isGrounded;

        if (isGrounded && verticalVelocity.y < 0)
        {
            verticalVelocity.y = -2f;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) vertical += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) vertical -= 1f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) horizontal -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) horizontal += 1f;

        // Sohbete yazarken tuşlar karakteri hareket ettirmesin.
        bool inputBlocked = ChatUI.IsTyping;
        if (inputBlocked)
        {
            horizontal = 0f;
            vertical = 0f;
        }

        Vector3 inputDir = (transform.right * horizontal + transform.forward * vertical).normalized;

        bool carryingHeavy = carrier != null && carrier.IsCarryingHeavy;

        // Hedef hız
        float targetSpeed = walkSpeed;
        if (isCrouching) targetSpeed = crouchSpeed;
        else if (!inputBlocked && Keyboard.current.leftShiftKey.isPressed && !carryingHeavy) targetSpeed = sprintSpeed;

        if (carrier != null) targetSpeed *= carrier.SpeedMultiplier;
        if (inventory != null) targetSpeed *= inventory.SpeedMultiplier; // çanta doldukça yavaşla

        Vector3 targetVelocity = inputDir * targetSpeed;

        // Ağırlık oranını al
        float weightRatio = carrier != null ? carrier.WeightRatio : 0f;
        if (inventory != null) weightRatio = Mathf.Max(weightRatio, inventory.LoadRatio * 0.5f); // dolu çanta ivmeyi de biraz ağırlaştırır

        // Tuşa basılıyor mu (hızlanma) yoksa bırakıldı mı (frenleme)?
        bool isMovingInput = inputDir.sqrMagnitude > 0.01f;
        float targetRate;

        if (isMovingInput)
        {
            // İleri/yana koşarken hızlanma ivmesi
            targetRate = Mathf.Lerp(baseAcceleration, heavyAcceleration, weightRatio);
        }
        else
        {
            // Elini tuştan çektiğinde sert durma/frenleme ivmesi (kaymayı önler)
            targetRate = Mathf.Lerp(baseDeceleration, heavyDeceleration, weightRatio);
        }

        // Hızı uygula
        currentHorizontalVelocity = Vector3.MoveTowards(currentHorizontalVelocity, targetVelocity, targetRate * Time.deltaTime);

        characterController.Move(currentHorizontalVelocity * Time.deltaTime);

        // İtilme: kısa bir savrulma
        if (pushVelocity.sqrMagnitude > 0.0004f)
        {
            characterController.Move(pushVelocity * Time.deltaTime);
            pushVelocity = Vector3.MoveTowards(pushVelocity, Vector3.zero, 14f * Time.deltaTime);
        }

        // Zıplama
        if (!inputBlocked && Keyboard.current.spaceKey.wasPressedThisFrame && isGrounded && !isCrouching && !carryingHeavy)
        {
            verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        float appliedGravity = gravity;
        if (verticalVelocity.y < 0)
        {
            appliedGravity *= fallMultiplier;
        }

        verticalVelocity.y += appliedGravity * Time.deltaTime;
        characterController.Move(verticalVelocity * Time.deltaTime);
    }

    /// <summary>Başka biri itti: yatay savrulma (+ biraz yukarı). PushSystem çağırır.</summary>
    public void AddPush(Vector3 velocity)
    {
        pushVelocity += new Vector3(velocity.x, 0f, velocity.z);
        if (velocity.y > 0f && characterController != null && characterController.isGrounded)
            verticalVelocity.y = Mathf.Max(verticalVelocity.y, velocity.y);
    }

    private void HandleCrouch()
    {
        if (Keyboard.current == null) return;

        isCrouching = !ChatUI.IsTyping && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed);

        float targetHeight = isCrouching ? crouchHeight : standingHeight;
        float targetCenterY = isCrouching ? -(standingHeight - crouchHeight) / 2f : 0f;
        float targetCamY = isCrouching ? crouchCameraY : StandCamY;

        characterController.height = Mathf.Lerp(characterController.height, targetHeight, Time.deltaTime * crouchTransitionSpeed);
        characterController.center = Vector3.Lerp(characterController.center, new Vector3(0, targetCenterY, 0), Time.deltaTime * crouchTransitionSpeed);

        currentBaseCamY = Mathf.Lerp(currentBaseCamY, targetCamY, Time.deltaTime * crouchTransitionSpeed);
    }

    private bool CheckIfOnStairs()
    {
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 1.5f))
        {
            return hit.collider.CompareTag(stairsTag);
        }
        return false;
    }

    private void HandleStairStepEffect()
    {
        if (playerCamera == null) return;

        Vector3 camPos = playerCamera.localPosition;

        landOffset = Mathf.Lerp(landOffset, 0f, Time.deltaTime * 10f);

        Vector3 horizontalVelocity = new Vector3(characterController.velocity.x, 0, characterController.velocity.z);
        float speed = horizontalVelocity.magnitude;
        bool onStairs = CheckIfOnStairs();

        if (characterController.isGrounded && onStairs && speed > 0.2f)
        {
            stepTimer += Time.deltaTime * stepFrequency;
            float stepCurve = Mathf.Abs(Mathf.Sin(stepTimer));
            float targetOffsetY = -stepCurve * stepDropAmount;
            float targetTilt = stepCurve * stepTiltAmount;

            camPos.y = currentBaseCamY + targetOffsetY + landOffset;
            currentCamTilt = Mathf.Lerp(currentCamTilt, targetTilt, Time.deltaTime * recoverySpeed);
        }
        else
        {
            stepTimer = 0f;
            camPos.y = Mathf.Lerp(camPos.y, currentBaseCamY + landOffset, Time.deltaTime * recoverySpeed);
            currentCamTilt = Mathf.Lerp(currentCamTilt, 0f, Time.deltaTime * recoverySpeed);
        }

        // Kamera sarsıntısı: önceki karenin sapmasını geri al, yenisini ekle
        camPos -= lastShakeOffset;
        lastShakeOffset = ShakeAmount > 0.0001f
            ? new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * (ShakeAmount * 0.06f)
            : Vector3.zero;
        camPos += lastShakeOffset;
        playerCamera.localPosition = camPos;
    }

    private void HandleMouseLook()
    {
        if (playerCamera == null || Mouse.current == null) return;
        if (Cursor.lockState != CursorLockMode.Locked || ChatUI.IsTyping) return; // Menü/sohbet açıkken kamera dönmesin
        if (LookFrozen) return; // F5 önizleme: orta tuşla karakterin etrafında dönülüyor

        float turnFactor = carrier != null ? carrier.TurnSensitivityMultiplier : 1f;
        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * (lookSensitivity * turnFactor * GameSettings.MouseSensitivity);

        rotationX -= GameSettings.InvertY ? -mouseDelta.y : mouseDelta.y;
        rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);

        playerCamera.localRotation = Quaternion.Euler(rotationX + currentCamTilt, 0f, 0f);
        if (lookOnly)
        {
            // Dolaptan dışarı: sağa / sola bakış sınırlı (arkaya, dolabın duvarına dönmesin)
            float rel = Mathf.DeltaAngle(lookCenterYaw, transform.eulerAngles.y) + mouseDelta.x;
            rel = Mathf.Clamp(rel, -HideYawLimit, HideYawLimit);
            transform.rotation = Quaternion.Euler(0f, lookCenterYaw + rel, 0f);
            return;
        }
        transform.Rotate(Vector3.up * mouseDelta.x);
    }
}
