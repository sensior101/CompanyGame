using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// The title screen's actions without any UI: 혼자 하기 / 서버 열기 / 참가하기 on the sync layer, the character of the
/// server's world, and entering the map. The account is the EOS PUID (EosAccount, 12.13): every step that joins a server
/// first waits for the EOS login to end, so the account never changes under a connection. Joining by the public list or a
/// server code goes over EOS P2P. Every step returns null on success or an error code, and
/// <see cref="Message"/> turns a code into Korean text. The UI and the headless TitleFlowQA both drive this class.
/// </summary>
public static class TitleFlow
{
    public const string TitleScene = "Assets/Scenes/Title.unity";
    public const string GameScene = "Assets/Scenes/daldongnaemap.unity";
    public const float StepTimeout = 8f, EosStepTimeout = 25f; // EOS P2P may first try NAT punching, then the relay

    static readonly Regex NamePattern = new Regex("^[가-힣A-Za-z0-9_]{2,12}$");

    public static bool IsValidName(string name) => name != null && NamePattern.IsMatch(name);

    /// <summary>The code of the server joined last by list or code, or of my own server process ("" when unknown). The pause menu shows it.</summary>
    public static string JoinedCode { get; private set; } = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { JoinedCode = ""; }

    /// <summary>Switches the profile slot: saves the current slot, then loads the other slot's personal and world files.</summary>
    public static bool ChangeSlot(int slot)
    {
        if (ProfileSlot.FromCommandLine || slot == ProfileSlot.Slot) return false;
        WorldSaveRunner.SaveNow();
        if (!ProfileSlot.Set(slot)) return false;
        PersonalSave.ResetToFresh();
        PersonalSave.LoadFromDisk();
        WorldSave.RestoreBaseline(); // this slot's own world (or a fresh one)
        WorldSave.LoadFromDisk();
        return true;
    }

    public static bool EnterGame() => SceneLoadManager.TryLoadMap(GameScene, SceneLoadManager.DefaultSpawnId);

    // ---- 혼자 하기 / 서버 열기 / 참가하기 (sync layer, Docs/Multiplayer_Redesign.md 12.3, 12.5) ----
    // Each step ends connected with the server's answer about my character (SyncClient.CharacterKnown). Without one the
    // title asks for a name (CreateServerCharacter), then EnterServerWorld. Since 4c the shared world (companies, land,
    // prices) is the server's too (12.9).

    /// <summary>
    /// 혼자 하기 (9장-4): the in-app server on 127.0.0.1 only, private, 1 player, on <paramref name="world"/> of this slot
    /// ("" = a new world). Null or an error code.
    /// </summary>
    public static async Awaitable<string> OpenSolo(string world)
    {
        await EosAccount.WhenSettled();
        JoinedCode = "";
        var running = LocalServerProcess.CanStart ? LocalServerProcess.Running() : null;
        if (running != null && !string.IsNullOrEmpty(world) && running.world == world) return "world_in_use"; // two writers of one world file
        SyncClient.Instance.Disconnect();
        string error = StartInApp(ServerSettings.Solo(world), true) ?? await WaitSync();
        if (error != null) StopInAppServer();
        return error;
    }

    static string StartInApp(ServerSettings settings, bool localOnly)
    {
        try { SyncHost.StartInApp(settings, localOnly); return null; }
        catch (System.IO.InvalidDataException e) { Debug.LogWarning("[TitleFlow] world unreadable: " + e.Message); return "world_unreadable"; }
        catch (Exception e) { Debug.LogWarning("[TitleFlow] in-app server failed: " + e.Message); return "server_start_failed"; }
    }

