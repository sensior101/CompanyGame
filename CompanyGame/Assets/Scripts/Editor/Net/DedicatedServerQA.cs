using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using QaPeer = SyncLayerQA.QaPeer;
using QaMsg = SyncLayerQA.QaMsg;

/// <summary>
/// QA of the dedicated server (Docs/Multiplayer_Redesign.md 12.15). Server folders and logs go to _temp/qa6/&lt;run&gt;/;
/// screenshots to env QA_SHOTS (D_*.png). The server is the final Windows game run headless (EosConnectQA.RunBundle first).
///   -executeMethod DedicatedServerQA.RunConsole  (with -quit) fake TCP clients: the settings reach them, file edits apply at
///                                                once, every console command, a non-admin's chat command refused, stop + restart
///   -executeMethod DedicatedServerQA.RunPlay     the editor game joins the dedicated server and plays: every chat command,
///                                                permissions, kicked/banned, /stop saves, the character is there after a restart
///   -executeMethod DedicatedServerQA.RunEos      (EOS on) the dedicated server lists itself under its settings name; stop removes it
///   -executeMethod DedicatedServerQA.RunLoad     (with -quit) 50 fake TCP clients for 60 s against the pass criteria
/// Play-mode runs use profile slot 3, run WITHOUT -quit and exit the editor themselves.
/// </summary>
[InitializeOnLoad]
public static class DedicatedServerQA
{
    const string Flag = "DedicatedServerQA.mode", Tag = "[DedicatedServerQA] ";
    const string Refused = "관리자만 쓸 수 있는 명령입니다.";
    static int checks;
    static bool sync; // -quit runs block on waits; play-mode runs await frames
    static readonly List<Server> servers = new List<Server>();
    static readonly List<string> logs = new List<string>(); // play mode: the editor's log lines (the chat logs "[시스템] …")

    static string Exe => Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Windows/CompanyGame.exe"));
    static string RunRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../_temp/qa6"));

    static DedicatedServerQA() => EditorApplication.playModeStateChanged += OnState;

    // ================= settings and the console (fake clients) =================

