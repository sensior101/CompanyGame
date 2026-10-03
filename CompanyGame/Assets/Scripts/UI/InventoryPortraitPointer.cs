using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Right-button orbit is owned by the portrait, never by the gameplay camera.</summary>
[DisallowMultipleComponent]
public sealed class InventoryPortraitPointer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    InventoryCharacterPreview preview;
    bool captured;
    public bool IsRotating => captured;
    public void Initialize(InventoryCharacterPreview value) { preview = value; }
    public void OnPointerDown(PointerEventData data)
    {
        captured = data.button == PointerEventData.InputButton.Right && preview;
    }
    public void OnPointerUp(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Right) captured = false;
    }
    public void OnBeginDrag(PointerEventData data)
    {
        if (captured && data.button == PointerEventData.InputButton.Right) data.eligibleForClick = false;
    }
    public void OnDrag(PointerEventData data)
    {
        if (!captured || !preview || data.button != PointerEventData.InputButton.Right) return;
        var owner = preview.GetComponentInParent<InventoryUI>();
        if (owner && owner.IsWithdrawalOpen) return;
        if (!RectTransformUtility.RectangleContainsScreenPoint(preview.PortraitRect, data.position, data.pressEventCamera)) return;
        preview.RotatePreview(data.delta.x);
        data.eligibleForClick = false;
    }
    public void OnEndDrag(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Right) captured = false;
    }
    void OnDisable() { captured = false; }
}
