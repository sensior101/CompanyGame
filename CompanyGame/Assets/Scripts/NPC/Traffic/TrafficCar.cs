using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// An NPC car that follows <see cref="TrafficLane"/> points. It brakes for red lights, for cars ahead in its
/// direction and for people, and picks a random continuation at the end of each lane.
/// </summary>
[DisallowMultipleComponent]
public sealed class TrafficCar : MonoBehaviour
{
    const float MinGap = 2.2f;           // bumper clearance when stopped behind something
    const float StopMargin = .5f;        // how far the bumper stays short of a stop line
    const float TurnSpeedMps = 6f;       // speed through an intersection corner
    const float UnstickSeconds = 15f;    // blocked by a car this long: ignore cars for a while

    static readonly Collider[] hits = new Collider[24];

    public VehicleGrade grade;
    public float wheelRadius = .34f;
    public string itemId = "";
    public int key;

    public bool Finished { get; private set; }
    public float Speed { get; private set; }
    public TrafficLane Lane => lane;
    public Vector3 Position => pos;
    public float Yaw => yaw;

    TrafficLane lane, planned;
    int index;
    Vector3 pos;
    float lift, yaw, wheelDegrees, blockedTime, ignoreCarsUntil;
    bool runLight, blockedByCar;
    Bounds body = new Bounds(new Vector3(0f, .7f, .3f), new Vector3(1.8f, 1.4f, 4.2f));
    readonly List<Transform> wheels = new List<Transform>();
    readonly List<Quaternion> wheelBase = new List<Quaternion>();

    float Brake => grade ? grade.brakeMps2 : 8f;
    float FrontOffset => body.center.z + body.extents.z;
    Vector3 Forward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

    /// <summary>Builds the collider, finds wheels and puts the car on a lane at distance s. A null lane keeps the current transform.</summary>
    public void Init(TrafficLane startLane, float distance)
    {
        MeasureBody();
        BuildPhysics();
        CollectWheels();
        pos = transform.position;
        yaw = transform.eulerAngles.y;
        if (startLane && startLane.Count > 0) Place(startLane, distance);
        ApplyPose();
    }

    void Place(TrafficLane target, float distance)
    {
        lane = target;
        pos = lane.Sample(distance, out Vector3 forward, out index) + Vector3.up * lift;
        if (lane.Count > 1) yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        runLight = false;
        PlanNext();
    }

    void MeasureBody()
    {
        body = VehicleBounds.Measure(gameObject);
        lift = Mathf.Max(0f, -body.min.y);     // pivot above the lowest point: raise so the wheels touch the road
    }

    void BuildPhysics()
    {
        Rigidbody rigid = GetComponent<Rigidbody>();
        if (!rigid) rigid = gameObject.AddComponent<Rigidbody>();
        rigid.isKinematic = true;
        rigid.useGravity = false;
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.center = body.center;
        box.size = body.size;
        var obstacle = gameObject.AddComponent<NavMeshObstacle>();   // citizens steer around the car (no carving: it moves)
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = body.center;
        obstacle.size = body.size;
        obstacle.carving = false;
    }

