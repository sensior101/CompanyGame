using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using QaPeer = SyncLayerQA.QaPeer;

/// <summary>
/// QA of the EOS connection (Docs/Multiplayer_Redesign.md 12.13). Play-mode runs use profile slot 3 (its server folder is
/// cleared first) and run WITHOUT -quit (the QA exits the editor); screenshots go to env QA_SHOTS (E_*.png).
///   -executeMethod EosConnectQA.Run         (with -quit) packet framing (1 byte, the 1170-byte edges, 512KB, bad lengths),
///                                           MultiTransport (2 TCP + 1 loopback), GameServer's identity wait, Ping/Pong
///   -executeMethod EosConnectQA.RunPlay     the EOS account, ID tokens against the in-app server, the session (list, code,
///                                           private, players, stale HB, removal), the join and pause screens
///   -executeMethod EosConnectQA.RunNoEos    COMPANYGAME_NOEOS=1: PC GUID account, 혼자 하기 works, the join screen says EOS is off
///   -executeMethod EosConnectQA.RunProcess  builds Builds/EosConnectQA, 서버 열기 starts it as my PC server: its session and the
///                                           code in running.json, the editor joins at 127.0.0.1 through the ID token check, 끄기 removes the session
///   -executeMethod EosConnectQA.RunBundle   (with -quit) the final game: Builds/Windows (+zip) and
///                                           Builds/Mac (+zip, universal) with README.txt
/// </summary>
[InitializeOnLoad]
public static class EosConnectQA
{
    const string Flag = "EosConnectQA.mode", ExeKey = "EosConnectQA.exe";
    static int checks;
    static Process started; // the server process this QA started (killed at the end)
    static readonly List<string> logs = new List<string>();

    static EosConnectQA() => EditorApplication.playModeStateChanged += OnState;

    // ================= in memory (with -quit) =================

    public static void Run()
    {
        checks = 0;
        Framing();
        Multi();
        Identity();
        Debug.Log("[EosConnectQA] RESULT run passed, checks=" + checks);
    }

