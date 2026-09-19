using UnityEngine;
using CompanyGame.Daldongne;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Scene-local character movement and camera handoff. F returns to the overview camera.
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public Camera viewCamera;
    public DaldongneMapCamera overview;
    public Vector3 spawn = new Vector3(-17.5f, 1.08f, -27f);
    public float moveSpeed = 3f;
    public bool walking;
    CharacterController motor;
    float verticalSpeed;
    float savedSize;
    bool savedOrthographic;
    Vector3 lastSafePosition;

    protected virtual void Awake()
    {
        motor = GetComponent<CharacterController>();
        motor.radius = .35f; motor.height = 1.8f;
        motor.center = Vector3.up * .9f; motor.stepOffset = .23f;
        motor.slopeLimit = 45; motor.skinWidth = .02f; motor.minMoveDistance = 0;
        lastSafePosition = spawn;
    }

    protected virtual void Start()
    {
        if (!viewCamera) viewCamera = Camera.main;
        if (!overview && viewCamera) overview = viewCamera.GetComponent<DaldongneMapCamera>();
        // A serialized walking=true must use the same camera handoff as F.
        bool startWalking = walking;
        walking = false;
        if (startWalking) SetWalking(true);
    }

    protected virtual void OnDisable()
    {
        // Disabling the preview while walking must return camera input.
        if (walking) SetWalking(false);
    }

    protected virtual void Update()
    {
        bool toggle = false, reset = false, sprint = false;
        Vector2 input = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        var keys = Keyboard.current;
        if (keys != null)
        {
            toggle = keys.fKey.wasPressedThisFrame;
            reset = keys.rKey.wasPressedThisFrame;
            sprint = keys.leftShiftKey.isPressed;
            input.x = (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0);
            input.y = (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0);
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        toggle = Input.GetKeyDown(KeyCode.F); reset = Input.GetKeyDown(KeyCode.R);
        sprint = Input.GetKey(KeyCode.LeftShift);
        input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        if (toggle) SetWalking(!walking);
        if (!walking) return;
        // Do not immediately walk away from the reset position on the same
        // frame if R is pressed while a movement key is still held.
        if (reset) { ResetToSpawn(); return; }
        Vector3 forward = viewCamera ? Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 motion = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1);
        if (motor.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
        verticalSpeed += Physics.gravity.y * Time.deltaTime;
        motor.Move((motion * moveSpeed * (sprint ? 1.5f : 1) + Vector3.up * verticalSpeed) * Time.deltaTime);
        if (motion.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(motion);
        if (motor.isGrounded && transform.position.y > -2.2f) lastSafePosition = transform.position;
        if (transform.position.y < -7) Teleport(lastSafePosition);
    }

    protected virtual void LateUpdate()
    {
        if (!walking || !viewCamera) return;
        Vector3 target = transform.position + Vector3.up * .9f;
        viewCamera.transform.position = target + new Vector3(6, 8, -10);
        viewCamera.transform.LookAt(target);
    }

    public void SetWalking(bool value)
    {
        if (value == walking || !viewCamera) return;
        walking = value;
        if (overview) overview.enabled = !value;
        if (value)
        {
            savedSize = viewCamera.orthographicSize; savedOrthographic = viewCamera.orthographic;
            viewCamera.orthographic = true; viewCamera.orthographicSize = 8;
        }
        else
        {
            viewCamera.orthographic = savedOrthographic; viewCamera.orthographicSize = savedSize;
            if (overview) overview.ResetView();
        }
    }

    public void ResetToSpawn()
    {
        lastSafePosition = spawn;
        Teleport(spawn);
    }
    void Teleport(Vector3 position)
    {
        if (!motor) motor = GetComponent<CharacterController>();
        motor.enabled = false; transform.position = position;
        motor.enabled = true; verticalSpeed = 0;
        Physics.SyncTransforms();
    }
}
