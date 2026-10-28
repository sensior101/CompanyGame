using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Builds BusinessDistrict traffic: lane points from the road meshes, signals with poles at every 3+ way
/// junction, a zebra crossing on each of their arms, two speed cameras and the spawner.
/// Re-running replaces only Traffic/Auto. Anything a person puts under Traffic/Manual is kept and is
/// picked up by the spawner like the generated lanes.
/// </summary>
public static class TrafficMapBuilder
{
    public const string ScenePath = "Assets/Scenes/Maps/BusinessDistrict.unity";
    public const string RootName = "Traffic";
    const string AutoName = "Auto", ManualName = "Manual";
    const string MaterialFolder = "Assets/Art/Traffic/Materials";
    static readonly string[] CameraRoads = { "Street_0", "Street_2" };
    const float CrosswalkInset = 3f;              // crosswalk centre is this much closer to the junction than the stop line

    [MenuItem("CompanyGame/Traffic/Build BusinessDistrict")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play Mode first.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string summary = BuildInto(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + ScenePath);
        Debug.Log("Traffic built: " + summary);
    }

    /// <summary>The road meshes of a scene as centre lines, the shared input of the builder and the QA.</summary>
    public static List<RoadCenterline> SampleRoads(Scene scene, RoadSurface surface = null)
    {
        var roads = new List<RoadCenterline>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(false))
            {
                if (!RoadSampler.IsRoadName(filter.name) || !filter.sharedMesh) continue;
                surface?.Add(filter.sharedMesh, filter.transform.localToWorldMatrix);
                roads.AddRange(RoadSampler.Sample(filter.sharedMesh, filter.transform.localToWorldMatrix, filter.name));
            }
        return roads;
    }

    public static string BuildInto(Scene scene)
    {
        var surface = new RoadSurface();
        List<RoadCenterline> roads = SampleRoads(scene, surface);
        if (roads.Count == 0) throw new InvalidOperationException("No road meshes (Street_#, PerimeterRoad*, BridgeAsphalt) found in " + scene.path);
        RoadGraph graph = RoadGraph.Build(roads);
        Transform auto = PrepareRoots(scene, out GameObject root);
        var made = new Made();
        PaintedSet paintedSet = PaintedCrosswalks.DetectAll(scene);
        List<PaintedCrosswalk> painted = paintedSet.crosswalks;
        Dictionary<RoadArm, PaintedCrosswalk> matches = MatchPainted(graph, painted, out float trimmed);
        Transform lanesRoot = Child(auto, "Lanes"), signalsRoot = Child(auto, "Signals");
        Dictionary<LaneSpec, TrafficLane> lanes = MakeLanes(graph, lanesRoot);
        made.signals = MakeSignals(graph, signalsRoot, lanes, out var signalOf);
        made.crosswalks = MakeCrosswalks(graph, Child(auto, "Crosswalks"), signalOf, surface, matches);
        var report = new List<string>();
        int drawn = DrawnCrosswalks.Draw(graph, surface, paintedSet, matches, Child(auto, "Crosswalks"), signalOf, report);
        made.crosswalks += drawn;
        foreach (string line in report) Debug.Log("[DrawnCrosswalks] " + line);
        made.cameras = MakeCameras(graph, Child(auto, "Cameras"));
        ConfigureSpawner(root);
        return $"{roads.Count} road pieces, {graph.nodes.Count} junctions, {lanes.Count} lanes, {made.signals} signals, {made.crosswalks} crosswalks, {made.cameras} speed cameras; painted crosswalks {painted.Count}, matched {matches.Count}, stop lines pulled back {trimmed:F1} m in total, drawn crosswalks {drawn}";
    }

    struct Made { public int signals, crosswalks, cameras; }

    // ---- scene roots -----------------------------------------------------------------------------------------

    static Transform PrepareRoots(Scene scene, out GameObject root)
    {
        root = null;
        foreach (GameObject candidate in scene.GetRootGameObjects())
            if (candidate.name == RootName) root = candidate;
        if (!root)
        {
            root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
        }
        Transform old = root.transform.Find(AutoName);
        if (old) Object.DestroyImmediate(old.gameObject);
        Child(root.transform, ManualName);
        return Child(root.transform, AutoName);
    }

    static Transform Child(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing) return existing;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void ConfigureSpawner(GameObject root)
    {
        TrafficSpawner spawner = root.GetComponent<TrafficSpawner>();
        if (!spawner) spawner = root.AddComponent<TrafficSpawner>();
        spawner.lanes = root.GetComponentsInChildren<TrafficLane>(true);
        EditorUtility.SetDirty(spawner);
    }

    // ---- lanes -------------------------------------------------------------------------------------------------

    static Dictionary<LaneSpec, TrafficLane> MakeLanes(RoadGraph graph, Transform parent)
    {
        var result = new Dictionary<LaneSpec, TrafficLane>();
        var all = new List<LaneSpec>(graph.lanes);
        foreach (LaneSpec spec in graph.lanes) all.AddRange(spec.next.FindAll(n => n.connector && !all.Contains(n)));
        foreach (LaneSpec spec in all) result[spec] = MakeLane(spec, parent);
        foreach (KeyValuePair<LaneSpec, TrafficLane> pair in result)
        {
            var next = new List<TrafficLane>();
            foreach (LaneSpec n in pair.Key.next) if (result.TryGetValue(n, out TrafficLane target)) next.Add(target);
            pair.Value.next = next.ToArray();
        }
        return result;
    }

    static TrafficLane MakeLane(LaneSpec spec, Transform parent)
    {
        var go = new GameObject(spec.name);
        go.transform.SetParent(parent, false);
        TrafficLane lane = go.AddComponent<TrafficLane>();
        lane.speedLimitKmh = spec.speedKmh;
        lane.connector = spec.connector;
        lane.points = new Transform[spec.points.Count];
        for (int i = 0; i < spec.points.Count; i++)
        {
            var point = new GameObject("P" + i).transform;
            point.SetParent(go.transform, false);
            point.position = spec.points[i];
            lane.points[i] = point;
        }
        return lane;
    }

    // ---- signals and poles ---------------------------------------------------------------------------------------

    static int MakeSignals(RoadGraph graph, Transform parent, Dictionary<LaneSpec, TrafficLane> lanes,
        out Dictionary<(RoadNode, int), TrafficSignal> signalOf)
    {
        signalOf = new Dictionary<(RoadNode, int), TrafficSignal>();
        int count = 0;
        foreach (RoadNode node in graph.nodes)
        {
            if (!node.Signalled) continue;
            for (int group = 0; group < 2; group++)
            {
                if (!node.arms.Exists(a => a.group == group)) continue;
                TrafficSignal signal = NewSignal(node, group, parent);
                signalOf[(node, group)] = signal;
                var heads = new List<TrafficSignal.Head>();
                foreach (RoadArm arm in node.arms)
                {
                    if (arm.group != group || arm.inLane == null) continue;
                    heads.Add(MakePole(signal.transform, arm));
                    lanes[arm.inLane].signal = signal;
                }
                signal.heads = heads.ToArray();
                count++;
            }
        }
        return count;
    }

    static TrafficSignal NewSignal(RoadNode node, int group, Transform parent)
    {
        var go = new GameObject($"Signal_n{node.id}_g{group}");
        go.transform.SetParent(parent, false);
        go.transform.position = node.position;
        TrafficSignal signal = go.AddComponent<TrafficSignal>();
        signal.offset = OffsetOf(node.id, group);
        return signal;
    }

    // The timings are TrafficSignal's (one cycle for all, 12.14); junctions are spread over the cycle, and the crossing
    // group of a junction runs half a cycle later (the pedestrians' turn comes between the two).
    static float OffsetOf(int node, int group) =>
        node * 7 % (int)TrafficSignal.CycleSeconds + (group == 1 ? TrafficSignal.CycleSeconds * .5f : 0f);

    /// <summary>
    /// Puts the current signal plan (TrafficSignal's cycle) on the signals of the built map without building it again: the
    /// offset of every Signal_n{node}_g{group}. Run with -executeMethod TrafficMapBuilder.ApplySignalPlan.
    /// </summary>
    [MenuItem("CompanyGame/Traffic/Apply signal plan to BusinessDistrict")]
    public static void ApplySignalPlan()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int count = 0;
        foreach (TrafficSignal signal in Object.FindObjectsByType<TrafficSignal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string[] parts = signal.name.Split('_'); // Signal, n<node>, g<group>
            if (parts.Length != 3 || !int.TryParse(parts[1].Substring(1), out int node) || !int.TryParse(parts[2].Substring(1), out int group))
                throw new InvalidOperationException("unexpected signal name " + signal.name);
            signal.offset = OffsetOf(node, group);
            EditorUtility.SetDirty(signal);
            count++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[TrafficMapBuilder] signal plan applied to " + count + " signals (cycle " + TrafficSignal.CycleSeconds + " s)");
    }

    // One mast per approach: a pole at the kerb, an arm reaching over the approach lane at 5.5 m and a horizontal
    // red-yellow-green housing on it facing the oncoming cars. Local +x points toward the road, +z toward the cars.
    static TrafficSignal.Head MakePole(Transform parent, RoadArm arm)
    {
        List<Vector3> pts = arm.inLane.points;
        Vector3 stop = pts[pts.Count - 1];
        Vector3 heading = RoadPolyline.Flat(stop - pts[pts.Count - 2]).normalized;
        Vector3 right = new Vector3(heading.z, 0f, -heading.x);
        float reach = Mathf.Max(2.5f, arm.width * .5f - RoadGraph.LaneOffset(arm.width) + .8f);   // lane centre to the kerb, plus a margin
        var rootGo = new GameObject($"Head_{arm.road}_{(arm.forward ? "f" : "b")}");
        rootGo.transform.SetParent(parent, false);
        rootGo.transform.SetPositionAndRotation(stop + right * reach + heading * 1.5f, Quaternion.LookRotation(-heading));
        Transform t = rootGo.transform;
        Material pole = Mat("Traffic_Pole", new Color(.35f, .36f, .38f)), housing = Mat("Traffic_Housing", new Color(.07f, .07f, .08f));
        Material lamp = LampMat();
        Prim(PrimitiveType.Cylinder, "Pole", t, new Vector3(0f, 3f, 0f), new Vector3(.22f, 3f, .22f), pole, true);
        Prim(PrimitiveType.Cube, "Arm", t, new Vector3(reach * .5f, 5.9f, 0f), new Vector3(reach, .14f, .14f), pole, false);
        Prim(PrimitiveType.Cube, "Housing", t, new Vector3(reach, 5.5f, 0f), new Vector3(1.75f, .6f, .32f), housing, false);
        Prim(PrimitiveType.Cube, "Backplate", t, new Vector3(reach, 5.5f, -.05f), new Vector3(1.95f, .8f, .06f), housing, false);
        Vector3 face = new Vector3(0f, 0f, .2f);
        // The driver faces -z, so red sits on local +x (the driver's left), green on -x.
        return new TrafficSignal.Head
        {
            red = Prim(PrimitiveType.Sphere, "Red", t, new Vector3(reach + .6f, 5.5f, 0f) + face, new Vector3(.38f, .38f, .2f), lamp, false),
            yellow = Prim(PrimitiveType.Sphere, "Yellow", t, new Vector3(reach, 5.5f, 0f) + face, new Vector3(.38f, .38f, .2f), lamp, false),
            green = Prim(PrimitiveType.Sphere, "Green", t, new Vector3(reach - .6f, 5.5f, 0f) + face, new Vector3(.38f, .38f, .2f), lamp, false),
        };
    }

    // A walk box on a post at one sidewalk end of a signalled crosswalk, facing the opposite end.
    static void MakePedHead(Crosswalk walk, Transform parent, string name, Vector3 at, Vector3 toward)
    {
        var rootGo = new GameObject(name);
        rootGo.transform.SetParent(parent, false);
        Vector3 look = RoadPolyline.Flat(toward - at).normalized;
        rootGo.transform.SetPositionAndRotation(at, Quaternion.LookRotation(look));
        Transform t = rootGo.transform;                   // +z = toward the people on the other side
        Material pole = Mat("Traffic_Pole", new Color(.35f, .36f, .38f)), housing = Mat("Traffic_Housing", new Color(.07f, .07f, .08f));
        Prim(PrimitiveType.Cylinder, "Post", t, new Vector3(0f, 1.1f, 0f), new Vector3(.12f, 1.1f, .12f), pole, true);
        Prim(PrimitiveType.Cube, "Box", t, new Vector3(0f, 2.55f, 0f), new Vector3(.5f, 1.0f, .24f), housing, false);
        var signal = rootGo.AddComponent<PedestrianSignal>();
        signal.walk = walk;
        signal.stand = Figure(t, "Stand", new Vector3(0f, 2.82f, .13f), PedMat("Traffic_PedStand", false));
        signal.go = Figure(t, "Walk", new Vector3(0f, 2.28f, .13f), PedMat("Traffic_PedWalk", true));
    }

    static Renderer Figure(Transform parent, string name, Vector3 localPos, Material material)
    {
        Renderer quad = Prim(PrimitiveType.Quad, name, parent, localPos, new Vector3(.4f, .44f, 1f), material, false);
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);     // a Quad faces -z; turn it toward +z
        return quad;
    }

    // ---- crossings -----------------------------------------------------------------------------------------------

    // Each signalled arm adopts the painted zebra that crosses it nearest its stop line (bars are parallel to the road,
    // so the walking direction must be square to the arm). Arms without a painted crossing get none: we draw no stripes.
    static Dictionary<RoadArm, PaintedCrosswalk> MatchPainted(RoadGraph graph, List<PaintedCrosswalk> painted, out float trimmed)
    {
        var pairs = new List<(float score, RoadArm arm, PaintedCrosswalk walk)>();
        foreach (RoadNode node in graph.nodes)
        {
            if (!node.Signalled) continue;
            foreach (RoadArm arm in node.arms)
                foreach (PaintedCrosswalk p in painted)
                    if (Fits(node, arm, p, out float distance)) pairs.Add((distance, arm, p));
        }
        pairs.Sort((x, y) => x.score.CompareTo(y.score));
        var result = new Dictionary<RoadArm, PaintedCrosswalk>();
        trimmed = 0f;
        foreach (var pair in pairs)
        {
            if (pair.walk.used || result.ContainsKey(pair.arm)) continue;
            pair.walk.used = true;
            result[pair.arm] = pair.walk;
            trimmed += FitStopLine(pair.arm, pair.walk);
        }
        return result;
    }

    static bool Fits(RoadNode node, RoadArm arm, PaintedCrosswalk p, out float distance)
    {
        Vector3 rel = RoadPolyline.Flat(p.centre - node.position);
        float along = Vector3.Dot(rel, arm.dir), side = Mathf.Abs(Vector3.Dot(rel, new Vector3(-arm.dir.z, 0f, arm.dir.x)));
        distance = Mathf.Abs(along - (node.clearance - CrosswalkInset));
        return Mathf.Abs(Vector3.Dot(p.walk, arm.dir)) < .5f && along > 3f && along < node.clearance + 10f && side < arm.width * .5f;
    }

    // Cars stop at the end of the approach lane; pull that end back so it is before the crossing's far edge.
    internal static float FitStopLine(RoadArm arm, PaintedCrosswalk p)
    {
        float far = Vector3.Dot(RoadPolyline.Flat(p.centre - arm.node.position), arm.dir) + p.depth * .5f + .6f;
        float trim = far - arm.node.clearance;
        if (trim <= 0f || arm.inLane == null) return 0f;
        List<Vector3> pts = arm.inLane.points;
        float left = trim;
        while (pts.Count > 2 && RoadPolyline.Flat(pts[pts.Count - 1] - pts[pts.Count - 2]).magnitude <= left + .3f)
        {
            left -= RoadPolyline.Flat(pts[pts.Count - 1] - pts[pts.Count - 2]).magnitude;
            pts.RemoveAt(pts.Count - 1);
        }
        Vector3 back = RoadPolyline.Flat(pts[pts.Count - 2] - pts[pts.Count - 1]);
        pts[pts.Count - 1] += back.normalized * Mathf.Min(left, back.magnitude * .8f);
        return trim;
    }

    static int MakeCrosswalks(RoadGraph graph, Transform parent, Dictionary<(RoadNode, int), TrafficSignal> signalOf, RoadSurface surface,
        Dictionary<RoadArm, PaintedCrosswalk> matches)
    {
        int count = 0;
        foreach (KeyValuePair<RoadArm, PaintedCrosswalk> pair in matches)
            if (MakeCrosswalk(graph, parent, pair.Key.node, pair.Key, pair.Value, signalOf[(pair.Key.node, pair.Key.group)], surface)) count++;
        return count;
    }

    // Placed on the painted zebra. Skipped when a sidewalk end would land on another road (a slanted junction).
    static bool MakeCrosswalk(RoadGraph graph, Transform parent, RoadNode node, RoadArm arm, PaintedCrosswalk painted, TrafficSignal signal, RoadSurface surface)
    {
        RoadPolyline line = graph.lines[arm.road];
        Vector3 centre = painted.centre, along = painted.along, across = painted.walk;
        centre.y = line.PointAt(line.Project(centre, out _)).y;
        // sidewalk ends sit 1 m outside the road edge on each side
        Vector3 a = centre - across * (EdgeDistance(surface, centre, -across) + 1f), b = centre + across * (EdgeDistance(surface, centre, across) + 1f);
        if (surface.Contains(a, 0f, 1f) || surface.Contains(b, 0f, 1f)) return false;
        NewCrosswalk(parent, node, arm, centre, along, across, a, b, signal);
        return true;
    }

    internal static Crosswalk NewCrosswalk(Transform parent, RoadNode node, RoadArm arm, Vector3 centre, Vector3 along, Vector3 across,
        Vector3 a, Vector3 b, TrafficSignal signal)
    {
        var go = new GameObject($"Crosswalk_n{node.id}_r{arm.road}{(arm.forward ? "f" : "b")}");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(across));
        Crosswalk walk = go.AddComponent<Crosswalk>();
        walk.signal = signal;
        walk.endA = Marker(go.transform, "EndA", a);
        walk.endB = Marker(go.transform, "EndB", b);
        if (signal)
        {
            MakePedHead(walk, go.transform, "PedSignal_A", a + along * 1.9f, b);
            MakePedHead(walk, go.transform, "PedSignal_B", b + along * 1.9f, a);
        }
        return walk;
    }

    // Distance from p along dir until the road surface ends (0.25 m steps, at most 20 m).
    internal static float EdgeDistance(RoadSurface surface, Vector3 p, Vector3 dir)
    {
        float d = 0f;
        while (d < 20f && surface.Contains(p + dir * (d + .25f), 0f, 1f)) d += .25f;
        return d;
    }

    static Transform Marker(Transform parent, string name, Vector3 world)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.position = world;
        return t;
    }

    // ---- speed cameras ---------------------------------------------------------------------------------------------

    static int MakeCameras(RoadGraph graph, Transform parent)
    {
        int count = 0;
        foreach (string roadName in CameraRoads)
        {
            int road = graph.roads.FindIndex(r => r.name == roadName);
            LaneSpec lane = LongestLane(graph, road);
            if (lane == null) continue;
            MakeCamera(graph, parent, road, lane, ++count);
        }
        return count;
    }

    static LaneSpec LongestLane(RoadGraph graph, int road)
    {
        LaneSpec best = null;
        float bestLength = 0f;
        foreach (LaneSpec lane in graph.lanes)
        {
            if (lane.road != road || lane.connector) continue;
            float length = new RoadPolyline(lane.points).Length;
            if (length > bestLength) { bestLength = length; best = lane; }
        }
        return best;
    }

    static void MakeCamera(RoadGraph graph, Transform parent, int road, LaneSpec lane, int number)
    {
        RoadPolyline line = graph.lines[road];
        var laneLine = new RoadPolyline(lane.points);
        Vector3 mid = laneLine.PointAt(laneLine.Length * .5f);
        float s = line.Project(mid, out _);
        Vector3 centre = line.PointAt(s), along = line.TangentAt(s);
        float width = graph.roads[road].width;
        var go = new GameObject("SpeedCamera_" + number);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(along));
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, 2f, 0f);
        box.size = new Vector3(width + 2f, 4f, 8f);
        SpeedCamera camera = go.AddComponent<SpeedCamera>();
        camera.zone = box;
        float side = width * .5f + 2.2f;
        Material steel = Mat("Traffic_Pole", new Color(.35f, .36f, .38f)), dark = Mat("Traffic_Housing", new Color(.07f, .07f, .08f));
        Prim(PrimitiveType.Cylinder, "Pole", go.transform, new Vector3(side, 3f, 0f), new Vector3(.2f, 3f, .2f), steel, true);
        Prim(PrimitiveType.Cube, "Head", go.transform, new Vector3(side - .6f, 6.1f, 0f), new Vector3(1.2f, .45f, .9f), dark, false);
        var flash = new GameObject("Flash").AddComponent<Light>();
        flash.transform.SetParent(go.transform, false);
        flash.transform.localPosition = new Vector3(side - 1.3f, 5.8f, 0f);
        flash.type = LightType.Point;
        flash.range = 14f;
        flash.intensity = 25f;
        flash.enabled = false;
        camera.flash = flash;
    }

    // ---- small helpers -------------------------------------------------------------------------------------------------

    static Renderer Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material material, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    // Unlit, so a lit lamp shows at full brightness in daylight (the Lit material's _EMISSION keyword was off in the saved asset, see NOTES).
    static Material LampMat()
    {
        string path = $"{MaterialFolder}/Traffic_Lamp.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (material && material.shader == unlit) return material;
        if (material) { material.shader = unlit; material.SetColor("_BaseColor", new Color(.06f, .06f, .06f)); EditorUtility.SetDirty(material); return material; }
        EnsureFolder("Assets/Art", "Traffic"); EnsureFolder("Assets/Art/Traffic", "Materials");
        material = new Material(unlit);
        material.SetColor("_BaseColor", new Color(.06f, .06f, .06f));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // Unlit white figure on black; the runtime tint (red / green / dark) colours the figure only.
    static Material PedMat(string name, bool walking)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", new Color(.03f, .03f, .03f));
        material.SetTexture("_BaseMap", FigureTexture(name, walking));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Texture2D FigureTexture(string name, bool walking)
    {
        const int w = 32, h = 36;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = Color.black;
        void Dot(float cx, float cy, float r)
        {
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) px[y * w + x] = Color.white;
        }
        void Line(float x0, float y0, float x1, float y1, float r)
        {
            for (int i = 0; i <= 30; i++) Dot(Mathf.Lerp(x0, x1, i / 30f), Mathf.Lerp(y0, y1, i / 30f), r);
        }
        Dot(16, 31, 3.2f);
        if (!walking)
        {
            Line(16, 27, 16, 15, 3.4f);
            Line(12, 25, 12, 14, 1.4f); Line(20, 25, 20, 14, 1.4f);
            Line(14, 14, 14, 3, 2f); Line(18, 14, 18, 3, 2f);
        }
        else
        {
            Line(16, 27, 17, 15, 3.4f);
            Line(13, 25, 8, 17, 1.4f); Line(19, 25, 24, 20, 1.4f);
            Line(16, 15, 9, 3, 2f); Line(17, 15, 24, 3, 2f);
        }
        tex.SetPixels(px);
        tex.Apply();
        EnsureFolder("Assets/Art", "Traffic");
        EnsureFolder("Assets/Art/Traffic", "Textures");
        string path = $"Assets/Art/Traffic/Textures/{name}.png";
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    static Material Mat(string name, Color color, bool emissive = false)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        EnsureFolder("Assets/Art", "Traffic");
        EnsureFolder("Assets/Art/Traffic", "Materials");
        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", .25f);
        if (emissive)
        {
            material.EnableKeyword("_EMISSION");     // TrafficSignal drives _EmissionColor per lamp at runtime
            material.SetColor("_EmissionColor", Color.black);
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
