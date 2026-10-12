/// <summary>
/// How this game instance runs. Single player and the host own the simulation
/// (economy, clock, events); a client only mirrors what the host sends.
/// Online random matching is a way to find a host, so it is still Host or Client.
/// </summary>
public enum SessionMode { Single, Host, Client }

public static class GameSession
{
    // 기본은 방장 호스트. 네트워크가 붙기 전에는 Single과 같게 동작한다.
    public static SessionMode Mode { get; private set; } = SessionMode.Host;

    // Offline identity: a per-PC GUID and the default name. A server account (hello) replaces both and is
    // remembered on this PC, so the same person keeps the same owner id offline and in rooms.
    static string AccountIdKey => ProfileSlot.Key("Session.AccountId");
    static string AccountNameKey => ProfileSlot.Key("Session.AccountName");
    static string offlineName = "Player", guid, accountId, accountName;
    static bool accountLoaded;

    /// <summary>The name ownership and system messages use. Account nickname once logged in on this PC.</summary>
    public static string LocalPlayerName
    {
        get { LoadAccount(); return HasAccount && !string.IsNullOrEmpty(accountName) ? accountName : offlineName; }
        set => offlineName = value;
    }

    /// <summary>The per-PC GUID from before accounts existed (the legacy owner id).</summary>
    public static string OfflinePlayerId
    {
        get
        {
            if (!string.IsNullOrEmpty(guid)) return guid;
            guid = UnityEngine.PlayerPrefs.GetString(ProfileSlot.Key("Session.LocalPlayerId"), "");
            if (string.IsNullOrEmpty(guid))
            {
                guid = System.Guid.NewGuid().ToString("N");
                UnityEngine.PlayerPrefs.SetString(ProfileSlot.Key("Session.LocalPlayerId"), guid);
                UnityEngine.PlayerPrefs.Save();
            }
            return guid;
        }
    }

    public static string OfflinePlayerName => offlineName;
    public static bool HasAccount { get { LoadAccount(); return !string.IsNullOrEmpty(accountId); } }

    /// <summary>Account id (server players.id) when known, else the legacy GUID.</summary>
    public static string LocalPlayerId { get { LoadAccount(); return HasAccount ? accountId : OfflinePlayerId; } }

    /// <summary>Raised after the account changed: (oldId, newId). Owners of records can migrate then.</summary>
    public static event System.Action<string, string> AccountChanged;

    /// <summary>The server said who this PC is (hello). Sticky until a different account logs in.</summary>
    public static void SetAccount(string id, string name)
    {
        if (string.IsNullOrEmpty(id)) return;
        string old = LocalPlayerId;
        accountId = id; accountName = name ?? ""; accountLoaded = true;
        UnityEngine.PlayerPrefs.SetString(AccountIdKey, id);
        UnityEngine.PlayerPrefs.SetString(AccountNameKey, accountName);
        UnityEngine.PlayerPrefs.Save();
        AccountChanged?.Invoke(old, id);
    }

    /// <summary>Forget the cached account so the next read comes from PlayerPrefs again (tests).</summary>
    public static void ReloadAccount() { accountLoaded = false; guid = null; }

    /// <summary>Back to the legacy offline identity (logout, tests).</summary>
    public static void ClearAccount()
    {
        accountId = accountName = ""; accountLoaded = true;
        UnityEngine.PlayerPrefs.DeleteKey(AccountIdKey); UnityEngine.PlayerPrefs.DeleteKey(AccountNameKey);
    }

    static void LoadAccount()
    {
        if (accountLoaded) return;
        accountLoaded = true;
        accountId = UnityEngine.PlayerPrefs.GetString(AccountIdKey, "");
        accountName = UnityEngine.PlayerPrefs.GetString(AccountNameKey, "");
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { guid = null; accountId = accountName = null; accountLoaded = false; AccountChanged = null; }

    /// <summary>True where economy, time and events are computed. Gate every simulation write on this.</summary>
    public static bool IsAuthority => Mode != SessionMode.Client;

    /// <summary>Called by the network layer (Net) when a session starts or ends.</summary>
    public static void Begin(SessionMode mode) => Mode = mode;
}

/// <summary>
/// Profile slot: lets several game instances share one PC. Slot 1 keeps the original PlayerPrefs keys and
/// save folders; slot N (2..4) gets a ".pN" key suffix and its own "profileN" save folder, so each slot has its
/// own server account, token and world/personal files. Chosen with the `-profile N` command-line argument
/// (fixed for that run) or, without it, remembered from the settings screen.
/// </summary>
public static class ProfileSlot
{
    public const int Max = 4;
    const string SlotKey = "Profile.Slot";
    static int slot;

    /// <summary>True when `-profile N` was given: the slot cannot be changed in the game.</summary>
    public static bool FromCommandLine { get; private set; }

    public static int Slot { get { Load(); return slot; } }

    public static string Key(string key) => Slot == 1 ? key : key + ".p" + Slot;

    /// <summary>Folder for this slot's world/ and player/ saves.</summary>
    public static string DataRoot => Slot == 1
        ? UnityEngine.Application.persistentDataPath
        : System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "profile" + Slot);

    /// <summary>Switches slot (remembered). False when fixed by the command line or out of range.</summary>
    public static bool Set(int next)
    {
        Load();
        if (FromCommandLine || next < 1 || next > Max) return false;
        if (next == slot) return true;
        slot = next;
        UnityEngine.PlayerPrefs.SetInt(SlotKey, next);
        GameSession.ReloadAccount();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Raised after the slot changed so caches that read slot keys (token, account) reload.</summary>
    public static event System.Action Changed;

    static void Load()
    {
        if (slot > 0) return;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-profile" && int.TryParse(args[i + 1], out int n) && n >= 1 && n <= Max) { slot = n; FromCommandLine = true; }
        if (slot == 0) slot = UnityEngine.Mathf.Clamp(UnityEngine.PlayerPrefs.GetInt(SlotKey, 1), 1, Max);
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { slot = 0; FromCommandLine = false; Changed = null; }
}
