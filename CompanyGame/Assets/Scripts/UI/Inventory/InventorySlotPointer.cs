using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Pointer drag source/target. Contents remain in the model until a valid release.</summary>
[DisallowMultipleComponent]
public sealed class InventorySlotPointer : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
    IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler
{
    public PlayerInventory Owner { get; private set; }
    public int InventoryIndex { get; private set; } = -1;
    public EquipmentSlot Equipment { get; private set; }
    public bool IsEquipment { get; private set; }
    public bool IsDropZone { get; private set; }
    bool ownsDrag;
    bool hoverOnly;

    public static InventorySlotPointer AttachStorage(GameObject host, PlayerInventory owner, int index)
    {
        var pointer = GetOrAdd(host);
        pointer.Configure(owner, index);
        return pointer;
    }

    public static InventorySlotPointer AttachEquipment(GameObject host, PlayerInventory owner, EquipmentSlot slot)
    {
        var pointer = GetOrAdd(host);
        pointer.ConfigureEquipment(owner, slot);
        return pointer;
    }

    public static InventorySlotPointer AttachDropZone(GameObject host, PlayerInventory owner)
    {
        var pointer = GetOrAdd(host);
        pointer.ConfigureDropZone(owner);
        return pointer;
    }

    static InventorySlotPointer GetOrAdd(GameObject host) => host.GetComponent<InventorySlotPointer>() ?? host.AddComponent<InventorySlotPointer>();
    public void Configure(PlayerInventory owner, int index) => Configure(owner, index, false);
    public void Configure(PlayerInventory owner, int index, bool hoverOnly)
    {
        Owner = owner; InventoryIndex = index; IsEquipment = false; IsDropZone = false;
        this.hoverOnly = hoverOnly;
    }
    public void ConfigureEquipment(PlayerInventory owner, EquipmentSlot slot)
    {
        Owner = owner; Equipment = slot; InventoryIndex = -1; IsEquipment = true; IsDropZone = false;
        hoverOnly = false;
    }
    public void ConfigureDropZone(PlayerInventory owner)
    {
        Owner = owner; InventoryIndex = -1; IsEquipment = false; IsDropZone = true;
        hoverOnly = false;
    }

    public void OnBeginDrag(PointerEventData data)
    {
        if (hoverOnly || !Owner || IsDropZone || data.button == PointerEventData.InputButton.Middle) return;
        if (Owner.UserInterface) Owner.UserInterface.HideItemTooltip();
        bool single=data.button==PointerEventData.InputButton.Right;
        ownsDrag = IsEquipment ? Owner.BeginDragEquipment(Equipment,single) : Owner.BeginDragInventory(InventoryIndex,single);
        if (!ownsDrag) return;
        data.eligibleForClick = false;
        Owner.UpdateDrag(data.position);
    }

    public void OnPointerClick(PointerEventData data)
    {
        if (!hoverOnly && data.button == PointerEventData.InputButton.Right && data.eligibleForClick &&
            Owner && !IsEquipment && !IsDropZone && !Owner.IsDragging)
            Owner.GetComponent<BookReader>()?.OpenPermissions(InventoryIndex, (RectTransform)transform);
    }

    public void OnDrag(PointerEventData data)
    {
        if (ownsDrag && Owner && data.button != PointerEventData.InputButton.Middle) Owner.UpdateDrag(data.position);
    }

    public void OnEndDrag(PointerEventData data)
    {
        if (!ownsDrag || !Owner || data.button == PointerEventData.InputButton.Middle) return;
        data.eligibleForClick = false;
        ownsDrag = false;
        Owner.EndDragAt(data.position);
    }

    public void OnDrop(PointerEventData data)
    {
        if (hoverOnly || data.button == PointerEventData.InputButton.Middle || !Owner) return;
        var source = data.pointerDrag ? data.pointerDrag.GetComponent<InventorySlotPointer>() : null;
        if (!source || source.Owner != Owner) return;
        data.eligibleForClick = false;
        AcceptDrop();
    }

    public bool AcceptDrop()
    {
        if (hoverOnly || !Owner || !Owner.IsDragging) return false;
        return IsDropZone ? Owner.DropIntoWorld() : IsEquipment ? Owner.DropOnEquipment(Equipment) : Owner.DropOnInventory(InventoryIndex);
    }

    public void OnPointerEnter(PointerEventData data)
    {
        if (Owner && Owner.UserInterface) Owner.UserInterface.ShowItemTooltip(this, data.position);
    }

    public void OnPointerMove(PointerEventData data)
    {
        if (Owner && Owner.UserInterface) Owner.UserInterface.ShowItemTooltip(this, data.position);
    }

    public void OnPointerExit(PointerEventData data)
    {
        if (Owner && Owner.UserInterface) Owner.UserInterface.HideItemTooltip();
    }

    void OnDisable()
    {
        if (Owner && Owner.UserInterface) Owner.UserInterface.HideItemTooltip();
        if (ownsDrag && Owner) Owner.CancelDrag();
        ownsDrag = false;
    }
}
