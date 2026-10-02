using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// <summary>Square inventory slots, separated outfit/pet equipment, and account withdrawals.</summary>
public sealed class InventoryUI : MonoBehaviour
{
    static readonly Color WindowColor = new Color(.985f, .98f, .96f, .94f);
    static readonly Color SurfaceColor = new Color(1f, .985f, .96f, .61f);
    static readonly Color SlotColor = new Color(.96f, .935f, .895f, .74f);
    static readonly Color Ink = new Color(.29f, .205f, .19f);
    static readonly Color Muted = new Color(.54f, .46f, .425f);
    static readonly Color Accent = new Color(.73f, .47f, .28f);
    static readonly EquipmentSlot[] ClothingSlots =
        { EquipmentSlot.Top, EquipmentSlot.Bottom, EquipmentSlot.Socks, EquipmentSlot.Shoes };
    readonly List<SlotView> storage = new List<SlotView>();
    readonly List<SlotView> hotbar = new List<SlotView>();
    readonly Dictionary<EquipmentSlot, SlotView> equipment = new Dictionary<EquipmentSlot, SlotView>();
    PlayerInventory owner;
    TMP_FontAsset font;
    GameObject modal;
    RectTransform window, grid, storageCard, withdrawalWindow, bankButton;
    readonly Vector3[] bankCorners = new Vector3[4];
    TMP_Text capacityLabel, bankLabel;
    InventoryCharacterPreview preview;
    RectTransform dragVisual;
    UnityEngine.UI.Image dragIcon;
    TMP_Text dragCount;
    GameObject withdrawalModal;
    TMP_InputField denominationInput, quantityInput;
    TMP_Text withdrawalError;
    UnityEngine.UI.Button withdrawButton;
    bool withdrawing;
    RectTransform tooltipPanel;
    TMP_Text tooltipText;
    int submittedFrame = -1;
    long lastBank = long.MinValue;
    float windowHeight = 750f;

    sealed class SlotView
    {
        public InventoryRoundedGraphic background;
        public UnityEngine.UI.Image icon;
        public TMP_Text count, number, emptyLabel;
        public UnityEngine.UI.Button button;
    }

    public bool IsOpen => modal && modal.activeSelf;
    public bool IsWithdrawalOpen => withdrawalModal && withdrawalModal.activeSelf;
    public int StorageSlotCount => storage.Count;
    public int HotbarSlotCount => hotbar.Count;
    public int EquipmentSlotCount => equipment.Count;
    public InventoryCharacterPreview CharacterPreview => preview;
    public RectTransform WindowRect => window;
    public RectTransform StorageGrid => grid;
    public TMP_InputField WithdrawalAmountInput => denominationInput;
    public TMP_InputField WithdrawalQuantityInput => quantityInput;
    public TMP_Text WithdrawalErrorText => withdrawalError;

