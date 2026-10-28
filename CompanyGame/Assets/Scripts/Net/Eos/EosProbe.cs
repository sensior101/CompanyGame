using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Sessions;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// EOS test mode (Docs/Multiplayer_Redesign.md 12.4), apart from the game: nothing happens without <c>-eostest</c>.
///   <c>-eostest server</c>         Device ID login, PUID, echo on P2P socket "eostest", a session with name/players/password attributes
///                                  and a chosen session id. Runs until closed; in batch mode for <c>-eostest-minutes N</c> (default 10, 0 = forever).
///   <c>-eostest client &lt;PUID&gt;</c> Device ID login, NAT type, P2P to that PUID with 20 pings (round trip, direct or relayed),
///                                  session search by attribute and by session id, then quits.
/// Options: <c>-eostest-code &lt;id&gt;</c> session id (16-64 chars; server makes one when missing), <c>-eostest-name</c>, <c>-eostest-max</c>,
/// <c>-eostest-password</c> (only marks "has password"), <c>-eostest-log &lt;path&gt;</c>.
/// Every result is a <c>KEY=value</c> line in the Unity log and in <c>eos_test_log.txt</c> next to the exe (editor: Logs/).
/// </summary>
public sealed class EosProbe : MonoBehaviour
{
    public const string Bucket = "companygame:eostest";
    const string LocalSession = "eostest";
    const int Pings = 20;

    public static EosProbe Current { get; private set; }

    /// <summary>Every line written, for the editor QA.</summary>
    public readonly List<string> Lines = new List<string>();
    public ProductUserId Puid { get; private set; }
    /// <summary>Completes with the exit code when the probe has finished (a server only when its time is up).</summary>
    public Task<int> Finished => finished.Task;
    public string LogPath { get; private set; }

