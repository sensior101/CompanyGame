using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// QA of 서버 열기 / 참가하기 (Docs/Multiplayer_Redesign.md 12.3). Clicks the real TitleScreen buttons in profile slot 3
/// (its server folder and recent addresses are cleared first). Run WITHOUT -quit (the QA exits the editor), with graphics
/// for the screenshots (env QA_SHOTS = folder, O_*.png).
///   -executeMethod ServerOpenQA.Run         ServerSettings checks, then the in-app server opened from the 서버 열기 screen,
///                                           fake TCP clients with a wrong and the right password, the server name in the
///                                           pause menu, leaving stops the in-app server (phone path forced)
///   -executeMethod ServerOpenQA.RunProcess  builds a Windows player to Builds/ServerOpenQA, then LocalServerProcess starts it,
///                                           참가하기 with 127.0.0.1:port and the password, the process outlives the game, 끄기 ends it;
///                                           then the same from the buttons: 서버 열기 → 서버 시작, leave, 접속 again, 끄기
/// Since 12.5 a first visit to a server's world asks for a character (캐릭터 만들기): <see cref="EnterGame"/> makes one.
/// </summary>
[InitializeOnLoad]
public static class ServerOpenQA
{
    const string Flag = "ServerOpenQA.mode", ExeKey = "ServerOpenQA.exe";
    static int checks;
    static Process started;

    static ServerOpenQA() => EditorApplication.playModeStateChanged += OnState;

    public static void Run() => Start("inapp");

