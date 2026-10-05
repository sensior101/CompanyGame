using CompanyGame.Daldongne;
using UnityEngine;

/// <summary>Seats the existing persistent player and restores the same motor and avatar on standing.</summary>
[RequireComponent(typeof(PlayerMovement))]
public sealed class PlayerSeating : MonoBehaviour
{
    public static PlayerSeating Local { get; private set; }
    public Seat Current { get; private set; }
    public bool IsSeated { get; private set; }
    PlayerMovement movement;
    CharacterController motor;
    DaldongneAvatarMotion[] avatars;
    PlayerCameraController cameraController;
    float eyeHeight;
    Vector3 standingPosition;
    Quaternion standingRotation;
    bool movementWasEnabled, motorWasEnabled;

    void Awake()
    {
        Local = this; movement = GetComponent<PlayerMovement>(); motor = GetComponent<CharacterController>();
        avatars = GetComponentsInChildren<DaldongneAvatarMotion>(true);
    }
    public bool TrySit(Seat seat)
    {
        if (IsSeated || !movement.enabled || !motor.enabled || SceneLoadManager.IsLoading || !seat || !seat.Reserve(transform)) return false;
        standingPosition = transform.position; standingRotation = transform.rotation;
        movementWasEnabled = movement.enabled; motorWasEnabled = motor.enabled;
        Current = seat; IsSeated = true;
        movement.enabled = false; motor.enabled = false;
        transform.SetPositionAndRotation(seat.transform.position - Vector3.up * .44f, seat.transform.rotation);
        foreach (var avatar in avatars) if (avatar) avatar.SetSeated(true);
        cameraController = Camera.main ? Camera.main.GetComponent<PlayerCameraController>() : null;
        if (cameraController) { eyeHeight = cameraController.firstPersonEyeHeight; cameraController.firstPersonEyeHeight = 1.3f; }
        return true;
    }

    public bool TryStand()
    {
        if (!IsSeated) return false;
        // Space is an unconditional return to the recorded approach pose.
        // This also provides an escape when furniture or other actors move while seated.
        RestoreStandingPose(standingPosition, standingRotation);
        return true;
    }
    public bool TryStand(Vector2 localDirection)
    {
        if (!IsSeated || !Current || localDirection.sqrMagnitude < .01f) return false;
        Vector3 direction = Current.transform.TransformDirection(new Vector3(localDirection.x, 0f, localDirection.y));
        direction.y = 0f; direction.Normalize();
        float distance = .65f;
        // Project the fixture bounds onto the requested direction, including wide sofas.
        foreach (var collider in Current.owner.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger) continue;
            var bounds = collider.bounds;
            float edge = Vector3.Dot(bounds.center - Current.transform.position, direction)
                + Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(direction.x), 0f, Mathf.Abs(direction.z)));
            distance = Mathf.Max(distance, edge + motor.radius + .1f);
        }
        Vector3 target = Current.transform.position + direction * distance;
        target.y = standingPosition.y;
        return StandAt(target, Quaternion.LookRotation(direction));
    }
    bool StandAt(Vector3 target, Quaternion rotation)
    {
        Physics.SyncTransforms();
        if (!ClearGround(ref target) || !ClearExitPath(target)) return false;
        RestoreStandingPose(target, rotation);
        return true;
    }
    void RestoreStandingPose(Vector3 target, Quaternion rotation)
    {
        ReleasePose();
        transform.SetPositionAndRotation(target, rotation);
        motor.enabled = motorWasEnabled; movement.enabled = movementWasEnabled;
    }
    bool BlocksPlayer(Collider collider) => collider && collider.enabled && !collider.isTrigger &&
        !collider.transform.IsChildOf(transform) &&
        !Physics.GetIgnoreLayerCollision(gameObject.layer, collider.gameObject.layer) &&
        !Physics.GetIgnoreCollision(motor, collider);
    bool ClearExitPath(Vector3 target)
    {
        Vector3 start = Current.transform.position; start.y = target.y;
        Vector3 path = target - start;
        float radius = motor.radius;
        // Ignore only this fixture and this actor; other furniture, walls and actors block egress.
        foreach (var hit in Physics.CapsuleCastAll(start + Vector3.up * (radius + .04f),
            start + Vector3.up * (motor.height - radius), radius, path.normalized, path.magnitude,
            ~0, QueryTriggerInteraction.Ignore))
            if (BlocksPlayer(hit.collider) && !hit.transform.IsChildOf(Current.owner.transform)) return false;
        return true;
    }
    bool ClearGround(ref Vector3 point)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 1.5f, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a,b) => a.distance.CompareTo(b.distance));
        bool ground = false;
        foreach (var hit in hits)
        {
            if (!BlocksPlayer(hit.collider)) continue;
            if (hit.normal.y < .7f || (Current && hit.transform.IsChildOf(Current.owner.transform))) return false;
            // The approach pose can be raised by a chair arm or a step. Compare the
            // floor to the fixture base, not the player's height before sitting.
            if (Mathf.Abs(hit.point.y - Current.owner.transform.position.y) > .6f) return false;
            point.y = hit.point.y + .04f; ground = true; break;
        }
        if (!ground) return false;
        foreach (var hit in Physics.OverlapCapsule(point + Vector3.up * (motor.radius + .04f), point + Vector3.up * (motor.height - motor.radius), motor.radius, ~0, QueryTriggerInteraction.Ignore))
            if (BlocksPlayer(hit)) return false;
        return true;
    }
    void Update()
    {
        if (!IsSeated) return;
        if (!Current || !Current.isActiveAndEnabled || SceneLoadManager.IsLoading)
        {
            ReleasePose(); motor.enabled = motorWasEnabled; movement.enabled = movementWasEnabled;
        }
    }
    void ReleasePose()
    {
        if (Current) Current.Release(transform);
        Current = null; IsSeated = false;
        foreach (var avatar in avatars) if (avatar) avatar.SetSeated(false);
        if (cameraController) cameraController.firstPersonEyeHeight = eyeHeight;
    }
    void OnDisable() { if (IsSeated) { ReleasePose(); if (motor) motor.enabled = motorWasEnabled; if (movement) movement.enabled = movementWasEnabled; } }
    void OnDestroy() { if (Local == this) Local = null; }
}
