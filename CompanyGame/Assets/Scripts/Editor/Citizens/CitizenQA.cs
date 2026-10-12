using System;
using System.Linq;
using System.Text;
using CompanyGame.Daldongne;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace CompanyGame.Editor.Citizens
{
    /// <summary>
    /// Edit-mode checks for the citizen feature. Run from the menu or with -executeMethod CompanyGame.Editor.Citizens.CitizenQA.Run.
    /// Throws when something that must hold does not; lines starting with INFO or WARN need a human look.
    /// Not covered here (needs Play Mode): 60 s of walking without stepping on a road, stop on dialogue, waiting at a red light.
    /// </summary>
    public static class CitizenQA
    {
        static readonly StringBuilder Log = new StringBuilder();
        static int failures;

        [MenuItem("Tools/Company Game/Citizens/Run QA")]
        public static void Run()
        {
            Log.Clear(); failures = 0;
            CheckInfo();
            CheckPrefab("CitizenMale");
            CheckPrefab("CitizenFemale");
            Check(Resources.Load<GameObject>("Citizens/CitizenBook"), "CitizenBook prefab exists in Resources/Citizens");
            foreach (string path in CitizenNavBuilder.OutdoorMaps().ToArray()) CheckMap(path);
            foreach (string path in CitizenNavBuilder.InteriorMaps().ToArray()) CheckInterior(path);
            Debug.Log("[CitizenQA]\n" + Log);
            if (failures > 0) throw new InvalidOperationException("CitizenQA: " + failures + " failure(s), see log above.");
        }

        static void Check(bool ok, string message)
        {
            if (!ok) failures++;
            Log.AppendLine((ok ? "PASS " : "FAIL ") + message);
        }

        static void Note(string level, string message) => Log.AppendLine(level + " " + message);

        static void CheckInfo()
        {
            Check(CitizenInfo.Names.Length >= 20 && CitizenInfo.Names.Distinct().Count() == CitizenInfo.Names.Length, "20+ distinct names");
            Check(CitizenInfo.Lines.Length >= 12 && CitizenInfo.Lines.Distinct().Count() == CitizenInfo.Lines.Length, "12+ distinct lines");
            int hash = CitizenInfo.Hash("Assets/Scenes/Maps/BusinessDistrict.unity");
            var names = Enumerable.Range(0, CitizenSpawner.PerMap).Select(i => CitizenInfo.NameFor(hash, i)).ToArray();
            var lines = Enumerable.Range(0, CitizenSpawner.PerMap).Select(i => CitizenInfo.LineFor(hash, i)).ToArray();
            Check(names.Distinct().Count() == names.Length && lines.Distinct().Count() == lines.Length, "names and lines unique within one map");
            Check(CitizenInfo.Hash("a") == CitizenInfo.Hash("a") && CitizenInfo.NameFor(hash, 3) == names[3], "deterministic per (map, index)");
        }

        static void CheckPrefab(string name)
        {
            var prefab = Resources.Load<GameObject>("Citizens/" + name);
            Check(prefab, name + " prefab exists in Resources/Citizens (run Build Citizen Prefabs)");
            if (!prefab) return;
            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            int triangles = skin && skin.sharedMesh ? skin.sharedMesh.triangles.Length / 3 : -1;
            Check(triangles > 0 && triangles <= CitizenPrefabBuilder.TriangleBudget, name + " triangles " + triangles + " <= " + CitizenPrefabBuilder.TriangleBudget);
            Check(skin && skin.bones.Length == 10 && skin.bones.All(b => b) && skin.sharedMesh.bindposes.Length == 10, name + " keeps the 10-bone rig");
            var motion = prefab.GetComponent<DaldongneAvatarMotion>();
            Check(motion && motion.hips && motion.leftLeg && motion.rightLeg && motion.leftKnee && motion.rightKnee && motion.leftArm && motion.rightArm,
                name + " DaldongneAvatarMotion bones assigned");
            var agent = prefab.GetComponent<NavMeshAgent>();
            var capsule = prefab.GetComponent<CapsuleCollider>();
            Check(agent && agent.radius <= .31f && !agent.autoTraverseOffMeshLink, name + " NavMeshAgent (r .3, manual link traversal)");
            Check(capsule && !capsule.isTrigger && prefab.GetComponent<CitizenWalker>() && prefab.layer == 0, name + " non-trigger capsule, CitizenWalker, Default layer");
        }

        static void CheckMap(string path)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            string tag = scene.name + ": ";
            int triangles = NavMesh.CalculateTriangulation().indices.Length / 3;
            var surfaces = UnityEngine.Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
            Check(triangles > 0 && surfaces.Any(s => s.navMeshData), tag + "NavMesh baked (" + triangles + " triangles)");
            CheckRoads(tag);
            CheckCrosswalks(tag);
            CheckStarts(tag, 20, null);
            int benches = UnityEngine.Object.FindObjectsByType<Seat>(FindObjectsSortMode.None).Count(s => s.owner);
            Note("INFO", tag + benches + " bench seats" + (benches == 0 ? " (citizens will never sit here)" : ""));
        }

        // Road centrelines: sample points on the top faces of every road mesh; none may have walkable NavMesh under it.
        static void CheckRoads(string tag)
        {
            int samples = 0, leaks = 0, roads = 0;
            var weights = new[] { new Vector3(.34f, .33f, .33f), new Vector3(.6f, .2f, .2f), new Vector3(.2f, .6f, .2f), new Vector3(.2f, .2f, .6f) };
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var filter = r.GetComponent<MeshFilter>();
                if (!CitizenNavBuilder.IsRoad(r.gameObject.name) || !filter || !filter.sharedMesh || !filter.sharedMesh.isReadable) continue;
                roads++;
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices; var indices = mesh.triangles;
                for (int t = 0; t < indices.Length; t += 3)
                {
                    Vector3 a = r.transform.TransformPoint(vertices[indices[t]]), b = r.transform.TransformPoint(vertices[indices[t + 1]]), c = r.transform.TransformPoint(vertices[indices[t + 2]]);
                    if (Vector3.Cross(b - a, c - a).normalized.y < .9f) continue;
                    // Centroids of a subdivision with about 4 m cells, so a long road is covered along its whole length.
                    int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max((b - a).magnitude, (c - a).magnitude) / 4f), 1, 40);
                    for (int i = 0; i < n; i++)
                    for (int j = 0; j < n - i; j++)
                    {
                        foreach (var up in new[] { true, false })
                        {
                            if (!up && j >= n - i - 1) continue;
                            float u = (i + (up ? 1f / 3f : 2f / 3f)) / n, v = (j + (up ? 1f / 3f : 2f / 3f)) / n;
                            Vector3 p = a + (b - a) * u + (c - a) * v;
                            samples++;
                            if (!NavMesh.SamplePosition(p, out var hit, .4f, NavMesh.AllAreas) || new Vector2(hit.position.x - p.x, hit.position.z - p.z).sqrMagnitude > .0225f) continue;
                            if (++leaks <= 5) Note("WARN", tag + r.gameObject.name + " walkable at " + p.ToString("F1"));
                        }
                    }
                }
            }
            // A few samples at the very rim of a road piece can fall inside the contour-simplification error (about .2 m) of the
            // sidewalk edge, so up to 0.2% is tolerated; the WARN lines above show where.
            Check(leaks <= samples * .002f, tag + roads + " road meshes, " + samples + " road samples, " + leaks + " walkable (max 0.2%)");
        }

        static void CheckCrosswalks(string tag)
        {
            var crosswalks = UnityEngine.Object.FindObjectsByType<Crosswalk>(FindObjectsSortMode.None);
            int bad = 0;
            foreach (var c in crosswalks)
            {
                bool ends = c.endA && c.endB && OnMesh(c.endA.position) && OnMesh(c.endB.position);
                if (!(ends && c.GetComponent<NavMeshLink>())) { bad++; Note("WARN", tag + c.name + " lacks a NavMeshLink or an end is off the NavMesh"); }
            }
            Check(bad == 0, tag + crosswalks.Length + " crosswalks, " + bad + " without link/ends on the NavMesh" + (crosswalks.Length == 0 ? " (citizens do not cross roads here)" : ""));
        }

        static bool OnMesh(Vector3 p) => NavMesh.SamplePosition(p, out _, 1.5f, NavMesh.AllAreas);

        // Start points exist, lie on the NavMesh, and a walker standing there can find a complete path to somewhere new
        // (the same pick the walker makes: random direction, 8-40 m outdoors / 2.5-9 m indoors).
        static void CheckStarts(string tag, int minimum, CitizenSpawner.IndoorSpec spec)
        {
            var info = UnityEngine.Object.FindAnyObjectByType<CitizenNavInfo>();
            var starts = (info ? info.starts : new Vector3[0]).Where(p => spec == null || spec.walkArea.Contains(p)).ToArray();
            Check(starts.Length >= minimum, tag + starts.Length + " start points" + (spec != null ? " in the walk area" : "") + " (>= " + minimum + ")");
            if (starts.Length == 0) return;
            var rng = new System.Random(3);
            var path = new NavMeshPath();
            float lo = spec != null ? 2.5f : 8f, hi = spec != null ? 9f : 40f;
            int ok = 0, trials = Mathf.Min(40, starts.Length), off = 0;
            for (int i = 0; i < trials; i++)
            {
                Vector3 s = starts[rng.Next(starts.Length)];
                if (!NavMesh.SamplePosition(s, out var onMesh, .5f, NavMesh.AllAreas)) { off++; continue; }
                for (int k = 0; k < 6; k++)
                {
                    float angle = (float)rng.NextDouble() * 6.2832f, d = lo + (float)rng.NextDouble() * (hi - lo);
                    var target = onMesh.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * d;
                    if (NavMesh.SamplePosition(target, out var t, spec != null ? 2f : 6f, NavMesh.AllAreas)
                        && NavMesh.CalculatePath(onMesh.position, t.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) { ok++; break; }
                }
            }
            Check(off == 0 && ok >= trials * .75f, tag + ok + "/" + trials + " random starts can walk to a new spot (" + off + " off the NavMesh)");
        }

        static void CheckInterior(string path)
        {
            var spec = CitizenSpawner.Indoors[path];
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            string tag = scene.name + ": ";
            int triangles = NavMesh.CalculateTriangulation().indices.Length / 3;
            Check(triangles > 0, tag + "NavMesh baked (" + triangles + " triangles)");
            var seats = CitizenSpawner.PickSeats(spec, new System.Random(CitizenInfo.Hash(path)));
            Check(seats.Count == spec.sitters, tag + seats.Count + "/" + spec.sitters + " seats found: " + string.Join(", ", seats.Select(s => s.name)));
            foreach (var seat in seats) Note("INFO", tag + "seat " + seat.name + " at " + seat.transform.position.ToString("F1") + " yaw " + seat.transform.eulerAngles.y.ToString("0"));
            CheckStarts(tag, spec.browsers + spec.chatPairs * 2 + 2, spec);
            var player = UnityEngine.Object.FindObjectsByType<CompanyGame.World.Maps.MapSpawnPoint>(FindObjectsSortMode.None).FirstOrDefault(s => s.spawnId == "default");
            if (path.EndsWith("CivicLibraryInterior.unity"))
            {
                foreach (float x in new[] { 7.85f, 10.15f, 12.45f }) Check(NavMesh.SamplePosition(new Vector3(x, 0f, 1.2f), out var aisle, .3f, NavMesh.AllAreas) && Mathf.Abs(aisle.position.x - x) < .1f, tag + "shelf aisle x=" + x + " is walkable");
                foreach (float x in new[] { 6.7f, 9f, 11.3f }) Check(!NavMesh.SamplePosition(new Vector3(x, 0f, 3.6f), out var shelf, .2f, NavMesh.AllAreas), tag + "shelf row x=" + x + " is blocked");
            }
            Check(player && NavMesh.SamplePosition(player.transform.position, out _, 1.5f, NavMesh.AllAreas), tag + "default spawn is on the NavMesh");
        }

        // Name of the renderer whose top is closest under the point (diagnostics only).
        static string SurfaceUnder(Vector3 p)
        {
            string best = "?";
            float bestGap = 1f;
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var b = r.bounds;
                if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
                float gap = Mathf.Abs(b.max.y - p.y);
                if (gap < bestGap) { bestGap = gap; best = r.gameObject.name + "(h" + b.size.y.ToString("0") + ")"; }
            }
            return best;
        }
    }
}