    public static void RunProcess()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/ServerOpenQA"));
        string exe = Path.Combine(dir, "ServerOpenQA.exe");
        string scene = Array.Find(EditorBuildSettings.scenes, s => s.enabled).path; // one scene is enough for a server
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scene }, locationPathName = exe, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
        });
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("[ServerOpenQA] RESULT FAILED: build " + report.summary.result);
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log("[ServerOpenQA] built " + exe);
        SessionState.SetString(ExeKey, exe);
        Start("process");
    }

    static void Start(string mode)
    {
        EditorSceneManager.OpenScene(TitleSceneBuilder.ScenePath, OpenSceneMode.Single);
        SessionState.SetString(Flag, mode);
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange change)
    {
        string mode = SessionState.GetString(Flag, "");
        if (mode == "" || change != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.SetString(Flag, "");
        Drive(mode);
    }

    static async void Drive(string mode)
    {
        int original = PlayerPrefs.GetInt("Profile.Slot", 1), code = 0;
        checks = 0;
        ProfileSlot.Set(3);
        Reset();
        try
        {
            if (mode == "inapp") { Settings(); await InApp(); }
            else await LocalProcess(SessionState.GetString(ExeKey, ""));
            Debug.Log("[ServerOpenQA] RESULT " + mode + " passed, checks=" + checks);
        }
        catch (Exception e)
        {
            Debug.LogError("[ServerOpenQA] RESULT " + mode + " FAILED: " + e.Message + "\n" + e.StackTrace);
            code = 1;
        }
        if (started != null && !started.HasExited) started.Kill(); // only the process this QA started
        ServerSettings.ForceMobile = null;
        PlayerPrefs.DeleteKey("server.recent.p3");
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    static void Reset()
    {
        ServerSettings.ForceMobile = null;
        PlayerPrefs.DeleteKey("server.recent.p3");
        if (Directory.Exists(ServerSettings.Folder)) Directory.Delete(ServerSettings.Folder, true);
    }

    // ---- ServerSettings (criterion 3) ----

    static void Settings()
    {
        Check(ServerSettings.FilePath.Contains("profile3") && LocalServerProcess.RunningPath.Contains("profile3"), "settings and running.json are in the slot folder (" + ServerSettings.Folder + ")");
        ServerSettings.ForceMobile = false;
        Check(new ServerSettings { maxPlayers = 1 }.Clamp().maxPlayers == 2, "PC: 1 player → 2");
        Check(new ServerSettings { maxPlayers = 99 }.Clamp().maxPlayers == 50, "PC: 99 players → 50");
        ServerSettings.ForceMobile = true;
        Check(new ServerSettings { maxPlayers = 20 }.Clamp().maxPlayers == 10, "mobile: 20 players → 10");
        ServerSettings.ForceMobile = false;
        var odd = new ServerSettings { name = "   ", password = new string('x', 40), port = 80 }.Clamp();
        Check(odd.name == ServerSettings.DefaultName && odd.password.Length == 32 && odd.port == ServerSettings.DefaultPort, "empty name → default name, password cut to 32, port 80 → 7777");
        var tag = new ServerSettings { name = "<b>" + new string('가', 40) }.Clamp();
        Check(!tag.name.Contains("<") && tag.name.Length == 30, "name loses <> and is cut to 30 characters");

        new ServerSettings { name = "QA 저장 서버", maxPlayers = 12, isPublic = true, password = "pw!", port = 12345 }.Save();
        var back = ServerSettings.Load();
        Check(back.name == "QA 저장 서버" && back.maxPlayers == 12 && back.isPublic && back.password == "pw!" && back.port == 12345, "saved settings read back the same");
        File.WriteAllText(ServerSettings.FilePath, "{ this is not json");
        var bad = ServerSettings.Load();
        var fresh = new ServerSettings();
        Check(bad.name == fresh.name && bad.maxPlayers == fresh.maxPlayers && bad.isPublic == fresh.isPublic && bad.password == "" && bad.port == ServerSettings.DefaultPort, "broken JSON → defaults");
        File.Delete(ServerSettings.FilePath);

        Check(TitleFlow.TryParseAddress(" 127.0.0.1:7001 ", out string h, out int p) && h == "127.0.0.1" && p == 7001, "address host:port");
        Check(TitleFlow.TryParseAddress("example.com", out h, out p) && h == "example.com" && p == ServerSettings.DefaultPort, "address without a port → 7777");
        Check(!TitleFlow.TryParseAddress("", out _, out _) && !TitleFlow.TryParseAddress("a:b", out _, out _) && !TitleFlow.TryParseAddress(":7000", out _, out _)
              && !TitleFlow.TryParseAddress("a:70000", out _, out _), "bad addresses are refused");
        foreach (string message in new[] { "Version", "Full", "Password", "Malformed", "Rate", "server_start_failed", "bad_address", "server_unreachable" })
            Check(!TitleFlow.Message(message).StartsWith("알 수 없는"), "friendly message for " + message);
        ServerSettings.ForceMobile = null;
    }

    // ---- in-app server (criterion 4) ----

    static async Task InApp()
    {
        await WaitFor(() => OnTitle && !Screen.Busy, 15, "title");
        Screen.UseCamera(Camera.main);
        foreach (string label in new[] { "혼자 하기", "서버 열기", "참가하기", "설정", "종료" })
            Check(HasButton(label), "main menu has '" + label + "'");
        Check(!HasButton("옛 방식"), "main menu has no 옛 방식(개발용) (12.11)");
        Screen.Open("main");
        Check(TextOf(Screen.transform).Contains("프로필 슬롯 3") && !TextOf(Screen.transform).Contains("http"), "main footer: the profile slot only, no server address");
        await Shot("O_main.png");
        Screen.Open("serverjoin");
        Check(!TextOf(Screen.transform).Contains("http"), "참가하기: no account server address");
        Screen.Open("main");
        await Click("서버 열기");
        Check(Screen.Screen == "serveropen" && Inputs().Length == 2, "서버 열기 screen with name and password fields (editor: no running server)");
        await Click("고급");
        Check(Inputs().Length == 3, "고급 shows the port field");
        await Shot("O_server_open.png");

        ServerSettings.ForceMobile = true; // the phone path: in-app server, at most 10 players, leaving stops it
        Screen.Open("main");
        await Click("서버 열기");
        int port = FreePort();
        var inputs = Inputs();
        inputs[0].text = "QA 앱 서버"; inputs[1].text = "앱비번"; inputs[2].text = port.ToString();
        for (int i = 0; i < 15; i++) Check(Screen.Click("▶"), "players ▶");
        Check(TextOf(Screen.Content).Contains("인원 10명"), "phone: ▶ stops at 10 players");
        await Click("서버 시작");
        await EnterGame("QA앱");
        var client = SyncClient.Instance;
        var host = SyncHost.Instance;
        Check(client.Connected && client.ServerName == "QA 앱 서버", "the SyncClient is in my server, named '" + client.ServerName + "'");
        Check(host && host.InApp && host.Port == port && host.Server.MaxPlayers == 10, "the in-app server listens on " + port + " for 10 players");
        Check(ServerSettings.Load().name == "QA 앱 서버" && ServerSettings.Load().password == "앱비번", "opening saved the settings of this slot");

        var (wrong, wrongMsg) = await Hello(port, "앱비번2");
        Check(wrongMsg.kind == ToClient.Reject && wrongMsg.reason == RejectReason.Password, "a fake TCP client with a wrong password is rejected (Password)");
        await WaitFor(() => Gone(wrong), 5, "the rejected socket to close");
        var (right, rightMsg) = await Hello(port, "앱비번");
        Check(rightMsg.kind == ToClient.Welcome && rightMsg.name == "QA 앱 서버" && right.IsOpen, "a fake TCP client with the right password gets in");

        PauseMenu.Instance.Open();
        var pause = PauseMenu.Instance.GetComponentInChildren<Canvas>().transform;
        Check(TextOf(pause).Contains("서버: QA 앱 서버"), "the pause menu shows the server name");
        Check(TitleUi.ClickButton(pause, "서버 나가기"), "clicked 서버 나가기 in the pause menu");
        await WaitFor(() => OnTitle, 60, "title after leaving");
        await WaitFor(() => Gone(right), 5, "the fake client to be dropped by the stopped server");
        Check(!SyncHost.Instance && !client.Connected, "leaving as the phone owner stopped the in-app server and disconnected");
        Check(PortFree(port), "port " + port + " is free again");
        right.Close();
    }

    // ---- separate server process (criterion 5) ----

    static async Task LocalProcess(string exe)
    {
        await WaitFor(() => OnTitle && !Screen.Busy, 15, "title");
        Screen.UseCamera(Camera.main);
        LocalServerProcess.ExePath = exe;
        Check(LocalServerProcess.CanStart && LocalServerProcess.Running() == null, "the built exe is the server exe; nothing is running yet");
        int port = FreePort();
        started = LocalServerProcess.Start(new ServerSettings { name = "QA 프로세스 서버", maxPlayers = 6, password = "pc비번", port = port });
        Check(started != null, "LocalServerProcess started " + exe);
        await WaitFor(() => LocalServerProcess.Running() != null || started.HasExited, 60, "running.json from the server process");
        var running = LocalServerProcess.Running();
        Check(running != null && running.pid == started.Id && running.port == port && File.Exists(LocalServerProcess.RunningPath),
            "running.json names the process " + started.Id + " and port " + port);

        await Click("서버 열기");
        Check(Screen.Screen == "serverrunning" && TextOf(Screen.Content).Contains("포트 " + port) && HasButton("접속") && HasButton("끄기"), "서버 열기 shows my running server with 접속 / 끄기");
        await Shot("O_server_running.png");
        await Click("뒤로");

        await Click("참가하기");
        Check(Screen.Screen == "serverjoin", "참가하기 opens the join screen");
        await Click("직접 주소 입력"); // 12.13: the address is folded under the list and the code
        var inputs = Inputs(); // code, address, password
        Check(inputs.Length == 3, "직접 주소 입력 shows the address and password fields under the code field");
        inputs[1].text = "127.0.0.1:" + port; inputs[2].text = "틀린비번";
        await Shot("O_join.png");
        await Click("주소로 접속");
        await WaitFor(() => Screen && Screen.Screen == "error", 15, "error screen for a wrong password");
        Check(TextOf(Screen.Content).Contains("비밀번호가 틀렸") && !HasButton("다시 시도"), "wrong password: friendly message, no pointless retry");
        await Shot("O_password_error.png");
        await Click("뒤로");
        Check(Screen.Screen == "serverjoin" && Inputs().Length == 3 && Inputs()[1].text == "127.0.0.1:" + port, "back on the join screen with the address kept");
        Inputs()[2].text = "pc비번";
        await Click("주소로 접속");
        await EnterGame("QA프로세스");
        var client = SyncClient.Instance;
        Check(client.Connected && client.ServerName == "QA 프로세스 서버" && !SyncHost.Instance, "joined the server process (no in-app server), named '" + client.ServerName + "'");
        Check(ServerSettings.Recent.Count == 1 && ServerSettings.Recent[0] == "127.0.0.1:" + port, "the address is remembered for this slot");

        TitleFlow.ReturnToTitle();
        await WaitFor(() => OnTitle, 60, "title");
        await Awaitable.WaitForSecondsAsync(1f);
        Check(!client.Connected && !started.HasExited && LocalServerProcess.Running()?.pid == started.Id, "back on the title the server process keeps running");
        var (fake, msg) = await Hello(port, "pc비번");
        Check(msg.kind == ToClient.Welcome, "the process still serves a client after the game left");
        fake.Close();

        await Click("서버 열기");
        Check(Screen.Screen == "serverrunning", "서버 열기 shows it running again");
        await Click("끄기");
        Check(started.WaitForExit(10000), "끄기 ended the server process");
        Check(!File.Exists(LocalServerProcess.RunningPath) && LocalServerProcess.Running() == null && Screen.Screen == "serveropen", "running.json is gone and 서버 열기 shows the settings again");

        await OpenFromUi();
    }

    // TitleFlow.OpenServer's PC path from the real buttons: 서버 시작 starts the process, joins it at 127.0.0.1, it outlives the game.
    static async Task OpenFromUi()
    {
        Check(Screen.Screen == "serveropen" && !TextOf(Screen.transform).Contains("서버 http"), "서버 열기 settings (footer without the old server address)");
        if (Inputs().Length < 3) await Click("고급");
        int port = FreePort();
        var inputs = Inputs();
        inputs[0].text = "QA UI 서버"; inputs[1].text = "ui비번"; inputs[2].text = port.ToString();
        await Click("서버 시작");
        await EnterGame("QA유아이", 120);
        var running = LocalServerProcess.Running();
        Check(running != null && running.port == port && File.Exists(LocalServerProcess.RunningPath), "a) 서버 시작 started the server process " + running?.pid + " and running.json (port " + port + ")");
        started = Process.GetProcessById(running.pid);
        var client = SyncClient.Instance;
        Check(client.Connected && client.ServerName == "QA UI 서버" && !SyncHost.Instance, "b) joined it at 127.0.0.1 (no in-app server) and entered the game map");

        PauseMenu.Instance.Open();
        var pause = PauseMenu.Instance.GetComponentInChildren<Canvas>().transform;
        Check(TextOf(pause).Contains("서버: QA UI 서버"), "c) the pause menu shows the server name");
        Check(TitleUi.ClickButton(pause, "서버 나가기"), "clicked 서버 나가기");
        await WaitFor(() => OnTitle, 60, "title after leaving");
        await Awaitable.WaitForSecondsAsync(1f);
        Check(!client.Connected && !started.HasExited && LocalServerProcess.Running()?.pid == started.Id, "d) back on the title the server process keeps running");
        await Click("서버 열기");
        Check(Screen.Screen == "serverrunning" && TextOf(Screen.Content).Contains("포트 " + port), "d) 서버 열기 shows 내 서버 실행 중");

        await Click("접속");
        await WaitFor(() => InGame || Screen && Screen.Screen == "name", 90, "entering again with 접속");
        Check(InGame, "e) the second visit keeps the character made on the first (no 캐릭터 만들기)");
        Check(client.Connected && client.ServerName == "QA UI 서버", "e) 접속 joined again with the saved password");
        TitleFlow.ReturnToTitle();
        await WaitFor(() => OnTitle, 60, "title");
        Check(!started.HasExited, "the process is still there after the second visit");

        await Click("서버 열기");
        await Click("끄기");
        Check(started.WaitForExit(10000) && !File.Exists(LocalServerProcess.RunningPath), "f) 끄기 ended the process and removed running.json");
    }

    // ---- helpers ----

    static TitleScreen Screen => TitleScreen.Current;
    static bool OnTitle => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null;
    static bool InGame => SceneManager.GetActiveScene().path == TitleFlow.GameScene && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading;

    static TMP_InputField[] Inputs() => Screen.Content.GetComponentsInChildren<TMP_InputField>();

    // A new character on the server's world (12.5): the 캐릭터 만들기 screen comes first, then the game map.
    static async Task EnterGame(string character, float seconds = 90)
    {
        await WaitFor(() => InGame || Screen && Screen.Screen == "name", seconds, "캐릭터 만들기 or the game map");
        if (!InGame)
        {
            Inputs()[0].text = character;
            await Click("확인");
        }
        await WaitFor(() => InGame, seconds, "entering the game map as " + character);
        Check(GameSession.LocalPlayerName == character, "playing as the server's character " + GameSession.LocalPlayerName);
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("FAIL: " + what + Where());
        checks++;
        Debug.Log("[ServerOpenQA] ok: " + what);
    }

    static string Where() => Screen ? " [screen=" + Screen.Screen + ": " + TextOf(Screen.Content).Replace('\n', '|') + "]" : "";

    static async Task WaitFor(Func<bool> condition, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what + Where());
            await Awaitable.NextFrameAsync();
        }
    }

    static async Task Click(string label)
    {
        await WaitFor(() => Screen && !Screen.Busy, 15, "screen not busy before clicking " + label);
        Check(Screen.Click(label), "clicked '" + label + "' on screen " + Screen.Screen);
        await Awaitable.NextFrameAsync();
    }

    static bool HasButton(string text)
    {
        foreach (var b in Screen.Content.GetComponentsInChildren<UnityEngine.UI.Button>())
        {
            var label = b.GetComponentInChildren<TMP_Text>();
            if (label && label.text.Contains(text)) return true;
        }
        return false;
    }

    static string TextOf(Transform root)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in root.GetComponentsInChildren<TMP_Text>()) sb.Append(t.text).Append('\n');
        return sb.ToString();
    }

    static async Task Shot(string file)
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (string.IsNullOrEmpty(dir) || !Camera.main) return;
        Directory.CreateDirectory(dir);
        await Awaitable.WaitForSecondsAsync(.3f);
        await TitleShots.Snap(Camera.main, Path.Combine(dir, file));
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static bool PortFree(int port)
    {
        try { var l = new TcpListener(IPAddress.Any, port); l.Start(); l.Stop(); return true; }
        catch (SocketException) { return false; }
    }

    // IsOpen turns false once every frame before the close has been read.
    static bool Gone(TcpClientTransport socket)
    {
        while (socket.TryReceive(out _)) { }
        return !socket.IsOpen;
    }

    struct FirstMsg { public ToClient kind; public RejectReason reason; public string name; }

    /// <summary>A fake TCP client says Hello with <paramref name="password"/>; returns its socket and the server's first answer.</summary>
    static async Task<(TcpClientTransport, FirstMsg)> Hello(int port, string password)
    {
        var socket = new TcpClientTransport();
        await socket.Connect("127.0.0.1", port);
        socket.Send(SyncProtocol.Hello("qa-fake", "가짜", "QA/fake", password));
        float end = Time.realtimeSinceStartup + 5f;
        while (true)
        {
            if (socket.TryReceive(out byte[] frame))
            {
                using var r = SyncProtocol.Reader(frame);
                var m = new FirstMsg { kind = (ToClient)r.ReadByte() };
                if (m.kind == ToClient.Welcome) { r.ReadUInt32(); r.ReadUInt16(); m.name = SyncProtocol.ReadStr(r); }
                else if (m.kind == ToClient.Reject) m.reason = (RejectReason)r.ReadByte();
                return (socket, m);
            }
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for the server's answer to a fake Hello");
            await Awaitable.NextFrameAsync();
        }
    }
}
