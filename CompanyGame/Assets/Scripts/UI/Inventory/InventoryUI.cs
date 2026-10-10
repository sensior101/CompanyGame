using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Square inventory slots, separated outfit/pet equipment, and account withdrawals.</summary>
public sealed partial class InventoryUI : MonoBehaviour
{
    static readonly Color WindowColor = new Color(.985f, .98f, .96f, .78f);
    static readonly Color SurfaceColor = new Color(1f, .985f, .96f, .44f);
    static readonly Color SlotColor = new Color(.96f, .935f, .895f, .56f);
    static readonly Color Ink = new Color(.29f, .205f, .19f);
    static readonly Color Muted = new Color(.54f, .46f, .425f);
    static readonly Color Accent = new Color(.73f, .47f, .28f);
    static readonly EquipmentSlot[] ClothingSlots =
        { EquipmentSlot.Top, EquipmentSlot.Bottom, EquipmentSlot.Socks, EquipmentSlot.Shoes };
    readonly List<SlotView> storage = new List<SlotView>();
    readonly List<SlotView> hotbar = new List<SlotView>();
    readonly Dictionary<EquipmentSlot, SlotView> equipment = new Dictionary<EquipmentSlot, SlotView>();
    PlayerInventory owner;
    PlayerInteraction interaction;
    RectTransform quickSlots;
    PlayerStats playerStats;
    readonly RectTransform[] statusFills = new RectTransform[3];
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
        BuildHotbar();
        BuildStatusBars();
        var shade = Panel("InventoryModal", transform, Vector2.zero, new Color(.16f, .13f, .13f, .12f), 0f, 0f);
        UIBuild.Stretch(shade);
        modal = shade.gameObject;
        window = Panel("InventoryWindow", shade, new Vector2(1100f, windowHeight), WindowColor, 64f, 27f);
        window.GetComponent<InventoryRoundedGraphic>().raycastTarget = true;
        var title = Label("Title", window, "인벤토리", 36f, Ink, new Vector2(310f, 54f));
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        AtTop(title.rectTransform, -331f, -44f);
        AtTop(Icon("BagIcon", window, InventoryGlyphGraphic.Glyph.Bag, new Vector2(43f, 48f), Accent), -504f, -44f);
        var bank = BuildWalletDisplay(window, out bankLabel);
        bankButton = bank; AtTop(bank, 218f, -38f); MakeButton(bank, OpenWithdrawal);
        var close = Panel("CloseInventory", window, new Vector2(127f, 51f), new Color(1f, .99f, .96f, .66f), 22f, 10f);
        AtTop(close, 448f, -38f);
        MakeButton(close, owner.CloseInventory);
        Label("CloseLabel", close, "×  닫기", 24f, Ink, new Vector2(117f, 42f));
        BuildPortrait(); BuildEquipment(); BuildPet();
        storageCard = Panel("Storage", window, new Vector2(1036f, 274f), new Color(1f, 1f, 1f, .25f), 34f, 17f);
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
        grid = UIBuild.Rect("StorageSlots", storageCard, new Vector2(976f, 222f));
        grid.anchorMin = grid.anchorMax = new Vector2(.5f, 1f);
        grid.pivot = new Vector2(.5f, 1f);
        grid.anchoredPosition = new Vector2(0f, -48f);
        var layout = grid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        layout.cellSize = new Vector2(106f, 106f);
        layout.spacing = new Vector2(16f, 10f);
        layout.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 8;
        layout.childAlignment = TextAnchor.UpperCenter;
        BuildWithdrawal(); BuildDragVisual(); BuildTooltip(); BuildStatusFeedback();
        modal.SetActive(false);
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
        UpdateStatusBars();
        if (statusPanel && statusPanel.gameObject.activeSelf && Time.unscaledTime >= statusUntil)
            statusPanel.gameObject.SetActive(false);
        if (quickSlots)
        {
            bool visible = !InventoryItemSelector.IsOpen && (!interaction || !interaction.IsTradeOpen);
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
            ApplySlot(storage[i], item, owner.SelectedInventorySlot == i, "+");
        }
        for (int i = 0; i < hotbar.Count; i++) ApplySlot(hotbar[i], state.GetSlot(i), state.SelectedHotbarIndex == i, "");
        foreach (var pair in equipment) ApplySlot(pair.Value, state.GetEquipment(pair.Key), false, EquipmentName(pair.Key));
        capacityLabel.text = used + " / " + state.Capacity;
        RefreshBalances();
    }

    void BuildStatusFeedback()
    {
        statusPanel = Panel("ItemStatus", transform, new Vector2(740f, 40f), new Color(.16f, .12f, .10f, .94f), 15f, 7f);
        statusPanel.anchorMin = statusPanel.anchorMax = new Vector2(.5f, 1f);
        statusPanel.pivot = new Vector2(.5f, 1f);
        statusPanel.anchoredPosition = new Vector2(0f, -12f);
        statusText = Label("Message", statusPanel, "", 18f, Color.white, new Vector2(708f, 34f));
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
