using System;
using UnityEngine;

/// <summary>
/// The traffic of one map as a table (Docs/Multiplayer_Redesign.md 12.14): every car's lane, distance and speed at
/// <see cref="Step"/> intervals over one loop of <see cref="TrafficSignal.LoopSeconds"/>. It is built from the lanes, the
/// car sizes and the seed only, with fixed steps, integer time and no trigonometry, so every PC builds the same table.
/// The signals and the route choices (a hash of seed, car, lane and signal cycle of the loop) repeat every loop, so the
/// traffic does too. Rules of the old per-frame TrafficCar: lane and car top speed, slowing into a turn, stopping
/// before a red or yellow light unless too fast to stop, keeping 2.2 m behind the car ahead (same lane, or the next
/// lane; a car crossing a junction counts as on its next lane), ignoring cars for 5 s after 15 s stuck. A car coming
/// to rest is put exactly on its stop line or gap, so waiting forgets small differences and the loop closes: loops are
/// run until one ends in the state an earlier one ended in (at most <see cref="MaxLoops"/>) and the repeating loops kept.
/// </summary>
public sealed class TrafficPlan
{
    public const float Step = .1f;
    public const int MaxLoops = 40;
    const double StepSeconds = .1;
    const int StepsPerCycle = (int)(TrafficSignal.CycleSeconds * 10), StepsPerLoop = TrafficSignal.CyclesPerLoop * StepsPerCycle; // 0.1 s steps
    const float MinGap = 2.2f, StopMargin = .5f, TurnSpeedMps = 6f, UnstickSeconds = 60f, IgnoreSeconds = 5f;
    const float MinSpacing = 8f, EndMargin = 8f, RestSpeed = .3f, RestSnap = .15f;

    /// <summary>One car: top speed, acceleration and braking (m/s, m/s²), front and back bumper distance from the pivot.</summary>
    public struct Car { public float top, accel, brake, front, back; }

    struct State
    {
        public int lane, next;
        public float s, v, blocked, ignore;
        public bool runLight, stopping, stopNext; // per light: going through it, stopping for it; stopping for the next lane's
    }

    readonly TrafficLane[] lanes;
    readonly float[] length, stopAt; // stopAt: where a signalled lane's cars stop (its end, or before a crossing over it)
    readonly Vector3[] startPoint, endPoint;
    readonly int[][] nexts;
    readonly int[] fromLane; // a corner's lane in (-1 for other lanes)
    readonly Car[] cars;
    readonly uint seed;
    ushort[] laneAt, nextAt;
    float[] distAt, speedAt;

    public int Count => cars.Length;
    /// <summary>Steps in the table: loops kept x 16 signal cycles x 380.</summary>
    public int Steps { get; private set; }
    public double Seconds => Steps * StepSeconds;
    /// <summary>Loops run while building, and how many repeat (0: none did, the last loop is used and jumps at its end).</summary>
    public int LoopsRun { get; private set; }
    public int Repeat { get; private set; }
    /// <summary>The largest jump of a car (metres) where the table wraps: 0 when it repeats.</summary>
    public float WrapJump { get; private set; }
    public TrafficLane Lane(int index) => lanes[index];
    /// <summary>Distance along the lane where its cars stop for its light.</summary>
    public float StopLine(int lane) => stopAt[lane];

    TrafficPlan(TrafficLane[] lanes, Car[] cars, uint seed)
    {
        this.lanes = lanes;
        this.cars = cars;
        this.seed = seed;
        length = new float[lanes.Length];
        startPoint = new Vector3[lanes.Length];
        endPoint = new Vector3[lanes.Length];
        nexts = new int[lanes.Length][];
        for (int i = 0; i < lanes.Length; i++)
        {
            length[i] = lanes[i].Count > 1 ? lanes[i].Length : 0f;
            startPoint[i] = lanes[i].Point(0);
            endPoint[i] = lanes[i].Point(lanes[i].Count - 1);
            var list = new System.Collections.Generic.List<int>();
            if (lanes[i].next != null)
                foreach (TrafficLane n in lanes[i].next)
                {
                    int index = n && n.Count > 0 ? Array.IndexOf(lanes, n) : -1;
                    if (index >= 0) list.Add(index);
                }
            nexts[i] = list.ToArray();
        }
        stopAt = StopLines(lanes, length);
        fromLane = new int[lanes.Length];
        for (int i = 0; i < lanes.Length; i++) fromLane[i] = -1;
        for (int i = 0; i < lanes.Length; i++)
            foreach (int n in nexts[i]) if (lanes[n].connector && fromLane[n] < 0) fromLane[n] = i;
    }

