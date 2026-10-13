using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The title menu (Scenes/Title.unity): 서버 열기 / 참가하기 / 설정 (the sync layer, Docs/Multiplayer_Redesign.md 12.3) and,
/// under 옛 방식(개발용), the old Cloudflare/Steam rooms (방 만들기 / 참가하기 / 연결 방식) until stage 4, plus the small screens
/// they lead to (name sign-up, room code entry, room list, errors, confirmations). Built in code with uGUI; keyboard (arrows/Tab/Enter/Esc) and mouse both work.
/// All real work is in <see cref="TitleFlow"/>.
/// </summary>
public sealed class TitleScreen : MonoBehaviour
{
    const float RoomRefreshSeconds = 3f;
    const int MaxRoomRows = 8;

    public static TitleScreen Current { get; private set; }

    Canvas canvas;
    RectTransform content, logo;
    TMP_Text footer;
    Action back;
    bool busy, worldReady, pendingNew, oldFooter;
    string screen = "";
    RectTransform roomList;
    TMP_Text roomHint;
    RoomInfo[] shownRooms;
    float nextRefresh, nextHide;
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();

    public RectTransform Content => content;
    public string Screen => screen;
    public bool Busy => busy;

    void Awake()
    {
        Current = this;
        canvas = TitleUi.NewCanvas("TitleCanvas", 5000);
        canvas.transform.SetParent(transform, false);
        BuildChrome();
        HideOtherCanvases();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Start() { HideOtherCanvases(); ShowMain(); }

    void OnDestroy()
    {
        foreach (var c in hiddenCanvases) if (c) c.enabled = true;
        if (Current == this) Current = null;
    }

    /// <summary>Draw the menu through a camera instead of as an overlay (used to render screenshots without a window).</summary>
    public void UseCamera(Camera camera)
    {
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
    }

    // Other game UI (phone, chat) lives in overlay canvases that persist across scenes; the title covers them.
    void HideOtherCanvases()
    {
        foreach (var c in FindObjectsByType<Canvas>())
            if (c != canvas && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.enabled)
            { c.enabled = false; hiddenCanvases.Add(c); }
    }

    void BuildChrome()
    {
        var root = (RectTransform)canvas.transform;
        logo = TitleUi.Column(root, "Logo", 1200, 0);
        logo.anchoredPosition = new Vector2(0, -90);
        var title = TitleUi.Label(logo, "달동네", 150, TitleUi.Cream, TextAlignmentOptions.Center, FontStyles.Bold);
        title.outlineWidth = 0.18f; title.outlineColor = new Color32(0x3B, 0x2A, 0x20, 255);
        TitleUi.Label(logo, "작은 동네에서 회사를 키우는 이야기", 34, TitleUi.Amber);

        content = TitleUi.Column(root, "Content", 640, 14);
        content.anchoredPosition = new Vector2(0, -420);

        footer = TitleUi.Label(root, "", 22, TitleUi.Muted, TextAlignmentOptions.BottomLeft);
        var rt = (RectTransform)footer.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0, 0);
        rt.offsetMin = new Vector2(32, 20); rt.offsetMax = new Vector2(-32, 70);
    }

    // Opens a screen: clears the content, shows or hides the logo, moves the content under it.
    void Begin(string name, bool withLogo, float width, Action onBack)
    {
        screen = name; busy = false; back = onBack; roomList = null;
        TitleUi.Clear(content);
        logo.gameObject.SetActive(withLogo);
        content.sizeDelta = new Vector2(width, 0);
        content.anchoredPosition = new Vector2(0, withLogo ? -420 : -60);
        // The new menus (12.3) do not use the old Cloudflare/Steam rooms, so only the old ones show that connection.
        // Shared screens (busy, error, name, settings, confirm) keep the footer of the flow they came from.
        if (name == "main" || name.StartsWith("server")) oldFooter = false;
        else if (name == "legacy" || name == "create" || name == "join" || name == "rooms") oldFooter = true;
        // The server address is only worth showing when it is not the built-in one (or the built-in one is still a placeholder).
        bool steam = NetMode.IsSteam;
        string server = !oldFooter ? "" : steam ? "   ·   Steam 개발 모드" : LocalAccount.IsCustomUrl ? "   ·   서버 " + LocalAccount.ServerUrl : ServerConfig.IsPlaceholder ? "   ·   서버 주소 미설정" : "";
        footer.text = "프로필 슬롯 " + ProfileSlot.Slot + server;
    }

