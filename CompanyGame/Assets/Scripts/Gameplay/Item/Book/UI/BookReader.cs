using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>One shared book editor on the persistent player, bound to the selected physical book.</summary>
[DefaultExecutionOrder(-340)]
[DisallowMultipleComponent]
public sealed class BookReader : MonoBehaviour
{
    public static bool IsAnyOpen { get; private set; }
    readonly ControlLock controls = new ControlLock();
    PlayerMovement movement;
    PlayerInventory playerInventory;
    PlayerHeldItem held;
    ItemStack current;
    BookInstanceData draft;
    BookEditSession editSession;
    string writerId, writerName;
    int sourceSlot;
    bool ownsControls;
    bool titlePromptOpen, permissionsOpen;
    static int dismissedFrame = -1;
    public static bool BlocksInventoryInput => IsAnyOpen || dismissedFrame == Time.frameCount;
    GameObject canvasRoot, titleModal, permissionsModal;
    RectTransform bookFrame, titlePage;
    TMP_InputField titleInput, leftInput, rightInput, firstTitleInput;
    TMP_Text counter, authorLabel, titleError, permissionHeading, hint;
    int pageStart;
    EventSystem navigationOwner;
    bool previousNavigationEvents;
    bool changingPages;
    RectTransform permissionAnchor;
    UnityEngine.UI.Image backdrop;
    readonly Vector3[] permissionCorners = new Vector3[4];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { IsAnyOpen = false; dismissedFrame = -1; }

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        playerInventory = GetComponent<PlayerInventory>();
        held = GetComponent<PlayerHeldItem>();
    }

    void Update()
    {
        if (IsAnyOpen)
        {
            if (SceneLoadManager.IsLoading || (current != null && current.IsUniqueBook && playerInventory.Inventory.FindBook(current.InstanceId) == null)) { ForceClose(); return; }
            if (permissionsOpen)
            {
                if (GameInput.CancelPressed || !playerInventory.IsOpen) ForceClose();
                return;
            }
            if (GameInput.CancelPressed)
            {
                if (titlePromptOpen) { titlePromptOpen = false; titleModal.SetActive(false); SetPagesEnabled(true); }
                else RequestClose();
                return;
            }
            if (titlePromptOpen) return;
            if (GameInput.NavLeftPressed) Turn(-2);
            else if (GameInput.NavRightPressed) Turn(2);
            return;
        }
        if (!GameInput.InteractPressed || PlayerInventory.SpaceConsumedThisFrame ||
            !movement || !movement.isActiveAndEnabled || !playerInventory || DialogueManager.HasNearbyNpc ||
            PlayerInventory.IsAnyOpen || ChatUIManager.IsChatting || DialogueManager.OwnsInput ||
            UIEventSystem.IsEditingText() || SceneLoadManager.IsLoading ||
            (PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) ||
            (PlayerSeating.Local && PlayerSeating.Local.IsSeated)) return;
        var inventory = playerInventory.Inventory;
        var stack = inventory?.GetSlot(inventory.SelectedHotbarIndex);
        if (stack == null || stack.IsEmpty || !stack.Item.IsBook || !held || held.HeldItem != stack.Item) return;
        Open(stack, inventory.SelectedHotbarIndex);
    }

    void Open(ItemStack stack, int slot)
    {
        UIEventSystem.Ensure();
        if (!canvasRoot) Build();
        current = stack; sourceSlot = slot;
        draft = stack.BookData?.Clone() ?? new BookInstanceData().Clone();
        editSession = draft.isPublished ? playerInventory.Inventory.BeginBookEdit(stack.InstanceId, GameSession.LocalPlayerId) : null;
        writerId = writerName = null;
        pageStart = 0;
        PlayerInventory.ConsumeSpaceThisFrame();
        controls.Hold(movement); ownsControls = true;
        OwnNavigation();
        IsAnyOpen = true;
        canvasRoot.SetActive(true); bookFrame.gameObject.SetActive(true);
        backdrop.color = new Color(0f, 0f, 0f, .65f);
        titleModal.SetActive(false); permissionsModal.SetActive(false);
        Refresh();
        StartCoroutine(FocusPageNextFrame(0, 0));
    }

    void RequestClose()
    {
        if (draft == null) { ForceClose(); return; }
        if (draft.isPublished || !draft.HasContent) { ForceClose(); return; }
        titlePromptOpen = true;
        SetPagesEnabled(false);
        firstTitleInput.SetTextWithoutNotify(draft.title);
        titleError.text = "";
        titleModal.SetActive(true);
        // Esc opened this popup. Wait a frame so that same Esc cannot cancel its input field.
        StartCoroutine(FocusTitleNextFrame());
    }

    System.Collections.IEnumerator FocusTitleNextFrame()
    {
        yield return null;
        if (!titlePromptOpen) yield break;
        firstTitleInput.Select(); firstTitleInput.ActivateInputField();
    }

    void Publish()
    {
        if (!titlePromptOpen || draft == null) return;
        draft.title = firstTitleInput.text.Trim();
        if (playerInventory.Inventory.TryPublishBook(sourceSlot, draft, writerId ?? GameSession.LocalPlayerId,
            writerName ?? GameSession.LocalPlayerName, out int result, out string error))
        {
            if (result < InventoryState.HotbarSize) playerInventory.Inventory.SelectHotbar(result);
            ForceClose();
        }
        else { titleError.text = error; StartCoroutine(FocusTitleNextFrame()); }
    }

    void ForceClose()
    {
        if (!IsAnyOpen) return;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        IsAnyOpen = false; titlePromptOpen = permissionsOpen = false;
        dismissedFrame = Time.frameCount;
        if (canvasRoot) canvasRoot.SetActive(false);
        current = null; draft = null; editSession = null;
        permissionAnchor = null;
        if (navigationOwner) navigationOwner.sendNavigationEvents = previousNavigationEvents;
        navigationOwner = null;
        SetPagesEnabled(true);
        if (ownsControls) controls.Release(movement, !SceneLoadManager.IsLoading);
        ownsControls = false;
    }

    void OnDisable() => ForceClose();

    void SetPagesEnabled(bool enabled)
    {
        if (!bookFrame) return;
        var group = bookFrame.GetComponent<CanvasGroup>();
        if (!group) group = bookFrame.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = enabled;
    }

    public void OpenPermissions(int slot, RectTransform anchor)
    {
        var stack = playerInventory.Inventory.GetSlot(slot);
        if (!anchor || IsAnyOpen || playerInventory.IsDragging || !playerInventory.IsOpen || stack == null || !stack.IsUniqueBook ||
            !stack.BookData.isPublished || stack.BookData.IsLibraryLoan || !stack.BookData.IsAuthor(GameSession.LocalPlayerId)) return;
        UIEventSystem.Ensure(); if (!canvasRoot) Build();
        current = stack; permissionsOpen = true; IsAnyOpen = true;
        permissionAnchor = anchor;
        OwnNavigation();
        playerInventory.UserInterface.HideItemTooltip();
        canvasRoot.SetActive(true); bookFrame.gameObject.SetActive(false);
        titleModal.SetActive(false); permissionsModal.SetActive(true);
        backdrop.color = Color.clear;
        permissionHeading.text = "다른 플레이어 권한\n현재: " + PermissionName(stack.BookData.permission);
        Canvas.ForceUpdateCanvases();
        PositionPermissions();
    }

    void PositionPermissions()
    {
        if (!permissionAnchor) { ForceClose(); return; }
        var sourceCanvas = permissionAnchor.GetComponentInParent<Canvas>();
        var sourceCamera = sourceCanvas && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? sourceCanvas.worldCamera : null;
        var canvasRect = (RectTransform)canvasRoot.transform;
        permissionAnchor.GetWorldCorners(permissionCorners);
        Vector2 topLeft, topRight;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,
            RectTransformUtility.WorldToScreenPoint(sourceCamera, permissionCorners[1]), null, out topLeft);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,
            RectTransformUtility.WorldToScreenPoint(sourceCamera, permissionCorners[2]), null, out topRight);
        var popup = (RectTransform)permissionsModal.transform;
        // Storage slots are 106px with 16px horizontal / 10px vertical gaps.
        // Match the clicked slot's actual scale across inventory resizing and canvases.
        float scale = Vector2.Distance(topLeft, topRight) / 106f;
        var canvasBounds = canvasRect.rect;
        scale = Mathf.Min(scale, (canvasBounds.width - 16f) / 350f, (canvasBounds.height - 16f) / 222f);
        popup.localScale = Vector3.one * scale;
        Vector2 half = popup.sizeDelta * scale * .5f;
        Vector2 position = (topLeft + topRight) * .5f + Vector2.up * (half.y + 8f);
        position.x = Mathf.Clamp(position.x, canvasBounds.xMin + half.x + 8f, canvasBounds.xMax - half.x - 8f);
        position.y = Mathf.Clamp(position.y, canvasBounds.yMin + half.y + 8f, canvasBounds.yMax - half.y - 8f);
        popup.anchoredPosition = position;
    }

    static string PermissionName(BookEditPermission permission) => permission == BookEditPermission.AddOnly ? "추가만 가능" :
        permission == BookEditPermission.ReadOnly ? "읽기만 가능" : "수정 가능";

    void SetPermission(BookEditPermission permission)
    {
        if (current != null && playerInventory.Inventory.TrySetBookPermission(current.InstanceId, permission,
            GameSession.LocalPlayerId, out _)) ForceClose();
    }

    void Turn(int delta)
    {
        if (titlePromptOpen || permissionsOpen || draft == null || IsComposing()) return;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        int next = Mathf.Clamp(pageStart + delta, draft.isPublished ? -2 : 0, BookInstanceData.PageCount - 2);
        if (next == pageStart) return;
        pageStart = next; Refresh();
        StartCoroutine(FocusPageNextFrame(pageStart, 0));
    }

    void OwnNavigation()
    {
        navigationOwner = EventSystem.current;
        if (!navigationOwner) return;
        previousNavigationEvents = navigationOwner.sendNavigationEvents;
        // TMP still receives UpdateSelected and text input. Prevent Enter from
        // submitting a selected page-arrow button and arrows from navigating UI widgets.
        navigationOwner.sendNavigationEvents = false;
    }

    static bool IsComposing() => EventSystem.current && EventSystem.current.currentInputModule &&
        !string.IsNullOrEmpty(EventSystem.current.currentInputModule.input.compositionString);

    System.Collections.IEnumerator FocusPageNextFrame(int page, int caret)
    {
        yield return null;
        if (!IsAnyOpen || titlePromptOpen || permissionsOpen || draft == null) yield break;
        if (page < 0)
        {
            if (pageStart >= 0) yield break;
            titleInput.Select(); titleInput.ActivateInputField(); yield break;
        }
        if (page < pageStart || page > pageStart + 1) yield break;
        var field = page == pageStart ? leftInput : rightInput;
        field.Select(); field.ActivateInputField();
        field.selectionStringAnchorPosition = field.selectionStringFocusPosition = Mathf.Clamp(caret, 0, field.text.Length);
    }

    void LateUpdate()
    {
        if (permissionsOpen) { PositionPermissions(); return; }
        if (!IsAnyOpen || titlePromptOpen || permissionsOpen || draft == null || pageStart < 0 || IsComposing()) return;
        // Commit completed IME input after composition ends, before deciding whether a page is full.
        if (leftInput.isFocused && leftInput.text != draft.pages[pageStart]) SavePage(false, leftInput.text);
        else if (rightInput.isFocused && rightInput.text != draft.pages[pageStart + 1]) SavePage(true, rightInput.text);
    }

    void Refresh()
    {
        if (draft == null) return;
        bool cover = pageStart < 0;
        titlePage.gameObject.SetActive(cover);
        leftInput.gameObject.SetActive(!cover); rightInput.gameObject.SetActive(!cover);
        titleInput.SetTextWithoutNotify(draft.title ?? "");
        titleInput.readOnly = !draft.CanRename(GameSession.LocalPlayerId);
        authorLabel.text = "저자 : " + draft.authorName;
        if (!cover)
        {
            leftInput.SetTextWithoutNotify(draft.pages[pageStart] ?? "");
            rightInput.SetTextWithoutNotify(draft.pages[pageStart + 1] ?? "");
        }
        leftInput.readOnly = rightInput.readOnly = !draft.CanEdit(GameSession.LocalPlayerId);
        counter.text = cover ? "제목" : (pageStart + 1) + "/" + BookInstanceData.PageCount;
        hint.text = !draft.isPublished ? "내용을 입력한 뒤 닫으면 제목을 저장합니다." :
            draft.IsLibraryLoan ? "도서관 대여본 · 읽기 전용 · 도서관에서 반납" :
            draft.IsAuthor(GameSession.LocalPlayerId) ? "자동 저장 · 첫 페이지에서 ← 제목 변경" :
            PermissionName(draft.permission) + (draft.permission == BookEditPermission.AddOnly ? " · 기존 글자는 지울 수 없습니다." : "");
        if (!draft.isPublished && current.Count > 1)
        {
            bool room = false;
            for (int i = 0; i < playerInventory.Inventory.Capacity; i++)
                if (playerInventory.Inventory.GetSlot(i).IsEmpty) { room = true; break; }
            if (!room)
            {
                leftInput.readOnly = rightInput.readOnly = true;
                hint.text = "작성한 책을 분리할 인벤토리 한 칸을 비워 주세요.";
            }
        }
    }

    void SaveTitle(string value)
    {
        if (draft == null || !draft.isPublished || !draft.CanRename(GameSession.LocalPlayerId)) return;
        var revision = draft.Clone(); revision.title = value.Trim();
        if (playerInventory.Inventory.TryUpdateBook(current.InstanceId, revision, GameSession.LocalPlayerId, out _, editSession))
            draft = playerInventory.Inventory.FindBook(current.InstanceId).BookData.Clone();
        titleInput.SetTextWithoutNotify(draft.title);
    }

    void SavePage(bool right, string value)
    {
        if (draft == null || pageStart < 0 || changingPages || IsComposing()) return;
        if ((right ? rightInput : leftInput).readOnly) return;
        int page = pageStart + (right ? 1 : 0);
        var input = right ? rightInput : leftInput;
        var revision = draft.Clone(); revision.pages[page] = value;
        int focusPage = page;
        int caret = Mathf.Clamp(input.stringPosition, 0, value.Length);
        bool flowed = false;
        for (int i = page; i < BookInstanceData.PageCount; i++)
        {
            string text = revision.pages[i];
            int fits = FittingPrefix(input, text);
            if (fits >= text.Length) break;
            if (i == BookInstanceData.PageCount - 1 || fits == 0)
            {
                input.SetTextWithoutNotify(draft.pages[page]);
                hint.text = "20페이지를 모두 채웠습니다. 기존 내용을 줄여 주세요.";
                return;
            }
            revision.pages[i] = text.Substring(0, fits);
            revision.pages[i + 1] = text.Substring(fits) + revision.pages[i + 1];
            if (focusPage == i && caret >= fits) { focusPage++; caret -= fits; }
            flowed = true;
        }
        if (draft.isPublished)
        {
            if (playerInventory.Inventory.TryUpdateBook(current.InstanceId, revision, GameSession.LocalPlayerId, out _, editSession))
                draft = playerInventory.Inventory.FindBook(current.InstanceId).BookData.Clone();
            else { input.SetTextWithoutNotify(draft.pages[page]); return; }
        }
        else
        {
            // Do not consume a blank or create an author merely by opening it.
            draft = revision;
            if (draft.HasContent && writerId == null)
            { writerId = GameSession.LocalPlayerId; writerName = GameSession.LocalPlayerName; }
        }
        if (flowed)
        {
            changingPages = true;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            pageStart = focusPage / 2 * 2;
            Refresh();
            changingPages = false;
            StartCoroutine(FocusPageNextFrame(focusPage, caret));
        }
    }

    static int FittingPrefix(TMP_InputField field, string text)
    {
        float width = field.textViewport.rect.width;
        float height = field.textViewport.rect.height;
        if (width <= 0 || height <= 0 || string.IsNullOrEmpty(text)) return text.Length;
        bool Fits(string candidate) => field.textComponent.GetPreferredValues(candidate, width, Mathf.Infinity).y <= height + .1f;
        if (Fits(text)) return text.Length;
        // Text elements avoid cutting a surrogate pair or a combining character in half.
        int[] boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        int low = 0, high = boundaries.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            int end = middle == boundaries.Length ? text.Length : boundaries[middle];
            if (Fits(text.Substring(0, end))) low = middle; else high = middle - 1;
        }
        return low == boundaries.Length ? text.Length : boundaries[low];
    }

    void Build()
    {
        var root = new GameObject("BookReaderCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        root.transform.SetParent(transform, false);
        canvasRoot = root;
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 400;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
        backdrop = Panel("Backdrop", root.transform, Vector2.zero, new Color(0f, 0f, 0f, .65f), true).GetComponent<UnityEngine.UI.Image>();
        var dismissPermissions = backdrop.gameObject.AddComponent<UnityEngine.UI.Button>();
        dismissPermissions.transition = UnityEngine.UI.Selectable.Transition.None;
        dismissPermissions.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        dismissPermissions.onClick.AddListener(() => { if (permissionsOpen) ForceClose(); });
        bookFrame = Panel("BookFrame", root.transform, new Vector2(960, 620), new Color(.32f, .15f, .075f, 1f));
        var gold = new Color(.78f, .59f, .29f, 1f);
        foreach (float y in new[] { -299f, 299f })
            PanelAt("GoldHorizontal", bookFrame, new Vector2(934, 2), new Vector2(0, y), gold).GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        foreach (float x in new[] { -469f, 469f })
            PanelAt("GoldVertical", bookFrame, new Vector2(2, 598), new Vector2(x, 0), gold).GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        foreach (float x in new[] { -462f, 462f })
        foreach (float y in new[] { -292f, 292f })
        {
            var corner = PanelAt("GoldCorner", bookFrame, new Vector2(8, 8), new Vector2(x, y), gold);
            corner.localRotation = Quaternion.Euler(0, 0, 45);
            corner.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        }
        PanelAt("LeftPage", bookFrame, new Vector2(450, 568), new Vector2(-229, 0), new Color(.975f, .952f, .892f));
        PanelAt("RightPage", bookFrame, new Vector2(450, 568), new Vector2(229, 0), new Color(.99f, .973f, .921f));
        PanelAt("SpineShade", bookFrame, new Vector2(16, 568), Vector2.zero, new Color(.40f, .29f, .18f, .42f));
        titlePage = PanelAt("TitlePage", bookFrame, new Vector2(410, 410), new Vector2(229, 0), new Color(.99f, .973f, .921f));
        Label("TitleLabel", titlePage, new Vector2(370, 50), new Vector2(0, 118), 30).text = "제목";
        titleInput = Input("Title", titlePage, new Vector2(370, 90), new Vector2(0, 25), "제목", 27, false);
        titleInput.GetComponent<UnityEngine.UI.Image>().color = new Color(.85f, .78f, .63f, .5f);
        authorLabel = Label("Author", titlePage, new Vector2(370, 70), new Vector2(0, -75), 22);
        leftInput = Input("LeftPageText", bookFrame, new Vector2(394, 444), new Vector2(-229, -20), "", 21, true);
        rightInput = Input("RightPageText", bookFrame, new Vector2(394, 444), new Vector2(229, -20), "", 21, true);
        titleInput.onEndEdit.AddListener(SaveTitle);
        leftInput.onValueChanged.AddListener(value => SavePage(false, value));
        rightInput.onValueChanged.AddListener(value => SavePage(true, value));
        counter = Label("PageCounter", bookFrame, new Vector2(100, 34), new Vector2(381, 257), 21);
        counter.alignment = TextAlignmentOptions.Right;
        Button("PreviousPage", bookFrame, new Vector2(62, 46), new Vector2(-416, -255), "←", () => Turn(-2));
        Button("NextPage", bookFrame, new Vector2(62, 46), new Vector2(416, -255), "→", () => Turn(2));
        Button("CloseBook", bookFrame, new Vector2(70, 40), new Vector2(-415, 256), "닫기", RequestClose);
        hint = Label("BookHint", bookFrame, new Vector2(710, 38), new Vector2(0, -255), 17);
        titleModal = Panel("TitleModal", root.transform, new Vector2(680, 350), new Color(.98f, .95f, .87f)).gameObject;
        Label("TitleLabel", titleModal.transform, new Vector2(600, 55), new Vector2(0, 117), 28).text = "제목";
        firstTitleInput = Input("FirstTitle", titleModal.transform, new Vector2(570, 65), new Vector2(0, 40), "책 제목을 입력하세요", 27, false);
        firstTitleInput.GetComponent<UnityEngine.UI.Image>().color = new Color(.86f, .80f, .69f);
        firstTitleInput.onSubmit.AddListener(_ => Publish());
        titleError = Label("Error", titleModal.transform, new Vector2(610, 58), new Vector2(0, -30), 18);
        Button("ConfirmTitle", titleModal.transform, new Vector2(180, 52), new Vector2(0, -110), "확인", Publish);
        permissionsModal = Panel("BookPermissions", root.transform, new Vector2(350, 222), new Color(.98f, .95f, .87f)).gameObject;
        permissionHeading = Label("Heading", permissionsModal.transform, new Vector2(270, 46), new Vector2(0, 73), 17);
        Button("FullEdit", permissionsModal.transform, new Vector2(314, 36), new Vector2(0, 25), "수정 가능", () => SetPermission(BookEditPermission.FullEdit), 19);
        Button("AddOnly", permissionsModal.transform, new Vector2(314, 36), new Vector2(0, -21), "추가만 가능", () => SetPermission(BookEditPermission.AddOnly), 19);
        Button("ReadOnly", permissionsModal.transform, new Vector2(314, 36), new Vector2(0, -67), "읽기만 가능", () => SetPermission(BookEditPermission.ReadOnly), 19);
        Button("ClosePermissions", permissionsModal.transform, new Vector2(25, 25), new Vector2(154, 91), "×", ForceClose, 20);
        root.SetActive(false);
    }

    static RectTransform Panel(string name, Transform parent, Vector2 size, Color color, bool stretch = false)
    {
        var rect = UIBuild.Rect(name, parent, size);
        if (stretch) UIBuild.Stretch(rect);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color; image.raycastTarget = true;
        return rect;
    }

    static RectTransform PanelAt(string name, Transform parent, Vector2 size, Vector2 position, Color color)
    {
        var rect = Panel(name, parent, size, color); rect.anchoredPosition = position; return rect;
    }

    TMP_Text Label(string name, Transform parent, Vector2 size, Vector2 position, float fontSize)
    {
        var rect = UIBuild.Rect(name, parent, size); rect.anchoredPosition = position;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = playerInventory.uiFont ? playerInventory.uiFont : TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize; text.color = new Color(.20f, .12f, .07f);
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; text.richText = false;
        return text;
    }

    TMP_InputField Input(string name, Transform parent, Vector2 size, Vector2 position,
        string hint, float fontSize, bool multiline)
    {
        var frame = PanelAt(name, parent, size, position, new Color(1f, 1f, 1f, .06f));
        var viewport = UIBuild.Rect("Viewport", frame, Vector2.zero); UIBuild.Stretch(viewport);
        viewport.offsetMin = new Vector2(10, 8); viewport.offsetMax = new Vector2(-10, -8);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var value = Label("Text", viewport, Vector2.zero, Vector2.zero, fontSize);
        UIBuild.Stretch(value.rectTransform); value.alignment = TextAlignmentOptions.TopLeft;
        value.textWrappingMode = TextWrappingModes.Normal;
        var placeholder = Label("Placeholder", viewport, Vector2.zero, Vector2.zero, fontSize);
        UIBuild.Stretch(placeholder.rectTransform); placeholder.alignment = TextAlignmentOptions.TopLeft;
        placeholder.text = hint; placeholder.color = new Color(.45f, .38f, .30f, .7f);
        var field = frame.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = viewport; field.textComponent = (TextMeshProUGUI)value;
        field.placeholder = placeholder; field.targetGraphic = frame.GetComponent<UnityEngine.UI.Image>();
        field.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        field.characterLimit = multiline ? 80000 : 80; field.richText = false; field.restoreOriginalTextOnEscape = false;
        field.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        return field;
    }

    void Button(string name, Transform parent, Vector2 size, Vector2 position, string caption, UnityEngine.Events.UnityAction action, float fontSize = 25)
    {
        var frame = PanelAt(name, parent, size, position, new Color(.42f, .25f, .12f, .88f));
        var button = frame.gameObject.AddComponent<UnityEngine.UI.Button>(); button.onClick.AddListener(action);
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var text = Label("Label", frame, Vector2.zero, Vector2.zero, fontSize);
        UIBuild.Stretch(text.rectTransform); text.text = caption; text.color = new Color(1f, .96f, .84f);
    }
}