    public static void RunConsole()
    {
        RunSync("console", () =>
        {
            int port = FreePort();
            var s = Server.Start("console", new ServerSettings { name = "QA 전용 서버", maxPlayers = 3, password = "pw1", port = port, nightSkip = "1-5", trafficCars = 6 });
            WaitReady(s).Wait();
            Section("settings");
            Check(TryJoin(port, "qa-x", "QA_x", "wrong").Result.reason == RejectReason.Password, "a wrong password is refused (Password)");
            var a = Join(port, "qa-a", "QA_a", "pw1").Result;
            var b = Join(port, "qa-b", "QA_b", "pw1").Result;
            var c = Join(port, "qa-c", "QA_c", "pw1").Result;
            Check(a.serverName == "QA 전용 서버", "the Welcome carries the settings' name");
            Check(TryJoin(port, "qa-d", "QA_d", "pw1").Result.reason == RejectReason.Full, "maxPlayers 3: a fourth is refused (Full)");
            var clock = Clock(a);
            Check(clock.start == 1 && clock.end == 5 && clock.cars == 6, "the clock section carries night skip 1-5 and 6 cars (" + clock.start + "-" + clock.end + ", " + clock.cars + ")");

            Section("console commands");
            Console(s, "help", "명령: players");
            Console(s, "players", "접속 3/3명");
            Check(s.Printed("- QA_a (qa-a)") && s.Printed("- QA_c (qa-c)"), "players lists names and accounts");
            Console(s, "say 안녕하세요", "공지를 보냈습니다");
            foreach (var f in new[] { a, b, c }) Notice(f, "[공지] 안녕하세요").Wait();
            Check(true, "say: every player got the announcement");

            Section("player chat");
            Op(b, "안녕 <b>모두</b>", ServerWorld.ChatKind);
            foreach (var f in new[] { a, b, c })
                Check(ChatLine(f).Result == "QA_b|<noparse>안녕 <b>모두</b></noparse>", "chat reaches everyone (connection " + f.number + ") under the character name, rich text literal");
            Check(OpResult(b).Result == "", "chat: no error for the sender");
            Op(b, new string('가', ServerWorld.ChatMax + 1), ServerWorld.ChatKind);
            Check(OpResult(b).Result.Contains(ServerWorld.ChatMax + "자") && !a.peer.Has(m => IsEvent(a, m, WorldSection.ChatEvent)), "a line over " + ServerWorld.ChatMax + " characters is refused and sent to no one");
            Console(s, "kick QA_c", "QA_c을(를) 내보냈습니다");
            Check(Rejected(c, RejectReason.Kicked).Result, "kick: QA_c is closed with Reject(Kicked)");
            Console(s, "ban qa_b", "QA_b을(를) 차단했습니다(내보냄)");
            Check(Rejected(b, RejectReason.Banned).Result && s.File().bans.Contains("qa-b"), "ban (any case): QA_b closed with Reject(Banned), its account in settings.json");
            Check(TryJoin(port, "qa-b", "QA_b", "pw1").Result.reason == RejectReason.Banned, "ban: QA_b joining again is refused (Banned)");
            Console(s, "unban QA_b", "QA_b의 차단을 풀었습니다");
            Check(!s.File().bans.Contains("qa-b"), "unban: out of settings.json");
            b = Join(port, "qa-b", "QA_b", "pw1").Result;
            Check(b.hadCharacter, "unban: QA_b joins again (its character is there)");

            Section("chat permissions");
            Op(a, "players");
            Check(OpResult(a).Result == Refused, "a non-admin's chat command is refused: " + Refused);
            Console(s, "admin QA_a", "QA_a을(를) 관리자로 정했습니다");
            Check(s.File().admins.Contains("qa-a"), "admin: the account is in settings.json");
            Op(a, "players");
            Notice(a, "접속 2/3명").Wait();
            Check(OpResult(a).Result == "", "an admin's chat command runs (players answered as Notice)");
            Op(a, "admin QA_b");
            Notice(a, "관리자 지정·해제는 서버 콘솔이나 서버를 연 사람만").Wait();
            Check(!s.File().admins.Contains("qa-b"), "an admin cannot make admins");
            Console(s, "admin QA_b", "관리자로 정했습니다");
            Op(a, "kick QA_b");
            Notice(a, "서버 연 사람과 관리자는 내보내거나 차단할 수 없습니다").Wait();
            Check(!Rejected(b, RejectReason.Kicked, 1).Result, "an admin cannot kick another admin");
            Console(s, "unadmin QA_b", "관리자에서 뺐습니다");
            Console(s, "unadmin QA_a", "QA_a을(를) 관리자에서 뺐습니다");
            Op(a, "save");
            Check(OpResult(a).Result == Refused && !s.File().admins.Contains("qa-a"), "unadmin: refused again, out of settings.json");

            Section("time and save");
            Console(s, "time 22:30", "월드 시각을 22:30(으)로");
            Until(() => Clock(a).minute % 1440 == 22 * 60 + 30, 10, "the clock at 22:30");
            Check(true, "time 22:30: the world clock is at 22:30");
            string worldDat = Path.Combine(s.folder, "worlds", s.File().world, WorldStore.FileName);
            var before = File.GetLastWriteTimeUtc(worldDat);
            Thread.Sleep(50);
            Console(s, "save", "월드를 저장했습니다");
            Check(File.GetLastWriteTimeUtc(worldDat) > before, "save: world.dat written now");
            Console(s, "dance", "모르는 명령입니다: dance");

            Section("the file edited while running");
            int mark = s.Mark;
            var file = s.File();
            file.trafficCars = 9; file.bans.Add("qa-a"); file.name = "다른 이름"; file.nightSkip = "2-4";
            Thread.Sleep(1100); // a new modification time
            file.Save(s.settingsPath);
            Until(() => Clock(b).cars == 9 && Clock(b).start == 2, 10, "9 cars and night 2-4 at the client");
            Check(true, "trafficCars 9 and nightSkip 2-4 reach the client at once");
            Check(Rejected(a, RejectReason.Banned, 10).Result, "a ban added to the file kicks QA_a at once (Banned)");
            Until(() => s.Printed("needs a restart: name", mark), 5, "the restart note");
            Check(true, "a changed name is only logged (needs a restart)");

            Section("stop and restart");
            before = File.GetLastWriteTimeUtc(worldDat);
            Thread.Sleep(50);
            string world = s.File().world;
            Console(s, "stop", "월드를 저장하고 서버를 끕니다");
            Until(() => s.process.HasExited, 20, "the server to stop");
            Check(s.process.ExitCode == 0 && File.GetLastWriteTimeUtc(worldDat) > before, "stop: world.dat saved, exit code 0");
            s = Server.Start("console", null);
            WaitReady(s).Wait();
            b = Join(port, "qa-b", "QA_b", "pw1").Result;
            Check(b.hadCharacter && s.File().world == world && b.serverName == "다른 이름", "after a restart: the same world, QA_b's character, the new name");
            Check(TryJoin(port, "qa-a", "QA_a", "pw1").Result.reason == RejectReason.Banned, "the ban from the file is still there after the restart");
            s.Command("stop");
            Until(() => s.process.HasExited, 20, "the server to stop");
        });
    }

    // ================= the game on the dedicated server (play mode) =================

    public static void RunPlay() => Enter("play");

