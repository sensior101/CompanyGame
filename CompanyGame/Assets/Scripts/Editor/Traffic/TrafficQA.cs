using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Checks the traffic code without Play Mode: road sampling on synthetic meshes, the junction/lane graph, signal
/// cycles, crossings, the speed camera fine logic, and (if the map was built) the lanes in BusinessDistrict.
/// Run with: -executeMethod TrafficQA.Run. Throws when something fails.
/// </summary>
public static class TrafficQA
{
    sealed class Report
    {
        public int checks;
        public readonly List<string> failures = new List<string>();
        public readonly List<string> notes = new List<string>();
    }

    [MenuItem("CompanyGame/Traffic/Run QA")]
    static void Menu() => Run();

    public static void Run()
    {
        var report = new Report();
        if (EditorApplication.isPlayingOrWillChangePlaymode) report.failures.Add("Run in Edit mode.");
        else
        {
            Guarded(report, "sampler", () => SamplerTests(report));
            Guarded(report, "graph", () => GraphTests(report));
            Guarded(report, "signal", () => SignalTests(report));
            Guarded(report, "camera", () => CameraTests(report));
            Guarded(report, "scene", () => SceneTests(report));
        }
        string text = $"Traffic QA: {report.checks} checks, {report.failures.Count} failures\n" +
            string.Join("\n", report.failures.ConvertAll(f => "FAIL " + f)) + "\n" + string.Join("\n", report.notes);
        Debug.Log(text);
        if (report.failures.Count > 0) throw new InvalidOperationException(text);
    }

    static void Guarded(Report report, string name, Action body)
    {
        try { body(); }
        catch (Exception exception) { report.failures.Add($"{name} threw {exception.GetType().Name}: {exception.Message}"); }
    }

    static void Check(Report report, bool ok, string what)
    {
        report.checks++;
        if (!ok) report.failures.Add(what);
    }

    // ---- road sampler on meshes built here -----------------------------------------------------------------

    static void SamplerTests(Report report)
    {
        Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var toWorld = Matrix4x4.TRS(new Vector3(10f, .025f, 47f), Quaternion.identity, new Vector3(400f, .05f, 14f));
        List<RoadCenterline> straight = RoadSampler.Sample(cube, toWorld, "Street_T");
        Check(report, straight.Count == 1, "cube road gives one centre line");
        if (straight.Count == 1)
        {
            RoadCenterline line = straight[0];
            Check(report, Mathf.Abs(line.width - 14f) < .3f, $"cube road width {line.width:0.00} is 14");
            Check(report, line.points.TrueForAll(p => Mathf.Abs(p.z - 47f) < .05f), "cube road centre line stays on z=47");
            Check(report, new RoadPolyline(line.points).Length > 395f, "cube road centre line covers the whole 400 m");
        }
        var arcMesh = ArcMesh(330f, 342f, 7f, 173f, 120);
        List<RoadCenterline> arcs = RoadSampler.Sample(arcMesh, Matrix4x4.identity, "PerimeterRoad_T");
        Check(report, arcs.Count == 1, "arc road gives one centre line");
        if (arcs.Count == 1)
        {
            RoadCenterline arc = arcs[0];
            Check(report, Mathf.Abs(arc.width - 12f) < .3f, $"arc width {arc.width:0.00} is 12");
            float worst = 0f;
            foreach (Vector3 p in arc.points) worst = Mathf.Max(worst, Mathf.Abs(new Vector2(p.x, p.z).magnitude - 336f));
            Check(report, worst < .15f, $"arc centre line is within 15 cm of radius 336 (worst {worst:0.000})");
            float expected = 336f * 166f * Mathf.Deg2Rad;
            Check(report, new RoadPolyline(arc.points).Length > expected * .96f, "arc centre line covers the whole arc");
            var surface = new RoadSurface();
            surface.Add(arcMesh, Matrix4x4.identity);
            Check(report, arc.points.TrueForAll(p => surface.Contains(p)), "arc centre line points lie on the arc mesh");
        }
        UnityEngine.Object.DestroyImmediate(arcMesh);
    }

