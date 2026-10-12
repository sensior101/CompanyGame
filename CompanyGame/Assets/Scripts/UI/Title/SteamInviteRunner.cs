using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Joins a Steam friend invite directly: the overlay's "join game" (GameLobbyJoinRequested_t) while the game runs, or the
/// `+connect_lobby &lt;id&gt;` launch argument when the invite started the game. Works from the title screen and in the game
/// (while not in a room). Steam is only touched when Steam mode is on or the launch argument is present.
/// </summary>
public sealed class SteamInviteRunner : MonoBehaviour
{
    static SteamInviteRunner instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        ulong launch = SteamBackend.ParseLaunchLobby(System.Environment.GetCommandLineArgs());
        if (!NetMode.IsSteam && launch == 0) return;
        if (launch != 0) NetMode.IsSteam = true;
        if (launch != 0) SteamBackend.LaunchLobby = launch;
        Ensure();
    }

    /// <summary>Starts listening for invites once Steam is up (called after a successful Steam sign-in as well).</summary>
    public static void Ensure()
    {
        if (instance || !SteamBackend.TryInit(out _)) return; // the title screen explains it when the player tries to use Steam
        var go = new GameObject("SteamInviteRunner");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SteamInviteRunner>();
        SteamBackend.LobbyJoinRequested += instance.OnInvite;
    }

    void Update()
    {
        if (SteamBackend.LaunchLobby == 0) return;
        // wait for the title (or game) to be up before acting on the launch argument
        if (Time.frameCount < 10) return;
        ulong id = SteamBackend.LaunchLobby;
        SteamBackend.LaunchLobby = 0;
        OnInvite(id);
    }

    void OnDestroy() { if (instance == this) { SteamBackend.LobbyJoinRequested -= OnInvite; instance = null; } }

    void OnInvite(ulong lobbyId)
    {
        var net = NetSession.Instance;
        if (net && net.InRoom) { net.StatusText = "이미 방에 있어서 초대를 받지 못했습니다. 먼저 방을 나가 주세요."; return; }
        if (TitleScreen.Current) { TitleScreen.Current.JoinFromInvite(lobbyId); return; }
        RunInGame(lobbyId);
    }

    static async void RunInGame(ulong lobbyId)
    {
        string error = await TitleFlow.JoinInvite(lobbyId);
        var net = NetSession.Instance;
        if (error != null && net) net.StatusText = TitleFlow.Message(error);
    }
}
