using CompanyGame.Daldongne;
using CompanyGame.World.Maps;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns the one player from Resources/Player, keeps it across map loads and moves it
/// to each map's spawn point, binding that map's follow camera.
/// </summary>
public static class PlayerSpawner
{
    const string PrefabName = "Player";

    /// <summary>The one player, kept across map loads.</summary>
    public static PlayerMovement Player { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        Player = null;
        SceneLoadManager.MapReady -= Place;
        SceneLoadManager.MapReady += Place;
        SceneLoadManager.LoadStarted -= Pause;
        SceneLoadManager.LoadStarted += Pause;
        SceneLoadManager.LoadFinished -= Resume;
        SceneLoadManager.LoadFinished += Resume;
    }

    // The first map's sceneLoaded can be missed in the editor; make sure the player exists anyway.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsurePlayer()
    {
        if (!Player) Place(SceneManager.GetActiveScene(), SceneLoadManager.DefaultSpawnId, false);
    }

    static void Pause() { if (Player) Player.enabled = false; }
    static void Resume() { if (Player) Player.enabled = true; }

    static void Place(Scene scene, string spawnId, bool spawnRequired)
    {
        if (!Player) Spawn();
        if (!Player)
        {
            SceneLoadManager.Report("No player. Run CompanyGame/Setup/Move Scene Players To Prefab to create Resources/Player.prefab.");
            return;
        }
        SceneLoadManager.Traveller = Player;

        if (MapSpawnPoint.TryFind(scene, spawnId, out var spawnPoint, out var spawnError))
        {
            Player.spawn = spawnPoint.transform.position;
            Player.ResetToSpawn(); // Also resets falling velocity and safe reset position.
            Player.transform.rotation = Quaternion.Euler(0f, spawnPoint.transform.eulerAngles.y, 0f);
        }
        else if (spawnRequired)
            // Keep the player where it is rather than choosing an arbitrary spawn.
            // Fix the ID in the portal/spawn Inspector.
            SceneLoadManager.Report(spawnError);

        BindCamera(scene);
    }

    static void Spawn()
    {
        var prefab = Resources.Load<PlayerMovement>(PrefabName);
        if (!prefab) return;
        Player = Object.Instantiate(prefab);
        Player.name = PrefabName;
        Object.DontDestroyOnLoad(Player.gameObject);
    }

    static void BindCamera(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var follow = root.GetComponentInChildren<PlayerCameraController>(true);
            if (!follow) continue;
            // Each map's camera keeps its own view settings (the store is first person); the body it hides comes from the player.
            var look = Player.GetComponent<DaldongnePlayerAppearance>();
            follow.Bind(Player.transform, look ? look.female : null, look ? look.male : null);
            Player.viewCamera = follow.GetComponent<Camera>();
            return;
        }
        Player.viewCamera = Camera.main;
    }
}
