using System;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// The title screen's actions without any UI: sign up by name, connect, create or join a room, prepare the
/// local world and enter the game map. Every step returns null on success or an error code, and
/// <see cref="Message"/> turns a code into Korean text. The UI and the headless TitleFlowQA both drive this class.
/// </summary>
public static class TitleFlow
{
    public const string TitleScene = "Assets/Scenes/Title.unity";
    public const string GameScene = "Assets/Scenes/daldongnaemap.unity";
    public const float StepTimeout = 8f;

    static readonly Regex NamePattern = new Regex("^[가-힣A-Za-z0-9_]{2,12}$");

    public static bool IsValidName(string name) => name != null && NamePattern.IsMatch(name);

    public static string ServerUrl => LocalAccount.ServerUrl;

    /// <summary>The saved account for the current server and slot (null if none yet).</summary>
    public static LocalAccount.Entry Account
    {
        get
        {
            if (!NetMode.IsSteam) return LocalAccount.Find(LocalAccount.ServerUrl);
            SteamBackend.TryInit(out _); // the Steam account is the identity: its persona name is the nickname
            return new LocalAccount.Entry { url = "steam", nickname = SteamBackend.PersonaName };
        }
    }

    /// <summary>True when no sign-up question is needed: always in Steam mode, else when this slot already has a named account for the current server.</summary>
    public static bool HasAccount { get { if (NetMode.IsSteam) return true; var a = Account; return a != null && a.HasName; } }

    static PlayerData Data => PlayerData.Instance ? PlayerData.Instance : new GameObject("PlayerData").AddComponent<PlayerData>();

    // ---- account ----

    /// <summary>
    /// Makes sure this PC is logged in to the current server. With a saved account it just logs in; otherwise it
    /// registers a generated username/password silently. The account nickname is <paramref name="name"/> when given,
    /// else a generated one: players never see it, they make a character per room's world (<see cref="LoadCharacter"/>).
    /// If the server forgot the saved account (database reset) it is registered again.
    /// </summary>
    public static async Awaitable<string> SignIn(string name, string gender)
    {
        if (NetMode.IsSteam) { SteamBackend.TryInit(out string steamError); SteamInviteRunner.Ensure(); return steamError; } // no name sign-up: Steam is the login
        string url = LocalAccount.ServerUrl;
        var data = Data;
        data.BaseUrl = url;
        var entry = LocalAccount.Find(url);
        if (entry != null)
        {
            var login = await data.Login(entry.username, entry.password);
            if (login.Error == "invalid_credentials")
            {
                name = string.IsNullOrEmpty(name) ? entry.nickname : name;
                gender = string.IsNullOrEmpty(gender) ? entry.gender : gender;
                LocalAccount.Remove(url);
                entry = null;
            }
            else if (!login.Ok) return login.Error;
        }
        if (entry == null)
        {
            if (!string.IsNullOrEmpty(name) && !IsValidName(name)) return "invalid_name";
            string error;
            (entry, error) = await Register(data, url);
            if (error != null) return error;
        }
        if (data.Profile == null || !data.Profile.IsComplete)
        {
            string nick = string.IsNullOrEmpty(name) ? entry.nickname : name;
            string sex = gender == "female" || gender == "male" ? gender : (string.IsNullOrEmpty(entry.gender) ? "male" : entry.gender);
            bool generated = !IsValidName(nick);
            var saved = default(ApiResult);
            for (int i = 0; i < 3; i++) // a generated nickname can collide: roll another
            {
                saved = await data.SaveProfile(generated ? AutoNickname() : nick, sex);
                if (saved.Ok || !generated || saved.Error != "nickname_taken") break;
            }
            if (!saved.Ok) return saved.Error; // the account stays saved (nickname empty), so the next try sets it again
        }
        entry.nickname = data.Profile.nickname; entry.gender = data.Profile.gender;
        LocalAccount.Save(entry);
        return null;
    }

