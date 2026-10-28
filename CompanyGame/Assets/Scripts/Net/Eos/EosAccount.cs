using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

/// <summary>
/// This game's EOS account (Docs/Multiplayer_Redesign.md 12.13). On start it logs in with the EOS Device ID in the
/// background; on success the PUID becomes the account (<see cref="GameSession.SetAccount"/>, kept on this slot).
/// Without the plugin config (StreamingAssets/EOS) or with <c>-noeos</c> / env <c>COMPANYGAME_NOEOS=1</c> (QA, two windows on
/// one PC) EOS stays <see cref="Status.Off"/> and the account is the slot's PC GUID. A failed login keeps the last PUID of
/// this slot (solo worlds keep their character offline); servers then see no EOS token from it.
/// Nothing happens under <c>-eostest</c> (EosProbe owns EOS then). Every EOS call is made on the main thread.
/// </summary>
public sealed class EosAccount : MonoBehaviour
{
    public enum Status { Off, LoggingIn, Ready, Failed }

    public const string DisableArg = "-noeos", DisableEnv = "COMPANYGAME_NOEOS";

    public static Status State { get; private set; } = Status.Off;
    /// <summary>Why EOS is Off or Failed ("disabled", "no_config", "no_platform", or the login result), for logs.</summary>
    public static string Problem { get; private set; } = "";
    /// <summary>My PUID as text, null until Ready.</summary>
    public static string LocalPuid => puid?.ToString();
    /// <summary>True while EOS is on or still logging in (servers then wait for it instead of trusting Hellos).</summary>
    public static bool Enabled => State == Status.LoggingIn || State == Status.Ready;
    internal static P2PInterface P2P { get; private set; }
    internal static ProductUserId Puid => puid;
    internal static Epic.OnlineServices.Sessions.SessionsInterface Sessions { get; private set; }

