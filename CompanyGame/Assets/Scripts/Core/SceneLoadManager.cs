using System;
using System.Collections;
using CompanyGame.Daldongne;
using CompanyGame.World.Maps;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads one map at a time. Only this short-lived transition host survives the
/// load; each scene owns its player, camera and lights.
/// </summary>
public sealed class SceneLoadManager : MonoBehaviour
{
    public static bool IsLoading { get; private set; }
    public static string LastError { get; private set; } = string.Empty;

    static SceneLoadManager runner;
    PlayerMovement sourcePlayer;
    bool sourceWasEnabled;
    bool savedWalking;
    bool hasAppearance;
    DaldongnePlayerAppearance.Variant savedAppearance;
    string destinationPath;
    string destinationSpawnId;
    bool arrivalHandled;
    float dismissAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Domain reload can be disabled in Enter Play Mode settings.
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        runner = null;
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
        if (!player || !player.isActiveAndEnabled || !player.gameObject.scene.IsValid())
            return Reject("A map transition requires an active scene-local player.");
        if (!TryGetScenePlayer(player.gameObject.scene, out var uniquePlayer, out var playerError) ||
            uniquePlayer != player)
            return Reject(playerError ?? "The requesting player does not belong to this map.");

        EnsureRunner();
        LastError = string.Empty;
        IsLoading = true;
        runner.sourcePlayer = player;
        runner.sourceWasEnabled = player.enabled;
        runner.savedWalking = player.walking;
        var appearance = player.GetComponent<DaldongnePlayerAppearance>();
        runner.hasAppearance = appearance != null;
        if (appearance) runner.savedAppearance = appearance.selected;
        runner.destinationPath = path;
        runner.destinationSpawnId = spawnId;
        runner.arrivalHandled = false;
        player.enabled = false;
        runner.StartCoroutine(runner.LoadMap());
        return true;
    }

    /// <summary>Resolves exactly one enabled walker in the supplied scene.</summary>
    public static bool TryGetScenePlayer(Scene scene, out PlayerMovement player, out string error)
    {
        player = null;
        error = null;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            error = "The map scene is not loaded.";
            return false;
        }
        foreach (var root in scene.GetRootGameObjects())
        foreach (var candidate in root.GetComponentsInChildren<PlayerMovement>(false))
        {
            if (!candidate.isActiveAndEnabled) continue;
            if (player)
            {
                player = null;
                error = "Map '" + scene.name + "' contains multiple active players. Keep one scene-local player.";
                return false;
            }
            player = candidate;
        }
        if (player) return true;
        error = "Map '" + scene.name + "' has no active PlayerMovement.";
        return false;
    }

    static void EnsureRunner()
    {
        if (runner) return;
        var host = new GameObject("Map Transition (runtime)");
        runner = host.AddComponent<SceneLoadManager>();
        DontDestroyOnLoad(host);
    }

    static bool Reject(string error)
    {
        EnsureRunner();
        runner.ReportError(error);
        return false;
    }

    void ReportError(string error)
    {
        LastError = error;
        dismissAt = Time.unscaledTime + 10f;
        Debug.LogError("[Map transition] " + error);
    }

    IEnumerator LoadMap()
    {
        AsyncOperation operation = null;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        try
        {
            try { operation = SceneManager.LoadSceneAsync(destinationPath, LoadSceneMode.Single); }
            catch (Exception exception) { ReportError("Could not load map: " + exception.Message); }

            if (operation != null)
            {
                yield return operation;
                if (!arrivalHandled)
                    ReportError("The scene load finished without the expected destination: " + destinationPath);
            }
            else if (string.IsNullOrEmpty(LastError))
                ReportError("Unity could not start the scene load: " + destinationPath);
        }
        finally
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            IsLoading = false;
            RestoreSourceIfPresent();
            if (string.IsNullOrEmpty(LastError)) Dismiss();
        }
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!runner || !IsLoading || !string.Equals(scene.path, runner.destinationPath, StringComparison.Ordinal)) return;
        runner.arrivalHandled = true;
        try { runner.PlaceArrival(scene); }
        catch (Exception exception) { runner.ReportError("Could not initialize map arrival: " + exception.Message); }
    }

    void PlaceArrival(Scene scene)
    {
        if (!TryGetScenePlayer(scene, out var player, out var playerError))
        {
            ReportError(playerError);
            return;
        }
        if (!MapSpawnPoint.TryFind(scene, destinationSpawnId, out var spawnPoint, out var spawnError))
        {
            // Keep the destination's own default rather than choosing an
            // arbitrary first match. Fix the ID in the portal/spawn Inspector.
            ReportError(spawnError);
            return;
        }

        player.spawn = spawnPoint.transform.position;
        player.ResetToSpawn(); // Also resets falling velocity and safe reset position.
        player.transform.rotation = Quaternion.Euler(0f, spawnPoint.transform.eulerAngles.y, 0f);
        var appearance = player.GetComponent<DaldongnePlayerAppearance>();
        if (hasAppearance && appearance) appearance.Select(savedAppearance);
        // sceneLoaded runs after Awake/OnEnable and before Start. The existing
        // walker Start() performs the camera handoff for this serialized flag.
        player.walking = savedWalking;
    }

    void RestoreSourceIfPresent()
    {
        if (!sourcePlayer) return;
        sourcePlayer.enabled = sourceWasEnabled;
        sourcePlayer.SetWalking(savedWalking);
        sourcePlayer = null;
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
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        RestoreSourceIfPresent();
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
