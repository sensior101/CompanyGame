using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CompanyGame.Daldongne;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.SceneTemplate;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompanyGame.Editor.WorldMaps
{
    /// <summary>Checks serialized map references, reusable prefab instances and native template creation.</summary>
    public static class MapAssetValidation
    {
        const string ReportPath = "Temp/WorldMapMigration/asset-validation.txt";
        const string PartsPath = "Assets/Art/Daldongne/WarmVillage/Prefabs/";

        [MenuItem("Tools/Company Game/Maps/Validate Maps")]
        static void ValidateMenu() => Debug.Log(ValidateMaps());

        public static string ValidateMaps()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before validating serialized map assets.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save or discard scene changes before validating map assets.");

            var setup = EditorSceneManager.GetSceneManagerSetup();
            var report = new StringBuilder();
            var errors = new List<string>();
            string testPath = "Assets/Scenes/Templates/__MapValidation_" + Guid.NewGuid().ToString("N") + ".unity";
            // The path is newly minted and checked before any write; cleanup owns only this asset.
            if (File.Exists(testPath) || AssetDatabase.LoadMainAssetAtPath(testPath))
                throw new IOException("Validation output path unexpectedly exists: " + testPath);
            report.AppendLine("Map asset validation | " + DateTime.UtcNow.ToString("u"));
            try
            {
                var scenes = new Dictionary<string, Scene>(StringComparer.Ordinal);
                foreach (string path in new[] { SmallMapSceneBuilder.VillageScenePath,
                    SmallMapSceneBuilder.ExampleScenePath, SmallMapSceneBuilder.TemplateScenePath })
                {
                    if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(path))
                    {
                        errors.Add("Missing scene asset: " + path);
                        continue;
                    }
                    var scene = Open(path);
                    scenes.Add(path, scene);
                    CheckScene(scene, report, errors);
                }
                foreach (var scene in scenes.Values)
                    CheckPortals(scene, scenes, report, errors);
                CheckVillagePrefab(report, errors);
                CheckBuildScenes(errors);
                if (scenes.TryGetValue(SmallMapSceneBuilder.ExampleScenePath, out var example))
                    CaptureIsolated(example, "pocket-garden.png");
                if (scenes.TryGetValue(SmallMapSceneBuilder.TemplateScenePath, out var templateScene))
                    CaptureIsolated(templateScene, "small-map-template.png");
                CheckTemplate(testPath, report, errors);
            }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
            }
            finally
            {
                try { EditorSceneManager.RestoreSceneManagerSetup(setup); }
                catch (Exception exception) { errors.Add("Could not restore editor scene setup: " + exception.Message); }
                try
                {
                    var temporary = SceneManager.GetSceneByPath(testPath);
                    if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
                    if (File.Exists(testPath) || AssetDatabase.LoadMainAssetAtPath(testPath))
                    {
                        if (!AssetDatabase.DeleteAsset(testPath)) errors.Add("Could not delete generated validation scene: " + testPath);
                        else report.AppendLine("Temporary template instance deleted: " + testPath);
                    }
                }
                catch (Exception exception) { errors.Add("Validation scene cleanup failed: " + exception.Message); }
                report.Insert(0, errors.Count == 0 ? "PASS\n" : "FAIL: " + errors.Count + " problem(s)\n");
                foreach (string error in errors) report.AppendLine("ERROR: " + error);
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, report.ToString());
            }
            return report.ToString();
        }

        static Scene Open(string path)
        {
            var scene = SceneManager.GetSceneByPath(path);
            return scene.IsValid() && scene.isLoaded ? scene : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        static void CheckScene(Scene scene, StringBuilder report, List<string> errors)
        {
            var roots = scene.GetRootGameObjects();
            var objects = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Select(item => item.gameObject).ToArray();
            int before = errors.Count;
            CheckReferences(objects, scene.path, errors);
            var players = Components<PlayerMovement>(scene).Where(item => item.isActiveAndEnabled).ToArray();
            var cameras = Components<Camera>(scene).Where(item => item.isActiveAndEnabled).ToArray();
            if (players.Length != 1) errors.Add(scene.path + ": expected one active player, found " + players.Length);
            if (cameras.Length != 1) errors.Add(scene.path + ": expected one active camera, found " + cameras.Length);
            if (players.Length == 1)
            {
                var player = players[0];
                if (!player.viewCamera || player.viewCamera.gameObject.scene != scene)
                    errors.Add(scene.path + ": player View Camera does not reference this scene.");
                if (!player.overview || player.overview.gameObject.scene != scene ||
                    player.overview.GetComponent<Camera>() != player.viewCamera)
                    errors.Add(scene.path + ": player Overview does not reference the local view camera.");
                if (!player.GetComponent<CharacterController>()) errors.Add(scene.path + ": player has no CharacterController.");
            }
            var spawns = Components<MapSpawnPoint>(scene).ToArray();
            if (spawns.Length == 0) errors.Add(scene.path + ": missing spawn point.");
            foreach (var group in spawns.GroupBy(item => item.spawnId))
            {
                if (string.IsNullOrWhiteSpace(group.Key)) errors.Add(scene.path + ": empty spawn ID.");
                if (group.Count() != 1) errors.Add(scene.path + ": duplicate spawn ID '" + group.Key + "'.");
                if (!group.First().isActiveAndEnabled) errors.Add(scene.path + ": inactive spawn '" + group.Key + "'.");
            }
            var mapRoot = roots.FirstOrDefault(item => item.name.StartsWith("Map_", StringComparison.Ordinal));
            if (roots.Length != 1 || !mapRoot) errors.Add(scene.path + ": expected a single Map_ hierarchy root.");
            else foreach (string required in new[] { "00_Systems", "10_World", "20_Gameplay/Player", "20_Gameplay/NPCs",
                "20_Gameplay/Interactables", "20_Gameplay/Portals", "20_Gameplay/SpawnPoints", "30_Presentation/Cameras",
                "30_Presentation/Lighting", "30_Presentation/Volumes", "90_Development" })
                if (!mapRoot.transform.Find(required)) errors.Add(scene.path + ": missing hierarchy group " + required);
            if (scene.path != SmallMapSceneBuilder.VillageScenePath)
                foreach (var item in objects)
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(item) &&
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item) == WorldMapTools.VillagePrefab)
                        errors.Add(scene.path + ": contains entire village prefab instead of independent pieces.");
            report.AppendLine(scene.path + ": objects=" + objects.Length + ", players=" + players.Length +
                ", cameras=" + cameras.Length + ", spawns=[" + string.Join(",", spawns.Select(item => item.spawnId)) +
                "], portals=" + Components<MapPortal>(scene).Count() + ", issues=" + (errors.Count - before));
        }

        static void CheckReferences(IEnumerable<GameObject> objects, string context, List<string> errors)
        {
            foreach (var item in objects)
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item);
                if (missing > 0) errors.Add(context + "/" + item.name + ": " + missing + " missing script(s).");
                foreach (var filter in item.GetComponents<MeshFilter>())
                    if (!filter.sharedMesh) errors.Add(context + "/" + item.name + ": missing MeshFilter mesh.");
                foreach (var renderer in item.GetComponents<SkinnedMeshRenderer>())
                    if (!renderer.sharedMesh) errors.Add(context + "/" + item.name + ": missing skinned mesh.");
                foreach (var collider in item.GetComponents<MeshCollider>())
                    if (!collider.sharedMesh) errors.Add(context + "/" + item.name + ": missing collision mesh.");
                foreach (var renderer in item.GetComponents<Renderer>())
                {
                    var materials = renderer.sharedMaterials;
                    if (materials.Length == 0 || materials.Any(material => !material))
                        errors.Add(context + "/" + item.name + ": missing renderer material.");
                    foreach (var material in materials.Where(material => material))
                        if (!material.shader || material.shader.name == "Hidden/InternalErrorShader")
                            errors.Add(context + "/" + item.name + ": missing/error material shader.");
                }
                foreach (var component in item.GetComponents<Component>().Where(component => component))
                {
                    var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();
                    while (property.NextVisible(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference &&
                            !property.objectReferenceValue && property.objectReferenceEntityIdValue != EntityId.None)
                            errors.Add(context + "/" + item.name + ": broken reference " + property.propertyPath);
                    serialized.Dispose();
                }
            }
        }

        static void CheckPortals(Scene scene, Dictionary<string, Scene> scenes, StringBuilder report, List<string> errors)
        {
            foreach (var portal in Components<MapPortal>(scene))
            {
                string path = portal.targetScenePath ?? string.Empty;
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".unity", StringComparison.Ordinal) ||
                    !AssetDatabase.LoadAssetAtPath<SceneAsset>(path))
                {
                    errors.Add(scene.path + "/" + portal.name + ": invalid target scene path '" + path + "'.");
                    continue;
                }
                if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == path))
                    errors.Add(scene.path + "/" + portal.name + ": target scene is not enabled in build settings.");
                Scene target = scenes.TryGetValue(path, out var existing) ? existing : Open(path);
                if (!MapSpawnPoint.TryFind(target, portal.targetSpawnId, out _, out var error))
                    errors.Add(scene.path + "/" + portal.name + ": " + error);
                report.AppendLine("Portal: " + scene.name + "/" + portal.name + " -> " + path + " #" + portal.targetSpawnId);
            }
        }

        static void CheckVillagePrefab(StringBuilder report, List<string> errors)
        {
            var root = PrefabUtility.LoadPrefabContents(WorldMapTools.VillagePrefab);
            try
            {
                CheckReferences(root.GetComponentsInChildren<Transform>(true).Select(item => item.gameObject), WorldMapTools.VillagePrefab, errors);
                var buildings = root.transform.Find("10_Buildings");
                var trees = root.transform.Find("20_Nature/Trees");
                if (!buildings || buildings.childCount != 13) errors.Add("Village prefab: expected 13 building groups.");
                if (!trees || trees.childCount != 48) errors.Add("Village prefab: expected 48 tree instances.");
                int buildingInstances = 0, treeInstances = 0;
                var treeSources = new HashSet<string>(StringComparer.Ordinal);
                if (buildings) foreach (Transform building in buildings)
                {
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(building.gameObject);
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(building.gameObject) || !path.StartsWith(PartsPath + "Buildings/", StringComparison.Ordinal))
                        errors.Add("Village building is not a reusable nested prefab: " + building.name);
                    else buildingInstances++;
                }
                if (trees) foreach (Transform tree in trees)
                {
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(tree.gameObject);
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(tree.gameObject) ||
                        (path != PartsPath + "Trees/Tree_Common.prefab" && path != PartsPath + "Trees/Tree_Extra.prefab"))
                        errors.Add("Village tree is not a shared nested tree prefab: " + tree.name);
                    else { treeInstances++; treeSources.Add(path); }
                }
                if (treeSources.Count != 2) errors.Add("Village prefab: expected exactly two shared tree prefab sources.");
                report.AppendLine("Village nested prefabs: buildings=" + buildingInstances + ", trees=" + treeInstances + ", tree source assets=" + treeSources.Count);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void CheckBuildScenes(List<string> errors)
        {
            var enabled = EditorBuildSettings.scenes.Where(item => item.enabled).ToArray();
            if (enabled.Length == 0 || enabled[0].path != SmallMapSceneBuilder.VillageScenePath)
                errors.Add("Village must be the first enabled build scene.");
            if (!enabled.Any(item => item.path == SmallMapSceneBuilder.ExampleScenePath)) errors.Add("Example scene is not enabled for builds.");
            if (enabled.Any(item => item.path == SmallMapSceneBuilder.TemplateScenePath)) errors.Add("Authoring template must not be enabled for builds.");
        }

        static void CheckTemplate(string testPath, StringBuilder report, List<string> errors)
        {
            var template = AssetDatabase.LoadAssetAtPath<SceneTemplateAsset>(SmallMapSceneBuilder.TemplateAssetPath);
            if (!template || !template.isValid)
            {
                errors.Add("Native SmallMap.scenetemplate is missing or invalid.");
                return;
            }
            if (AssetDatabase.GetAssetPath(template.templateScene) != SmallMapSceneBuilder.TemplateScenePath)
                errors.Add("Native template references the wrong source scene.");
            foreach (var dependency in template.dependencies)
            {
                if (!dependency.dependency) errors.Add("Native template has a missing dependency reference.");
                if (dependency.instantiationMode != TemplateInstantiationMode.Reference)
                    errors.Add("Native template clones a shared dependency: " + AssetDatabase.GetAssetPath(dependency.dependency));
            }
            if (errors.Any(error => error.StartsWith("Native template", StringComparison.Ordinal))) return;
            var result = SceneTemplateService.Instantiate(template, true, testPath);
            if (result == null || !result.scene.IsValid())
            {
                errors.Add("Unity failed to instantiate the native scene template.");
                return;
            }
            // Reference-only templates may initially instantiate as an unsaved scene.
            if (!EditorSceneManager.SaveScene(result.scene, testPath))
            {
                errors.Add("Could not save the generated validation scene.");
                return;
            }
            CheckScene(result.scene, report, errors);
            report.AppendLine("Native template instantiation: created and inspected a new scene with shared dependencies (" + template.dependencies.Length + ").");
            EditorSceneManager.CloseScene(result.scene, true);
        }

        static void CaptureIsolated(Scene scene, string fileName)
        {
            var camera = Components<Camera>(scene).First();
            var active = SceneManager.GetActiveScene();
            ulong oldSceneMask = EditorSceneManager.GetSceneCullingMask(scene);
            ulong oldCameraMask = camera.overrideSceneCullingMask;
            try
            {
                ulong mask = EditorSceneManager.CalculateAvailableSceneCullingMask();
                EditorSceneManager.SetSceneCullingMask(scene, mask);
                camera.overrideSceneCullingMask = mask;
                SceneManager.SetActiveScene(scene);
                WorldMapTools.CaptureScene(fileName);
            }
            finally
            {
                camera.overrideSceneCullingMask = oldCameraMask;
                EditorSceneManager.SetSceneCullingMask(scene, oldSceneMask);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
        }

        static IEnumerable<T> Components<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    }
}
