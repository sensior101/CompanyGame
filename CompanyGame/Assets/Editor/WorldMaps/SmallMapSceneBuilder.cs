using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.Daldongne;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.SceneTemplate;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.WorldMaps
{
    /// <summary>One-time scene organization and reusable, independently playable small maps.</summary>
    public static class SmallMapSceneBuilder
    {
        public const string VillageScenePath = "Assets/Scenes/daldongnaemap.unity";
        public const string ExampleScenePath = "Assets/Scenes/Maps/DaldongnePocketGarden.unity";
        public const string TemplateScenePath = "Assets/Scenes/Templates/SmallMapTemplate.unity";
        public const string TemplateAssetPath = "Assets/Scenes/Templates/SmallMap.scenetemplate";
        const string PrefabRoot = "Assets/Art/Daldongne/WarmVillage/Prefabs/";
        const string MaterialRoot = "Assets/Art/Daldongne/MapCommon/Materials/";
        static readonly Vector3 StationSpawn = new Vector3(-17.5f, 1.08f, -27f);

        public static void Build()
        {
            RequireCleanScenes();
            EnsureFolder("Assets/Scenes/Maps");
            EnsureFolder("Assets/Scenes/Templates");
            EnsureFolder(MaterialRoot.TrimEnd('/'));
            var village = SceneManager.GetSceneByPath(VillageScenePath);
            if (!village.IsValid() || !village.isLoaded)
                village = EditorSceneManager.OpenScene(VillageScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(village);
            OrganizeVillage(village);

            var walker = FindComponent<PlayerMovement>(village);
            var camera = walker ? walker.viewCamera : null;
            if (!camera) camera = FindComponent<Camera>(village);
            var sun = FindComponents<Light>(village).FirstOrDefault(light => light.type == LightType.Directional);
            var volume = FindComponents<Volume>(village).FirstOrDefault(item => item.isGlobal);
            if (!walker || !camera || !sun)
                throw new InvalidOperationException("Village needs a player, camera and directional light before creating small maps.");

            CreateSmallScene(ExampleScenePath, walker, camera, sun, volume, true);
            CreateSmallScene(TemplateScenePath, walker, camera, sun, volume, false);
            if (!AssetDatabase.LoadAssetAtPath<SceneTemplateAsset>(TemplateAssetPath))
            {
                var template = SceneTemplateService.CreateTemplateFromScene(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(TemplateScenePath), TemplateAssetPath);
                if (!template) throw new InvalidOperationException("Could not create the Unity scene template.");
                template.templateName = "Company Game - Small Map";
                template.description = "Independent map with organized hierarchy, ground, player, camera, light and default spawn. Add reusable buildings, trees and portals.";
                template.addToDefaults = true;
                // Materials, meshes, scripts and character assets remain shared between maps.
                foreach (var dependency in template.dependencies)
                    dependency.instantiationMode = TemplateInstantiationMode.Reference;
                EditorUtility.SetDirty(template);
            }
            AddBuildScene(VillageScenePath, true);
            AddBuildScene(ExampleScenePath, false);
            AssetDatabase.SaveAssets();
            SceneManager.SetActiveScene(village);
            Debug.Log("[World maps] Village hierarchy, pocket garden and Small Map scene template are ready.");
        }

        [MenuItem("Tools/Company Game/Maps/Create Small Map")]
        public static void CreateSmallMap()
        {
            RequireCleanScenes();
            var template = AssetDatabase.LoadAssetAtPath<SceneTemplateAsset>(TemplateAssetPath);
            if (!template || !template.isValid)
                throw new InvalidOperationException("Small Map template is missing. Run the world map setup first.");
            string path = EditorUtility.SaveFilePanelInProject("Create Small Map", "NewSmallMap", "unity",
                "Save the new independent map scene.", "Assets/Scenes/Maps");
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path))
                throw new InvalidOperationException("Choose a new scene path; existing scenes are never overwritten.");
            var result = SceneTemplateService.Instantiate(template, false, path);
            if (result == null || !result.scene.IsValid())
                throw new InvalidOperationException("Unity could not instantiate the Small Map template.");
            var scene = result.scene;
            var root = scene.GetRootGameObjects().FirstOrDefault(item => item.name.StartsWith("Map_", StringComparison.Ordinal));
            if (root) root.name = "Map_" + Path.GetFileNameWithoutExtension(path);
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new IOException("Could not save the new map: " + path);
            AddBuildScene(path, false);
            SceneManager.SetActiveScene(scene);
            if (root) Selection.activeGameObject = root;
            Debug.Log("[World maps] Created and enabled build scene: " + path);
        }

        static void OrganizeVillage(Scene scene)
        {
            // Once organized, a rerun must not reset a designer's spawn or authored hierarchy.
            if (scene.GetRootGameObjects().Any(item => item.name == "Map_daldongnaemap"))
            {
                Debug.Log("[World maps] Village already organized; preserving current scene content.");
                return;
            }
            var originals = scene.GetRootGameObjects();
            var layout = new Layout(scene, "daldongnaemap");
            Transform existingSpawn = null;
            foreach (var item in originals)
            {
                Transform destination;
                if (item.GetComponent<PlayerMovement>()) destination = layout.player;
                else if (item.GetComponent<Camera>()) destination = layout.cameras;
                else if (item.GetComponent<Volume>()) destination = layout.volumes;
                else if (item.GetComponent<Light>() || item.name.Contains("Practical Lights")) destination = layout.lighting;
                else if (item.name.StartsWith("Player Spawn", StringComparison.Ordinal))
                {
                    destination = layout.spawns;
                    existingSpawn = item.transform;
                }
                else if (item.name.StartsWith("Destination", StringComparison.Ordinal)) destination = layout.development;
                else destination = layout.world;
                item.transform.SetParent(destination, true);
            }
            if (!existingSpawn) existingSpawn = Child(layout.spawns, "Spawn_Station");
            existingSpawn.position = StationSpawn;
            var spawn = existingSpawn.GetComponent<MapSpawnPoint>() ?? existingSpawn.gameObject.AddComponent<MapSpawnPoint>();
            spawn.spawnId = "station";
            CreatePortal(layout.portals, "Portal_PocketGarden", StationSpawn + new Vector3(0, 0, -1.5f),
                ExampleScenePath, "default", "Pocket Garden", "POCKET GARDEN");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save organized village scene.");
        }

        static void CreateSmallScene(string path, PlayerMovement sourcePlayer, Camera sourceCamera,
            Light sourceSun, Volume sourceVolume, bool example)
        {
            if (File.Exists(path))
            {
                Debug.Log("[World maps] Keeping existing authored scene: " + path);
                return;
            }
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var layout = new Layout(scene, Path.GetFileNameWithoutExtension(path));
                var terrain = Child(layout.world, "Terrain");
                var buildings = Child(layout.world, "Buildings");
                var vegetation = Child(layout.world, "Vegetation");
                Child(layout.world, "Props");
                var ground = Material("GardenGround", new Color(.42f, .49f, .29f));
                var stone = Material("WarmStone", new Color(.7f, .65f, .52f));
                Box(terrain, "Ground", new Vector3(0, -.3f, 0), new Vector3(28, .6f, 26), ground, true);
                Box(terrain, "Path", new Vector3(0, .015f, -4), new Vector3(3.2f, .025f, 16), stone, false);
                // Low boundaries make this starter map playable without falling off the test floor.
                Box(terrain, "Boundary_North", new Vector3(0, .45f, 12.7f), new Vector3(28, .9f, .6f), stone, true);
                Box(terrain, "Boundary_South", new Vector3(0, .45f, -12.7f), new Vector3(28, .9f, .6f), stone, true);
                Box(terrain, "Boundary_West", new Vector3(-13.7f, .45f, 0), new Vector3(.6f, .9f, 26), stone, true);
                Box(terrain, "Boundary_East", new Vector3(13.7f, .45f, 0), new Vector3(.6f, .9f, 26), stone, true);

                var camera = Clone(sourceCamera.gameObject, layout.cameras, "Map Camera").GetComponent<Camera>();
                var overview = camera.GetComponent<DaldongneMapCamera>() ?? camera.gameObject.AddComponent<DaldongneMapCamera>();
                overview.enabled = true;
                overview.homeFocus = overview.focus = new Vector3(0, 1, 0);
                overview.homeZoom = overview.zoom = 18;
                overview.homeYaw = overview.yaw = -25;
                overview.homePitch = overview.pitch = 42;
                overview.ResetView();
                var player = Clone(sourcePlayer.gameObject, layout.player, "Map Player").GetComponent<PlayerMovement>();
                player.spawn = new Vector3(0, .12f, -8);
                player.transform.SetPositionAndRotation(player.spawn, Quaternion.identity);
                player.viewCamera = camera;
                player.overview = overview;
                player.enabled = true;
                player.walking = true;
                var spawn = Child(layout.spawns, "Spawn_Default").gameObject.AddComponent<MapSpawnPoint>();
                spawn.spawnId = "default";
                spawn.transform.position = player.spawn;
                var sun = Clone(sourceSun.gameObject, layout.lighting, "Map Sun").GetComponent<Light>();
                if (sourceVolume) Clone(sourceVolume.gameObject, layout.volumes, "Map Color Grade");
                RenderSettings.sun = sun;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(.42f, .5f, .6f);
                RenderSettings.ambientEquatorColor = new Color(.35f, .32f, .29f);
                RenderSettings.ambientGroundColor = new Color(.22f, .2f, .16f);
                RenderSettings.ambientIntensity = 1;
                RenderSettings.fog = false;
                if (example)
                {
                    PlacePrefab(PrefabRoot + "Buildings/Bakery.prefab", buildings, new Vector3(0, 0, 5), 10, 9);
                    PlacePrefab(PrefabRoot + "Trees/Tree_Common.prefab", vegetation, new Vector3(-8, 0, 4), 4, 4);
                    PlacePrefab(PrefabRoot + "Trees/Tree_Common.prefab", vegetation, new Vector3(8, 0, 4), 4, 4);
                    PlacePrefab(PrefabRoot + "Trees/Tree_Extra.prefab", vegetation, new Vector3(-9, 0, -5), 4, 4);
                    CreatePortal(layout.portals, "Portal_Village", new Vector3(0, .12f, -9.5f),
                        VillageScenePath, "station", "Back to Village", "VILLAGE");
                }
                if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Could not save small map: " + path);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void CreatePortal(Transform parent, string name, Vector3 position, string scenePath,
            string spawnId, string display, string label)
        {
            var host = Child(parent, name);
            host.position = position;
            var portal = host.gameObject.AddComponent<MapPortal>();
            portal.targetScenePath = scenePath;
            portal.targetSpawnId = spawnId;
            portal.displayName = display;
            portal.interactionRadius = 2.5f;
            var visuals = Child(host, "Visuals");
            var material = Material("PortalTeal", new Color(.18f, .68f, .7f));
            Box(visuals, "Left Post", new Vector3(-.8f, 1, 0), new Vector3(.16f, 2, .16f), material, false);
            Box(visuals, "Right Post", new Vector3(.8f, 1, 0), new Vector3(.16f, 2, .16f), material, false);
            Box(visuals, "Lintel", new Vector3(0, 2, 0), new Vector3(1.76f, .18f, .18f), material, false);
            var text = Child(visuals, "Destination Label").gameObject.AddComponent<TextMesh>();
            text.transform.localPosition = new Vector3(0, 2.45f, 0);
            text.text = label + "\n[E]";
            text.fontSize = 48;
            text.characterSize = .065f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(.97f, .93f, .76f);
        }

        static void PlacePrefab(string path, Transform parent, Vector3 position, float maxWidth, float maxDepth)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) throw new InvalidOperationException("Reusable prefab is missing: " + path);
            var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            var renderers = item.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Prefab has no visible geometry: " + path);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            float scale = Mathf.Min(1, maxWidth / Mathf.Max(.01f, bounds.size.x), maxDepth / Mathf.Max(.01f, bounds.size.z));
            item.transform.localScale *= scale;
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            item.transform.position += position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        static GameObject Clone(GameObject source, Transform parent, string name)
        {
            var clone = Object.Instantiate(source, parent);
            clone.name = name;
            return clone;
        }

        static GameObject Box(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material, bool collider)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localScale = size;
            item.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(item.GetComponent<Collider>());
            return item;
        }

        static Material Material(string name, Color color)
        {
            string path = MaterialRoot + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP Lit shader is required for map materials.");
            material = new Material(shader) { name = name, color = color };
            material.SetFloat("_Smoothness", .15f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Transform Child(Transform parent, string name)
        {
            var item = new GameObject(name).transform;
            item.SetParent(parent, false);
            return item;
        }

        static IEnumerable<T> FindComponents<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<T>(true));
        static T FindComponent<T>(Scene scene) where T : Component => FindComponents<T>(scene).FirstOrDefault();

        static void RequireCleanScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before creating or organizing maps.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save or discard loaded scene changes before creating or organizing maps.");
        }

        static void AddBuildScene(string path, bool first)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(item => item.path == path || item.path == TemplateScenePath);
            if (first) scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            else scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        sealed class Layout
        {
            public readonly Transform world, player, portals, spawns, cameras, lighting, volumes, development;
            public Layout(Scene scene, string name)
            {
                var root = new GameObject("Map_" + name);
                SceneManager.MoveGameObjectToScene(root, scene);
                Child(root.transform, "00_Systems");
                world = Child(root.transform, "10_World");
                var gameplay = Child(root.transform, "20_Gameplay");
                player = Child(gameplay, "Player");
                Child(gameplay, "NPCs");
                Child(gameplay, "Interactables");
                portals = Child(gameplay, "Portals");
                spawns = Child(gameplay, "SpawnPoints");
                var presentation = Child(root.transform, "30_Presentation");
                cameras = Child(presentation, "Cameras");
                lighting = Child(presentation, "Lighting");
                volumes = Child(presentation, "Volumes");
                development = Child(root.transform, "90_Development");
            }
        }
    }
}