    static async Awaitable<(LocalAccount.Entry, string)> Register(PlayerData data, string url)
    {
        for (int i = 0; i < 3; i++) // a random username collision is practically impossible, but retry anyway
        {
            var entry = new LocalAccount.Entry { url = url, username = LocalAccount.NewUsername(), password = LocalAccount.NewPassword() };
            var result = await data.Register(entry.username, entry.password);
            if (result.Ok) { LocalAccount.Save(entry); return (entry, null); }
            if (result.Error != "username_taken") return (null, result.Error);
        }
        return (null, "username_taken");
    }

    // Server nickname rule: 2..12 of [가-힣A-Za-z0-9_]. "p" + 10 hex = 11.
    static string AutoNickname() => "p" + Guid.NewGuid().ToString("N").Substring(0, 10);

    // ---- character (one per world) ----

    /// <summary>
    /// Loads this account's character of the world in memory (the room's world once joined, the own world before
    /// hosting). When there is one it becomes the session character (<see cref="GameSession.HasCharacter"/>);
    /// otherwise the caller asks for a name and look and calls <see cref="CreateCharacter"/>. The state is handed to
    /// NetPlayerSave so entering the map does not fetch it again. Returns null or an error code.
    /// </summary>
    public static async Awaitable<string> LoadCharacter()
    {
        var net = NetSession.Instance;
        // A visitor learns the room's world from its snapshot; a room created a moment ago sends it within about a second.
        if (net && net.InRoom && !await Until(() => net.HasRoomSnapshot || !net.InRoom, StepTimeout)) return "timeout";
        string world = WorldSave.EnsureWorldId();
        ApiResult result; PlayerState state;
        if (NetMode.IsSteam) (result, state) = SteamLocalState.Get(net ? net.MyId : null, world);
        else (result, state) = await Data.GetState(world);
        if (!result.Ok || state == null) return Code(result.Error, "network");
        NetPlayerSave.Prefetch(world, state);
        if (!string.IsNullOrEmpty(state.name)) GameSession.SetCharacter(state.name, state.gender);
        return null;
    }

    /// <summary>A new character for this world. NetPlayerSave saves it (fresh inventory and money) once the map is up.</summary>
    public static bool CreateCharacter(string name, string gender)
    {
        if (!IsValidName(name)) return false;
        GameSession.SetCharacter(name, gender);
        return true;
    }

    /// <summary>Forgets the account of this slot on this PC. The server keeps it; world files stay on disk.</summary>
    public static void ResetAccount()
    {
        var net = NetSession.Instance;
        if (net) net.Disconnect();
        if (PlayerData.Instance) PlayerData.Instance.Logout();
        LocalAccount.Clear();
        GameSession.ClearAccount();
    }

    public static async Awaitable<bool> TestServer(string url) => await Data.Ping(LocalAccount.Normalize(url));

    /// <summary>Switches the profile slot: saves the current slot, then loads the other slot's personal and world files.</summary>
    public static bool ChangeSlot(int slot)
    {
        if (ProfileSlot.FromCommandLine || slot == ProfileSlot.Slot) return false;
        var net = NetSession.Instance;
        if (net) net.Disconnect();
        WorldSaveRunner.SaveNow();
        if (!ProfileSlot.Set(slot)) return false;
        PersonalSave.ResetToFresh();
        PersonalSave.LoadFromDisk();
        PrepareForJoin();
        return true;
    }

    // ---- connection ----

    /// <summary>Opens the WebSocket with the current login and waits for the server's hello.</summary>
    public static async Awaitable<string> Connect()
    {
        var net = NetSession.Instance;
        if (NetMode.IsSteam) return await ConnectSteam(net);
        var data = Data;
        if (!net || !data.IsLoggedIn) return "unauthorized";
        net.Disconnect();
        net.ClearError();
        net.Connect(LocalAccount.ServerUrl, data.Token);
        if (!await Until(() => !net.Connecting, 10f)) return "network";
        if (!net.Connected) return Code(net.LastError, "network");
        await Until(() => !string.IsNullOrEmpty(net.MyId) || !net.Connected, 5f);
        return net.Connected && !string.IsNullOrEmpty(net.MyId) ? null : Code(net.LastError, "network");
    }

