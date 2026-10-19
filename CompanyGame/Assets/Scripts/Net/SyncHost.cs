using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// Runs <see cref="GameServer"/> in Unity, ticked in Update. Two ways, same code:
/// in-app (<see cref="StartInApp"/>: this game is the server and its own SyncClient joins through 127.0.0.1), and a
/// separate process: <c>-gameserver &lt;port&gt; [-maxplayers N] [-bind address] [-serversettings path]</c>. In batch mode that
/// process drops the title scene and runs only the server.
/// With <c>-serversettings</c> the process takes name, players and password from that file (<c>-gameserver</c>/<c>-maxplayers</c>
/// win) and keeps <c>running.json</c> next to it while it listens (<see cref="LocalServerProcess"/>).
/// Worlds (12.5): the server opens <c>ServerSettings.world</c> under <c>worlds/</c> next to the settings file (a new one when empty)
/// and saves it on <see cref="Stop"/>. The process checks for <c>stop.request</c> there every second: it saves and quits.
/// <c>-nightskip start-end</c> sets the hours the world clock skips (saved in the world, 12.9). <see cref="Started"/> and
/// <see cref="Stopped"/> let the game run its side of the server (ServerWorld) without Net knowing it.
/// EOS (12.13): a server that is not 혼자 하기 also takes EOS P2P connections (<see cref="MultiTransport"/>: TCP + EOS) and keeps
/// an EOS session (the public list and the server code) once this game's EOS login is done. Then every Hello's account is
/// checked (<see cref="CheckIdentity"/>): an EOS connection must be the PUID EOS gives, a TCP one must carry a valid EOS ID
/// token of it; only 127.0.0.1 saying the server's own PUID may come without one. A server without EOS trusts the Hello.
/// </summary>
public sealed class SyncHost : MonoBehaviour
{
    public const int DefaultMaxPlayers = 50;

    public static SyncHost Instance { get; private set; }

    public GameServer Server { get; private set; }
    public int Port => tcp != null ? tcp.Port : 0;
    /// <summary>The server code once its EOS session exists (the code then works), else "".</summary>
    public string Code => session != null && session.Created ? session.Code : "";
    /// <summary>The EOS session (null without EOS), for QA.</summary>
    public EosSession Session => session;
    /// <summary>QA only: false refuses a token-less 127.0.0.1 Hello even with the server's own PUID.</summary>
    public static bool LoopbackException = true;
    /// <summary>True when this game is the server and a player (<see cref="StartInApp"/>); the owner leaving stops it.</summary>
    public bool InApp { get; private set; }

    /// <summary>An in-app server is about to stop: the last character save goes out now (CharacterSync).</summary>
    public static event Action BeforeStop;
    /// <summary>A server runs in this process now (Server and InApp are set). The game side of the server hooks in here (ServerWorld, 12.9).</summary>
    public static event Action<SyncHost> Started;
    /// <summary>The server of this process stopped and saved; Server is still readable during the call.</summary>
    public static event Action<SyncHost> Stopped;

