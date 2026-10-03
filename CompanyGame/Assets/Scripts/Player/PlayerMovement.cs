using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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
    private float verticalSpeed;
    private Vector3 lastSafePosition;

    protected virtual void Awake()
    {
        motor = GetComponent<CharacterController>();

        motor.radius = 0.35f;
        motor.height = 1.8f;
        motor.center = Vector3.up * 0.9f;
        motor.stepOffset = 0.23f;
        motor.slopeLimit = 45f;
        motor.skinWidth = 0.02f;
        motor.minMoveDistance = 0f;

        lastSafePosition = spawn;
    }

    protected virtual void Start()
    {
        if (!viewCamera)
            viewCamera = Camera.main;
    }

    protected virtual void Update()
    {
        Vector2 input = Vector2.zero;

        bool sprint = false;
        bool reset = false;

#if ENABLE_INPUT_SYSTEM

        var keys = Keyboard.current;

        if (keys != null)
        {
            input.x =
                (keys.dKey.isPressed ? 1 : 0) -
                (keys.aKey.isPressed ? 1 : 0);

            input.y =
                (keys.wKey.isPressed ? 1 : 0) -
                (keys.sKey.isPressed ? 1 : 0);

            sprint = keys.leftShiftKey.isPressed;

            reset = keys.homeKey.wasPressedThisFrame;
        }

#elif ENABLE_LEGACY_INPUT_MANAGER

        input = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        );

        sprint = Input.GetKey(KeyCode.LeftShift);

        reset = Input.GetKeyDown(KeyCode.Home);

#endif

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

        if (sprint)
            speed *= sprintMultiplier;

        // 플레이어 이동
        motor.Move(
            (
                motion * speed +
                Vector3.up * verticalSpeed
            ) * Time.deltaTime
        );

        // 이동 방향으로 캐릭터 회전
        if (motion.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(motion);

        // 안전한 위치 저장
        if (motor.isGrounded && transform.position.y > -2.2f)
            lastSafePosition = transform.position;

        // 맵 밖으로 떨어졌을 때 복귀
        if (transform.position.y < -7f)
            Teleport(lastSafePosition);
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

        transform.position = position;

        motor.enabled = true;

        verticalSpeed = 0f;

        Physics.SyncTransforms();
    }
}