    static Mesh ArcMesh(float inner, float outer, float fromDeg, float toDeg, int quads)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i < quads; i++)
        {
            float a = Mathf.Lerp(fromDeg, toDeg, (float)i / quads) * Mathf.Deg2Rad, b = Mathf.Lerp(fromDeg, toDeg, (i + 1f) / quads) * Mathf.Deg2Rad;
            int start = vertices.Count;
            vertices.Add(new Vector3(Mathf.Cos(a) * inner, .045f, Mathf.Sin(a) * inner));
            vertices.Add(new Vector3(Mathf.Cos(b) * inner, .045f, Mathf.Sin(b) * inner));
            vertices.Add(new Vector3(Mathf.Cos(b) * outer, .045f, Mathf.Sin(b) * outer));
            vertices.Add(new Vector3(Mathf.Cos(a) * outer, .045f, Mathf.Sin(a) * outer));
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        return mesh;
    }

    // ---- junction graph on two crossing roads --------------------------------------------------------------

    static RoadCenterline Straight(string name, Vector3 a, Vector3 b, float width)
    {
        var line = new RoadCenterline { name = name, width = width };
        line.points.Add(a);
        line.points.Add(b);
        return line;
    }

    static void GraphTests(Report report)
    {
        var roads = new List<RoadCenterline>
        {
            Straight("Street_EW", new Vector3(-100f, 0f, 0f), new Vector3(100f, 0f, 0f), 14f),
            Straight("Street_NS", new Vector3(0f, 0f, -100f), new Vector3(0f, 0f, 100f), 14f),
        };
        RoadGraph graph = RoadGraph.Build(roads);
        Check(report, graph.nodes.Count == 1 && graph.nodes[0].arms.Count == 4, "a crossing makes one 4-arm junction");
        if (graph.nodes.Count != 1) return;
        RoadNode node = graph.nodes[0];
        Check(report, node.Signalled, "a 4-arm junction is signalled");
        Check(report, graph.lanes.FindAll(l => !l.connector).Count == 8, "4 arms give 8 lanes (one per direction)");
        foreach (LaneSpec lane in graph.lanes)
        {
            if (lane.connector) continue;
            Vector3 end = lane.points[lane.points.Count - 1];
            Vector3 start = lane.points[0];
            bool arriving = lane.endsAt != null;
            Vector3 stop = arriving ? end : start;
            Check(report, Mathf.Abs(RoadPolyline.Flat(stop - node.position).magnitude - Mathf.Sqrt(node.clearance * node.clearance + 3.5f * 3.5f)) < .1f,
                lane.name + " stops one clearance from the junction");
        }
        LaneSpec east = graph.lanes.Find(l => !l.connector && l.points[0].x < -50f && l.points[l.points.Count - 1].x > -50f && l.points[0].x < l.points[l.points.Count - 1].x);
        Check(report, east != null && east.points[0].z < -3f, "eastbound lane is on the south (right-hand) side");
        Check(report, east != null && east.next.Count == 2 && east.next.Exists(n => n.connector), "eastbound lane may go straight or turn right");
        var corners = new HashSet<LaneSpec>();
        foreach (LaneSpec lane in graph.lanes) foreach (LaneSpec n in lane.next) if (n.connector) corners.Add(n);
        Check(report, corners.Count == 4, "four right-turn corners (connectors are reached through next, not listed in lanes)");
    }

    // ---- signal cycle, crossing ----------------------------------------------------------------------------------

    static void SignalTests(Report report)
    {
        var go = new GameObject("TrafficQA_Signal") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            float g = TrafficMapBuilder.Green, y = TrafficMapBuilder.Yellow, r = TrafficMapBuilder.Red, cycle = g + y + r;
            TrafficSignal signal = go.AddComponent<TrafficSignal>();
            signal.green = g; signal.yellow = y; signal.red = r; signal.offset = 0f;
            Check(report, signal.PhaseAt(0, out _, out _) == TrafficSignal.Phase.Green, "t=0 is green");
            Check(report, signal.PhaseAt(g - .1f, out _, out _) == TrafficSignal.Phase.Green, "just before the green ends it is green");
            Check(report, signal.PhaseAt(g, out _, out _) == TrafficSignal.Phase.Yellow, "after green comes yellow");
            Check(report, signal.PhaseAt(g + y, out _, out _) == TrafficSignal.Phase.Red, "after yellow comes red");
            Check(report, signal.PhaseAt(cycle, out _, out _) == TrafficSignal.Phase.Green, "the cycle wraps to green");
            Check(report, signal.PhaseAt(-1, out _, out float left) == TrafficSignal.Phase.Red && Mathf.Abs(left - 1f) < .01f, "t=-1 is red with 1 s left");
            var other = go.AddComponent<TrafficSignal>();
            other.green = g; other.yellow = y; other.red = r; other.offset = cycle * .5f;
            bool clash = false;
            for (double t = 0; t < cycle * 2; t += .25)
                clash |= signal.PhaseAt(t, out _, out _) != TrafficSignal.Phase.Red && other.PhaseAt(t, out _, out _) != TrafficSignal.Phase.Red;
            Check(report, !clash, "crossing groups are never open at the same time");
            CrosswalkTests(report, go, signal);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    // Moves the signal's offset so that the shared clock reads phase time t right now.
    static void Align(TrafficSignal signal, float t) => signal.offset = (float)(t - TrafficSignal.Clock % signal.Cycle);

    static void CrosswalkTests(Report report, GameObject host, TrafficSignal signal)
    {
        var walkGo = new GameObject("TrafficQA_Walk") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            Crosswalk walk = walkGo.AddComponent<Crosswalk>();
            walk.signal = signal;
            walk.endA = new GameObject("A").transform;
            walk.endB = new GameObject("B").transform;
            walk.endA.SetParent(walkGo.transform);
            walk.endB.SetParent(walkGo.transform);
            walk.endA.position = new Vector3(-8f, 0f, 0f);
            walk.endB.position = new Vector3(8f, 0f, 0f);
            float redStart = signal.green + signal.yellow;
            Align(signal, 5f);
            Check(report, !walk.CanCross, "pedestrians wait while cars have green");
            Align(signal, redStart + 2f);
            Check(report, walk.CanCross, "pedestrians go early in red");
            Align(signal, redStart + .2f);
            Check(report, !walk.CanCross, "pedestrians wait the first moment of red");
            Align(signal, signal.Cycle - 1f);
            Check(report, !walk.CanCross, "pedestrians do not start with 1 s of red left");
            Check(report, walk.CrossSeconds > 12f && walk.CrossSeconds < signal.red, "a 16 m crossing needs about 13 s and fits in the red");
            walk.signal = null;
            Check(report, walk.CanCross, "a crossing without a signal is always open");
            // FindNear needs OnEnable (Play Mode) to register crossings, so it is not covered here (needs Play Mode).
        }
        finally { UnityEngine.Object.DestroyImmediate(walkGo); }
    }

    // ---- speed camera ------------------------------------------------------------------------------------------------

    static void CameraTests(Report report)
    {
        var go = new GameObject("TrafficQA_Camera") { hideFlags = HideFlags.HideAndDontSave };
        Action<float, float, long, bool> listener = null;
        try
        {
            SpeedCamera camera = go.AddComponent<SpeedCamera>();
            Check(report, camera.limitKmh == 50f, "camera limit defaults to 50 km/h");
            Check(report, camera.FineFor(55f) == 0 && camera.FineFor(56f) == 30000 + 6 * 2000, "fine = base + over-limit km/h * rate, none within tolerance");
            long paid = 0;
            var events = new List<bool>();
            listener = (kmh, limit, fine, ok) => events.Add(ok);
            SpeedCamera.Fined += listener;
            camera.payFine = fine => { paid += fine; return true; };
            Check(report, camera.Process(70f, 0f, out long charged, out bool wasPaid) && charged == 70000 && wasPaid && paid == 70000, "70 km/h is charged 70000 to the bank");
            Check(report, !camera.Process(80f, 5f, out _, out _), "no second fine inside the 10 s cooldown");
            Check(report, camera.Process(80f, 11f, out _, out _), "fined again after the cooldown");
            camera.payFine = _ => false;
            long before = PenaltyPoints(report);
            bool processed = camera.Process(90f, 30f, out _, out bool unpaid);
            Check(report, processed && !unpaid, "an unaffordable fine is reported as unpaid");
            long after = PenaltyPoints(report);
            if (before >= 0)
            {
                Check(report, after == before + 1, "an unaffordable fine adds one penalty point");
                LibraryCatalog.GetAccount(GameSession.LocalPlayerId).penaltyPoints = (int)before;
            }
            Check(report, events.Count == 3, "Fined event fires once per fine");
            string source = File.ReadAllText("Assets/Scripts/Gameplay/Traffic/SpeedCamera.cs");
            Check(report, !source.Contains("CashService") && !source.Contains("InventoryState"), "the camera never touches wallet cash");
        }
        finally
        {
            if (listener != null) SpeedCamera.Fined -= listener;
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static long PenaltyPoints(Report report)
    {
        try { return LibraryCatalog.GetAccount(GameSession.LocalPlayerId).penaltyPoints; }
        catch (Exception exception)
        {
            report.notes.Add("SKIP penalty-point check (LibraryCatalog unavailable in edit mode): " + exception.Message);
            return -1;
        }
    }

    // ---- the built map ---------------------------------------------------------------------------------------------------

    static void SceneTests(Report report)
    {
        Scene scene = EditorSceneManager.OpenScene(TrafficMapBuilder.ScenePath, OpenSceneMode.Single);
        GameObject root = Array.Find(scene.GetRootGameObjects(), g => g.name == TrafficMapBuilder.RootName);
        if (!root) { report.notes.Add("SKIP scene checks: Traffic root not found, run CompanyGame/Traffic/Build BusinessDistrict first."); return; }
        var surface = new RoadSurface();
        List<RoadCenterline> roads = TrafficMapBuilder.SampleRoads(scene, surface);
        TrafficLane[] lanes = root.GetComponentsInChildren<TrafficLane>(true);
        Check(report, lanes.Length > 0, "map has lanes");
        TrafficSpawner spawner = root.GetComponent<TrafficSpawner>();
        Check(report, spawner && spawner.lanes != null && spawner.lanes.Length == lanes.Length && root.GetComponent<TrafficSync>(), "spawner and sync are set up with every lane");
        CheckLanes(report, lanes, surface);
        CheckSignals(report, lanes);
        CheckSignalHeads(report, root, surface);
        CheckCrossings(report, root, surface, PaintedCrosswalks.Detect(scene), lanes);
        CheckDrawnCrosswalks(report, root, surface, roads, PaintedCrosswalks.DetectAll(scene));
        CheckCameras(report, root, surface);
        int curved = 0;
        foreach (TrafficLane lane in lanes) if (lane.name.Contains("Perimeter") && lane.Count >= 3) curved++;
        Check(report, curved > 0, "curved perimeter road has lanes with 3+ points");
        report.notes.Add($"map: {roads.Count} road pieces, {surface.TriangleCount} road triangles, {lanes.Length} lanes, curved lanes {curved}");
        int cars = Resources.LoadAll<ItemData>("Inventory/Vehicles/Cars").Length;
        if (cars == 0) report.notes.Add("NOTE: no car ItemData under Resources/Inventory/Vehicles/Cars yet, so no NPC cars would spawn.");
    }

    static void CheckLanes(Report report, TrafficLane[] lanes, RoadSurface surface)
    {
        int bad = 0, deadEnds = 0;
        foreach (TrafficLane lane in lanes)
        {
            Check(report, lane.Count >= (lane.connector ? 1 : 2) && Array.TrueForAll(lane.points, p => p), lane.name + " has its points");
            Check(report, lane.next != null && Array.TrueForAll(lane.next, n => n), lane.name + " has no broken next link");
            if (lane.next.Length == 0 && !lane.connector) deadEnds++;
            foreach (Transform point in lane.points)
                if (point && !surface.Contains(point.position, .1f, 1f) && bad++ < 20) report.failures.Add($"{lane.name}/{point.name} at {point.position} is not on a road mesh");
            foreach (TrafficLane next in lane.next)
            {
                if (!next || next.Count == 0 || lane.Count == 0) continue;
                Vector3 a = lane.Point(lane.Count - 1), b = next.Point(0);
                Check(report, Vector3.Distance(a, b) < 40f, $"{lane.name} -> {next.name} gap is under 40 m");
                if (!surface.Contains((a + b) * .5f, .5f, 1f) && bad++ < 20) report.failures.Add($"{lane.name} -> {next.name} crosses off the road");
            }
        }
        report.checks++;
        if (bad > 0) report.failures.Add($"{bad} lane points or links are off the road mesh");
        report.notes.Add($"lanes without a continuation (road ends): {deadEnds}");
    }

    // Every lane that ends next to lanes from 3+ directions must have a signal; crossing signals must never both be open.
    static void CheckSignals(Report report, TrafficLane[] lanes)
    {
        var approach = new List<TrafficLane>();
        foreach (TrafficLane lane in lanes) if (!lane.connector && lane.Count >= 2) approach.Add(lane);
        foreach (TrafficLane lane in approach)
        {
            if (lane.signal) { Check(report, lane.signal.heads.Length > 0 && lane.signal.Cycle > 0f, lane.name + " signal has lamp heads"); }
            if (lane.next.Length == 0) continue;
            var directions = new List<Vector3>();
            foreach (TrafficLane other in approach)
            {
                if (other.next.Length == 0 || (other.Point(other.Count - 1) - lane.Point(lane.Count - 1)).magnitude > 30f) continue;
                Vector3 d = Heading(other);
                if (!directions.Exists(x => Vector3.Angle(x, d) < 45f)) directions.Add(d);
            }
            Check(report, directions.Count < 3 || lane.signal, lane.name + " ends at a 3+ way junction and has a signal");
            if (!lane.signal) continue;
            foreach (TrafficLane other in approach)
                if (other.signal && other.signal != lane.signal && (other.signal.transform.position - lane.signal.transform.position).magnitude < 1f
                    && Vector3.Angle(Heading(other), Heading(lane)) > 60f && Vector3.Angle(Heading(other), Heading(lane)) < 120f)
                    Check(report, NeverBothOpen(lane.signal, other.signal), $"{lane.name} and {other.name} are never open together");
        }
    }

    static Color LampColor(Renderer r)
    {
        var b = new MaterialPropertyBlock();
        r.GetPropertyBlock(b);
        return b.GetColor("_EmissionColor");
    }

    static void CheckSignalHeads(Report report, GameObject root, RoadSurface surface)
    {
        foreach (TrafficSignal signal in root.GetComponentsInChildren<TrafficSignal>(true))
        {
            Check(report, signal.heads.Length > 0, signal.name + " has car heads");
            foreach (TrafficSignal.Head head in signal.heads)
            {
                if (head == null || !head.yellow) { Check(report, false, signal.name + " has an empty head"); continue; }
                Vector3 p = head.yellow.transform.position;
                Check(report, p.y > 4.5f && surface.Contains(p, .1f, 12f), signal.name + " head hangs over the road (" + p + ")");
                signal.ApplyLamps(TrafficSignal.Phase.Red);
                Color red = LampColor(head.red), yellowOff = LampColor(head.yellow);
                signal.ApplyLamps(TrafficSignal.Phase.Green);
                Color greenOn = LampColor(head.green), redOff = LampColor(head.red);
                signal.ApplyLamps(TrafficSignal.Phase.Yellow);
                Color yellowOn = LampColor(head.yellow);
                Check(report, red.maxColorComponent > 1f && yellowOff.maxColorComponent < .01f && greenOn.maxColorComponent > 1f
                    && redOff.maxColorComponent < .01f && yellowOn.maxColorComponent > 1f, signal.name + " lamp emission follows the phase");
            }
        }
    }

    static void CheckPedHeads(Report report, Crosswalk walk)
    {
        PedestrianSignal[] heads = walk.GetComponentsInChildren<PedestrianSignal>(true);
        Check(report, heads.Length == 2, walk.name + " has 2 pedestrian heads (" + heads.Length + ")");
        float saved = walk.signal.offset;
        var seen = new HashSet<int>();
        bool mismatch = false;
        for (float t = 0; t < walk.signal.Cycle; t += .25f)
        {
            Align(walk.signal, t);
            int state = PedestrianSignal.StateOf(walk);
            seen.Add(state);
            if ((state == 1) != walk.CanCross) { mismatch = true; }
        }
        Check(report, !mismatch, walk.name + " steady walk signal == Crosswalk.CanCross");
        walk.signal.offset = saved;
        Check(report, seen.Contains(0) && seen.Contains(1) && seen.Contains(2), walk.name + " pedestrian signal shows stop, walk and blink");
        foreach (PedestrianSignal head in heads) Check(report, head.walk == walk && head.stand && head.go, walk.name + " pedestrian head is wired");
    }

    static Vector3 Heading(TrafficLane lane) => (lane.Point(lane.Count - 1) - lane.Point(lane.Count - 2)).normalized;

    static bool NeverBothOpen(TrafficSignal a, TrafficSignal b)
    {
        for (double t = 0; t < a.Cycle * 2; t += .25)
            if (a.PhaseAt(t, out _, out _) != TrafficSignal.Phase.Red && b.PhaseAt(t, out _, out _) != TrafficSignal.Phase.Red) return false;
        return true;
    }

    static void CheckCrossings(Report report, GameObject root, RoadSurface surface, List<PaintedCrosswalk> painted, TrafficLane[] lanes)
    {
        Check(report, painted.Count > 0, "painted crosswalks found in StreetMarkings (" + painted.Count + ")");
        CheckStopLines(report, painted, lanes);
        Crosswalk[] walks = root.GetComponentsInChildren<Crosswalk>(true);
        Check(report, walks.Length > 0, "signalled junctions have crossings");
        foreach (Crosswalk walk in walks)
        {
            Check(report, walk.endA && walk.endB && walk.signal, walk.name + " has both ends and a signal");
            Check(report, !HasStripes(walk), walk.name + " draws no stripes of its own (the map paints the zebra)");
            if (!walk.transform.Find("Bars")) Check(report, painted.Exists(p => p.Contains(walk.transform.position, .3f)), walk.name + " centre lies within a painted crosswalk");
            if (walk.signal) CheckPedHeads(report, walk);
            if (!walk.endA || !walk.endB) continue;
            Vector3 middle = (walk.endA.position + walk.endB.position) * .5f;
            Check(report, surface.Contains(middle, .1f, 1f), walk.name + " crosses the road");
            Check(report, !surface.Contains(walk.endA.position, 0f, 1f) && !surface.Contains(walk.endB.position, 0f, 1f), walk.name + " ends on the sidewalk, outside the road");
            Check(report, surface.Contains(walk.endA.position, 2.5f, 1f) && surface.Contains(walk.endB.position, 2.5f, 1f), walk.name + " ends are within 2.5 m of the road edge");
        }
    }

    // Crossings drawn for the arms the map left unpainted: they must look like the painted ones, and every signalled arm has one.
    static void CheckDrawnCrosswalks(Report report, GameObject root, RoadSurface surface, List<RoadCenterline> roads, PaintedSet set)
    {
        var painted = set.crosswalks;
        Crosswalk[] drawn = Array.FindAll(root.GetComponentsInChildren<Crosswalk>(true), w => w.transform.Find("Bars"));
        Check(report, drawn.Length > 0, "drawn crosswalks exist (" + drawn.Length + ")");
        report.notes.Add($"drawn crosswalks: {drawn.Length}, painted in map: {painted.Count}");
        float pitch = painted.Count == 0 ? 1.4f : painted.OrderBy(p => p.pitch).ElementAt(painted.Count / 2).pitch;
        var lines = new List<RoadPolyline>();
        foreach (RoadCenterline road in roads) lines.Add(new RoadPolyline(road.points));
        foreach (Crosswalk walk in drawn)
        {
            Transform bars = walk.transform.Find("Bars");
            Check(report, bars.childCount >= 4, walk.name + " has at least 4 bars");
            Vector3 walkDir = RoadPolyline.Flat(walk.endB.position - walk.endA.position).normalized;
            float worst = 0f, worstHeight = 0f;
            foreach (Transform bar in bars)
            {
                Vector3 axis = RoadPolyline.Flat(bar.forward).normalized;
                worst = Mathf.Max(worst, Mathf.Abs(Vector3.Dot(axis, walkDir)));       // bars lie across the walking direction: dot is 0
                var filter = bar.GetComponent<MeshFilter>(); var renderer = bar.GetComponent<MeshRenderer>();
                Check(report, filter && filter.sharedMesh && renderer && renderer.sharedMaterial && !bar.GetComponent<Collider>(), walk.name + "/" + bar.name + " is a render-only bar with a mesh and material");
                if (filter && filter.sharedMesh)
                    Check(report, Mathf.Abs(filter.sharedMesh.bounds.size.x - set.barMesh.bounds.size.x) < .02f && Mathf.Abs(filter.sharedMesh.bounds.size.z - set.barMesh.bounds.size.z) < .02f, walk.name + "/" + bar.name + " has the painted bar's size");
                if (renderer && set.barMaterial) Check(report, renderer.sharedMaterial == set.barMaterial, walk.name + "/" + bar.name + " uses the painted bar material");
                float roadY = RoadY(lines, bar.position);
                worstHeight = Mathf.Max(worstHeight, Mathf.Abs(bar.position.y - roadY));
            }
            Check(report, worst < .05f, walk.name + " bars run parallel to the road, like the painted ones (dot " + worst.ToString("F3") + ")");
            Check(report, worstHeight > .004f && worstHeight < .15f, walk.name + " bars sit just above the road, not in it (" + worstHeight.ToString("F3") + " m)");
            if (walk.signal) CheckPedHeads(report, walk);
            Check(report, bars.childCount > 1 && Mathf.Abs(Vector3.Distance(bars.GetChild(0).position, bars.GetChild(1).position) - pitch) < .05f, walk.name + " bar spacing equals the painted pitch");
        }
        RoadGraph graph = RoadGraph.Build(roads);
        foreach (RoadNode node in graph.nodes)
            if (node.Signalled)
                foreach (RoadArm arm in node.arms)
                {
                    string name = $"Crosswalk_n{node.id}_r{arm.road}{(arm.forward ? "f" : "b")}";
                    Check(report, Array.Exists(root.GetComponentsInChildren<Crosswalk>(true), w => w.name == name), "signalled arm has a crosswalk: " + name);
                }
    }

    static float RoadY(List<RoadPolyline> lines, Vector3 p)
    {
        float best = float.MaxValue, y = 0f;
        foreach (RoadPolyline line in lines)
        {
            float s = line.Project(p, out float distance);
            if (distance < best) { best = distance; y = line.PointAt(s).y; }
        }
        return y;
    }

    static bool HasStripes(Crosswalk walk)
    {
        foreach (Transform child in walk.transform) if (child.name.StartsWith("Stripe")) return true;
        return false;
    }

    // Stop lines (end of a signalled approach lane) must not sit on a painted zebra.
    static void CheckStopLines(Report report, List<PaintedCrosswalk> painted, TrafficLane[] lanes)
    {
        foreach (TrafficLane lane in lanes)
        {
            if (!lane.signal || lane.points == null || lane.points.Length == 0) continue;
            Vector3 stop = lane.points[lane.points.Length - 1].position;
            Check(report, !painted.Exists(p => p.Contains(stop, .3f)), lane.name + " stops before the painted crosswalk, not on it");
        }
    }

    static void CheckCameras(Report report, GameObject root, RoadSurface surface)
    {
        SpeedCamera[] cameras = root.GetComponentsInChildren<SpeedCamera>(true);
        Check(report, cameras.Length >= 1, "map has a speed camera");
        foreach (SpeedCamera camera in cameras)
        {
            Check(report, camera.zone && camera.zone.isTrigger && camera.limitKmh == 50f, camera.name + " has a trigger zone and the 50 km/h limit");
            if (camera.zone) Check(report, surface.Contains(camera.transform.position, .1f, 1f), camera.name + " watches the road");
        }
    }
}
