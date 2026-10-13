using System;
using System.Diagnostics;
using System.IO;
using System.Net;
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
/// </summary>
public sealed class SyncHost : MonoBehaviour
{
    public const int DefaultMaxPlayers = 50;

    public static SyncHost Instance { get; private set; }

    public GameServer Server { get; private set; }
    public int Port => transport != null ? transport.Port : 0;
    /// <summary>True when this game is the server and a player (<see cref="StartInApp"/>); the owner leaving stops it.</summary>
    public bool InApp { get; private set; }

    TcpServerTransport transport;
    string runningPath;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    /// <summary>
    /// Listens on every interface (settings.port 0 = any free port) and connects this game's SyncClient to it with the password.
    /// Throws when the port cannot be bound.
    /// </summary>
    public static SyncHost StartInApp(ServerSettings settings)
    {
        var host = Create(IPAddress.Any, settings.port, settings.maxPlayers, settings.name, settings.password);
        host.InApp = true;
        SyncClient.Instance.Connect("127.0.0.1", host.Port, settings.password);
        return host;
    }

    static SyncHost Create(IPAddress address, int port, int maxPlayers, string name, string password)
    {
        if (Instance) Instance.Stop();
        var transport = new TcpServerTransport(address, port); // throws before anything is made when the port is taken
        var go = new GameObject("SyncHost");
        DontDestroyOnLoad(go);
        var host = go.AddComponent<SyncHost>();
        host.transport = transport;
        host.Server = new GameServer(transport, maxPlayers, name, password);
        host.Server.Log += message => Debug.LogWarning("[SyncServer] " + message);
        Instance = host;
        Debug.Log("[SyncServer] '" + name + "' listening on " + address + ":" + host.Port + ", max " + maxPlayers + (string.IsNullOrEmpty(password) ? "" : ", password set"));
        return host;
    }

    public void Stop()
    {
        Server?.Stop();
        Server = null; transport = null;
        if (Instance == this) Instance = null;
        DeleteRunning();
        Destroy(gameObject);
    }

    void Update() => Server?.Tick();
    void OnDestroy() { Server?.Stop(); DeleteRunning(); }
    void OnApplicationQuit() => DeleteRunning();

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
        if (Application.isBatchMode)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60; // the tick rate; batch mode would otherwise spin a core
            var title = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("GameServer"));
            SceneManager.UnloadSceneAsync(title);
        }
        SyncHost host;
        try { host = Create(address, p, settings.maxPlayers, settings.name, settings.password); }
        catch (Exception e)
        {
            Debug.LogError("[SyncServer] cannot listen on port " + p + ": " + e.Message);
            if (Application.isBatchMode) Application.Quit(1); // a windowless server that does not serve must not linger
            return;
        }
        if (settingsPath == null) return;
        string running = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "running.json");
        var info = new LocalServerProcess.RunningInfo { pid = Process.GetCurrentProcess().Id, port = host.Port, startedAt = DateTime.UtcNow.ToString("o") };
        try { File.WriteAllText(running, JsonUtility.ToJson(info)); host.runningPath = running; }
        catch (Exception e) { Debug.LogError("[SyncServer] cannot write " + running + ": " + e.Message); }
    }

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
