using UnityEngine;

/// <summary>
/// Creates the game-wide systems once at startup so no scene has to contain them.
/// Each manager gets its own root object and keeps itself alive across map loads.
/// </summary>
static class GameSystems
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        // UI input first, so a failing manager below cannot leave the game without clicks.
        UIEventSystem.Ensure();
        // Order matters: the clock first, then the systems that read it or each other on startup.
        BankManager.EnsureInstance();
        Spawn<SimpleGameClock>();
        Spawn<ReportManager>();
        Spawn<CompanyManager>();
        Spawn<StockMarketManager>();
        Spawn<SnsManager>();
        Spawn<LandPlotManager>();
        Spawn<PropertyRegistry>();

        // PhoneUI is built by CompanyGame/Setup/Build Phone UI Prefab; ChatUI was moved out of daldongnaemap.
        SpawnPrefab("PhoneUI");
        SpawnPrefab("ChatUI");
    }

    static void SpawnPrefab(string name)
    {
        var prefab = Resources.Load<GameObject>(name);
        if (!prefab) return;
        var instance = Object.Instantiate(prefab);
        instance.name = prefab.name;
        Object.DontDestroyOnLoad(instance);
    }

    static void Spawn<T>() where T : Component
    {
        if (!Object.FindAnyObjectByType<T>()) new GameObject(typeof(T).Name).AddComponent<T>();
    }
}
