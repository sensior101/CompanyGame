using UnityEngine;
using UnityEngine.EventSystems;

public sealed class TradePointer : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerDownHandler, IPointerClickHandler
{
    public enum Kind { Inventory, Payment, Output }
    public TradeWindow owner;
    public Kind kind;
    public int index;
    bool movedSincePress;
    public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)movedSincePress=false;}
    public void OnPointerClick(PointerEventData e){if(owner && kind==Kind.Output && e.button==PointerEventData.InputButton.Left && !movedSincePress)owner.ClickOffer(index,e.position);}
    public void OnPointerEnter(PointerEventData e){if(owner)owner.ShowTooltip(this,e.position);}
    public void OnPointerMove(PointerEventData e){if(owner)owner.ShowTooltip(this,e.position);}
    public void OnPointerExit(PointerEventData e){if(owner)owner.HideTooltip();}
    bool Matches(PointerEventData e)=>owner && e.button==(owner.DragUsesRight?PointerEventData.InputButton.Right:PointerEventData.InputButton.Left);
    public void OnBeginDrag(PointerEventData e){movedSincePress=true;if(e.button!=PointerEventData.InputButton.Middle && owner)owner.BeginDrag(kind,index,e.position,e.button==PointerEventData.InputButton.Right);}
    public void OnDrag(PointerEventData e){if(Matches(e))owner.MoveDrag(e.position);}
    public void OnEndDrag(PointerEventData e){if(Matches(e))owner.EndDrag(e.position);}
    public void OnDrop(PointerEventData e){if(Matches(e))owner.Drop(kind,index);}
}