    // A crossing of a skewed junction can lie over an approach lane a few metres before its stop line: its cars stop
    // 2.5 m before that crossing's line instead, so none waits on a crossing while its pedestrians have green.
    static float[] StopLines(TrafficLane[] lanes, float[] length)
    {
        var stops = (float[])length.Clone();
        Crosswalk[] walks = lanes.Length > 0 && lanes[0] ? lanes[0].transform.root.GetComponentsInChildren<Crosswalk>(true) : new Crosswalk[0];
        for (int i = 0; i < lanes.Length; i++)
        {
            if (!lanes[i].signal || lanes[i].Count < 2) continue;
            float along = 0f;
            for (int k = 1; k < lanes[i].Count; k++)
            {
                Vector3 a = lanes[i].Point(k - 1), b = lanes[i].Point(k);
                float span = Vector3.Distance(a, b);
                foreach (Crosswalk walk in walks)
                {
                    if (!walk.endA || !walk.endB || !Intersect(a, b, walk.endA.position, walk.endB.position, out float f)) continue;
                    float at = along + f * span;
                    if (at < length[i] - .5f && at > length[i] - 25f) stops[i] = Mathf.Min(stops[i], at - 2.5f);
                }
                along += span;
            }
        }
        return stops;
    }

    // Where segment ab crosses segment cd (fraction along ab), on the ground plane.
    static bool Intersect(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out float f)
    {
        float rx = b.x - a.x, rz = b.z - a.z, sx = d.x - c.x, sz = d.z - c.z, den = rx * sz - rz * sx;
        f = 0f;
        if (Mathf.Abs(den) < 1e-6f) return false;
        float qx = c.x - a.x, qz = c.z - a.z;
        f = (qx * sz - qz * sx) / den;
        float g = (qx * rz - qz * rx) / den;
        return f >= 0f && f <= 1f && g >= 0f && g <= 1f;
    }

    /// <summary>
    /// Places the cars from the seed (on lanes that are not corners, 8 m apart; a car that finds no place is left out)
    /// and builds the table. <paramref name="lanes"/> must be in the same order on every PC (the scene's).
    /// </summary>
    public static TrafficPlan Build(TrafficLane[] lanes, Car[] candidates, uint seed)
    {
        var plan = new TrafficPlan(lanes, candidates, seed);
        State[] start = plan.Place(out int placed);
        if (placed < candidates.Length)
        {
            var kept = new Car[placed];
            Array.Copy(candidates, kept, placed);
            plan = new TrafficPlan(lanes, kept, seed);
            Array.Resize(ref start, placed);
        }
        plan.Run(start);
        return plan;
    }

