using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.Daldongne;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>One-time, explicit editor migration. Run only from a clean Edit Mode.</summary>
public static class PlayerMovementMigration
{
    const string MovementPath = "Assets/Scripts/Player/PlayerMovement.cs";
    const string MovementGuid = "bc80122d6304a1047b34de579caa4e9c";
    static readonly string[] Prefabs = {
        "Assets/Art/Daldongne/Players/PlayerFemale.prefab",
        "Assets/Art/Daldongne/Players/PlayerMale.prefab"
    };
    static readonly string[] Scenes = {
        "Assets/Scenes/daldongnaemap.unity",
        "Assets/Scenes/Maps/DaldongnePocketGarden.unity",
        "Assets/Scenes/Templates/SmallMapTemplate.unity"
    };

    sealed class MovementState
    {
        public bool enabled, walking;
        public Camera camera;
        public DaldongneMapCamera overview;
        public Vector3 spawn;
        public float speed;

        public MovementState(PlayerMovement movement)
        {
            var source = new SerializedObject(movement);
            enabled = source.FindProperty("m_Enabled").boolValue;
            camera = source.FindProperty("viewCamera").objectReferenceValue as Camera;
            overview = source.FindProperty("overview").objectReferenceValue as DaldongneMapCamera;
            spawn = source.FindProperty("spawn").vector3Value;
            speed = source.FindProperty("moveSpeed").floatValue;
            walking = source.FindProperty("walking").boolValue;
        }

        public void Apply(PlayerMovement movement)
        {
            var target = new SerializedObject(movement);
            target.FindProperty("m_Enabled").boolValue = enabled;
            target.FindProperty("viewCamera").objectReferenceValue = camera;
            target.FindProperty("overview").objectReferenceValue = overview;
            target.FindProperty("spawn").vector3Value = spawn;
            target.FindProperty("moveSpeed").floatValue = speed;
            target.FindProperty("walking").boolValue = walking;
            target.ApplyModifiedPropertiesWithoutUndo();
            Check(movement);
        }

        public void Check(PlayerMovement movement)
        {
            Require(movement.enabled == enabled && movement.walking == walking &&
                movement.viewCamera == camera && movement.overview == overview &&
                movement.spawn == spawn && movement.moveSpeed == speed,
                "Migration did not preserve movement fields on " + movement.name);
        }
    }

    sealed class Reference
    {
        public Component owner;
        public string path;
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Guard()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before migrating movement.");
        Require(!EditorApplication.isCompiling && !EditorUtility.scriptCompilationFailed, "Finish a successful compilation first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save or discard user scene changes before migrating movement.");
        Require(AssetDatabase.AssetPathToGUID(MovementPath) == MovementGuid,
            "The original PlayerMovement script GUID must be preserved.");
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(MovementPath);
        Require(script && script.GetClass() == typeof(PlayerMovement), "PlayerMovement has not compiled.");
    }

    static Component[] Components(GameObject[] roots)
    {
        return roots.SelectMany(root => root.GetComponentsInChildren<Component>(true)).Where(item => item).ToArray();
    }

    static int Convert(GameObject[] roots)
    {
        var components = Components(roots);
        var old = components.OfType<DaldongneVillageWalker>().ToArray();
        foreach (var legacy in old)
        {
            var host = legacy.gameObject;
            Require(host.GetComponents<PlayerMovement>().Length == 1, "Multiple movement controllers on " + host.name);
            // Current authored controllers belong directly to their scene/prefab.
            // Do not turn a future inherited component into an unexplained override.
            Require(!PrefabUtility.IsPartOfPrefabInstance(legacy),
                "Migrate the source prefab first for inherited movement on " + host.name);
            var state = new MovementState(legacy);
            var transform = host.transform;
            Vector3 position = transform.localPosition, scale = transform.localScale;
            Quaternion rotation = transform.localRotation;
            var motor = host.GetComponent<CharacterController>();
            string motorBefore = EditorJsonUtility.ToJson(motor);
            var references = new List<Reference>();
            foreach (var owner in components)
            {
                if (!owner || owner == legacy) continue;
                using (var serialized = new SerializedObject(owner))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == legacy)
                            references.Add(new Reference { owner = owner, path = property.propertyPath });
                }
            }

