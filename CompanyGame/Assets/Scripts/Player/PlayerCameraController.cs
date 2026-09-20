using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class PlayerCameraController : MonoBehaviour
{
    [Header("Player")]
    public Transform target;

    [Header("Player Visuals")]
    public GameObject femaleVisuals;
    public GameObject maleVisuals;

    [Header("View Mode")]
    public bool firstPerson = false;
    public float firstPersonEyeHeight = 1.6f;

    [Header("Third-Person Camera")]
    public float distance = 5f;
    public float minDistance = 2f;
    public float maxDistance = 15f;
    public float targetHeight = 1.2f;

    [Header("Mouse Rotation")]
    public float mouseSensitivity = 0.22f;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    [Header("Mouse Zoom")]
    public float zoomSpeed = 0.12f;

    [Header("Walking Camera Bob")]
    public bool enableHeadBob = true;

    public float walkBobSpeed = 10f;
    public float walkBobAmount = 0.035f;

    public float runBobSpeed = 15f;
    public float runBobAmount = 0.065f;

    public float thirdPersonBobMultiplier = 0.35f;

    public float movementThreshold = 0.1f;
    public float runSpeedThreshold = 5f;

    public float bobSmoothSpeed = 10f;

    private float bobTimer;
    private float currentBobOffset;
    private Vector3 previousTargetPosition;
    private bool hasPreviousTargetPosition;

    private float yaw;
    private float pitch = 25f;
    private Camera playerCamera;

    private void Awake()
    {
        playerCamera = GetComponent<Camera>();
    }
    private void Start()
    {
        playerCamera.orthographic = false;
        yaw = transform.eulerAngles.y;

        UpdatePlayerVisuals();
    }

    private void LateUpdate()
    {
        if (!target)
            return;

        Vector3 currentTargetPosition = target.position;

        float horizontalSpeed = 0f;

        if (hasPreviousTargetPosition && Time.deltaTime > 0f)
        {
            Vector3 movement = currentTargetPosition - previousTargetPosition;
            movement.y = 0f;

            horizontalSpeed = movement.magnitude / Time.deltaTime;
        }

        previousTargetPosition = currentTargetPosition;
        hasPreviousTargetPosition = true;

        Vector2 mouseDelta = Vector2.zero;
        float wheel = 0f;
        bool orbit = false;
        bool switchView = false;

#if ENABLE_INPUT_SYSTEM

        if (Mouse.current != null)
        {
            mouseDelta = Mouse.current.delta.ReadValue();
            orbit = Mouse.current.rightButton.isPressed;
            wheel = Mouse.current.scroll.ReadValue().y / 120f;
        }

        if (Keyboard.current != null)
        {
            switchView = Keyboard.current.tabKey.wasPressedThisFrame;
        }

#elif ENABLE_LEGACY_INPUT_MANAGER

        mouseDelta = new Vector2(
            Input.GetAxis("Mouse X"),
            Input.GetAxis("Mouse Y")
        ) * 10f;

        orbit = Input.GetMouseButton(1);
        wheel = Input.mouseScrollDelta.y;
        switchView = Input.GetKeyDown(KeyCode.Tab);

#endif

        if (switchView)
        {
            firstPerson = !firstPerson;
            UpdatePlayerVisuals();
        }

        // 기존 조작 유지: 우클릭 중에만 카메라 회전
        if (orbit)
        {
            yaw += mouseDelta.x * mouseSensitivity;

            pitch = Mathf.Clamp(
                pitch - mouseDelta.y * mouseSensitivity,
                firstPerson ? -85f : minPitch,
                firstPerson ? 85f : maxPitch
            );
        }

        // 3인칭에서만 휠로 카메라 거리 조절
        if (!firstPerson)
        {
            distance *= Mathf.Exp(-wheel * zoomSpeed);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        float targetBobOffset = 0f;

        if (enableHeadBob && firstPerson && horizontalSpeed > movementThreshold)
        {
            bool isRunning = horizontalSpeed >= runSpeedThreshold;

            float bobSpeed = isRunning ? runBobSpeed : walkBobSpeed;
            float bobAmount = isRunning ? runBobAmount : walkBobAmount;

            bobTimer += Time.deltaTime * bobSpeed;

            targetBobOffset = Mathf.Sin(bobTimer) * bobAmount;

            if (!firstPerson)
            {
                targetBobOffset *= thirdPersonBobMultiplier;
            }
        }
        else
        {
            // 멈췄을 때 카메라가 중립 위치로 돌아오도록 처리
            bobTimer = 0f;
        }

        currentBobOffset = Mathf.Lerp(
            currentBobOffset,
            targetBobOffset,
            Time.deltaTime * bobSmoothSpeed
        );

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

        if (firstPerson)
        {
            transform.position =
                target.position
                + Vector3.up * (firstPersonEyeHeight + currentBobOffset);

            transform.rotation = rotation;
        }
        else
        {
            Vector3 focusPosition =
    target.position + Vector3.up * targetHeight;

            transform.position =
                focusPosition - rotation * Vector3.forward * distance;

            transform.rotation = rotation;
        }
    }
    private void UpdatePlayerVisuals()
    {
        SetVisualRenderers(femaleVisuals, !firstPerson);
        SetVisualRenderers(maleVisuals, !firstPerson);
    }

    private void SetVisualRenderers(GameObject visuals, bool visible)
    {
        if (visuals == null)
            return;

        Renderer[] renderers =
            visuals.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            renderer.enabled = visible;
        }
    }
}