    void CollectWheels()
    {
        wheels.Clear();
        wheelBase.Clear();
        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            // Skip wheels nested inside another wheel so they are not rotated twice.
            if (child == transform || !child.name.StartsWith("Wheel") || child.parent.name.StartsWith("Wheel")) continue;
            wheels.Add(child);
            wheelBase.Add(child.localRotation);
        }
    }

    // ---- driving -------------------------------------------------------------------------------------------

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, .1f);
        if (dt <= 0f) return;
        Vector3 before = pos;
        Drive(dt);
        float moved = Vector3.Dot(pos - before, Forward);
        wheelDegrees += moved / Mathf.Max(.05f, wheelRadius) * Mathf.Rad2Deg;
        ApplyPose();
    }

    void ApplyPose()
    {
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        for (int i = 0; i < wheels.Count; i++)
            wheels[i].localRotation = wheelBase[i] * Quaternion.Euler(wheelDegrees, 0f, 0f);
    }

    void Drive(float dt)
    {
        if (!lane || lane.Count == 0) { Finished = true; return; }
        Vector3 to = Flat(lane.Point(index) - pos);
        float reach = lane.connector ? 2.5f : 1.5f;
        if (to.sqrMagnitude < reach * reach || (Vector3.Dot(to, Forward) < 0f && to.sqrMagnitude < 100f))
        {
            if (!Advance()) return;
            to = Flat(lane.Point(index) - pos);
        }
        float targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float allowed = AllowedSpeed(Mathf.Abs(Mathf.DeltaAngle(yaw, targetYaw)), dt);
        Speed = grade ? grade.Step(Speed, allowed / grade.TopSpeedMps, dt) : Mathf.MoveTowards(Speed, allowed, dt * (allowed > Speed ? 3f : 8f));
        yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, Mathf.Clamp(Speed * 30f, 45f, 140f) * dt);
        pos += Forward * (Speed * dt);
        pos.y = Mathf.Lerp(pos.y, lane.Point(index).y + lift, 1f - Mathf.Exp(-3f * dt));
    }

    bool Advance()
    {
        if (index < lane.Count - 1) { index++; return true; }
        if (!planned || planned.Count == 0) { Finished = true; return false; }
        lane = planned;
        index = 0;
        runLight = false;
        PlanNext();
        return true;
    }

    void PlanNext()
    {
        planned = null;
        if (lane.next == null) return;
        int valid = 0;
        foreach (TrafficLane option in lane.next) if (option && option.Count > 0) valid++;
        if (valid == 0) return;
        int pick = Random.Range(0, valid);
        foreach (TrafficLane option in lane.next)
            if (option && option.Count > 0 && pick-- == 0) { planned = option; return; }
    }

    float AllowedSpeed(float turnAngle, float dt)
    {
        float cap = Mathf.Min(grade ? grade.TopSpeedMps : 12f, lane.speedLimitKmh / 3.6f);
        if (turnAngle > 60f) cap = Mathf.Min(cap, 3f);
        else if (turnAngle > 25f) cap = Mathf.Min(cap, 5f);
        float remaining = Flat(lane.Point(index) - pos).magnitude + lane.DistanceFromPointToEnd(index);
        if (lane.connector) cap = Mathf.Min(cap, TurnSpeedMps);
        else if (planned && planned.connector)
            cap = Mathf.Min(cap, Mathf.Sqrt(TurnSpeedMps * TurnSpeedMps + 1.4f * Brake * remaining));
        return Mathf.Min(cap, Mathf.Min(SignalCap(remaining), ObstacleCap(dt)));
    }

    float SignalCap(float remaining)
    {
        if (!lane.signal || runLight) return float.MaxValue;
        TrafficSignal.Phase phase = lane.signal.Current;
        if (phase == TrafficSignal.Phase.Green) return float.MaxValue;
        float stopDistance = remaining - FrontOffset - StopMargin;
        if (stopDistance < -1.5f) return float.MaxValue;               // already over the line
        bool cannotStop = Speed * Speed / (2f * Mathf.Max(.1f, stopDistance)) > Brake * 1.3f;
        if (cannotStop && stopDistance > .5f)
        {
            runLight = true;                                           // too late to stop: keep going through
            return float.MaxValue;
        }
        return Mathf.Sqrt(2f * Brake * .8f * Mathf.Max(0f, stopDistance));
    }

    float ObstacleCap(float dt)
    {
        Vector3 fwd = Forward;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 front = pos + rot * new Vector3(body.center.x, 0f, FrontOffset);
        float ahead = Mathf.Clamp(4f + Speed * .6f, 4f, 14f);
        Vector3 half = new Vector3(body.extents.x * .9f, Mathf.Max(.4f, body.extents.y * .8f), ahead * .5f);
        Vector3 center = front + fwd * half.z + Vector3.up * body.center.y;
        float gap = NearestInBox(center, half, rot, front, fwd, out float otherSpeed);
        blockedTime = blockedByCar && Speed < .2f ? blockedTime + dt : 0f;
        if (blockedTime > UnstickSeconds) { ignoreCarsUntil = Time.time + 5f; blockedTime = 0f; }
        if (gap == float.MaxValue) return float.MaxValue;
        if (gap <= MinGap) return 0f;
        return Mathf.Sqrt(2f * Brake * .7f * (gap - MinGap)) + otherSpeed * .8f;
    }

    float NearestInBox(Vector3 center, Vector3 half, Quaternion rot, Vector3 front, Vector3 fwd, out float otherSpeed)
    {
        float best = float.MaxValue;
        otherSpeed = 0f;
        blockedByCar = false;
        int count = Physics.OverlapBoxNonAlloc(center, half, hits, rot, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = hits[i];
            if (!hit || hit.transform.IsChildOf(transform)) continue;
            TrafficCar other = hit.GetComponentInParent<TrafficCar>();
            if (other ? !CarMatters(other, fwd) : !IsPerson(hit)) continue;
            float gap = Mathf.Max(0f, Vector3.Dot(hit.bounds.ClosestPoint(front) - front, fwd));
            if (gap >= best) continue;
            best = gap;
            otherSpeed = other ? Mathf.Max(0f, other.Speed) : 0f;
            blockedByCar = other;
        }
        // The player's own car has its colliders off and remote avatars have none: test them directly.
        Behaviour player = SceneLoadManager.Traveller;
        if (player) Consider(player.transform, front, fwd, half, ref best, ref otherSpeed);
        foreach (Transform remote in CitizenWalker.RemoteAvatars()) if (remote) Consider(remote, front, fwd, half, ref best, ref otherSpeed);
        return best;
    }

    // A person is a point; the player's car is the NavMeshObstacle box PlayerVehicle puts on the player (body bounds, not just the pivot).
    void Consider(Transform t, Vector3 front, Vector3 fwd, Vector3 half, ref float best, ref float otherSpeed)
    {
        var box = t.GetComponent<NavMeshObstacle>();
        Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
        float nearest = float.MaxValue;
        for (int i = 0; i < 9; i++)
        {
            Vector3 local = box ? box.center + Vector3.Scale(box.size * .5f, i == 8 ? Vector3.zero
                : new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)) : Vector3.zero;
            Vector3 d = t.TransformPoint(local) - front;
            float along = Vector3.Dot(d, fwd);
            if (along > -.5f && along < half.z * 2f && Mathf.Abs(Vector3.Dot(d, right)) < half.x + .8f && Mathf.Abs(d.y) < 2.5f)
                nearest = Mathf.Min(nearest, Mathf.Max(0f, along));
        }
        if (nearest >= best) return;
        best = nearest;
        otherSpeed = 0f;
        blockedByCar = false;
    }

    bool CarMatters(TrafficCar other, Vector3 fwd) =>
        other != this && Time.time >= ignoreCarsUntil && Vector3.Dot(other.Forward, fwd) > .3f;

    static bool IsPerson(Collider hit) =>
        hit.GetComponentInParent<CharacterController>() || hit.GetComponentInParent<NavMeshAgent>();

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
