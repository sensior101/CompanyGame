using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.WorldMaps
{
    /// <summary>Preserves source geometry while organizing the legacy imported village.</summary>
    public static class WorldMapTools
    {
        public const string VillagePrefab = "Assets/Art/Daldongne/WarmVillage/DaldongneWarmTown.prefab";
        const string PartsFolder = "Assets/Art/Daldongne/WarmVillage/Prefabs";
        const string ReportFolder = "Temp/WorldMapMigration";
        static readonly string[] Buildings = { "Bakery", "Cafe", "CityHall", "Convenience", "Snack", "Hamburger", "Laundry", "Gosiwon", "Home_A", "Home_B", "Home_C", "Home_D", "Station" };

        [MenuItem("Tools/Company Game/Maps/Organize Legacy Village")]
        public static void OrganizeVillage()
        {
            RequireClean();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(VillagePrefab);
            if (!asset) throw new InvalidOperationException("Village prefab was not found.");
            if (asset.transform.Find("10_Buildings"))
            {
                Debug.Log("The village is already organized; existing edits were preserved.");
                return;
            }
            Directory.CreateDirectory(ReportFolder + "/backups");
            foreach (var path in new[] { VillagePrefab, VillagePrefab + ".meta", SmallMapSceneBuilder.VillageScenePath, SmallMapSceneBuilder.VillageScenePath + ".meta", "ProjectSettings/EditorBuildSettings.asset" })
            {
                var backup = ReportFolder + "/backups/" + Path.GetFileName(path);
                if (!File.Exists(backup)) File.Copy(path, backup);
            }
            CaptureScene("village-before.png");
            EnsureFolder(PartsFolder + "/Buildings");
            EnsureFolder(PartsFolder + "/Trees");
            var root = PrefabUtility.LoadPrefabContents(VillagePrefab);
            try
            {
                var originals = root.transform.Cast<Transform>().ToArray();
                if (originals.Any(t => t.childCount != 0))
                    throw new InvalidOperationException("This migration expects the original flat source. Review the existing hierarchy first.");
                var before = new GeometrySnapshot(root);
                var terrain = Group(root.transform, "00_Terrain");
                var buildings = Group(root.transform, "10_Buildings");
                var nature = Group(root.transform, "20_Nature");
                var props = Group(root.transform, "30_Props");
                var colliders = Group(root.transform, "40_Colliders");
                var claimed = new HashSet<Transform>();
                foreach (var building in Buildings)
                {
                    var pieces = originals.Where(t => t.name.StartsWith(building + "_", StringComparison.Ordinal) ||
                        (building == "CityHall" && t.name.StartsWith("Building_CityHall_", StringComparison.Ordinal))).ToArray();
                    if (pieces.Length == 0) continue;
                    var holder = Group(buildings, building);
                    holder.position = GroundPivot(pieces);
                    foreach (var piece in pieces)
                    {
                        piece.SetParent(Group(holder, BuildingSection(piece.name)), true);
                        claimed.Add(piece);
                    }
                    Extract(holder, PartsFolder + "/Buildings/" + building + ".prefab");
                }
                var trees = Group(nature, "Trees");
                var treeGroups = originals.Select(t => new { transform = t, match = Regex.Match(t.name, @"^(Tree_(?:Extra_)?\d+)_(.+)$") })
                    .Where(item => item.match.Success).GroupBy(item => item.match.Groups[1].Value).OrderBy(items => items.Key);
                foreach (var tree in treeGroups)
                {
                    var pieces = tree.Select(item => item.transform).ToArray();
                    var holder = Group(trees, tree.Key);
                    var pivot = GroundPivot(pieces);
                    var trunk = tree.FirstOrDefault(item => item.match.Groups[2].Value == "trunk");
                    if (trunk != null) { pivot.x = trunk.transform.position.x; pivot.z = trunk.transform.position.z; }
                    holder.position = pivot;
                    foreach (var item in tree.OrderBy(item => item.match.Groups[2].Value, StringComparer.Ordinal))
                    {
                        item.transform.name = item.match.Groups[2].Value;
                        item.transform.SetParent(holder, true);
                        claimed.Add(item.transform);
                    }
                    Extract(holder, PartsFolder + "/Trees/" + (tree.Key.StartsWith("Tree_Extra_", StringComparison.Ordinal) ? "Tree_Extra" : "Tree_Common") + ".prefab");
                }
                foreach (var piece in originals.Where(t => !claimed.Contains(t)))
                {
                    string name = piece.name;
                    Transform parent;
                    if (name.StartsWith("Collider_", StringComparison.Ordinal)) parent = colliders;
                    else if (!piece.GetComponent<MeshFilter>()) parent = Group(props, "SourceHelpers");
                    else if (name.StartsWith("Terrain", StringComparison.Ordinal)) parent = Group(terrain, "Ground");
                    else if (Regex.IsMatch(name, @"^(Wall|WallCap)_")) parent = Group(terrain, "Walls/" + Token(name, 1));
                    else if (Regex.IsMatch(name, @"^(Rock|Water|Foam)")) parent = Group(terrain, "Coast");
                    else if (Regex.IsMatch(name, @"^(Walk_Stair|Stair|Step|Rail|Support_Stair)")) parent = Group(terrain, "Stairs/" + RouteKey(name));
                    else if (Regex.IsMatch(name, @"^(Walk|Bridge|PavingJoint|Curb|Road|CausewayGuard)")) parent = Group(terrain, "RoadsAndPaths/" + Token(name, 0));
                    else if (Regex.IsMatch(name, @"^(Garden|Planter|Soil|Shrub|Flower|WarmPot)")) parent = Group(nature, "GardensAndPlanters/" + Token(name, 0));
                    else if (Regex.IsMatch(name, @"^(TownBus|RedCar|CreamVan|YellowCar)")) parent = Group(props, "Vehicles/" + Token(name, 0));
                    else if (name.StartsWith("WarmLamp_", StringComparison.Ordinal)) parent = Group(props, "StreetLights/WarmLamp_" + Token(name, 1));
                    else if (Regex.IsMatch(name, @"^(StationBench|RestBench)")) parent = Group(props, "Benches/" + Token(name, 0));
                    else parent = Group(props, Token(name, 0));
                    piece.SetParent(parent, true);
                }
                before.AssertUnchanged(root);
                if (!PrefabUtility.SaveAsPrefabAsset(root, VillagePrefab)) throw new InvalidOperationException("Could not save organized village prefab.");
                File.WriteAllText(ReportFolder + "/geometry-validation.txt", "PASS: all original renderer, mesh, material, collider and world-transform references preserved.\n" + before.Summary + "\nBuilding prefabs: " + buildings.childCount + "\nTree instances: " + trees.childCount + "\nShared tree prefabs: 2\n");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            CaptureScene("village-after.png");
            Debug.Log(File.ReadAllText(ReportFolder + "/geometry-validation.txt"));
        }

        static string BuildingSection(string name)
        {
            if (Regex.IsMatch(name, "Window|Door|Awning", RegexOptions.IgnoreCase)) return "20_DoorsAndWindows";
            if (Regex.IsMatch(name, "Roof|Chimney|Gutter", RegexOptions.IgnoreCase)) return "10_Roof";
            if (Regex.IsMatch(name, "Wall|Base|Plinth|Floor|Foundation|Beam|Pillar|Column|Stair|Step|Balcony|Rail|Stucco", RegexOptions.IgnoreCase)) return "00_Structure";
            if (Regex.IsMatch(name, "Sign|Letter|Logo|Board|Banner|Flag", RegexOptions.IgnoreCase)) return "30_Signs";
            return "40_Details";
        }

        static string Token(string name, int index)
        {
            var tokens = Regex.Replace(name, @"\.\d+$", "").Split('_');
            return tokens[Math.Min(index, tokens.Length - 1)];
        }

        static string RouteKey(string name)
        {
            var match = Regex.Match(name, "BridgeNorth|BridgeSouth|CentralCrest|CentralLower|CentralMiddle|CityHall|ShopCenter|ShopWest|Subway|WestCrest|WestLower|WestMiddle|East");
            return match.Success ? match.Value : Token(name, 0);
        }

        static Transform Group(Transform parent, string path)
        {
            foreach (string name in path.Split('/'))
            {
                var next = parent.Find(name);
                if (!next)
                {
                    next = new GameObject(name).transform;
                    next.SetParent(parent, false);
                }
                parent = next;
            }
            return parent;
        }

        static Vector3 GroundPivot(Transform[] pieces)
        {
            var renderers = pieces.SelectMany(piece => piece.GetComponentsInChildren<Renderer>(true)).ToArray();
            if (renderers.Length == 0) return pieces[0].position;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        static void Extract(Transform holder, string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset)
            {
                // Save a clone so the original component identities remain intact.
                var clone = Object.Instantiate(holder.gameObject);
                clone.name = Path.GetFileNameWithoutExtension(path);
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                try { asset = PrefabUtility.SaveAsPrefabAsset(clone, path); }
                finally { Object.DestroyImmediate(clone); }
                if (!asset) throw new InvalidOperationException("Could not create " + path);
            }
            PrefabUtility.ConvertToPrefabInstance(holder.gameObject, asset, new ConvertToPrefabInstanceSettings
            {
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                changeRootNameToAssetName = false,
                componentsNotMatchedBecomesOverride = true,
                gameObjectsNotMatchedBecomesOverride = true,
                recordPropertyOverridesOfMatches = true,
                logInfo = false
            }, InteractionMode.AutomatedAction);
        }

        static void EnsureFolder(string path)
        {
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void RequireClean()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your open scenes before organizing the map.");
        }

        sealed class GeometrySnapshot
        {
            readonly Dictionary<Transform, Matrix4x4> transforms;
            readonly Dictionary<MeshFilter, Mesh> meshes;
            readonly Dictionary<Renderer, Material[]> materials;
            readonly Dictionary<Collider, string> colliders;
            readonly Dictionary<Renderer, bool> enabled;
            public string Summary => $"Transforms: {transforms.Count}; mesh filters: {meshes.Count}; renderers: {materials.Count}; colliders: {colliders.Count}";
            public GeometrySnapshot(GameObject root)
            {
                transforms = root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.localToWorldMatrix);
                meshes = root.GetComponentsInChildren<MeshFilter>(true).ToDictionary(m => m, m => m.sharedMesh);
                materials = root.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.sharedMaterials);
                enabled = materials.Keys.ToDictionary(r => r, r => r.enabled);
                colliders = root.GetComponentsInChildren<Collider>(true).ToDictionary(c => c, ColliderState);
            }
            public void AssertUnchanged(GameObject root)
            {
                foreach (var pair in transforms)
                {
                    if (!pair.Key) throw new InvalidOperationException("A source object was removed by the migration.");
                    var current = pair.Key.localToWorldMatrix;
                    for (int i = 0; i < 16; i++)
                        if (Mathf.Abs(current[i] - pair.Value[i]) > .0005f) throw new InvalidOperationException("World transform changed: " + pair.Key.name);
                }
                foreach (var pair in meshes)
                    if (!pair.Key || pair.Key.sharedMesh != pair.Value) throw new InvalidOperationException("Source mesh changed.");
                foreach (var pair in materials)
                    if (!pair.Key || !pair.Key.sharedMaterials.SequenceEqual(pair.Value) || pair.Key.enabled != enabled[pair.Key]) throw new InvalidOperationException("Source renderer changed.");
                foreach (var pair in colliders)
                    if (!pair.Key || ColliderState(pair.Key) != pair.Value) throw new InvalidOperationException("Source collider changed: " + pair.Key);
                if (root.GetComponentsInChildren<MeshFilter>(true).Length != meshes.Count || root.GetComponentsInChildren<Collider>(true).Length != colliders.Count)
                    throw new InvalidOperationException("Geometry count changed.");
            }
            static string ColliderState(Collider c)
            {
                var common = $"{c.GetType().Name}|{c.enabled}|{c.isTrigger}|{c.contactOffset:R}|{c.sharedMaterial?.GetEntityId()}|{c.includeLayers.value}|{c.excludeLayers.value}|{c.layerOverridePriority}";
                if (c is MeshCollider mesh) return common + $"|{mesh.sharedMesh?.GetEntityId()}|{mesh.convex}|{mesh.cookingOptions}";
                if (c is BoxCollider box) return common + "|" + box.center.ToString("R") + "|" + box.size.ToString("R");
                throw new InvalidOperationException("Add preservation validation for collider: " + c.GetType().Name);
            }
        }

        public static void CaptureScene(string fileName)
        {
            var scene = SceneManager.GetActiveScene();
            var camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            if (!camera) return;
            Directory.CreateDirectory(ReportFolder);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1100, 900, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(1100, 900, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1100, 900), 0, 0);
                texture.Apply();
                File.WriteAllBytes(ReportFolder + "/" + fileName, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(texture);
            }
        }

    }
}
