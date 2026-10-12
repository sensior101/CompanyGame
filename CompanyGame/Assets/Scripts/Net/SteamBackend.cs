using System;
using UnityEngine;
#if STEAMWORKS_NET
using Steamworks;
#endif

/// <summary>
/// Steam is optional: nothing here may throw when Steam is not installed, not running, or the package is missing.
/// Errors come back as codes (steam_not_compiled, steam_dll_missing, steam_not_running, steam_init_failed) that
/// TitleFlow.Message turns into Korean text. Development mode uses Valve's test AppID 480 (the game shows as "Spacewar").
/// </summary>
public static class SteamBackend
{
    public const uint AppId = 480;
    /// <summary>Lobby key that tells our lobbies apart from every other Spacewar developer's.</summary>
    public const string GameKey = "companygame";
    /// <summary>Bump when the wire protocol changes; lobbies of another value are refused.</summary>
    public const string Protocol = "1";

    public static bool Initialized { get; private set; }
    public static string PersonaName { get; private set; } = "";
    public static string SteamIdText { get; private set; } = "";
    /// <summary>The overlay or the `+connect_lobby` launch argument asked to join this lobby (raised on the main thread).</summary>
    public static event Action<ulong> LobbyJoinRequested;
    /// <summary>A lobby id from `+connect_lobby <id>` waiting for a consumer (0 = none).</summary>
    public static ulong LaunchLobby { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Initialized = false; LobbyJoinRequested = null; LaunchLobby = 0; PersonaName = SteamIdText = ""; }

    public static string VersionTag => Protocol + "|" + Application.version;

    /// <summary>The `+connect_lobby` argument, if the game was started from a Steam invite (0 = none).</summary>
    public static ulong ParseLaunchLobby(string[] args)
    {
        for (int i = 0; args != null && i + 1 < args.Length; i++)
            if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id)) return id;
        return 0;
    }

#if STEAMWORKS_NET
    static Callback<GameLobbyJoinRequested_t> joinRequested;

    public static bool TryInit(out string error)
    {
        error = null;
        if (Initialized) return true;
        try
        {
            var result = SteamAPI.InitEx(out string message);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                Debug.LogWarning("[Steam] init failed: " + result + " " + message);
                error = result == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient ? "steam_not_running" : "steam_init_failed";
                return false;
            }
        }
        catch (DllNotFoundException e) { Debug.LogWarning("[Steam] " + e.Message); error = "steam_dll_missing"; return false; }
        catch (Exception e) { Debug.LogWarning("[Steam] init exception: " + e.Message); error = "steam_init_failed"; return false; }
        Initialized = true;
        PersonaName = SteamFriends.GetPersonaName() ?? "";
        SteamIdText = SteamUser.GetSteamID().m_SteamID.ToString();
        joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => LobbyJoinRequested?.Invoke(r.m_steamIDLobby.m_SteamID));
        SteamPump.Ensure();
        return true;
    }

    internal static void Shutdown()
    {
        if (!Initialized) return;
        Initialized = false;
        joinRequested?.Dispose(); joinRequested = null;
        SteamAPI.Shutdown();
    }

    public static INetTransport CreateTransport(out string error)
    {
        if (!TryInit(out error)) return null;
        return new SteamTransport();
    }
#else
    public static bool TryInit(out string error) { error = "steam_not_compiled"; return false; }
    public static INetTransport CreateTransport(out string error) { error = "steam_not_compiled"; return null; }
#endif
}

#if STEAMWORKS_NET
/// <summary>Runs Steam's callbacks every frame and shuts Steam down on quit.</summary>
[DefaultExecutionOrder(-30000)]
sealed class SteamPump : MonoBehaviour
{
    static SteamPump instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    public static void Ensure()
    {
        if (instance) return;
        var go = new GameObject("SteamPump");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SteamPump>();
    }

    void Update() { if (SteamBackend.Initialized) SteamAPI.RunCallbacks(); }
    void OnApplicationQuit() { SteamBackend.Shutdown(); }
}
#endif