    public static InventoryUI Create(PlayerInventory owner, TMP_FontAsset font)
    {
        var host = new GameObject("InventoryCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        var ui = host.AddComponent<InventoryUI>();
        ui.owner = owner;
        ui.font = font ? font : TMP_Settings.defaultFontAsset;
        ui.Build();
        owner.UiChanged += ui.Refresh;
        ui.Refresh();
        return ui;
    }

    void Build()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 160;
        var scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = .5f;
        EnsureEventSystem();
        BuildHotbar();
        var shade = Panel("InventoryModal", transform, Vector2.zero, new Color(.16f, .13f, .13f, .30f), 0f, 0f);
        Stretch(shade);
        modal = shade.gameObject;
        window = Panel("InventoryWindow", shade, new Vector2(1100f, windowHeight), WindowColor, 64f, 27f);
        window.GetComponent<InventoryRoundedGraphic>().raycastTarget = true;
        var title = Label("Title", window, "인벤토리", 36f, Ink, new Vector2(310f, 54f));
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        AtTop(title.rectTransform, -331f, -44f);
        AtTop(Icon("BagIcon", window, InventoryGlyphGraphic.Glyph.Bag, new Vector2(43f, 48f), Accent), -504f, -44f);
        var bank = Panel("BankBalanceButton", window, new Vector2(285f, 51f), new Color(1f, .99f, .96f, .85f), 24f, 12f);
        bankButton = bank;
        AtTop(bank, 218f, -38f);
        MakeButton(bank, OpenWithdrawal);
        var coin = Icon("BankCoin", bank, InventoryGlyphGraphic.Glyph.Coin, new Vector2(30f, 30f), new Color(.94f, .65f, .18f));
        coin.anchoredPosition = new Vector2(-113f, 0f);
        bankLabel = Label("BankBalance", bank, "통장  0원", 23f, Ink, new Vector2(218f, 43f));
        bankLabel.fontStyle = FontStyles.Bold;
        AutoSize(bankLabel, 12f, 23f);
        bankLabel.rectTransform.anchoredPosition = new Vector2(20f, 0f);
        var close = Panel("CloseInventory", window, new Vector2(127f, 51f), new Color(1f, .99f, .96f, .85f), 22f, 10f);
        AtTop(close, 448f, -38f);
        MakeButton(close, owner.CloseInventory);
        Label("CloseLabel", close, "×  닫기", 24f, Ink, new Vector2(117f, 42f));
        BuildPortrait(); BuildEquipment(); BuildPet();
        storageCard = Panel("Storage", window, new Vector2(1036f, 274f), new Color(1f, 1f, 1f, .37f), 34f, 17f);
        storageCard.anchorMin = storageCard.anchorMax = new Vector2(.5f, 1f);
        storageCard.pivot = new Vector2(.5f, 1f);
        storageCard.anchoredPosition = new Vector2(0f, -438f);
        var storageTitle = Label("StorageHeading", storageCard, "소지품", 24f, Ink, new Vector2(210f, 38f));
        storageTitle.fontStyle = FontStyles.Bold;
        storageTitle.alignment = TextAlignmentOptions.MidlineLeft;
        AtTop(storageTitle.rectTransform, -384f, -24f);
        capacityLabel = Label("Capacity", storageCard, "0 / 16", 24f, Ink, new Vector2(155f, 38f));
        capacityLabel.alignment = TextAlignmentOptions.MidlineRight;
        AtTop(capacityLabel.rectTransform, 411f, -24f);
        grid = Rect("StorageSlots", storageCard, new Vector2(976f, 222f));
        grid.anchorMin = grid.anchorMax = new Vector2(.5f, 1f);
        grid.pivot = new Vector2(.5f, 1f);
        grid.anchoredPosition = new Vector2(0f, -48f);
        var layout = grid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        layout.cellSize = new Vector2(106f, 106f);
        layout.spacing = new Vector2(16f, 10f);
        layout.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 8;
        layout.childAlignment = TextAnchor.UpperCenter;
        BuildWithdrawal(); BuildDragVisual(); BuildTooltip();
        modal.SetActive(false);
    }

    void BuildHotbar()
    {
        var bar = Panel("QuickSlots", transform, new Vector2(618f, 78f), new Color(.985f, .976f, .945f, .82f), 25f, 11f);
        bar.GetComponent<InventoryRoundedGraphic>().raycastTarget = true;
        bar.anchorMin = bar.anchorMax = new Vector2(.5f, 0f);
        bar.pivot = new Vector2(.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 12f);
        var row = Rect("Slots", bar, new Vector2(568f, 64f));
        row.anchoredPosition = new Vector2(-12f, 0f);
        var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        for (int i = 0; i < InventoryState.HotbarSize; i++)
        {
            int index = i;
            var slot = CreateSlot("QuickSlot_" + (i + 1), row, 64f, (i + 1).ToString(), () => owner.SelectHotbar(index));
            InventorySlotPointer.AttachStorage(slot.background.gameObject, owner, index);
            hotbar.Add(slot);
        }
        var key = Label("InventoryKey", bar, "E", 14f, Ink, new Vector2(24f, 28f));
        key.fontStyle = FontStyles.Bold;
        key.rectTransform.anchoredPosition = new Vector2(287f, 0f);
    }