    static async Awaitable<string> ConnectSteam(NetSession net)
    {
        if (!net) return "steam_init_failed";
        net.Disconnect();
        net.ClearError();
        if (!net.ConnectSteam()) return Code(net.LastError, "steam_init_failed");
        await Until(() => !string.IsNullOrEmpty(net.MyId), 5f);
        return string.IsNullOrEmpty(net.MyId) ? "steam_init_failed" : null;
    }

    /// <summary>
    /// A Steam friend invite (overlay or +connect_lobby): switch to Steam mode, connect, join that lobby and open the game map.
    /// Works from the title screen and from the game while not in a room. Returns null or an error code.
    /// </summary>
    public static async Awaitable<string> JoinInvite(ulong lobbyId)
    {
        NetMode.IsSteam = true;
        var net = NetSession.Instance;
        if (net && net.InRoom) return "already_in_room";
        string error = await SignIn(null, null);
        if (error == null && !(net.IsSteam && net.Connected)) error = await Connect();
        if (error != null) return error;
        bool inGame = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != PlayerSpawner.TitleSceneName;
        if (inGame) WorldSaveRunner.SaveNow();
        PrepareForJoin();
        error = await JoinOnline("#" + lobbyId);
        if (error == null) error = await LoadCharacter();
        // ponytail: an invite can arrive in the game where there is no name screen; a new character takes the Steam name.
        if (error == null && !GameSession.HasCharacter) GameSession.SetCharacter(Account?.nickname ?? "플레이어", "male");
        if (error == null && !EnterGame()) error = "load_failed";
        return error;
    }

    public static async Awaitable<string> HostOnline(string roomName, int max = 4)
    {
        var net = NetSession.Instance;
        net.ClearError();
        net.CreateRoom(roomName, max);
        string error = await WaitInRoom(net);
        if (error == null) RoomCodeCard.RequestOnArrival(); // the room code (net.RoomId) is shown big once the host stands in the map
        return error;
    }

    /// <summary>Joins a room by its id: a code typed on the join screen (already cleaned by <see cref="RoomCode.Normalize"/>) or a room list entry.</summary>
    public static async Awaitable<string> JoinOnline(string roomId)
    {
        var net = NetSession.Instance;
        net.ClearError();
        net.Join(roomId);
        return await WaitInRoom(net);
    }

    static async Awaitable<string> WaitInRoom(NetSession net)
    {
        await Until(() => net.InRoom || !string.IsNullOrEmpty(net.LastError) || !net.Connected, StepTimeout);
        if (net.InRoom) return null;
        return Code(net.LastError, net.Connected ? "timeout" : "disconnected");
    }

    // ---- world ----

    /// <summary>
    /// Puts this PC's own world into memory: a new world (the old file is backed up, never deleted) or the saved one.
    /// Start the room only after this, so the room's first snapshot is the chosen world.
    /// </summary>
    public static string PrepareWorld(bool newWorld)
    {
        if (newWorld) return WorldSave.StartNewWorld(out _) ? null : "world_backup_failed";
        WorldSave.RestoreBaseline();
        return WorldSave.LoadFromDisk() ? null : "world_load_failed";
    }

    /// <summary>Visitors keep their own world untouched; this only makes memory equal to the file (or a fresh world).</summary>
    public static void PrepareForJoin()
    {
        WorldSave.RestoreBaseline();
        if (WorldSave.HasLocalWorld) WorldSave.LoadFromDisk();
    }

    /// <summary>Single player without a server. <paramref name="name"/> becomes the offline player name when there is no account.</summary>
    public static string StartOffline(bool newWorld, string name, bool worldAlreadyPrepared)
    {
        var net = NetSession.Instance;
        if (net) net.Disconnect();
        if (IsValidName(name)) GameSession.LocalPlayerName = name;
        string error = worldAlreadyPrepared ? null : PrepareWorld(newWorld);
        return error ?? (EnterGame() ? null : "load_failed");
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
        return await JoinServer("127.0.0.1:" + running.port, ServerSettings.Load().password, false);
    }

