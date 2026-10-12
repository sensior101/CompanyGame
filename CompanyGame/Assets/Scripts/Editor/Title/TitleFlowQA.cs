using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Play-mode QA of the title flow against a real running test server (default http://localhost:3000, env QA_SERVER).
/// It clicks the real TitleScreen buttons. Uses profile slots 3 and 4 only, so slot 1 saves stay untouched.
///   -executeMethod TitleFlowQA.Run          needs the server; sign-up, host new world (room code card), existing world, join by typed code
///                                           (lowercase + hyphen, full, unknown code) and from the list, 4409, return to title
/// Optional env QA_SHOTS = folder: also writes T_ingame_code.png (the code card in the real game map).
///   -executeMethod TitleFlowQA.RunOffline   run with the server STOPPED; checks the offline fallback
/// Run WITHOUT -quit (the QA exits the editor itself).
/// </summary>
[InitializeOnLoad]
public static class TitleFlowQA
{
    const string Flag = "TitleFlowQA.mode";
    static int checks;

    static TitleFlowQA() => EditorApplication.playModeStateChanged += OnState;

    public static void Run() => Start("online");
    public static void RunOffline() => Start("offline");

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
        int original = PlayerPrefs.GetInt("Profile.Slot", 1);
        int code = 0;
        ClearKeys();
        try
        {
            ProfileSlot.Set(3);
            Directory.Delete(Path.Combine(Application.persistentDataPath, "profile3"), true);
        }
        catch (DirectoryNotFoundException) { }
        try
        {
            if (mode == "online") await Online(); else await Offline();
            Debug.Log("[TitleFlowQA] RESULT passed, checks=" + checks);
        }
        catch (Exception e)
        {
            Debug.LogError("[TitleFlowQA] RESULT FAILED: " + e.Message + "\n" + e.StackTrace);
            code = 1;
        }
        Cleanup(original);
        EditorApplication.Exit(code);
    }

    static void ClearKeys()
    {
        foreach (int slot in new[] { 3, 4 })
            foreach (string key in new[] { "Session.AccountId", "Session.AccountName", "Session.LocalPlayerId", "auth_token", "net.server_url", "net.accounts" })
                PlayerPrefs.DeleteKey(key + ".p" + slot);
    }

    static void Cleanup(int originalSlot)
    {
        ClearKeys();
        PlayerPrefs.SetInt("Profile.Slot", originalSlot); // the in-memory slot stays 3 so the exit-time save lands in profile3
        PlayerPrefs.Save();
    }

    // ---- helpers ----

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("FAIL: " + what);
        checks++;
        Debug.Log("[TitleFlowQA] ok: " + what);
    }

    static async Task WaitFor(Func<bool> condition, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what + Where());
            await Awaitable.NextFrameAsync();
        }
    }

    // Where the title screen is when something times out: its name and all its texts.
    static string Where() => Screen ? " [screen=" + Screen.Screen + ": " + TextOf(Screen.Content).Replace('\n', '|') + "]" : "";

    static string Server => Environment.GetEnvironmentVariable("QA_SERVER") ?? "http://localhost:3000";
    static TitleScreen Screen => TitleScreen.Current;
    static bool OnTitle => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null;
    static bool InGame => SceneManager.GetActiveScene().path == TitleFlow.GameScene && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading;

    static async Task Click(string label)
    {
        await WaitFor(() => Screen && !Screen.Busy, 15, "screen not busy before clicking " + label);
        Check(Screen.Click(label), "clicked '" + label + "' on screen " + Screen.Screen);
        await Awaitable.NextFrameAsync();
    }

    static async Task TypeName(string name)
    {
        await WaitFor(() => Screen && Screen.Screen == "name", 15, "name screen");
        Screen.FirstInput().text = name;
        await Click("확인");
    }

    static string WorldDir => Path.GetDirectoryName(WorldSave.Path);

    static async Task ReturnToTitle()
    {
        Check(TitleFlow.WhyCannotLeave() == null, "may leave to the title");
        TitleFlow.ReturnToTitle();
        await WaitFor(() => OnTitle, 60, "title scene again");
        await Awaitable.NextFrameAsync();
        Check(!NetSession.Instance.InRoom && !NetSession.Instance.Connected, "left the room and disconnected on returning to the title");
        Check(!PlayerSpawner.Player || !PlayerSpawner.Player.gameObject.activeSelf, "player hidden on the title");
    }

    // ---- online ----

    static async Task Online()
    {
        string server = Server;
        Check(OnTitle, "title scene is the starting scene");
        Check(PlayerSpawner.Player == null, "no player is spawned on the title");
        Check(ProfileSlot.Slot == 3, "QA runs in profile slot 3");
        CheckConfig(server);
        LocalAccount.ServerUrl = server;
        foreach (string label in new[] { "방 만들기", "참가하기", "설정" })
            Check(HasButton(label), "main menu has '" + label + "'");
        await CheckSettings(server);

        // an existing world in slot 3, so "new world" must ask first and back it up
        Check(TitleFlow.PrepareWorld(true) == null && WorldSave.HasLocalWorld, "slot 3 has an old local world");
        string oldId = WorldSave.WorldId;

        // 1) 방 만들기 -> 신규 -> confirm -> name sign-up -> connect -> create room -> map
        await Click("방 만들기");
        await Click("신규 월드");
        Check(Screen.Screen == "confirm", "new world asks before replacing the existing one");
        await Click("새로 시작");
        await TypeName("QA호스트");
        await WaitFor(() => InGame, 90, "host entering the game map");
        var net = NetSession.Instance;
        Check(net.InRoom && net.IsHost, "host: in a room as host");
        Check(PlayerData.Instance.Profile.nickname == "QA호스트", "server nickname = typed name");
        var account = LocalAccount.Find(server);
        Check(account != null && account.username.Length >= 3 && account.password.Length >= 8 && account.nickname == "QA호스트", "local account saved with the server address");
        var backups = Directory.GetFiles(WorldDir, "world.backup-*.json");
        Check(backups.Length == 1 && File.Exists(WorldSave.Path), "old world kept as one backup file, new world.json written");
        Check(JsonUtility.FromJson<WorldSaveData>(File.ReadAllText(backups[0])).worldId == oldId && WorldSave.WorldId != oldId, "backup holds the old world; the new world has a new id");
        string hostWorldId = WorldSave.WorldId;
        Check(net.MyName == "QA호스트" && GameSession.LocalPlayerName == "QA호스트", "session identity = nickname");
        Check(PlayerSpawner.Player.gameObject.activeInHierarchy, "player exists in the map");

        // the room code: id from the server, shown on a card once on arrival, copyable, dismissible
        string code = net.RoomId;
        bool coded = RoomCode.IsCode(code);
        Check(!string.IsNullOrEmpty(code), "host: the room has an id, the room code = " + code);
        if (coded) Check(true, "room id is a 6-character code from the server alphabet");
        else Debug.LogWarning("[TitleFlowQA] NOTE: the server's room id '" + code + "' is not a 6-character code (old server). Typed-code checks are skipped; the room list path is used instead.");
        await WaitFor(() => RoomCodeCard.Instance && RoomCodeCard.Instance.Shown, 10, "room code card on arrival");
        var card = RoomCodeCard.Instance.GetComponentInChildren<Canvas>().transform;
        Check(TextOf(card).Contains(code), "code card shows the room code " + code);
        await ShotIngame();
        GUIUtility.systemCopyBuffer = "";
        Check(TitleUi.ClickButton(card, "코드 복사"), "clicked 코드 복사 on the card");
        Check(GUIUtility.systemCopyBuffer == code, "the clipboard now holds the room code");
        Check(TitleUi.ClickButton(card, "닫기") && !RoomCodeCard.Instance.Shown, "code card closes");
        await Awaitable.NextFrameAsync();
        await Awaitable.NextFrameAsync();
        Check(!RoomCodeCard.Instance.Shown, "code card does not come back after it was dismissed");

        // pause menu opens, shows the code, copies it, closes
        PauseMenu.Instance.Open();
        Check(PauseMenu.Instance.IsOpen, "pause menu opens");
        var pause = PauseMenu.Instance.GetComponentInChildren<Canvas>().transform;
        Check(TextOf(pause).Contains(code), "pause menu shows the room code");
        GUIUtility.systemCopyBuffer = "";
        Check(TitleUi.ClickButton(pause, "코드 복사") && GUIUtility.systemCopyBuffer == code, "pause menu copy button puts the code on the clipboard");
        PauseMenu.Instance.Close();
        Check(!PauseMenu.Instance.IsOpen, "pause menu closes");

        // a second player (raw client) sees the room and joins; the server takes the code in lowercase too
        using (var guest = new QaRawClient(server))
        {
            await guest.Register("QA손님");
            await guest.Connect();
            var rooms = await guest.ListRooms();
            Check(Array.Exists(rooms, r => r.room == "QA호스트의 방" && r.hostName == "QA호스트" && r.rid == code), "second player sees the host's room in the list under its code");
            var joined = await guest.JoinRoom(coded ? code.ToLowerInvariant() : code);
            Check(joined.t == "joined" && joined.host == net.MyId && joined.rid == code, "second player joined the host's room" + (coded ? " with the code in lowercase" : ""));
            await WaitFor(() => net.Peers.Count == 2, 10, "host sees the guest");
            Check(TitleFlow.WhyCannotLeave() == "host_has_peers", "host with a guest present may not go back to the title");
        }
        await WaitFor(() => net.Peers.Count == 1, 10, "guest left");
        await ReturnToTitle();
        Check(!RoomCodeCard.Instance.Shown, "no code card on the title");

        // 2) 방 만들기 -> 기존 -> same account, same world
        await Click("방 만들기");
        Check(HasButton("기존 월드 이어하기"), "existing world is offered");
        await Click("기존 월드 이어하기");
        await WaitFor(() => InGame, 90, "continuing the existing world");
        Check(net.InRoom && net.IsHost, "continue: in a room as host");
        Check(WorldSave.WorldId == hostWorldId, "continue keeps the same world id");
        Check(LocalAccount.Find(server).username == account.username, "continue reused the saved account (no second sign-up)");
        Check(Directory.GetFiles(WorldDir, "world.backup-*.json").Length == 1, "continuing made no extra backup");
        await WaitFor(() => RoomCodeCard.Instance.Shown, 10, "room code card for the continued world");
        Check(TextOf(RoomCodeCard.Instance.GetComponentInChildren<Canvas>().transform).Contains(net.RoomId), "continued world: the card shows its new room code " + net.RoomId);

        // 4409: the same account logs in somewhere else
        using (var twin = new QaRawClient(server))
        {
            await twin.Login(account.username, account.password);
            await twin.Connect();
            await WaitFor(() => !net.Connected, 10, "first connection kicked");
            Check(net.LastError == "replaced" && TitleFlow.Message(net.LastError).Contains("다른 곳"), "4409 -> friendly 'replaced' message");
        }
        foreach (string message in new[] { "network", "rate_limited", "too_many_connections", "invalid_name", "nickname_taken", "room_full", "room_not_found", "invalid_code" })
            Check(!TitleFlow.Message(message).StartsWith("알 수 없는"), "friendly message for " + message);
        await ReturnToTitle();

        // 3) profile slot 4: separate account, joins a room hosted by someone else, by typing its code
        Check(TitleFlow.ChangeSlot(4) && ProfileSlot.Slot == 4, "switched to profile slot 4");
        Check(LocalAccount.ServerUrl == ServerConfig.PublicUrl, "slot 4 starts on the built-in server (the address override belongs to slot 3)");
        LocalAccount.ServerUrl = server; // the override is per profile slot: a second window needs it too (or -server <url>)
        Check(!TitleFlow.HasAccount && LocalAccount.Find(server) == null, "slot 4 has no account of its own yet");
        Check(Path.GetDirectoryName(WorldSave.Path).Contains("profile4"), "slot 4 saves into its own folder");
        using (var rawHost = new QaRawClient(server))
        using (var rawGuest = new QaRawClient(server))
        {
            await rawHost.Register("QA원격방장");
            await rawHost.Connect();
            var created = await rawHost.CreateRoom("QA원격방", 2);
            Check(created.t == "joined" && !string.IsNullOrEmpty(created.rid), "raw host created a 2-player room, code " + created.rid);
            string rid = created.rid;
            bool remoteCoded = RoomCode.IsCode(rid);
            await rawGuest.Register("QA빈자리");
            await rawGuest.Connect();
            Check((await rawGuest.JoinRoom(rid)).t == "joined", "raw guest took the second seat: the room is full");

            await Click("참가하기");
            await TypeName("QA참가자");
            await WaitFor(() => Screen && Screen.Screen == "join", 30, "join-by-code screen");
            Check(HasButton("방 목록 보기") && Screen.FirstInput() != null, "join screen: code field with the room list as the secondary option");
            var slot4 = LocalAccount.Find(server);
            Check(slot4 != null && slot4.username != account.username, "slot 4 got its own generated account");
            // the player's server-side data: personal bank balance is restored from the server on joining
            var personal = JsonUtility.ToJson(new PersonalSaveData { bankBalance = 123456 });
            var put = await PlayerData.Instance.PutState(new PlayerState { map = "", inventory_json = "", personal_json = personal });
            Check(put.Ok, "pre-seeded the slot 4 player's server data");

            if (remoteCoded)
            {
                string typed = rid.Substring(0, 3).ToLowerInvariant() + "-" + rid.Substring(3).ToLowerInvariant(); // "abc-def"
                TypeCode("ab");
                await Click("참가");
                Check(Screen.Screen == "join" && TextOf(Screen.Content).Contains("6자리"), "a too-short code is refused on the join screen with a hint");
                TypeCode(typed);
                await Click("참가");
                await WaitFor(() => Screen && Screen.Screen == "error", 15, "error screen for a full room");
                Check(TextOf(Screen.Content).Contains("가득"), "full room: friendly '가득 찼습니다' message");
                await Click("뒤로");
                Check(Screen.Screen == "join" && NetSession.Instance.Connected, "back on the join screen, still connected");
                TypeCode("zzzzzz");
                await Click("참가");
                await WaitFor(() => Screen && Screen.Screen == "error", 15, "error screen for an unknown code");
                Check(TextOf(Screen.Content).Contains("찾을 수 없") && !HasButton("다시 시도"), "unknown code: friendly 'not found' message without a pointless retry");
                await Click("뒤로");
                Check(Screen.Screen == "join", "back on the join screen after an unknown code");
                rawGuest.Dispose();
                await WaitRoomCount(net, rid, 1);
                TypeCode(typed);
                await Click("참가");
            }
            else
            {
                Debug.LogWarning("[TitleFlowQA] NOTE: remote room id '" + rid + "' is not a 6-character code (old server): joining from the room list only.");
                rawGuest.Dispose();
                await WaitRoomCount(net, rid, 1);
                await Click("방 목록 보기");
                await WaitFor(() => Screen && Screen.Screen == "rooms" && HasButton("참가"), 15, "remote room in the list");
                await Click("참가");
            }
            await WaitFor(() => InGame, 90, "participant entering the game map");
            Check(net.InRoom && !net.IsHost && net.HostId == rawHost.Id && net.RoomId == rid, "participant: in the raw host's room " + rid + ", not host");
            Check(!RoomCodeCard.Instance.Shown, "a participant gets no code card (only the host does)");
            await WaitFor(() => BankManager.Instance.Money == 123456, 30, "bank balance restored from the server");
            Check(true, "bank balance loaded from the server after joining (123456)");
            await Awaitable.NextFrameAsync();
            await ReturnToTitle();

            // the room list still works as the secondary way in
            await Click("참가하기");
            await WaitFor(() => Screen && Screen.Screen == "join", 30, "join screen again (saved account, no name asked)");
            await Click("방 목록 보기");
            await WaitFor(() => Screen && Screen.Screen == "rooms" && Array.Exists(net.Rooms, r => r.rid == rid) && HasButton("참가"), 15, "room list with the remote room");
            Check(Screen.Click("참가"), "clicked the room's 참가 button in the list");
            await WaitFor(() => InGame, 90, "joining from the list");
            Check(net.InRoom && !net.IsHost && net.RoomId == rid, "joined the same room from the list");
        }
        await Awaitable.NextFrameAsync();
        await ReturnToTitle();
    }

    // ---- code / config helpers ----

    static void CheckConfig(string server)
    {
        Check(RoomCode.Normalize("abc-d e2") == "ABCDE2" && RoomCode.Normalize(" k7m 2qx ") == "K7M2QX" && RoomCode.Normalize(null) == "", "room code text loses spaces and hyphens and is upper-cased");
        Check(RoomCode.IsCode("K7M2QX") && !RoomCode.IsCode("K7M2Q0") && !RoomCode.IsCode("K7M2Q") && !RoomCode.IsCode("k7m2qx"), "IsCode follows the server alphabet (no 0 O 1 I, 6 characters)");
        Check(ServerConfig.FromArgs(new[] { "game.exe", "-server", "http://a:1" }) == "http://a:1" && ServerConfig.FromArgs(new[] { "game.exe", "-server" }) == null, "-server <url> argument is read");
        Check(ServerConfig.CommandLineUrl == null, "QA runs without a -server argument");
        string key = "net.server_url.p3";
        PlayerPrefs.DeleteKey(key);
        Check(LocalAccount.ServerUrl == ServerConfig.PublicUrl && !LocalAccount.IsCustomUrl, "without an override the built-in server address is used (" + ServerConfig.PublicUrl + ")");
        LocalAccount.ServerUrl = server;
        Check(LocalAccount.ServerUrl == server && (LocalAccount.IsCustomUrl || server == ServerConfig.PublicUrl), "an address typed under 고급 overrides the built-in one (same as built-in counts as default)");
        LocalAccount.ServerUrl = "";
        Check(!PlayerPrefs.HasKey(key) && LocalAccount.ServerUrl == ServerConfig.PublicUrl, "an empty address clears the override");
    }

    static async Task CheckSettings(string server)
    {
        Screen.Open("settings");
        await Awaitable.NextFrameAsync();
        Check(Screen.Screen == "settings" && HasButton("고급") && !TextOf(Screen.Content).Contains("서버 주소"), "settings: the server address is hidden under a collapsed 고급 section");
        Check(Screen.Click("고급"), "clicked 고급");
        await Awaitable.NextFrameAsync();
        Check(TextOf(Screen.Content).Contains("서버 주소") && HasButton("로컬 테스트 서버") && HasButton("기본 서버로"), "고급 opens the server address with the local-server shortcut");
        Check(Screen.Click("로컬 테스트 서버") && Screen.Content.GetComponentsInChildren<TMPro.TMP_InputField>().Length > 0, "local test server shortcut fills the field");
        Check(Screen.Click("뒤로") && Screen.Screen == "main", "back to the main menu");
    }

    static void TypeCode(string typed)
    {
        var input = Screen.FirstInput();
        input.text = typed;
        string want = RoomCode.Normalize(typed);
        Check(input.text == want, "typing '" + typed + "' leaves '" + want + "' in the code field");
    }

    static async Task WaitRoomCount(NetSession net, string rid, int count)
    {
        for (int i = 0; i < 25; i++)
        {
            net.RefreshRooms();
            await Task.Delay(400);
            if (Array.Exists(net.Rooms, r => r.rid == rid && r.count == count)) return;
        }
        throw new Exception("TIMEOUT waiting for room " + rid + " to have " + count + " player(s)");
    }

    static string TextOf(Transform root)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in root.GetComponentsInChildren<TMPro.TMP_Text>()) sb.Append(t.text).Append('\n');
        return sb.ToString();
    }

    static async Task ShotIngame()
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (string.IsNullOrEmpty(dir) || !Camera.main) return;
        try
        {
            Directory.CreateDirectory(dir);
            RoomCodeCard.Instance.UseCamera(Camera.main);
            await TitleShots.Snap(Camera.main, Path.Combine(dir, "T_ingame_code.png"));
        }
        catch (Exception e) { Debug.LogWarning("[TitleFlowQA] in-game screenshot skipped: " + e.Message); }
    }

    static bool HasButton(string text)
    {
        if (!Screen) return false;
        foreach (var b in Screen.Content.GetComponentsInChildren<UnityEngine.UI.Button>())
        {
            var label = b.GetComponentInChildren<TMPro.TMP_Text>();
            if (label && label.text.Contains(text)) return true;
        }
        return false;
    }

    // ---- offline (server must be stopped) ----

    static async Task Offline()
    {
        LocalAccount.ServerUrl = Server;
        Check(OnTitle, "on the title");
        await Click("방 만들기");
        await Click("신규 월드");
        await TypeName("QA혼자");
        await WaitFor(() => Screen && Screen.Screen == "error", 30, "error screen for the dead server");
        Check(HasButton("오프라인으로 시작") && HasButton("다시 시도"), "error screen offers retry and the offline start");
        await Click("오프라인으로 시작");
        await WaitFor(() => InGame, 90, "offline start entering the game map");
        Check(!NetSession.Instance.InRoom && !NetSession.Instance.Connected, "offline: no room, no connection");
        Check(GameSession.IsAuthority, "offline: this PC owns the simulation");
        Check(GameSession.LocalPlayerName == "QA혼자", "offline player name = typed name");
        Check(File.Exists(WorldSave.Path), "offline new world written to disk");
        await ReturnToTitle();
        await Click("방 만들기");
        Check(HasButton("기존 월드 이어하기"), "existing offline world is offered afterwards");
    }
}
