using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The NPC cars of a map (Docs/Multiplayer_Redesign.md 12.14). Builds the <see cref="TrafficPlan"/> from the lanes and
/// NpcClock.Seed (again whenever the seed changes: a server's seed may come after the map) and draws every car at
/// NpcClock.Now, so every PC of one server shows the same cars at the same places. Car models come from the seed.
/// Reacting to people is this PC's drawing only: a car with someone (the player, a remote avatar, a citizen, the
/// player's car) or a car drawn further behind the plan in its front box slows and stops; its <see cref="TrafficCar.Delay"/>
/// grows. Afterwards it runs 1.5 times the plan's pace, skips the rest of a wait the plan has it in, and while behind
/// it holds before a stop line that is red now. Over 120 s behind, or after a clock jump, it is put back on the plan.
/// </summary>
public sealed class TrafficSpawner : MonoBehaviour
{
    public const int MaxCars = 40;
    const float MinGap = 2.2f, StopMargin = .5f, CatchUp = 1.5f;
    const double Smooth = .3, MaxDelay = 120;

    [Range(0, MaxCars)] public int count = 8;
    public TrafficLane[] lanes = Array.Empty<TrafficLane>();

    readonly List<TrafficCar> cars = new List<TrafficCar>();
    readonly List<Transform> people = new List<Transform>();
    List<ItemData> models;
    TrafficPlan plan;
    uint builtSeed;
    bool built;
    double lastClock = double.NaN;

    public IEnumerable<TrafficCar> Cars => cars;
    /// <summary>The plan the cars are drawn from (null before the first frame or without models/lanes).</summary>
    public TrafficPlan Plan => plan;

    void Awake()
    {
        if (lanes == null || lanes.Length == 0) lanes = GetComponentsInChildren<TrafficLane>();
        models = LoadModels();
    }

    /// <summary>The car items NPC traffic uses, in item id order (the same on every PC).</summary>
    public static List<ItemData> LoadModels()
    {
        var list = new List<ItemData>();
        foreach (ItemData item in Resources.LoadAll<ItemData>("Inventory/Vehicles/Cars"))
            if (item && item.vehiclePrefab) list.Add(item);
        list.Sort((a, b) => string.CompareOrdinal(a.itemId, b.itemId));
        return list;
    }

    /// <summary>The models of <paramref name="cars"/> cars for a seed, and their plan sizes.</summary>
    public static ItemData[] PickModels(List<ItemData> models, int cars, uint seed, out TrafficPlan.Car[] specs)
    {
        var rng = new System.Random(unchecked((int)(seed * 31u + 7u)));
        var picks = new ItemData[models.Count == 0 ? 0 : cars];
        specs = new TrafficPlan.Car[picks.Length];
        for (int i = 0; i < picks.Length; i++)
        {
            ItemData item = picks[i] = models[rng.Next(models.Count)];
            Bounds body = VehicleBounds.Measure(item.vehiclePrefab);
            VehicleGrade grade = item.grade;
            specs[i] = new TrafficPlan.Car
            {
                top = grade ? grade.TopSpeedMps : 12f, accel = grade ? grade.AccelMps2 : 3f, brake = grade ? grade.brakeMps2 : 8f,
                front = body.center.z + body.extents.z, back = body.extents.z - body.center.z,
            };
        }
        return picks;
    }

    void Update()
    {
        if (!built || NpcClock.Seed != builtSeed) Build();
        if (plan != null) Drive();
    }

    void Build()
    {
        foreach (TrafficCar car in cars) if (car) Destroy(car.gameObject);
        cars.Clear();
        plan = null;
        built = true;
        builtSeed = NpcClock.Seed;
        if (models.Count == 0 || lanes.Length == 0) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ItemData[] picks = PickModels(models, Mathf.Min(count, MaxCars), builtSeed, out TrafficPlan.Car[] specs);
        plan = TrafficPlan.Build(lanes, specs, builtSeed);
        for (int i = 0; i < plan.Count; i++) cars.Add(Create(picks[i], i));
        lastClock = double.NaN;
        Debug.Log("[Traffic] " + gameObject.scene.name + ": " + plan.Count + " cars, seed " + builtSeed + ", plan " + plan.Seconds + " s ("
                  + plan.LoopsRun + " loops run, " + (plan.Repeat > 0 ? "repeats" : "no repeat, wrap jump " + plan.WrapJump.ToString("0.0") + " m")
                  + ") in " + watch.ElapsedMilliseconds + " ms");
    }

    TrafficCar Create(ItemData item, int key)
    {
        GameObject go = Instantiate(item.vehiclePrefab, Vector3.zero, Quaternion.identity, transform);
        go.name = "NpcCar_" + key;
        TrafficCar car = go.AddComponent<TrafficCar>();
        car.grade = item.grade;
        car.wheelRadius = item.wheelRadius;
        car.itemId = item.itemId;
        car.key = key;
        car.Init();
        return car;
    }

    // ---- drawing ----

