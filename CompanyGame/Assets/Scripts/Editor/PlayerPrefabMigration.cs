using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-time move from a player copied into every map to one Resources/Player prefab
/// that SceneLoadManager spawns and carries between maps. Safe to run again.
/// </summary>
public static class PlayerPrefabMigration
{
    const string PrefabPath = "Assets/Resources/Player.prefab";
    const string SourceScene = "Assets/Scenes/daldongnaemap.unity";

    [MenuItem("CompanyGame/Setup/Move Scene Players To Prefab")]
    public static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string startScene = ActiveScenePath();

        if (!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) && !CreatePrefab()) return;

        var changed = new List<string>();
        foreach (string path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var players = roots.SelectMany(r => r.GetComponentsInChildren<PlayerMovement>(true)).ToArray();
            if (players.Length == 0) continue;

            bool hasDefault = roots.SelectMany(r => r.GetComponentsInChildren<MapSpawnPoint>(true))
                .Any(s => s.spawnId == SceneLoadManager.DefaultSpawnId);
            foreach (var player in players)
            {
                if (!hasDefault)
                {
                    // Maps started directly in Play mode place the player here.
                    var spawn = new GameObject("Spawn_" + SceneLoadManager.DefaultSpawnId);
                    spawn.transform.SetParent(player.transform.parent, false);
                    spawn.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
                    spawn.AddComponent<MapSpawnPoint>().spawnId = SceneLoadManager.DefaultSpawnId;
                    hasDefault = true;
                }
                Object.DestroyImmediate(player.gameObject);
            }
            EditorSceneManager.SaveScene(scene);
            changed.Add(path);
        }

        if (!string.IsNullOrEmpty(startScene)) EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);
        Debug.Log("Player prefab: " + PrefabPath + ". Removed scene players from " + changed.Count + " scenes:\n" + string.Join("\n", changed));
    }

    static bool CreatePrefab()
    {
        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
        var source = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerMovement>(true)).FirstOrDefault();
        if (!source)
        {
            Debug.LogError("No player found in " + SourceScene);
            return false;
        }

        // Components the runtime used to add on each map load now live on the prefab.
        var go = source.gameObject;
        foreach (var type in new[] { typeof(PlayerStats), typeof(PlayerInventory), typeof(PlayerCombat), typeof(PlayerHeldItem) })
            if (!go.GetComponent(type)) go.AddComponent(type);
        source.viewCamera = null; // Bound to each map's camera on arrival.
        go.name = "Player";

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single); // Discard the edits; the scene pass removes the player.
        return true;
    }

    static string ActiveScenePath() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
}