    public static void RunEos() => Enter("eos");

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
        checks = 0;
        sync = false;
        Application.logMessageReceived += Capture;
        try
        {
            if (!File.Exists(Exe)) throw new Exception("no " + Exe + ": run EosConnectQA.RunBundle first");
            await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy, 30, "title");
            ProfileSlot.Set(3);
            if (mode == "play") await Play();
            else await Eos();
            Debug.Log(Tag + "RESULT " + mode + " passed, checks=" + checks);
        }
        catch (Exception e)
        {
            Debug.LogError(Tag + "RESULT " + mode + " FAILED: " + e.Message + "\n" + e.StackTrace);
            code = 1;
        }
        Application.logMessageReceived -= Capture;
        if (SyncClient.Instance) SyncClient.Instance.Disconnect();
        KillAll();
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    static void Capture(string message, string stack, LogType type) { lock (logs) logs.Add(message); }

    // A system line in the editor's chat (ChatUIManager logs each one as "[시스템] …").
    static Task ChatShows(string line) => WaitFor(() => { lock (logs) return logs.Exists(l => l.EndsWith("] " + line, StringComparison.Ordinal)); }, 10, "the chat line '" + line + "'");

    static async Task Play()
    {
        int port = FreePort();
        var s = Server.Start("play", new ServerSettings { name = "QA 플레이 서버", maxPlayers = 4, port = port, trafficCars = 7, nightSkip = "0-6" });
        await WaitReady(s);
        var client = SyncClient.Instance;
        Check(await TitleFlow.JoinServer("127.0.0.1:" + port, "", false) == null && client.ServerName == "QA 플레이 서버", "the editor game joins the dedicated server");
        Check(await TitleFlow.CreateServerCharacter("QA관리자", "male") == null && TitleFlow.EnterServerWorld() == null, "made a character and entered");
        await WaitFor(() => InGame && WorldStateSync.World, 90, "the game map");
        await WaitFor(() => NpcClock.Cars == 7, 10, "the server's car count");
        Check(GameTime.NightSkipStart == 0 && GameTime.NightSkipEnd == 6, "the game takes the server's car count (7) and night skip 0-6");
        await Shot("D_ingame.png");

        Section("chat commands");
        var f = await Join(port, "qa-play-f", "QA_f");
        Op(f, "say 몰래 공지");
        Check(await OpResult(f) == Refused, "a non-admin (fake client) is refused");
        await Chat("/players");
        await ChatShows(Refused);
        Check(true, "the editor before admin: refused too, in its chat");
        await Shot("D_chat_refused.png", true);
        Console(s, "admin QA관리자", "QA관리자을(를) 관리자로 정했습니다");
        await Chat("/help");
        await ChatShows("채팅에서는 앞에 / 를 붙입니다. 예: /kick 홍길동");
        Check(true, "/help answers in the chat");
        await Chat("/players");
        await ChatShows("- QA_f (qa-play-f)");
        await ChatShows("접속 2/4명");
        Check(true, "/players lists the two players");
        await Shot("D_chat_players.png", true);
        await Chat("/say 공지 시험입니다");
        await ChatShows("[공지] 공지 시험입니다");
        await Notice(f, "[공지] 공지 시험입니다");
        Check(true, "/say reaches everyone");
        await Shot("D_chat_say.png");
        await Chat("/kick QA_f");
        Check(await Rejected(f, RejectReason.Kicked), "/kick: the fake is closed (Kicked)");
        await ChatShows("QA_f을(를) 내보냈습니다.");
        f = await Join(port, "qa-play-f", "QA_f");
        Check(f.hadCharacter, "after a kick the fake may join again");
        await Chat("/ban QA_f");
        Check(await Rejected(f, RejectReason.Banned) && s.File().bans.Contains("qa-play-f"), "/ban: closed (Banned), in settings.json");
        Check((await TryJoin(port, "qa-play-f", "QA_f")).reason == RejectReason.Banned, "/ban: joining again is refused");
        await Chat("/unban QA_f");
        await ChatShows("QA_f의 차단을 풀었습니다.");
        f = await Join(port, "qa-play-f", "QA_f");
        Check(f.hadCharacter, "/unban: the fake joins again");
        await Chat("/admin QA_f");
        await ChatShows("관리자 지정·해제는 서버 콘솔이나 서버를 연 사람만 할 수 있습니다.");
        Check(!s.File().admins.Contains("qa-play-f"), "/admin by an admin who did not open the server: refused");
        Console(s, "admin QA_f", "QA_f을(를) 관리자로 정했습니다");
        Op(f, "say 나도 관리자");
        Check(await OpResult(f) == "", "the fake made admin on the console: its command runs");
        await ChatShows("[공지] 나도 관리자");
        Op(f, "kick QA관리자");
        await Notice(f, "서버 연 사람과 관리자는 내보내거나 차단할 수 없습니다");
        Check(client.Connected, "an admin cannot kick another admin (the editor stays)");
        Console(s, "unadmin QA_f", "관리자에서 뺐습니다");
        Op(f, "save");
        Check(await OpResult(f) == Refused, "unadmin: the fake is refused again");
        await Chat("/time 21");
        await WaitFor(() => GameClock.Current.Now.hour == 21, 15, "the game clock at 21:00");
        await ChatShows("월드 시각을 21:00(으)로 옮겼습니다.");
        Check(true, "/time 21: the game clock moves to 21:00");
        await Chat("/save");
        await ChatShows("월드를 저장했습니다.");
        Check(true, "/save answers");
        await Shot("D_chat_admin.png", true);

        Section("kicked, /stop and a restart");
        Console(s, "kick QA관리자", "QA관리자을(를) 내보냈습니다");
        await WaitFor(() => !client.Connected && client.LastError == "Kicked", 10, "the editor kicked");
        await ChatShows("관리자가 서버에서 내보냈습니다.");
        Check(TitleFlow.Message("Kicked") == "관리자가 서버에서 내보냈습니다." && TitleFlow.Message("Banned").StartsWith("이 서버에서 차단되어"), "the kicked line in the chat, the title's kick and ban texts");
        await Shot("D_kicked.png", true);
        TitleFlow.ReturnToTitle();
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy, 60, "title");
        Check(await TitleFlow.JoinServer("127.0.0.1:" + port, "", false) == null && client.HasCharacter, "joined again after the kick (the character is there)");
        Check(TitleFlow.EnterServerWorld() == null, "entered again");
        await WaitFor(() => InGame && WorldStateSync.World && CharacterSync.Active, 90, "the game map again");
        f.peer.client.Close();
        await Chat("/stop");
        await WaitFor(() => s.process.HasExited, 20, "/stop to end the server");
        Check(s.process.ExitCode == 0 && s.Log().Contains("[SyncServer] stopping: saving the world and quitting"), "/stop: the server saved and quit (exit 0)");
        await WaitFor(() => !client.Connected, 10, "the editor to notice");
        s = Server.Start("play", null);
        await WaitReady(s);
        TitleFlow.ReturnToTitle();
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy, 60, "title");
        Check(await TitleFlow.JoinServer("127.0.0.1:" + port, "", false) == null && client.HasCharacter && client.CharacterName == "QA관리자", "after the restart the character QA관리자 is there");
        TitleFlow.LeaveServer();
        s.Command("stop");
        await WaitFor(() => s.process.HasExited, 20, "the server to stop");
    }

    static async Task Eos()
    {
        Check(await EosAccount.WhenSettled(60), "EOS login in the editor (" + EosAccount.State + " " + EosAccount.Problem + ")");
        var s = Server.Start("eos", new ServerSettings { name = "QA 전용 EOS", maxPlayers = 10, isPublic = true, port = FreePort() }, true);
        await WaitReady(s);
        string running = Path.Combine(s.folder, "running.json");
        string code = "";
        await WaitFor(() => File.Exists(running) && (code = RunningCode(running)).Length == 6, 60, "the server code in running.json");
        Check(s.Printed("EOS P2P on, session " + code), "the console shows the server code " + code);
        var entry = await Poll(async () => (await EosSession.Search(code)).found.FirstOrDefault(), e => e != null, 60, "the session by code");
        Check(entry.name == "QA 전용 EOS" && entry.maxPlayers == 10 && entry.isPublic && entry.puid == EosAccount.LocalPuid, "the dedicated server's session: the settings' name, 10 players, public");
        await Poll(async () => (await EosSession.Search(null)).found.Any(e => e.code == code), any => any, 60, "the public list");
        Check(true, "it is in the public list");
        s.Command("stop");
        await WaitFor(() => s.process.HasExited, 20, "the server to stop");
        Check(s.Log().Contains("removed: Success"), "stop removed the session");
        Check((await EosSession.Search(code)).found.Count == 0, "the session is gone");
    }

    // ================= load =================

    public static void RunLoad()
    {
        RunSync("load", () =>
        {
            const int Players = 50, Seconds = 60;
            int port = FreePort();
            var s = Server.Start("load", new ServerSettings { name = "QA 부하", maxPlayers = Players, port = port });
            WaitReady(s).Wait();
            s.process.Refresh();
            var idle0 = s.process.TotalProcessorTime;
            Thread.Sleep(10000);
            s.process.Refresh();
            double idleCpu = (s.process.TotalProcessorTime - idle0).TotalSeconds / 10 * 100;
            var fakes = new List<Fake>();
            for (int i = 0; i < Players; i++) fakes.Add(Join(port, "qa-load-" + i, "QA_load" + i, "", "load").Result);
            Check(fakes.Count == Players, Players + " fake clients joined with characters");
            var quiet = new List<double>(); // round trips with everyone joined but nobody moving
            for (int i = 0; i < 20; i++)
            {
                var sw = Stopwatch.StartNew();
                fakes[0].peer.Send(SyncProtocol.Ping(7));
                Until(() => { Drain(fakes[0], out _, out _, frame => { if (frame[0] == (byte)ToClient.Pong) quiet.Add(sw.Elapsed.TotalMilliseconds); }); return quiet.Count > i; }, 5, "a quiet Pong");
                Thread.Sleep(50);
            }
            quiet.Sort();
            Debug.Log(Tag + $"quiet round trip p50={quiet[10]:F1} ms max={quiet[19]:F1} ms");
            var ids = new ulong[Players];
            for (int i = 0; i < Players; i++)
            {
                ids[i] = SyncProtocol.MakeId(fakes[i].number, 1);
                fakes[i].peer.Send(SyncProtocol.Spawn(ids[i], "Net/Avatar", "load", EntityFlags.FollowsOwnerMap, new[] { SyncLayerQA.Pos(new Vector3(i, 0, 0), 0) }));
            }
            Thread.Sleep(1000);
            foreach (var f in fakes) Drain(f, out _, out _, null);
            int stateSize = SyncProtocol.State(ids[0], 0, SyncLayerQA.Pos(Vector3.zero, 0)).Length; // a relayed position State has the same size
            var process = s.process;
            process.Refresh();
            var cpu0 = process.TotalProcessorTime;
            var clock = Stopwatch.StartNew();
            var rtts = new List<double>();
            long bytes = 0, frames = 0, positions = 0;
            int rounds = 0, loops = 0;
            long nextRound = 0, nextPing = 0;
            while (clock.ElapsedMilliseconds < Seconds * 1000)
            {
                long now = clock.ElapsedMilliseconds;
                if (now >= nextRound)
                {
                    nextRound += 100;
                    rounds++;
                    for (int i = 0; i < Players; i++) fakes[i].peer.Send(SyncProtocol.State(ids[i], 0, SyncLayerQA.Pos(new Vector3(i, 0, rounds * .1f), rounds)));
                }
                if (now >= nextPing) { nextPing += 500; fakes[0].peer.Send(SyncProtocol.Ping((uint)now)); }
                foreach (var f in fakes)
                {
                    positions += Drain(f, out long b, out long n, frame =>
                    {
                        if (frame[0] == (byte)ToClient.Pong) rtts.Add(clock.ElapsedMilliseconds - BitConverter.ToUInt32(frame, 1));
                    }, stateSize);
                    bytes += b; frames += n;
                }
                Thread.Sleep(5);
                loops++;
            }
            double seconds = clock.Elapsed.TotalSeconds;
            process.Refresh();
            double cpu = (process.TotalProcessorTime - cpu0).TotalSeconds / seconds * 100;
            long memory = MemoryMb(process.Id);
            var end = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < end) { foreach (var f in fakes) positions += Drain(f, out _, out _, null, stateSize); Thread.Sleep(20); } // the last ones in flight
            long expected = (long)rounds * Players * (Players - 1);
            rtts.Sort();
            double p50 = rtts.Count > 0 ? rtts[rtts.Count / 2] : -1, p95 = rtts.Count > 0 ? rtts[(int)(rtts.Count * .95)] : -1, max = rtts.Count > 0 ? rtts[rtts.Count - 1] : -1;
            double upload = (bytes + 4.0 * frames) / seconds;
            Debug.Log(Tag + $"LOAD players={Players} seconds={seconds:F1} rounds={rounds} positions={positions} expected={expected} ({100.0 * positions / expected:F2}%) "
                      + $"server_cpu_pct_of_one_core={cpu:F1} (idle, no players: {idleCpu:F1}) memory_mb={memory} upload_bytes_per_s={upload:F0} ({upload * 8 / 1e6:F2} Mbps) msgs_per_s={frames / seconds:F0} "
                      + $"rtt_ms p50={p50:F0} p95={p95:F0} max={max:F0} samples={rtts.Count} qa_loop_ms={seconds * 1000 / loops:F1}");
            Check(positions >= expected * .99, $"positions received {positions}/{expected} (>= 99%)");
            Check(rtts.Count >= Seconds && p95 - quiet[10] <= 100, $"round trip p95 {p95:F0} ms, {p95 - quiet[10]:F0} ms over the quiet {quiet[10]:F0} ms (load adds <= 100 ms, {rtts.Count} samples)");
            Check(cpu <= 100, $"server CPU {cpu:F1}% of one core (<= 100%: the server's loop is one thread)");
            Check(upload <= 1.25e6, $"server upload {upload * 8 / 1e6:F2} Mbps (<= 10 Mbps)");
            foreach (var f in fakes) f.peer.client.Close();
            s.Command("stop");
            Until(() => s.process.HasExited, 30, "the server to stop");
        });
    }

    // The working set from tasklist (Process.WorkingSet64 reads 0 for another process here).
    static long MemoryMb(int pid)
    {
        var info = new ProcessStartInfo("tasklist", "/FI \"PID eq " + pid + "\" /FO CSV /NH") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using var p = Process.Start(info);
        string line = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        string[] cells = line.Split('"');
        string kb = cells.Length > 9 ? new string(cells[9].Where(char.IsDigit).ToArray()) : "";
        return long.TryParse(kb, out long value) ? value / 1024 : -1;
    }

    // Frames waiting at a fake, counted without parsing (the load run); the number that were relayed positions.
    static long Drain(Fake f, out long bytes, out long frames, Action<byte[]> each, int stateSize = -1)
    {
        bytes = frames = 0;
        long positions = 0;
        while (f.peer.client.TryReceive(out byte[] frame))
        {
            bytes += frame.Length; frames++;
            if (frame.Length == stateSize && frame[0] == (byte)ToClient.State) positions++;
            each?.Invoke(frame);
        }
        return positions;
    }

    // ================= the server process =================

    sealed class Server
    {
        public Process process;
        public string folder, settingsPath, log;
        public int port;
        readonly List<string> output = new List<string>();

        /// <summary>The bundle's exe on _temp/qa6/&lt;name&gt;/settings.json; null settings: the file there as it is (a restart).</summary>
        public static Server Start(string name, ServerSettings settings, bool eos = false)
        {
            string folder = Path.Combine(RunRoot, name), settingsPath = Path.Combine(folder, "settings.json"), log = Path.Combine(folder, "server.log");
            if (settings != null)
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                settings.Save(settingsPath);
            }
            return Launch(name, new ProcessStartInfo(Exe, $"-batchmode -nographics -console -gameserver -serversettings \"{settingsPath}\" -logFile \"{log}\""), settingsPath, log, eos);
        }

        public static Server Launch(string name, ProcessStartInfo info, string settingsPath, string log, bool eos)
        {
            info.UseShellExecute = false; info.CreateNoWindow = true;
            info.RedirectStandardInput = true; info.RedirectStandardOutput = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            if (eos) info.EnvironmentVariables.Remove(EosAccount.DisableEnv);
            else info.EnvironmentVariables[EosAccount.DisableEnv] = "1";
            var server = new Server { folder = Path.GetDirectoryName(settingsPath), settingsPath = settingsPath, log = log, port = ServerSettings.Load(settingsPath).port };
            server.process = Process.Start(info);
            server.process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (server.output) server.output.Add(e.Data); };
            server.process.BeginOutputReadLine();
            servers.Add(server);
            Debug.Log(Tag + "started " + name + " (pid " + server.process.Id + ", port " + server.port + ")");
            return server;
        }

        /// <summary>A console line, as UTF-8 (Korean names).</summary>
        public void Command(string line)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
            process.StandardInput.BaseStream.Flush();
        }

        // The console's answers are "[Admin] …" lines of the server log (its stdout is the log file under -logFile).
        public int Mark => Log().Length;

        public bool Printed(string part, int from = 0)
        {
            string text = Log();
            return from <= text.Length && text.IndexOf(part, from, StringComparison.Ordinal) >= 0;
        }

        /// <summary>What the process wrote to stdout (StartServer.bat's echo lines).</summary>

        public ServerSettings File() => ServerSettings.Load(settingsPath);
        public string Log() => ReadShared(log);
    }

    static async Task WaitReady(Server s)
    {
        await WaitFor(() => s.Printed("console ready") || s.process.HasExited, 90, "the dedicated server to be ready");
        if (s.process.HasExited) throw new Exception("the server exited (" + s.process.ExitCode + "), see " + s.log);
        Check(true, "the dedicated server is up (console ready, port " + s.port + ")");
    }

    // A console command; its answer line must show up on the console (stdout).
    static void Console(Server s, string line, string answer)
    {
        int mark = s.Mark;
        s.Command(line);
        Until(() => s.Printed(answer, mark), 10, "console '" + line + "' → '" + answer + "'");
        Check(true, "console " + line + " → " + answer);
    }

    static void KillAll()
    {
        foreach (var s in servers)
            try { if (!s.process.HasExited) Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + s.process.Id) { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(5000); }
            catch (Exception) { }
        servers.Clear();
    }

    // The code in running.json; "" while it is missing or being written.
    static string RunningCode(string path)
    {
        try { return JsonUtility.FromJson<LocalServerProcess.RunningInfo>(ReadShared(path))?.code ?? ""; }
        catch (Exception) { return ""; }
    }

    static string ReadShared(string path)
    {
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ================= fake clients =================

    sealed class Fake
    {
        public QaPeer peer;
        public uint number;
        public ulong world;
        public string serverName;
        public bool hadCharacter;
        public RejectReason reason;
    }

    static async Task<Fake> Join(int port, string account, string name, string password = "", string map = "qa/map")
    {
        var f = await TryJoin(port, account, name, password, map);
        if (f.number == 0) throw new Exception(name + " was refused: " + f.reason);
        return f;
    }

    // Hello, the character (made when new), the world entity. number 0 and the reason when refused.
    static async Task<Fake> TryJoin(int port, string account, string name, string password = "", string map = "qa/map")
    {
        var socket = new TcpClientTransport();
        Task connect = socket.Connect("127.0.0.1", port);
        if (sync) connect.GetAwaiter().GetResult(); else await connect;
        var f = new Fake { peer = new QaPeer(socket) };
        f.peer.Send(SyncProtocol.Hello(account, name, map, password));
        await Wait(() => f.peer.Has(m => m.kind == ToClient.Welcome || m.kind == ToClient.Reject), 10, name + "'s welcome");
        var first = f.peer.Take(m => m.kind == ToClient.Welcome || m.kind == ToClient.Reject);
        if (first.kind == ToClient.Reject) { f.reason = first.reason; socket.Close(); return f; }
        f.number = first.number; f.serverName = first.name;
        await Wait(() => f.peer.Has(ToClient.Character), 10, name + "'s character");
        f.hadCharacter = f.peer.Take(m => m.kind == ToClient.Character).has;
        if (!f.hadCharacter)
        {
            f.peer.Send(SyncProtocol.CharacterCreate(name, "male"));
            await Wait(() => f.peer.Has(ToClient.CharacterResult), 10, name + "'s new character");
            var result = f.peer.Take(m => m.kind == ToClient.CharacterResult).result;
            if (result != CharacterResult.Ok) throw new Exception(name + ": character " + result);
        }
        await Wait(() => f.peer.Has(m => m.kind == ToClient.Spawn && m.key == ServerWorld.PrefabKey), 10, name + "'s world entity");
        f.world = f.peer.inbox.Find(m => m.kind == ToClient.Spawn && m.key == ServerWorld.PrefabKey).id;
        return f;
    }

    // Earlier answers are dropped first: OpResult then reads this command's.
    static void Op(Fake f, string line, string kind = ServerAdmin.Kind)
    {
        f.peer.Pump();
        f.peer.inbox.RemoveAll(m => IsEvent(f, m, WorldSection.OpResultEvent));
        f.peer.Send(SyncProtocol.Event(f.world, 0, WorldSection.OpEvent, EventTarget.Server, Blob(w =>
    {
        SyncProtocol.WriteStr(w, kind);
        SyncProtocol.WriteStr(w, JsonUtility.ToJson(new SharedOpArgs { a = line }));
    })));
    }

    static async Task<string> OpResult(Fake f)
    {
        await Wait(() => f.peer.Has(m => IsEvent(f, m, WorldSection.OpResultEvent)), 10, "an OpResult");
        using var r = SyncProtocol.Reader(f.peer.Take(m => IsEvent(f, m, WorldSection.OpResultEvent)).blob);
        SyncProtocol.ReadStr(r);
        return SyncProtocol.ReadStr(r);
    }

    static async Task Notice(Fake f, string part)
    {
        await Wait(() => f.peer.Has(m => IsEvent(f, m, WorldSection.NoticeEvent) && NoticeText(m).Contains(part)), 10, "the notice '" + part + "'");
        f.peer.Take(m => IsEvent(f, m, WorldSection.NoticeEvent) && NoticeText(m).Contains(part));
    }

    // "name|text" of the next player chat line (world event 6).
    static async Task<string> ChatLine(Fake f)
    {
        await Wait(() => f.peer.Has(m => IsEvent(f, m, WorldSection.ChatEvent)), 10, "a chat line");
        using var r = SyncProtocol.Reader(f.peer.Take(m => IsEvent(f, m, WorldSection.ChatEvent)).blob);
        return SyncProtocol.ReadStr(r) + "|" + SyncProtocol.ReadStr(r);
    }

    static bool IsEvent(Fake f, QaMsg m, byte id) => m.kind == ToClient.Event && m.id == f.world && m.eventId == id && m.sender == 0;

    static string NoticeText(QaMsg m)
    {
        using var r = SyncProtocol.Reader(m.blob);
        return SyncProtocol.ReadStr(r);
    }

    static async Task<bool> Rejected(Fake f, RejectReason reason, float seconds = 10)
    {
        try { await Wait(() => f.peer.Has(m => m.kind == ToClient.Reject && m.reason == reason), seconds, "Reject(" + reason + ")"); }
        catch (TimeoutException) { return false; }
        return true;
    }

    // u32 minute, u8 night start, u8 night end, f64, u32 seed, u8 cars (ServerWorld.ClockBlob)
    static (uint minute, int start, int end, int cars) Clock(Fake f)
    {
        f.peer.Pump();
        var m = f.peer.inbox.FindLast(x => x.kind == ToClient.State && x.id == f.world && x.component == ServerWorld.ClockSection);
        if (m == null) return (0, -1, -1, -1);
        using var r = SyncProtocol.Reader(m.blob);
        uint minute = r.ReadUInt32();
        int start = r.ReadByte(), end = r.ReadByte();
        r.ReadDouble(); r.ReadUInt32();
        return (minute, start, end, r.ReadByte());
    }

    static byte[] Blob(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8);
        write(w);
        w.Flush();
        return stream.ToArray();
    }

    // ================= the game's chat =================

    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    // Types the line into the chat and sends it, as Enter does.
    static async Task Chat(string line)
    {
        var chat = Object.FindAnyObjectByType<ChatUIManager>();
        var input = (TMP_InputField)typeof(ChatUIManager).GetField("chatInput", Private).GetValue(chat);
        input.text = line;
        typeof(ChatUIManager).GetMethod("SendChatMessage", Private).Invoke(chat, null);
        await Awaitable.WaitForSecondsAsync(.5f);
    }

    static bool InGame => SceneManager.GetActiveScene().path == TitleFlow.GameScene && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading;

    // The game camera with the overlay canvases drawn into it; withHistory opens the chat window first (its history).
    static async Task Shot(string file, bool withHistory = false)
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (string.IsNullOrEmpty(dir) || !Camera.main) return;
        Directory.CreateDirectory(dir);
        var chat = Object.FindAnyObjectByType<ChatUIManager>();
        if (withHistory && chat) typeof(ChatUIManager).GetMethod("OpenChat", Private).Invoke(chat, null);
        await Awaitable.WaitForSecondsAsync(.5f);
        var camera = Camera.main;
        var moved = new List<Canvas>();
        foreach (var canvas in Object.FindObjectsByType<Canvas>())
            if (canvas && canvas.isRootCanvas && canvas.isActiveAndEnabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .05f; moved.Add(canvas); }
        try { await TitleShots.Snap(camera, Path.Combine(dir, file)); }
        finally
        {
            foreach (var canvas in moved) if (canvas) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (withHistory && chat) typeof(ChatUIManager).GetMethod("CloseChat", Private).Invoke(chat, null);
        }
    }

    // ================= helpers =================

    static void RunSync(string name, Action body)
    {
        checks = 0;
        sync = true;
        int code = 0;
        try
        {
            body();
            Debug.Log(Tag + "RESULT " + name + " passed, checks=" + checks);
        }
        catch (Exception e)
        {
            var inner = e is AggregateException a ? a.InnerException : e;
            Debug.LogError(Tag + "RESULT " + name + " FAILED: " + inner.Message + "\n" + inner.StackTrace);
            code = 1;
        }
        KillAll();
        if (code != 0) EditorApplication.Exit(code);
    }

    // Blocks in -quit runs (the network threads fill the queues); awaits frames in play mode.
    static Task Wait(Func<bool> condition, float seconds, string what)
    {
        if (!sync) return WaitFor(condition, seconds, what);
        Until(condition, seconds, what);
        return Task.CompletedTask;
    }

    static void Until(Func<bool> condition, float seconds, string what)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("TIMEOUT waiting for " + what);
            Thread.Sleep(20);
        }
    }

    static async Task WaitFor(Func<bool> condition, float seconds, string what)
    {
        if (sync) { Until(condition, seconds, what); return; }
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) throw new TimeoutException("TIMEOUT waiting for " + what);
            await Awaitable.NextFrameAsync();
        }
    }

    // Asks again every 5 seconds (EOS rate-limits searches) until the answer is right.
    static async Task<T> Poll<T>(Func<Task<T>> ask, Func<T, bool> ok, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (true)
        {
            T answer = await ask();
            if (ok(answer)) return answer;
            if (Time.realtimeSinceStartup > end) throw new TimeoutException("TIMEOUT waiting for " + what);
            await Awaitable.WaitForSecondsAsync(5f);
        }
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static void Section(string name) => Debug.Log(Tag + "---- " + name + " ----");

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception(Tag + "FAIL: " + what);
        checks++;
        Debug.Log(Tag + "ok: " + what);
    }
}