    // A rounded dark panel (title + children) for every sub screen.
    RectTransform Panel(string heading, string sub = null)
    {
        var panel = TitleUi.Column(content, "Panel", 0, 14, TitleUi.Panel, 32, false);
        TitleUi.Label(panel, heading, 46, TitleUi.Cream, TextAlignmentOptions.Center, FontStyles.Bold);
        if (!string.IsNullOrEmpty(sub)) TitleUi.Label(panel, sub, 25, TitleUi.Muted);
        return panel;
    }

    void Focus() { TitleUi.FocusFirst(content); }

    void Update()
    {
        if (GameInput.CancelPressed && back != null && !busy) back();
        // Keep a focus target: a click on empty background would otherwise leave the keyboard with nothing selected.
        if (!busy && EventSystem.current && !EventSystem.current.currentSelectedGameObject) Focus();
        if (screen == "rooms" || screen == "join") UpdateConnected();
        if (Time.unscaledTime >= nextHide) { nextHide = Time.unscaledTime + 1f; HideOtherCanvases(); } // HUDs created after this scene loaded
    }

    // ---- main ----

    public void ShowMain()
    {
        Begin("main", true, 560, null);
        worldReady = false;
        var net = NetSession.Instance;
        if (net && net.Connected) net.Disconnect();
        TitleUi.Button(content, "서버 열기", () => ShowServerOpen(), 76, true, 36);
        TitleUi.Button(content, "참가하기", () => ShowServerJoin(), 76, true, 36);
        TitleUi.Button(content, "설정", () => ShowSettings(), 76, true, 36);
        TitleUi.Button(content, "종료", Quit, 60, false, 28);
        TitleUi.Button(content, "옛 방식(개발용)", ShowLegacy, 44, false, 22);
        Focus();
    }

    /// <summary>The old Cloudflare/Steam room menu, kept for development until stage 4.</summary>
    public void ShowLegacy()
    {
        Begin("legacy", true, 560, ShowMain);
        worldReady = false;
        var net = NetSession.Instance;
        if (net && net.Connected) net.Disconnect();
        TitleUi.Label(content, "옛 방식(개발용)", 26, TitleUi.Muted);
        TitleUi.Button(content, "방 만들기", ShowCreate, 76, true, 36);
        TitleUi.Button(content, "참가하기", StartJoin, 76, true, 36);
        TitleUi.Button(content, "연결 방식: " + NetMode.Label + " (바꾸기)", () => { NetMode.IsSteam = !NetMode.IsSteam; ShowLegacy(); }, 44, false, 22);
        TitleUi.Button(content, "뒤로", ShowMain, 56, false, 26);
        Focus();
    }

    // ---- 서버 열기 ----

    ServerSettings draft;
    bool openAdvanced;

    /// <summary>Settings for my server, or (PC) the running server with 접속 / 끄기.</summary>
    public void ShowServerOpen() => ShowServerOpen(null);

