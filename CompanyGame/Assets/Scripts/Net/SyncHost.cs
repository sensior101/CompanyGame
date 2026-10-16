using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// Runs <see cref="GameServer"/> in Unity, ticked in Update. Two ways, same code:
/// in-app (<see cref="StartInApp"/>: this game is the server and its own SyncClient joins through 127.0.0.1), and a
/// separate process: <c>-gameserver &lt;port&gt; [-maxplayers N] [-bind address] [-serversettings path]</c>. In batch mode that
/// process drops the title scene and runs only the server. (<c>-server</c> is already the Cloudflare address argument.)
/// With <c>-serversettings</c> the process takes name, players and password from that file (<c>-gameserver</c>/<c>-maxplayers</c>
/// win) and keeps <c>running.json</c> next to it while it listens (<see cref="LocalServerProcess"/>).
/// Worlds (12.5): the server opens <c>ServerSettings.world</c> under <c>worlds/</c> next to the settings file (a new one when empty)
/// and saves it on <see cref="Stop"/>. The process checks for <c>stop.request</c> there every second: it saves and quits.
/// <c>-nightskip start-end</c> sets the hours the world clock skips (saved in the world, 12.9). <see cref="Started"/> and
/// <see cref="Stopped"/> let the game run its side of the server (ServerWorld) without Net knowing it.
/// </summary>
public sealed class SyncHost : MonoBehaviour
{
    public const int DefaultMaxPlayers = 50;

    public static SyncHost Instance { get; private set; }

    public GameServer Server { get; private set; }
    public int Port => transport != null ? transport.Port : 0;
    /// <summary>True when this game is the server and a player (<see cref="StartInApp"/>); the owner leaving stops it.</summary>
    public bool InApp { get; private set; }

    /// <summary>An in-app server is about to stop: the last character save goes out now (CharacterSync).</summary>
    public static event Action BeforeStop;
    /// <summary>A server runs in this process now (Server and InApp are set). The game side of the server hooks in here (ServerWorld, 12.9).</summary>
    public static event Action<SyncHost> Started;
    /// <summary>The server of this process stopped and saved; Server is still readable during the call.</summary>
    public static event Action<SyncHost> Stopped;

    TcpServerTransport transport;
    string runningPath, stopPath;
    float nextStopCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    /// <summary>
    /// Listens on every interface, or only on 127.0.0.1 with <paramref name="localOnly"/> (혼자 하기); settings.port 0 = any
    /// free port. Opens settings.world of this slot (a new world when empty: its id is written into settings) and connects
    /// this game's SyncClient with the password. Throws when the world cannot be read or the port cannot be bound.
    /// </summary>
    public static SyncHost StartInApp(ServerSettings settings, bool localOnly = false)
    {
        var world = OpenWorld(ServerSettings.WorldsFolder, settings);
        var host = Create(localOnly ? IPAddress.Loopback : IPAddress.Any, settings.port, settings.maxPlayers, settings.name, settings.password, world, true);
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

    static SyncHost Create(IPAddress address, int port, int maxPlayers, string name, string password, WorldStore world, bool inApp)
    {
        if (Instance) Instance.Stop();
        var transport = new TcpServerTransport(address, port); // throws before anything is made when the port is taken
        var go = new GameObject("SyncHost");
        DontDestroyOnLoad(go);
        var host = go.AddComponent<SyncHost>();
        host.transport = transport;
        host.Server = new GameServer(transport, maxPlayers, name, password, world);
        host.Server.Log += message => Debug.LogWarning("[SyncServer] " + message);
        host.InApp = inApp;
        Instance = host;
        Debug.Log("[SyncServer] '" + name + "' listening on " + address + ":" + host.Port + ", max " + maxPlayers + (string.IsNullOrEmpty(password) ? "" : ", password set")
                  + ", world " + world.Id + " (" + world.CharacterCount + " characters)");
        try { Started?.Invoke(host); } catch (Exception e) { Debug.LogException(e); }
        return host;
    }

    // Saves the world (the game's state provider is read during it), then tells the game side it is over.
    void Shutdown()
    {
        if (Server == null) return;
        Server.Stop();
        try { Stopped?.Invoke(this); } catch (Exception e) { Debug.LogException(e); }
        Server = null; transport = null;
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
        Server?.Tick();
        if (stopPath == null || Time.unscaledTime < nextStopCheck) return;
        nextStopCheck = Time.unscaledTime + 1f;
        if (!File.Exists(stopPath)) return;
        Debug.Log("[SyncServer] stop requested: saving the world and quitting");
        try { File.Delete(stopPath); } catch (Exception) { /* LocalServerProcess removes it too */ }
        Stop();
        Application.Quit(0);
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
            host = Create(address, p, settings.maxPlayers, settings.name, settings.password, world, false);
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
        string running = Path.Combine(folder, "running.json");
        var info = new LocalServerProcess.RunningInfo { pid = Process.GetCurrentProcess().Id, port = host.Port, startedAt = DateTime.UtcNow.ToString("o"), world = settings.world };
        try { File.WriteAllText(running, JsonUtility.ToJson(info)); host.runningPath = running; }
        catch (Exception e) { Debug.LogError("[SyncServer] cannot write " + running + ": " + e.Message); }
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
