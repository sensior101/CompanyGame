using System;
using System.Collections;
using CompanyGame.World.Maps;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads one map at a time. The player is spawned once from Resources/Player and
/// carried between maps; each scene owns only its camera, lights and spawn points.
/// </summary>
public sealed class SceneLoadManager : MonoBehaviour
{
    public const string DefaultSpawnId = "default";
    const string PlayerPrefab = "Player";

    public static bool IsLoading { get; private set; }
    public static string LastError { get; private set; } = string.Empty;

    /// <summary>The one player, kept across map loads.</summary>
    public static PlayerMovement Player { get; private set; }

    static SceneLoadManager runner;
    static string pendingSpawnId;
    string destinationPath;
    bool arrivalHandled;
    float dismissAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Domain reload can be disabled in Enter Play Mode settings.
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        runner = null;
        Player = null;
        pendingSpawnId = null;
        IsLoading = false;
        LastError = string.Empty;
    }

    /// <summary>
    /// Returns false without leaving the current map if the request is invalid.
    /// Use the full Assets/...unity path, enabled in the build scene list.
    /// A true result means the asynchronous transition was started.
    /// </summary>
    public static bool TryLoadMap(string targetScenePath, string targetSpawnId,
        PlayerMovement player)
    {
        if (!Application.isPlaying || IsLoading) return false;
        string path = (targetScenePath ?? string.Empty).Trim().Replace('\\', '/');
        string spawnId = (targetSpawnId ?? string.Empty).Trim();
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
            !path.EndsWith(".unity", StringComparison.Ordinal) ||
            SceneUtility.GetBuildIndexByScenePath(path) < 0 || !Application.CanStreamedLevelBeLoaded(path))
            return Reject("Map is not enabled in the build scene list: " + path);
        if (spawnId.Length == 0) return Reject("The destination spawn ID is empty.");
        if (!player || !player.isActiveAndEnabled) return Reject("Only the active player can change maps.");
        if (player != Player)
        {
            // Should not happen with one spawned player; keep the one that asked to travel.
            Debug.LogWarning("[Map transition] Tracked player was " + (Player ? Player.name : "none") +
                "; travelling with " + player.name + ". Active players: " +
                FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None).Length);
            Player = player;
        }

        EnsureRunner();
        LastError = string.Empty;
        IsLoading = true;
        pendingSpawnId = spawnId;
        runner.destinationPath = path;
        runner.arrivalHandled = false;
        player.enabled = false;
        runner.StartCoroutine(runner.LoadMap());
        return true;
    }

    static void EnsureRunner()
    {
        if (runner) return;
        var host = new GameObject("Map Transition (runtime)");
        runner = host.AddComponent<SceneLoadManager>();
        DontDestroyOnLoad(host);
    }

    // The first map's sceneLoaded can be missed in the editor; make sure the player exists anyway.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsurePlayer()
    {
        if (!Player) PlacePlayer(SceneManager.GetActiveScene(), DefaultSpawnId, false);
    }

    static bool Reject(string error)
    {
        Report(error);
        return false;
    }

    static void Report(string error)
    {
        EnsureRunner();
        LastError = error;
        runner.dismissAt = Time.unscaledTime + 10f;
        Debug.LogError("[Map transition] " + error);
    }

    IEnumerator LoadMap()
    {
        AsyncOperation operation = null;
        try
        {
            try { operation = SceneManager.LoadSceneAsync(destinationPath, LoadSceneMode.Single); }
            catch (Exception exception) { Report("Could not load map: " + exception.Message); }

            if (operation != null)
            {
                yield return operation;
                if (!arrivalHandled)
                    Report("The scene load finished without the expected destination: " + destinationPath);
            }
            else if (string.IsNullOrEmpty(LastError))
                Report("Unity could not start the scene load: " + destinationPath);
        }
        finally
        {
            IsLoading = false;
            if (Player) Player.enabled = true;
            if (string.IsNullOrEmpty(LastError)) Dismiss();
        }
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        bool arrival = IsLoading && runner && string.Equals(scene.path, runner.destinationPath, StringComparison.Ordinal);
        if (arrival) runner.arrivalHandled = true;
        try { PlacePlayer(scene, arrival ? pendingSpawnId : DefaultSpawnId, arrival); }
        catch (Exception exception) { Report("Could not initialize map arrival: " + exception.Message); }
    }

    static void PlacePlayer(Scene scene, string spawnId, bool spawnRequired)
    {
        if (!Player) Spawn(scene);
        if (!Player)
        {
            Report("No player. Run CompanyGame/Setup/Move Scene Players To Prefab to create Resources/Player.prefab.");
            return;
        }

        if (MapSpawnPoint.TryFind(scene, spawnId, out var spawnPoint, out var spawnError))
        {
            Player.spawn = spawnPoint.transform.position;
            Player.ResetToSpawn(); // Also resets falling velocity and safe reset position.
            Player.transform.rotation = Quaternion.Euler(0f, spawnPoint.transform.eulerAngles.y, 0f);
        }
        else if (spawnRequired)
            // Keep the player where it is rather than choosing an arbitrary spawn.
            // Fix the ID in the portal/spawn Inspector.
            Report(spawnError);

        BindCamera(scene);
    }

    static void Spawn(Scene scene)
    {
        var prefab = Resources.Load<PlayerMovement>(PlayerPrefab);
        if (prefab)
        {
            Player = Instantiate(prefab);
            Player.name = PlayerPrefab;
            DontDestroyOnLoad(Player.gameObject);
            return;
        }

        // ponytail: maps not yet migrated still carry their own player; drop once every map uses the prefab.
        foreach (var root in scene.GetRootGameObjects())
        {
            Player = root.GetComponentInChildren<PlayerMovement>(false);
            if (Player) return;
        }
    }

    static void BindCamera(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var follow = root.GetComponentInChildren<PlayerCameraController>(true);
            if (!follow) continue;
            follow.target = Player.transform;
            Player.viewCamera = follow.GetComponent<Camera>();
            return;
        }
        Player.viewCamera = Camera.main;
    }

    void Update()
    {
        if (runner == this && !IsLoading && Time.unscaledTime >= dismissAt) Dismiss();
    }

    void Dismiss()
    {
        // Destroy is deferred until the end of the frame. An immediate return
        // trip must create a fresh host rather than reuse this dying object.
        if (runner == this) runner = null;
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (runner != this) return;
        IsLoading = false;
        runner = null;
    }

    void OnGUI()
    {
        if (runner != this) return;
        string message = IsLoading ? "Loading map..." : LastError;
        if (string.IsNullOrEmpty(message)) return;
        float width = Mathf.Min(760f, Screen.width - 32f);
        var style = new GUIStyle(GUI.skin.box) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        GUI.Box(new Rect((Screen.width - width) * .5f, 24f, width, 76f), message, style);
    }
}