    void ShowServerOpen(ServerSettings settings)
    {
        var running = LocalServerProcess.CanStart ? LocalServerProcess.Running() : null;
        if (running != null) { ShowServerRunning(running); return; }
        draft = (settings ?? ServerSettings.Load()).Clamp();
        Begin("serveropen", false, 820, ShowMain);
        var panel = Panel("서버 열기", ServerSettings.IsMobile
            ? "이 폰이 서버가 됩니다. 앱을 나가면 서버도 꺼집니다."
            : LocalServerProcess.CanStart ? "이 PC에서 서버가 따로 돌아갑니다. 게임을 꺼도 서버는 계속 켜져 있습니다." : "에디터에서는 게임 안에서 서버를 엽니다.");

        TitleUi.Label(panel, "서버 이름", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        var nameInput = TitleUi.Input(panel, "서버 이름 (1~30자)", ServerSettings.NameMax);
        nameInput.text = draft.name;

        var countRow = TitleUi.Row(panel, 56, 12, false);
        var less = TitleUi.Button(countRow, "◀", null, 56, false, 26);
        var countLabel = TitleUi.Label(countRow, "", 28, TitleUi.Cream);
        var more = TitleUi.Button(countRow, "▶", null, 56, false, 26);
        TitleUi.Width(less, 90); TitleUi.Width(more, 90); TitleUi.Width(countLabel, 0, 1);
        Action showCount = () => countLabel.text = "인원 " + draft.maxPlayers + "명 <size=22><color=#C9C3D9>(" + ServerSettings.MinPlayers + "~" + ServerSettings.PlayerLimit + ")</color></size>";
        less.onClick.AddListener(() => { draft.maxPlayers = Mathf.Max(ServerSettings.MinPlayers, draft.maxPlayers - 1); showCount(); });
        more.onClick.AddListener(() => { draft.maxPlayers = Mathf.Min(ServerSettings.PlayerLimit, draft.maxPlayers + 1); showCount(); });
        showCount();

        var publicRow = TitleUi.Row(panel, 56);
        var priv = TitleUi.Button(publicRow, "", null, 56, false, 26);
        var pub = TitleUi.Button(publicRow, "", null, 56, false, 26);
        Action showPublic = () => { SetLabel(priv, (draft.isPublic ? "○" : "●") + " 비공개"); SetLabel(pub, (draft.isPublic ? "●" : "○") + " 공개"); };
        priv.onClick.AddListener(() => { draft.isPublic = false; showPublic(); });
        pub.onClick.AddListener(() => { draft.isPublic = true; showPublic(); });
        showPublic();
        TitleUi.Label(panel, "공개 서버 목록은 아직 준비 중입니다. 지금은 공개여도 주소로만 들어올 수 있습니다.", 22, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);

        TitleUi.Label(panel, "비밀번호 (비워 두면 누구나 들어옴)", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        var passwordInput = TitleUi.Input(panel, "비밀번호 (선택, 32자까지)", ServerSettings.PasswordMax);
        passwordInput.text = draft.password;

        TMP_InputField portInput = null;
        Action capture = () =>
        {
            draft.name = nameInput.text; draft.password = passwordInput.text;
            if (portInput && int.TryParse(portInput.text, out int port)) draft.port = port;
        };
        TitleUi.Button(panel, openAdvanced ? "고급 접기" : "고급 ▶", () => { capture(); openAdvanced = !openAdvanced; ShowServerOpen(draft); }, 48, false, 24);
        if (openAdvanced)
        {
            var portRow = TitleUi.Row(panel, 56, 12, false);
            TitleUi.Width(TitleUi.Label(portRow, "포트", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft), 120);
            portInput = TitleUi.Input(portRow, "1024~65535", 5, null, 56);
            portInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            portInput.text = draft.port.ToString();
            TitleUi.Width(portInput, 0, 1);
        }
        TitleUi.Button(panel, "서버 시작", () => { capture(); RunOpenServer(draft.Clamp()); }, 72, true, 34);
        TitleUi.Button(panel, "뒤로", ShowMain, 56, false, 26);
        Focus();
    }

    void ShowServerRunning(LocalServerProcess.RunningInfo running)
    {
        Begin("serverrunning", true, 760, ShowMain);
        var settings = ServerSettings.Load();
        var panel = Panel("내 서버 실행 중 (포트 " + running.port + ")", settings.name + " · 최대 " + settings.maxPlayers + "명" + (settings.password.Length > 0 ? " · 비밀번호 있음" : ""));
        TitleUi.Label(panel, "게임을 꺼도 서버는 계속 돕니다.\n다른 사람은 이 PC 주소의 " + running.port + "번 포트로 들어옵니다.", 23, TitleUi.Muted);
        TitleUi.Button(panel, "접속", () => RunJoinServer("127.0.0.1:" + running.port, settings.password, ShowServerOpen, false), 72, true, 34);
        TitleUi.Button(panel, "끄기", () => { LocalServerProcess.Stop(); ShowServerOpen(); }, 60, true, 28);
        TitleUi.Button(panel, "뒤로", ShowMain, 56, false, 26);
        Focus();
    }

    async void RunOpenServer(ServerSettings settings)
    {
        ShowBusy("서버를 여는 중…");
        string error = await TitleFlow.OpenServer(settings);
        if (!this) return;
        if (error != null) ShowError(error, () => RunOpenServer(settings), () => ShowServerOpen(settings), null);
    }

    // ---- 참가하기 (server address) ----

    public void ShowServerJoin() => ShowServerJoin(null);

    void ShowServerJoin(string address)
    {
        Begin("serverjoin", true, 760, ShowMain);
        var panel = Panel("참가하기", "서버 주소를 입력하세요. 포트를 빼면 " + ServerSettings.DefaultPort + "번으로 접속합니다.");
        var addressInput = TitleUi.Input(panel, "주소 (예: 192.168.0.5:7777)", 120);
        addressInput.text = address ?? "";
        var passwordInput = TitleUi.Input(panel, "비밀번호 (없으면 비워 두기)", ServerSettings.PasswordMax);
        passwordInput.contentType = TMP_InputField.ContentType.Password;
        var hint = TitleUi.Label(panel, " ", 24, TitleUi.Warn);
        Action submit = () =>
        {
            string typed = addressInput.text.Trim();
            if (!TitleFlow.TryParseAddress(typed, out _, out _)) { hint.text = TitleFlow.Message("bad_address"); return; }
            RunJoinServer(typed, passwordInput.text, () => ShowServerJoin(typed), true);
        };
        addressInput.onSubmit.AddListener(_ => submit());
        passwordInput.onSubmit.AddListener(_ => submit());
        TitleUi.Button(panel, "접속", () => submit(), 72, true, 34);
        var recent = ServerSettings.Recent;
        if (recent.Count > 0)
        {
            TitleUi.Label(panel, "최근 접속한 주소", 22, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
            foreach (string r in recent) TitleUi.Button(panel, r, () => addressInput.text = r, 46, false, 24);
        }
        TitleUi.Button(panel, "뒤로", ShowMain, 56, false, 26);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(addressInput.gameObject);
        addressInput.ActivateInputField();
    }

    // A wrong password gets no retry button: 뒤로 goes back to the form to type it again.
    async void RunJoinServer(string address, string password, Action from, bool remember)
    {
        ShowBusy("서버에 접속하는 중…");
        string error = await TitleFlow.JoinServer(address, password, remember);
        if (!this) return;
        if (error != null) ShowError(error, error == "Password" ? null : (Action)(() => RunJoinServer(address, password, from, remember)), from, null);
    }

    // ---- Steam friend invite ----

    /// <summary>An invite was accepted (Steam overlay or +connect_lobby): join that lobby from wherever the title screen is.</summary>
    public async void JoinFromInvite(ulong lobbyId)
    {
        if (busy) return;
        ShowBusy("친구의 방에 들어가는 중…");
        string error = await TitleFlow.JoinInvite(lobbyId);
        if (!this) return;
        if (error != null) ShowError(error, () => JoinFromInvite(lobbyId), ShowLegacy, null);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- 방 만들기 ----

    public void ShowCreate()
    {
        Begin("create", true, 700, ShowLegacy);
        worldReady = false;
        var panel = Panel("방 만들기", "새 월드를 시작하거나, 저장된 월드를 이어서 합니다.");
        TitleUi.Button(panel, "신규 월드 시작", OnNewWorld, 76, true, 32);
        bool has = WorldSave.TryPeek(out DateTime savedAt, out long minutes);
        string label = has
            ? "기존 월드 이어하기\n<size=22>" + savedAt.ToString("yyyy-MM-dd HH:mm") + " 저장 · 게임 " + (GameTime.FromTotalMinutes(minutes).absoluteDay + 1) + "일째</size>"
            : "저장된 월드가 없습니다";
        var existing = TitleUi.Button(panel, label, () => BeginCreate(false), 96, true, 32);
        existing.interactable = has;
        TitleUi.Button(panel, "뒤로", ShowLegacy, 56, false, 26);
        Focus();
    }

    void OnNewWorld()
    {
        if (!WorldSave.HasLocalWorld) { BeginCreate(true); return; }
        ShowConfirm("이미 저장된 월드가 있습니다.\n새로 시작하면 기존 월드는 지워지지 않고 백업 파일(world.backup-날짜.json)로 보관되며, 새 월드가 시작됩니다.",
            "새로 시작", () => BeginCreate(true), ShowCreate);
    }

    void BeginCreate(bool newWorld)
    {
        pendingNew = newWorld; worldReady = false;
        RunCreate();
    }

    // The world is chosen first, so its character is known before the room exists: an old one comes back, a new one is made here.
    async void RunCreate()
    {
        ShowBusy("서버에 접속하는 중…");
        string error = await TitleFlow.SignIn(null, null);
        if (!this) return;
        if (error == null) error = await TitleFlow.Connect();
        if (!this) return;
        if (error == null && !worldReady) { error = TitleFlow.PrepareWorld(pendingNew); worldReady = error == null; }
        if (error == null) error = await TitleFlow.LoadCharacter();
        if (!this) return;
        if (error != null) { FailCreate(error); return; }
        if (GameSession.HasCharacter) Host();
        else ShowName(CharacterHint, (name, gender) => { TitleFlow.CreateCharacter(name, gender); Host(); }, ShowCreate);
    }

    async void Host()
    {
        ShowBusy("방을 만드는 중…");
        string error = await TitleFlow.HostOnline(GameSession.LocalPlayerName + "의 방");
        if (!this) return;
        if (error == null) { ShowBusy("맵을 불러오는 중…"); if (!TitleFlow.EnterGame()) error = "load_failed"; }
        if (error != null) FailCreate(error);
    }

    const string CharacterHint = "이 방에서 살아갈 캐릭터의 이름과 성별을 정하세요.\n방(월드)마다 캐릭터가 따로이고, 다시 들어오면 이 캐릭터로 이어서 합니다.";

    void FailCreate(string error)
    {
        ShowError(error, RunCreate, ShowCreate, () => OfflineStart(null));
    }

    void OfflineStart(string name)
    {
        ShowBusy("오프라인으로 시작하는 중…");
        string error = TitleFlow.StartOffline(pendingNew, name, worldReady);
        if (error == null) return;
        worldReady = false;
        ShowError(error, () => OfflineStart(name), ShowCreate, null);
    }

    // ---- 참가하기 ----

    public void StartJoin() => RunJoin();

    async void RunJoin()
    {
        ShowBusy("서버에 접속하는 중…");
        string error = await TitleFlow.SignIn(null, null);
        if (!this) return;
        if (error == null) error = await TitleFlow.Connect();
        if (!this) return;
        if (error == null) { TitleFlow.PrepareForJoin(); ShowJoin(); return; }
        ShowError(error, RunJoin, ShowLegacy, null);
    }

    void LeaveJoin()
    {
        var net = NetSession.Instance;
        if (net) net.Disconnect();
        ShowLegacy();
    }

    /// <summary>The main join screen: type (or paste) the 6-character room code. The room list is the secondary option.</summary>
    public void ShowJoin()
    {
        Begin("join", true, 700, LeaveJoin);
        var panel = Panel("참가하기", NetMode.IsSteam
            ? "친구에게 받은 방 코드를 입력하세요.\nSteam 친구 초대를 받았다면 Steam 오버레이(Shift+Tab)에서 수락하면 바로 들어갑니다."
            : "친구에게 받은 방 코드를 입력하세요.");
        var input = TitleUi.Input(panel, "방 코드", 24, null, 96);
        StyleCodeInput(input);
        var hint = TitleUi.Label(panel, " ", 24, TitleUi.Warn);
        input.onValueChanged.AddListener(v =>
        {
            string code = RoomCode.Normalize(v);
            if (code.Length > RoomCode.Length) code = code.Substring(0, RoomCode.Length);
            if (code != v) input.text = code; // upper case, no spaces or hyphens, also for pasted text
            hint.text = " ";
        });
        Action submit = () =>
        {
            string code = RoomCode.Normalize(input.text);
            if (code.Length != RoomCode.Length) { hint.text = TitleFlow.Message("invalid_code"); return; }
            EnterRoom(code, ShowJoin);
        };
        input.onSubmit.AddListener(_ => submit());
        TitleUi.Button(panel, "참가", () => submit(), 72, true, 34);
        TitleUi.Button(panel, "방 목록 보기", ShowRooms, 56, false, 26);
        TitleUi.Button(panel, "뒤로", LeaveJoin, 56, false, 26);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(input.gameObject);
        input.ActivateInputField();
    }

    // A big centred field with wide letter gaps: a code is read from a friend's message and typed once.
    static void StyleCodeInput(TMP_InputField input)
    {
        input.pointSize = 56;
        var text = input.textComponent;
        text.fontSize = 56; text.alignment = TextAlignmentOptions.Center; text.characterSpacing = 14;
        var hint = (TMP_Text)input.placeholder;
        hint.fontSize = 40; hint.alignment = TextAlignmentOptions.Center;
    }

    public void ShowRooms()
    {
        Begin("rooms", false, 980, ShowJoin);
        var panel = Panel("방 목록");
        roomHint = TitleUi.Label(panel, "", 25, TitleUi.Muted);
        roomList = TitleUi.Column(panel, "Rooms", 0, 10, null, 0, false);
        var row = TitleUi.Row(panel, 60);
        TitleUi.Button(row, "새로고침", RefreshRooms, 60, true, 28);
        TitleUi.Button(row, "뒤로", ShowJoin, 60, false, 28);
        shownRooms = null; nextRefresh = 0f;
        RefreshRooms();
        BuildRoomRows();
        Focus();
    }

    void RefreshRooms()
    {
        var net = NetSession.Instance;
        if (net && net.Connected) net.RefreshRooms();
        nextRefresh = Time.unscaledTime + RoomRefreshSeconds;
    }

    // The join and room-list screens need the server connection; if it drops, say so instead of leaving dead buttons.
    void UpdateConnected()
    {
        var net = NetSession.Instance;
        if (!net || busy) return;
        if (!net.Connected)
        {
            string error = string.IsNullOrEmpty(net.LastError) ? "disconnected" : net.LastError;
            ShowError(error, RunJoin, ShowLegacy, null);
            return;
        }
        if (screen != "rooms") return;
        if (Time.unscaledTime >= nextRefresh) RefreshRooms();
        if (!ReferenceEquals(net.Rooms, shownRooms)) BuildRoomRows();
    }

    void BuildRoomRows()
    {
        var net = NetSession.Instance;
        shownRooms = net ? net.Rooms : new RoomInfo[0];
        bool hadFocus = EventSystem.current && EventSystem.current.currentSelectedGameObject
            && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(roomList);
        TitleUi.Clear(roomList);
        roomHint.text = shownRooms.Length == 0 ? "열려 있는 방이 없습니다. 다른 사람이 방을 만들면 여기에 나타납니다." : "참가할 방을 고르세요. (3초마다 자동 새로고침)";
        for (int i = 0; i < shownRooms.Length && i < MaxRoomRows; i++)
        {
            var room = shownRooms[i];
            var row = TitleUi.Row(roomList, 64, 12, false);
            var text = TitleUi.Label(row, room.room + "   <size=22><color=#C9C3D9>" + room.count + "/" + room.max + "명</color></size>", 30, TitleUi.Cream, TextAlignmentOptions.MidlineLeft);
            TitleUi.Width(text, 0, 1);
            var join = TitleUi.Button(row, room.count >= room.max ? "가득 참" : "참가", () => EnterRoom(room.rid, ShowRooms), 64, true, 28);
            join.interactable = room.count < room.max;
            TitleUi.Width(join, 170);
        }
        if (shownRooms.Length > MaxRoomRows) TitleUi.Label(roomList, "외 " + (shownRooms.Length - MaxRoomRows) + "개 (표시 한도)", 22, TitleUi.Muted);
        if (hadFocus) Focus();
    }

    // Joins a room by its id (the typed code or a list entry), then loads the map. `from` is the screen to return to on an error.
    // The room's world is known only after joining, so its character is looked up (or made) while already in the room.
    async void EnterRoom(string rid, Action from)
    {
        ShowBusy("방에 들어가는 중…");
        string error = await TitleFlow.JoinOnline(rid);
        if (!this) return;
        if (error == null) error = await TitleFlow.LoadCharacter();
        if (!this) return;
        Action back = () => { var n = NetSession.Instance; if (n && n.InRoom) n.Leave(); if (n && n.Connected) from(); else ShowLegacy(); };
        if (error == null && !GameSession.HasCharacter)
        {
            ShowName(CharacterHint, (name, gender) => { TitleFlow.CreateCharacter(name, gender); EnterMap(back); }, back);
            return;
        }
        if (error == null) { EnterMap(back); return; }
        if (NetSession.Instance && NetSession.Instance.InRoom) NetSession.Instance.Leave();
        ShowError(error, error == "room_not_found" ? null : (Action)(() => EnterRoom(rid, from)), back, null);
    }

    void EnterMap(Action back)
    {
        ShowBusy("맵을 불러오는 중…");
        if (!TitleFlow.EnterGame()) ShowError("load_failed", null, back, null);
    }

    // ---- name sign-up ----

    void ShowName(string sub, Action<string, string> next, Action onBack)
    {
        Begin("name", true, 700, onBack);
        var panel = Panel("캐릭터 만들기", sub);
        var input = TitleUi.Input(panel, "이름 (한글·영문·숫자·_ 2~12자)", 12, null);
        string gender = "male";
        var row = TitleUi.Row(panel, 60);
        var male = TitleUi.Button(row, "● 남자", null, 60, true, 28);
        var female = TitleUi.Button(row, "○ 여자", null, 60, false, 28);
        male.onClick.AddListener(() => { gender = "male"; SetLabel(male, "● 남자"); SetLabel(female, "○ 여자"); });
        female.onClick.AddListener(() => { gender = "female"; SetLabel(male, "○ 남자"); SetLabel(female, "● 여자"); });
        var error = TitleUi.Label(panel, "", 24, TitleUi.Warn);
        Action submit = () =>
        {
            string name = input.text.Trim();
            if (!TitleFlow.IsValidName(name)) { error.text = TitleFlow.Message("invalid_name"); return; }
            next(name, gender);
        };
        input.onSubmit.AddListener(_ => submit());
        TitleUi.Button(panel, "확인", () => submit(), 64, true, 30);
        TitleUi.Button(panel, "뒤로", () => onBack?.Invoke(), 56, false, 26);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(input.gameObject);
        input.ActivateInputField();
    }

    static void SetLabel(Button button, string text) { button.GetComponentInChildren<TMP_Text>().text = text; }

    // ---- busy / error / confirm ----

    void ShowBusy(string message)
    {
        Begin("busy", true, 700, null);
        busy = true;
        var panel = Panel(message);
        TitleUi.Label(panel, "잠시만 기다려 주세요.", 24, TitleUi.Muted);
    }

    void ShowError(string code, Action retry, Action onBack, Action offline)
    {
        Begin("error", true, 760, onBack);
        var panel = Panel("문제가 생겼어요");
        TitleUi.Label(panel, TitleFlow.Message(code), 27, TitleUi.Warn);
        TitleUi.Spacer(panel, 6);
        if (retry != null) TitleUi.Button(panel, "다시 시도", retry, 64, true, 30);
        if (offline != null) TitleUi.Button(panel, "오프라인으로 시작", offline, 64, true, 30);
        bool unreachable = code == "network" || code == "timeout" || code == "connect_failed" || code == "bad_url";
        if (code == ServerBudget.Code || code.StartsWith("steam_"))
            TitleUi.Button(panel, NetMode.IsSteam ? "Cloudflare 서버 모드로 바꾸기" : "Steam 모드로 바꾸기", () => { NetMode.IsSteam = !NetMode.IsSteam; ShowLegacy(); }, 56, false, 26);
        if (unreachable && !NetMode.IsSteam && (LocalAccount.IsCustomUrl || ServerConfig.IsPlaceholder)) // with the built-in server the address is not the player's problem
            TitleUi.Button(panel, "서버 주소 설정", () => ShowSettings(() => onBack?.Invoke(), true), 56, false, 26);
        TitleUi.Button(panel, "뒤로", () => onBack?.Invoke(), 56, false, 26);
        Focus();
    }

    void ShowConfirm(string message, string yesText, Action yes, Action no)
    {
        Begin("confirm", true, 780, no);
        var panel = Panel("확인");
        TitleUi.Label(panel, message, 27, TitleUi.Cream);
        TitleUi.Spacer(panel, 6);
        var row = TitleUi.Row(panel, 64);
        TitleUi.Button(row, "취소", () => no?.Invoke(), 64, false, 28);
        TitleUi.Button(row, yesText, () => yes?.Invoke(), 64, true, 28);
        Focus();
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(row.GetChild(0).gameObject); // the safe choice is focused
    }

    // ---- 설정 ----

    public void ShowSettings(Action onBack = null, bool advanced = false)
    {
        Action leave = onBack ?? ShowMain;
        Begin("settings", false, 860, leave);
        var panel = Panel("설정");

        bool steam = NetMode.IsSteam;
        TitleUi.Label(panel, "연결 방식: " + NetMode.Label, 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        TitleUi.Button(panel, steam ? "Cloudflare 서버 모드로 바꾸기" : "Steam 개발 모드로 바꾸기", () => { NetMode.IsSteam = !steam; ShowSettings(onBack, advanced); }, 56, false, 26);
        TitleUi.Label(panel, "캐릭터는 방(월드)마다 따로 만듭니다. 방을 만들거나 들어갈 때 이름과 성별을 정합니다.", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);

        TitleUi.Label(panel, "프로필 슬롯 (한 PC에서 게임 2개를 켜는 테스트용)", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        var slotRow = TitleUi.Row(panel, 56, 12, false);
        var prev = TitleUi.Button(slotRow, "◀", () => SwitchSlot(ProfileSlot.Slot - 1, onBack, advanced), 56, false, 26);
        var slotLabel = TitleUi.Label(slotRow, "슬롯 " + ProfileSlot.Slot + " / " + ProfileSlot.Max, 28, TitleUi.Cream);
        var next = TitleUi.Button(slotRow, "▶", () => SwitchSlot(ProfileSlot.Slot + 1, onBack, advanced), 56, false, 26);
        TitleUi.Width(prev, 90); TitleUi.Width(next, 90); TitleUi.Width(slotLabel, 0, 1);
        prev.interactable = next.interactable = !ProfileSlot.FromCommandLine;
        TitleUi.Label(panel, ProfileSlot.FromCommandLine
            ? "실행 인자 -profile " + ProfileSlot.Slot + " 로 고정되어 있습니다."
            : "슬롯마다 계정·토큰·월드 저장 폴더가 따로입니다. 두 번째 창은 다른 슬롯(또는 -profile 2)으로 실행하세요.", 22, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);

        if (!steam)
        {
            TitleUi.Button(panel, "계정 초기화", ConfirmReset, 56, false, 26);
            TitleUi.Button(panel, advanced ? "고급 접기" : "고급 ▶", () => ShowSettings(onBack, !advanced), 56, false, 26);
            if (advanced) BuildAdvanced(panel, onBack);
        }
        TitleUi.Button(panel, "뒤로", () => leave(), 56, false, 26);
        Focus();
    }

    // Settings > 고급: the server address. Players use the built-in server and never need this; it is for the local test server.
    void BuildAdvanced(RectTransform panel, Action onBack)
    {
        bool fixedByArg = ServerConfig.CommandLineUrl != null;
        string origin = fixedByArg ? "실행 인자 -server 로 고정" : LocalAccount.IsCustomUrl ? "직접 지정한 주소" : "기본 서버";
        TitleUi.Label(panel, "서버 주소 (" + origin + ")", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        var url = TitleUi.Input(panel, "비워 두면 기본 서버", 120, null, 56);
        url.text = LocalAccount.IsCustomUrl ? LocalAccount.ServerUrl : "";
        var result = TitleUi.Label(panel, " ", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        var row = TitleUi.Row(panel, 56);
        var apply = TitleUi.Button(row, "적용", () => { LocalAccount.ServerUrl = url.text; ShowSettings(onBack, true); }, 56, true, 26);
        TitleUi.Button(row, "연결 테스트", () => TestServer(url.text, result), 56, true, 26);
        var local = TitleUi.Button(panel, "로컬 테스트 서버 (" + ServerConfig.LocalUrl + ")", () => url.text = ServerConfig.LocalUrl, 52, false, 24);
        var reset = TitleUi.Button(panel, "기본 서버로 되돌리기", () => { LocalAccount.ServerUrl = ""; ShowSettings(onBack, true); }, 52, false, 24);
        apply.interactable = local.interactable = reset.interactable = !fixedByArg;
        if (fixedByArg) TitleUi.Label(panel, "주소: " + LocalAccount.ServerUrl, 22, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
    }

    async void TestServer(string url, TMP_Text result)
    {
        result.color = TitleUi.Muted; result.text = "확인하는 중…";
        string normalized = LocalAccount.Normalize(url);
        bool ok = await TitleFlow.TestServer(normalized);
        if (!this || !result) return;
        result.color = ok ? TitleUi.Good : TitleUi.Warn;
        result.text = ok ? "연결됨: " + normalized : "연결할 수 없습니다: " + normalized;
    }

    void SwitchSlot(int slot, Action onBack, bool advanced)
    {
        if (slot < 1 || slot > ProfileSlot.Max) return;
        TitleFlow.ChangeSlot(slot);
        ShowSettings(onBack, advanced);
    }

    void ConfirmReset()
    {
        ShowConfirm("이 슬롯에 저장된 계정 정보를 이 PC에서 지웁니다.\n서버의 계정과 월드 파일은 남지만, 이 PC는 더 이상 그 계정으로 자동 접속하지 않습니다. 계속할까요?",
            "계정 초기화", () => { TitleFlow.ResetAccount(); ShowSettings(); }, () => ShowSettings());
    }

    // ---- test support ----

    /// <summary>Invokes the first enabled button whose text contains <paramref name="text"/>. Used by the headless QA.</summary>
    public bool Click(string text)
    {
        return TitleUi.ClickButton(content, text);
    }

    /// <summary>Opens a screen by name with sample content (screenshots and QA only).</summary>
    public void Open(string which)
    {
        switch (which)
        {
            case "main": ShowMain(); break;
            case "legacy": ShowLegacy(); break;
            case "serveropen": ShowServerOpen(); break;
            case "serverjoin": ShowServerJoin(); break;
            case "create": ShowCreate(); break;
            case "settings": ShowSettings(); break;
            case "settings-advanced": ShowSettings(null, true); break;
            case "join": ShowJoin(); break;
            case "rooms": ShowRooms(); break;
            case "name": ShowName(CharacterHint, (n, g) => { }, ShowMain); break;
            case "error": ShowError("network", () => { }, ShowMain, () => { }); break;
            case "confirm": OnNewWorld(); break;
            case "busy": ShowBusy("서버에 접속하는 중…"); break;
        }
    }

    public TMP_InputField FirstInput() => content.GetComponentInChildren<TMP_InputField>();
}
