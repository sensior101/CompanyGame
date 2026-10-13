using System;
using System.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs <see cref="GameServer"/> in Unity, ticked in Update. Two ways, same code:
/// in-app (<see cref="StartInApp"/>: this game is the server and its own SyncClient joins through 127.0.0.1), and a
/// separate process: <c>-gameserver &lt;port&gt; [-maxplayers N] [-bind address]</c>. In batch mode that process drops the
/// title scene and runs only the server. (<c>-server</c> is already the Cloudflare address argument.)
/// </summary>
public sealed class SyncHost : MonoBehaviour
{
    public const int DefaultMaxPlayers = 50;

    public static SyncHost Instance { get; private set; }

    public GameServer Server { get; private set; }
    public int Port => transport != null ? transport.Port : 0;

    TcpServerTransport transport;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    /// <summary>Listens on every interface (port 0 = any free port) and connects this game's SyncClient to it.</summary>
    public static SyncHost StartInApp(int port, int maxPlayers)
    {
        var host = Create(IPAddress.Any, port, maxPlayers);
        SyncClient.Instance.Connect("127.0.0.1", host.Port);
        return host;
    }

    static SyncHost Create(IPAddress address, int port, int maxPlayers)
    {
        if (Instance) Instance.Stop();
        var go = new GameObject("SyncHost");
        DontDestroyOnLoad(go);
        var host = go.AddComponent<SyncHost>();
        host.transport = new TcpServerTransport(address, port);
        host.Server = new GameServer(host.transport, maxPlayers);
        host.Server.Log += message => Debug.LogWarning("[SyncServer] " + message);
        Instance = host;
        Debug.Log("[SyncServer] listening on " + address + ":" + host.Port + ", max " + maxPlayers);
        return host;
    }

    public void Stop()
    {
        Server?.Stop();
        Server = null; transport = null;
        if (Instance == this) Instance = null;
        Destroy(gameObject);
    }

    void Update() => Server?.Tick();
    void OnDestroy() => Server?.Stop();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void FromCommandLine()
    {
        string port = Arg("-gameserver");
        if (port == null) return;
        if (!int.TryParse(port, out int p)) { Debug.LogError("[SyncServer] bad -gameserver port: " + port); return; }
        if (!int.TryParse(Arg("-maxplayers"), out int max) || max < 1) max = DefaultMaxPlayers;
        var address = IPAddress.TryParse(Arg("-bind") ?? "", out var bind) ? bind : IPAddress.Any;
        if (Application.isBatchMode)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60; // the tick rate; batch mode would otherwise spin a core
            var title = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("GameServer"));
            SceneManager.UnloadSceneAsync(title);
        }
        Create(address, p, max);
    }

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
