using System.Collections.Generic;
using CompanyGame.Daldongne;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One citizen (Docs/Multiplayer_Redesign.md 12.14). <see cref="Plan"/> makes its day of one loop
/// (TrafficSignal.LoopSeconds) from (map, index, NpcClock.Seed) when the map opens: walks along NavMesh paths to random
/// places with short rests, sometimes to a bench of its own, a wait before each crossing until Crosswalk.CanCrossAt,
/// then home and looking around until the loop ends. It is drawn at NpcClock.Now, so every PC of one server shows it at
/// the same place. Citizens do not avoid each other. Reacting is this PC's drawing only: it stops and faces someone
/// who talks to it or stands within 1.2 m, stops for a car in front (never on a crossing), then catches up (1.5 times as
/// fast, skipping the rest of a wait; over 120 s behind it is put back on the plan). The walking pose comes from
/// DaldongneAvatarMotion reading position changes, so only the transform is moved; the prefab's NavMeshAgent stays off.
/// </summary>
[DisallowMultipleComponent]
public sealed class CitizenWalker : MonoBehaviour
{
    /// <summary>Street = wanders the map; Browse = short walks indoors; Stand = stays and chats; Sit = fixed on a chair.</summary>
    public enum Role { Street, Browse, Stand, Sit }
    public const float Range = 40f, CullDistance = 70f, TalkDistance = 1.2f;
    const float CatchUp = 1.5f;
    const double MaxDelay = 120;

    enum Kind : byte { Walk, Cross, Stand, Sit }
    struct Leg { public float t0, t1, yaw; public Vector3 a, b; public Kind kind; public Seat seat; }

    public int Index { get; private set; }
    public bool Seated => seat;
    /// <summary>Seconds this PC draws the citizen behind its plan (it stopped for someone); 0 = exactly the plan.</summary>
    public double Delay { get; private set; }
    /// <summary>Pieces of the plan (walks, crossings, rests, sitting), for QA.</summary>
    public int PlanLegs => legs.Count;
    /// <summary>Seconds of the loop spent walking or crossing, for QA and the log.</summary>
    public float WalkSeconds { get { float t = 0f; foreach (var l in legs) if (l.kind == Kind.Walk || l.kind == Kind.Cross) t += l.t1 - l.t0; return t; } }

    DaldongneAvatarMotion motion;
    DialogueData dialogue;
    Renderer[] renderers;
    Seat[] seats = System.Array.Empty<Seat>();
    Seat seat, fixedSeat;
    GameObject book;
    NavMeshPath path; // created in Awake: NavMeshPath cannot be built in a field initializer
    System.Random rng;
    readonly List<Leg> legs = new List<Leg>();
    readonly List<Vector3> corners = new List<Vector3>();
    Role role;
    int seed;
    float speed = 1.3f, minDist = 8f, maxDist = Range, lookAmplitude = 55f, lookPhase;
    double phase; // where in the loop this citizen starts: whole signal cycles, so the crossings still fit
    Vector3 home;
    bool culled, homeSet;
    float nextCull;
    double lastClock = double.NaN;

    /// <summary>Called by the spawner before the object is activated.</summary>
    public void Init(int index, int seed, Seat[] benches)
    {
        Index = index;
        this.seed = seed;
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
        transform.SetPositionAndRotation(fixedSeat.transform.position - Vector3.up * .44f, fixedSeat.transform.rotation);
        seat = fixedSeat;
        fixedSeat.Reserve(transform);
        if (motion) motion.SetSeated(true);
    }

    float R() => (float)rng.NextDouble();

