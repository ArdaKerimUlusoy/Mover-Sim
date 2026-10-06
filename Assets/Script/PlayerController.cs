using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Hareket Hýzlarý")]
    [SerializeField] private float walkSpeed = 4.5f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float crouchSpeed = 2.2f;

    [Header("Ýnsan Zýplamasý & Yerçekimi")]
    [Tooltip("Gerçekçi insan zýplama yüksekliði (metre cinsinden)")]
    [SerializeField] private float jumpHeight = 0.55f;
    [SerializeField] private float gravity = -20f;
    [Tooltip("Ýnerken uygulanan ekstra yerçekimi (havada süzülmeyi önler)")]
    [SerializeField] private float fallMultiplier = 1.8f;
    [Tooltip("Yere indiðinde kameranýn hafifçe aþaðý esneme miktarý")]
    [SerializeField] private float landBobAmount = 0.08f;

    [Header("Eðilme (Crouch) Ayarlarý")]
    [SerializeField] private float standingHeight = 2f;
    [SerializeField] private float crouchHeight = 1f;
    [SerializeField] private float standingCameraY = 0.6f;
    [SerializeField] private float crouchCameraY = 0.1f;
    [SerializeField] private float crouchTransitionSpeed = 10f;

    [Header("Kamera & Bakýþ")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private float lookSensitivity = 0.05f;
    [SerializeField] private float lookXLimit = 85f;

    [Header("Merdiven Adým Hissi")]
    [SerializeField] private string stairsTag = "Stairs";
    [SerializeField] private float stepFrequency = 11f;
    [SerializeField] private float stepDropAmount = 0.08f;
    [SerializeField] private float stepTiltAmount = 1.2f;
    [SerializeField] private float recoverySpeed = 12f;

    private CharacterController characterController;
    private Vector3 velocity;
    private float rotationX = 0f;
    private bool isCrouching = false;
    private bool wasGrounded = true;
    private float currentBaseCamY;
    private float currentCamTilt = 0f;
    private float stepTimer = 0f;
    private float landOffset = 0f;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        currentBaseCamY = standingCameraY;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        HandleMouseLook();
        HandleCrouch();
        HandleMovement();
        HandleStairStepEffect();
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

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        float horizontal = 0f;
        float vertical = 0f;

        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) vertical += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) vertical -= 1f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) horizontal -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) horizontal += 1f;

        Vector3 moveDirection = (transform.right * horizontal + transform.forward * vertical).normalized;

        float currentSpeed = walkSpeed;
        if (isCrouching) currentSpeed = crouchSpeed;
        else if (Keyboard.current.leftShiftKey.isPressed) currentSpeed = sprintSpeed;

        characterController.Move(moveDirection * currentSpeed * Time.deltaTime);

        if (Keyboard.current.spaceKey.wasPressedThisFrame && isGrounded && !isCrouching)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        float appliedGravity = gravity;
        if (velocity.y < 0)
        {
            appliedGravity *= fallMultiplier;
        }

        velocity.y += appliedGravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);
    }

    private void HandleCrouch()
    {
        if (Keyboard.current == null) return;

        isCrouching = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.cKey.isPressed;

        float targetHeight = isCrouching ? crouchHeight : standingHeight;
        float targetCenterY = isCrouching ? -(standingHeight - crouchHeight) / 2f : 0f;
        float targetCamY = isCrouching ? crouchCameraY : standingCameraY;

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

        playerCamera.localPosition = camPos;
    }

    private void HandleMouseLook()
    {
        if (playerCamera == null || Mouse.current == null) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * lookSensitivity;

        rotationX -= mouseDelta.y;
        rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);

        playerCamera.localRotation = Quaternion.Euler(rotationX + currentCamTilt, 0f, 0f);
        transform.Rotate(Vector3.up * mouseDelta.x);
    }
}