    /// <summary>
    /// Opens my server and joins it. PC: the separate server process (kept if already running) at 127.0.0.1.
    /// Phone and editor: the in-app server. A new world gets its id here so the next start opens the same one. Null or an error code.
    /// </summary>
    public static async Awaitable<string> OpenServer(ServerSettings settings)
    {
        await EosAccount.WhenSettled();
        JoinedCode = "";
        settings.Clamp();
        if (string.IsNullOrEmpty(settings.world)) settings.world = WorldStore.NewId();
        if (ServerSettings.IsMobile || !LocalServerProcess.CanStart)
        {
            settings.Save();
            SyncClient.Instance.Disconnect();
            string error = StartInApp(settings, false) ?? await WaitSync();
            if (error != null) StopInAppServer();
            return error;
        }
        var running = LocalServerProcess.Running();
        if (running == null)
        {
            using var process = LocalServerProcess.Start(settings);
            if (process == null) return "server_start_failed";
            // The process writes running.json once it listens (a player boot takes a few seconds).
            if (!await Until(() => (running = LocalServerProcess.Running()) != null || process.HasExited, 60f) || running == null) return "server_start_failed";
        }
        return await JoinMine(running);
    }

    /// <summary>My PC server process (127.0.0.1, my PUID needs no token there): "내 서버 실행 중 > 접속" and 서버 열기.</summary>
    public static async Awaitable<string> JoinMine(LocalServerProcess.RunningInfo running)
    {
        string error = await JoinServer("127.0.0.1:" + running.port, ServerSettings.Load().password, false);
        if (error == null) JoinedCode = LocalServerProcess.Running()?.code ?? running.code ?? "";
        return error;
    }

    /// <summary>Joins a server by "host" or "host:port" (default port 7777). Null or an error code.</summary>
    public static async Awaitable<string> JoinServer(string address, string password, bool remember = true)
    {
        if (!TryParseAddress(address, out string host, out int port)) return "bad_address";
        await EosAccount.WhenSettled();
        JoinedCode = "";
        var client = SyncClient.Instance;
        client.Disconnect();
        client.Connect(host, port, password ?? "");
        string error = await WaitSync();
        if (error != null) return error;
        if (remember) ServerSettings.Remember(address.Trim());
        return null;
    }

    // ---- public list and server codes (EOS Sessions, 12.13) ----

    /// <summary>The public servers (stale ones left out), or an error code (eos_offline, search_failed).</summary>
    public static async Awaitable<(List<EosSession.Entry>, string)> ListServers()
    {
        if (!await EosAccount.WhenSettled()) return (null, "eos_offline");
        var (failed, found) = await EosSession.Search(null);
        if (failed != null) Debug.LogWarning("[TitleFlow] server list search failed: " + failed);
        return failed == null ? (found, null) : (null, "search_failed");
    }

    /// <summary>The server with code <paramref name="typed"/>, or an error code (bad_code, eos_offline, code_not_found, search_failed).</summary>
    public static async Awaitable<(EosSession.Entry, string)> FindCode(string typed)
    {
        string code = ServerSettings.NormalizeCode(typed);
        if (code == null) return (null, "bad_code");
        if (!await EosAccount.WhenSettled()) return (null, "eos_offline");
        var (failed, found) = await EosSession.Search(code);
        if (failed != null) { Debug.LogWarning("[TitleFlow] code search failed: " + failed); return (null, "search_failed"); }
        return found.Count > 0 ? (found[0], null) : (null, "code_not_found");
    }

    /// <summary>
    /// Joins a server from the list or a code over EOS P2P. My own server (same PUID: P2P to myself does not work, 4.2-1) is
    /// joined at 127.0.0.1 when it is my PC server process. Null or an error code.
    /// </summary>
    public static async Awaitable<string> JoinListed(EosSession.Entry entry, string password)
    {
        if (!await EosAccount.WhenSettled()) return "eos_offline";
        if (entry.puid == EosAccount.LocalPuid)
        {
            var running = LocalServerProcess.CanStart ? LocalServerProcess.Running() : null;
            return running != null && running.code == entry.code ? await JoinMine(running) : "own_server";
        }
        JoinedCode = "";
        var client = SyncClient.Instance;
        client.Disconnect();
        client.ConnectEos(entry.puid, password ?? "");
        string error = await WaitSync(EosStepTimeout);
        if (error == null) JoinedCode = entry.code;
        return error;
    }