    void BuildPortrait()
    {
        var card = Panel("CurrentPlayer", window, new Vector2(312f, 336f), SurfaceColor, 34f, 15f);
        AtTop(card, -362f, -257f);
        Heading(card, "플레이어", InventoryGlyphGraphic.Glyph.Person, -111f);
        var portraitRect = Rect("CharacterPortrait", card, new Vector2(235.556f, 265f));
        portraitRect.anchoredPosition = new Vector2(0f, -11f);
        var portraitImage = portraitRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        portraitImage.raycastTarget = false;
        portraitImage.color = Color.white;
        preview = gameObject.AddComponent<InventoryCharacterPreview>();
        preview.Initialize(owner.GetComponent<PlayerMovement>(), portraitImage);
    }

    void BuildEquipment()
    {
        var group = Panel("Clothing", window, new Vector2(374f, 336f), SurfaceColor, 34f, 15f);
        AtTop(group, -3f, -257f);
        Heading(group, "착용", InventoryGlyphGraphic.Glyph.Top, -142f);
        for (int i = 0; i < ClothingSlots.Length; i++)
        {
            EquipmentSlot slot = ClothingSlots[i];
            var view = CreateSlot("Equipment_" + slot, group, 128f, "", () => owner.ClickEquipmentSlot(slot));
            view.background.rectTransform.anchoredPosition = new Vector2(i % 2 == 0 ? -73f : 73f, 51f - i / 2 * 142f);
            view.emptyLabel.text = EquipmentName(slot);
            view.emptyLabel.fontSize = 18f;
            view.emptyLabel.rectTransform.anchoredPosition = new Vector2(0f, -43f);
            var glyph = Icon("EquipmentSilhouette", view.background.transform, (InventoryGlyphGraphic.Glyph)(i + 1), new Vector2(67f, 67f), new Color(.62f, .53f, .48f, .42f));
            glyph.anchoredPosition = new Vector2(0f, 9f);
            InventorySlotPointer.AttachEquipment(view.background.gameObject, owner, slot);
            equipment.Add(slot, view);
        }
    }

    void BuildPet()
    {
        var group = Panel("CompanionPet", window, new Vector2(318f, 336f), SurfaceColor, 34f, 15f);
        AtTop(group, 359f, -257f);
        Heading(group, "동행 펫", InventoryGlyphGraphic.Glyph.Pet, -113f);
        var view = CreateSlot("Equipment_Pet", group, 228f, "", () => owner.ClickEquipmentSlot(EquipmentSlot.Pet));
        view.background.rectTransform.anchoredPosition = new Vector2(0f, -4f);
        view.emptyLabel.text = "펫";
        view.emptyLabel.fontSize = 21f;
        view.emptyLabel.rectTransform.anchoredPosition = new Vector2(0f, -139f);
        Icon("EquipmentSilhouette", view.background.transform, InventoryGlyphGraphic.Glyph.Pet,
            new Vector2(92f, 92f), new Color(.62f, .53f, .48f, .27f));
        InventorySlotPointer.AttachEquipment(view.background.gameObject, owner, EquipmentSlot.Pet);
        equipment.Add(EquipmentSlot.Pet, view);
    }

    void Heading(RectTransform card, string text, InventoryGlyphGraphic.Glyph glyph, float left)
    {
        var icon = Icon("HeadingIcon", card, glyph, new Vector2(29f, 29f), Ink);
        icon.anchoredPosition = new Vector2(left, 140f);
        var label = Label("Caption", card, text, 23f, Ink, new Vector2(card.sizeDelta.x - 84f, 37f));
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.rectTransform.anchoredPosition = new Vector2(27f, 140f);
    }

    SlotView CreateSlot(string name, Transform parent, float size, string key, Action onClick)
    {
        var rect = Panel(name, parent, Vector2.one * size, SlotColor, size * .28f, size * .11f);
        var view = new SlotView { background = rect.GetComponent<InventoryRoundedGraphic>() };
        view.button = MakeButton(rect, onClick);
        var iconRect = Rect("Icon", rect, Vector2.one * size * .68f);
        iconRect.anchoredPosition = new Vector2(0f, size * .04f);
        view.icon = iconRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        view.icon.preserveAspect = true;
        view.icon.raycastTarget = false;
        view.count = Label("Quantity", rect, "", size < 80 ? 12f : 16f, Ink, new Vector2(size - 16f, size < 80 ? 29f : 36f));
        view.count.alignment = TextAlignmentOptions.TopRight;
        view.count.fontStyle = FontStyles.Bold;
        view.count.rectTransform.anchorMin = view.count.rectTransform.anchorMax = new Vector2(1f, 1f);
        view.count.rectTransform.pivot = new Vector2(1f, 1f);
        view.count.rectTransform.anchoredPosition = new Vector2(-7f, -4f);
        view.number = Label("Key", rect, key, size < 80 ? 11f : 12f, Muted, new Vector2(24f, 22f));
        view.number.rectTransform.anchorMin = view.number.rectTransform.anchorMax = new Vector2(0f, 1f);
        view.number.rectTransform.pivot = new Vector2(0f, 1f);
        view.number.rectTransform.anchoredPosition = new Vector2(8f, -4f);
        view.emptyLabel = Label("Empty", rect, "+", size < 80 ? 20f : 28f, new Color(.61f, .57f, .54f, .64f), new Vector2(size - 10f, Mathf.Min(size - 10f, 64f)));
        return view;
    }

