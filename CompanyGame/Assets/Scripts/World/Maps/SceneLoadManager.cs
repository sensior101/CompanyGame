using System;
using System.Collections;
using CompanyGame.World.Maps;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads one map at a time. The player lives outside the map scenes; PlayerSpawner places it
/// when a map is ready. World code only sees the player as a generic Traveller.
/// </summary>
public sealed class SceneLoadManager : MonoBehaviour
{
    public const string DefaultSpawnId = "default";

    public static bool IsLoading { get; private set; }
    public static string LastError { get; private set; } = string.Empty;

    /// <summary>The map the player is in.</summary>
    public static Scene CurrentMap => SceneManager.GetActiveScene();

    /// <summary>The player as World code sees it, for range checks. Set by PlayerSpawner.</summary>
    public static Behaviour Traveller { get; set; }

    /// <summary>A map transition started; the player should stop moving.</summary>
    public static event Action LoadStarted;

    /// <summary>A map transition ended, successfully or not.</summary>
    public static event Action LoadFinished;

    /// <summary>A map is loaded: (scene, spawn ID, true when arriving by transition rather than at startup).</summary>
    public static event Action<Scene, string, bool> MapReady;

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
        pendingSpawnId = null;
        Traveller = null;
        IsLoading = false;
        LastError = string.Empty;
    }

    /// <summary>
    /// Returns false without leaving the current map if the request is invalid.
    /// Use the full Assets/...unity path, enabled in the build scene list.
    /// A true result means the asynchronous transition was started.
    /// </summary>
    public static bool TryLoadMap(string targetScenePath, string targetSpawnId, Behaviour requester = null)
    {
        if (!Application.isPlaying || IsLoading) return false;
        string path = (targetScenePath ?? string.Empty).Trim().Replace('\\', '/');
        string spawnId = (targetSpawnId ?? string.Empty).Trim();
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
            !path.EndsWith(".unity", StringComparison.Ordinal) ||
            SceneUtility.GetBuildIndexByScenePath(path) < 0 || !Application.CanStreamedLevelBeLoaded(path))
            return Reject("Map is not enabled in the build scene list: " + path);
        if (spawnId.Length == 0) return Reject("The destination spawn ID is empty.");
        if (requester && !requester.isActiveAndEnabled) return Reject("Only the active player can change maps.");

        EnsureRunner();
        LastError = string.Empty;
        IsLoading = true;
        pendingSpawnId = spawnId;
        runner.destinationPath = path;
        runner.arrivalHandled = false;
        LoadStarted?.Invoke();
        runner.StartCoroutine(runner.LoadMap());
        return true;
    }

    /// <summary>Shows a transition error on screen for a few seconds and logs it.</summary>
    public static void Report(string error)
    {
        EnsureRunner();
        LastError = error;
        runner.dismissAt = Time.unscaledTime + 10f;
        Debug.LogError("[Map transition] " + error);
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
        Report(error);
        return false;
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
            LoadFinished?.Invoke();
            if (string.IsNullOrEmpty(LastError)) Dismiss();
        }
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        bool arrival = IsLoading && runner && string.Equals(scene.path, runner.destinationPath, StringComparison.Ordinal);
        if (arrival) runner.arrivalHandled = true;
        try { MapReady?.Invoke(scene, arrival ? pendingSpawnId : DefaultSpawnId, arrival); }
        catch (Exception exception) { Report("Could not initialize map arrival: " + exception.Message); }
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