    void Awake()
    {
        path = new NavMeshPath();
        var agent = GetComponent<NavMeshAgent>();
        if (agent) agent.enabled = false; // the plan moves the transform (12.14)
        motion = GetComponentInChildren<DaldongneAvatarMotion>(true);
        dialogue = GetComponent<DialogueData>();
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    void OnDisable()
    {
        if (seat) seat.Release(transform);
    }

    // ---- the plan ------------------------------------------------------------------

    /// <summary>Makes the loop's plan for this world seed (again when the seed changes). Same inputs, same plan on every PC.</summary>
    public void Plan(uint worldSeed)
    {
        if (!homeSet) { home = transform.position; homeSet = true; }
        rng = new System.Random(seed ^ (int)worldSeed);
        speed = 1.1f + R() * .4f;
        lookPhase = R() * 6.28f;
        phase = TrafficSignal.LoopSeconds / 16 * rng.Next(16); // not everyone home at the end of the loop at once
        legs.Clear();
        Delay = 0;
        lastClock = double.NaN;
        if (role == Role.Sit) return;
        float loop = (float)TrafficSignal.LoopSeconds, t = 0f;
        float startYaw = transform.eulerAngles.y;
        if (role == Role.Stand) { Rest(home, 0f, loop, startYaw); return; }
        t = Rest(home, t, .5f + R() * 2f, startYaw);
        Vector3 at = home;
        float yaw = startYaw;
        var marks = new List<(int legs, float t, Vector3 at, float yaw)>();
        for (int guard = 0; guard < 200 && t < loop * .9f; guard++)
        {
            marks.Add((legs.Count, t, at, yaw));
            Seat bench = null;
            Vector3 target = default;
            bool found = seats.Length > 0 && R() < .2f ? FindBench(at, out bench, out target) : PickDestination(at, out target);
            if (!found || !PathTo(at, target)) { marks.RemoveAt(marks.Count - 1); t = Rest(at, t, 1.5f, yaw); continue; }
            t = Walk(t, ref yaw);
            at = corners[corners.Count - 1];
            if (bench) { legs.Add(new Leg { t0 = t, t1 = t + 20f + R() * 40f, a = at, b = at, kind = Kind.Sit, seat = bench }); t = legs[legs.Count - 1].t1; }
            else t = Rest(at, t, 2f + R() * 4f, yaw);
        }
        // Home again within the loop: drop the last outings until the way back fits.
        marks.Add((legs.Count, t, at, yaw));
        for (int m = marks.Count - 1; m >= 0; m--)
        {
            legs.RemoveRange(marks[m].legs, legs.Count - marks[m].legs);
            t = marks[m].t; at = marks[m].at; yaw = marks[m].yaw;
            if ((at - home).sqrMagnitude < .01f) break;
            if (!PathTo(at, home)) continue;
            int before = legs.Count;
            float end = Walk(t, ref yaw);
            if (end <= loop - .5f) { t = end; break; }
            legs.RemoveRange(before, legs.Count - before);
        }
        Rest(home, t, loop - t, yaw);
    }

    float Rest(Vector3 at, float t, float seconds, float yaw)
    {
        if (seconds <= 0f) return t;
        legs.Add(new Leg { t0 = t, t1 = t + seconds, a = at, b = at, kind = Kind.Stand, yaw = yaw });
        return t + seconds;
    }

    // The path in corners: a walk per corner; a crossing (both ends at a Crosswalk's ends) waits until it may start.
    float Walk(float t, ref float yaw)
    {
        for (int i = 1; i < corners.Count; i++)
        {
            Vector3 a = corners[i - 1], b = corners[i];
            float length = Vector3.Distance(a, b);
            if (length < .01f) continue;
            Vector3 d = b - a;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; // facing only, never part of a position
            var walk = Crossing(a, b);
            if (walk)
            {
                float wait = 0f;
                while (wait < 60f && !walk.CanCrossAt(t + wait)) wait += .25f;
                t = Rest(a, t, wait, yaw);
                legs.Add(new Leg { t0 = t, t1 = t + length / (speed * 1.15f), a = a, b = b, kind = Kind.Cross, yaw = yaw });
            }
            else legs.Add(new Leg { t0 = t, t1 = t + length / speed, a = a, b = b, kind = Kind.Walk, yaw = yaw });
            t = legs[legs.Count - 1].t1;
        }
        return t;
    }

    static Crosswalk Crossing(Vector3 a, Vector3 b)
    {
        var walk = Crosswalk.FindNear((a + b) * .5f, 2f);
        if (!walk) return null;
        Vector3 p = walk.endA.position, q = walk.endB.position;
        bool Near(Vector3 x, Vector3 y) => new Vector2(x.x - y.x, x.z - y.z).sqrMagnitude < 2.25f;
        return Near(a, p) && Near(b, q) || Near(a, q) && Near(b, p) ? walk : null;
    }

    bool PickDestination(Vector3 from, out Vector3 target)
    {
        for (int k = 0; k < 6; k++)
        {
            // A direction without trigonometry (the same on every platform): a point in the unit square, kept when inside the circle.
            float x = R() * 2f - 1f, z = R() * 2f - 1f, distance = minDist + R() * (maxDist - minDist), sqr = x * x + z * z;
            if (sqr > 1f || sqr < .01f) continue;
            Vector3 point = from + new Vector3(x, 0f, z) * (distance / Mathf.Sqrt(sqr));
            if (NavMesh.SamplePosition(point, out var hit, role == Role.Street ? 6f : 2f, NavMesh.AllAreas)) { target = hit.position; return true; }
        }
        target = default;
        return false;
    }

    bool FindBench(Vector3 from, out Seat best, out Vector3 target)
    {
        best = null;
        target = default;
        float bestSqr = Range * Range;
        foreach (var candidate in seats)
        {
            if (!candidate || !candidate.isActiveAndEnabled) continue;
            float sqr = (candidate.transform.position - from).sqrMagnitude;
            if (sqr < bestSqr) { best = candidate; bestSqr = sqr; }
        }
        if (!best || !NavMesh.SamplePosition(best.transform.position, out var hit, 1.5f, NavMesh.AllAreas)) { best = null; return false; }
        target = hit.position;
        return true;
    }

    // Only fully reachable targets: an unreachable one would end the walk at the nearest edge.
    bool PathTo(Vector3 from, Vector3 to)
    {
        corners.Clear();
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        corners.AddRange(path.corners);
        return corners.Count >= 2;
    }

    int LegAt(double clock, out float u)
    {
        double loop = TrafficSignal.LoopSeconds;
        u = (float)((((clock + phase) % loop) + loop) % loop);
        int lo = 0, hi = legs.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (legs[mid].t0 <= u) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>Where the plan has this citizen at a shared clock value (its seat when sitting), for QA.</summary>
    public Vector3 PlanPosition(double clock)
    {
        if (legs.Count == 0) return transform.position;
        Leg leg = legs[LegAt(clock, out float u)];
        if (leg.kind == Kind.Sit) return leg.seat ? leg.seat.transform.position - Vector3.up * .44f : leg.a;
        return Vector3.Lerp(leg.a, leg.b, leg.t1 > leg.t0 ? Mathf.Clamp01((u - leg.t0) / (leg.t1 - leg.t0)) : 1f);
    }

    // ---- drawing -------------------------------------------------------------------

    void Update()
    {
        Cull();
        if (role == Role.Sit) { if (fixedSeat) FaceVisitors(Time.deltaTime); return; }
        if (legs.Count == 0) return;
        double now = NpcClock.Now, dt = now - lastClock;
        bool jumped = !(dt >= 0 && dt <= 1);
        lastClock = now;
        if (jumped) { Delay = 0; dt = 0; }
        double at = now - Delay;
        Leg leg = legs[LegAt(at, out float u)];
        Vector3 toPlayer = NearestPlayerOffset(out bool near);
        bool talk = leg.kind != Kind.Cross && (near || dialogue && DialogueManager.Instance && DialogueManager.Instance.Current == dialogue);
        bool blocked = talk || leg.kind == Kind.Walk && VehicleAhead(leg.b - leg.a);
        double rate = blocked ? 0 : Delay > 0 ? CatchUp : 1, next = System.Math.Min(now, at - dt + rate * dt); // from last frame's plan time
        if (rate > 0 && next < now)
        {
            Leg still = legs[LegAt(next, out float v)];
            if (still.kind == Kind.Stand || still.kind == Kind.Sit) next = System.Math.Min(now, next + (still.t1 - v)); // a wait anyway: skip it
        }
        Delay = now - next > MaxDelay ? 0 : now - next;
        Show(legs[LegAt(now - Delay, out u)], u, (float)dt);
        if (talk && toPlayer != Vector3.zero) Face(toPlayer, 240f * Time.deltaTime);
    }

    void Show(Leg leg, float u, float dt)
    {
        bool sit = leg.kind == Kind.Sit && leg.seat;
        if (seat && (!sit || seat != leg.seat))
        {
            seat.Release(transform);
            seat = null;
            if (motion) motion.SetSeated(false);
        }
        if (sit && (seat == leg.seat || leg.seat.Reserve(transform))) // taken by someone else (the player): stand beside it
        {
            if (seat != leg.seat) { seat = leg.seat; if (motion) motion.SetSeated(true); }
            transform.SetPositionAndRotation(seat.transform.position - Vector3.up * .44f, seat.transform.rotation);
            return;
        }
        if (leg.kind == Kind.Walk || leg.kind == Kind.Cross)
        {
            transform.position = Vector3.Lerp(leg.a, leg.b, leg.t1 > leg.t0 ? Mathf.Clamp01((u - leg.t0) / (leg.t1 - leg.t0)) : 1f);
            Face(leg.b - leg.a, 360f * dt);
            return;
        }
        transform.position = leg.a; // a rest, a crossing wait, or a bench someone else took: stand and look around
        var target = Quaternion.Euler(0f, leg.yaw + Mathf.Sin(u * .9f + lookPhase) * lookAmplitude, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, 90f * dt);
    }

    // ---- people --------------------------------------------------------------------

    // Local player plus the remote avatars on the map (Resources/Net/RemotePlayer clones, root name "RemotePlayer...").
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

    static readonly List<Transform> remoteAvatars = new List<Transform>();
    static float nextRemoteScan;

    // One scan per half second for all citizens. The remote avatars (Player assembly) are not referenced from here, so avatars are found by name.
    internal static List<Transform> RemoteAvatars()
    {
        if (Time.time < nextRemoteScan) return remoteAvatars;
        nextRemoteScan = Time.time + .5f;
        remoteAvatars.Clear();
        foreach (var m in FindObjectsByType<DaldongneAvatarMotion>(FindObjectsSortMode.None))
            if (m.transform.root.name.StartsWith("RemotePlayer")) remoteAvatars.Add(m.transform.root);
        return remoteAvatars;
    }

    // ---- cars: wait while one is in front (player car carries a NavMeshObstacle, traffic cars a BoxCollider) ----

    struct Blocker { public Transform t; public Vector3 center, half; }
    static readonly List<Blocker> blockers = new List<Blocker>();
    static float nextBlockerScan;
    static readonly float[] AheadSamples = { 0f, .8f, 1.6f };

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

    // A car box within ~1.5 m ahead of (or under) the citizen.
    bool VehicleAhead(Vector3 forward)
    {
        ScanBlockers();
        forward.y = 0f;
        forward = forward.sqrMagnitude > .0001f ? forward.normalized : transform.forward;
        foreach (var b in blockers)
        {
            if (!b.t) continue;
            foreach (float d in AheadSamples)
            {
                Vector3 local = b.t.InverseTransformPoint(transform.position + forward * d) - b.center;
                if (Mathf.Abs(local.x) < b.half.x + .45f && Mathf.Abs(local.z) < b.half.z + .45f && Mathf.Abs(local.y) < b.half.y + 1f) return true;
            }
        }
        return false;
    }

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

    // ---- far away: hide the mesh -------------------------------------------------------

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