    public void SetOpen(bool value)
    {
        modal.SetActive(value);
        preview.SetVisible(value);
        if (!value) { CloseWithdrawal(); EndDragVisual(); HideItemTooltip(); }
        else { Refresh(); FitWindow(); if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null); }
    }
    void Update()
    {
        if (!IsOpen) return;
        FitWindow(); RefreshBalances();
        if (IsWithdrawalOpen && TabPressed())
        {
            quantityInput.Select();
            quantityInput.ActivateInputField();
            return;
        }
        if (IsWithdrawalOpen && ConfirmPressed()) SubmitWithdrawal();
    }
    void FitWindow()
    {
        var canvasRect = (RectTransform)transform;
        float width = Mathf.Max(1f, canvasRect.rect.width - 36f);
        float height = Mathf.Max(1f, canvasRect.rect.height - 112f);
        window.localScale = Vector3.one * Mathf.Min(1f, width / 1100f, height / windowHeight);
        window.anchoredPosition = new Vector2(0f, 43f);
        PositionWithdrawal();
    }
    void PositionWithdrawal()
    {
        if (!withdrawalWindow || !bankButton) return;
        var parent = (RectTransform)withdrawalWindow.parent;
        float scale = window.localScale.x;
        withdrawalWindow.localScale = Vector3.one * scale;
        bankButton.GetWorldCorners(bankCorners);
        Vector2 position = parent.InverseTransformPoint((bankCorners[0] + bankCorners[3]) * .5f);
        position.y -= 8f * scale;
        float halfWidth = withdrawalWindow.rect.width * scale * .5f;
        float popupHeight = withdrawalWindow.rect.height * scale;
        position.x = Mathf.Clamp(position.x, parent.rect.xMin + halfWidth + 8f, parent.rect.xMax - halfWidth - 8f);
        position.y = Mathf.Clamp(position.y, parent.rect.yMin + popupHeight + 8f, parent.rect.yMax - 8f);
        withdrawalWindow.anchoredPosition = position;
    }
    public void Refresh()
    {
        if (!owner || owner.Inventory == null) return;
        HideItemTooltip();
        var state = owner.Inventory;
        if (storage.Count != state.Capacity) RebuildStorage(state.Capacity);
        int used = 0;
        for (int i = 0; i < storage.Count; i++)
        {
            var item = state.GetSlot(i);
            if (item != null && !item.IsEmpty) used++;
            ApplySlot(storage[i], item, owner.SelectedInventorySlot == i, "+");
        }
        for (int i = 0; i < hotbar.Count; i++) ApplySlot(hotbar[i], state.GetSlot(i), state.SelectedHotbarIndex == i, "");
        foreach (var pair in equipment) ApplySlot(pair.Value, state.GetEquipment(pair.Key), false, EquipmentName(pair.Key));
        capacityLabel.text = used + " / " + state.Capacity;
        RefreshBalances();
    }
    void RefreshBalances()
    {
        long bank = CashService.BankBalance;
        if (bank != lastBank)
        {
            lastBank = bank;
            bankLabel.text = "통장  " + bank.ToString("N0") + "원";
            if (IsWithdrawalOpen) ValidateWithdrawal();
        }
    }
    void RebuildStorage(int count)
    {
        // Capacity only grows, preserving live pointer objects and their drag state.
        for (int i = storage.Count; i < count; i++)
        {
            int index = i;
            var view = CreateSlot("StorageSlot_" + (i + 1).ToString("00"), grid, 106f,
                i < 8 ? (i + 1).ToString() : "", () => owner.ClickInventorySlot(index));
            InventorySlotPointer.AttachStorage(view.background.gameObject, owner, index);
            storage.Add(view);
        }
        int rows = Mathf.CeilToInt(count / 8f);
        float added = Mathf.Max(0, rows - 2) * 116f;
        windowHeight = 750f + added;
        window.sizeDelta = new Vector2(1100f, windowHeight);
        storageCard.sizeDelta = new Vector2(1036f, 274f + added);
        grid.sizeDelta = new Vector2(976f, rows * 116f - 10f);
        FitWindow();
    }
    static void ApplySlot(SlotView view, ItemStack stack, bool selected, string empty)
    {
        bool hasItem = stack != null && !stack.IsEmpty;
        view.background.color = selected ? new Color(.99f, .87f, .68f, .93f) : SlotColor;
        view.background.borderColor = selected ? Accent : new Color(1f, 1f, 1f, .95f);
        view.background.SetVerticesDirty();
        view.icon.sprite = hasItem ? stack.Item.icon : null;
        view.icon.enabled = hasItem && view.icon.sprite;
        view.count.text = hasItem && stack.Count > 1 ? stack.Count.ToString() : "";
        view.emptyLabel.gameObject.SetActive(!hasItem || !view.icon.enabled);
        view.emptyLabel.text = hasItem ? "" : empty;
        view.emptyLabel.color = hasItem ? Ink : new Color(.51f, .43f, .4f, .65f);
        view.number.color = selected ? Ink : Muted;
        var silhouette = view.background.transform.Find("EquipmentSilhouette");
        if (silhouette) silhouette.gameObject.SetActive(!hasItem);
    }

    void BuildWithdrawal()
    {
        var blocker = Panel("WithdrawalModal", modal.transform, Vector2.zero, Color.clear, 0f, 0f);
        Stretch(blocker);
        MakeButton(blocker, CloseWithdrawal);
        withdrawalModal = blocker.gameObject;
        var card = Panel("WithdrawalWindow", blocker, new Vector2(320f, 234f), new Color(.995f, .982f, .955f, .99f), 30f, 14f);
        withdrawalWindow = card;
        card.GetComponent<InventoryRoundedGraphic>().raycastTarget = true;
        card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
        card.pivot = new Vector2(.5f, 1f);
        var title = Label("WithdrawalTitle", card, "출금", 23f, Ink, new Vector2(288f, 40f));
        title.fontStyle = FontStyles.Bold;
        AtTop(title.rectTransform, 0f, -25f);
        var amountLabel = Label("AmountLabel", card, "금액", 15f, Ink, new Vector2(176f, 28f));
        amountLabel.alignment = TextAlignmentOptions.MidlineLeft;
        AtTop(amountLabel.rectTransform, -56f, -59f);
        var quantityLabel = Label("QuantityLabel", card, "장수", 15f, Ink, new Vector2(100f, 28f));
        quantityLabel.alignment = TextAlignmentOptions.MidlineLeft;
        AtTop(quantityLabel.rectTransform, 94f, -59f);
        denominationInput = InputField("WithdrawalAmount", card, new Vector2(176f, 48f), "개당 금액");
        AtTop(denominationInput.GetComponent<RectTransform>(), -56f, -99f);
        quantityInput = InputField("WithdrawalQuantity", card, new Vector2(100f, 48f), "1");
        AtTop(quantityInput.GetComponent<RectTransform>(), 94f, -99f);
        denominationInput.characterLimit = 19;
        quantityInput.characterLimit = 9;
        var amountNavigation = denominationInput.navigation;
        amountNavigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
        amountNavigation.selectOnRight = quantityInput;
        amountNavigation.selectOnDown = quantityInput;
        denominationInput.navigation = amountNavigation;
        var quantityNavigation = quantityInput.navigation;
        quantityNavigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
        quantityNavigation.selectOnLeft = denominationInput;
        quantityNavigation.selectOnUp = denominationInput;
        quantityInput.navigation = quantityNavigation;
        denominationInput.onValueChanged.AddListener(_ => ValidateWithdrawal());
        quantityInput.onValueChanged.AddListener(_ => ValidateWithdrawal());
        withdrawalError = Label("WithdrawalError", card, "", 12f, new Color(.64f, .23f, .2f), new Vector2(288f, 40f));
        withdrawalError.textWrappingMode = TextWrappingModes.Normal;
        AtTop(withdrawalError.rectTransform, 0f, -151f);
        var cancel = Panel("CancelWithdrawal", card, new Vector2(100f, 38f), new Color(.9f, .865f, .82f, .85f), 15f, 7f);
        AtBottom(cancel, -79f, 31f);
        MakeButton(cancel, CloseWithdrawal);
        Label("CancelText", cancel, "취소", 15f, Ink, new Vector2(92f, 31f));
        var confirm = Panel("ConfirmWithdrawal", card, new Vector2(137f, 38f), new Color(.72f, .49f, .33f, 1f), 15f, 7f);
        AtBottom(confirm, 54f, 31f);
        withdrawButton = MakeButton(confirm, SubmitWithdrawal);
        Label("ConfirmText", confirm, "확인 (Enter)", 14f, Color.white, new Vector2(128f, 31f));
        withdrawalModal.SetActive(false);
    }
    TMP_InputField InputField(string name, Transform parent, Vector2 size, string placeholder)
    {
        var rect = Panel(name, parent, size, new Color(1f, 1f, 1f, .97f), 19f, 8f);
        var background = rect.GetComponent<InventoryRoundedGraphic>();
        background.raycastTarget = true;
        var viewport = Rect("TextViewport", rect, size - new Vector2(24f, 10f));
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var text = Label("Text", viewport, "", 18f, Ink, size - new Vector2(24f, 10f));
        text.overflowMode = TextOverflowModes.Overflow;
        Stretch(text.rectTransform); text.alignment = TextAlignmentOptions.MidlineLeft;
        var hint = Label("Placeholder", viewport, placeholder, 15f, Muted, size - new Vector2(24f, 10f));
        Stretch(hint.rectTransform); hint.alignment = TextAlignmentOptions.MidlineLeft;
        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background; input.textViewport = viewport;
        input.textComponent = (TextMeshProUGUI)text; input.placeholder = hint;
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.caretColor = Ink; input.customCaretColor = true;
        input.selectionColor = new Color(.84f, .68f, .46f, .45f);
        input.characterValidation = TMP_InputField.CharacterValidation.Digit;
        return input;
    }
    public void OpenWithdrawal()
    {
        if (!IsOpen || IsWithdrawalOpen) return;
        owner.CancelDrag(); HideItemTooltip(); withdrawalModal.SetActive(true);
        Canvas.ForceUpdateCanvases(); PositionWithdrawal();
        denominationInput.SetTextWithoutNotify(""); quantityInput.SetTextWithoutNotify("1");
        withdrawalError.text = "";
        ValidateWithdrawal(); denominationInput.Select(); denominationInput.ActivateInputField();
    }
    public void CloseWithdrawal()
    {
        if (!withdrawalModal) return;
        denominationInput.DeactivateInputField(); quantityInput.DeactivateInputField();
        withdrawalModal.SetActive(false);
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
    }
    public bool HandleEscape()
    {
        if (!IsWithdrawalOpen) return false;
        CloseWithdrawal(); return true;
    }
    public void SetWithdrawalValues(string amount, string quantity)
    {
        denominationInput.text = amount; quantityInput.text = quantity; ValidateWithdrawal();
    }
    void ValidateWithdrawal()
    {
        if (!withdrawButton) return;
        bool valid = TryWithdrawalValues(out long amount, out int quantity, out string error);
        if (valid)
        {
            long total = amount * quantity;
            withdrawButton.interactable = total <= CashService.BankBalance;
            withdrawalError.text = total > CashService.BankBalance ? "통장 잔액이 부족합니다." : "";
        }
        else
        {
            withdrawButton.interactable = false;
            withdrawalError.text = string.IsNullOrEmpty(denominationInput.text) ? "" : error;
        }
    }
    bool TryWithdrawalValues(out long amount, out int quantity, out string error)
    {
        error = "";
        bool a = long.TryParse(denominationInput.text, NumberStyles.None, CultureInfo.InvariantCulture, out amount);
        bool q = int.TryParse(quantityInput.text, NumberStyles.None, CultureInfo.InvariantCulture, out quantity);
        if (!a || amount < 0) { error = "한 개의 금액을 0원 이상으로 입력해 주세요."; return false; }
        if (!q || quantity < 1) { error = "장수를 1 이상으로 입력해 주세요."; return false; }
        if (amount > long.MaxValue / quantity) { error = "출금할 합계 금액이 너무 큽니다."; return false; }
        return true;
    }
    public void SubmitWithdrawal()
    {
        if (!IsWithdrawalOpen || withdrawing || submittedFrame == Time.frameCount) return;
        submittedFrame = Time.frameCount;
        if (!TryWithdrawalValues(out long amount, out int quantity, out string error))
        { withdrawalError.text = error; return; }
        withdrawing = true;
        try
        {
            if (!CashService.TryWithdraw(owner.Inventory, amount, quantity, out error))
            { withdrawalError.text = error; return; }
            owner.SetStatus(amount.ToString("N0") + "원 × " + quantity + (amount < 10000 ? "개를 꺼냈습니다." : "장을 꺼냈습니다."));
            CloseWithdrawal(); Refresh();
        }
        finally { withdrawing = false; }
    }

    void BuildDragVisual()
    {
        dragVisual = Panel("DraggedItem", transform, Vector2.one * 82f, new Color(1f, .97f, .89f, .95f), 25f, 9f);
        dragVisual.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
        var iconRect = Rect("Icon", dragVisual, Vector2.one * 58f);
        iconRect.anchoredPosition = new Vector2(0f, 3f);
        dragIcon = iconRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        dragIcon.preserveAspect = true; dragIcon.raycastTarget = false;
        dragCount = Label("Count", dragVisual, "", 14f, Ink, new Vector2(70f, 22f));
        dragCount.alignment = TextAlignmentOptions.TopRight;
        AtTop(dragCount.rectTransform, 0f, -4f); dragVisual.gameObject.SetActive(false);
    }

    void BuildTooltip()
    {
        tooltipPanel = Panel("ItemTooltip", transform, new Vector2(218f, 44f),
            new Color(.16f, .12f, .10f, .96f), 16f, 7f);
        tooltipPanel.GetComponent<InventoryRoundedGraphic>().raycastTarget = false;
        tooltipText = Label("Text", tooltipPanel, "", 15f, Color.white, new Vector2(202f, 35f));
        tooltipText.alignment = TextAlignmentOptions.Midline;
        tooltipText.overflowMode = TextOverflowModes.Ellipsis;
        tooltipPanel.gameObject.SetActive(false);
    }

    public void ShowItemTooltip(InventorySlotPointer slot, Vector2 screenPosition)
    {
        if (IsWithdrawalOpen || slot == null || slot.IsDropZone || !owner || owner.IsDragging || owner.Inventory == null)
        {
            HideItemTooltip();
            return;
        }
        ItemStack stack = slot.IsEquipment
            ? owner.Inventory.GetEquipment(slot.Equipment)
            : owner.Inventory.GetSlot(slot.InventoryIndex);
        if (stack == null || stack.IsEmpty || !stack.Item)
        {
            HideItemTooltip();
            return;
        }
        tooltipText.text = stack.Item.DisplayName;
        tooltipPanel.gameObject.SetActive(true);
        tooltipPanel.SetAsLastSibling();
        MoveItemTooltip(screenPosition);
    }

    public void MoveItemTooltip(Vector2 screenPosition)
    {
        if (!tooltipPanel || !tooltipPanel.gameObject.activeSelf) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,
                screenPosition, null, out Vector2 local)) return;
        Rect canvas = ((RectTransform)transform).rect;
        Vector2 half = tooltipPanel.rect.size * .5f;
        Vector2 offset = new Vector2(half.x + 16f, -half.y - 16f);
        Vector2 position = local + offset;
        position.x = Mathf.Clamp(position.x, canvas.xMin + half.x + 8f, canvas.xMax - half.x - 8f);
        position.y = Mathf.Clamp(position.y, canvas.yMin + half.y + 8f, canvas.yMax - half.y - 8f);
        tooltipPanel.anchoredPosition = position;
    }

    public void HideItemTooltip()
    {
        if (tooltipPanel) tooltipPanel.gameObject.SetActive(false);
    }

    public void BeginDragVisual(ItemStack stack)
    {
        if (!dragVisual || stack == null || stack.IsEmpty) return;
        HideItemTooltip();
        dragVisual.gameObject.SetActive(true); dragVisual.SetAsLastSibling();
        dragIcon.sprite = stack.Item.icon; dragIcon.enabled = dragIcon.sprite;
        dragCount.text = stack.Count > 1 ? stack.Count.ToString() : "";
    }
    public void UpdateDragVisual(Vector2 screenPosition)
    {
        if (!dragVisual || !dragVisual.gameObject.activeSelf) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screenPosition, null, out Vector2 local))
            dragVisual.anchoredPosition = local + new Vector2(30f, -28f);
    }
    public void EndDragVisual() { if (dragVisual) dragVisual.gameObject.SetActive(false); }
    public bool IsInsideWindow(Vector2 position) => window && RectTransformUtility.RectangleContainsScreenPoint(window, position, null);

    static bool TabPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Tab);
