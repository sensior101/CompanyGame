using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Square inventory slots, separated outfit/pet equipment, and account withdrawals.</summary>
public sealed partial class InventoryUI : MonoBehaviour
{
    static readonly Color WindowColor = UITheme.A(UITheme.Ivory, .98f);
    static readonly Color SurfaceColor = UITheme.A(UITheme.Sand, .6f);
    static readonly Color SlotColor = UITheme.A(UITheme.Sand, .85f);
    static readonly Color Ink = UITheme.Ink;
    static readonly Color Muted = UITheme.Brown;
    static readonly Color Accent = UITheme.Selected;
    static readonly EquipmentSlot[] ClothingSlots =
        { EquipmentSlot.Top, EquipmentSlot.Bottom, EquipmentSlot.Socks, EquipmentSlot.Shoes };
    readonly List<SlotView> storage = new List<SlotView>();
    readonly List<SlotView> hotbar = new List<SlotView>();
    readonly Dictionary<EquipmentSlot, SlotView> equipment = new Dictionary<EquipmentSlot, SlotView>();
    PlayerInventory owner;
    PlayerInteraction interaction;
    RectTransform quickSlots;
    PlayerStats playerStats;
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
    RectTransform statusPanel;
    TMP_Text statusText;
    float statusUntil;
    int submittedFrame = -1;
    long lastFoodMinute = -1;
    long lastBank = long.MinValue;
    const float WindowWidth = 740f;
    const float BaseWindowHeight = 448f;
    const float StorageCellSize = 72f;
    const float StorageCellStep = 80f;
    float windowHeight = BaseWindowHeight;

    sealed class SlotView
    {
        public InventoryRoundedGraphic background;
        public UnityEngine.UI.Image icon;
        public TMP_Text count, number, emptyLabel;
        public UnityEngine.UI.Button button;
        public bool flatSurface;
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
        BuildHotbar();
        BuildStatusBars();
        var shade = Panel("InventoryModal", transform, Vector2.zero, new Color(.16f, .13f, .13f, .12f), 0f, 0f);
        UIBuild.Stretch(shade);
        modal = shade.gameObject;
        window = Panel("InventoryWindow", shade, new Vector2(WindowWidth, windowHeight), WindowColor, 12f, 12f);
        var windowGraphic = window.GetComponent<InventoryRoundedGraphic>();
        windowGraphic.chamfered = true;
        windowGraphic.bevelEdges = true;
        windowGraphic.borderWidth = 2f;
        windowGraphic.edgeTopColor = UITheme.Ivory;
        windowGraphic.edgeBottomColor = UITheme.Slate;
        window.GetComponent<InventoryRoundedGraphic>().raycastTarget = true;
        var title = Label("Title", window, "인벤토리", 20f, Ink, new Vector2(210f, 32f));
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        AtTop(title.rectTransform, -202f, -30f);
        AtTop(Icon("BagIcon", window, InventoryGlyphGraphic.Glyph.Bag, new Vector2(22f, 26f), UITheme.Brown), -331f, -30f);
        var bank = BuildWalletDisplay(window, out bankLabel);
        bankButton = bank; AtTop(bank, 182f, -30f); bank.localScale = Vector3.one * .72f; MakeButton(bank, OpenWithdrawal);
        bank.GetComponent<InventoryRoundedGraphic>().borderWidth = 0f;
        bank.GetComponent<InventoryRoundedGraphic>().color = Color.clear;
        bankLabel.fontStyle = FontStyles.Normal;
        var close = Panel("CloseInventory", window, new Vector2(30f, 30f), UITheme.Slate, 6f, 6f);
        close.GetComponent<InventoryRoundedGraphic>().borderWidth = 0f;
        close.GetComponent<InventoryRoundedGraphic>().chamfered = true;
        AtTop(close, 336f, -30f);
        MakeButton(close, owner.CloseInventory);
        Label("CloseLabel", close, "×", 18f, Ink, new Vector2(30f, 30f));
        BuildPortrait(); BuildEquipment(); BuildPet();
        storageCard = UIBuild.Rect("Storage", window, new Vector2(312f, 352f));
        storageCard.anchorMin = storageCard.anchorMax = new Vector2(.5f, 1f);
        storageCard.pivot = new Vector2(.5f, 1f);
        storageCard.anchoredPosition = new Vector2(182f, -66f);
        var storageTitle = Label("StorageHeading", storageCard, "소지품", 16f, Ink, new Vector2(180f, 24f));
        storageTitle.fontStyle = FontStyles.Bold;
        storageTitle.alignment = TextAlignmentOptions.MidlineLeft;
        AtTop(storageTitle.rectTransform, -66f, -12f);
        capacityLabel = Label("Capacity", storageCard, "0 / 16", 13f, Muted, new Vector2(90f, 24f));
        capacityLabel.alignment = TextAlignmentOptions.MidlineRight;
        AtTop(capacityLabel.rectTransform, 111f, -12f);
        grid = UIBuild.Rect("StorageSlots", storageCard, new Vector2(312f, 312f));
        grid.anchorMin = grid.anchorMax = new Vector2(.5f, 1f);
        grid.pivot = new Vector2(.5f, 1f);
        grid.anchoredPosition = new Vector2(0f, -32f);
        var layout = grid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        layout.cellSize = Vector2.one * StorageCellSize;
        layout.spacing = Vector2.one * 8f;
        layout.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 4;
        layout.childAlignment = TextAnchor.UpperCenter;
        BuildWithdrawal(); BuildDragVisual(); BuildTooltip(); BuildStatusFeedback();
        modal.SetActive(false);
    }

