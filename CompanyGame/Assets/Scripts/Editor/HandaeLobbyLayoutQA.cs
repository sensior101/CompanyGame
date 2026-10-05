using System;
using System.Collections.Generic;
using System.Diagnostics;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Read-only final-state report for reference-plan routes, with temporary gate poses.</summary>
public static class HandaeLobbyLayoutQA
{
    const float Radius = .35f;
    const float Bottom = .08f;
    const float Height = 1.8f;
    const float CellSize = .25f;
    const float Scale = HandaeLobbyBuilder.PlanScale;
    const float LandingExtension = HandaeLobbyBuilder.StaffLandingExtension;
    const float MinX = (75f - 730f) * Scale;
    const float MaxX = (1385f - 730f) * Scale;
    const float MinZ = (480f - 885f) * Scale;
    const float MaxZ = (480f - 30f) * Scale + LandingExtension;

    [Serializable]
    sealed class Check
    {
        public string name;
        public bool pass;
        public string detail;
    }

    [Serializable]
    sealed class Report
    {
        public string scene;
        public bool pass;
        public int passed;
        public int failed;
        public int sampledCells;
        public int closedReachableCells;
        public int openReachableCells;
        public long milliseconds;
        public string error;
        public List<Check> checks = new List<Check>();
    }

    sealed class GatePose
    {
        public EmployeeGate gate;
        public Vector3 left;
        public Vector3 right;
        public bool blockerEnabled;

        public GatePose(EmployeeGate value)
        {
            gate = value;
            if (value.leftWing) left = value.leftWing.localPosition;
            if (value.rightWing) right = value.rightWing.localPosition;
            if (value.passageBlocker) blockerEnabled = value.passageBlocker.enabled;
        }

        public void Apply(bool open)
        {
            float travel = open ? Mathf.Max(0f, gate.wingSlideDistance) : 0f;
            if (gate.leftWing) gate.leftWing.localPosition = left + Vector3.left * travel;
            if (gate.rightWing) gate.rightWing.localPosition = right + Vector3.right * travel;
            if (gate.passageBlocker) gate.passageBlocker.enabled = !open;
        }

        public void Restore()
        {
            if (!gate) return;
            if (gate.leftWing) gate.leftWing.localPosition = left;
            if (gate.rightWing) gate.rightWing.localPosition = right;
            if (gate.passageBlocker) gate.passageBlocker.enabled = blockerEnabled;
        }
    }

