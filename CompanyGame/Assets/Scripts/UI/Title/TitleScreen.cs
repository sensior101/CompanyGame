using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The title menu (Scenes/Title.unity): 혼자 하기 / 서버 열기 / 참가하기 / 설정 (the sync layer, Docs/Multiplayer_Redesign.md
/// 12.3, 12.5: worlds and characters live on the server, 혼자 하기 is my PC server for one; 12.13: the public server list
/// and server codes on EOS), plus the small screens they lead to (character, errors). Built in code with uGUI; keyboard
/// (arrows/Tab/Enter/Esc) and mouse both work.
/// All real work is in <see cref="TitleFlow"/>.
/// </summary>
public sealed class TitleScreen : MonoBehaviour
{
    public static TitleScreen Current { get; private set; }

    Canvas canvas;
    RectTransform content, logo;
    TMP_Text footer;
    Action back;
    bool busy;
    string screen = "";
    float nextHide;
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
        screen = name; busy = false; back = onBack;
        TitleUi.Clear(content);
        logo.gameObject.SetActive(withLogo);
        content.sizeDelta = new Vector2(width, 0);
        content.anchoredPosition = new Vector2(0, withLogo ? -420 : -60);
        footer.text = "프로필 슬롯 " + ProfileSlot.Slot;
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
        if (Time.unscaledTime >= nextHide) { nextHide = Time.unscaledTime + 1f; HideOtherCanvases(); } // HUDs created after this scene loaded
    }

    // ---- main ----

    public void ShowMain()
    {
        Begin("main", true, 560, null);
        TitleUi.Button(content, "혼자 하기", ShowSolo, 76, true, 36);
        TitleUi.Button(content, "서버 열기", () => ShowServerOpen(), 76, true, 36);
        TitleUi.Button(content, "참가하기", () => ShowServerJoin(), 76, true, 36);
        TitleUi.Button(content, "설정", () => ShowSettings(), 76, true, 36);
        TitleUi.Button(content, "종료", Quit, 60, false, 28);
        Focus();
    }

    // ---- 혼자 하기 / worlds / the server's character (12.5) ----

    const int MaxWorldRows = 6;
    const string ServerCharacterHint = "이 월드에서 살아갈 캐릭터의 이름과 성별을 정하세요.\n월드마다 캐릭터가 따로이고, 다시 들어오면 이 캐릭터로 이어서 합니다.";

    /// <summary>혼자 하기: pick a world, then my PC server for one opens on it.</summary>
    public void ShowSolo() => ShowWorlds("혼자 하기", "이 PC에서 혼자 합니다. 같은 월드를 나중에 서버 열기로 열면 친구도 들어올 수 있습니다.", RunSolo, ShowMain);

    // The worlds of this slot (shared by 혼자 하기 and 서버 열기): 새 월드, then the saved ones, newest first.
    void ShowWorlds(string heading, string sub, Action<string> pick, Action onBack)
    {
        Begin("worlds", false, 820, onBack);
        var panel = Panel(heading, sub);
        TitleUi.Button(panel, "새 월드 만들기", () => pick(""), 72, true, 32);
        var worlds = WorldStore.List(ServerSettings.WorldsFolder);
        var running = LocalServerProcess.CanStart ? LocalServerProcess.Running() : null;
        TitleUi.Label(panel, worlds.Count == 0 ? "저장된 월드가 없습니다." : "저장된 월드", 22, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        for (int i = 0; i < worlds.Count && i < MaxWorldRows; i++)
        {
            var w = worlds[i];
            bool busyWorld = running != null && running.world == w.id;
            var button = TitleUi.Button(panel, WorldName(w) + "\n<size=22>마지막 저장 " + w.savedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " · 캐릭터 " + w.characters + "명"
                + (busyWorld ? " · 서버에서 실행 중" : "") + "</size>", () => pick(w.id), 88, false, 28);
            button.interactable = !busyWorld; // one world file, one server
        }
        if (worlds.Count > MaxWorldRows) TitleUi.Label(panel, "외 " + (worlds.Count - MaxWorldRows) + "개 (표시 한도)", 22, TitleUi.Muted);
        TitleUi.Button(panel, "뒤로", () => onBack(), 56, false, 26);
        Focus();
    }

    // A world has no name of its own: it is called by when it was made.
    static string WorldName(WorldStore.Summary w) => "월드 " + w.createdAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    static string WorldLabel(string id)
    {
        if (string.IsNullOrEmpty(id)) return "새 월드";
        foreach (var w in WorldStore.List(ServerSettings.WorldsFolder)) if (w.id == id) return WorldName(w);
        return "새 월드"; // made but never saved yet
    }

    async void RunSolo(string world)
    {
        ShowBusy("혼자 하기를 준비하는 중…");
        string error = await TitleFlow.OpenSolo(world);
        if (!this) return;
        if (error != null) { ShowError(error, () => RunSolo(world), ShowSolo); return; }
        AfterConnect(ShowMain);
    }

    // Connected and the server said whether I have a character there: make one, or go in.
    void AfterConnect(Action back)
    {
        Action leave = () => { TitleFlow.LeaveServer(); back(); };
        if (SyncClient.Instance.HasCharacter) { EnterServer(back); return; }
        ShowName(ServerCharacterHint, (name, gender) => RunCreateCharacter(name, gender, back), leave);
    }

    async void RunCreateCharacter(string name, string gender, Action back)
    {
        ShowBusy("캐릭터를 만드는 중…");
        string error = await TitleFlow.CreateServerCharacter(name, gender);
        if (!this) return;
        Action leave = () => { TitleFlow.LeaveServer(); back(); };
        if (error == "character_name_taken" || error == "invalid_name")
            ShowName(ServerCharacterHint, (n, g) => RunCreateCharacter(n, g, back), leave, TitleFlow.Message(error), name, gender);
        else if (error != null) ShowError(error, null, leave);
        else EnterServer(back);
    }

    void EnterServer(Action back)
    {
        ShowBusy("맵을 불러오는 중…");
        string error = TitleFlow.EnterServerWorld();
        if (error != null) ShowError(error, null, back);
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
        TMP_InputField passwordInput = null, portInput = null;
        Action capture = () =>
        {
            draft.name = nameInput.text; draft.password = passwordInput.text;
            if (portInput && int.TryParse(portInput.text, out int port)) draft.port = port;
        };

        TitleUi.Button(panel, "월드: " + WorldLabel(draft.world) + " (바꾸기)", () =>
        {
            capture();
            ShowWorlds("월드 선택", "서버에서 쓸 월드를 고르세요. 캐릭터는 월드마다 따로입니다.", id => { draft.world = id; ShowServerOpen(draft); }, () => ShowServerOpen(draft));
        }, 52, false, 24);

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
        var publicNote = TitleUi.Label(panel, "", 22, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        Action showPublic = () =>
        {
            SetLabel(priv, (draft.isPublic ? "○" : "●") + " 비공개"); SetLabel(pub, (draft.isPublic ? "●" : "○") + " 공개");
            publicNote.text = (draft.isPublic ? "공개: 참가하기의 공개 서버 목록에 나오고, 서버 코드로도 들어올 수 있습니다."
                                              : "비공개: 목록에 나오지 않습니다. 서버 코드를 아는 사람만 들어올 수 있습니다.")
                + (EosAccount.Enabled ? "" : "\n지금은 EOS에 연결되지 않아 목록과 코드 없이 주소로만 들어올 수 있습니다.");
        };
        priv.onClick.AddListener(() => { draft.isPublic = false; showPublic(); });
        pub.onClick.AddListener(() => { draft.isPublic = true; showPublic(); });
        showPublic();

        TitleUi.Label(panel, "비밀번호 (비워 두면 누구나 들어옴)", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        passwordInput = TitleUi.Input(panel, "비밀번호 (선택, 32자까지)", ServerSettings.PasswordMax);
        passwordInput.text = draft.password;

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
        TitleUi.Label(panel, string.IsNullOrEmpty(running.code) ? "서버 코드: 준비 중 (EOS 연결을 기다리는 중이거나 EOS 없이 도는 서버)"
            : "서버 코드: <color=#FFC857>" + running.code + "</color>  (참가하기 > 서버 코드)", 28, TitleUi.Cream);
        TitleUi.Label(panel, "게임을 꺼도 서버는 계속 돕니다.\n같은 네트워크나 포트를 연 경우 이 PC 주소의 " + running.port + "번 포트로도 들어옵니다.", 23, TitleUi.Muted);
        TitleUi.Button(panel, "접속", () => RunJoin(() => TitleFlow.JoinMine(running), ShowServerOpen), 72, true, 34);
        TitleUi.Button(panel, "끄기", () => { LocalServerProcess.Stop(); ShowServerOpen(); }, 60, true, 28);
        TitleUi.Button(panel, "뒤로", ShowMain, 56, false, 26);
        Focus();
    }

    async void RunOpenServer(ServerSettings settings)
    {
        ShowBusy("서버를 여는 중…");
        string error = await TitleFlow.OpenServer(settings);
        if (!this) return;
        if (error != null) ShowError(error, () => RunOpenServer(settings), () => ShowServerOpen(settings));
        else AfterConnect(() => ShowServerOpen());
    }

    // ---- 참가하기 (public list, server code, address: 12.13) ----

    const int MaxListRows = 6;
    const float RefreshSeconds = 10;
    static List<EosSession.Entry> listed; // the last list (null: not searched yet), kept while the game runs
    static string listError;
    static float listedAt = -RefreshSeconds;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { listed = null; listError = null; listedAt = -RefreshSeconds; }
    bool listing, joinAdvanced;
    string typedCode = "", typedAddress = "";

    public void ShowServerJoin()
    {
        Begin("serverjoin", false, 860, ShowMain);
        var panel = Panel("참가하기");
        bool eos = EosAccount.Enabled;

        var head = TitleUi.Row(panel, 52, 12, false);
        TitleUi.Width(TitleUi.Label(head, "공개 서버", 30, TitleUi.Cream, TextAlignmentOptions.MidlineLeft, FontStyles.Bold), 0, 1);
        var refresh = TitleUi.Button(head, "새로고침", () => RefreshList(), 52, false, 24);
        TitleUi.Width(refresh, 180);
        refresh.interactable = eos && !listing;
        if (!eos) TitleUi.Label(panel, TitleFlow.Message("eos_offline"), 23, TitleUi.Warn, TextAlignmentOptions.MidlineLeft);
        else if (listing || listed == null && listError == null) TitleUi.Label(panel, "목록을 불러오는 중…", 23, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        else if (listError != null) TitleUi.Label(panel, TitleFlow.Message(listError), 23, TitleUi.Warn, TextAlignmentOptions.MidlineLeft);
        else if (listed.Count == 0) TitleUi.Label(panel, "지금 열린 공개 서버가 없습니다. 서버 코드를 받았다면 아래에 입력하세요.", 23, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        else
        {
            for (int i = 0; i < listed.Count && i < MaxListRows; i++)
            {
                var e = listed[i];
                TitleUi.Button(panel, e.name + "    <size=24>" + e.players + "/" + e.maxPlayers + "명" + (e.hasPassword ? " · 비밀번호" : "") + "</size>", () => PickListed(e), 60, false, 28);
            }
            if (listed.Count > MaxListRows) TitleUi.Label(panel, "외 " + (listed.Count - MaxListRows) + "개 (표시 한도)", 22, TitleUi.Muted);
        }

        TitleUi.Label(panel, "서버 코드", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        var codeRow = TitleUi.Row(panel, 60, 12, false);
        var codeInput = TitleUi.Input(codeRow, "6자 (예: K7PQ2M)", 8, null, 60);
        codeInput.text = typedCode;
        codeInput.onValueChanged.AddListener(t => typedCode = t);
        codeInput.onSubmit.AddListener(t => RunJoinCode(t));
        TitleUi.Width(codeInput, 0, 1);
        var codeJoin = TitleUi.Button(codeRow, "접속", () => RunJoinCode(codeInput.text), 60, true, 28);
        TitleUi.Width(codeJoin, 160);
        codeInput.interactable = codeJoin.interactable = eos;

        TitleUi.Button(panel, joinAdvanced ? "직접 주소 입력 접기" : "직접 주소 입력 (데디케이티드·같은 네트워크) ▶", () => { joinAdvanced = !joinAdvanced; ShowServerJoin(); }, 48, false, 24);
        TMP_InputField addressInput = null;
        if (joinAdvanced)
        {
            addressInput = TitleUi.Input(panel, "주소 (예: 192.168.0.5:7777, 포트를 빼면 " + ServerSettings.DefaultPort + ")", 120);
            addressInput.text = typedAddress;
            addressInput.onValueChanged.AddListener(t => typedAddress = t);
            var passwordInput = TitleUi.Input(panel, "비밀번호 (없으면 비워 두기)", ServerSettings.PasswordMax);
            passwordInput.contentType = TMP_InputField.ContentType.Password;
            var hint = TitleUi.Label(panel, " ", 24, TitleUi.Warn);
            Action submit = () =>
            {
                string typed = addressInput.text.Trim();
                if (!TitleFlow.TryParseAddress(typed, out _, out _)) { hint.text = TitleFlow.Message("bad_address"); return; }
                string password = passwordInput.text;
                RunJoin(() => TitleFlow.JoinServer(typed, password), ShowServerJoin);
            };
            addressInput.onSubmit.AddListener(_ => submit());
            passwordInput.onSubmit.AddListener(_ => submit());
            TitleUi.Button(panel, "주소로 접속", () => submit(), 60, true, 30);
            var recent = ServerSettings.Recent;
            if (recent.Count > 0)
            {
                TitleUi.Label(panel, "최근 접속한 주소", 22, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
                foreach (string r in recent) TitleUi.Button(panel, r, () => addressInput.text = r, 46, false, 24);
            }
        }
        TitleUi.Button(panel, "뒤로", ShowMain, 56, false, 26);
        var first = addressInput ? addressInput : eos ? codeInput : null;
        if (first && EventSystem.current) { EventSystem.current.SetSelectedGameObject(first.gameObject); first.ActivateInputField(); }
        else Focus();
        if (eos && listed == null && listError == null && !listing) RefreshList();
    }

    async void RefreshList()
    {
        if (listing || Time.unscaledTime - listedAt < RefreshSeconds) return; // once in 10 seconds: EOS allows only so many searches
        listing = true;
        if (screen == "serverjoin") ShowServerJoin();
        var (found, error) = await TitleFlow.ListServers();
        listing = false;
        listed = found; listError = error; listedAt = Time.unscaledTime;
        if (this && screen == "serverjoin") ShowServerJoin();
    }

    async void RunJoinCode(string typed)
    {
        if (ServerSettings.NormalizeCode(typed) == null) { ShowError("bad_code", null, ShowServerJoin); return; }
        ShowBusy("서버를 찾는 중…");
        var (entry, error) = await TitleFlow.FindCode(typed);
        if (!this) return;
        if (error != null) ShowError(error, error == "bad_code" ? null : (Action)(() => RunJoinCode(typed)), ShowServerJoin);
        else PickListed(entry);
    }

    // A listed (or found) server: its password first when it has one, then EOS P2P.
    void PickListed(EosSession.Entry entry)
    {
        if (!entry.hasPassword) { RunJoin(() => TitleFlow.JoinListed(entry, ""), ShowServerJoin); return; }
        Begin("joinpassword", true, 700, ShowServerJoin);
        var panel = Panel("비밀번호", entry.name + " · " + entry.players + "/" + entry.maxPlayers + "명");
        var input = TitleUi.Input(panel, "비밀번호", ServerSettings.PasswordMax);
        input.contentType = TMP_InputField.ContentType.Password;
        Action submit = () => { string password = input.text; RunJoin(() => TitleFlow.JoinListed(entry, password), () => PickListed(entry)); };
        input.onSubmit.AddListener(_ => submit());
        TitleUi.Button(panel, "접속", () => submit(), 64, true, 30);
        TitleUi.Button(panel, "뒤로", ShowServerJoin, 56, false, 26);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(input.gameObject);
        input.ActivateInputField();
    }

    // A wrong password gets no retry button: 뒤로 goes back to the form to type it again.
    async void RunJoin(Func<Awaitable<string>> join, Action from)
    {
        ShowBusy("서버에 접속하는 중…");
        string error = await join();
        if (!this) return;
        if (error != null) ShowError(error, error == "Password" ? null : (Action)(() => RunJoin(join, from)), from);
        else AfterConnect(from);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- name sign-up ----

    // shownError/name/gender: the server refused the name (taken in this world), shown again with what was typed.
    void ShowName(string sub, Action<string, string> next, Action onBack, string shownError = null, string typed = null, string chosenGender = null)
    {
        Begin("name", true, 700, onBack);
        var panel = Panel("캐릭터 만들기", sub);
        var input = TitleUi.Input(panel, "이름 (한글·영문·숫자·_ 2~12자)", 12, null);
        input.text = typed ?? "";
        string gender = chosenGender == "female" ? "female" : "male";
        var row = TitleUi.Row(panel, 60);
        var male = TitleUi.Button(row, gender == "male" ? "● 남자" : "○ 남자", null, 60, true, 28);
        var female = TitleUi.Button(row, gender == "female" ? "● 여자" : "○ 여자", null, 60, false, 28);
        male.onClick.AddListener(() => { gender = "male"; SetLabel(male, "● 남자"); SetLabel(female, "○ 여자"); });
        female.onClick.AddListener(() => { gender = "female"; SetLabel(male, "○ 남자"); SetLabel(female, "● 여자"); });
        var error = TitleUi.Label(panel, shownError ?? "", 24, TitleUi.Warn);
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

    // ---- busy / error ----

    void ShowBusy(string message)
    {
        Begin("busy", true, 700, null);
        busy = true;
        var panel = Panel(message);
        TitleUi.Label(panel, "잠시만 기다려 주세요.", 24, TitleUi.Muted);
    }

    void ShowError(string code, Action retry, Action onBack)
    {
        Begin("error", true, 760, onBack);
        var panel = Panel("문제가 생겼어요");
        TitleUi.Label(panel, TitleFlow.Message(code), 27, TitleUi.Warn);
        TitleUi.Spacer(panel, 6);
        if (retry != null) TitleUi.Button(panel, "다시 시도", retry, 64, true, 30);
        TitleUi.Button(panel, "뒤로", () => onBack?.Invoke(), 56, false, 26);
        Focus();
    }

    // ---- 설정 ----

    public void ShowSettings(Action onBack = null)
    {
        Action leave = onBack ?? ShowMain;
        Begin("settings", false, 860, leave);
        var panel = Panel("설정");

        TitleUi.Label(panel, "캐릭터는 월드마다 따로 만듭니다. 서버에 처음 들어갈 때 이름과 성별을 정합니다.", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);

        TitleUi.Label(panel, "프로필 슬롯 (한 PC에서 게임 2개를 켜는 테스트용)", 24, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        var slotRow = TitleUi.Row(panel, 56, 12, false);
        var prev = TitleUi.Button(slotRow, "◀", () => SwitchSlot(ProfileSlot.Slot - 1, onBack), 56, false, 26);
        var slotLabel = TitleUi.Label(slotRow, "슬롯 " + ProfileSlot.Slot + " / " + ProfileSlot.Max, 28, TitleUi.Cream);
        var next = TitleUi.Button(slotRow, "▶", () => SwitchSlot(ProfileSlot.Slot + 1, onBack), 56, false, 26);
        TitleUi.Width(prev, 90); TitleUi.Width(next, 90); TitleUi.Width(slotLabel, 0, 1);
        prev.interactable = next.interactable = !ProfileSlot.FromCommandLine;
        TitleUi.Label(panel, ProfileSlot.FromCommandLine
            ? "실행 인자 -profile " + ProfileSlot.Slot + " 로 고정되어 있습니다."
            : "슬롯마다 월드 저장 폴더가 따로입니다. 두 번째 창은 다른 슬롯(또는 -profile 2)으로 실행하세요. 계정(EOS)은 한 PC에 하나입니다.", 22, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);

        string eos = EosAccount.State == EosAccount.Status.Ready ? "연결됨 (계정 " + EosAccount.LocalPuid.Substring(0, 4) + "…)"
            : EosAccount.State == EosAccount.Status.LoggingIn ? "연결하는 중…" : "연결 안 됨 (" + EosAccount.Problem + ")";
        TitleUi.Label(panel, "EOS: " + eos, 22, TitleUi.Muted, TextAlignmentOptions.MidlineLeft);
        TitleUi.Button(panel, "뒤로", () => leave(), 56, false, 26);
        Focus();
    }

    void SwitchSlot(int slot, Action onBack)
    {
        if (slot < 1 || slot > ProfileSlot.Max) return;
        TitleFlow.ChangeSlot(slot);
        ShowSettings(onBack);
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
            case "worlds": ShowSolo(); break;
            case "serveropen": ShowServerOpen(); break;
            case "serverjoin": ShowServerJoin(); break;
            case "settings": ShowSettings(); break;
            case "name": ShowName(ServerCharacterHint, (n, g) => { }, ShowMain); break;
            case "error": ShowError("server_unreachable", () => { }, ShowMain); break;
            case "busy": ShowBusy("서버에 접속하는 중…"); break;
        }
    }

    public TMP_InputField FirstInput() => content.GetComponentInChildren<TMP_InputField>();
}
