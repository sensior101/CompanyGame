using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CompanyGame.World.Maps;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace CompanyGame.Editor.Citizens
{
    /// <summary>
    /// Bakes the walkable NavMesh for the citizens: every outdoor map and the interiors that have citizens.
    /// Outdoors: everything with a render mesh is walkable except roads and water (Not Walkable modifiers) in ALL maps,
    /// whether or not cars drive there; citizens only cross where a Crosswalk object gives a NavMeshLink.
    /// Indoors: physics colliders (what the player walks on).
    /// The bake uses the citizen's own size (radius .3, height 1.8) on the default agent type, so ProjectSettings stay untouched
    /// (the project's Humanoid radius .5 closed every gap under about 1.1 m). A start-point list of real ground
    /// (CitizenNavInfo) is written next to the surface. Idempotent: reuses the "Citizen Navigation" root.
    /// </summary>
    public static class CitizenNavBuilder
    {
        public const string RootName = "Citizen Navigation";
        public const float AgentRadius = .3f, AgentHeight = 1.8f, VoxelSize = .15f;
        const int NotWalkable = 1;
        const string BusinessPath = "Assets/Scenes/Maps/BusinessDistrict.unity";
        static readonly Regex Road = new Regex("(street|road|perimeter|asphalt)", RegexOptions.IgnoreCase);
        static readonly Regex Water = new Regex("(water|ocean|^sea|lake|pond|canal|riversurface|riverbed)", RegexOptions.IgnoreCase);
        // Small props named Street*, and walkways that merely contain "road" in their name.
        static readonly Regex Prop = new Regex("(lamp|light|(?<!s)tree|sign|bench|bin|pole|hydrant|planter|rail|fence|causeway|walk|crosswalk)", RegexOptions.IgnoreCase);

        public static IEnumerable<string> OutdoorMaps() =>
            AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(CitizenSpawner.IsOutdoorMap).OrderBy(p => p);

        public static IEnumerable<string> InteriorMaps() => CitizenSpawner.Indoors.Keys.OrderBy(p => p);

        public static bool IsRoad(string name) => !Prop.IsMatch(name) && Road.IsMatch(name);
        public static bool IsNotWalkable(string name) => !Prop.IsMatch(name) && (Road.IsMatch(name) || Water.IsMatch(name));

        [MenuItem("Tools/Company Game/Citizens/Bake Navigation (all maps and interiors)")]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (string path in OutdoorMaps().Concat(InteriorMaps()).ToArray()) Debug.Log("[Citizens] " + Build(path));
        }

        /// <summary>Every outdoor map except BusinessDistrict (its traffic coder saves it separately), then the interiors.</summary>
        public static void BuildAllButBusiness()
        {
            foreach (string path in OutdoorMaps().Where(p => p != BusinessPath).Concat(InteriorMaps()).ToArray()) Debug.Log("[Citizens] " + Build(path));
        }

        public static void BuildBusiness() => Debug.Log("[Citizens] " + Build(BusinessPath));

        /// <summary>Bakes one scene and saves it. Returns a one-line report.</summary>
        public static string Build(string scenePath)
        {
            bool interior = CitizenSpawner.Indoors.ContainsKey(scenePath);
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var surface = EnsureSurface(scene, interior);
            int roads = interior ? 0 : MarkNotWalkable(scene);
            int links = interior ? 0 : EnsureCrosswalkLinks();
            Bake(surface, scene);
            int starts = WriteStarts(surface, scene, interior);
            var triangulation = NavMesh.CalculateTriangulation();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return Path.GetFileName(scenePath) + ": " + (triangulation.indices.Length / 3) + " NavMesh triangles, "
                + roads + " not-walkable renderers, " + links + " new crosswalk links, " + starts + " start points.";
        }

        static NavMeshSurface EnsureSurface(Scene scene, bool interior)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(o => o.name == RootName);
            if (!root) { root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene); }
            var surface = root.GetComponent<NavMeshSurface>();
            if (!surface) surface = root.AddComponent<NavMeshSurface>();
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = interior ? NavMeshCollectGeometry.PhysicsColliders : NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = ~0;
            surface.defaultArea = 0;
            surface.minRegionArea = 0f; // keep small sidewalk pieces
            if (!root.GetComponent<CitizenNavInfo>()) root.AddComponent<CitizenNavInfo>();
            return surface;
        }

        static int MarkNotWalkable(Scene scene)
        {
            int count = 0;
            // Start clean: modifiers from an earlier bake may now be wrong.
            foreach (var old in UnityEngine.Object.FindObjectsByType<NavMeshModifier>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (old.gameObject.scene == scene && old.overrideArea && old.area == NotWalkable && old.GetComponent<MeshRenderer>()) UnityEngine.Object.DestroyImmediate(old);
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (renderer.gameObject.scene != scene || !IsNotWalkable(renderer.gameObject.name)) continue;
                var modifier = renderer.GetComponent<NavMeshModifier>();
                if (!modifier) modifier = renderer.gameObject.AddComponent<NavMeshModifier>();
                modifier.overrideArea = true;
                modifier.area = NotWalkable;
                count++;
            }
            return count;
        }

        // Crosswalk (NPC/Traffic, bundle F) says where two sidewalks meet; give it a link if F did not.
        static int EnsureCrosswalkLinks()
        {
            int added = 0;
            foreach (var crosswalk in UnityEngine.Object.FindObjectsByType<Crosswalk>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!crosswalk.endA || !crosswalk.endB || crosswalk.GetComponent<NavMeshLink>()) continue;
                var link = crosswalk.gameObject.AddComponent<NavMeshLink>();
                link.agentTypeID = 0;
                link.area = 0;
                link.width = 3f;
                link.bidirectional = true;
                link.startPoint = crosswalk.transform.InverseTransformPoint(crosswalk.endA.position);
                link.endPoint = crosswalk.transform.InverseTransformPoint(crosswalk.endB.position);
                added++;
            }
            return added;
        }

        // NavMeshSurface.BuildNavMesh would use the project's Humanoid size; this is the same call with the citizen's size.
        // CollectSources is private in the package, hence the reflection (editor tool only).
        static void Bake(NavMeshSurface surface, Scene scene)
        {
            string folder = Path.Combine(Path.GetDirectoryName(scene.path), Path.GetFileNameWithoutExtension(scene.path)).Replace('\\', '/');
            string asset = folder + "/NavMesh-CitizenNavigation.asset";
            surface.RemoveData();
            surface.navMeshData = null;
            if (File.Exists(asset)) AssetDatabase.DeleteAsset(asset);
            var collect = typeof(NavMeshSurface).GetMethod("CollectSources", BindingFlags.Instance | BindingFlags.NonPublic);
            if (collect == null) throw new InvalidOperationException("NavMeshSurface.CollectSources not found (package changed).");
            var sources = (List<NavMeshBuildSource>)collect.Invoke(surface, null);
            var settings = surface.GetBuildSettings();
            settings.agentRadius = AgentRadius;
            settings.agentHeight = AgentHeight;
            settings.overrideVoxelSize = true;
            settings.voxelSize = VoxelSize;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, SourceBounds(sources), surface.transform.position, surface.transform.rotation);
            if (!data) throw new InvalidOperationException("NavMesh bake produced no data for " + scene.path);
            data.name = surface.gameObject.name;
            surface.navMeshData = data;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            AssetDatabase.CreateAsset(data, asset);
            surface.AddData();
            EditorUtility.SetDirty(surface);
        }

        static Bounds SourceBounds(List<NavMeshBuildSource> sources)
        {
            var bounds = new Bounds();
            bool first = true;
            foreach (var s in sources)
            {
                Bounds local = s.shape == NavMeshBuildSourceShape.Mesh && s.sourceObject is Mesh m ? m.bounds
                    : s.shape == NavMeshBuildSourceShape.Terrain && s.sourceObject is TerrainData t ? new Bounds(t.size * .5f, t.size)
                    : new Bounds(Vector3.zero, s.size);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 world = s.transform.MultiplyPoint3x4(corner);
                    if (first) { bounds = new Bounds(world, Vector3.zero); first = false; } else bounds.Encapsulate(world);
                }
            }
            return bounds;
        }

        // ---- start points: ground islands a person can really stand on ------------------------

        sealed class Island { public float area, minY; public bool anchored; public List<int> triangles = new List<int>(); }

        // Islands of the baked mesh: big enough, and either containing a spawn point/portal or at ground level
        // (the lowest level an anchor stands on). Roofs, rail tops and slivers are dropped.
        static int WriteStarts(NavMeshSurface surface, Scene scene, bool interior)
        {
            var info = surface.GetComponent<CitizenNavInfo>();
            var mesh = NavMesh.CalculateTriangulation();
            int triangleCount = mesh.indices.Length / 3;
            var parent = Enumerable.Range(0, mesh.vertices.Length).ToArray();
            var cell = new Dictionary<(int, int, int), int>();
            int[] canon = new int[mesh.vertices.Length];
            for (int v = 0; v < canon.Length; v++)
            {
                Vector3 p = mesh.vertices[v];
                var key = (Mathf.RoundToInt(p.x * 20f), Mathf.RoundToInt(p.y * 20f), Mathf.RoundToInt(p.z * 20f));
                if (cell.TryGetValue(key, out int other)) canon[v] = other; else { cell[key] = v; canon[v] = v; }
            }
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int t = 0; t < triangleCount; t++)
            {
                int a = Find(canon[mesh.indices[t * 3]]);
                for (int k = 1; k < 3; k++) { int b = Find(canon[mesh.indices[t * 3 + k]]); if (a != b) parent[b] = a; }
            }
            var islands = new Dictionary<int, Island>();
            for (int t = 0; t < triangleCount; t++)
            {
                int id = Find(canon[mesh.indices[t * 3]]);
                if (!islands.TryGetValue(id, out var island)) islands[id] = island = new Island { minY = float.MaxValue };
                Vector3 a = mesh.vertices[mesh.indices[t * 3]], b = mesh.vertices[mesh.indices[t * 3 + 1]], c = mesh.vertices[mesh.indices[t * 3 + 2]];
                island.area += Vector3.Cross(b - a, c - a).magnitude * .5f;
                island.minY = Mathf.Min(island.minY, a.y, b.y, c.y);
                island.triangles.Add(t);
            }
            float groundY = float.MaxValue;
            foreach (var anchor in Anchors(scene))
            {
                if (!NavMesh.SamplePosition(anchor, out var hit, 10f, NavMesh.AllAreas)) continue;
                int best = -1; float bestSqr = 9f;
                for (int t = 0; t < triangleCount; t++)
                {
                    Vector3 centre = (mesh.vertices[mesh.indices[t * 3]] + mesh.vertices[mesh.indices[t * 3 + 1]] + mesh.vertices[mesh.indices[t * 3 + 2]]) / 3f;
                    float sqr = (centre - hit.position).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = t; }
                }
                if (best < 0) continue;
                islands[Find(canon[mesh.indices[best * 3]])].anchored = true;
                groundY = Mathf.Min(groundY, hit.position.y);
            }
            float minArea = interior ? 15f : 120f;
            var good = islands.Values.Where(i => i.area >= minArea && (i.anchored || i.minY <= groundY + .6f)).ToList();
            var rng = new System.Random(7);
            var points = new List<Vector3>();
            float totalArea = good.Sum(i => i.area);
            int target = interior ? 400 : 300;
            foreach (var island in good)
            {
                int n = Mathf.Max(3, Mathf.RoundToInt(target * island.area / Mathf.Max(1f, totalArea)));
                for (int k = 0; k < n; k++)
                {
                    int t = island.triangles[rng.Next(island.triangles.Count)];
                    float u = (float)rng.NextDouble(), w = (float)rng.NextDouble();
                    if (u + w > 1f) { u = 1f - u; w = 1f - w; }
                    Vector3 a = mesh.vertices[mesh.indices[t * 3]], b = mesh.vertices[mesh.indices[t * 3 + 1]], c = mesh.vertices[mesh.indices[t * 3 + 2]];
                    points.Add(a + (b - a) * u + (c - a) * w);
                }
            }
            info.starts = points.ToArray();
            EditorUtility.SetDirty(info);
            return points.Count;
        }

        static IEnumerable<Vector3> Anchors(Scene scene)
        {
            foreach (var spawn in UnityEngine.Object.FindObjectsByType<MapSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (spawn.gameObject.scene == scene) yield return spawn.transform.position;
            foreach (var portal in UnityEngine.Object.FindObjectsByType<MapPortal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (portal.gameObject.scene == scene) yield return portal.transform.position;
        }
    }
}
