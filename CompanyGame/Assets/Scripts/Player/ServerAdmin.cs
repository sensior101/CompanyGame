using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
#endif

/// <summary>
/// Admin commands of a sync-layer server (Docs/Multiplayer_Redesign.md 12.15), made by ServerWorld while it runs.
///   who      the server console, the one who opened the server (SyncHost.Owner) and the accounts in the settings' admins.
///            A chat command comes as Op kind <see cref="Kind"/> and is judged by the account of its connection only
///   answers  console: "[Admin] …" log lines (the console shows them); chat: Notice lines to the sender
///   console  a server process started with <c>-console</c> only (StartServer.bat; other processes never read stdin: a
///            read blocked on an inherited handle keeps the process from quitting): stdin when it is a pipe or a file (QA), else the console window of StartServer.bat
///            (attached: a Unity player has none of its own; a process started by 서버 열기 has none to attach to and
///            takes chat commands only). A thread only moves lines; they run in Tick. Ctrl+C or closing that window
///            saves and quits (closing gives about 5 seconds)
///   settings the file is read again when it changes (checked every second): admins, bans (online ones are kicked),
///            nightSkip and trafficCars apply at once; the rest needs a restart and is only logged. A command that
///            changes the lists reads the file first and writes back only the lists
/// </summary>
public sealed class ServerAdmin
{
    /// <summary>The Op kind of a chat command; its json is SharedOpArgs with the line in a.</summary>
    public const string Kind = "admin";
    const string Prefix = "[Admin] ", ConsoleArg = "-console";
    static readonly string[] Mirrored = { "[SyncServer]", "[Admin]", "[ServerWorld]", "[Eos]" };

    static readonly ConcurrentQueue<string> consoleLines = new ConcurrentQueue<string>();
    static TextWriter consoleOut;
    static bool consoleStarted;
    static volatile bool ctrlStop, saved;

    readonly ServerWorld world;
    readonly SyncHost host;
    readonly GameServer server;
    DateTime fileTime;
    float nextFileCheck;
    bool stopAsked;

    /// <summary>The answer lines of the last command (console or chat), for QA.</summary>
    public readonly List<string> LastAnswer = new List<string>();

    public ServerAdmin(ServerWorld world, SyncHost host)
    {
        this.world = world;
        this.host = host;
        server = host.Server;
        fileTime = FileTime();
        if (!host.InApp && !Application.isEditor && Array.IndexOf(Environment.GetCommandLineArgs(), ConsoleArg) >= 0) StartConsole();
    }

    ServerSettings Settings => host.Settings;

    public bool IsAdmin(string account) => !string.IsNullOrEmpty(account) && (account == host.Owner || Settings.admins.Contains(account));

    /// <summary>
    /// A chat command from connection <paramref name="sender"/> (account <paramref name="from"/>). Its answers go to that
    /// chat as Notice; the return is the refusal for a non-admin ("" when it ran).
    /// </summary>
    public string Chat(uint sender, string from, string json)
    {
        string line = "";
        try { line = JsonUtility.FromJson<SharedOpArgs>(json)?.a ?? ""; }
        catch (Exception) { }
        if (!IsAdmin(from))
        {
            Debug.Log(Prefix + "refused '" + line + "' from " + from + " (not an admin)");
            return "관리자만 쓸 수 있는 명령입니다.";
        }
        Debug.Log(Prefix + "chat command from " + from + ": " + line);
        foreach (string answer in Run(line, from)) world.Notice(sender, answer);
        return "";
    }