    public static bool TryParseAddress(string address, out string host, out int port)
    {
        host = (address ?? "").Trim(); port = ServerSettings.DefaultPort;
        int colon = host.LastIndexOf(':');
        if (colon >= 0 && host.IndexOf(':') == colon) // one colon = host:port (more would be a bare IPv6 address)
        {
            if (!int.TryParse(host.Substring(colon + 1), out port) || port < 1 || port > 65535) return false;
            host = host.Substring(0, colon);
        }
        return host.Length > 0 && host.Length <= 253 && host.IndexOf(' ') < 0;
    }

    static async Awaitable<string> WaitSync(float timeout = StepTimeout)
    {
        var client = SyncClient.Instance;
        if (!await Until(() => client.Connected && client.CharacterKnown || !string.IsNullOrEmpty(client.LastError), timeout)) { client.Disconnect(); return "server_unreachable"; }
        if (client.Connected) return null;
        return client.LastError == "connect_failed" || client.LastError == "disconnected" ? "server_unreachable" : client.LastError;
    }

    /// <summary>Asks the server for my character in its world. Null, or character_name_taken / invalid_name / an error code.</summary>
    public static async Awaitable<string> CreateServerCharacter(string name, string gender)
    {
        if (!IsValidName(name)) return "invalid_name";
        var client = SyncClient.Instance;
        client.CreateCharacter(name, gender);
        if (!await Until(() => client.LastCharacterResult != null || !client.Connected, StepTimeout)) { LeaveServer(); return "server_unreachable"; }
        if (!client.Connected) return Code(client.LastError, "disconnected");
        switch (client.LastCharacterResult)
        {
            case CharacterResult.Ok: return null;
            case CharacterResult.NameTaken: return "character_name_taken";
            case CharacterResult.Invalid: return "invalid_name";
            default: return "character_exists";
        }
    }

    /// <summary>
    /// Enters as the server's character at its saved map (CharacterSync puts it back where it was), else the game map.
    /// The shared world is the server's (12.9: ServerWorld in an in-app server, the world sections from another one), so this
    /// PC's world is not loaded. Leaves the server on an error.
    /// </summary>
    public static string EnterServerWorld()
    {
        var client = SyncClient.Instance;
        GameSession.SetCharacter(client.CharacterName, client.CharacterGender);
        string error = null;
        string map = CharacterSync.SavedMap(client.CharacterContent);
        if (!(map != null && SceneLoadManager.TryLoadMap(map, SceneLoadManager.DefaultSpawnId)) && !EnterGame()) error = "load_failed";
        if (error != null) LeaveServer();
        return error;
    }

    /// <summary>Back out of a server before entering it (the name screen's 뒤로): disconnect, and an in-app server stops.</summary>
    public static void LeaveServer()
    {
        SyncClient.Instance.Disconnect();
        StopInAppServer();
        GameSession.ClearCharacter();
    }

    /// <summary>The in-app server dies with its owner leaving (phone: 서버 종료). A PC server process is never stopped here.</summary>
    static void StopInAppServer()
    {
        if (SyncHost.Instance && SyncHost.Instance.InApp) SyncHost.Instance.Stop();
    }

    // ---- leaving the game ----

    /// <summary>Saves, leaves the server (if any) and loads the title scene.</summary>
    public static void ReturnToTitle()
    {
        WorldSaveRunner.SaveNow();
        CharacterSync.SaveNow(); // before the disconnect: the socket sends it, then closes
        if (SyncClient.Instance) SyncClient.Instance.Disconnect();
        StopInAppServer();
        SceneLoadManager.TryLoadMap(TitleScene, SceneLoadManager.DefaultSpawnId);
    }

    // ---- text ----

    static string Code(string code, string fallback) => string.IsNullOrEmpty(code) ? fallback : code;

