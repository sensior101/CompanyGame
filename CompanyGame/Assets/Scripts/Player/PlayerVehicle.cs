using System;
using System.Collections.Generic;
using CompanyGame.Daldongne;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// Drives a placed vehicle. Bicycles (no grade) use the persistent CharacterController motor; cars
/// (ItemData.grade) switch it off and move by their own body box with ground rays under the wheels.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public sealed class PlayerVehicle : MonoBehaviour
{
    public bool IsRiding => instance;
    public bool IsCar => instance && isCar;
    public ItemData CurrentItem { get; private set; }
    // Signed forward speed in m/s (cars may reverse).
    public float Speed { get; private set; }
    public float SpeedKmh => Mathf.Abs(Speed) * 3.6f;
    public event Action<string> Feedback;
    // The inventory UI layer owns world-item placement and quantity transfers.
    public event Func<bool> PlacementRequested;
    const float StepLift = .5f, TurnRate = 110f, ReverseShare = .15f, RayLift = .7f, DropReach = 1f, MaxTilt = 35f;
    PlayerMovement movement;
    CharacterController motor;
    GameObject instance;
    readonly List<Transform> wheels = new List<Transform>();
    DaldongneAvatarMotion[] avatars;
    Vector3 previousPosition;
    float wheelAngle, originalRadius;
    Action returnToWorld;
    // Car state: body box (player-local, bottom lifted by StepLift so curbs and slopes pass), hub points, fall speed, body tilt.
    bool isCar;
    Bounds body;
    Vector3 boxCenter, boxHalf;
    Vector3[] contacts = new Vector3[0];
    float carFall;
    NavMeshObstacle obstacle;
    Quaternion tilt = Quaternion.identity;
    // Driving view: third person shows only the car, first person hides car and character (renderers only, scripts keep running).
    readonly List<Renderer> bodyRenderers = new List<Renderer>();
    readonly Dictionary<Renderer, bool> bodyWasEnabled = new Dictionary<Renderer, bool>();
    Renderer[] carRenderers = new Renderer[0];
    ShadowCastingMode[] carShadows = new ShadowCastingMode[0];
    PlayerCameraController cameraController;
    bool? carHidden;

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
        isCar = item.IsCar;
        motor.enabled = false;
        var pose = instance.transform.rotation;
        transform.SetPositionAndRotation(instance.transform.position, isCar ? Quaternion.Euler(0, pose.eulerAngles.y, 0) : pose);
        // A car keeps the controller off until dismount; a bicycle uses it as before.
        if (!isCar) motor.enabled = true;
        instance.transform.SetParent(transform, true);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        foreach (var collider in instance.GetComponentsInChildren<Collider>()) collider.enabled = false;
        wheels.Clear();
        foreach (var child in instance.GetComponentsInChildren<Transform>())
        {
            if (child.name == "Kickstand") child.gameObject.SetActive(false);
            if (child.name.StartsWith("Wheel") && !InsideWheel(child)) wheels.Add(child);
        }
        if (isCar) { PrepareCar(); CaptureRenderers(); }
        else { originalRadius = motor.radius; motor.radius = .46f; }
        avatars = GetComponentsInChildren<DaldongneAvatarMotion>(true);
        previousPosition = transform.position; Speed = wheelAngle = carFall = 0; tilt = Quaternion.identity;
        foreach (var avatar in avatars)
            if (avatar) { if (isCar) avatar.SetSeated(true); else avatar.SetCycling(true, 0); }
        return true;
    }
    bool InsideWheel(Transform child)
    {
        foreach (var wheel in wheels) if (child.IsChildOf(wheel)) return true;
        return false;
    }
    void PrepareCar()
    {
        body = VehicleBounds.Measure(instance);
        float bottom = Mathf.Min(body.min.y + StepLift, body.max.y - .2f);
        boxHalf = new Vector3(Mathf.Max(.1f, body.extents.x - .03f), (body.max.y - bottom) * .5f, Mathf.Max(.1f, body.extents.z - .03f));
        boxCenter = new Vector3(body.center.x, (body.max.y + bottom) * .5f, body.center.z);
        var points = new List<Vector3>();
        foreach (var wheel in wheels) points.Add(transform.InverseTransformPoint(wheel.position));
        if (points.Count == 0)
            foreach (var sign in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                points.Add(new Vector3(body.center.x + sign.x * body.extents.x, body.min.y + CurrentItem.wheelRadius, body.center.z + sign.y * body.extents.z));
        contacts = points.ToArray();
        // Lets NavMeshAgents (citizens) route around the moving car; no carving, so the mesh is not rebuilt each frame.
        obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box; obstacle.carving = false;
        obstacle.center = body.center; obstacle.size = body.size;
    }
    bool FirstPerson()
    {
        if (!cameraController && movement && movement.viewCamera) cameraController = movement.viewCamera.GetComponent<PlayerCameraController>();
        return cameraController && cameraController.firstPerson;
    }
    void CaptureRenderers()
    {
        carRenderers = instance.GetComponentsInChildren<Renderer>(true);
        carShadows = new ShadowCastingMode[carRenderers.Length];
        for (int i = 0; i < carRenderers.Length; i++) carShadows[i] = carRenderers[i].shadowCastingMode;
        bodyRenderers.Clear(); bodyWasEnabled.Clear(); carHidden = null;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            if (!renderer.transform.IsChildOf(instance.transform)) { bodyRenderers.Add(renderer); bodyWasEnabled[renderer] = renderer.enabled; }
    }
    bool InHeldItem(Renderer renderer)
    {
        var held = GetComponent<PlayerHeldItem>();
        return held && held.WorldItemRoot && renderer.transform.IsChildOf(held.WorldItemRoot);
    }
    // Character is always hidden in a car (the camera toggle re-shows it, so this runs every frame); the car follows the camera mode.
    void ApplyCarVisibility(bool first)
    {
        foreach (var renderer in bodyRenderers) if (renderer && renderer.enabled && !InHeldItem(renderer)) renderer.enabled = false;
        if (carHidden == first) return;
        carHidden = first;
        for (int i = 0; i < carRenderers.Length; i++)
            if (carRenderers[i]) { carRenderers[i].enabled = !first; carRenderers[i].shadowCastingMode = first ? ShadowCastingMode.Off : carShadows[i]; }
    }
    void RestoreCarVisibility()
    {
        bool first = FirstPerson();
        for (int i = 0; i < carRenderers.Length; i++)
            if (carRenderers[i]) { carRenderers[i].enabled = true; carRenderers[i].shadowCastingMode = carShadows[i]; }
        // The camera owns the body look (hidden in first person); anything else (shot trail etc.) gets its pre-ride state back.
        var look = GetComponent<DaldongnePlayerAppearance>();
        foreach (var renderer in bodyRenderers)
        {
            if (!renderer || InHeldItem(renderer)) continue;
            bool body = look && ((look.female && renderer.transform.IsChildOf(look.female.transform)) || (look.male && renderer.transform.IsChildOf(look.male.transform)));
            renderer.enabled = body ? !first : bodyWasEnabled.TryGetValue(renderer, out bool was) && was;
        }
        bodyRenderers.Clear(); bodyWasEnabled.Clear(); carRenderers = new Renderer[0]; carHidden = null;
    }
    bool Refuse(string reason) { Feedback?.Invoke(reason); return false; }
    bool Blocks(Collider other) => other && other.enabled && !other.isTrigger && !other.transform.IsChildOf(transform) &&
        !Physics.GetIgnoreLayerCollision(gameObject.layer, other.gameObject.layer) && (!motor.enabled || !Physics.GetIgnoreCollision(motor, other));
    public Vector3 RideMotion(Vector3 desired, bool sprint, float dt)
    {
        if (isCar) return RideCar(desired, dt);
        float target = desired.magnitude * (sprint ? CurrentItem.vehicleSpeed * 1.3f : CurrentItem.vehicleSpeed);
        Speed = Mathf.MoveTowards(Speed, target, dt * (target > Speed ? 5f : 12f));
        if (desired.sqrMagnitude > .01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(desired), 200f * dt);
        // Sweep the bicycle footprint; the motor still handles slopes, steps and gravity.
        float travel = Speed * dt;
        foreach (var hit in Physics.BoxCastAll(transform.position + Vector3.up * (BodySize.StepHeight + .42f), new Vector3(.30f,.40f,.77f), // bottom just above StepHeight: steps the motor can climb pass
                     transform.forward, transform.rotation, travel + .04f, ~0, QueryTriggerInteraction.Ignore))
            if (Blocks(hit.collider) && hit.normal.y < .55f) { Speed = 0; break; }
        return transform.forward * Speed;
    }

    // Camera-independent car input: W throttle, S brake then reverse, A/D steer (flips naturally while reversing).
    public static void MapCarInput(Vector2 move, float speed, out float throttle, out bool reverse, out float steer)
    {
        throttle = 0f; reverse = false; steer = Mathf.Clamp(move.x, -1f, 1f);
        float y = Mathf.Clamp(move.y, -1f, 1f);
        if (y > 0f && speed > -.5f) throttle = y;
        else if (y < 0f && speed <= .5f) { reverse = true; throttle = -y; }
    }

    // The car moves the player transform itself, so PlayerMovement gets no horizontal motion back.
    Vector3 RideCar(Vector3 desired, float dt)
    {
        // desired is camera-relative; it only tells whether input is currently allowed.
        MapCarInput(desired.sqrMagnitude > 1e-4f ? GameInput.Move : Vector2.zero, Speed, out float throttle, out bool reverse, out float steer);
        var grade = CurrentItem.grade;
        if (reverse) Speed = -grade.Step(Mathf.Max(0f, -Speed), Mathf.Min(throttle, ReverseShare), dt);
        else if (Speed < 0f) Speed = -grade.Step(-Speed, 0f, dt);
        else Speed = grade.Step(Speed, throttle, dt);
        Steer(steer, dt);
        Advance(dt);
        SnapToGround(dt);
        return Vector3.zero;
    }
    void Steer(float steer, float dt)
    {
        // No turning in place: the signed rate follows speed (reversed when backing up). A turn that would push the body into something new is cancelled.
        if (Mathf.Abs(steer) < .01f) return;
        var to = transform.rotation * Quaternion.Euler(0f, steer * TurnRate * Mathf.Clamp(Speed / 3f, -1f, 1f) * dt, 0f);
        if (!TurnBlocked(to)) transform.rotation = to;
    }
    bool TurnBlocked(Quaternion to)
    {
        var before = Physics.OverlapBox(transform.position + transform.rotation * boxCenter, boxHalf, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (var other in Physics.OverlapBox(transform.position + to * boxCenter, boxHalf, to, ~0, QueryTriggerInteraction.Ignore))
            if (Blocks(other) && Array.IndexOf(before, other) < 0) return true;
        return false;
    }
    void Advance(float dt)
    {
        float distance = Mathf.Abs(Speed * dt);
        if (distance < 1e-5f) return;
        var direction = transform.forward * Mathf.Sign(Speed);
        float free = distance;
        var origin = transform.position + transform.rotation * boxCenter;
        // ponytail: static boxes already overlapping at the start are ignored so a car spawned inside geometry can leave it;
        // moving bodies (citizens walking into the car) overlapping ahead of us still stop it.
        foreach (var hit in Physics.BoxCastAll(origin, boxHalf, direction, transform.rotation, distance + .04f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!Blocks(hit.collider)) continue;
            if (hit.distance > 0f) { if (hit.normal.y < .55f) free = Mathf.Min(free, Mathf.Max(0f, hit.distance - .01f)); }
            else if (hit.collider.attachedRigidbody && Vector3.Dot(hit.collider.bounds.center - origin, direction) > 0f) free = 0f;
        }
        if (free < distance) Speed = 0;
        transform.position += direction * free;
    }
    bool GroundBelow(Vector3 origin, float distance, out RaycastHit best)
    {
        best = default; float nearest = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore))
            if (hit.distance < nearest && hit.normal.y > .3f && Blocks(hit.collider)) { best = hit; nearest = hit.distance; }
        return nearest < float.MaxValue;
    }
    // Rays under each hub give the pivot height and body tilt; with no ground the car falls.
    void SnapToGround(float dt)
    {
        float sum = 0f; int found = 0; var up = Vector3.zero;
        foreach (var hub in contacts)
        {
            var origin = transform.TransformPoint(hub) + Vector3.up * RayLift;
            if (!GroundBelow(origin, RayLift + CurrentItem.wheelRadius + DropReach, out var hit)) continue;
            sum += hit.point.y - hub.y + CurrentItem.wheelRadius; up += hit.normal; found++;
        }
        float y = transform.position.y;
        if (found == 0 || sum / found < y - .02f)
        {
            carFall += Physics.gravity.y * dt;
            y += carFall * dt;
            if (found > 0 && y <= sum / found) { y = sum / found; carFall = 0; }
        }
        else { carFall = 0; y = Mathf.Lerp(y, sum / found, 1f - Mathf.Exp(-30f * dt)); }
        var position = transform.position; position.y = y; transform.position = position;
        if (found == 0) return;
        var lean = Quaternion.RotateTowards(Quaternion.identity,
            Quaternion.FromToRotation(Vector3.up, transform.InverseTransformDirection(up.normalized)), MaxTilt);
        tilt = Quaternion.Slerp(tilt, lean, 1f - Mathf.Exp(-12f * dt));
        instance.transform.localRotation = tilt;
    }
    void Update()
    {
        if (IsRiding && SceneLoadManager.IsLoading) Dismount(false);
        if (IsRiding && (!movement || !movement.isActiveAndEnabled)) Speed = 0;
    }
    void LateUpdate()
    {
        if (!IsRiding) return;
        if (isCar) ApplyCarVisibility(FirstPerson());
        var delta = transform.position - previousPosition;
        float distance = isCar ? Vector3.Dot(delta, transform.forward) : Vector3.ProjectOnPlane(delta, Vector3.up).magnitude;
        previousPosition = transform.position;
        if (Mathf.Abs(distance) > (isCar ? 5f : 2f)) return;
        float angle = distance / CurrentItem.wheelRadius * Mathf.Rad2Deg;
        wheelAngle += angle;
        foreach (var wheel in wheels) if (wheel) wheel.RotateAround(wheel.position, instance.transform.right, angle);
        if (isCar) return;
        foreach (var avatar in avatars) if (avatar) avatar.SetCycling(true, wheelAngle * .45f * Mathf.Deg2Rad);
    }
    public bool Dismount(bool moveAside = true)
    {
        if (!instance) return false;
        Vector3 exit = transform.position;
        if (moveAside && !TryFindExit(out exit)) return Refuse("내릴 공간이 부족합니다. 넓은 곳으로 이동해 주세요.");
        var release = returnToWorld;
        bool car = isCar;
        if (car) { instance.transform.localRotation = Quaternion.identity; RestoreCarVisibility(); }
        if (obstacle) { Destroy(obstacle); obstacle = null; }
        instance = null; returnToWorld = null; isCar = false;
        // Scene travel keeps the player where it is, so the controller returns at once; otherwise it returns at the exit point below.
        if (motor) { if (!car) motor.radius = originalRadius; else if (!moveAside) motor.enabled = true; }
        if (avatars != null)
            foreach (var avatar in avatars)
                if (avatar) { if (car) avatar.SetSeated(false); else avatar.SetCycling(false,0); }
        CurrentItem = null; wheels.Clear(); Speed = carFall = 0; tilt = Quaternion.identity;
        release?.Invoke();
        if (moveAside)
        {
            motor.enabled = false; transform.position = exit; motor.enabled = true;
            Physics.SyncTransforms();
        }
        return true;
    }
    // Exit points to the sides and rear, clear of the car body (the pivot is the driver's seat, not the centre).
    Vector3[] ExitOffsets()
    {
        if (!isCar) return new[] { transform.right * .95f, -transform.right * .95f, -transform.forward * 1.6f };
        return new[] { transform.right * (body.min.x - .7f), transform.right * (body.max.x + .7f), transform.forward * (body.min.z - 1f) };
    }
    bool TryFindExit(out Vector3 point)
    {
        foreach (var offset in ExitOffsets())
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
    void OnDisable()
    {
        SceneLoadManager.LoadStarted -= StopForTravel;
        // Scene travel dismounts through LoadStarted. During Player deactivation Unity
        // forbids reparenting the model out of its hierarchy, so leave teardown alone.
        if (gameObject.activeInHierarchy) Dismount(false);
    }
}