    readonly TaskCompletionSource<int> finished = new TaskCompletionSource<int>();
    readonly Queue<(ProductUserId peer, byte[] data)> received = new Queue<(ProductUserId, byte[])>();
    string mode, serverPuid, code, serverName;
    int maxPlayers, minutes;
    bool hasPassword;
    SocketId socket = new SocketId { SocketName = "eostest" };
    ConnectInterface connect;
    P2PInterface p2p;
    SessionsInterface sessions;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Current = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void FromCommandLine()
    {
        string mode = Arg("-eostest");
        if (mode == null) return;
        if (Application.isBatchMode)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 200; // EOS callbacks and packets are handled once a frame; a high rate keeps that out of the ping times
            var title = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("EosProbe"));
            SceneManager.UnloadSceneAsync(title);
        }
        int.TryParse(Arg("-eostest-minutes") ?? "", out int minutes);
        if (Arg("-eostest-minutes") == null) minutes = Application.isBatchMode ? 10 : 0;
        Begin(mode, mode == "client" ? Arg("client") : null, Arg("-eostest-code"), minutes, Arg("-eostest-log"));
    }

    /// <summary>Adds the plugin's EOSManager (which creates the EOS platform) and starts the probe. The editor QA calls this in play mode.</summary>
    public static EosProbe Begin(string mode, string serverPuid, string code, int minutes, string logPath = null)
    {
        var go = new GameObject("EosProbe");
        DontDestroyOnLoad(go);
        var probe = go.AddComponent<EosProbe>();
        probe.mode = mode;
        probe.serverPuid = serverPuid;
        probe.code = code;
        probe.minutes = minutes;
        probe.serverName = Arg("-eostest-name") ?? "EOS 시험 서버";
        probe.maxPlayers = int.TryParse(Arg("-eostest-max") ?? "", out int max) && max >= 2 ? max : 50;
        probe.hasPassword = Environment.GetCommandLineArgs().Contains("-eostest-password");
        probe.LogPath = Path.GetFullPath(logPath ?? Path.Combine(Path.GetDirectoryName(Application.dataPath), Application.isEditor ? "Logs" : "", "eos_test_log.txt"));
        Current = probe;
        try { File.WriteAllText(probe.LogPath, ""); }
        catch (Exception e) { Debug.LogError("[EosProbe] cannot write " + probe.LogPath + ": " + e.Message); }
        probe.Log("EOSTEST mode=" + mode + " pid=" + Process.GetCurrentProcess().Id + " batch=" + Application.isBatchMode + " version=" + Application.version);
        probe.Log("PID=" + Process.GetCurrentProcess().Id);
        probe.Log("LOG=" + probe.LogPath);
        go.AddComponent<EOSManager>(); // Awake creates the platform from StreamingAssets/EOS (or takes the one the native plugin made)
        probe.Run();
        return probe;
    }

    async void Run()
    {
        int exit = 0;
        try
        {
            var platform = EOSManager.Instance.GetEOSPlatformInterface();
            if (platform == null) throw new Exception("no EOS platform (EOSManager init failed, see the Unity log; StreamingAssets/EOS config missing?)");
            connect = platform.GetConnectInterface();
            p2p = platform.GetP2PInterface();
            sessions = platform.GetSessionsInterface();
            WatchLogin();
            Puid = await Login(mode == "server" ? "eostest server" : "eostest client");
            Log("PUID=" + Puid);
            ListenP2P();
            Log("NAT=" + await NatType());
            if (mode == "server") await Server();
            else if (mode == "client") await Client();
            else throw new Exception("unknown -eostest mode '" + mode + "' (server or client)");
            Log("RESULT done");
        }
        catch (Exception e)
        {
            Log("FAILED " + e.Message);
            exit = 1;
        }
        finished.TrySetResult(exit);
        if (!Application.isEditor && (mode != "server" || exit != 0 || minutes > 0)) Application.Quit(exit);
    }

    // ================= login =================

    async Task<ProductUserId> Login(string displayName)
    {
        var created = new TaskCompletionSource<Result>();
        var deviceOptions = new CreateDeviceIdOptions { DeviceModel = SystemInfo.deviceModel.Length > 0 ? SystemInfo.deviceModel : "PC" };
        connect.CreateDeviceId(ref deviceOptions, null, (ref CreateDeviceIdCallbackInfo info) => created.TrySetResult(info.ResultCode));
        Result device = await Within(created.Task, 30, "CreateDeviceId");
        Log("CREATE_DEVICE_ID=" + device);
        if (device != Result.Success && device != Result.DuplicateNotAllowed) throw new Exception("CreateDeviceId " + device);

        var login = new TaskCompletionSource<LoginCallbackInfo>();
        EOSManager.Instance.StartConnectLoginWithDeviceToken(displayName, info => login.TrySetResult(info));
        var result = await Within(login.Task, 30, "Connect.Login");
        Log("CONNECT_LOGIN=" + result.ResultCode);
        if (result.ResultCode == Result.InvalidUser && result.ContinuanceToken != null)
        {
            // first login of this device: make the EOS user from the continuance token
            var user = new TaskCompletionSource<CreateUserCallbackInfo>();
            var options = new CreateUserOptions { ContinuanceToken = result.ContinuanceToken };
            connect.CreateUser(ref options, null, (ref CreateUserCallbackInfo info) => user.TrySetResult(info));
            var made = await Within(user.Task, 30, "Connect.CreateUser");
            Log("CONNECT_CREATE_USER=" + made.ResultCode);
            if (made.ResultCode != Result.Success) throw new Exception("Connect.CreateUser " + made.ResultCode);
            return made.LocalUserId;
        }
        if (result.ResultCode != Result.Success) throw new Exception("Connect.Login " + result.ResultCode);
        return result.LocalUserId;
    }

    void WatchLogin()
    {
        var statusOptions = new AddNotifyLoginStatusChangedOptions();
        connect.AddNotifyLoginStatusChanged(ref statusOptions, null,
            (ref LoginStatusChangedCallbackInfo info) => Log("LOGIN_STATUS_CHANGED " + info.PreviousStatus + " -> " + info.CurrentStatus));
        var expiryOptions = new AddNotifyAuthExpirationOptions();
        connect.AddNotifyAuthExpiration(ref expiryOptions, null, (ref AuthExpirationCallbackInfo info) => Log("AUTH_EXPIRING"));
    }

    // ================= P2P =================

    void ListenP2P()
    {
        var request = new AddNotifyPeerConnectionRequestOptions { LocalUserId = Puid, SocketId = socket };
        p2p.AddNotifyPeerConnectionRequest(ref request, null, (ref OnIncomingConnectionRequestInfo info) =>
        {
            var accept = new AcceptConnectionOptions { LocalUserId = Puid, RemoteUserId = info.RemoteUserId, SocketId = socket };
            Log("P2P_REQUEST from=" + info.RemoteUserId + " accept=" + p2p.AcceptConnection(ref accept));
        });
        var established = new AddNotifyPeerConnectionEstablishedOptions { LocalUserId = Puid, SocketId = socket };
        p2p.AddNotifyPeerConnectionEstablished(ref established, null, (ref OnPeerConnectionEstablishedInfo info) =>
            Log("P2P_ESTABLISHED peer=" + info.RemoteUserId + " network=" + info.NetworkType + " type=" + info.ConnectionType));
        var interrupted = new AddNotifyPeerConnectionInterruptedOptions { LocalUserId = Puid, SocketId = socket };
        p2p.AddNotifyPeerConnectionInterrupted(ref interrupted, null, (ref OnPeerConnectionInterruptedInfo info) =>
            Log("P2P_INTERRUPTED peer=" + info.RemoteUserId));
        var closed = new AddNotifyPeerConnectionClosedOptions { LocalUserId = Puid, SocketId = socket };
        p2p.AddNotifyPeerConnectionClosed(ref closed, null, (ref OnRemoteConnectionClosedInfo info) =>
            Log("P2P_CLOSED peer=" + info.RemoteUserId + " reason=" + info.Reason));
    }

    async Task<NATType> NatType()
    {
        var done = new TaskCompletionSource<OnQueryNATTypeCompleteInfo>();
        var options = new QueryNATTypeOptions();
        p2p.QueryNATType(ref options, null, (ref OnQueryNATTypeCompleteInfo info) => done.TrySetResult(info));
        var result = await Within(done.Task, 30, "QueryNATType");
        if (result.ResultCode != Result.Success) Log("NAT_QUERY=" + result.ResultCode);
        return result.NATType;
    }

    Result Send(ProductUserId peer, byte[] data)
    {
        var options = new SendPacketOptions
        {
            LocalUserId = Puid, RemoteUserId = peer, SocketId = socket, Channel = 0, Data = new ArraySegment<byte>(data),
            AllowDelayedDelivery = true, Reliability = PacketReliability.ReliableOrdered,
        };
        return p2p.SendPacket(ref options);
    }

    void Update()
    {
        if (p2p == null || Puid == null) return;
        while (true)
        {
            var sizeOptions = new GetNextReceivedPacketSizeOptions { LocalUserId = Puid };
            if (p2p.GetNextReceivedPacketSize(ref sizeOptions, out uint size) != Result.Success) break;
            var options = new ReceivePacketOptions { LocalUserId = Puid, MaxDataSizeBytes = size };
            ProductUserId peer = null;
            var from = new SocketId();
            var buffer = new byte[size];
            if (p2p.ReceivePacket(ref options, ref peer, ref from, out _, new ArraySegment<byte>(buffer), out uint length) != Result.Success) break;
            Array.Resize(ref buffer, (int)length);
            if (mode == "server") Send(peer, Encoding.UTF8.GetBytes("echo " + Encoding.UTF8.GetString(buffer))); // marked, so a packet that loops back to its sender is not taken for an echo
            else received.Enqueue((peer, buffer));
        }
    }

    // ================= server =================

    async Task Server()
    {
        if (string.IsNullOrEmpty(code)) code = "CGTEST" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant();
        var create = new CreateSessionModificationOptions
        {
            SessionName = LocalSession, BucketId = Bucket, MaxPlayers = (uint)maxPlayers, LocalUserId = Puid, PresenceEnabled = false, SessionId = code,
        };
        Check(sessions.CreateSessionModification(ref create, out SessionModification modification), "CreateSessionModification");
        Attribute(modification, "NAME", serverName);
        Attribute(modification, "MAXP", (long?)maxPlayers);
        Attribute(modification, "HASPW", (bool?)hasPassword);
        Attribute(modification, "KIND", "eostest");
        var permission = new SessionModificationSetPermissionLevelOptions { PermissionLevel = OnlineSessionPermissionLevel.PublicAdvertised };
        Check(modification.SetPermissionLevel(ref permission), "SetPermissionLevel");
        var joinInProgress = new SessionModificationSetJoinInProgressAllowedOptions { AllowJoinInProgress = true };
        Check(modification.SetJoinInProgressAllowed(ref joinInProgress), "SetJoinInProgressAllowed");
        var updated = new TaskCompletionSource<UpdateSessionCallbackInfo>();
        var update = new UpdateSessionOptions { SessionModificationHandle = modification };
        sessions.UpdateSession(ref update, null, (ref UpdateSessionCallbackInfo info) => updated.TrySetResult(info));
        var made = await Within(updated.Task, 30, "UpdateSession");
        modification.Release();
        Log("SESSION_CREATE=" + made.ResultCode + " id=" + made.SessionId + " name=" + serverName + " max=" + maxPlayers + " haspw=" + hasPassword);
        if (made.ResultCode != Result.Success) throw new Exception("UpdateSession " + made.ResultCode);
        Log("SESSION=" + made.SessionId);

        // alive check every 30 s: still logged in, and an authenticated call (search for my own session) still works
        var until = minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : DateTime.MaxValue;
        while (DateTime.UtcNow < until && this)
        {
            await Task.Delay(30000);
            if (!this) return;
            var (result, found) = await Search(code: made.SessionId, write: false);
            Log("ALIVE status=" + connect.GetLoginStatus(Puid) + " search=" + result + " found=" + found.Count);
        }
        var destroyed = new TaskCompletionSource<Result>();
        var destroy = new DestroySessionOptions { SessionName = LocalSession };
        sessions.DestroySession(ref destroy, null, (ref DestroySessionCallbackInfo info) => destroyed.TrySetResult(info.ResultCode));
        Log("SESSION_DESTROY=" + await Within(destroyed.Task, 15, "DestroySession"));
    }

    static void Attribute(SessionModification modification, string key, AttributeDataValue value)
    {
        var options = new SessionModificationAddAttributeOptions
        {
            SessionAttribute = new AttributeData { Key = key, Value = value }, AdvertisementType = SessionAttributeAdvertisementType.Advertise,
        };
        Check(modification.AddAttribute(ref options), "AddAttribute " + key);
    }

    // ================= client =================

    async Task Client()
    {
        if (!string.IsNullOrEmpty(serverPuid))
        {
            var server = ProductUserId.FromString(serverPuid.Trim());
            if (server == null || !server.IsValid()) Log("P2P_SKIPPED bad PUID '" + serverPuid + "'");
            else await Ping(server);
        }
        else Log("P2P_SKIPPED no server PUID");

        var (byAttribute, list) = await Search(code: null, write: true);
        Log("SEARCH_ATTR=" + byAttribute + " count=" + list.Count);
        string target = !string.IsNullOrEmpty(code) ? code
            : list.FirstOrDefault(s => s.owner == serverPuid?.Trim()).id ?? list.FirstOrDefault().id;
        if (target == null) { Log("SEARCH_ID_SKIPPED no session to look for"); return; }
        var (byId, one) = await Search(code: target, write: true);
        Log("SEARCH_ID=" + byId + " id=" + target + " count=" + one.Count + " match=" + one.Any(s => s.id == target));
        Log("LOGIN_STATUS=" + connect.GetLoginStatus(Puid));
    }

    async Task Ping(ProductUserId server)
    {
        Log("P2P_CONNECT to=" + server + (server.ToString() == Puid.ToString() ? " (same PUID as me: same OS user / device id)" : ""));
        var rtts = new List<double>();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < Pings; i++)
        {
            byte[] ping = Encoding.UTF8.GetBytes("ping " + i);
            double sent = clock.Elapsed.TotalMilliseconds;
            Result result = Send(server, ping);
            if (result != Result.Success) { Log("PING " + i + " send=" + result); break; }
            float wait = i == 0 ? 30 : 5; // the first one also sets up the connection (and the relay when direct fails)
            bool back = false;
            var until = DateTime.UtcNow.AddSeconds(wait);
            while (!back && DateTime.UtcNow < until && this)
            {
                await Task.Yield();
                while (received.Count > 0) back |= Encoding.UTF8.GetString(received.Dequeue().data) == "echo ping " + i;
            }
            if (!back) { Log("PING " + i + " timeout " + wait + "s"); if (i == 0) break; continue; }
            double rtt = clock.Elapsed.TotalMilliseconds - sent;
            rtts.Add(rtt);
            Log("PING " + i + " rtt=" + rtt.ToString("0.0") + "ms");
            await Task.Delay(100);
        }
        Log("PING_SUMMARY ok=" + rtts.Count + "/" + Pings + (rtts.Count > 0
            ? " min=" + rtts.Min().ToString("0.0") + " avg=" + rtts.Average().ToString("0.0") + " max=" + rtts.Max().ToString("0.0") + "ms (includes up to a frame on each side)"
            : ""));
    }

    /// <summary>One session search: by session id when <paramref name="code"/> is set, otherwise by bucket and the KIND attribute.</summary>
    async Task<(Result, List<(string id, string owner)>)> Search(string code, bool write)
    {
        var found = new List<(string id, string owner)>();
        var create = new CreateSessionSearchOptions { MaxSearchResults = 50 };
        Check(sessions.CreateSessionSearch(ref create, out SessionSearch search), "CreateSessionSearch");
        try
        {
            if (code != null)
            {
                var byId = new SessionSearchSetSessionIdOptions { SessionId = code };
                Check(search.SetSessionId(ref byId), "SetSessionId");
            }
            else
            {
                var bucket = new SessionSearchSetParameterOptions { Parameter = new AttributeData { Key = SessionsInterface.SEARCH_BUCKET_ID, Value = Bucket }, ComparisonOp = ComparisonOp.Equal };
                Check(search.SetParameter(ref bucket), "SetParameter bucket");
                var kind = new SessionSearchSetParameterOptions { Parameter = new AttributeData { Key = "KIND", Value = "eostest" }, ComparisonOp = ComparisonOp.Equal };
                Check(search.SetParameter(ref kind), "SetParameter KIND");
            }
            var done = new TaskCompletionSource<Result>();
            var find = new SessionSearchFindOptions { LocalUserId = Puid };
            search.Find(ref find, null, (ref SessionSearchFindCallbackInfo info) => done.TrySetResult(info.ResultCode));
            Result result = await Within(done.Task, 30, "SessionSearch.Find");
            if (result != Result.Success) return (result, found);
            var countOptions = new SessionSearchGetSearchResultCountOptions();
            uint count = search.GetSearchResultCount(ref countOptions);
            for (uint i = 0; i < count; i++)
            {
                var copy = new SessionSearchCopySearchResultByIndexOptions { SessionIndex = i };
                if (search.CopySearchResultByIndex(ref copy, out SessionDetails details) != Result.Success) continue;
                var infoOptions = new SessionDetailsCopyInfoOptions();
                details.CopyInfo(ref infoOptions, out SessionDetailsInfo? info);
                string id = info?.SessionId, owner = info?.OwnerUserId?.ToString();
                found.Add((id, owner));
                if (write)
                    Log("SEARCH_RESULT id=" + id + " owner=" + owner + " name=" + Text(details, "NAME") + " max=" + Text(details, "MAXP")
                        + " haspw=" + Text(details, "HASPW") + " open=" + info?.NumOpenPublicConnections);
                details.Release();
            }
            return (result, found);
        }
        finally { search.Release(); }
    }

    static string Text(SessionDetails details, string key)
    {
        var options = new SessionDetailsCopySessionAttributeByKeyOptions { AttrKey = key };
        if (details.CopySessionAttributeByKey(ref options, out SessionDetailsAttribute? attribute) != Result.Success || attribute?.Data == null) return "?";
        var value = attribute.Value.Data.Value.Value;
        return value.AsUtf8 != null ? (string)value.AsUtf8 : value.AsInt64?.ToString() ?? value.AsBool?.ToString() ?? value.AsDouble?.ToString() ?? "?";
    }

    // ================= helpers =================

    static void Check(Result result, string what)
    {
        if (result != Result.Success) throw new Exception(what + " " + result);
    }

    static async Task<T> Within<T>(Task<T> task, float seconds, string what)
    {
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))) != task) throw new TimeoutException(what + " got no answer in " + seconds + "s");
        return task.Result;
    }

    void Log(string line)
    {
        Lines.Add(line);
        Debug.Log("[EosProbe] " + line);
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine); }
        catch (Exception) { /* the Unity log has it */ }
    }

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