    /// <summary>Every frame (ServerWorld.Update): console lines, a stop asked for, the settings file once a second.</summary>
    public void Tick()
    {
        while (consoleLines.TryDequeue(out string line))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            Debug.Log(Prefix + "> " + line);
            foreach (string answer in Run(line, null)) Debug.Log(Prefix + answer);
        }
        if (ctrlStop) { ctrlStop = false; stopAsked = !host.InApp; }
        if (stopAsked)
        {
            stopAsked = false;
            host.StopAndQuit(); // destroys this server's objects (ServerWorld ends)
            saved = true;
            return;
        }
        if (Time.unscaledTime < nextFileCheck) return;
        nextFileCheck = Time.unscaledTime + 1f;
        ReloadIfChanged();
    }

    // ---- commands ----

    /// <summary>Runs one command; <paramref name="by"/> is the admin's account, null for the console. The answer lines.</summary>
    public List<string> Run(string line, string by)
    {
        LastAnswer.Clear();
        string[] parts = (line ?? "").Trim().TrimStart('/').Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        string command = parts.Length > 0 ? parts[0].ToLowerInvariant() : "", arg = parts.Length > 1 ? parts[1].Trim() : "";
        bool top = by == null || by == host.Owner; // the console or the one who opened the server
        ReloadIfChanged(); // the lists as the file has them now
        switch (command)
        {
            case "help":
                Say("명령: players, kick <이름>, ban <이름|계정>, unban <이름|계정>, admin <이름|계정>, unadmin <이름|계정>, say <글>, save, time <시>[:<분>], stop");
                Say("채팅에서는 앞에 / 를 붙입니다. 예: /kick 홍길동");
                break;
            case "players":
                Say("접속 " + server.PlayerCount + "/" + server.MaxPlayers + "명");
                foreach (var c in server.World.Characters) if (c.online) Say("- " + c.name + " (" + c.account + ")" + (IsAdmin(c.account) ? " 관리자" : ""));
                break;
            case "kick":
            case "ban":
            {
                string account = AccountOf(arg);
                if (account == null) { Say(arg.Length == 0 ? "사용법: " + command + " <이름>" : "'" + arg + "' 캐릭터가 이 월드에 없습니다."); break; }
                if (!top && IsAdmin(account)) { Say("서버 연 사람과 관리자는 내보내거나 차단할 수 없습니다."); break; }
                if (command == "ban")
                {
                    if (!Settings.bans.Contains(account)) Settings.bans.Add(account);
                    server.Banned.Add(account);
                    SaveLists();
                }
                bool was = server.Kick(account, command == "ban" ? RejectReason.Banned : RejectReason.Kicked);
                Say(NameOf(account) + (command == "ban" ? "을(를) 차단했습니다" + (was ? "(내보냄)." : ".") : was ? "을(를) 내보냈습니다." : "은(는) 접속 중이 아닙니다."));
                break;
            }
            case "unban":
            {
                string account = AccountOf(arg) ?? arg;
                if (!Settings.bans.Remove(account)) { Say("'" + arg + "'은(는) 차단 목록에 없습니다."); break; }
                server.Banned.Remove(account);
                SaveLists();
                Say(NameOf(account) + "의 차단을 풀었습니다.");
                break;
            }
            case "admin":
            case "unadmin":
            {
                if (!top) { Say("관리자 지정·해제는 서버 콘솔이나 서버를 연 사람만 할 수 있습니다."); break; }
                string account = AccountOf(arg);
                if (account == null) { Say(arg.Length == 0 ? "사용법: " + command + " <이름|계정>" : "'" + arg + "' 캐릭터가 이 월드에 없습니다."); break; }
                bool add = command == "admin";
                if (add && !Settings.admins.Contains(account)) Settings.admins.Add(account);
                if (!add && !Settings.admins.Remove(account)) { Say(NameOf(account) + "은(는) 설정 파일의 관리자가 아닙니다."); break; }
                SaveLists();
                Say(NameOf(account) + (add ? "을(를) 관리자로 정했습니다." : "을(를) 관리자에서 뺐습니다."));
                break;
            }
            case "say":
                if (arg.Length == 0) { Say("사용법: say <글>"); break; }
                world.Notice(0, "[공지] " + arg);
                Debug.Log(Prefix + "announced: " + arg);
                Say("공지를 보냈습니다.");
                break;
            case "save":
                server.Save();
                Say("월드를 저장했습니다.");
                break;
            case "time":
                Say(SetTime(arg));
                break;
            case "stop":
                if (host.InApp) { Say("앱 안 서버는 일시정지 메뉴의 '서버 나가기'로 끕니다."); break; }
                world.Notice(0, "[공지] 서버를 저장하고 끕니다.");
                Say("월드를 저장하고 서버를 끕니다.");
                stopAsked = true; // after this frame's network work (never inside GameServer.Tick)
                break;
            default:
                Say("모르는 명령입니다: " + command + " (help 로 목록을 봅니다)");
                break;
        }
        return new List<string>(LastAnswer);
    }

    void Say(string line) => LastAnswer.Add(line);

    // "22" or "22:30": the world clock goes forward to the next such time (a skipped night moves it to the night's end).
    string SetTime(string arg)
    {
        string[] hm = arg.Split(':');
        if (!int.TryParse(hm[0], out int hour) || hour < 0 || hour > 23 || hm.Length > 2) return "사용법: time <시>[:<분>] (예: time 22 또는 time 7:30)";
        int minute = 0;
        if (hm.Length == 2 && (!int.TryParse(hm[1], out minute) || minute < 0 || minute > 59)) return "사용법: time <시>[:<분>] (예: time 22 또는 time 7:30)";
        var w = server.World;
        double now = w.WorldMinutes, at = Math.Floor(now / GameServer.DayMinutes) * GameServer.DayMinutes + hour * 60 + minute;
        if (at <= now) at += GameServer.DayMinutes;
        w.WorldMinutes = GameServer.SkipNight(at, w.NightSkipStart, w.NightSkipEnd);
        double inDay = w.WorldMinutes % GameServer.DayMinutes;
        Debug.Log(Prefix + "world clock " + now.ToString("0") + " -> " + w.WorldMinutes.ToString("0"));
        return "월드 시각을 " + ((int)inDay / 60).ToString("00") + ":" + ((int)inDay % 60).ToString("00") + "(으)로 옮겼습니다.";
    }

    // A character of this world by account or name (any case), else something that looks like an account id.
    string AccountOf(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return null;
        foreach (var c in server.World.Characters) if (c.account == arg) return arg;
        foreach (var c in server.World.Characters) if (string.Equals(c.name, arg, StringComparison.OrdinalIgnoreCase)) return c.account;
        return arg.Length >= 16 && arg.IndexOf(' ') < 0 ? arg : null; // an account that never played here (a PUID is 32 letters)
    }

    string NameOf(string account) => server.World.Find(account)?.name ?? account;

    // ---- the settings file ----

    DateTime FileTime()
    {
        try { return host.SettingsPath != null && File.Exists(host.SettingsPath) ? File.GetLastWriteTimeUtc(host.SettingsPath) : default; }
        catch (Exception) { return default; }
    }

    // The lists go into the file as it is now: whatever else was edited there stays.
    void SaveLists()
    {
        if (host.SettingsPath == null) return; // 혼자 하기: nothing to keep
        var file = ServerSettings.Load(host.SettingsPath);
        file.admins = new List<string>(Settings.admins);
        file.bans = new List<string>(Settings.bans);
        try { file.Save(host.SettingsPath); }
        catch (Exception e) { Debug.LogError(Prefix + "cannot write " + host.SettingsPath + ": " + e.Message); }
        fileTime = FileTime();
    }

    void ReloadIfChanged()
    {
        DateTime time = FileTime();
        if (time == fileTime || time == default) return;
        fileTime = time;
        var file = ServerSettings.Load(host.SettingsPath);
        var live = Settings;
        var later = new List<string>();
        if (file.name != live.name) later.Add("name");
        if (file.maxPlayers != live.maxPlayers) later.Add("maxPlayers");
        if (file.isPublic != live.isPublic) later.Add("isPublic");
        if (file.password != live.password) later.Add("password");
        if (file.port != live.port) later.Add("port");
        if (file.world != live.world && file.world.Length > 0) later.Add("world");
        live.admins = file.admins;
        live.bans = file.bans;
        server.Banned.Clear();
        server.Banned.UnionWith(file.bans);
        foreach (string account in file.bans) server.Kick(account, RejectReason.Banned);
        if (file.nightSkip.Length > 0 && file.nightSkip != live.nightSkip && GameServer.TryParseNightSkip(file.nightSkip, out int start, out int end))
            server.SetNightSkip(start, end);
        live.nightSkip = file.nightSkip;
        live.trafficCars = file.trafficCars;
        Debug.Log(Prefix + "settings file read again: " + file.admins.Count + " admins, " + file.bans.Count + " bans, night skip "
                  + server.World.NightSkipStart + "-" + server.World.NightSkipEnd + ", " + file.trafficCars + " cars"
                  + (later.Count > 0 ? "; needs a restart: " + string.Join(", ", later) : ""));
    }

    // ---- the console (a server process) ----

    void StartConsole()
    {
        if (consoleStarted) return;
        consoleStarted = true;
        Func<string> readLine = null;
        Stream output = null;
        try { OpenConsole(out readLine, out output); }
        catch (Exception e) { Debug.LogWarning(Prefix + "no console (" + e.Message + "): commands only from the chat of admins"); }
        if (output != null)
        {
            consoleOut = TextWriter.Synchronized(new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true });
            Application.logMessageReceivedThreaded += Mirror;
        }
        if (readLine == null) { Debug.Log(Prefix + "no console window: commands come from the chat of admins"); return; }
        new Thread(() =>
        {
            try { string line; while ((line = readLine()) != null) consoleLines.Enqueue(line); }
            catch (Exception) { /* the console went away: the chat still works */ }
        }) { IsBackground = true, Name = "ServerConsole" }.Start();
        Debug.Log(Prefix + "console ready: type help");
    }

    static void Mirror(string message, string stack, LogType type)
    {
        bool shown = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        foreach (string prefix in Mirrored) shown |= message.StartsWith(prefix, StringComparison.Ordinal);
        if (!shown) return;
        try { consoleOut.WriteLine(DateTime.Now.ToString("HH:mm:ss ") + message); }
        catch (Exception) { /* the window was closed */ }
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll")] static extern int GetFileType(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool SetConsoleCP(uint codePage);
    [DllImport("kernel32.dll")] static extern bool SetConsoleOutputCP(uint codePage);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool ReadConsoleW(IntPtr handle, [Out] char[] buffer, int toRead, out int read, IntPtr control);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    delegate bool CtrlHandler(int type);
    [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(CtrlHandler handler, bool add);
    static readonly CtrlHandler ctrlHandler = OnCtrl; // kept: Windows calls it later

    // stdin that is a pipe or a file (QA) is used as it is, stdout only when it is a pipe (with -logFile Unity points stdout at
    // the log file, which has every line anyway); otherwise the console window of the batch file.
    static void OpenConsole(out Func<string> readLine, out Stream output)
    {
        const int StdIn = -10, StdOut = -11, Disk = 1, Pipe = 3;
        const uint ReadWrite = 0xC0000000, ShareBoth = 3, OpenExisting = 3;
        IntPtr inHandle = GetStdHandle(StdIn), outHandle = GetStdHandle(StdOut);
        int inType = GetFileType(inHandle), outType = GetFileType(outHandle);
        bool pipedIn = inType == Disk || inType == Pipe, pipedOut = outType == Pipe;
        // -1: the parent's console (cmd running StartServer.bat); 5 = access denied: this process has one already
        bool attached = (!pipedIn || !pipedOut) && (AttachConsole(-1) || Marshal.GetLastWin32Error() == 5);
        if (attached)
        {
            SetConsoleCP(65001); SetConsoleOutputCP(65001); // UTF-8 both ways (Korean names)
            if (!pipedIn) inHandle = CreateFileW("CONIN$", ReadWrite, ShareBoth, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (!pipedOut) outHandle = CreateFileW("CONOUT$", ReadWrite, ShareBoth, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            SetConsoleCtrlHandler(ctrlHandler, true);
        }
        readLine = pipedIn ? new StreamReader(new FileStream(new SafeFileHandle(inHandle, false), FileAccess.Read), new UTF8Encoding(false)).ReadLine
            : attached ? ConsoleLines(inHandle) : null;
        output = pipedOut || attached ? new FileStream(new SafeFileHandle(outHandle, false), FileAccess.Write) : null;
    }

    // Typed lines as UTF-16 (ReadFile on a UTF-8 console loses Korean letters on some Windows versions).
    static Func<string> ConsoleLines(IntPtr handle)
    {
        var buffer = new char[512];
        var line = new StringBuilder();
        return () =>
        {
            while (true)
            {
                int newline = line.ToString().IndexOf('\n');
                if (newline >= 0)
                {
                    string done = line.ToString(0, newline).TrimEnd('\r');
                    line.Remove(0, newline + 1);
                    return done;
                }
                if (!ReadConsoleW(handle, buffer, buffer.Length, out int read, IntPtr.Zero) || read == 0) return null;
                line.Append(buffer, 0, read);
            }
        };
    }

    // Ctrl+C, Ctrl+Break, closing the window, logoff, shutdown: Tick saves and quits. Windows ends the process when this
    // returns (closing: after about 5 seconds anyway), so it waits for the save.
    static bool OnCtrl(int type)
    {
        ctrlStop = true;
        var until = DateTime.UtcNow.AddSeconds(4.5);
        while (!saved && DateTime.UtcNow < until) Thread.Sleep(50);
        return true;
    }
#else
    static void OpenConsole(out Func<string> readLine, out Stream output) => throw new PlatformNotSupportedException("console commands need the Windows player");
#endif
}
