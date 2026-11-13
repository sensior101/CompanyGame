using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Opening furniture storage reuses this same inventory window: the container's slots go on top and the player's own
/// slots (with their existing pointers, drag and tooltips) go underneath. No second inventory view exists.
/// </summary>
public sealed partial class InventoryUI
{
    const int ContainerColumns = 8;
    const float ContainerWidth = 632f;
    const float ContainerHeader = 32f;
    readonly List<SlotView> containerSlots = new List<SlotView>();
    RectTransform containerCard, containerGrid;
    TMP_Text containerHeading, containerCapacity;
    InventoryState containerState;
    Action<int> containerClick;
    Predicate<ItemStack> containerAccepts;
    string containerTitle;

    public bool IsContainerOpen => containerState != null;
    public InventoryState ContainerState => containerState;
    /// <summary>False for a bag item this storage refuses (shown disabled, like the library's unavailable items).</summary>
    public bool BagSlotAccepted(int index)
    {
        var stack = owner && owner.Inventory != null ? owner.Inventory.GetSlot(index) : null;
        return containerState == null || stack == null || stack.IsEmpty || containerAccepts == null || containerAccepts(stack);
    }
    public bool BagSlotEnabled(int index) => storage[index].button.interactable;

    public void ShowContainer(string label, InventoryState state, Action<int> click, Predicate<ItemStack> accepts)
    {
        containerState = state; containerClick = click; containerTitle = label; containerAccepts = accepts;
        if (!containerCard) BuildContainerCard();
        SetInventoryOnlyParts(false);
        window.Find("Title").GetComponent<TMP_Text>().text = label;
        containerHeading.text = label;
        containerCard.gameObject.SetActive(true);
        for (int i = containerSlots.Count; i < state.Capacity; i++)
        {
            int index = i;
            var view = CreateSlot("ContainerSlot_" + (i + 1).ToString("00"), containerGrid, StorageCellSize, "", () => containerClick?.Invoke(index));
            StyleWardrobeSlot(view);
            InventorySlotPointer.AttachContainer(view.background.gameObject, owner, index);
            containerSlots.Add(view);
        }
        for (int i = 0; i < containerSlots.Count; i++) containerSlots[i].background.gameObject.SetActive(i < state.Capacity);
        LayoutStorage(storage.Count);
        RefreshContainer();
    }

    public void HideContainer()
    {
        if (containerState == null) return;
        containerState = null; containerClick = null; containerAccepts = null;
        ApplyContainerFilter();
        containerCard.gameObject.SetActive(false);
        window.Find("Title").GetComponent<TMP_Text>().text = "인벤토리";
        SetInventoryOnlyParts(true);
        HideItemTooltip();
        LayoutStorage(storage.Count);
    }

    /// <summary>Same action as clicking the container slot on screen.</summary>
    public void ClickContainerSlot(int index) => containerClick?.Invoke(index);

    public void RefreshContainer()
    {
        if (containerState == null) return;
        int used = 0;
        for (int i = 0; i < containerState.Capacity && i < containerSlots.Count; i++)
        {
            var stack = containerState.GetSlot(i);
            if (stack != null && !stack.IsEmpty) used++;
            ApplySlot(containerSlots[i], stack, false, "");
        }
        containerCapacity.text = used + " / " + containerState.Capacity;
        ApplyContainerFilter();
    }

    void ApplyContainerFilter()
    {
        var bag = owner ? owner.Inventory : null;
        if (bag == null) return;
        for (int i = 0; i < storage.Count; i++)
        {
            bool allowed = BagSlotAccepted(i);
            storage[i].button.interactable = allowed;
            if (allowed) SetFoodTint(storage[i], bag.GetSlot(i));
            else storage[i].icon.color = DisabledItemTint;
        }
    }

