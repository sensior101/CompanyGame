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
    /// registers a generated username/password silently and sets the nickname to <paramref name="name"/>.
    /// If the server forgot the saved account (database reset) the nickname is registered again.
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
            if (!IsValidName(name)) return "invalid_name";
            string error;
            (entry, error) = await Register(data, url);
            if (error != null) return error;
        }
        if (data.Profile == null || !data.Profile.IsComplete)
        {
            string nick = string.IsNullOrEmpty(name) ? entry.nickname : name;
            string sex = gender == "female" || gender == "male" ? gender : (string.IsNullOrEmpty(entry.gender) ? "male" : entry.gender);
            if (!IsValidName(nick)) return "invalid_name";
            var saved = await data.SaveProfile(nick, sex);
            if (!saved.Ok) return saved.Error; // the account stays saved (nickname empty), so the next try only asks for a name again
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

    /// <summary>Changes the nickname on the server (needs the saved account) and in the local record.</summary>
    public static async Awaitable<string> Rename(string newName)
    {
        if (!IsValidName(newName)) return "invalid_name";
        var entry = Account;
        if (entry == null) return "no_account";
        var data = Data;
        data.BaseUrl = entry.url;
        var login = await data.Login(entry.username, entry.password);
        if (!login.Ok) return login.Error;
        var saved = await data.SaveProfile(newName, string.IsNullOrEmpty(data.Profile?.gender) ? (entry.gender ?? "male") : data.Profile.gender);
        if (!saved.Ok) return saved.Error;
        entry.nickname = data.Profile.nickname;
        LocalAccount.Save(entry);
        if (GameSession.HasAccount) GameSession.SetAccount(GameSession.LocalPlayerId, entry.nickname);
        return null;
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
            case "no_account": return "저장된 계정이 없습니다. 방 만들기나 참가하기에서 먼저 이름을 정해 주세요.";
            case "world_backup_failed": return "기존 월드를 백업하지 못해 새 월드를 만들지 않았습니다. 디스크 공간과 권한을 확인해 주세요.";
            case "world_load_failed": return "저장된 월드를 불러오지 못했습니다. 월드 폴더의 world.prev.json 백업을 확인해 주세요.";
            case "load_failed": return "맵을 불러오지 못했습니다.";
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