    State[] Place(out int placed)
    {
        var rng = new System.Random(unchecked((int)seed));
        var spawn = new System.Collections.Generic.List<int>();
        float total = 0f;
        for (int i = 0; i < lanes.Length; i++)
            if (!lanes[i].connector && lanes[i].Count > 1 && length[i] > EndMargin * 2f && nexts[i].Length > 0) { spawn.Add(i); total += length[i]; }
        var states = new State[cars.Length];
        var points = new Vector3[cars.Length];
        placed = 0;
        for (int c = 0; c < cars.Length && spawn.Count > 0; c++)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float roll = (float)rng.NextDouble() * total;
                int lane = spawn[spawn.Count - 1];
                foreach (int i in spawn) { roll -= length[i]; if (roll <= 0f) { lane = i; break; } }
                float s = EndMargin + (float)rng.NextDouble() * (length[lane] - EndMargin * 2f);
                Vector3 p = lanes[lane].Sample(s, out _, out _);
                bool clear = true;
                for (int o = 0; o < placed && clear; o++) clear = (points[o] - p).sqrMagnitude >= MinSpacing * MinSpacing;
                if (!clear) continue;
                points[placed] = p;
                states[placed] = new State { lane = lane, s = s, next = Choose(placed, lane, 0) };
                placed++;
                break;
            }
            if (placed == c) break; // no room left: fewer cars
        }
        return states;
    }

    // ---- building ----

    void Run(State[] state)
    {
        var loopStarts = new System.Collections.Generic.List<State[]> { (State[])state.Clone() };
        int from = -1;
        for (int loop = 1; loop <= MaxLoops && from < 0; loop++)
        {
            for (int k = 0; k < StepsPerLoop; k++) Advance(state, k, -1);
            LoopsRun = loop;
            for (int m = 0; m < loopStarts.Count && from < 0; m++) if (Same(loopStarts[m], state)) from = m;
            loopStarts.Add((State[])state.Clone());
        }
        int loops = from >= 0 ? loopStarts.Count - 1 - from : 1;
        Repeat = from >= 0 ? loops : 0;
        State[] record = (State[])loopStarts[from >= 0 ? from : loopStarts.Count - 2].Clone();
        Steps = loops * StepsPerLoop;
        laneAt = new ushort[Steps * cars.Length];
        nextAt = new ushort[Steps * cars.Length];
        distAt = new float[Steps * cars.Length];
        speedAt = new float[Steps * cars.Length];
        for (int k = 0; k < Steps; k++) Advance(record, k % StepsPerLoop, k);
        if (Repeat == 0)
            for (int c = 0; c < cars.Length; c++)
                WrapJump = Mathf.Max(WrapJump, Vector3.Distance(Position(record[c].lane, record[c].next, record[c].s), PositionAt(c, 0)));
    }

    static bool Same(State[] a, State[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if (a[i].lane != b[i].lane || a[i].next != b[i].next || a[i].s != b[i].s || a[i].v != b[i].v
                || a[i].blocked != b[i].blocked || a[i].ignore != b[i].ignore || a[i].runLight != b[i].runLight
                || a[i].stopping != b[i].stopping || a[i].stopNext != b[i].stopNext) return false;
        return true;
    }

    // Scratch for one step: where each car counts (a car crossing a junction counts on its next lane, at minus the
    // distance still to go), per lane a list of the cars counted there, and per lane the cars crossing out of its end.
    int[] effLane, firstInLane, nextInLane, firstOut, nextOut;
    float[] effS, outS, allowed, restAt;

    // One step of every car from the state at its start (all caps first, then all moves): the order of the cars never
    // matters. row >= 0 writes the state at the start of the step into that table row.
    void Advance(State[] state, int k, int row)
    {
        int n = state.Length;
        if (effLane == null)
        {
            effLane = new int[n]; effS = new float[n]; allowed = new float[n]; restAt = new float[n]; nextInLane = new int[n]; nextOut = new int[n]; outS = new float[n];
            firstInLane = new int[lanes.Length]; firstOut = new int[lanes.Length];
        }
        for (int i = 0; i < lanes.Length; i++) firstInLane[i] = firstOut[i] = -1;
        for (int c = n - 1; c >= 0; c--)
        {
            ref State st = ref state[c];
            if (row >= 0)
            {
                int at = row * n + c;
                laneAt[at] = (ushort)st.lane; nextAt[at] = (ushort)Math.Max(0, st.next); distAt[at] = st.s; speedAt[at] = st.v;
            }
            bool crossing = st.next >= 0 && st.s > length[st.lane];
            effLane[c] = crossing ? st.next : st.lane;
            effS[c] = crossing ? st.s - Route(st.lane, st.next) : st.s;
            nextInLane[c] = firstInLane[effLane[c]];
            firstInLane[effLane[c]] = c;
            // Out of a lane's end: crossing toward the next lane, or just round a corner (until its back is clear of the turn).
            bool turning = lanes[st.lane].connector && fromLane[st.lane] >= 0 && st.s < cars[c].back + 6f;
            int from = turning ? fromLane[st.lane] : crossing ? st.lane : -1;
            if (from >= 0)
            {
                outS[c] = turning ? Route(from, st.lane) + st.s : st.s;
                nextOut[c] = firstOut[from];
                firstOut[from] = c;
            }
        }
        double clock = (k % StepsPerLoop) * StepSeconds;
        for (int c = 0; c < n; c++) allowed[c] = Allowed(state, c, clock);
        for (int c = 0; c < n; c++) Move(ref state[c], c, k);
    }

    float Allowed(State[] state, int c, double clock)
    {
        ref State st = ref state[c];
        Car car = cars[c];
        TrafficLane lane = lanes[st.lane];
        float cap = Mathf.Min(car.top, lane.speedLimitKmh / 3.6f);
        float remaining = Mathf.Max(0f, length[st.lane] - st.s);
        if (lane.connector) cap = Mathf.Min(cap, TurnSpeedMps);
        else if (st.next >= 0 && lanes[st.next].connector) cap = Mathf.Min(cap, Mathf.Sqrt(TurnSpeedMps * TurnSpeedMps + 1.4f * car.brake * remaining));
        restAt[c] = float.MaxValue;

        // Signal: the old SignalCap, but decided once per light (the old check, made again every frame, switched to going
        // through when the braking lagged behind its curve right before the line).
        float toLine = stopAt[st.lane] - st.s;
        if (lane.signal && !st.runLight && st.s <= length[st.lane] && lane.signal.PhaseAt(clock, out _, out _) != TrafficSignal.Phase.Green)
        {
            float stop = toLine - car.front - StopMargin;
            if (stop >= -1.5f)
            {
                bool cannotStop = st.v * st.v / (2f * Mathf.Max(.1f, stop)) > car.brake * 1.3f;
                if (!st.stopping && cannotStop && stop > .5f) st.runLight = true; // too late to stop: keep going through
                else
                {
                    st.stopping = true;
                    cap = Mathf.Min(cap, Mathf.Sqrt(2f * car.brake * .8f * Mathf.Max(0f, stop)));
                    restAt[c] = st.s + stop;
                }
            }
        }
        // The next lane's light: a lane between two close junctions is shorter than the braking, so look one lane ahead and
        // start stopping for it when that is possible (the old code saw it only on the short lane and ran it).
        bool beyond = st.next >= 0 && st.s > length[st.lane];
        TrafficSignal nextSignal = st.next >= 0 && !beyond ? lanes[st.next].signal : null;
        if (nextSignal && nextSignal.PhaseAt(clock, out _, out _) != TrafficSignal.Phase.Green)
        {
            float stop = Route(st.lane, st.next) - st.s + stopAt[st.next] - car.front - StopMargin;
            if (stop >= 0f && (st.stopNext || st.v * st.v / (2f * Mathf.Max(.1f, stop)) <= car.brake * 1.3f))
            {
                st.stopNext = true;
                float nextCap = Mathf.Sqrt(2f * car.brake * .8f * stop);
                if (nextCap < cap) { cap = nextCap; restAt[c] = Mathf.Min(restAt[c], st.s + stop); }
            }
        }

        // Do not block the junction: before a stop line, wait there while the lane after the junction (after the corner of a
        // turn) has no room for this car behind a car that is (nearly) stopped, so nobody is left on a crossing or in the
        // junction when the pedestrians get green.
        if (lane.signal && !beyond && st.next >= 0)
        {
            float stop = toLine - car.front - StopMargin;
            int after = lanes[st.next].connector && nexts[st.next].Length == 1 ? nexts[st.next][0] : st.next;
            if (stop > .5f && Full(state, c, after, car.front + car.back + MinGap + 6f))
            {
                float boxCap = Mathf.Sqrt(2f * car.brake * .8f * stop);
                if (boxCap < cap) { cap = boxCap; restAt[c] = Mathf.Min(restAt[c], st.s + stop); }
            }
        }

        // The car ahead: the old ObstacleCap, over the plan's cars instead of physics.
        float ahead = Mathf.Clamp(4f + st.v * .6f, 4f, 14f), best = float.MaxValue, otherSpeed = 0f;
        if (st.ignore <= 0f)
        {
            Nearest(state, c, firstInLane[effLane[c]], -1f, ahead, ref best, ref otherSpeed);
            bool crossing = st.next >= 0 && st.s > length[st.lane];
            if (!crossing && st.next >= 0) Nearest(state, c, firstInLane[st.next], Route(st.lane, st.next) - st.s, ahead, ref best, ref otherSpeed);
            // A car crossing out of my lane's end toward another lane is still in front of me until it has turned away.
            for (int o = firstOut[st.lane]; o >= 0; o = nextOut[o])
            {
                float d = outS[o] - st.s;
                if (o == c || d < 0f || (d == 0f && o > c)) continue;
                float gap = Mathf.Max(0f, d - car.front - cars[o].back);
                if (gap <= ahead && gap < best) { best = gap; otherSpeed = state[o].v; }
            }
        }
        bool blocked = best != float.MaxValue;
        if (blocked)
        {
            float gapCap = best <= MinGap ? 0f : Mathf.Sqrt(2f * car.brake * .7f * (best - MinGap)) + otherSpeed * .8f;
            if (gapCap < cap) { cap = gapCap; restAt[c] = Mathf.Min(restAt[c], st.s + best - MinGap); }
        }
        st.blocked = blocked && st.v < .2f ? st.blocked + Step : 0f;
        if (st.blocked > UnstickSeconds) { st.ignore = IgnoreSeconds; st.blocked = 0f; }
        return cap;
    }

    // A car whose back is less than room metres into the lane and slow (under 4 m/s), another car than c.
    bool Full(State[] state, int c, int lane, float room)
    {
        for (int o = firstInLane[lane]; o >= 0; o = nextInLane[o])
            if (o != c && state[o].v < 4f && effS[o] - cars[o].back < room) return true;
        return false;
    }

    // offset < 0: cars on my counted lane ahead of me; else cars on my next lane, offset metres further on.
    void Nearest(State[] state, int c, int first, float offset, float ahead, ref float best, ref float otherSpeed)
    {
        for (int o = first; o >= 0; o = nextInLane[o])
        {
            if (o == c) continue;
            float d = offset >= 0f ? offset + effS[o] : effS[o] - effS[c];
            if (d < 0f || (d == 0f && offset < 0f && o > c)) continue; // behind me (a tie goes to the lower number)
            float gap = Mathf.Max(0f, d - cars[c].front - cars[o].back);
            if (gap > ahead || gap >= best) continue;
            best = gap;
            otherSpeed = state[o].v;
        }
    }

    void Move(ref State st, int c, int k)
    {
        Car car = cars[c];
        float cap = allowed[c];
        float v = Mathf.MoveTowards(st.v, cap, Step * (cap > st.v ? car.accel : car.brake));
        float s = st.s + v * Step;
        if (cap < RestSpeed && restAt[c] - s < RestSnap) { s = Mathf.Max(st.s, restAt[c]); v = 0f; } // at rest: exactly on the line or gap
        st.v = v;
        if (st.ignore > 0f) st.ignore = Mathf.Max(0f, st.ignore - Step);
        if (st.next < 0) { if (s >= length[st.lane]) { s = length[st.lane]; st.v = 0f; } st.s = s; return; } // a road end: wait there
        for (int guard = 0; guard < 4 && st.next >= 0 && s >= Route(st.lane, st.next); guard++)
        {
            s -= Route(st.lane, st.next);
            st.lane = st.next;
            st.runLight = false;
            st.stopping = st.stopNext;
            st.stopNext = false;
            st.next = Choose(c, st.lane, k + 1);
        }
        st.s = s;
    }

    float Route(int lane, int next) => length[lane] + Vector3.Distance(endPoint[lane], startPoint[next]);

    // The continuation a car takes on entering a lane: the same for the same seed, car, lane and signal cycle of the loop.
    int Choose(int car, int lane, int step)
    {
        int[] options = nexts[lane];
        if (options.Length == 0) return -1;
        uint h = seed ^ (uint)car * 0x9E3779B1u ^ (uint)lane * 0x85EBCA77u ^ (uint)((step % StepsPerLoop) / StepsPerCycle) * 0xC2B2AE3Du;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
        return options[h % (uint)options.Length];
    }

    // ---- reading ----

    /// <summary>A world position on lane at distance s; past the lane's end it is on the straight line to next's start.</summary>
    public Vector3 Position(int lane, int next, float s)
    {
        float end = length[lane];
        if (s <= end || next < 0) return lanes[lane].Count > 1 ? lanes[lane].Sample(s, out _, out _) : lanes[lane].Point(0);
        Vector3 a = endPoint[lane], b = startPoint[next];
        float link = Vector3.Distance(a, b);
        return link < .0001f ? b : Vector3.Lerp(a, b, (s - end) / link);
    }

    Vector3 PositionAt(int car, int row)
    {
        int at = row * cars.Length + car;
        return Position(laneAt[at], nexts[laneAt[at]].Length > 0 ? nextAt[at] : -1, distAt[at]);
    }

    /// <summary>
    /// Car <paramref name="car"/> at shared clock <paramref name="clock"/>: position between the two nearest steps, speed,
    /// and the lane and distance of the earlier step.
    /// </summary>
    public Vector3 Sample(int car, double clock, out float speed, out int lane, out float s)
    {
        double u = clock % Seconds;
        if (u < 0) u += Seconds;
        double f = u / StepSeconds;
        int row = Math.Min(Steps - 1, (int)f), row1 = (row + 1) % Steps;
        float a = (float)(f - row);
        int i0 = row * cars.Length + car, i1 = row1 * cars.Length + car;
        lane = laneAt[i0]; s = distAt[i0];
        speed = Mathf.Lerp(speedAt[i0], speedAt[i1], a);
        int next = nexts[lane].Length > 0 ? nextAt[i0] : -1;
        if (laneAt[i1] == lane) return Position(lane, next, Mathf.Lerp(s, distAt[i1], a));
        return Vector3.Lerp(Position(lane, next, s), PositionAt(car, row1), a);
    }

    /// <summary>Seconds from <paramref name="clock"/> until the car moves again (0 when it is moving).</summary>
    public double StillFor(int car, double clock)
    {
        double u = clock % Seconds;
        if (u < 0) u += Seconds;
        int row = Math.Min(Steps - 1, (int)(u / StepSeconds)), count = 0;
        for (int r = row; count < Steps; r = (r + 1) % Steps, count++)
            if (speedAt[r * cars.Length + car] > 0f) break;
        return count == 0 ? 0 : Math.Max(0, count * StepSeconds - (u - row * StepSeconds));
    }
}