    public void SetOpen(bool value)
    {
        modal.SetActive(value);
        if (quickSlots) quickSlots.gameObject.SetActive(!value && !InventoryItemSelector.IsOpen && (!interaction || !interaction.IsTradeOpen));
        preview.SetVisible(value);
        if (!value) { CloseWithdrawal(); EndDragVisual(); HideItemTooltip(); }
        else { Refresh(); FitWindow(); if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null); }
    }
    void Update()
    {
        UpdateStatusBars();
        long foodMinute = FoodShelfLife.Now.TotalMinutes;
        if (lastFoodMinute != foodMinute)
        {
            lastFoodMinute = foodMinute;
            if (owner && owner.Inventory != null)
            {
                for (int i = 0; i < storage.Count; i++) SetFoodTint(storage[i], owner.Inventory.GetSlot(i));
                for (int i = 0; i < hotbar.Count; i++) SetFoodTint(hotbar[i], owner.Inventory.GetSlot(i));
            }
        }
        if (statusPanel && statusPanel.gameObject.activeSelf && Time.unscaledTime >= statusUntil)
            statusPanel.gameObject.SetActive(false);
        if (quickSlots)
        {
            bool visible = !IsOpen && !InventoryItemSelector.IsOpen && (!interaction || !interaction.IsTradeOpen);
            if (quickSlots.gameObject.activeSelf != visible) quickSlots.gameObject.SetActive(visible);
        }
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
    public void Refresh()
    {
        if (!owner || owner.Inventory == null) return;
        RefreshStatusFeedback();
        HideItemTooltip();
        var state = owner.Inventory;
        if (storage.Count != state.Capacity) RebuildStorage(state.Capacity);
        int used = 0;
        for (int i = 0; i < storage.Count; i++)
        {
            var item = state.GetSlot(i);
            if (item != null && !item.IsEmpty) used++;
            ApplySlot(storage[i], item, owner.SelectedInventorySlot == i, "");
        }
        for (int i = 0; i < hotbar.Count; i++) ApplySlot(hotbar[i], state.GetSlot(i), state.SelectedHotbarIndex == i, "");
        foreach (var pair in equipment) ApplySlot(pair.Value, state.GetEquipment(pair.Key), false, EquipmentName(pair.Key));
        capacityLabel.text = used + " / " + state.Capacity;
        RefreshBalances();
    }

    void BuildStatusFeedback()
    {
        statusPanel = Panel("ItemStatus", transform, new Vector2(740f, 40f), UITheme.A(UITheme.Ivory, .93f), 15f, 7f);
        statusPanel.anchorMin = statusPanel.anchorMax = new Vector2(.5f, 1f);
        statusPanel.pivot = new Vector2(.5f, 1f);
        statusPanel.anchoredPosition = new Vector2(0f, -12f);
        statusText = Label("Message", statusPanel, "", 18f, UITheme.Ink, new Vector2(708f, 34f));
        AutoSize(statusText, 12f, 18f);
        statusPanel.gameObject.SetActive(false);
    }

    void RefreshStatusFeedback()
    {
        if (!statusPanel) return;
        string message = owner.StatusMessage;
        statusText.text = message;
        statusPanel.gameObject.SetActive(!string.IsNullOrEmpty(message));
        if (string.IsNullOrEmpty(message)) return;
        statusPanel.sizeDelta = new Vector2(Mathf.Min(740f, ((RectTransform)transform).rect.width - 24f), 40f);
        statusText.rectTransform.sizeDelta = new Vector2(statusPanel.sizeDelta.x - 32f, 34f);
        statusUntil = Time.unscaledTime + 4.5f;
        statusPanel.SetAsLastSibling();
    }
    void RefreshBalances()
    {
        long bank = CashService.BankBalance;
        if (bank != lastBank)
        {
            lastBank = bank;
            bankLabel.text = "지갑  " + bank.ToString("N0") + "원";
            if (IsWithdrawalOpen) ValidateWithdrawal();
        }
    }
    void RebuildStorage(int count)
    {
        // Capacity only grows, preserving live pointer objects and their drag state.
        for (int i = storage.Count; i < count; i++)
        {
            int index = i;
            var view = CreateSlot("StorageSlot_" + (i + 1).ToString("00"), grid, StorageCellSize,
                i < 8 ? (i + 1).ToString() : "", () => owner.ClickInventorySlot(index));
            InventorySlotPointer.AttachStorage(view.background.gameObject, owner, index);
            StyleWardrobeSlot(view);
            storage.Add(view);
        }
        int rows = Mathf.CeilToInt(count / 4f);
        float added = Mathf.Max(0, rows - 4) * StorageCellStep;
        windowHeight = BaseWindowHeight + added;
        window.sizeDelta = new Vector2(WindowWidth, windowHeight);
        storageCard.sizeDelta = new Vector2(312f, 352f + added);
        grid.sizeDelta = new Vector2(312f, rows * StorageCellStep - 8f);
        FitWindow();
    }
    static void SetFoodTint(SlotView view, ItemStack stack)
    {
        view.icon.color = stack != null && stack.IsSpoiled ? new Color(.72f, .9f, .55f) : Color.white;
    }
    static void ApplySlot(SlotView view, ItemStack stack, bool selected, string empty)
    {
        bool hasItem = stack != null && !stack.IsEmpty;
        bool wardrobeSlot = view.flatSurface;
        view.background.color = selected ? UITheme.A(UITheme.Selected, .55f) : wardrobeSlot ? UITheme.Sand : SlotColor;
        if (wardrobeSlot)
        {
            view.background.borderWidth = selected ? 2f : 1.25f;
            view.background.bevelEdges = !selected;
        }
        view.background.borderColor = selected ? Accent : UITheme.Brown;
        view.background.SetVerticesDirty();
        SetFoodTint(view, stack);
        view.icon.sprite = hasItem ? stack.Item.icon : null;
        view.icon.enabled = hasItem && view.icon.sprite;
        view.count.text = hasItem && stack.Count > 1 ? stack.Count.ToString() : "";
        view.emptyLabel.gameObject.SetActive(!hasItem || !view.icon.enabled);
        view.emptyLabel.text = hasItem ? "" : empty;
        view.emptyLabel.color = hasItem ? Ink : UITheme.A(UITheme.Brown, wardrobeSlot ? 1f : .6f);
        view.number.color = selected ? Ink : Muted;
        var silhouette = view.background.transform.Find("EquipmentSilhouette");
        if (silhouette) silhouette.gameObject.SetActive(!hasItem);
    }

    static bool TabPressed()
    {
        return GameInput.NextFieldPressed;
    }

    static bool ConfirmPressed()
    {
        return GameInput.SubmitPressed;
    }
    void OnDestroy() { if (owner) owner.UiChanged -= Refresh; }
}