    void Drive()
    {
        double now = NpcClock.Now, dt = now - lastClock;
        bool jumped = !(dt >= 0 && dt <= 1); // first frame, a skipped night, a clock snap: everyone back on the plan
        lastClock = now;
        if (jumped) dt = 0;
        GatherPeople();
        for (int i = 0; i < cars.Count; i++)
        {
            TrafficCar car = cars[i];
            if (jumped) car.Delay = 0;
            double at = now - car.Delay;
            Pose(i, at, out Vector3 p, out Vector3 forward, out float speed, out int lane, out float s);
            double rate = 1;
            if (dt > 0)
            {
                rate = car.Delay > 0 ? CatchUp : 1;
                float ahead = Mathf.Clamp(4f + speed * .6f, 4f, 14f);
                float gap = Obstacle(car, p, forward, ahead);
                if (car.Delay > 0) gap = Mathf.Min(gap, RedLine(car, lane, s));
                if (gap < float.MaxValue) rate = Math.Min(rate, Mathf.Clamp01((gap - MinGap) / Mathf.Max(.5f, ahead - MinGap)));
                double next = Math.Min(now, at - dt + rate * dt); // from last frame's plan time (at assumes full pace)
                if (rate > 0 && next < now) next = Math.Min(now, next + plan.StillFor(i, next)); // the plan waits here anyway: no visible change
                car.Delay = now - next > MaxDelay ? 0 : now - next;
                if (next != at) Pose(i, now - car.Delay, out p, out forward, out speed, out _, out _);
            }
            car.Show(p, forward, (float)(speed * rate));
        }
    }

    // Three plan samples 0.3 s apart: the average rounds the corners of the lane polylines, the outer two give the heading.
    void Pose(int car, double at, out Vector3 p, out Vector3 forward, out float speed, out int lane, out float s)
    {
        Vector3 before = plan.Sample(car, at - Smooth, out _, out _, out _);
        Vector3 here = plan.Sample(car, at, out speed, out lane, out s);
        Vector3 after = plan.Sample(car, at + Smooth, out _, out _, out _);
        p = (before + here + after) / 3f;
        forward = after - before;
        forward.y = 0f;
    }

    void GatherPeople()
    {
        people.Clear();
        Behaviour player = SceneLoadManager.Traveller;
        if (player) people.Add(player.transform);
        foreach (Transform remote in CitizenWalker.RemoteAvatars()) if (remote) people.Add(remote);
        if (CitizenSpawner.Current) foreach (CitizenWalker w in CitizenSpawner.Current.Citizens) if (w) people.Add(w.transform);
    }

    // Metres from the front bumper to the nearest person, player car or more delayed NPC car in the front box (MaxValue: none).
    float Obstacle(TrafficCar car, Vector3 p, Vector3 forward, float ahead)
    {
        if (forward.sqrMagnitude < 1e-6f) forward = car.Forward;
        forward.Normalize();
        Bounds body = car.Body;
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);
        Vector3 front = p + Vector3.up * body.center.y + right * body.center.x + forward * (body.center.z + body.extents.z);
        float half = body.extents.x * .9f, best = float.MaxValue;
        foreach (Transform t in people)
        {
            // The player's car is the NavMeshObstacle box PlayerVehicle puts on the player (body bounds, not just the pivot).
            var box = t.GetComponent<NavMeshObstacle>();
            if (box && box.enabled) Nearest(t, box.center, box.size, front, forward, right, half, ahead, ref best);
            else Nearest(t, Vector3.zero, Vector3.zero, front, forward, right, half, ahead, ref best);
        }
        foreach (TrafficCar other in cars)
            if (other != car && other.Delay > car.Delay + .05) Nearest(other.transform, other.Body.center, other.Body.size, front, forward, right, half, ahead, ref best);
        return best;
    }

    static void Nearest(Transform t, Vector3 center, Vector3 size, Vector3 front, Vector3 forward, Vector3 right, float half, float ahead, ref float best)
    {
        for (int i = 0; i < 9; i++)
        {
            Vector3 local = center + Vector3.Scale(size * .5f, i == 8 ? Vector3.zero
                : new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 d = t.TransformPoint(local) - front;
            float along = Vector3.Dot(d, forward);
            if (along > -.5f && along < ahead && Mathf.Abs(Vector3.Dot(d, right)) < half + .8f && Mathf.Abs(d.y) < 2.5f)
                best = Mathf.Min(best, Mathf.Max(0f, along));
        }
    }

    // A car drawn behind the plan saw an older green: before a stop line that is red or yellow now it waits (gap to the line).
    float RedLine(TrafficCar car, int lane, float s)
    {
        TrafficLane l = plan.Lane(lane);
        if (!l.signal || l.signal.Current == TrafficSignal.Phase.Green) return float.MaxValue;
        float stop = l.Length - s - (car.Body.center.z + car.Body.extents.z) - StopMargin;
        return stop >= 0f ? stop + MinGap : float.MaxValue;
    }
}

/// <summary>Wires gameplay-level code that cannot see World (SpeedCamera) to the player.</summary>
static class TrafficBridge
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        SpeedCamera.PlayerProvider = () => SceneLoadManager.Traveller ? SceneLoadManager.Traveller.transform : null;
    }
}