    /// <summary>Run only in Edit Mode with the lobby active. Returns a self-contained JSON report.</summary>
    public static string Run()
    {
        var report = new Report { scene = SceneManager.GetActiveScene().path };
        var clock = Stopwatch.StartNew();
        var poses = new List<GatePose>();
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Layout QA requires Edit Mode.");
            if (report.scene != HandaeLobbyBuilder.ScenePath)
                throw new InvalidOperationException("Open HandaeHQLobby before running layout QA.");

            var scene = SceneManager.GetActiveScene();
            foreach (var gate in UnityEngine.Object.FindObjectsByType<EmployeeGate>(FindObjectsSortMode.None))
                if (gate.gameObject.scene == scene) poses.Add(new GatePose(gate));
            Add(report, "Three employee gate lanes", poses.Count == 3, "Found " + poses.Count + " lanes.");
            foreach (var pose in poses)
                Add(report, pose.gate.name + " blocking setup", pose.gate.leftWing && pose.gate.rightWing &&
                    pose.gate.passageBlocker && !pose.gate.passageBlocker.isTrigger,
                    "Both wings and a solid passage blocker are required.");

            foreach (var pose in poses) pose.Apply(false);
            Physics.SyncTransforms();
            var start = P(730, 810);
            Add(report, "Entry capsule clearance", Clear(start), "Reference point (730, 810).");
            var closed = new WalkGrid(start);
            report.sampledCells = closed.SampleCount;
            report.closedReachableCells = closed.ReachableCount;

            Route(report, closed, "Entry to shared restroom entrance", 210, 460, true);
            Route(report, closed, "Entry to women's restroom aisle", 190, 250, true);
            Route(report, closed, "Entry to men's restroom aisle", 190, 700, true);
            var reception = FindSceneTransform(scene, "U-shaped reception marble body");
            var receptionRenderer = reception ? reception.GetComponent<Renderer>() : null;
            if (receptionRenderer)
            {
                Bounds bounds = receptionRenderer.bounds;
                Route(report, closed, "Entry to reception front",
                    new Vector3(bounds.center.x, 0f, bounds.min.z - .8f), true);
            }
            else Add(report, "Entry to reception front", false, "Reception body renderer missing.");
            Route(report, closed, "Entry to lounge approach", 990, 700, true);
            Route(report, closed, "Closed gates block staff area", 1164, 320, false);
            Route(report, closed, "Closed gates block elevator approach", 1100, 240, false);
            Route(report, closed, "Closed gates block stairs popup", 1290, 330, false);
            Add(report, "Elevator west narrow gap filled", !Clear(P(990, 180)),
                "Standing capsule must be blocked at former west shaft gap (990, 180).");
            Add(report, "Elevator east narrow gap filled", !Clear(P(1190, 180)),
                "Standing capsule must be blocked at former east shaft gap (1190, 180).");
            Add(report, "Stair flight remains physically blocked", !Clear(P(1291, 298)),
                "Standing capsule must not enter the physical staircase.");

            CheckModels(report, closed, scene, "(armchair.fbx)", "armchair_game.fbx", 4, false);
            CheckModels(report, closed, scene, "(sofa_company2.fbx)", "sofa_company2_game.fbx", 1, false);
            CheckModels(report, closed, scene, "(toilet.fbx)", "toilet_game.fbx", 7, true);
            CheckModels(report, closed, scene, "(sink.fbx)", "sink_game.fbx", 7, true);

            var stairBarrier = FindSceneTransform(scene, "Transparent stair access barrier");
            var barrierCollider = stairBarrier ? stairBarrier.GetComponent<Collider>() : null;
            float gateRearZ = float.NegativeInfinity;
            foreach (var pose in poses)
                foreach (var collider in pose.gate.GetComponentsInChildren<Collider>())
                    if (collider.enabled && !collider.isTrigger)
                        gateRearZ = Mathf.Max(gateRearZ, collider.bounds.max.z);
            float landingDepth = barrierCollider ? barrierCollider.bounds.min.z - gateRearZ : 0f;
            Add(report, "Generous landing between gate and staircase",
                barrierCollider && !float.IsInfinity(gateRearZ) && landingDepth >= 4.5f,
                "Measured solid gate rear to stair barrier: " + landingDepth.ToString("F2") + " m; minimum 4.50 m.");

            foreach (var pose in poses) pose.Apply(true);
            Physics.SyncTransforms();
            var opened = new WalkGrid(start);
            report.openReachableCells = opened.ReachableCount;
            Route(report, opened, "Open gates allow staff area", 1164, 320, true);
            Route(report, opened, "Open gates allow elevator approach", 1100, 240, true);
            Route(report, opened, "Open gates allow stairs popup", 1290, 330, true);
        }
        catch (Exception exception)
        {
            report.error = exception.GetType().Name + ": " + exception.Message;
            Add(report, "QA execution", false, report.error);
        }
        finally
        {
            foreach (var pose in poses) pose.Restore();
            Physics.SyncTransforms();
            clock.Stop();
            report.milliseconds = clock.ElapsedMilliseconds;
        }
        foreach (var check in report.checks)
        {
            if (check.pass) report.passed++;
            else report.failed++;
        }
        report.pass = report.failed == 0 && report.error == null;
        return JsonUtility.ToJson(report, true);
    }

    static void Add(Report report, string name, bool pass, string detail)
        => report.checks.Add(new Check { name = name, pass = pass, detail = detail });

    static void Route(Report report, WalkGrid grid, string name, float px, float py, bool expectedReachable)
        => Route(report, grid, name, P(px, py), expectedReachable);

    static void Route(Report report, WalkGrid grid, string name, Vector3 target, bool expectedReachable)
    {
        bool clear = Clear(target);
        bool reachable = clear && grid.Reaches(target);
        // A blocked target alone is not proof that a security boundary works: all named
        // destinations must themselves accommodate the walking capsule in either state.
        Add(report, name, clear && reachable == expectedReachable,
            "World point " + target.ToString("F2") + ": capsuleClear=" + clear +
            ", reachable=" + reachable + ", expected=" + expectedReachable + ".");
    }

    static Transform FindSceneTransform(Scene scene, string name)
    {
        foreach (var value in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (value.gameObject.scene == scene && value.name == name) return value;
        return null;
    }

    static void CheckModels(Report report, WalkGrid grid, Scene scene, string suffix,
        string modelFile, int expectedCount, bool checkAisle)
    {
        var wrappers = new List<Transform>();
        foreach (var value in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (value.gameObject.scene == scene && value.name.EndsWith(suffix, StringComparison.Ordinal))
                wrappers.Add(value);
        Add(report, suffix + " replacement count", wrappers.Count == expectedCount,
            "Found " + wrappers.Count + "; expected " + expectedCount + ".");
        foreach (var wrapper in wrappers)
        {
            bool hasModel = false;
            foreach (var filter in wrapper.GetComponentsInChildren<MeshFilter>(true))
            {
                string path = filter.sharedMesh ? AssetDatabase.GetAssetPath(filter.sharedMesh) : "";
                if (filter.transform != wrapper && path.EndsWith("/" + modelFile, StringComparison.OrdinalIgnoreCase))
                    hasModel = true;
            }
            bool hasSolidCollider = false;
            foreach (var collider in wrapper.GetComponentsInChildren<Collider>(true))
                if (collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy)
                    hasSolidCollider = true;
            Add(report, wrapper.name + " imported model and collision", hasModel && hasSolidCollider,
                "FBX mesh descendant=" + hasModel + ", enabled solid collider=" + hasSolidCollider + ".");
            if (!checkAisle) continue;

            var renderers = wrapper.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Add(report, wrapper.name + " aisle approach", false, "No visible model bounds.");
                continue;
            }
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            Vector3 front = wrapper.forward;
            front.y = 0f;
            front.Normalize();
            float extent = Mathf.Abs(front.x) * bounds.extents.x + Mathf.Abs(front.z) * bounds.extents.z;
            Vector3 approach = bounds.center + front * (extent + Radius + .15f);
            approach.y = 0f;
            Vector3 aisle = new Vector3(P(190, 480).x, 0f, approach.z);
            bool clear = Clear(approach) && Clear(aisle);
            bool reachable = clear && grid.Reaches(approach) && grid.Reaches(aisle) && CanTravel(aisle, approach);
            Add(report, wrapper.name + " aisle approach", reachable,
                "Front=" + approach.ToString("F2") + ", aisle=" + aisle.ToString("F2") +
                ", capsuleClear=" + clear + ", connected=" + reachable + ".");
        }
    }

    static Vector3 P(float px, float py) => HandaeLobbyBuilder.PlanPoint(px, py);

    static void Capsule(Vector3 position, out Vector3 lower, out Vector3 upper)
    {
        lower = position + Vector3.up * (Bottom + Radius);
        upper = position + Vector3.up * (Bottom + Height - Radius);
    }

    static bool Clear(Vector3 position)
    {
        Capsule(position, out var lower, out var upper);
        return !Physics.CheckCapsule(lower, upper, Radius, ~0, QueryTriggerInteraction.Ignore);
    }

    static bool CanTravel(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < .0001f) return true;
        Capsule(from, out var lower, out var upper);
        return !Physics.CapsuleCast(lower, upper, Radius, delta / distance, distance,
            ~0, QueryTriggerInteraction.Ignore);
    }

    static bool InFootprint(Vector3 position)
    {
        float px = position.x / Scale + 730f;
        float lowerJoinZ = (480f - 385f) * Scale;
        float upperJoinZ = (480f - 330f) * Scale + LandingExtension;
        float py;
        if (position.z <= lowerJoinZ) py = 480f - position.z / Scale;
        else if (position.z >= upperJoinZ) py = 480f - (position.z - LandingExtension) / Scale;
        else py = (480f * Scale + 385f * LandingExtension / 55f - position.z) /
            (Scale + LandingExtension / 55f);
        // Only the pictured staircase extends north of the main wall line.
        return px >= 75f && px <= 1385f && py <= 885f &&
            (py >= 75f || (py >= 30f && px >= 1228f && px <= 1360f));
    }

    sealed class WalkGrid
    {
        readonly int width = Mathf.FloorToInt((MaxX - MinX) / CellSize) + 1;
        readonly int depth = Mathf.FloorToInt((MaxZ - MinZ) / CellSize) + 1;
        readonly bool[] clear;
        readonly bool[] reached;
        public int SampleCount { get; private set; }
        public int ReachableCount { get; private set; }

        public WalkGrid(Vector3 start)
        {
            clear = new bool[width * depth];
            reached = new bool[clear.Length];
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++)
                {
                    Vector3 point = Point(x, z);
                    if (!InFootprint(point)) continue;
                    SampleCount++;
                    clear[z * width + x] = Clear(point);
                }
            if (!Clear(start)) return;
            var queue = new Queue<int>();
            ForNearby(start, (index, point) =>
            {
                if (!clear[index] || !CanTravel(start, point)) return false;
                reached[index] = true;
                ReachableCount++;
                queue.Enqueue(index);
                return true;
            });
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % width;
                int z = index / width;
                Visit(x - 1, z, index, queue);
                Visit(x + 1, z, index, queue);
                Visit(x, z - 1, index, queue);
                Visit(x, z + 1, index, queue);
            }
        }

        public bool Reaches(Vector3 target)
            => ForNearby(target, (index, point) => reached[index] && CanTravel(point, target));

        void Visit(int x, int z, int from, Queue<int> queue)
        {
            if (x < 0 || z < 0 || x >= width || z >= depth) return;
            int index = z * width + x;
            if (reached[index] || !clear[index] || !CanTravel(Point(from % width, from / width), Point(x, z))) return;
            reached[index] = true;
            ReachableCount++;
            queue.Enqueue(index);
        }

        bool ForNearby(Vector3 target, Func<int, Vector3, bool> predicate)
        {
            int cx = Mathf.RoundToInt((target.x - MinX) / CellSize);
            int cz = Mathf.RoundToInt((target.z - MinZ) / CellSize);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx;
                    int z = cz + dz;
                    if (x < 0 || z < 0 || x >= width || z >= depth) continue;
                    int index = z * width + x;
                    if (clear[index] && predicate(index, Point(x, z))) return true;
                }
            return false;
        }

        Vector3 Point(int x, int z) => new Vector3(MinX + x * CellSize, 0f, MinZ + z * CellSize);
    }
}