            // The old type is only a compatibility adapter. Destroy it before
            // adding the real controller so no object ever has two controllers.
            Object.DestroyImmediate(legacy);
            var movement = host.AddComponent<PlayerMovement>();
            state.Apply(movement);
            foreach (var reference in references)
            {
                var serialized = new SerializedObject(reference.owner);
                var property = serialized.FindProperty(reference.path);
                Require(property != null, "Lost reference property: " + reference.path);
                property.objectReferenceValue = movement;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Require(new SerializedObject(reference.owner).FindProperty(reference.path).objectReferenceValue == movement,
                    "Could not redirect a movement reference: " + reference.path);
                EditorUtility.SetDirty(reference.owner);
            }
            Require(position == transform.localPosition && scale == transform.localScale && rotation == transform.localRotation,
                "Migration changed the player's authored transform.");
            Require(motor && EditorJsonUtility.ToJson(motor) == motorBefore, "Migration changed CharacterController settings.");
            EditorUtility.SetDirty(movement);
        }
        return old.Length;
    }

    static object Check(GameObject[] roots, string path, bool sceneAsset)
    {
        var allObjects = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Select(item => item.gameObject).ToArray();
        foreach (var item in allObjects)
            Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item) == 0, "Missing script: " + path + "/" + item.name);
        var components = Components(roots);
        Require(!components.OfType<DaldongneVillageWalker>().Any(), "Legacy movement remains in " + path);
        var players = components.OfType<PlayerMovement>().ToArray();
        Require(players.Length == 1 && players[0].GetType() == typeof(PlayerMovement), "Expected one actual PlayerMovement in " + path);
        var player = players[0];
        Require(player.GetComponents<PlayerMovement>().Length == 1 && player.GetComponent<CharacterController>(), "Invalid controller setup in " + path);
        Require(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(player)) == MovementPath, "Wrong movement script on " + path);
        var appearance = player.GetComponent<DaldongnePlayerAppearance>();
        Require(appearance && appearance.female && appearance.male, "Lost player visual references in " + path);
        if (sceneAsset)
        {
            Require(player.viewCamera && player.overview, "Lost camera reference in " + path);
            Require(player.viewCamera.gameObject.scene == player.gameObject.scene &&
                player.overview.gameObject.scene == player.gameObject.scene &&
                player.overview.GetComponent<Camera>() == player.viewCamera, "Camera no longer belongs to the player's scene in " + path);
            Require(SceneLoadManager.TryGetScenePlayer(player.gameObject.scene, out var resolved, out var error) && resolved == player,
                "SceneLoadManager does not resolve PlayerMovement: " + error);
        }
        return new { path, movement = player.GetType().Name, scriptGuid = MovementGuid,
            controllerCount = 1, moveSpeed = player.moveSpeed, spawn = player.spawn,
            walking = player.walking, cameraLinked = !!player.viewCamera, missingScripts = 0 };
    }

    public static object Migrate()
    {
        Guard();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var records = new List<object>();
        int changed = 0;
        try
        {
            foreach (string path in Prefabs)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int count = Convert(new[] { root });
                    records.Add(Check(new[] { root }, path, false));
                    if (count > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); changed += count; }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (string path in Scenes)
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    int count = Convert(scene.GetRootGameObjects());
                    records.Add(Check(scene.GetRootGameObjects(), path, true));
                    if (count > 0)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        Require(EditorSceneManager.SaveScene(scene), "Could not save " + path);
                        changed += count;
                    }
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            AssetDatabase.SaveAssets();
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        var result = new { passed = true, migratedComponents = changed, prefabs = 2, scenes = 3, records };
        Directory.CreateDirectory("Temp/PlayerMovementMigration");
        File.WriteAllText("Temp/PlayerMovementMigration/migration.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
        return result;
    }

    public static object Verify()
    {
        Guard();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var records = new List<object>();
        try
        {
            foreach (string path in Prefabs)
                records.Add(Check(new[] { AssetDatabase.LoadAssetAtPath<GameObject>(path) }, path, false));
            foreach (string path in Scenes)
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try { records.Add(Check(scene.GetRootGameObjects(), path, true)); }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        return new { passed = true, prefabs = 2, scenes = 3, records };
    }

    // Run once in Play Mode to exercise camera handoff and R/reset API, then
    // restore the player's position, rotation and walking mode for the user.
    public static object VerifyRuntime()
    {
        Require(EditorApplication.isPlaying, "Enter Play Mode for runtime verification.");
        var scene = SceneManager.GetActiveScene();
        Require(SceneLoadManager.TryGetScenePlayer(scene, out var player, out var error), error);
        Require(player.GetType() == typeof(PlayerMovement), "Runtime still uses the legacy movement adapter.");
        Require(player.GetComponents<PlayerMovement>().Length == 1, "Duplicate runtime movement controllers.");
        var motor = player.GetComponent<CharacterController>();
        Vector3 position = player.transform.position;
        Quaternion rotation = player.transform.rotation;
        bool walking = player.walking;
        try
        {
            player.SetWalking(false);
            Require(!player.walking && player.overview.enabled, "Overview handoff failed.");
            player.SetWalking(true);
            Require(player.walking && !player.overview.enabled && player.viewCamera.orthographic &&
                Mathf.Approximately(player.viewCamera.orthographicSize, 8), "Walking camera handoff failed.");
            player.ResetToSpawn();
            Require((player.transform.position - player.spawn).sqrMagnitude < .000001f && motor.enabled, "ResetToSpawn failed.");
            return new { passed = true, movement = player.GetType().Name, scene = scene.path,
                walkingCamera = true, overviewCamera = true, resetToSpawn = true, controllerCount = 1 };
        }
        finally
        {
            motor.enabled = false;
            player.transform.SetPositionAndRotation(position, rotation);
            motor.enabled = true;
            Physics.SyncTransforms();
            player.SetWalking(walking);
        }
    }
}
