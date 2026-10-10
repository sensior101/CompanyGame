using System;
using CompanyGame.Daldongne;
using UnityEngine;

/// <summary>Drives a placed vehicle using the existing persistent player's motor.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public sealed class PlayerVehicle : MonoBehaviour
{
    public bool IsRiding => instance;
    public ItemData CurrentItem { get; private set; }
    public float Speed { get; private set; }
    public event Action<string> Feedback;
    // The inventory UI layer owns world-item placement and quantity transfers.
    public event Func<bool> PlacementRequested;
    PlayerMovement movement;
    CharacterController motor;
    GameObject instance;
    Transform frontWheel, rearWheel;
    DaldongneAvatarMotion[] avatars;
    Vector3 previousPosition;
    float wheelAngle, originalRadius;
    Action returnToWorld;

    void Awake() { movement = GetComponent<PlayerMovement>(); motor = GetComponent<CharacterController>(); }
    void OnEnable() { SceneLoadManager.LoadStarted += StopForTravel; }
    void StopForTravel() { Dismount(false); }
    public ItemData SelectedItem
    {
        get
        {
            var inventory = InventoryManager.Instance ? InventoryManager.Instance.State : null;
            var stack = inventory?.GetSlot(inventory.SelectedHotbarIndex);
            return stack != null && !stack.IsEmpty && stack.Item.IsVehicle ? stack.Item : null;
        }
    }
    public bool TryPlaceSelected()
    {
        if (IsRiding || !SelectedItem || !CanUseVehicle()) return false;
        return PlacementRequested?.Invoke() ?? false;
    }
    bool CanUseVehicle() => movement && movement.isActiveAndEnabled && motor.enabled &&
        !SceneLoadManager.IsLoading && !InputFocus.GameplayBlocked() &&
        !(GetComponent<PlayerSeating>()?.IsSeated ?? false);

    public bool TryMount(ItemData item, GameObject placedModel, Action onDismounted)
    {
        if (!item || !item.IsVehicle || !placedModel || onDismounted == null || IsRiding || !CanUseVehicle()) return false;
        CurrentItem = item;
        instance = placedModel;
        returnToWorld = onDismounted;
        motor.enabled = false;
        transform.SetPositionAndRotation(instance.transform.position, instance.transform.rotation);
        motor.enabled = true;
        instance.transform.SetParent(transform, true);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        // The existing CharacterController remains the only movement and collision owner.
        foreach (var collider in instance.GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (var child in instance.GetComponentsInChildren<Transform>())
        {
            if (child.name == "WheelFront") frontWheel = child;
            if (child.name == "WheelRear") rearWheel = child;
            if (child.name == "Kickstand") child.gameObject.SetActive(false);
        }
        originalRadius = motor.radius; motor.radius = .46f;
        avatars = GetComponentsInChildren<DaldongneAvatarMotion>(true);
        previousPosition = transform.position; Speed = wheelAngle = 0;
        foreach (var avatar in avatars) if (avatar) avatar.SetCycling(true, 0);
        return true;
    }
    bool Refuse(string reason) { Feedback?.Invoke(reason); return false; }
    bool Blocks(Collider other) => other && other.enabled && !other.isTrigger && !other.transform.IsChildOf(transform) &&
        !Physics.GetIgnoreLayerCollision(gameObject.layer, other.gameObject.layer) && !Physics.GetIgnoreCollision(motor, other);
    public Vector3 RideMotion(Vector3 desired, bool sprint, float dt)
    {
        float target = desired.magnitude * (sprint ? CurrentItem.vehicleSpeed * 1.3f : CurrentItem.vehicleSpeed);
        Speed = Mathf.MoveTowards(Speed, target, dt * (target > Speed ? 5f : 12f));
        if (desired.sqrMagnitude > .01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(desired), 200f * dt);
        // Sweep the bicycle footprint; the motor still handles slopes, steps and gravity.
        float travel = Speed * dt;
        foreach (var hit in Physics.BoxCastAll(transform.position + Vector3.up * .85f, new Vector3(.30f,.42f,.77f),
                     transform.forward, transform.rotation, travel + .04f, ~0, QueryTriggerInteraction.Ignore))
            if (Blocks(hit.collider) && hit.normal.y < .55f) { Speed = 0; break; }
        return transform.forward * Speed;
    }
    void Update()
    {
        if (IsRiding && SceneLoadManager.IsLoading) Dismount(false);
        if (IsRiding && (!movement || !movement.isActiveAndEnabled)) Speed = 0;
    }
    void LateUpdate()
    {
        if (!IsRiding) return;
        float distance = Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up).magnitude;
        previousPosition = transform.position;
        if (distance > 2f) return;
        float angle = distance / .34f * Mathf.Rad2Deg;
        wheelAngle += angle;
        if (frontWheel) frontWheel.RotateAround(frontWheel.position, transform.right, angle);
        if (rearWheel) rearWheel.RotateAround(rearWheel.position, transform.right, angle);
        foreach (var avatar in avatars) if (avatar) avatar.SetCycling(true, wheelAngle * .45f * Mathf.Deg2Rad);
    }
    public bool Dismount(bool moveAside = true)
    {
        if (!instance) return false;
        Vector3 exit = transform.position;
        if (moveAside && !TryFindExit(out exit)) return Refuse("내릴 공간이 부족합니다. 넓은 곳으로 이동해 주세요.");
        var release = returnToWorld;
        instance = null; returnToWorld = null;
        if (motor) motor.radius = originalRadius;
        if (avatars != null) foreach (var avatar in avatars) if (avatar) avatar.SetCycling(false,0);
        CurrentItem = null; frontWheel = rearWheel = null; Speed = 0;
        release?.Invoke();
        if (moveAside)
        {
            motor.enabled = false; transform.position = exit; motor.enabled = true;
            Physics.SyncTransforms();
        }
        return true;
    }
    bool TryFindExit(out Vector3 point)
    {
        foreach (var offset in new[] { transform.right * .95f, -transform.right * .95f, -transform.forward * 1.6f })
        {
            var candidate = transform.position + offset;
            if (!Physics.Raycast(candidate + Vector3.up * 1.2f, Vector3.down, out var floor, 2.5f, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < .7f) continue;
            candidate.y = floor.point.y + .025f;
            bool blocked = false;
            foreach (var hit in Physics.OverlapCapsule(candidate + Vector3.up * .4f, candidate + Vector3.up * 1.45f,
                         .34f, ~0, QueryTriggerInteraction.Ignore))
                if (Blocks(hit)) { blocked = true; break; }
            if (blocked) continue;
            // Do not teleport through a wall to an otherwise empty exit point.
            foreach (var hit in Physics.SphereCastAll(transform.position + Vector3.up * .9f, .3f, offset.normalized,
                         offset.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (Blocks(hit.collider) && hit.normal.y < .7f) { blocked = true; break; }
            if (!blocked) { point = candidate; return true; }
        }
        point = default; return false;
    }
    void OnDisable() { SceneLoadManager.LoadStarted -= StopForTravel; Dismount(false); }
}