    void BuildContainerCard()
    {
        containerCard = UIBuild.Rect("Container", window, new Vector2(ContainerWidth, 184f));
        containerCard.anchorMin = containerCard.anchorMax = new Vector2(.5f, 1f);
        containerCard.pivot = new Vector2(.5f, 1f);
        containerHeading = Label("ContainerHeading", containerCard, "", 16f, Ink, new Vector2(180f, 24f));
        containerHeading.fontStyle = FontStyles.Bold;
        containerHeading.alignment = TextAlignmentOptions.MidlineLeft;
        AtTop(containerHeading.rectTransform, -226f, -12f);
        containerCapacity = Label("ContainerCapacity", containerCard, "0 / 16", 13f, Muted, new Vector2(90f, 24f));
        containerCapacity.alignment = TextAlignmentOptions.MidlineRight;
        AtTop(containerCapacity.rectTransform, 271f, -12f);
        containerGrid = UIBuild.Rect("ContainerSlots", containerCard, new Vector2(ContainerWidth, 152f));
        containerGrid.anchorMin = containerGrid.anchorMax = new Vector2(.5f, 1f);
        containerGrid.pivot = new Vector2(.5f, 1f);
        containerGrid.anchoredPosition = new Vector2(0f, -ContainerHeader);
        var layout = containerGrid.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        layout.cellSize = Vector2.one * StorageCellSize;
        layout.spacing = Vector2.one * 8f;
        layout.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = ContainerColumns;
        layout.childAlignment = TextAnchor.UpperCenter;
    }

    // Wardrobe, wallet and portrait stay out of the storage view; the bag slots keep their own objects.
    void SetInventoryOnlyParts(bool visible)
    {
        foreach (string part in new[] { "CurrentPlayer", "Clothing", "CompanionPet", "BankBalanceButton" })
        {
            var child = window.Find(part);
            if (child) child.gameObject.SetActive(visible);
        }
        preview.SetVisible(visible && IsOpen);
    }

    void LayoutStorage(int count)
    {
        var gridLayout = grid.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        var heading = (RectTransform)storageCard.Find("StorageHeading");
        if (containerState == null)
        {
            int rows = Mathf.CeilToInt(count / 4f);
            float added = Mathf.Max(0, rows - 4) * StorageCellStep;
            gridLayout.constraintCount = 4;
            AtTop(heading, -66f, -12f);
            AtTop(capacityLabel.rectTransform, 111f, -12f);
            windowHeight = BaseWindowHeight + added;
            storageCard.anchoredPosition = new Vector2(182f, -66f);
            storageCard.sizeDelta = new Vector2(312f, 352f + added);
            grid.sizeDelta = new Vector2(312f, rows * StorageCellStep - 8f);
        }
        else
        {
            int containerRows = Mathf.CeilToInt(containerState.Capacity / (float)ContainerColumns);
            int bagRows = Mathf.CeilToInt(count / (float)ContainerColumns);
            float containerHeight = ContainerHeader + containerRows * StorageCellStep - 8f;
            float bagHeight = ContainerHeader + bagRows * StorageCellStep - 8f;
            containerCard.sizeDelta = new Vector2(ContainerWidth, containerHeight);
            containerCard.anchoredPosition = new Vector2(0f, -66f);
            containerGrid.sizeDelta = new Vector2(ContainerWidth, containerRows * StorageCellStep - 8f);
            gridLayout.constraintCount = ContainerColumns;
            AtTop(heading, -226f, -12f);
            AtTop(capacityLabel.rectTransform, 271f, -12f);
            storageCard.anchoredPosition = new Vector2(0f, -66f - containerHeight - 14f);
            storageCard.sizeDelta = new Vector2(ContainerWidth, bagHeight);
            grid.sizeDelta = new Vector2(ContainerWidth, bagRows * StorageCellStep - 8f);
            windowHeight = 66f + containerHeight + 14f + bagHeight + 24f;
        }
        window.sizeDelta = new Vector2(WindowWidth, windowHeight);
        FitWindow();
    }
}
