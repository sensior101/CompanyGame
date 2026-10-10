using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


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
    public float initialPitch = 25f;
    public float firstPersonEyeHeight = 1.6f;
    // 1인칭 시야각
    public float firstPersonFOV = 85f;
    // 3인칭 시야각
    public float thirdPersonFOV = 75f;

    [Header("Third-Person Camera")]
    public float distance = 4f;
    public float targetHeight = 2.2f;

    [Header("Mouse Rotation")]
    public float mouseSensitivity = 0.22f;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    [Header("Walking Camera Bob")]
    public bool enableHeadBob = true;

    public float walkBobSpeed = 10f;
    public float walkBobAmount = 0.1f;

    public float runBobSpeed = 20f;
    public float runBobAmount = 0.1f;

    public float thirdPersonBobMultiplier = 0.35f;

    public float movementThreshold = 0.1f;
    public float runSpeedThreshold = 5f;

    public float bobSmoothSpeed = 10f;

    private float bobTimer;
    private float currentBobOffset;
    private bool wasRiding, previousFirstPerson;
    private Vector3 previousTargetPosition;
    private bool hasPreviousTargetPosition;

    [Header("Cinematic Dialogue")]
    [Min(.1f)] public float dialogueBlendSeconds = .65f;
    [Min(.1f)] public float dialogueReturnSeconds = .5f;
    [Range(30f, 85f)] public float dialogueFOV = 55f;
    [Min(1.5f)] public float dialogueDistance = 2.1f;
    [Range(0f, 1f)] public float dialogueSideOffset = .35f;

    public bool IsDialogueCameraActive => dialogueActive || dialogueReturning;
    int menuInputLocks;
    Transform dialogueSubject, dialogueHead;
    Vector3 dialogueLocalFocus, blendFromPosition, dialogueShotPosition, dialogueShotFocus;
    Quaternion dialogueShotRotation;
    Quaternion blendFromRotation;
    float blendFromFov, dialogueStartedAt, gameplayFov, dialogueSide, dialogueShotFov;
    bool dialogueActive, dialogueReturning;

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
        pitch = initialPitch;

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

        if (SceneLoadManager.IsLoading && IsDialogueCameraActive) EndDialogueCamera(true);
        if (dialogueActive && (!dialogueSubject || !dialogueSubject.gameObject.activeInHierarchy)) EndDialogueCamera();
        if (dialogueActive)
        {
            bobTimer = currentBobOffset = 0f;
            UpdateDialogueShot();
            return;
        }
        // A normal menu freezes the view, but a dialogue/return blend keeps rendering.
        if (menuInputLocks > 0 && !dialogueReturning) return;

        Vector2 mouseDelta = Vector2.zero;
        float wheel = 0f;
        bool orbit = false;
        bool switchView = false;

        mouseDelta = GameInput.PointerDelta;
        bool cameraInputAllowed = menuInputLocks == 0 && !dialogueReturning && !InputFocus.GameplayBlocked();
        orbit = GameInput.OrbitHeld && cameraInputAllowed;
        wheel = GameInput.Scroll;
        switchView = GameInput.ViewTogglePressed && cameraInputAllowed;

        if (InputFocus.ScrollCaptured()) wheel = 0f;

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

        var vehicle = target.GetComponent<PlayerVehicle>();
        bool riding = vehicle && vehicle.IsRiding;
        if (riding != wasRiding || firstPerson != previousFirstPerson)
        {
            bobTimer = currentBobOffset = 0f;
            wasRiding = riding; previousFirstPerson = firstPerson;
        }
        float targetBobOffset = 0f;

        if (!riding && !dialogueReturning && enableHeadBob && firstPerson && horizontalSpeed > movementThreshold)
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

        currentBobOffset = riding || dialogueReturning ? 0f : Mathf.Lerp(
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
            UpdateDialogueReturn();

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
            UpdateDialogueReturn();

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

    public void HoldMenuInput()
    {
        menuInputLocks++;
        if (!IsDialogueCameraActive) RestoreAllObstacles();
    }
    public void ReleaseMenuInput() => menuInputLocks = Mathf.Max(0, menuInputLocks - 1);

    public void BeginDialogueCamera(Transform subject, Transform head, Vector3 fallbackFocus)
    {
        if (!isActiveAndEnabled || !target || !subject) return;
        if (dialogueActive && dialogueSubject == subject) return;
        if (!IsDialogueCameraActive) gameplayFov = playerCamera.fieldOfView;
        CaptureDialogueBlend();
        dialogueSubject = subject; dialogueHead = head;
        dialogueLocalFocus = subject.InverseTransformPoint(fallbackFocus);
        Vector3 direction = Vector3.ProjectOnPlane(target.position - subject.position, Vector3.up).normalized;
        if (direction.sqrMagnitude < .01f) direction = -subject.forward;
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        dialogueSide = Vector3.Dot(transform.position - subject.position, side) < 0f ? -1f : 1f;
        dialogueActive = true; dialogueReturning = false;
        ComposeDialogueShot();
        bobTimer = currentBobOffset = 0f;
        RestoreAllObstacles();
    }

    public void EndDialogueCamera(bool immediately = false)
    {
        if (!IsDialogueCameraActive) return;
        if (!dialogueActive && !immediately) return;
        dialogueActive = false;
        dialogueSubject = dialogueHead = null;
        if (immediately)
        {
            dialogueReturning = false;
            if (playerCamera) playerCamera.fieldOfView = gameplayFov;
            if (target)
            {
                Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
                Vector3 position = firstPerson ? target.position + Vector3.up * firstPersonEyeHeight :
                    target.position + Vector3.up * targetHeight - rotation * Vector3.forward * distance;
                transform.SetPositionAndRotation(position, rotation);
            }
            RestoreAllObstacles();
            return;
        }
        CaptureDialogueBlend();
        dialogueReturning = true;
    }

    void CaptureDialogueBlend()
    {
        blendFromPosition = transform.position; blendFromRotation = transform.rotation;
        blendFromFov = playerCamera.fieldOfView; dialogueStartedAt = Time.unscaledTime;
    }

    void ComposeDialogueShot()
    {
        const float minimumDistance = 1.5f;
        const float collisionRadius = .12f;
        Vector3 focus = (dialogueHead ? dialogueHead.position : dialogueSubject.TransformPoint(dialogueLocalFocus)) - Vector3.up * .08f;
        // Cast at camera height: a chest-height cast can hit a counter edge and
        // incorrectly pull an otherwise unobstructed camera into the NPC's body.
        Vector3 pivot = focus + Vector3.up * .2f;
        Vector3 direction = Vector3.ProjectOnPlane(target.position - dialogueSubject.position, Vector3.up);
        direction = direction.sqrMagnitude > .01f ? direction.normalized : -dialogueSubject.forward;
        float range = Mathf.Max(minimumDistance, dialogueDistance);
        float bestDistance = 0f;
        Vector3 bestPosition = blendFromPosition;

        // Compose once. Try nearby angles before reducing the shot distance, and
        // never turn an obstacle into an extreme close-up of the speaker.
        for (int attempt = 0; attempt <= 12; attempt++)
        {
            float angle = ((attempt + 1) / 2) * 15f * (attempt % 2 == 1 ? dialogueSide : -dialogueSide);
            Vector3 candidateDirection = Quaternion.AngleAxis(angle, Vector3.up) * direction;
            Vector3 ray = candidateDirection * range + Vector3.Cross(Vector3.up, candidateDirection) * (dialogueSideOffset * dialogueSide);
            float clearDistance = ray.magnitude;
            foreach (var hit in Physics.SphereCastAll(pivot, collisionRadius, ray.normalized, clearDistance, obstacleLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(target) && !hit.transform.IsChildOf(dialogueSubject))
                    clearDistance = Mathf.Min(clearDistance, Mathf.Max(0f, hit.distance - .08f));
            if (clearDistance < minimumDistance || clearDistance <= bestDistance) continue;

            Vector3 candidate = pivot + ray.normalized * clearDistance;
            bool occupied = false;
            foreach (var collider in Physics.OverlapSphere(candidate, collisionRadius, obstacleLayers, QueryTriggerInteraction.Ignore))
                if (!collider.transform.IsChildOf(target) && !collider.transform.IsChildOf(dialogueSubject))
                { occupied = true; break; }
            if (occupied) continue;
            bestDistance = clearDistance;
            bestPosition = candidate;
            if (clearDistance >= ray.magnitude - .001f) break;
        }

        dialogueShotFocus = focus;
        dialogueShotPosition = bestPosition;
        // If the room cannot fit a readable shot, retain the existing view and
        // FOV instead of zooming through nearby furniture or into the speaker.
        dialogueShotRotation = bestDistance > 0f ? Quaternion.LookRotation(focus - bestPosition, Vector3.up) : blendFromRotation;
        dialogueShotFov = bestDistance > 0f ? dialogueFOV : blendFromFov;
    }

    void UpdateDialogueShot()
    {
        float progress = (Time.unscaledTime - dialogueStartedAt) / Mathf.Max(.1f, dialogueBlendSeconds);
        if (progress < 1f)
        {
            float blend = Mathf.SmoothStep(0f, 1f, progress);
            transform.SetPositionAndRotation(Vector3.Lerp(blendFromPosition, dialogueShotPosition, blend), Quaternion.Slerp(blendFromRotation, dialogueShotRotation, blend));
            playerCamera.fieldOfView = Mathf.Lerp(blendFromFov, dialogueShotFov, blend);
        }
        else
        {
            transform.SetPositionAndRotation(dialogueShotPosition, dialogueShotRotation);
            playerCamera.fieldOfView = dialogueShotFov;
        }
        if (enableObstacleFade) UpdateObstacleFade(dialogueShotFocus, dialogueSubject);
        else RestoreAllObstacles();
    }

    void UpdateDialogueReturn()
    {
        if (!dialogueReturning) return;
        float progress = (Time.unscaledTime - dialogueStartedAt) / Mathf.Max(.1f, dialogueReturnSeconds);
        float blend = Mathf.SmoothStep(0f, 1f, progress);
        transform.SetPositionAndRotation(Vector3.Lerp(blendFromPosition, transform.position, blend), Quaternion.Slerp(blendFromRotation, transform.rotation, blend));
        playerCamera.fieldOfView = Mathf.Lerp(blendFromFov, gameplayFov, blend);
        if (progress >= 1f) dialogueReturning = false;
    }

    private void UpdateObstacleFade(Vector3 focusPosition, Transform focusSubject = null)
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
                hit.collider.transform.IsChildOf(target) ||
                (focusSubject && hit.collider.transform.IsChildOf(focusSubject)))
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
        EndDialogueCamera(true);
        RestoreAllObstacles();
    }

    private void OnDestroy()
    {
        RestoreAllObstacles();
    }
    /// <summary>Follow this player and hide its body in first person.</summary>
    public void Bind(Transform player, GameObject female, GameObject male)
    {
        target = player;
        femaleVisuals = female;
        maleVisuals = male;
        UpdatePlayerVisuals();
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