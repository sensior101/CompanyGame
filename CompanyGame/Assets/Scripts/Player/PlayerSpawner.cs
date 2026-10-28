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

    /// <summary>The title scene has no player: it is hidden there and shown again when a map loads.</summary>
    public const string TitleSceneName = "Title";

    /// <summary>The one player, kept across map loads.</summary>
    public static PlayerMovement Player { get; private set; }
    static bool hasPendingView;
    static bool pendingFirstPerson;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        Player = null;
        hasPendingView = false;
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

    static void Pause()
    {
        if (!Player) return;
        var camera = Player.viewCamera ? Player.viewCamera.GetComponent<PlayerCameraController>() : null;
        hasPendingView = camera;
        if (camera) pendingFirstPerson = camera.firstPerson;
        Player.enabled = false;
    }
    static void Resume() { if (Player) Player.enabled = true; }

    static void Place(Scene scene, string spawnId, bool spawnRequired)
    {
        if (scene.name == TitleSceneName)
        {
            hasPendingView = false;
            if (Player) Player.gameObject.SetActive(false);
            return;
        }
        if (Player) Player.gameObject.SetActive(true);
        if (!Player) Spawn();
        if (!Player)
        {
            SceneLoadManager.Report("No player. Run CompanyGame/Setup/Move Scene Players To Prefab to create Resources/Player.prefab.");
            return;
        }
        SceneLoadManager.Traveller = Player;

        if (MapSpawnPoint.TryFind(scene, spawnId, out var spawnPoint, out var spawnError))
        {
            MovePlayerTo(spawnPoint);
        }
        else if (spawnRequired)
            // Keep the player where it is rather than choosing an arbitrary spawn.
            // Fix the ID in the portal/spawn Inspector.
            SceneLoadManager.Report(spawnError);

        BindCamera(scene, spawnRequired);
    }

    /// <summary>Moves the player to a spawn point of the current map without loading a scene.</summary>
    public static bool TeleportInScene(string spawnId)
    {
        if (!Player) return false;
        if (!MapSpawnPoint.TryFind(SceneLoadManager.CurrentMap, spawnId, out var sp, out var err))
        {
            SceneLoadManager.Report(err);
            return false;
        }
        MovePlayerTo(sp);
        return true;
    }

    static void MovePlayerTo(MapSpawnPoint sp)
    {
        Player.spawn = sp.transform.position;
        Player.ResetToSpawn(); // Also resets falling velocity and safe reset position.
        Player.transform.rotation = Quaternion.Euler(0f, sp.transform.eulerAngles.y, 0f);
    }

    static void Spawn()
    {
        var prefab = Resources.Load<PlayerMovement>(PrefabName);
        if (!prefab) return;
        Player = Object.Instantiate(prefab);
        Player.name = PrefabName;
        Object.DontDestroyOnLoad(Player.gameObject);
    }

    static void BindCamera(Scene scene, bool arriving)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var follow = root.GetComponentInChildren<PlayerCameraController>(true);
            if (!follow) continue;
            // Preserve the player's chosen view across map transitions; direct scene starts keep the map default.
            if (arriving && hasPendingView)
                follow.firstPerson = pendingFirstPerson;
            var look = Player.GetComponent<DaldongnePlayerAppearance>();
            follow.Bind(Player.transform, look ? look.female : null, look ? look.male : null);
            Player.viewCamera = follow.GetComponent<Camera>();
            hasPendingView = false;
            return;
        }
        Player.viewCamera = Camera.main;
        hasPendingView = false;
    }
}