#else
        return false;
#endif
    }

    static bool ConfirmPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#else
        return false;
#endif
    }
    static string EquipmentName(EquipmentSlot slot)
    {
        switch (slot)
        {
            case EquipmentSlot.Top: return "상의";
            case EquipmentSlot.Bottom: return "하의";
            case EquipmentSlot.Socks: return "양말";
            case EquipmentSlot.Shoes: return "신발";
            case EquipmentSlot.Pet: return "펫";
            default: return "";
        }
    }
    UnityEngine.UI.Button MakeButton(RectTransform rect, Action onClick)
    {
        var graphic = rect.GetComponent<InventoryRoundedGraphic>(); graphic.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = graphic;
        var colors = button.colors;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, .975f, .92f);
        colors.selectedColor = Color.white; colors.pressedColor = new Color(.89f, .83f, .75f);
        colors.disabledColor = new Color(.76f, .76f, .76f, .68f); button.colors = colors;
        button.onClick.AddListener(() => onClick());
        var navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
        return button;
    }
    void EnsureEventSystem()
    {
        var existing = FindFirstObjectByType<EventSystem>();
        if (existing && existing.GetComponent<BaseInputModule>()) return;
        var host = existing ? existing.gameObject : new GameObject("InventoryEventSystem", typeof(EventSystem));
        if (!existing) host.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM
        host.AddComponent<InputSystemUIInputModule>();
#else
        host.AddComponent<StandaloneInputModule>();
#endif
    }
    static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = size; return rect;
    }
    RectTransform Panel(string name, Transform parent, Vector2 size, Color color, float large = 28f, float small = 11f)
    {
        var rect = Rect(name, parent, size);
        var graphic = rect.gameObject.AddComponent<InventoryRoundedGraphic>();
        graphic.color = color; graphic.largeRadius = large; graphic.smallRadius = small;
        graphic.borderWidth = large > 0f ? 1.5f : 0f; graphic.raycastTarget = false; return rect;
    }
    static RectTransform Icon(string name, Transform parent, InventoryGlyphGraphic.Glyph kind, Vector2 size, Color color)
    {
        var rect = Rect(name, parent, size); var glyph = rect.gameObject.AddComponent<InventoryGlyphGraphic>();
        glyph.kind = kind; glyph.color = color; glyph.raycastTarget = false; return rect;
    }
    TMP_Text Label(string name, Transform parent, string value, float size, Color color, Vector2 dimensions)
    {
        var rect = Rect(name, parent, dimensions); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = size; text.color = color;
        text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; return text;
    }
    static void AutoSize(TMP_Text label, float min, float max) { label.enableAutoSizing = true; label.fontSizeMin = min; label.fontSizeMax = max; }
    static void AtTop(RectTransform rect, float x, float y) { rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f); rect.anchoredPosition = new Vector2(x, y); }
    static void AtBottom(RectTransform rect, float x, float y) { rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f); rect.anchoredPosition = new Vector2(x, y); }
    static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    void OnDestroy() { if (owner) owner.UiChanged -= Refresh; }
}