    static ProductUserId puid;
    static ConnectInterface connect;
    static TaskCompletionSource<bool> settled = new TaskCompletionSource<bool>();
    static readonly List<(ProductUserId peer, SocketId socket, float at)> closing = new List<(ProductUserId, SocketId, float)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        State = Status.Off; Problem = ""; puid = null; connect = null; P2P = null; Sessions = null;
        settled = new TaskCompletionSource<bool>();
        closing.Clear();
    }

    /// <summary>Done when the login ended either way (or EOS is off): true when Ready.</summary>
    public static Task<bool> Settled => settled.Task;

    /// <summary>
    /// Why EOS will not run in this process ("disabled", "no_config"), or null when it will log in. Does not depend on the
    /// boot order: a server process (SyncHost, also AfterSceneLoad) asks it before <see cref="Boot"/> may have run.
    /// </summary>
    public static string OffReason()
    {
        // ponytail: File.Exists cannot see into an Android apk; read StreamingAssets with UnityWebRequest when mobile builds come
        return Array.IndexOf(Environment.GetCommandLineArgs(), DisableArg) >= 0 || Environment.GetEnvironmentVariable(DisableEnv) == "1" ? "disabled"
            : !File.Exists(Path.Combine(Application.streamingAssetsPath, "EOS", "eos_product_config.json")) ? "no_config" : null;
    }

    // After the first scene: at BeforeSceneLoad the editor's EOS platform is not there yet (no_platform).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-eostest") >= 0) return; // the 12.4 probe runs its own EOS
        string off = OffReason();
        ProfileSlot.Changed += Apply; // a slot switched to later gets the same account
        if (off != null)
        {
            Problem = off;
            Apply();
            Debug.Log("[Eos] off (" + off + "): offline and direct addresses only, account = PC GUID");
            settled.TrySetResult(false);
            return;
        }
        var go = new GameObject("EosAccount");
        DontDestroyOnLoad(go);
        go.AddComponent<EOSManager>(); // Awake makes the platform from StreamingAssets/EOS (or takes the one already made)
        go.AddComponent<EosAccount>();
        State = Status.LoggingIn;
        Login();
    }

    static async void Login()
    {
        try
        {
            var platform = EOSManager.Instance.GetEOSPlatformInterface();
            if (platform == null) throw new Exception("no_platform");
            connect = platform.GetConnectInterface();
            P2P = platform.GetP2PInterface();
            Sessions = platform.GetSessionsInterface();
            var id = await DeviceLogin();
            puid = id;
            State = Status.Ready;
            var expiry = new AddNotifyAuthExpirationOptions();
            connect.AddNotifyAuthExpiration(ref expiry, null, (ref AuthExpirationCallbackInfo info) => Relogin());
            Apply();
            Debug.Log("[Eos] logged in, PUID " + LocalPuid.Substring(0, 4) + "…");
        }
        catch (Exception e)
        {
            State = Status.Failed;
            Problem = e.Message;
            Debug.LogWarning("[Eos] login failed (" + e.Message + "): offline and direct addresses only");
        }
        settled.TrySetResult(State == Status.Ready);
    }

    // Connect auth runs out after about an hour; a server process must stay logged in (sessions, tokens, P2P).
    // Ready: the PUID is this slot's account. Off: the slot's PC GUID, not a PUID remembered from another run. Failed: unchanged.
    static void Apply()
    {
        if (State == Status.Ready && GameSession.LocalPlayerId != LocalPuid) GameSession.SetAccount(LocalPuid, GameSession.OfflinePlayerName);
        else if (State == Status.Off) GameSession.ClearAccount();
    }

    static async void Relogin()
    {
        try { await DeviceLogin(); Debug.Log("[Eos] auth refreshed"); }
        catch (Exception e) { Debug.LogWarning("[Eos] auth refresh failed: " + e.Message); }
    }

    // Same steps as EosProbe.Login (12.4): make the device id once, log in, make the EOS user on the first login.
    static async Task<ProductUserId> DeviceLogin()
    {
        var created = new TaskCompletionSource<Result>();
        var deviceOptions = new CreateDeviceIdOptions { DeviceModel = SystemInfo.deviceModel.Length > 0 ? SystemInfo.deviceModel : "PC" };
        connect.CreateDeviceId(ref deviceOptions, null, (ref CreateDeviceIdCallbackInfo info) => created.TrySetResult(info.ResultCode));
        Result device = await Within(created.Task, 30, "CreateDeviceId");
        if (device != Result.Success && device != Result.DuplicateNotAllowed) throw new Exception("CreateDeviceId " + device);

        var login = new TaskCompletionSource<LoginCallbackInfo>();
        EOSManager.Instance.StartConnectLoginWithDeviceToken("CompanyGame", info => login.TrySetResult(info));
        var result = await Within(login.Task, 30, "Connect.Login");
        if (result.ResultCode == Result.InvalidUser && result.ContinuanceToken != null)
        {
            var user = new TaskCompletionSource<CreateUserCallbackInfo>();
            var options = new CreateUserOptions { ContinuanceToken = result.ContinuanceToken };
            connect.CreateUser(ref options, null, (ref CreateUserCallbackInfo info) => user.TrySetResult(info));
            var made = await Within(user.Task, 30, "Connect.CreateUser");
            if (made.ResultCode != Result.Success) throw new Exception("Connect.CreateUser " + made.ResultCode);
            return made.LocalUserId;
        }
        if (result.ResultCode != Result.Success) throw new Exception("Connect.Login " + result.ResultCode);
        return result.LocalUserId;
    }

    /// <summary>Waits for the login to end (at most <paramref name="seconds"/>). True when Ready.</summary>
    public static async Task<bool> WhenSettled(float seconds = 40)
    {
        await Task.WhenAny(Settled, Task.Delay(TimeSpan.FromSeconds(seconds)));
        return State == Status.Ready;
    }

    /// <summary>My EOS ID token for a server's identity check, "" without one.</summary>
    public static string IdToken()
    {
        if (State != Status.Ready) return "";
        var options = new CopyIdTokenOptions { LocalUserId = puid };
        return connect.CopyIdToken(ref options, out IdToken? token) == Result.Success && token.HasValue ? (string)token.Value.JsonWebToken ?? "" : "";
    }

    /// <summary>True when <paramref name="token"/> is a valid EOS ID token of <paramref name="account"/> (asked of EOS, at most 8 seconds).</summary>
    public static async Task<bool> Verify(string account, string token)
    {
        if (State != Status.Ready || string.IsNullOrEmpty(token)) return false;
        var claimed = ProductUserId.FromString(account ?? "");
        if (claimed == null || !claimed.IsValid()) return false;
        var done = new TaskCompletionSource<VerifyIdTokenCallbackInfo>();
        var options = new VerifyIdTokenOptions { IdToken = new IdToken { ProductUserId = claimed, JsonWebToken = token } };
        connect.VerifyIdToken(ref options, null, (ref VerifyIdTokenCallbackInfo info) => done.TrySetResult(info));
        try
        {
            var info = await Within(done.Task, 8, "VerifyIdToken");
            return info.ResultCode == Result.Success && info.ProductUserId?.ToString() == account;
        }
        catch (TimeoutException) { return false; }
    }

    /// <summary>Closes a P2P connection a second from now, so packets already queued for it (a Reject, a last save) go out first.</summary>
    internal static void CloseLater(ProductUserId peer, SocketId socket) => closing.Add((peer, socket, Time.realtimeSinceStartup + 1f));

    /// <summary>A new connection to <paramref name="peer"/> starts: a close still waiting for the old one must not end it.</summary>
    internal static void KeepOpen(ProductUserId peer) => closing.RemoveAll(c => c.peer.ToString() == peer.ToString());

    void Update()
    {
        for (int i = closing.Count - 1; i >= 0; i--)
        {
            if (Time.realtimeSinceStartup < closing[i].at) continue;
            if (P2P != null && puid != null)
            {
                var options = new CloseConnectionOptions { LocalUserId = puid, RemoteUserId = closing[i].peer, SocketId = closing[i].socket };
                P2P.CloseConnection(ref options);
            }
            closing.RemoveAt(i);
        }
    }

    internal static async Task<T> Within<T>(Task<T> task, float seconds, string what)
    {
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))) != task) throw new TimeoutException(what + " got no answer in " + seconds + "s");
        return task.Result;
    }
}