    MultiTransport transport;
    TcpServerTransport tcp;
    EosServerTransport eos;
    EosSession session;
    ServerSettings settings;
    bool wantEos, warnedTrust;
    string runningPath, stopPath, runningCode = "";
    float nextStopCheck;
    LocalServerProcess.RunningInfo running;
    static Task lastDestroy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; LoopbackException = true; lastDestroy = null; }

    /// <summary>
    /// Listens on every interface, or only on 127.0.0.1 with <paramref name="localOnly"/> (혼자 하기); settings.port 0 = any
    /// free port. Opens settings.world of this slot (a new world when empty: its id is written into settings) and connects
    /// this game's SyncClient with the password. Throws when the world cannot be read or the port cannot be bound.
    /// </summary>
    public static SyncHost StartInApp(ServerSettings settings, bool localOnly = false)
    {
        var world = OpenWorld(ServerSettings.WorldsFolder, settings);
        var host = Create(localOnly ? IPAddress.Loopback : IPAddress.Any, settings, world, true, !localOnly);
        SyncClient.Instance.Connect("127.0.0.1", host.Port, settings.password);
        return host;
    }

    /// <summary>settings.world under <paramref name="root"/>; a new world (id written into settings) when it is empty.</summary>
    public static WorldStore OpenWorld(string root, ServerSettings settings)
    {
        if (string.IsNullOrEmpty(settings.world)) settings.world = WorldStore.NewId();
        var world = WorldStore.Open(root, settings.world);
        if (world.LoadedFromPrev) Debug.LogWarning("[SyncServer] world.dat of " + world.Id + " was unreadable; loaded world.prev.dat");
        return world;
    }

    // eos: take EOS connections and keep a session once the login is done (not for 혼자 하기).
    static SyncHost Create(IPAddress address, ServerSettings settings, WorldStore world, bool inApp, bool eos)
    {
        if (Instance) Instance.Stop();
        var tcp = new TcpServerTransport(address, settings.port); // throws before anything is made when the port is taken
        var go = new GameObject("SyncHost");
        DontDestroyOnLoad(go);
        var host = go.AddComponent<SyncHost>();
        host.tcp = tcp;
        host.transport = new MultiTransport(tcp);
        host.settings = settings;
        host.Server = new GameServer(host.transport, settings.maxPlayers, settings.name, settings.password, world);
        host.Server.Log += message => Debug.LogWarning("[SyncServer] " + message);
        host.InApp = inApp;
        host.wantEos = eos && EosAccount.OffReason() == null;
        if (host.wantEos) host.Server.IdentityCheck = host.CheckIdentity;
        else if (eos) Debug.LogWarning("[SyncServer] no EOS (" + EosAccount.OffReason() + "): TCP only, and every Hello's account is trusted");
        Instance = host;
        Debug.Log("[SyncServer] '" + settings.name + "' listening on " + address + ":" + host.Port + ", max " + settings.maxPlayers + (string.IsNullOrEmpty(settings.password) ? "" : ", password set")
                  + ", world " + world.Id + " (" + world.CharacterCount + " characters)" + (host.wantEos ? ", EOS once logged in (code " + ServerSettings.CodeOf(world.Id) + ")" : ""));
        try { Started?.Invoke(host); } catch (Exception e) { Debug.LogException(e); }
        return host;
    }

    // EOS is logged in now: EOS connections join the TCP ones, and the session is made (the next Updates send it).
    void AttachEos()
    {
        eos = new EosServerTransport();
        transport.Add(eos);
        session = new EosSession(ServerSettings.CodeOf(Server.World.Id), settings.name, settings.maxPlayers, settings.password.Length > 0, settings.isPublic);
        Debug.Log("[SyncServer] EOS P2P on, session " + session.Code + (settings.isPublic ? " (public)" : " (private)"));
    }

    /// <summary>
    /// GameServer.IdentityCheck (12.13). Waits for this server's EOS login if it is still going; a server whose login
    /// failed trusts the Hello like a server without EOS.
    /// </summary>
    async Task<bool> CheckIdentity(int connection, string account, string token)
    {
        if (!EosAccount.Settled.IsCompleted) await EosAccount.WhenSettled(9); // still logging in (or not booted yet): inside the 10 s Hello limit
        if (EosAccount.State != EosAccount.Status.Ready)
        {
            if (!warnedTrust) Debug.LogWarning("[SyncServer] EOS login failed (" + EosAccount.Problem + "): Hello accounts are trusted");
            warnedTrust = true;
            return true;
        }
        if (transport == null || !transport.Resolve(connection, out var part, out int inner)) return false;
        string how;
        if (part is EosServerTransport p2p) how = p2p.PeerOf(inner) == account ? "EOS P2P" : null;
        else if (!string.IsNullOrEmpty(token)) how = await EosAccount.Verify(account, token) ? "ID token" : null;
        else how = LoopbackException && account == EosAccount.LocalPuid && IPAddress.IsLoopback(((TcpServerTransport)part).RemoteAddress(inner) ?? IPAddress.None)
            ? "own PUID on 127.0.0.1" : null;
        Debug.Log("[SyncServer] identity of " + Short(account) + (how != null ? " confirmed (" + how + ")"
            : " NOT confirmed (" + (part is EosServerTransport ? "EOS P2P peer differs" : string.IsNullOrEmpty(token) ? "no ID token" : "bad ID token") + ")"));
        return how != null;
    }

    static string Short(string account) => string.IsNullOrEmpty(account) ? "(none)" : account.Substring(0, Math.Min(4, account.Length)) + "…";

    // Saves the world (the game's state provider is read during it), then tells the game side it is over. The EOS session
    // is removed (the process waits for that before quitting, at most 3 seconds).
    void Shutdown()
    {
        if (Server == null) return;
        Server.Stop();
        if (session != null) lastDestroy = session.Destroy();
        try { Stopped?.Invoke(this); } catch (Exception e) { Debug.LogException(e); }
        Server = null; transport = null; tcp = null; eos = null; session = null;
    }

    /// <summary>Saves the world and stops. In-app: my own last character save is read first (at most 1 second).</summary>
    public void Stop()
    {
        if (InApp && Server != null)
        {
            try { BeforeStop?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            if (SyncClient.Instance) SyncClient.Instance.Disconnect();
            // The save travels through 127.0.0.1 before the disconnect (TCP keeps the order): tick until my connection is gone.
            var until = DateTime.UtcNow.AddSeconds(1);
            while (Server.HasAccount(GameSession.LocalPlayerId) && DateTime.UtcNow < until) { Server.Tick(); Thread.Sleep(5); }
        }
        Shutdown();
        if (Instance == this) Instance = null;
        DeleteRunning();
        Destroy(gameObject);
    }

    void Update()
    {
        if (Server == null) return;
        if (wantEos && eos == null && EosAccount.State == EosAccount.Status.Ready) AttachEos();
        eos?.Pump();
        Server.Tick();
        session?.Tick(Server.PlayerCount);
        if (runningPath != null && Code != runningCode) WriteRunning(); // the code works now: the title shows it
        if (stopPath == null || Time.unscaledTime < nextStopCheck) return;
        nextStopCheck = Time.unscaledTime + 1f;
        if (!File.Exists(stopPath)) return;
        Debug.Log("[SyncServer] stop requested: saving the world and quitting");
        try { File.Delete(stopPath); } catch (Exception) { /* LocalServerProcess removes it too */ }
        Stop();
        QuitAfter(lastDestroy);
    }

    static async void QuitAfter(Task wait)
    {
        if (wait != null) await wait; // the session removal (EosSession.Destroy gives up after 3 seconds)
        Application.Quit(0);
    }

    void WriteRunning()
    {
        running.code = runningCode = Code;
        try { File.WriteAllText(runningPath, JsonUtility.ToJson(running)); }
        catch (Exception e) { Debug.LogError("[SyncServer] cannot write " + runningPath + ": " + e.Message); }
    }

    void OnDestroy() { Shutdown(); if (Instance == this) Instance = null; DeleteRunning(); }

    void OnApplicationQuit()
    {
        if (Server != null) Stop(); // saves the world
        DeleteRunning();
    }

    void DeleteRunning()
    {
        if (runningPath == null) return;
        try { File.Delete(runningPath); } catch (Exception) { /* the title treats a dead pid as not running anyway */ }
        runningPath = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void FromCommandLine()
    {
        string port = Arg("-gameserver");
        if (port == null) return;
        if (!int.TryParse(port, out int p)) { Debug.LogError("[SyncServer] bad -gameserver port: " + port); return; }
        string settingsPath = Arg("-serversettings");
        var settings = settingsPath != null ? ServerSettings.Load(settingsPath) : new ServerSettings { name = "", maxPlayers = DefaultMaxPlayers };
        if (int.TryParse(Arg("-maxplayers"), out int max) && max >= 1) settings.maxPlayers = max;
        var address = IPAddress.TryParse(Arg("-bind") ?? "", out var bind) ? bind : IPAddress.Any;
        string folder = settingsPath != null ? Path.GetDirectoryName(Path.GetFullPath(settingsPath)) : ServerSettings.Folder;
        if (Application.isBatchMode)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60; // the tick rate; batch mode would otherwise spin a core
            var title = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("GameServer"));
            SceneManager.UnloadSceneAsync(title);
        }
        SyncHost host;
        try
        {
            var world = OpenWorld(Path.Combine(folder, "worlds"), settings);
            ApplyNightSkip(world, Arg("-nightskip"));
            settings.port = p;
            host = Create(address, settings, world, false, true);
        }
        catch (Exception e)
        {
            Debug.LogError("[SyncServer] cannot open world '" + settings.world + "' or listen on port " + p + ": " + e.Message);
            if (Application.isBatchMode) Application.Quit(1); // a windowless server that does not serve must not linger
            return;
        }
        if (settingsPath == null) return;
        host.stopPath = Path.Combine(folder, LocalServerProcess.StopRequestName);
        try { File.Delete(host.stopPath); } catch (Exception) { /* checked again every second */ }
        host.running = new LocalServerProcess.RunningInfo { pid = Process.GetCurrentProcess().Id, port = host.Port, startedAt = DateTime.UtcNow.ToString("o"), world = settings.world };
        host.runningPath = Path.Combine(folder, "running.json");
        host.WriteRunning();
    }

    // -nightskip "start-end": set on the world before the server opens it, so the clock is never put on a skipped hour first.
    static void ApplyNightSkip(WorldStore world, string arg)
    {
        if (arg == null) return;
        if (GameServer.ApplyNightSkipArg(world, arg)) Debug.Log("[SyncServer] night skip " + world.NightSkipStart + "-" + world.NightSkipEnd + " (saved in the world)");
        else Debug.LogError("[SyncServer] bad -nightskip '" + arg + "' (hours start-end, 0 <= start <= end <= 24); the world keeps " + world.NightSkipStart + "-" + world.NightSkipEnd);
    }

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
