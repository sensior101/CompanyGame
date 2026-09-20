using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
    // 1인칭 시야각
    public float firstPersonFOV = 85f;
    // 3인칭 시야각
    public float thirdPersonFOV = 75f;

    [Header("Third-Person Camera")]
    public float distance = 8f;
    public float minDistance = 3f;
    public float maxDistance = 20f;
    public float targetHeight = 2.2f;

    [Header("Mouse Rotation")]
    public float mouseSensitivity = 0.22f;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    [Header("Mouse Zoom")]
    public float zoomSpeed = 0.3f;

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
    [Header("Obstacle Transparency")]

    public bool enableObstacleFade = true;

    [Range(0f, 1f)]
    public float obstacleAlpha = 0.25f;

    public LayerMask obstacleLayers = ~0;

    public float obstacleCheckRadius = 0.25f;


    // 원래 머티리얼 저장
    private readonly Dictionary<Renderer, Material[]> originalMaterials
        = new Dictionary<Renderer, Material[]>();

    // 현재 카메라와 플레이어 사이에 있는 장애물
    private readonly HashSet<Renderer> currentObstacles
        = new HashSet<Renderer>();

    // 복구할 장애물
    private readonly List<Renderer> restoreBuffer
        = new List<Renderer>();

    // 투명화용으로 생성한 머티리얼
    private readonly Dictionary<Renderer, Material[]> fadedMaterials
        = new Dictionary<Renderer, Material[]>();

    private void Awake()
    {
        playerCamera = GetComponent<Camera>();
    }
    private void Start()
    {
        playerCamera.orthographic = false;
        yaw = transform.eulerAngles.y;

        // 게임 시작 시 선택된 시점의 FOV 적용
        playerCamera.fieldOfView = firstPerson
            ? firstPersonFOV
            : thirdPersonFOV;

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

            // 시점에 따라 FOV 변경
            playerCamera.fieldOfView = firstPerson
                ? firstPersonFOV
                : thirdPersonFOV;

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

            // 1인칭에서는 장애물 투명화 비활성화
            RestoreAllObstacles();
        }
        else
        {
            Vector3 focusPosition =
                target.position + Vector3.up * targetHeight;

            transform.position =
                focusPosition - rotation * Vector3.forward * distance;

            transform.rotation = rotation;

            // 3인칭에서 플레이어를 가리는 장애물 감지
            if (enableObstacleFade)
            {
                UpdateObstacleFade(focusPosition);
            }
            else
            {
                RestoreAllObstacles();
            }
        }
    }

        private void UpdateObstacleFade(Vector3 focusPosition)
    {
        currentObstacles.Clear();

        Vector3 origin = transform.position;

        Vector3 direction = focusPosition - origin;

        float checkDistance = direction.magnitude;

        if (checkDistance <= 0.01f)
        {
            RestoreAllObstacles();
            return;
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            obstacleCheckRadius,
            direction.normalized,
            checkDistance,
            obstacleLayers,
            QueryTriggerInteraction.Ignore
        );

        foreach (RaycastHit hit in hits)
        {
            // 플레이어 자신은 투명화하지 않음
            if (hit.collider.transform == target ||
                hit.collider.transform.IsChildOf(target))
            {
                continue;
            }

            Renderer renderer =
                hit.collider.GetComponent<Renderer>();

            if (renderer == null)
            {
                renderer =
                    hit.collider.GetComponentInParent<Renderer>();
            }

            if (renderer == null)
                continue;

            currentObstacles.Add(renderer);

            if (!originalMaterials.ContainsKey(renderer))
            {
                FadeObstacle(renderer);
            }
        }

        // 더 이상 플레이어를 가리지 않는 장애물 복구
        restoreBuffer.Clear();

        foreach (Renderer renderer in originalMaterials.Keys)
        {
            if (!currentObstacles.Contains(renderer))
            {
                restoreBuffer.Add(renderer);
            }
        }

        foreach (Renderer renderer in restoreBuffer)
        {
            RestoreObstacle(renderer);
        }
    }


    // ========================================
    // 장애물 반투명 처리
    // ========================================

    private void FadeObstacle(Renderer renderer)
    {
        Material[] originals = renderer.sharedMaterials;

        Material[] transparentMaterials =
            new Material[originals.Length];

        for (int i = 0; i < originals.Length; i++)
        {
            if (originals[i] == null)
                continue;

            Material material = new Material(originals[i]);

            // URP Lit / Simple Lit 투명화
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);

                material.SetOverrideTag(
                    "RenderType",
                    "Transparent"
                );

                material.SetInt(
                    "_SrcBlend",
                    (int)BlendMode.SrcAlpha
                );

                material.SetInt(
                    "_DstBlend",
                    (int)BlendMode.OneMinusSrcAlpha
                );

                material.SetInt("_ZWrite", 0);

                material.EnableKeyword(
                    "_SURFACE_TYPE_TRANSPARENT"
                );

                material.DisableKeyword(
                    "_SURFACE_TYPE_OPAQUE"
                );

                material.renderQueue =
                    (int)RenderQueue.Transparent;
            }

            // 투명도 설정
            if (material.HasProperty("_BaseColor"))
            {
                Color color = material.GetColor("_BaseColor");

                color.a = obstacleAlpha;

                material.SetColor("_BaseColor", color);
            }

            transparentMaterials[i] = material;
        }

        originalMaterials.Add(renderer, originals);

        fadedMaterials.Add(renderer, transparentMaterials);

        renderer.sharedMaterials = transparentMaterials;
    }


    // ========================================
    // 장애물 원래 모습으로 복구
    // ========================================

    private void RestoreObstacle(Renderer renderer)
    {
        if (!originalMaterials.TryGetValue(
            renderer,
            out Material[] originals))
        {
            return;
        }

        if (renderer != null)
        {
            renderer.sharedMaterials = originals;
        }

        if (fadedMaterials.TryGetValue(
            renderer,
            out Material[] transparentMaterials))
        {
            foreach (Material material in transparentMaterials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }

            fadedMaterials.Remove(renderer);
        }

        originalMaterials.Remove(renderer);
    }


    // ========================================
    // 모든 장애물 복구
    // ========================================

    private void RestoreAllObstacles()
    {
        restoreBuffer.Clear();

        foreach (Renderer renderer in originalMaterials.Keys)
        {
            restoreBuffer.Add(renderer);
        }

        foreach (Renderer renderer in restoreBuffer)
        {
            RestoreObstacle(renderer);
        }

        currentObstacles.Clear();
    }


    // ========================================
    // 카메라 비활성화 시 원상 복구
    // ========================================

    private void OnDisable()
    {
        RestoreAllObstacles();
    }

    private void OnDestroy()
    {
        RestoreAllObstacles();
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