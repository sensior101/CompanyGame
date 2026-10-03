using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.World.Maps;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.WorldMaps
{
    /// <summary>Checks the saved transport layout and exercises the runtime destination picker.</summary>
    [InitializeOnLoad]
    public static class TransitMapQA
    {
        const string Output = "../ArtSource/WorldDistricts/QA/";
        const string Session = "CompanyGame.TransitTour";
        static readonly string[] ScenePaths = new[] { TerracedVillageExpansion.ScenePath }
            .Concat(Enumerable.Range(0, CityDistrictBuilder.Names.Length).Select(CityDistrictBuilder.PathFor)).ToArray();

        [Serializable]
        sealed class Tour
        {
            public int leg;
            public int stage;
            public int frame;
            public int waitedFrames;
            public int sceneHandle;
            public double deadline;
            public bool finished;
            public bool success;
            public bool restore;
            public bool originalBackground;
            public bool cursorVisible;
            public int cursorLock;
            public string originalScene;
            public string originalStartScene;
            public string error = "";
            public List<string> checks = new List<string>();
        }

        static TransitMapQA() { EditorApplication.update += Tick; }

        static void Write(string name, object value)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + name + ".json", JsonConvert.SerializeObject(value, Formatting.Indented));
        }

        static T[] Components<T>(Scene scene, bool inactive = false) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(inactive)).ToArray();

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static bool Accessible(Vector3 point, float radius, out string reason)
        {
            if (!Physics.Raycast(point + Vector3.up * .35f, Vector3.down, out var hit, 1.1f, ~0, QueryTriggerInteraction.Ignore))
            {
                reason = "no supporting floor";
                return false;
            }
            if (Vector3.Angle(hit.normal, Vector3.up) > 45f)
            {
                reason = "floor exceeds the player slope limit";
                return false;
            }
            // Keep the capsule above the expected floor, including the controller skin.
            if (Physics.CheckCapsule(point + Vector3.up * (radius + .05f), point + Vector3.up * (1.8f - radius),
                radius, ~0, QueryTriggerInteraction.Ignore))
            {
                reason = "player capsule blocked";
                return false;
            }
            reason = "clear floor and standing room";
            return true;
        }

        static CharacterController Probe(PlayerMovement player)
        {
            var go = new GameObject("Temporary transit walking probe") { hideFlags = HideFlags.HideAndDontSave };
            var motor = go.AddComponent<CharacterController>();
            var source = player.GetComponent<CharacterController>();
            motor.radius = source ? source.radius : .35f;
            motor.height = source ? source.height : 1.8f;
            motor.center = source ? source.center : Vector3.up * .9f;
            motor.stepOffset = source ? source.stepOffset : .23f;
            motor.slopeLimit = source ? source.slopeLimit : 45f;
            motor.skinWidth = source ? source.skinWidth : .02f;
            motor.minMoveDistance = 0;
            motor.enabled = false;
            return motor;
        }

        static bool Walk(CharacterController motor, Vector3 from, Vector3 to, float speed, out Vector3 finish, bool followsDescendingFlight = false)
        {
            motor.enabled = false;
            motor.transform.position = from + Vector3.up * .08f;
            motor.enabled = true;
            Physics.SyncTransforms();
            for (int settle = 0; settle < 15; settle++) motor.Move(Vector3.down * .02f);
            float vertical = -2f;
            int stalled = 0;
            float step = Mathf.Max(.01f, speed) / 60f;
            int frames = Mathf.CeilToInt(FlatDistance(from, to) / step) * 3 + 90;
            for (int frame = 0; frame < frames; frame++)
            {
                Vector3 remaining = to - motor.transform.position;
                remaining.y = 0;
                if (remaining.magnitude < .075f) break;
                if (motor.isGrounded && vertical < 0) vertical = -2f;
                vertical += Physics.gravity.y / 60f;
                Vector3 previous = motor.transform.position;
                motor.Move(Vector3.ClampMagnitude(remaining, step) + Vector3.up * (vertical / 60f));
                stalled = FlatDistance(previous, motor.transform.position) < .001f ? stalled + 1 : 0;
                // A stair descent is expected to travel below the initial pavement.
                // Bound the fall by its lower landing, never by the entrance height.
                float fallFloor = followsDescendingFlight ? Mathf.Min(from.y, to.y) - .65f : Mathf.Min(from.y, to.y) - 1f;
                if (stalled > 30 || motor.transform.position.y < fallFloor) break;
            }
            finish = motor.transform.position;
            bool passed = FlatDistance(finish, to) <= .16f && Mathf.Abs(finish.y - to.y) <= .35f;
            motor.enabled = false;
            return passed;
        }

        sealed class SurfaceTriangle
        {
            public string name;
            public Vector3 a, b, c;
            public float minX, maxX, minZ, maxZ;
        }

        /// <summary>
        /// Read only visible mesh triangles close to the entrance. This deliberately
        /// includes decorative road meshes with no Collider, which physics cannot see.
        /// </summary>
        static List<SurfaceTriangle> StationSurfaceTriangles(Scene scene, Transform station, List<string> failures)
        {
            var corridor = new Bounds(station.TransformPoint(new Vector3(0, -1.15f, 3.35f)), new Vector3(12f, 4.5f, 12f));
            var triangles = new List<SurfaceTriangle>();
            foreach (var renderer in Components<MeshRenderer>(scene).Where(r => r.enabled && r.bounds.Intersects(corridor)))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                var mesh = filter.sharedMesh;
                if (!mesh.isReadable)
                {
                    failures.Add("Cannot inspect visible entrance mesh: " + renderer.name + " is not CPU-readable.");
                    continue;
                }
                var vertices = mesh.vertices;
                var indices = mesh.triangles;
                var matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    var a = matrix.MultiplyPoint3x4(vertices[indices[i]]);
                    var b = matrix.MultiplyPoint3x4(vertices[indices[i + 1]]);
                    var c = matrix.MultiplyPoint3x4(vertices[indices[i + 2]]);
                    var normal = Vector3.Cross(b - a, c - a);
                    // The top of treads, landings, roads and ramps faces upward.
                    if (normal.sqrMagnitude < 1e-10f || normal.normalized.y < .55f) continue;
                    float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                    float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    if (maxX < corridor.min.x || minX > corridor.max.x || maxZ < corridor.min.z || minZ > corridor.max.z) continue;
                    if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > station.position.y + .24f ||
                        Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < station.position.y - 3f) continue;
                    triangles.Add(new SurfaceTriangle { name = renderer.name, a = a, b = b, c = c, minX = minX, maxX = maxX, minZ = minZ, maxZ = maxZ });
                }
            }
            return triangles;
        }

        static bool VisibleHeight(List<SurfaceTriangle> triangles, Vector3 point, float ceiling, out float height, out string meshName)
        {
            height = float.NegativeInfinity;
            meshName = "";
            foreach (var t in triangles)
            {
                if (point.x < t.minX - .0001f || point.x > t.maxX + .0001f || point.z < t.minZ - .0001f || point.z > t.maxZ + .0001f) continue;
                float denominator = (t.b.z - t.c.z) * (t.a.x - t.c.x) + (t.c.x - t.b.x) * (t.a.z - t.c.z);
                if (Mathf.Abs(denominator) < 1e-8f) continue;
                float a = ((t.b.z - t.c.z) * (point.x - t.c.x) + (t.c.x - t.b.x) * (point.z - t.c.z)) / denominator;
                float b = ((t.c.z - t.a.z) * (point.x - t.c.x) + (t.a.x - t.c.x) * (point.z - t.c.z)) / denominator;
                float c = 1f - a - b;
                if (a < -.0001f || b < -.0001f || c < -.0001f) continue;
                float y = a * t.a.y + b * t.b.y + c * t.c.y;
                if (y > ceiling + .0001f || y <= height) continue;
                height = y;
                meshName = t.name;
            }
            return !float.IsNegativeInfinity(height);
        }

        static object StationAudit(Scene scene, PlayerMovement player, List<string> combinedFailures)
        {
            var failures = new List<string>();
            var samples = new List<object>();
            var traversals = new List<object>();
            var station = scene.GetRootGameObjects().Select(g => g.transform.Find("10_World/Public Transport/Subway Station")).FirstOrDefault(t => t);
            if (!station)
            {
                failures.Add("Generated subway station hierarchy is missing.");
            }
            else
            {
                var triangles = StationSurfaceTriangles(scene, station, failures);
                var sourceMotor = player.GetComponent<CharacterController>();
                bool previousMotorState = sourceMotor && sourceMotor.enabled;
                if (sourceMotor) sourceMotor.enabled = false;
                var motor = Probe(player);
                Physics.SyncTransforms();
                try
                {
                    // These are the shared, scaled Daldongne flight dimensions, measured
                    // from the entrance threshold. Five lanes cover both outer edges too.
                    const float topZ = .0351f, bottomZ = 4.797f, topY = .0078f, bottomY = -2.223f;
                    foreach (float lane in new[] { -4.65f, -2.325f, 0f, 2.325f, 4.65f })
                    for (float z = .20f; z <= 6.81f; z += .20f)
                    {
                        float expectedLocalY = z <= bottomZ ? Mathf.Lerp(topY, bottomY, Mathf.InverseLerp(topZ, bottomZ, z)) : -2.21f;
                        var point = station.TransformPoint(new Vector3(lane, expectedLocalY, z));
                        float ceiling = station.position.y + .24f;
                        bool visible = VisibleHeight(triangles, point, ceiling, out float visibleY, out string meshName);
                        bool physical = Physics.Raycast(new Vector3(point.x, ceiling, point.z), Vector3.down, out var floor, 3.6f, ~0, QueryTriggerInteraction.Ignore) && floor.normal.y > .55f;
                        string issue = "";
                        if (!visible) issue = "visible stair/landing hole";
                        else if (visibleY - point.y > .25f) issue = "flat or raised visible surface covers the descending stairs";
                        else if (point.y - visibleY > .25f) issue = "visible stair/landing is below its expected surface";
                        if (!physical) issue += (issue.Length > 0 ? "; " : "") + "no supporting stair/landing collider";
                        else if (Mathf.Abs(floor.point.y - point.y) > .25f) issue += (issue.Length > 0 ? "; " : "") + "physical floor does not follow the stair profile";
                        if (visible && physical && Mathf.Abs(visibleY - floor.point.y) > .25f)
                            issue += (issue.Length > 0 ? "; " : "") + "visible mesh and physical floor disagree";
                        if (issue.Length > 0) failures.Add("lane " + lane.ToString("F3") + ", depth " + z.ToString("F2") + ": " + issue + " [" + meshName + "]");
                        samples.Add(new { lane, depth = z, expectedY = point.y, visible, visibleY = visible ? (float?)visibleY : null,
                            visibleMesh = meshName, physical, physicalY = physical ? (float?)floor.point.y : null,
                            physicalCollider = physical ? floor.collider.name : "", passed = issue.Length == 0, issue });
                    }
                    foreach (float lane in new[] { -4.1f, 0f, 4.1f })
                    foreach (bool descending in new[] { true, false })
                    {
                        var entrance = station.TransformPoint(new Vector3(lane, .025f, -.7f));
                        var landing = station.TransformPoint(new Vector3(lane, -2.21f, 6.25f));
                        var from = descending ? entrance : landing;
                        var to = descending ? landing : entrance;
                        bool passed = Walk(motor, from, to, player.moveSpeed, out var finish, true);
                        if (!passed) failures.Add((descending ? "Descend" : "Ascend") + " lane " + lane + " blocked at " + finish.ToString("F3"));
                        traversals.Add(new { lane, descending, passed, from = from.ToString("F3"), to = to.ToString("F3"), finish = finish.ToString("F3") });
                    }
                }
                finally
                {
                    Object.DestroyImmediate(motor.gameObject);
                    if (sourceMotor) sourceMotor.enabled = previousMotorState;
                    Physics.SyncTransforms();
                }
            }
            combinedFailures.AddRange(failures.Select(f => "Station: " + f));
            var report = new { scene = scene.path, passed = failures.Count == 0, visibleSamples = samples.Count, traversalCount = traversals.Count, failures, samples, traversals };
            Write("transit-" + scene.name + "-station", report);
            return report;
        }

        public static object InspectStationCurrent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before station QA.");
            var scene = SceneManager.GetActiveScene();
            if (!CityDistrictBuilder.Names.Contains(scene.name)) throw new InvalidOperationException("Open one of the five generated district scenes.");
            var player = Components<PlayerMovement>(scene).Single();
            return StationAudit(scene, player, new List<string>());
        }

        public static object InspectCurrent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before layout QA.");
            var scene = SceneManager.GetActiveScene();
            var failures = new List<string>();
            var records = new List<object>();
            object stationAudit = null;
            var players = Components<PlayerMovement>(scene);
            var stops = Components<TransitStop>(scene, true);
            var spawns = Components<MapSpawnPoint>(scene, true);
            int portals = Components<MapPortal>(scene, true).Length;
            if (players.Length != 1) failures.Add("Expected exactly one player.");
            if (portals != 0) failures.Add("Legacy MapPortal objects remain: " + portals);
            foreach (TransitKind kind in Enum.GetValues(typeof(TransitKind)))
                if (stops.Count(s => s.kind == kind && s.isActiveAndEnabled) != 1 || stops.Count(s => s.kind == kind) != 1)
                    failures.Add("Expected one active " + kind + " facility and no duplicate inactive facility.");
            float speed = players.Length == 1 ? players[0].moveSpeed : 0;
            float separation = stops.Length == 2 ? FlatDistance(stops[0].BoardingPosition, stops[1].BoardingPosition) : 0;
            float minimumWalkSeconds = speed > 0 ? separation / speed : 0;
            if (speed <= 0) failures.Add("Invalid player walking speed.");
            if (minimumWalkSeconds < 10f) failures.Add("Facilities are less than 10 seconds apart at walking speed.");
            if (scene.name == "BusinessDistrict" && stops.Length == 2 && stops[0].BoardingPosition.z * stops[1].BoardingPosition.z >= 0)
                failures.Add("Business transport facilities must be on opposite river banks.");
            if (players.Length == 1)
            {
                var interaction = players[0].GetComponent<PlayerInteraction>();
                if (!interaction || !interaction.enabled) failures.Add("PlayerInteraction is missing or disabled.");
                else
                {
                    if (!interaction.uiFont) failures.Add("Korean UI font is not assigned.");
                    foreach (string path in ScenePaths)
                        if (interaction.destinations == null || interaction.destinations.Count(d => d != null && d.scenePath == path) != 1)
                            failures.Add("Destination is missing or duplicated: " + path);
                }
                var playerMotor = players[0].GetComponent<CharacterController>();
                bool enabled = playerMotor && playerMotor.enabled;
                if (playerMotor) playerMotor.enabled = false;
                var motor = Probe(players[0]);
                Physics.SyncTransforms();
                try
                {
                    foreach (var stop in stops)
                    {
                        var issues = new List<string>();
                        if (!stop.boardingPoint) issues.Add("Missing explicit boarding point.");
                        if (!Accessible(stop.BoardingPosition, motor.radius, out var boardingFloor)) issues.Add("Boarding point: " + boardingFloor);
                        var matches = spawns.Where(s => s.spawnId == stop.ArrivalSpawnId).ToArray();
                        bool arrivalWalk = false, returnWalk = false, publicApproach = false;
                        Vector3 finish = Vector3.zero, approach = Vector3.zero;
                        if (matches.Length != 1) issues.Add("Expected one arrival spawn: " + stop.ArrivalSpawnId);
                        else
                        {
                            var arrival = matches[0].transform.position;
                            if (!Accessible(arrival, motor.radius, out var arrivalFloor)) issues.Add("Arrival: " + arrivalFloor);
                            arrivalWalk = Walk(motor, arrival, stop.BoardingPosition, speed, out finish);
                            if (!arrivalWalk) issues.Add("Arrival cannot walk to boarding point; stopped at " + finish.ToString("F3"));
                            returnWalk = Walk(motor, stop.BoardingPosition, arrival, speed, out finish);
                            if (!returnWalk) issues.Add("Boarding point cannot walk back to arrival; stopped at " + finish.ToString("F3"));
                        }
                        // Require a continuous approach from outside the interaction radius,
                        // so an isolated but locally clear point cannot pass on spawn checks alone.
                        float approachDistance = Mathf.Max(5f, stop.interactionRadius + 1.25f);
                        for (int bearing = 0; bearing < 16 && !publicApproach; bearing++)
                        {
                            Vector3 direction = Quaternion.Euler(0, bearing * 22.5f, 0) * Vector3.forward;
                            bool corridorClear = true;
                            for (float d = .75f; d <= approachDistance; d += .75f)
                                if (!Accessible(stop.BoardingPosition + direction * d, motor.radius, out _)) { corridorClear = false; break; }
                            if (!corridorClear) continue;
                            var candidate = stop.BoardingPosition + direction * approachDistance;
                            if (Walk(motor, candidate, stop.BoardingPosition, speed, out _) && Walk(motor, stop.BoardingPosition, candidate, speed, out _))
                            { publicApproach = true; approach = candidate; }
                        }
                        if (!publicApproach) issues.Add("No continuous five-metre walking approach outside the interaction zone.");
                        records.Add(new { kind = stop.kind.ToString(), boarding = stop.BoardingPosition.ToString("F3"), spawnId = stop.ArrivalSpawnId,
                            arrivalWalk, returnWalk, publicApproach, approach = approach.ToString("F3"), passed = issues.Count == 0, issues });
                        failures.AddRange(issues.Select(s => stop.kind + ": " + s));
                    }
                }
                finally
                {
                    Object.DestroyImmediate(motor.gameObject);
                    if (playerMotor) playerMotor.enabled = enabled;
                    Physics.SyncTransforms();
                }
                if (CityDistrictBuilder.Names.Contains(scene.name)) stationAudit = StationAudit(scene, players[0], failures);
            }
            var result = new { scene = scene.path, passed = failures.Count == 0, legacyPortals = portals, facilities = stops.Length,
                walkingSpeed = speed, straightLineSeparation = separation, minimumWalkSeconds, records, stationAudit, failures };
            Write("transit-" + scene.name + "-audit", result);
            return result;
        }

        /// <summary>
        /// Opens/cancels the real picker, then chooses each next map through PlayerInteraction.
        /// The first six legs use subway arrivals and the next six use bus arrivals.
        /// </summary>
        public static string StartTour()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene before starting transit QA.");
            var tour = new Tour
            {
                deadline = EditorApplication.timeSinceStartup + 600,
                originalBackground = PlayerSettings.runInBackground,
                originalScene = SceneManager.GetActiveScene().path,
                originalStartScene = EditorSceneManager.playModeStartScene ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : ""
            };
            EditorSceneManager.OpenScene(ScenePaths[0], OpenSceneMode.Single);
            EditorSceneManager.playModeStartScene = null;
            Store(tour);
            Application.runInBackground = true;
            EditorApplication.EnterPlaymode();
            return "Transit tour started: six maps, both facilities, twelve actual scene changes.";
        }

        public static string TourStatus() => SessionState.GetString(Session, "No transit tour.");

        static void Store(Tour tour) { SessionState.SetString(Session, JsonUtility.ToJson(tour)); }

        static void NextStage(Tour tour, int stage)
        {
            tour.stage = stage;
            tour.frame = Time.frameCount;
            tour.waitedFrames = 0;
            tour.sceneHandle = SceneManager.GetActiveScene().handle;
            Store(tour);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void CaptureScreen(Scene scene, TransitKind kind, string phase)
        {
            Directory.CreateDirectory(Output);
            // The following stage leaves this UI state visible for several rendered frames.
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(Output + "transit-" + scene.name + "-" + kind + "-" + phase + ".png"));
        }

        static void Tick()
        {
            string json = SessionState.GetString(Session, "");
            if (string.IsNullOrEmpty(json)) return;
            var tour = JsonUtility.FromJson<Tour>(json);
            if (tour.finished) return;
            try
            {
                if (tour.restore)
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    if (!string.IsNullOrEmpty(tour.originalScene)) EditorSceneManager.OpenScene(tour.originalScene, OpenSceneMode.Single);
                    EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(tour.originalStartScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(tour.originalStartScene);
                    PlayerSettings.runInBackground = tour.originalBackground;
                    Application.runInBackground = tour.originalBackground;
                    tour.finished = true;
                    Write("transit-play-tour", tour);
                    Store(tour);
                    return;
                }
                if (EditorApplication.timeSinceStartup > tour.deadline) throw new TimeoutException("Transit tour exceeded 600 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isPaused || SceneLoadManager.IsLoading) return;
                var scene = SceneManager.GetActiveScene();
                // Count frames after the new scene becomes active. A load may consume
                // many frames, and a Play/domain restart may reset the counter entirely.
                if (tour.sceneHandle != scene.handle || Time.frameCount < tour.frame)
                {
                    tour.sceneHandle = scene.handle;
                    tour.frame = Time.frameCount;
                    tour.waitedFrames = 0;
                    Store(tour);
                    return;
                }
                if (Time.frameCount == tour.frame) return;
                tour.waitedFrames += Time.frameCount - tour.frame;
                tour.frame = Time.frameCount;
                Store(tour);
                if (tour.waitedFrames < (tour.stage == 0 ? 35 : 3)) return;
                Require(string.IsNullOrEmpty(SceneLoadManager.LastError), "Scene load failed: " + SceneLoadManager.LastError);
                Application.runInBackground = true;
                Require(scene.path == ScenePaths[tour.leg % ScenePaths.Length], "Unexpected arrival scene: " + scene.path);
                var players = Components<PlayerMovement>(scene);
                Require(players.Length == 1, "Expected one scene-local player in " + scene.name);
                var player = players[0];
                var interaction = player.GetComponent<PlayerInteraction>();
                Require(interaction && interaction.enabled, "Missing active PlayerInteraction in " + scene.name);
                var cameras = Components<Camera>(scene).Where(c => c.isActiveAndEnabled).ToArray();
                Require(cameras.Length == 1 && player.viewCamera == cameras[0], "Player has the wrong active camera.");
                var cameraController = cameras[0].GetComponent<PlayerCameraController>();
                Require(cameraController && cameraController.target == player.transform, "Camera follows the wrong player.");
                var kind = tour.leg < ScenePaths.Length ? TransitKind.Subway : TransitKind.Bus;
                var stop = Components<TransitStop>(scene).Single(s => s.kind == kind);
                var spawn = Components<MapSpawnPoint>(scene).Single(s => s.spawnId == stop.ArrivalSpawnId);
                var motor = player.GetComponent<CharacterController>();

                if (tour.stage == 0)
                {
                    // The two starting points are test setup; all twelve destination
                    // arrivals below are made by the actual asynchronous scene loader.
                    if (tour.leg == ScenePaths.Length)
                    {
                        var subwayArrival = Components<MapSpawnPoint>(scene).Single(s => s.spawnId == "subway");
                        Require(FlatDistance(player.transform.position, subwayArrival.transform.position) < .65f &&
                            Mathf.Abs(player.transform.position.y - subwayArrival.transform.position.y) < .6f,
                            "Final subway arrival used the wrong spawn.");
                        Require(Physics.Raycast(player.transform.position + Vector3.up * .25f, Vector3.down, .9f, ~0, QueryTriggerInteraction.Ignore),
                            "Final subway arrival is not grounded.");
                        tour.checks.Add(scene.name + ": final subway arrival grounded; one player and own camera.");
                    }
                    if (tour.leg == 0 || tour.leg == ScenePaths.Length)
                    {
                        player.spawn = spawn.transform.position;
                        player.ResetToSpawn();
                    }
                    else Require(FlatDistance(player.transform.position, spawn.transform.position) < .65f &&
                        Mathf.Abs(player.transform.position.y - spawn.transform.position.y) < .6f, "Arrival used the wrong transport spawn.");
                    Require(Physics.Raycast(player.transform.position + Vector3.up * .25f, Vector3.down, .9f, ~0, QueryTriggerInteraction.Ignore), "Ungrounded arrival.");
                    if (tour.leg == ScenePaths.Length * 2)
                    {
                        tour.checks.Add(scene.name + ": final bus arrival grounded; one player and own camera.");
                        tour.success = true;
                        tour.restore = true;
                        Store(tour);
                        EditorApplication.ExitPlaymode();
                        return;
                    }
                    Require(Walk(motor, player.transform.position, stop.BoardingPosition, player.moveSpeed, out var finish),
                        "Cannot walk from arrival to " + kind + " boarding point; stopped at " + finish.ToString("F3"));
                    motor.enabled = true;
                    Physics.SyncTransforms();
                    NextStage(tour, 1);
                    return;
                }
                if (tour.stage == 1)
                {
                    Require(interaction.FocusedStop == stop, "Nearest transport prompt did not activate.");
                    var ui = Components<TransitUI>(scene).Single();
                    var prompt = ui.transform.Find("BoardingPrompt");
                    var key = prompt.Find("SpaceKeycap/Space").GetComponent<TMPro.TMP_Text>();
                    Require(prompt.gameObject.activeInHierarchy && key.text == "SPACE", "SPACE keycap is missing.");
                    Require(prompt.Find("Action").GetComponent<TMPro.TMP_Text>().text == stop.Prompt, "Wrong Korean action label.");
                    CaptureScreen(scene, kind, "prompt");
                    NextStage(tour, 2);
                    return;
                }
                if (tour.stage == 2)
                {
                    tour.cursorVisible = Cursor.visible;
                    tour.cursorLock = (int)Cursor.lockState;
                    Require(interaction.OpenDestinationMenu(), "Could not open the destination picker.");
                    Require(interaction.IsDestinationMenuOpen && !player.enabled && !cameraController.enabled, "Menu did not suspend player/camera controls.");
                    Require(!interaction.IsMenuReady, "The opening frame can accidentally submit a destination.");
                    Require(Cursor.visible && Cursor.lockState == CursorLockMode.None, "Menu cursor is not available.");
                    NextStage(tour, 3);
                    return;
                }
                if (tour.stage == 3)
                {
                    Require(interaction.IsMenuReady, "Picker did not become ready after opening input was released.");
                    var ui = Components<TransitUI>(scene).Single();
                    var modal = ui.transform.Find("TransitModal");
                    var rows = modal.Find("DestinationWindow/Destinations").GetComponentsInChildren<UnityEngine.UI.Button>();
                    Require(modal.gameObject.activeInHierarchy && rows.Length == ScenePaths.Length - 1, "Picker must show all five other maps.");
                    var current = interaction.destinations.Single(d => d.scenePath == scene.path).displayName;
                    Require(rows.All(b => b.name != "Destination_" + current && b.interactable), "Current map is listed or a destination is disabled.");
                    foreach(var row in rows)
                    {
                        var label=row.transform.Find("Name").GetComponent<TMPro.TMP_Text>();label.ForceMeshUpdate();
                        Require(label.textInfo.characterCount>0&&label.textInfo.characterInfo.Take(label.textInfo.characterCount).Any(c=>c.isVisible),"Destination name is clipped or invisible: "+row.name);
                    }
                    Require(!interaction.TryTravelTo(scene.path) && !interaction.TryTravelTo("Assets/Scenes/Unknown.unity"), "Picker accepted the current or an unknown map.");
                    CaptureScreen(scene, kind, "menu");
                    NextStage(tour, 4);
                    return;
                }
                if (tour.stage == 4)
                {
                    interaction.CloseDestinationMenu();
                    Require(!interaction.IsDestinationMenuOpen, "Cancel did not close the picker.");
                    NextStage(tour, 5);
                    return;
                }
                if (tour.stage == 5)
                {
                    Require(player.enabled && cameraController.enabled, "Cancel did not restore movement/camera controls.");
                    Require(Cursor.visible == tour.cursorVisible && (int)Cursor.lockState == tour.cursorLock, "Cancel did not restore the cursor state.");
                    Require(interaction.OpenDestinationMenu(), "Could not reopen the picker after cancelling.");
                    NextStage(tour, 6);
                    return;
                }
                Require(interaction.IsMenuReady, "Reopened picker is not ready.");
                string target = ScenePaths[(tour.leg + 1) % ScenePaths.Length];
                Require(interaction.TryTravelTo(target), "Picker failed to load " + target);
                Require(SceneLoadManager.IsLoading, "Scene transition did not start.");
                tour.checks.Add(scene.name + " / " + kind + ": grounded arrival, walking approach, Korean prompt and SPACE keycap, five destinations, cancel and control restoration, transition to " + Path.GetFileNameWithoutExtension(target));
                tour.leg++;
                NextStage(tour, 0);
            }
            catch (Exception exception)
            {
                tour.error = exception.ToString();
                tour.success = false;
                tour.restore = true;
                Write("transit-play-tour", tour);
                Store(tour);
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            }
        }
    }
}
