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
        // Order matters: the clock first, then the systems that read it or each other on startup.
        BankManager.EnsureInstance();
        Spawn<SimpleGameClock>();
        Spawn<ReportManager>();
        Spawn<CompanyManager>();
        Spawn<StockMarketManager>();
        Spawn<SnsManager>();
        Spawn<LandPlotManager>();

        // Built by CompanyGame/Setup/Build Phone UI Prefab.
        var phoneUI = Resources.Load<GameObject>("PhoneUI");
        if (phoneUI)
        {
            var phone = Object.Instantiate(phoneUI);
            phone.name = phoneUI.name;
            Object.DontDestroyOnLoad(phone);
        }
    }

    static void Spawn<T>() where T : Component
    {
        if (!Object.FindAnyObjectByType<T>()) new GameObject(typeof(T).Name).AddComponent<T>();
    }
}
