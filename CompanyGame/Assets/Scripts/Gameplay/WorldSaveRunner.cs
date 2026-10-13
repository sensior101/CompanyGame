using System.Collections;
using UnityEngine;

/// <summary>
/// Drives WorldSave (host/single only, shared world) and PersonalSave (always, local file).
/// Loads one frame after startup so every GameSystems.Create() has finished. Created automatically.
/// </summary>
public sealed class WorldSaveRunner : MonoBehaviour
{
    const float WorldInterval = 60f, PersonalInterval = 5f, DebounceSeconds = 2f;

    public static WorldSaveRunner Instance { get; private set; }

    float worldTimer, personalTimer, debounce = -1f;
    string lastPersonal;
    bool loaded;
    LandPlotManager subscribedPlots;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (Instance) return;
        var go = new GameObject("World Save Runner");
        DontDestroyOnLoad(go);
        go.AddComponent<WorldSaveRunner>();
    }

    void Awake() { Instance = this; }

    IEnumerator Start()
    {
        yield return null;
        PersonalSave.CaptureBaseline(); // what a new account starts with, before any save is loaded
        WorldSave.CaptureBaseline();
        PersonalSave.LoadFromDisk();
        if (GameSession.IsAuthority) WorldSave.LoadFromDisk();
        lastPersonal = PersonalSave.Export();
        loaded = true;
    }

    /// <summary>Immediate world save (host/single only) and personal save. Safe to call from anywhere.</summary>
    public static void SaveNow()
    {
        if (!Instance || !Instance.loaded) return;
        Instance.SaveWorld();
        Instance.SavePersonal(true);
    }

    /// <summary>Saves shortly after a burst of shared changes settles.</summary>
    public static void RequestSave() { if (Instance) Instance.debounce = DebounceSeconds; }

    void Update()
    {
        if (!loaded) return;
        TrySubscribe();
        float dt = Time.unscaledDeltaTime;
        if ((worldTimer += dt) >= WorldInterval) { worldTimer = 0f; SaveWorld(); }
        if ((personalTimer += dt) >= PersonalInterval) { personalTimer = 0f; SavePersonal(false); }
        if (debounce >= 0f && (debounce -= dt) < 0f) SaveWorld();
    }

    void TrySubscribe()
    {
        if (subscribedPlots || !LandPlotManager.Instance) return;
        subscribedPlots = LandPlotManager.Instance;
        subscribedPlots.PlotChanged += OnPlotChanged;
    }

    void OnPlotChanged(string _) => RequestSave();

    void SaveWorld()
    {
        // ponytail: whole snapshot is serialized every minute; skip-on-equal keeps disk quiet, add dirty flags if it gets heavy
        if (!GameSession.IsAuthority) return;
        string json = WorldSave.ToJson(WorldSave.Capture());
        if (json != WorldSave.LastJson) WorldSave.SaveToDisk();
    }

    void SavePersonal(bool force)
    {
        // In a room the money is the room character's; it is saved per world by NetPlayerSave, never into this PC's own file.
        if (SharedWriteGate.InRoomCheck != null && SharedWriteGate.InRoomCheck()) return;
        string json = PersonalSave.Export();
        if (!force && json == lastPersonal) return;
        if (PersonalSave.SaveToDisk()) lastPersonal = json;
    }

    void OnApplicationPause(bool paused) { if (paused) SaveNow(); }
    void OnApplicationQuit() => SaveNow();

    void OnDestroy()
    {
        if (subscribedPlots) subscribedPlots.PlotChanged -= OnPlotChanged;
        if (Instance == this) Instance = null;
    }
}
