using CompanyGame.Daldongne;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One citizen on the baked NavMesh: walk, stand and look around, sometimes sit on a bench.
/// Waits at crosswalk links until Crosswalk.CanCross. A proxy (non-authority) does nothing but
/// follow the transform that CitizenSync gives it. The walking pose comes from DaldongneAvatarMotion
/// reading position changes, so only the transform is moved.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class CitizenWalker : MonoBehaviour
{
    enum State { Walk, Pause, Cross, ToSeat, Seated }
    /// <summary>Street = wanders the map; Browse = short walks indoors; Stand = stays and chats; Sit = fixed on a chair.</summary>
    public enum Role { Street, Browse, Stand, Sit }
    public const float Range = 40f, CullDistance = 70f, TalkDistance = 1.2f;

    public int Index { get; private set; }
    public bool Seated => state == State.Seated;
    public bool IsProxy => proxy;

    NavMeshAgent agent;
    DaldongneAvatarMotion motion;
    DialogueData dialogue;
    Renderer[] renderers;
    Seat[] seats = System.Array.Empty<Seat>();
    Seat seat;
    NavMeshPath path; // created in Awake: NavMeshPath cannot be built in a field initializer
    System.Random rng = new System.Random(1);
    State state = State.Pause, afterCross = State.Walk;
    bool proxy, culled, visualSeated;
    float timer = 1f, grace, stuck, nextCull, baseYaw, lookPhase;
    Vector3 crossEnd, standPos;
    Role role;
    float minDist = 8f, maxDist = Range, lookAmplitude = 55f;
    Seat fixedSeat;
    GameObject book;

    /// <summary>Called by the spawner before the object is activated.</summary>
    public void Init(int index, int seed, Seat[] benches)
    {
        Index = index;
        rng = new System.Random(seed);
        if (benches != null) seats = benches;
    }

    /// <summary>Indoor citizens (called before activation). Sit citizens are placed with SitAt after activation.</summary>
    public void SetRole(Role value)
    {
        role = value;
        if (value == Role.Browse) { minDist = 2.5f; maxDist = 9f; lookAmplitude = 85f; }
        if (value == Role.Stand) lookAmplitude = 22f;
    }

    /// <summary>Sits on the seat for good (same offset the player uses); an optional book rests in the lap.</summary>
    public void SitAt(Seat target, GameObject bookPrefab)
    {
        fixedSeat = target;
        if (bookPrefab && !book)
        {
            book = Instantiate(bookPrefab, transform);
            book.transform.SetLocalPositionAndRotation(new Vector3(0f, .74f, .30f), Quaternion.Euler(-35f, 0f, 0f));
        }
        ApplyFixedSeat();
    }

    void ApplyFixedSeat()
    {
        if (agent.enabled) agent.enabled = false;
        transform.SetPositionAndRotation(fixedSeat.transform.position - Vector3.up * .44f, fixedSeat.transform.rotation);
        seat = fixedSeat;
        if (!proxy) fixedSeat.Reserve(transform);
        if (motion) motion.SetSeated(true);
        visualSeated = true;
        state = State.Seated;
    }

    float R() => (float)rng.NextDouble();

    void Awake()
    {
        path = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>();
        motion = GetComponentInChildren<DaldongneAvatarMotion>(true);
        dialogue = GetComponent<DialogueData>();
        renderers = GetComponentsInChildren<Renderer>(true);
        agent.autoTraverseOffMeshLink = false;
        agent.speed = 1.1f + R() * .4f;
        agent.avoidancePriority = 30 + rng.Next(60);
        timer = .5f + R() * 2f;
        baseYaw = transform.eulerAngles.y;
    }

    void OnDisable()
    {
        if (seat) seat.Release(transform);
    }

    void Update()
    {
        Cull();
        if (proxy) return;
        if (role == Role.Sit) { if (fixedSeat) FaceVisitors(Time.deltaTime); return; }
        if (state == State.Seated) { UpdateSeated(Time.deltaTime); return; }
        if (!agent.enabled || !agent.isOnNavMesh) return;
        if (Talking(Time.deltaTime)) return;
        if (state != State.Cross && VehicleAhead(Time.deltaTime)) return;
        switch (state)
        {
            case State.Walk:
            case State.ToSeat: Travel(Time.deltaTime); break;
            case State.Pause: LookAround(Time.deltaTime); break;
            case State.Cross: Cross(Time.deltaTime); break;
        }
    }

    // ---- authority <-> proxy -------------------------------------------------

    /// <summary>Proxy: the agent is off and CitizenSync moves the transform. Back to false: simulate from the current spot.</summary>
    public void SetProxy(bool on)
    {
        if (proxy == on) return;
        proxy = on;
        if (on)
        {
            if (seat && role != Role.Sit) seat.Release(transform);
            if (role != Role.Sit) seat = null;
            if (agent.enabled) agent.enabled = false;
            return;
        }
        if (role == Role.Sit && fixedSeat) { ApplyFixedSeat(); return; }
        SetSeatedVisual(false);
        state = State.Pause;
        timer = .5f;
        agent.enabled = true;
        if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
    }

    /// <summary>Proxy only: mirror the host's sitting pose.</summary>
    public void SetSeatedVisual(bool value)
    {
        if (visualSeated == value) return;
        visualSeated = value;
        if (motion) motion.SetSeated(value);
    }

    // ---- behaviour -------------------------------------------------------------

    bool Talking(float dt)
    {
        bool talk = dialogue && DialogueManager.Instance && DialogueManager.Instance.Current == dialogue;
        Vector3 toPlayer = NearestPlayerOffset(out bool near);
        talk |= near;
        if (state == State.Cross) talk = false; // never freeze in the middle of a road
        agent.isStopped = talk;
        if (talk && toPlayer != Vector3.zero) Face(toPlayer, 240f * dt);
        return talk;
    }

    // Local player plus the remote avatars of the room (Resources/Net/RemotePlayer clones, root name "RemotePlayer...").
    Vector3 NearestPlayerOffset(out bool near)
    {
        Vector3 best = Vector3.zero;
        float bestSqr = float.MaxValue;
        var player = SceneLoadManager.Traveller;
        if (player) Consider(player.transform.position, ref best, ref bestSqr);
        foreach (var remote in RemoteAvatars()) if (remote) Consider(remote.position, ref best, ref bestSqr);
        near = bestSqr < TalkDistance * TalkDistance;
        return best;
    }

    void Consider(Vector3 position, ref Vector3 best, ref float bestSqr)
    {
        Vector3 d = position - transform.position;
        float sqr = d.x * d.x + d.z * d.z;
        if (Mathf.Abs(d.y) >= 2f || sqr >= bestSqr) return;
        best = d; bestSqr = sqr;
    }

    static readonly System.Collections.Generic.List<Transform> remoteAvatars = new System.Collections.Generic.List<Transform>();
    static float nextRemoteScan;

    // One scan per half second for all citizens. NetPlayerSync (Player assembly) is not referenced from here, so avatars are found by name.
    internal static System.Collections.Generic.List<Transform> RemoteAvatars()
    {
        if (Time.time < nextRemoteScan) return remoteAvatars;
        nextRemoteScan = Time.time + .5f;
        remoteAvatars.Clear();
        foreach (var m in FindObjectsByType<DaldongneAvatarMotion>(FindObjectsSortMode.None))
            if (m.transform.root.name.StartsWith("RemotePlayer")) remoteAvatars.Add(m.transform.root);
        return remoteAvatars;
    }

    // ---- cars: wait while one is in front (player car and traffic cars both carry a NavMeshObstacle; traffic cars also a BoxCollider) ----

    struct Blocker { public Transform t; public Vector3 center, half; }
    static readonly System.Collections.Generic.List<Blocker> blockers = new System.Collections.Generic.List<Blocker>();
    static float nextBlockerScan;
    float blockedFor;

    static void ScanBlockers()
    {
        if (Time.time < nextBlockerScan) return;
        nextBlockerScan = Time.time + .3f;
        blockers.Clear();
        foreach (var o in FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None))
            if (o.shape == NavMeshObstacleShape.Box && o.enabled && !o.GetComponent<CitizenWalker>()) blockers.Add(new Blocker { t = o.transform, center = o.center, half = o.size * .5f });
        foreach (var car in FindObjectsByType<TrafficCar>(FindObjectsSortMode.None))
            if (!car.GetComponent<NavMeshObstacle>() && car.GetComponent<BoxCollider>() is BoxCollider box) blockers.Add(new Blocker { t = car.transform, center = box.center, half = box.size * .5f });
    }

    // Stops (and after 2 s picks another way) while a car box is within ~1.5 m ahead or under the citizen.
    bool VehicleAhead(float dt)
    {
        ScanBlockers();
        Vector3 forward = agent.velocity.sqrMagnitude > .04f ? agent.velocity.normalized : transform.forward;
        bool blocked = false;
        foreach (var b in blockers)
        {
            if (!b.t) continue;
            foreach (float d in AheadSamples)
            {
                Vector3 local = b.t.InverseTransformPoint(transform.position + forward * d) - b.center;
                if (Mathf.Abs(local.x) < b.half.x + .45f && Mathf.Abs(local.z) < b.half.z + .45f && Mathf.Abs(local.y) < b.half.y + 1f) { blocked = true; break; }
            }
            if (blocked) break;
        }
        blockedFor = blocked ? blockedFor + dt : 0f;
        agent.isStopped = blocked;
        if (blocked && blockedFor > 2f && (state == State.Walk || state == State.ToSeat)) { blockedFor = 0f; Rest(); }
        return blocked;
    }

    static readonly float[] AheadSamples = { 0f, .8f, 1.6f };

    // A reader turns toward someone who talks to them or stands close, and back to the chair's direction afterwards.
    void FaceVisitors(float dt)
    {
        Vector3 toPlayer = NearestPlayerOffset(out bool near);
        bool talk = near || (dialogue && DialogueManager.Instance && DialogueManager.Instance.Current == dialogue);
        if (talk && toPlayer != Vector3.zero) Face(toPlayer, 120f * dt);
        else transform.rotation = Quaternion.RotateTowards(transform.rotation, fixedSeat.transform.rotation, 60f * dt);
    }

    void Face(Vector3 direction, float maxDegrees)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < .0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), maxDegrees);
    }

    void Travel(float dt)
    {
        if (agent.isOnOffMeshLink) { TryCross(dt); return; }
        grace -= dt;
        stuck = agent.velocity.sqrMagnitude < .01f ? stuck + dt : 0f;
        if (agent.pathPending || grace > 0f) return;
        bool arrived = agent.pathStatus == NavMeshPathStatus.PathInvalid || agent.remainingDistance <= agent.stoppingDistance + .15f;
        if (!arrived && stuck < 1.5f) return;
        if (state == State.ToSeat && arrived && TrySit()) return;
        // Blocked (usually by another citizen head-on): turn and walk somewhere else instead of standing there.
        if (!arrived && PickDestination()) { state = State.Walk; return; }
        Rest();
    }

    void TryCross(float dt)
    {
        var link = agent.currentOffMeshLinkData;
        var crosswalk = Crosswalk.FindNear(link.startPos);
        if (crosswalk != null && !crosswalk.CanCross) { Face(link.endPos - transform.position, 240f * dt); return; }
        afterCross = state;
        crossEnd = link.endPos;
        state = State.Cross;
        stuck = 0f;
    }

    void Cross(float dt)
    {
        Vector3 next = Vector3.MoveTowards(transform.position, crossEnd, agent.speed * 1.15f * dt);
        Face(crossEnd - transform.position, 360f * dt);
        transform.position = next;
        if ((next - crossEnd).sqrMagnitude > .01f) return;
        agent.CompleteOffMeshLink();
        state = afterCross;
        grace = .3f;
    }

    void Rest()
    {
        if (agent.isOnNavMesh) agent.ResetPath();
        seat = null;
        state = State.Pause;
        timer = 2f + R() * 4f;
        baseYaw = transform.eulerAngles.y;
        lookPhase = R() * 6.28f;
        stuck = 0f;
    }

    void LookAround(float dt)
    {
        var target = Quaternion.Euler(0f, baseYaw + Mathf.Sin(Time.time * .9f + lookPhase) * lookAmplitude, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, 90f * dt);
        timer -= dt;
        if (timer > 0f) return;
        if (role == Role.Stand) { timer = 3f + R() * 3f; return; }
        if (seats.Length > 0 && R() < .2f && TrySeatTarget()) return;
        if (PickDestination()) { state = State.Walk; return; }
        timer = 1.5f;
    }

    bool PickDestination()
    {
        for (int k = 0; k < 6; k++)
        {
            float angle = R() * 6.2832f, distance = minDist + R() * (maxDist - minDist);
            Vector3 point = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            if (NavMesh.SamplePosition(point, out var hit, role == Role.Street ? 6f : 2f, NavMesh.AllAreas) && GoTo(hit.position)) return true;
        }
        return false;
    }

    // Only fully reachable targets: an unreachable one would make the agent walk to the nearest edge and stop there.
    bool GoTo(Vector3 target)
    {
        if (!agent.CalculatePath(target, path) || path.status != NavMeshPathStatus.PathComplete || !agent.SetPath(path)) return false;
        grace = .3f; stuck = 0f;
        return true;
    }

    // ---- benches ---------------------------------------------------------------

    bool TrySeatTarget()
    {
        Seat best = null;
        float bestSqr = Range * Range;
        foreach (var candidate in seats)
        {
            if (!candidate || candidate.Occupant || !candidate.isActiveAndEnabled) continue;
            float sqr = (candidate.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) { best = candidate; bestSqr = sqr; }
        }
        if (!best || !NavMesh.SamplePosition(best.transform.position, out var hit, 1.5f, NavMesh.AllAreas) || !GoTo(hit.position)) return false;
        seat = best;
        state = State.ToSeat;
        return true;
    }

    bool TrySit()
    {
        if (!seat || !seat.Reserve(transform)) { seat = null; return false; }
        standPos = transform.position;
        agent.enabled = false;
        transform.SetPositionAndRotation(seat.transform.position - Vector3.up * .44f, seat.transform.rotation);
        if (motion) motion.SetSeated(true);
        visualSeated = true;
        state = State.Seated;
        timer = 20f + R() * 40f;
        return true;
    }

    void UpdateSeated(float dt)
    {
        if (dialogue && DialogueManager.Instance && DialogueManager.Instance.Current == dialogue) return;
        timer -= dt;
        if (timer <= 0f || !seat) Stand();
    }

    void Stand()
    {
        if (seat) seat.Release(transform);
        seat = null;
        if (motion) motion.SetSeated(false);
        visualSeated = false;
        transform.position = standPos;
        agent.enabled = true;
        if (!agent.isOnNavMesh && NavMesh.SamplePosition(standPos, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
        state = State.Pause;
        timer = 1f;
    }

    // ---- far away: hide the mesh, keep the simulation -----------------------------

    void Cull()
    {
        if (Time.time < nextCull) return;
        nextCull = Time.time + .5f;
        var player = SceneLoadManager.Traveller;
        bool far = player && (player.transform.position - transform.position).sqrMagnitude > CullDistance * CullDistance;
        if (far == culled) return;
        culled = far;
        foreach (var r in renderers) if (r) r.enabled = !far;
    }
}