    public static string Message(string code)
    {
        switch (code)
        {
            case "invalid_name": return "이름은 한글, 영문, 숫자, _ 로 2~12자여야 합니다.";
            case "disconnected": return "서버와의 연결이 끊겼습니다.";
            case "eos_offline": return "EOS에 연결되지 않았습니다. 공개 서버 목록과 서버 코드는 EOS가 필요합니다. 인터넷 연결을 확인하고 게임을 다시 시작하거나, 아래의 주소로 접속해 주세요.";
            case "search_failed": return "서버 목록을 받지 못했습니다. 잠시 후 다시 시도해 주세요.";
            case "bad_code": return "서버 코드는 영문 대문자와 숫자 6자입니다. (0, O, 1, I는 쓰지 않습니다)";
            case "code_not_found": return "이 코드의 서버를 찾지 못했습니다. 코드가 맞는지, 서버가 켜져 있는지 확인해 주세요.";
            case "own_server": return "이 PC의 내 서버입니다. 서버 열기에서 접속해 주세요.";
            case "Identity": return "서버가 이 계정을 확인하지 못해 들어갈 수 없습니다. 게임을 다시 시작해 EOS에 다시 연결한 뒤 시도해 주세요.";
            case "load_failed": return "맵을 불러오지 못했습니다.";
            case "Version": return "서버와 게임 버전이 달라 들어갈 수 없습니다. 같은 빌드를 받아 실행해 주세요.";
            case "Full": return "서버가 가득 찼습니다. 자리가 나면 다시 시도해 주세요.";
            case "Password": return "비밀번호가 틀렸습니다. 서버를 연 사람에게 비밀번호를 다시 확인해 주세요.";
            case "AlreadyConnected": return "이 계정이 이미 서버에 접속해 있습니다. 다른 창이나 기기를 닫고 다시 시도해 주세요. (한 PC의 게임은 모두 같은 EOS 계정입니다. 두 창 시험은 서버와 두 창 모두 -noeos 와 서로 다른 프로필 슬롯으로 실행하세요.)";
            case "Banned": return "이 서버에서 차단되어 들어갈 수 없습니다. 서버 관리자에게 문의해 주세요.";
            case "Kicked": return "관리자가 서버에서 내보냈습니다.";
            case "Malformed": return "서버가 연결을 거부했습니다(잘못된 메시지). 게임을 다시 시작한 뒤 시도해 주세요.";
            case "Rate": return "메시지를 너무 많이 보내 서버가 연결을 끊었습니다. 잠시 후 다시 시도해 주세요.";
            case "server_start_failed": return "서버를 시작하지 못했습니다. 다른 프로그램이 같은 포트를 쓰고 있을 수 있습니다. 서버 열기 > 고급에서 포트를 바꿔 보세요.";
            case "bad_address": return "주소 형식이 올바르지 않습니다. 예: 192.168.0.5 또는 192.168.0.5:7777";
            case "server_unreachable": return "서버에 연결할 수 없습니다. 주소와 포트가 맞는지, 서버가 켜져 있는지 확인해 주세요. 다른 PC의 서버라면 그쪽 방화벽과 포트 열기도 확인해 주세요.";
            case "world_in_use": return "이 월드는 지금 내 서버(서버 열기)에서 돌고 있습니다. 서버를 끈 뒤 혼자 하기로 열거나, 서버에 접속해 주세요.";
            case "world_unreadable": return "월드 파일을 읽지 못했습니다(world.dat와 world.prev.dat 모두). 파일은 그대로 두었습니다. 다른 월드를 고르거나 새 월드를 만들어 주세요.";
            case "character_name_taken": return "이 월드에 같은 이름의 캐릭터가 이미 있습니다(대소문자만 달라도 같은 이름). 다른 이름을 입력해 주세요.";
            case "character_exists": return "이 서버에는 이미 내 캐릭터가 있습니다. 다시 접속해 주세요.";
            default: return "알 수 없는 오류입니다: " + code;
        }
    }

    // ---- util ----

    static async Awaitable<bool> Until(Func<bool> condition, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) return false;
            await Awaitable.NextFrameAsync();
        }
        return true;
    }
}
