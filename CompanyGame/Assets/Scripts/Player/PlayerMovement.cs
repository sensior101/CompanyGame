using UnityEngine;


[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 3f;
    public float sprintMultiplier = 1.5f;

    [Header("Camera Reference")]
    public Camera viewCamera;

    [Header("Spawn")]
    public Vector3 spawn = new Vector3(-17.5f, 1.08f, -27f);

    private CharacterController motor;
    private PlayerVehicle vehicle;
    private PlayerStats stats;
    private PlayerHitReaction hitReaction;
    private PlayerCombat combat;
    private float verticalSpeed;
    private Vector3 lastSafePosition;
    private bool settling;

    protected virtual void Awake()
    {
        motor = GetComponent<CharacterController>();

        motor.radius = BodySize.Radius;
        motor.height = BodySize.Height;
        motor.center = Vector3.up * (BodySize.Height * .5f);
        motor.stepOffset = BodySize.StepHeight;
        motor.slopeLimit = 45f;
        motor.skinWidth = 0.03f;
        motor.minMoveDistance = 0f;

        lastSafePosition = spawn;
    }

    protected virtual void Start()
    {
        if (!viewCamera)
            viewCamera = Camera.main;
    }

    // Re-enabled after a map load: the first frame carries the whole load hitch
    // in deltaTime, so let it pass before applying gravity again.
    protected virtual void OnEnable()
    {
        verticalSpeed = 0f;
        settling = true;
    }

    protected virtual void Update()
    {
        if (settling)
        {
            settling = false;
            return;
        }

        // Keep the player stationary through choices, handoffs, replies and the
        // return blend without disabling the component needed by trade entry.
        if (viewCamera && viewCamera.TryGetComponent<PlayerCameraController>(out var cameraController) &&
            cameraController.IsDialogueCameraActive)
        {
            ApplyCombatKnockback();
            verticalSpeed = 0f;
            return;
        }
        if (!vehicle) vehicle = GetComponent<PlayerVehicle>();
        Vector2 input = Vector2.zero;

        bool sprint = false;
        bool reset = false;

        if (!hitReaction) hitReaction = GetComponent<PlayerHitReaction>();
        if (!combat) combat = GetComponent<PlayerCombat>();
        input = hitReaction && hitReaction.BlocksMovement ? Vector2.zero : GameInput.Move;
        sprint = GameInput.SprintHeld;
        reset = GameInput.ResetPositionPressed;

        // Home 키: 플레이어 위치 초기화
        if (reset)
        {
            ResetToSpawn();
            return;
        }

        // 카메라 방향을 기준으로 이동
        Vector3 forward = viewCamera
            ? Vector3.ProjectOnPlane(
                viewCamera.transform.forward,
                Vector3.up
            ).normalized
            : Vector3.forward;

        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 motion = Vector3.ClampMagnitude(
            forward * input.y + right * input.x,
            1f
        );

        // 중력 적용
        if (motor.isGrounded && verticalSpeed < 0)
            verticalSpeed = -2f;

        verticalSpeed += Physics.gravity.y * Time.deltaTime;

        // 이동 속도
        float speed = moveSpeed;
        if (combat && combat.IsSwinging) speed *= combat.attackingMoveMultiplier;

        if (sprint)
            speed *= sprintMultiplier;

        // 플레이어 이동
        Vector3 beforeMove = transform.position;
        bool riding = vehicle && vehicle.IsRiding;
        Vector3 horizontal = riding ? vehicle.RideMotion(motion, sprint, Time.deltaTime) : motion * speed;
        // A car drives itself with its body box while the CharacterController is off.
        bool driving = riding && vehicle.IsCar;
        if (driving) verticalSpeed = 0f;
        else motor.Move(
            (
                horizontal +
                Vector3.up * verticalSpeed
            ) * Time.deltaTime
        );

        if (!riding && motion.sqrMagnitude > .001f && motor.isGrounded)
        {
            if (!stats) stats = GetComponent<PlayerStats>();
            float travelled = Vector3.ProjectOnPlane(transform.position - beforeMove, Vector3.up).magnitude;
            // Count achieved movement, excluding blocked input and teleports.
            if (stats) stats.RecordTravel(Mathf.Min(travelled, speed * Time.deltaTime), speed, sprint);
        }


        // 이동 방향으로 캐릭터 회전
        if (!riding && combat && combat.IsSwinging)
        {
            Vector3 facing = Vector3.ProjectOnPlane(combat.AttackDirection, Vector3.up);
            if (facing.sqrMagnitude > .001f) transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(facing), combat.attackTurnSpeed * Time.deltaTime);
        }
        else if (!riding && motion.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(motion);

        // Applied after voluntary travel accounting: a hit never spends walking stamina.
        if (!riding) ApplyCombatKnockback();

        // 안전한 위치 저장
        if (motor.isGrounded && transform.position.y > -2.2f)
            lastSafePosition = transform.position;

        // 맵 밖으로 떨어졌을 때 복귀
        if (transform.position.y < -7f)
            Teleport(lastSafePosition);
    }

    void ApplyCombatKnockback()
    {
        if (!hitReaction) hitReaction = GetComponent<PlayerHitReaction>();
        if (!motor || !motor.enabled || !hitReaction) return;
        Vector3 knockback = hitReaction.ConsumeKnockback(Time.deltaTime);
        if (knockback.sqrMagnitude > 0f) motor.Move(hitReaction.SafeDisplacement(knockback));
    }

    public void ResetToSpawn()
    {
        lastSafePosition = spawn;

        Teleport(spawn);
    }

    private void Teleport(Vector3 position)
    {
        if (!motor)
            motor = GetComponent<CharacterController>();

        motor.enabled = false;

        // Spawn points sit exactly on the floor; lift a little so the capsule
        // never starts inside a one-sided mesh collider and drops through.
        transform.position = position + Vector3.up * .1f;

        motor.enabled = !(vehicle && vehicle.IsCar);

        verticalSpeed = 0f;

        Physics.SyncTransforms();
    }
}