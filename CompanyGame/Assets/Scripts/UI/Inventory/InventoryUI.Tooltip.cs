using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class InventoryUI
{
    TMP_Text tooltipDetails;
    InventorySlotPointer tooltipSlot;
    Vector2 tooltipPosition;
    float nextTooltipRefresh;

    void BuildDragVisual()
    {
        dragVisual = Panel("DraggedItem", transform, Vector2.one * 82f, new Color(1f, .97f, .89f, .95f), 25f, 9f);
        dragVisual.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
        var iconRect = UIBuild.Rect("Icon", dragVisual, Vector2.one * 58f);
        iconRect.anchoredPosition = new Vector2(0f, 3f);
        dragIcon = iconRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        dragIcon.preserveAspect = true; dragIcon.useSpriteMesh = true; dragIcon.raycastTarget = false;
        dragCount = Label("Count", dragVisual, "", 14f, Ink, new Vector2(70f, 22f));
        dragCount.alignment = TextAlignmentOptions.TopRight;
        AtTop(dragCount.rectTransform, 0f, -4f); dragVisual.gameObject.SetActive(false);
    }

    void BuildTooltip()
    {
        tooltipPanel = Panel("ItemTooltip", transform, new Vector2(218f, 44f),
            new Color(.16f, .12f, .10f, .96f), 16f, 7f);
        tooltipPanel.GetComponent<InventoryRoundedGraphic>().raycastTarget = false;
        var canvas = tooltipPanel.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = 200;
        tooltipText = Label("Text", tooltipPanel, "", 15f, Color.white, new Vector2(202f, 35f));
        tooltipText.alignment = TextAlignmentOptions.Midline;
        tooltipText.richText = false;
        tooltipText.textWrappingMode = TextWrappingModes.Normal;
        tooltipText.overflowMode = TextOverflowModes.Overflow;
        tooltipDetails = Label("Details", tooltipPanel, "", 12f,
            new Color(.88f, .88f, .88f), new Vector2(202f, 20f));
        tooltipDetails.richText = false;
        tooltipDetails.textWrappingMode = TextWrappingModes.Normal;
        tooltipDetails.overflowMode = TextOverflowModes.Overflow;
        tooltipDetails.gameObject.SetActive(false);
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
        tooltipSlot = slot;
        tooltipPosition = screenPosition;
        nextTooltipRefresh = Time.unscaledTime + .25f;
        tooltipText.text = stack.Tooltip + (stack.Item.IsVehicle ? " · 좌클릭 설치" : "");
        tooltipDetails.text = stack.TooltipDetails;
        bool hasDetails = !string.IsNullOrEmpty(tooltipDetails.text);
        tooltipDetails.gameObject.SetActive(hasDetails);
        float width = Mathf.Clamp(Mathf.Max(tooltipText.GetPreferredValues(tooltipText.text).x,
            hasDetails ? tooltipDetails.GetPreferredValues(tooltipDetails.text).x : 0f) + 24f, 120f, 360f);
        float titleHeight = tooltipText.GetPreferredValues(tooltipText.text, width - 24f, 0f).y;
        float detailHeight = hasDetails ? tooltipDetails.GetPreferredValues(tooltipDetails.text, width - 24f, 0f).y : 0f;
        float height = Mathf.Max(44f, titleHeight + (hasDetails ? detailHeight + 6f : 0f) + 20f);
        tooltipPanel.sizeDelta = new Vector2(width, height);
        tooltipText.rectTransform.sizeDelta = new Vector2(width - 24f, hasDetails ? titleHeight : height - 16f);
        tooltipText.rectTransform.anchoredPosition = hasDetails ? new Vector2(0f, (height - titleHeight) * .5f - 10f) : Vector2.zero;
        tooltipDetails.rectTransform.sizeDelta = new Vector2(width - 24f, detailHeight);
        tooltipDetails.rectTransform.anchoredPosition = new Vector2(0f, height * .5f - 16f - titleHeight - detailHeight * .5f);
        tooltipPanel.gameObject.SetActive(true);
        tooltipPanel.SetAsLastSibling();
        MoveItemTooltip(screenPosition);
    }

    public void MoveItemTooltip(Vector2 screenPosition)
    {
        if (!tooltipPanel || !tooltipPanel.gameObject.activeSelf) return;
        tooltipPosition = screenPosition;
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
        tooltipSlot = null;
        if (tooltipPanel) tooltipPanel.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        // A game day can change without pointer movement or an inventory edit.
        if (tooltipPanel && tooltipPanel.gameObject.activeSelf && Time.unscaledTime >= nextTooltipRefresh)
        {
            if (!tooltipSlot || !tooltipSlot.isActiveAndEnabled) HideItemTooltip();
            else ShowItemTooltip(tooltipSlot, tooltipPosition);
        }
    }

    public void BeginDragVisual(ItemStack stack,int count=-1)
    {
        if (!dragVisual || stack == null || stack.IsEmpty) return;
        HideItemTooltip();
        dragVisual.gameObject.SetActive(true); dragVisual.SetAsLastSibling();
        dragIcon.sprite = stack.Item.icon; dragIcon.enabled = dragIcon.sprite;
        int quantity=count<0?stack.Count:count;
        dragCount.text = quantity > 1 ? quantity.ToString() : "";
    }

    public void UpdateDragVisual(Vector2 screenPosition)
    {
        if (!dragVisual || !dragVisual.gameObject.activeSelf) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screenPosition, null, out Vector2 local))
            dragVisual.anchoredPosition = local + new Vector2(30f, -28f);
    }

    public void EndDragVisual() { if (dragVisual) dragVisual.gameObject.SetActive(false); }

    public bool IsInsideWindow(Vector2 position) => window && RectTransformUtility.RectangleContainsScreenPoint(window, position, null);
}