    /// <summary>Joins a server by "host" or "host:port" (default port 7777). Null or an error code.</summary>
    public static async Awaitable<string> JoinServer(string address, string password, bool remember = true)
    {
        if (!TryParseAddress(address, out string host, out int port)) return "bad_address";
        var client = SyncClient.Instance;
        client.Disconnect();
        client.Connect(host, port, password ?? "");
        string error = await WaitSync();
        if (error != null) return error;
        if (remember) ServerSettings.Remember(address.Trim());
        return null;
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

    static async Awaitable<string> WaitSync()
    {
        var client = SyncClient.Instance;
        if (!await Until(() => client.Connected && client.CharacterKnown || !string.IsNullOrEmpty(client.LastError), StepTimeout)) { client.Disconnect(); return "server_unreachable"; }
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
        var net = NetSession.Instance;
        if (net) net.Disconnect();
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

    /// <summary>Why the player cannot go back to the title right now (null = they can).</summary>
    public static string WhyCannotLeave()
    {
        var net = NetSession.Instance;
        return net && net.InRoom && net.IsHost && net.Peers.Count > 1 ? "host_has_peers" : null;
    }

    /// <summary>Leaves the room (if any), saves and loads the title scene. Check <see cref="WhyCannotLeave"/> first.</summary>
    public static async void ReturnToTitle()
    {
        if (WhyCannotLeave() != null) return;
        var net = NetSession.Instance;
        bool wasInRoom = net && net.InRoom;
        if (wasInRoom) net.Leave();
        await Awaitable.NextFrameAsync(); // let the per-player save of NetPlayerSave see the room end while the map still exists
        await Awaitable.NextFrameAsync();
        WorldSaveRunner.SaveNow();
        if (net) net.Disconnect();
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
            case "network": case "connect_failed": case "timeout":
                return ServerConfig.IsPlaceholder && !LocalAccount.IsCustomUrl
                    ? "게임에 서버 주소가 아직 들어 있지 않습니다(개발 빌드). 설정 > 고급에서 서버 주소를 입력해 주세요."
                    : "서버에 연결할 수 없습니다. 인터넷 연결을 확인하고 잠시 후 다시 시도해 주세요. 서버가 점검 중일 수 있습니다.";
            case "daily_budget": return ServerBudget.Message;
            case "steam_not_compiled": return "이 빌드에는 Steam 기능이 들어 있지 않습니다. Cloudflare 서버 모드나 오프라인으로 플레이해 주세요.";
            case "steam_dll_missing": return "Steam 연동 파일(steam_api64.dll)을 찾을 수 없습니다. 압축을 푼 폴더가 온전한지 확인해 주세요.";
            case "steam_not_running": case "steam_init_failed":
                return "Steam에 연결할 수 없습니다. Steam 프로그램을 켜고 로그인한 뒤 다시 시도해 주세요. Steam 없이도 Cloudflare 서버 모드나 오프라인으로 할 수 있어요.";
            case "steam_lobby_failed": return "Steam 방을 만들거나 들어가지 못했습니다. 잠시 후 다시 시도해 주세요.";
            case "version_mismatch": return "방장과 게임 버전이 달라 들어갈 수 없습니다. 같은 빌드를 받아 실행해 주세요.";
            case "bad_url": return "서버 주소 형식이 올바르지 않습니다. 설정 > 고급에서 주소를 고쳐 주세요.";
            case "replaced": return "같은 계정이 다른 곳에서 접속해 이 연결이 끊겼습니다. 한 PC에서 게임을 두 개 켜려면 설정에서 서로 다른 프로필 슬롯을 쓰세요.";
            case "too_many_connections": return "이 주소에서 동시에 접속한 수가 너무 많습니다. 다른 창을 닫고 잠시 후 다시 시도해 주세요.";
            case "rate_limited": return "요청이 너무 많습니다. 잠시 기다렸다가 다시 시도해 주세요.";
            case "invalid_name": case "invalid_input": return "이름은 한글, 영문, 숫자, _ 로 2~12자여야 합니다.";
            case "nickname_taken": return "이미 사용 중인 이름입니다. 다른 이름을 입력해 주세요.";
            case "username_taken": return "계정을 만들지 못했습니다. 잠시 후 다시 시도해 주세요.";
            case "invalid_credentials": return "저장된 계정으로 로그인하지 못했습니다. 설정에서 계정 초기화 후 다시 시도해 주세요.";
            case "unauthorized": return "로그인이 만료되었거나 연결이 거부되었습니다. 다시 시도해 주세요.";
            case "disconnected": return "서버와의 연결이 끊겼습니다.";
            case "room_full": return "방이 가득 찼습니다. 자리가 나면 다시 시도해 주세요.";
            case "room_not_found": return "그 코드의 방을 찾을 수 없습니다. 코드를 다시 확인해 주세요. 방이 이미 닫혔을 수도 있습니다.";
            case "invalid_code": return "방 코드는 영문·숫자 6자리입니다.";
            case "too_many_rooms": return "서버에 방이 너무 많습니다. 잠시 후 다시 시도해 주세요.";
            case "bad_request": return "방 이름이 올바르지 않습니다.";
            case "already_in_room": return "이미 방에 들어가 있습니다.";
            case "world_backup_failed": return "기존 월드를 백업하지 못해 새 월드를 만들지 않았습니다. 디스크 공간과 권한을 확인해 주세요.";
            case "world_load_failed": return "저장된 월드를 불러오지 못했습니다. 월드 폴더의 world.prev.json 백업을 확인해 주세요.";
            case "load_failed": return "맵을 불러오지 못했습니다.";
            case "Version": return "서버와 게임 버전이 달라 들어갈 수 없습니다. 같은 빌드를 받아 실행해 주세요.";
            case "Full": return "서버가 가득 찼습니다. 자리가 나면 다시 시도해 주세요.";
            case "Password": return "비밀번호가 틀렸습니다. 서버를 연 사람에게 비밀번호를 다시 확인해 주세요.";
            case "AlreadyConnected": return "이 캐릭터가 이미 서버에 접속해 있습니다. 다른 창이나 기기를 닫고 다시 시도해 주세요. (한 PC에서 게임을 두 개 켜려면 설정에서 서로 다른 프로필 슬롯을 쓰세요.)";
            case "Malformed": return "서버가 연결을 거부했습니다(잘못된 메시지). 게임을 다시 시작한 뒤 시도해 주세요.";
            case "Rate": return "메시지를 너무 많이 보내 서버가 연결을 끊었습니다. 잠시 후 다시 시도해 주세요.";
            case "server_start_failed": return "서버를 시작하지 못했습니다. 다른 프로그램이 같은 포트를 쓰고 있을 수 있습니다. 서버 열기 > 고급에서 포트를 바꿔 보세요.";
            case "bad_address": return "주소 형식이 올바르지 않습니다. 예: 192.168.0.5 또는 192.168.0.5:7777";
            case "server_unreachable": return "서버에 연결할 수 없습니다. 주소와 포트가 맞는지, 서버가 켜져 있는지 확인해 주세요. 다른 PC의 서버라면 그쪽 방화벽과 포트 열기도 확인해 주세요.";
            case "world_in_use": return "이 월드는 지금 내 서버(서버 열기)에서 돌고 있습니다. 서버를 끈 뒤 혼자 하기로 열거나, 서버에 접속해 주세요.";
            case "world_unreadable": return "월드 파일을 읽지 못했습니다(world.dat와 world.prev.dat 모두). 파일은 그대로 두었습니다. 다른 월드를 고르거나 새 월드를 만들어 주세요.";
            case "character_name_taken": return "이 월드에 같은 이름의 캐릭터가 이미 있습니다(대소문자만 달라도 같은 이름). 다른 이름을 입력해 주세요.";
            case "character_exists": return "이 서버에는 이미 내 캐릭터가 있습니다. 다시 접속해 주세요.";
            case "host_has_peers": return "다른 플레이어가 있는 방의 방장은 나갈 수 없습니다. 먼저 방장 넘기기를 해 주세요.";
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