    static void Framing()
    {
        var rng = new System.Random(7);
        byte[] Frame(int n) { var b = new byte[n]; rng.NextBytes(b); return b; }
        // 1 byte; 1166 + 4 = exactly one packet; one byte more = two; 1170 itself; exactly two packets; 512KB
        int[] sizes = { 0, 1, 1165, 1166, 1167, 1170, 2 * PacketFraming.MaxPacket - 4, 2 * PacketFraming.MaxPacket - 3, PacketFraming.MaxFrame };
        var joiner = new PacketFraming.Joiner();
        var got = new List<byte[]>();
        foreach (int n in sizes)
        {
            byte[] frame = Frame(n);
            var packets = PacketFraming.Split(frame);
            Check(packets.All(p => p.Count <= PacketFraming.MaxPacket) && packets.Sum(p => p.Count) == n + 4
                  && packets.Count == (n + 4 + PacketFraming.MaxPacket - 1) / PacketFraming.MaxPacket, "split " + n + " bytes: " + packets.Count + " packets of at most 1170");
            got.Clear();
            bool accepted = packets.All(p => joiner.Add(p, got));
            Check(accepted && got.Count == 1 && got[0].SequenceEqual(frame), "a " + n + "-byte frame comes back the same");
        }
        var two = PacketFraming.Split(Frame(10)).Concat(PacketFraming.Split(Frame(3000))).ToList();
        got.Clear();
        foreach (var p in two) joiner.Add(p, got);
        Check(got.Count == 2 && got[0].Length == 10 && got[1].Length == 3000, "two frames in a row on one joiner");

        bool Bad(byte[] packet) { var j = new PacketFraming.Joiner(); var f = new List<byte[]>(); return !j.Add(new ArraySegment<byte>(packet), f) && j.Broken && f.Count == 0; }
        Check(Bad(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 1 }), "a negative length is refused");
        Check(Bad(BitConverter.GetBytes(PacketFraming.MaxFrame + 1)), "a length over 512KB is refused");
        Check(Bad(new byte[] { 1, 0 }), "a first packet shorter than the length is refused");
        Check(Bad(new byte[] { 2, 0, 0, 0, 1, 2, 3 }), "a packet running past its frame is refused");
        var broken = new PacketFraming.Joiner();
        broken.Add(new ArraySegment<byte>(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F }), got);
        Check(!broken.Add(PacketFraming.Split(Frame(5))[0], got), "a broken joiner stays broken");
        bool threw = false;
        try { PacketFraming.Split(new byte[PacketFraming.MaxFrame + 1]); } catch (ArgumentException) { threw = true; }
        Check(threw, "a frame over 512KB is not sent");
    }

    static void Multi()
    {
        var tcp1 = new TcpServerTransport(IPAddress.Loopback, 0);
        var tcp2 = new TcpServerTransport(IPAddress.Loopback, 0);
        var loop = new LoopbackTransport();
        var multi = new MultiTransport(tcp1, tcp2);
        multi.Add(loop);
        try
        {
            var clients = new List<IClientTransport>();
            foreach (int port in new[] { tcp1.Port, tcp1.Port, tcp2.Port })
            {
                var c = new TcpClientTransport();
                c.Connect("127.0.0.1", port).Wait();
                clients.Add(c);
            }
            clients.Add(loop.Connect()); clients.Add(loop.Connect());
            for (int i = 0; i < clients.Count; i++) clients[i].Send(new[] { (byte)i });
            var connected = new List<int>();
            var tagOf = new Dictionary<int, int>(); // connection → the client index it said
            Drain(multi, 3, e =>
            {
                if (e.Kind == TransportEventKind.Connected) connected.Add(e.Connection);
                if (e.Kind == TransportEventKind.Data) tagOf[e.Connection] = e.Data[0];
            }, () => tagOf.Count == clients.Count);
            Check(connected.Count == 5 && connected.Distinct().Count() == 5, "5 connections (inner numbers 1,2 / 1 / 1,2) get 5 different numbers: " + string.Join(",", connected));
            Check(tagOf.Count == 5 && tagOf.Values.Distinct().Count() == 5, "each connection's data comes from its own client");
            int ConnOf(int client) => tagOf.First(kv => kv.Value == client).Key;
            Check(multi.Resolve(ConnOf(0), out var part0, out _) && part0 == tcp1 && multi.Resolve(ConnOf(2), out var part2, out _) && part2 == tcp2
                  && multi.Resolve(ConnOf(4), out var part4, out _) && part4 == loop, "Resolve gives each connection's own transport");

            clients[1].Close(); clients[3].Close();
            var gone = new List<int>();
            Drain(multi, 3, e => { if (e.Kind == TransportEventKind.Disconnected) gone.Add(e.Connection); }, () => gone.Count == 2);
            Check(gone.Count == 2 && gone.Contains(ConnOf(1)) && gone.Contains(ConnOf(3)), "closing a TCP and a loopback client: Disconnected for exactly those two");
            Check(!multi.Resolve(ConnOf(1), out _, out _), "a closed connection no longer resolves");
            foreach (int i in new[] { 0, 2, 4 }) multi.Send(ConnOf(i), new byte[] { 42, (byte)i });
            var echoes = new Dictionary<int, byte[]>();
            var until = DateTime.UtcNow.AddSeconds(3);
            while (echoes.Count < 3 && DateTime.UtcNow < until)
                foreach (int i in new[] { 0, 2, 4 }) if (clients[i].TryReceive(out byte[] f)) echoes[i] = f;
            Check(echoes.Count == 3 && echoes.All(kv => kv.Value[1] == kv.Key), "the others still get their own frames");
            multi.Disconnect(ConnOf(2));
            until = DateTime.UtcNow.AddSeconds(3);
            while (clients[2].IsOpen && DateTime.UtcNow < until) clients[2].TryReceive(out _);
            Check(!clients[2].IsOpen && clients[0].IsOpen, "a server-side Disconnect closes only that client");
            foreach (var c in clients) c.Close();
        }
        finally { multi.Stop(); }
    }

    static void Drain(MultiTransport multi, float seconds, Action<TransportEvent> seen, Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until)
        {
            while (multi.TryReceive(out var e)) seen(e);
            System.Threading.Thread.Sleep(5);
        }
    }

    static void Identity()
    {
        var loop = new LoopbackTransport();
        var server = new GameServer(loop, 4);
        var asked = new List<string>();
        var answers = new Dictionary<string, TaskCompletionSource<bool>>();
        server.IdentityCheck = (connection, account, token) =>
        {
            asked.Add(account + "/" + token);
            answers[account] = new TaskCompletionSource<bool>();
            return answers[account].Task;
        };
        var a = new QaPeer(loop.Connect());
        a.Send(SyncProtocol.Hello("acc-a", "A", "m1", token: "tok-a"));
        a.Send(SyncProtocol.Map("m2")); // SyncClient may send its map before the Welcome
        server.Tick();
        Check(asked.Contains("acc-a/tok-a"), "the check gets the Hello's account and token");
        Check(!a.Has(ToClient.Welcome) && server.PlayerCount == 0, "no Welcome while the check runs");
        answers["acc-a"].SetResult(true);
        server.Tick();
        var welcome = a.Take(m => m.kind == ToClient.Welcome);
        Check(welcome != null && server.PlayerCount == 1, "confirmed → Welcome");
        Check(server.MapOf(welcome.number) == "m2", "the Map sent during the check is read after the Welcome (map " + server.MapOf(welcome.number) + ")");
        a.Send(SyncProtocol.Ping(4321));
        server.Tick();
        Check(a.Take(m => m.kind == ToClient.Pong)?.number == 4321, "Ping → Pong with the same stamp");

        var b = new QaPeer(loop.Connect());
        b.Send(SyncProtocol.Hello("acc-b", "B", "m1", token: "bad"));
        server.Tick();
        answers["acc-b"].SetResult(false);
        server.Tick();
        Check(b.Has(m => m.kind == ToClient.Reject && m.reason == RejectReason.Identity) && !b.client.IsOpen && server.PlayerCount == 1, "refused → Reject(Identity) and closed");

        var c = new QaPeer(loop.Connect());
        c.Send(SyncProtocol.Hello("acc-c", "C", "m1"));
        server.Tick();
        answers["acc-c"].SetException(new Exception("EOS down"));
        server.Tick();
        Check(c.Has(m => m.kind == ToClient.Reject && m.reason == RejectReason.Identity), "a check that failed → Reject(Identity)");

        var d = new QaPeer(loop.Connect());
        d.Send(SyncProtocol.Hello("acc-d", "D", "m1"));
        server.Tick();
        server.AdvanceClock(11000);
        server.Tick();
        Check(d.Has(m => m.kind == ToClient.Reject && m.reason == RejectReason.Malformed) && !d.client.IsOpen, "a check that never answers ends at the 10 s Hello limit");
        answers["acc-d"].SetResult(true);
        server.Tick();
        Check(server.PlayerCount == 1, "its late answer changes nothing");

        var e = new QaPeer(loop.Connect());
        e.Send(SyncProtocol.Hello("acc-e", "E", "m1"));
        for (int i = 0; i < 70; i++) e.Send(SyncProtocol.Map("m" + i));
        server.Tick();
        Check(e.Has(m => m.kind == ToClient.Reject && m.reason == RejectReason.Malformed), "more than 64 frames during the check → Reject(Malformed)");

        var plain = new GameServer(loop = new LoopbackTransport(), 4);
        var p = new QaPeer(loop.Connect());
        p.Send(SyncProtocol.Hello("any", "P", "m1", token: ""));
        plain.Tick();
        Check(p.Has(ToClient.Welcome), "a server without an identity check (no EOS) welcomes any account as before");
    }

    // ================= play mode =================

    public static void RunPlay() => Enter("play");

    public static void RunNoEos()
    {
        Environment.SetEnvironmentVariable(EosAccount.DisableEnv, "1"); // this editor process only
        Enter("noeos");
    }

    public static void RunProcess()
    {
        EosConfig.Write();
        string exe = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/EosConnectQA/EosConnectQA.exe"));
        string scene = Array.Find(EditorBuildSettings.scenes, s => s.enabled).path; // a server needs no maps
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene }, locationPathName = exe, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        if (report.summary.result != BuildResult.Succeeded) { Debug.LogError("[EosConnectQA] RESULT FAILED: build " + report.summary.result); EditorApplication.Exit(1); return; }
        SessionState.SetString(ExeKey, exe);
        Enter("process");
    }

    static void Enter(string mode)
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
        started = null;
        checks = 0;
        Application.logMessageReceived += Capture;
        try
        {
            await WaitFor(() => OnTitle && !Screen.Busy, 30, "title");
            ProfileSlot.Set(3);
            if (Directory.Exists(ServerSettings.Folder)) Directory.Delete(ServerSettings.Folder, true);
            Screen.UseCamera(Camera.main);
            if (mode == "play") await Play();
            else if (mode == "noeos") await NoEos();
            else await OwnProcess(SessionState.GetString(ExeKey, ""));
            Debug.Log("[EosConnectQA] RESULT " + mode + " passed, checks=" + checks);
        }
        catch (Exception e)
        {
            Debug.LogError("[EosConnectQA] RESULT " + mode + " FAILED: " + e.Message + "\n" + e.StackTrace);
            code = 1;
        }
        if (started != null && !started.HasExited) started.Kill(); // only the process this QA started
        Application.logMessageReceived -= Capture;
        EosSession.UtcNow = () => DateTime.UtcNow;
        SyncHost.LoopbackException = true;
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    static void Capture(string message, string stack, LogType type) { lock (logs) logs.Add(message); }
    static bool Logged(string part) { lock (logs) return logs.Any(l => l.Contains(part)); }

    static async Task Play()
    {
        // account
        Check(await EosAccount.WhenSettled(60), "EOS Device ID login (" + EosAccount.State + " " + EosAccount.Problem + ")");
        string me = EosAccount.LocalPuid;
        Check(GameSession.LocalPlayerId == me, "the account is the PUID " + me.Substring(0, 4) + "… (slot 3 too)");

        // 서버 열기 text
        Screen.Open("serveropen");
        await Click("○ 공개");
        Check(TextOf(Screen.Content).Contains("공개 서버 목록에 나오고"), "서버 열기 public: says it is listed and has a code");
        await Shot("E_open_public.png");
        await Click("○ 비공개");
        Check(TextOf(Screen.Content).Contains("서버 코드를 아는 사람만"), "서버 열기 private: says only the code lets people in");
        await Shot("E_open_private.png");
        Screen.Open("main");

        // the in-app server (editor) with EOS; my SyncClient joins it at 127.0.0.1 with my token
        int port = FreePort();
        var opened = Time.realtimeSinceStartup;
        Check(await TitleFlow.OpenServer(new ServerSettings { name = "QA EOS 서버", maxPlayers = 8, isPublic = true, port = port }) == null, "서버 열기 (in-app) joined");
        var host = SyncHost.Instance;
        await WaitFor(() => host.Session != null && host.Session.Created, 40, "the EOS session");
        string serverCode = host.Code;
        Check(serverCode.Length == 6 && serverCode == ServerSettings.CodeOf(host.Server.World.Id) && ServerSettings.NormalizeCode(serverCode) == serverCode, "the session is made with code " + serverCode + " (from the world id)");
        await WaitFor(() => Logged("identity of " + me.Substring(0, 4) + "… confirmed (ID token)"), 10, "my join confirmed by the ID token");
        Check(true, "my SyncClient's TCP join was confirmed with its real ID token (EOS VerifyIdToken) → Welcome");

        // ID token checks with fake TCP clients
        string token = EosAccount.IdToken();
        string other = "0002" + Guid.NewGuid().ToString("N").Substring(4);
        Check((await FakeHello(port, other, token)) == RejectReason.Identity, "another account with my token → Reject(Identity)");
        Check((await FakeHello(port, other, "")) == RejectReason.Identity, "another account without a token from 127.0.0.1 → Reject(Identity)");
        SyncHost.LoopbackException = false;
        Check((await FakeHello(port, me, "")) == RejectReason.Identity, "my PUID without a token, 127.0.0.1 exception off (as from another IP) → Reject(Identity)");
        SyncHost.LoopbackException = true;
        Check((await FakeHello(port, me, "")) == RejectReason.AlreadyConnected && Logged("confirmed (own PUID on 127.0.0.1)"),
            "my PUID without a token from 127.0.0.1: identity passes (then AlreadyConnected, my game is in)");

        // the list and the code
        var mine = await Poll(async () => (await EosSession.Search(null)).found.FirstOrDefault(e => e.code == serverCode), e => e != null, 45, "my session in the public list");
        Check(mine.name == "QA EOS 서버" && mine.maxPlayers == 8 && mine.puid == me && mine.isPublic && !mine.hasPassword, "list entry: name, max 8, my PUID, public, no password");
        Check((await EosSession.Search(serverCode)).found.Any(e => e.code == serverCode), "the code search finds it");
        await Poll(async () => (await EosSession.Search(serverCode)).found.FirstOrDefault(), e => e != null && e.players == 1, 50, "players = 1 in the session");
        Check(true, "the player count reached the session (1)");
        var (found, findError) = await TitleFlow.FindCode(serverCode.ToLowerInvariant());
        Check(findError == null && found.code == serverCode, "TitleFlow.FindCode takes the code in small letters too");
        Check(await TitleFlow.JoinListed(found, "") == "own_server", "my own in-app server from the list: own_server (no P2P to myself)");

        Screen.Open("serverjoin");
        await WaitFor(() => TextOf(Screen.Content).Contains("QA EOS 서버"), 30, "my server on the 참가하기 list");
        Check(TextOf(Screen.Content).Contains("1/8명"), "the row shows players/max");
        await Shot("E_join_list.png");
        Inputs()[0].text = serverCode;
        await Shot("E_join_code.png");

        // private: off the list, still found by code
        host.Session.SetPublic(false);
        await Poll(async () => (await EosSession.Search(null)).found.Any(e => e.code == serverCode), any => !any, 50, "my session off the public list");
        var byCode = (await EosSession.Search(serverCode)).found.FirstOrDefault();
        Check(byCode != null && !byCode.isPublic, "private: not listed, the code still finds it");
        await Awaitable.WaitForSecondsAsync(10f); // the list refreshes once in 10 s
        Screen.Open("serverjoin");
        await Click("새로고침");
        await WaitFor(() => !TextOf(Screen.Content).Contains("불러오는 중"), 30, "the refreshed list");
        Check(!TextOf(Screen.Content).Contains("QA EOS 서버"), "the refreshed 참가하기 list does not show it");
        await Shot("E_join_empty.png");

        // players back to 0, then a stale HB
        SyncClient.Instance.Disconnect();
        await Poll(async () => (await EosSession.Search(serverCode)).found.FirstOrDefault(), e => e != null && e.players == 0, 50, "players = 0 in the session");
        Check(true, "leaving reached the session (0 players)");
        EosSession.UtcNow = () => DateTime.UtcNow.AddMinutes(EosSession.StaleMinutes + 1);
        Check((await EosSession.Search(serverCode)).found.Count == 0, "with the clock 6 minutes on, its HB is stale: hidden");
        EosSession.UtcNow = () => DateTime.UtcNow;
        float minutes = (Time.realtimeSinceStartup - opened) / 60f;
        Check(host.Session.Updates <= 2 * minutes + 2, host.Session.Updates + " updates in " + minutes.ToString("0.0") + " minutes (at most 2 a minute, EOS allows 30)");

        // in the game: the pause menu
        Check(await TitleFlow.JoinServer("127.0.0.1:" + port, "", false) == null, "joined my server again");
        Check(await TitleFlow.CreateServerCharacter("QA이오에스", "male") == null && TitleFlow.EnterServerWorld() == null, "made a character and entered");
        await WaitFor(() => InGame, 90, "the game map");
        await Awaitable.WaitForSecondsAsync(3f); // a Ping goes out every 2 s
        PauseMenu.Instance.Open();
        var pause = PauseMenu.Instance.GetComponentInChildren<Canvas>().transform;
        string text = TextOf(pause);
        Check(text.Contains("서버 코드: <color=#FFC857>" + serverCode) && text.Contains("연결: 직접(TCP)") && text.Contains("왕복"), "pause menu: server code, connection kind and round trip: " + text.Replace('\n', '|'));
        PauseMenu.Instance.UseCamera(Camera.main);
        await Shot("E_pause.png");
        Check(TitleUi.ClickButton(pause, "서버 나가기"), "clicked 서버 나가기");
        await WaitFor(() => OnTitle, 60, "title");
        Check(!SyncHost.Instance, "leaving stopped the in-app server");
        await Awaitable.WaitForSecondsAsync(5f);
        Check((await EosSession.Search(serverCode)).found.Count == 0, "the stopped server's session is gone");
    }

    static async Task NoEos()
    {
        Check(EosAccount.State == EosAccount.Status.Off && EosAccount.Problem == "disabled", "COMPANYGAME_NOEOS=1: EOS off (" + EosAccount.Problem + ")");
        Check(GameSession.LocalPlayerId == GameSession.OfflinePlayerId, "the account is this slot's PC GUID");
        Screen.Open("serverjoin");
        await Awaitable.NextFrameAsync();
        Check(TextOf(Screen.Content).Contains("EOS에 연결되지 않았습니다") && !Inputs()[0].interactable, "참가하기: EOS is off, the code field is disabled");
        await Shot("E_join_noeos.png");
        Check((await TitleFlow.ListServers()).Item2 == "eos_offline" && (await TitleFlow.FindCode("ABCDEF")).Item2 == "eos_offline", "list and code searches answer eos_offline");
        Check(await TitleFlow.OpenSolo("") == null && SyncClient.Instance.Connected && SyncClient.Instance.CharacterKnown, "혼자 하기 works with the PC GUID");
        Check(SyncHost.Instance.Session == null && SyncClient.Instance.Link == "tcp", "no EOS session, a TCP link");
        TitleFlow.LeaveServer();
    }

    static async Task OwnProcess(string exe)
    {
        Check(await EosAccount.WhenSettled(60), "EOS login in the editor");
        LocalServerProcess.ExePath = exe;
        int port = FreePort();
        Check(await TitleFlow.OpenServer(new ServerSettings { name = "QA EOS 프로세스", maxPlayers = 6, isPublic = true, port = port }) == null, "서버 열기 started the server process and joined it at 127.0.0.1");
        var running = LocalServerProcess.Running();
        var process = started = System.Diagnostics.Process.GetProcessById(running.pid);
        Check(SyncClient.Instance.Connected && !SyncHost.Instance, "the editor is in the process's server (no in-app server)");
        running = await Poll(async () => { await Task.Yield(); return LocalServerProcess.Running(); }, r => r != null && !string.IsNullOrEmpty(r.code), 60, "the code in running.json");
        Check(running.code.Length == 6, "running.json has the server code " + running.code);
        var entry = await Poll(async () => (await EosSession.Search(running.code)).found.FirstOrDefault(), e => e != null, 30, "the process's session");
        Check(entry.puid == EosAccount.LocalPuid && entry.isPublic && entry.name == "QA EOS 프로세스", "the process made the session (same OS user: same PUID)");
        string log = ServerLog();
        Check(log.Contains("confirmed (ID token)"), "the server process confirmed the editor with its ID token");
        TitleFlow.ReturnToTitle();
        await WaitFor(() => OnTitle, 60, "title");
        Check(LocalServerProcess.Stop() && process.WaitForExit(10000), "끄기 stopped the process");
        Check(ServerLog().Contains("removed: Success"), "it removed its session before quitting");
        Check((await EosSession.Search(running.code)).found.Count == 0, "the session is gone");
    }

    // ================= the bundle for another PC (criterion 5) =================

    public static void RunBundle()
    {
        checks = 0;
        EosConfig.Write();
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        string win = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Windows"));
        if (Directory.Exists(win)) Directory.Delete(win, true);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = Path.Combine(win, "CompanyGame.exe"), target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
        Check(report.summary.result == BuildResult.Succeeded, "Windows game built (" + scenes.Length + " scenes)");
        Check(File.Exists(Path.Combine(win, "CompanyGame_Data/StreamingAssets/EOS/eos_windows_config.json")), "the EOS config is in the Windows build");
        EosTestQA.CopyTemplates(win, "README.txt");
        string winZip = EosTestQA.Zip(win, "Windows", _ => false);

        string mac = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Mac"));
        if (Directory.Exists(mac)) Directory.Delete(mac, true); // the plugin notes that building over an old .app misbehaves
        Directory.CreateDirectory(mac);
        string app = Path.Combine(mac, "CompanyGame.app");
        Check(EosTestQA.BuildMacApp(app, scenes) == BuildResult.Succeeded, "macOS game built (universal)");
        Check(EosTestQA.Universal(Path.Combine(app, "Contents/MacOS/CompanyGame")), "the mac executable is universal");
        Check(Directory.GetFiles(app, "eos_macos_config.json", SearchOption.AllDirectories).Length == 1, "the EOS config is in the app");
        EosTestQA.CopyTemplates(mac, "README.txt");
        string macZip = EosTestQA.Zip(mac, "Mac", rel => rel.StartsWith("CompanyGame.app/Contents/MacOS/"));
        Debug.Log("[EosConnectQA] RESULT bundle passed, checks=" + checks + " (" + winZip + " " + new FileInfo(winZip).Length / (1024 * 1024) + "MB, "
                  + macZip + " " + new FileInfo(macZip).Length / (1024 * 1024) + "MB)");
    }

    // ================= helpers =================

    static TitleScreen Screen => TitleScreen.Current;
    static bool OnTitle => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null;
    static bool InGame => SceneManager.GetActiveScene().path == TitleFlow.GameScene && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading;
    static TMP_InputField[] Inputs() => Screen.Content.GetComponentsInChildren<TMP_InputField>();

    /// <summary>A fake TCP client's Hello; the Reject reason, or 0 for a Welcome.</summary>
    static async Task<RejectReason> FakeHello(int port, string account, string token)
    {
        var socket = new TcpClientTransport();
        await socket.Connect("127.0.0.1", port);
        var peer = new QaPeer(socket);
        peer.Send(SyncProtocol.Hello(account, "가짜", "QA/fake", token: token));
        float end = Time.realtimeSinceStartup + 12f;
        try
        {
            while (Time.realtimeSinceStartup < end)
            {
                var first = peer.Take(m => m.kind == ToClient.Welcome || m.kind == ToClient.Reject);
                if (first != null) return first.kind == ToClient.Welcome ? 0 : first.reason;
                await Awaitable.NextFrameAsync();
            }
            throw new Exception("TIMEOUT waiting for the answer to a fake Hello of " + account);
        }
        finally { socket.Close(); }
    }

    // Asks again every 5 seconds (EOS rate-limits searches) until the answer is right.
    static async Task<T> Poll<T>(Func<Task<T>> ask, Func<T, bool> ok, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (true)
        {
            T answer = await ask();
            if (ok(answer)) return answer;
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what);
            await Awaitable.WaitForSecondsAsync(5f);
        }
    }

    static async Task WaitFor(Func<bool> condition, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what + (Screen ? " [screen " + Screen.Screen + "]" : ""));
            await Awaitable.NextFrameAsync();
        }
    }

    static async Task Click(string label)
    {
        await WaitFor(() => Screen && !Screen.Busy, 15, "screen not busy before clicking " + label);
        Check(Screen.Click(label), "clicked '" + label + "' on " + Screen.Screen);
        await Awaitable.NextFrameAsync();
    }

    // The process may still be writing it.
    static string ServerLog()
    {
        using var stream = new FileStream(Path.Combine(ServerSettings.Folder, "server.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static string TextOf(Transform root) => string.Join("\n", root.GetComponentsInChildren<TMP_Text>().Select(t => t.text));

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

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("[EosConnectQA] FAIL: " + what);
        checks++;
        Debug.Log("[EosConnectQA] ok: " + what);
    }
}
