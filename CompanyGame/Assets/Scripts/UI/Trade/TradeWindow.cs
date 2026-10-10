using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The trade window every NPC uses. Each row swaps the left item (what the player gives) for the right item
/// (what the player gets); cash is just an item, so the same window buys and sells. Payment happens on taking the right item.
/// </summary>
public sealed partial class TradeWindow : MonoBehaviour
{
    public TradeSession Session { get; private set; }
    public bool IsDragging => Session!=null && Session.HasCursorItem;
    public bool DragUsesRight { get; private set; }
    TMP_FontAsset font;
    RectTransform window,ghost,tooltip;
    TMP_Text tooltipText,tooltipDetails,ghostCount;
    public const int TradeCapacity=18;
    public int OfferPage { get; private set; }
    public int OfferPageCount => Math.Max(1,(Session.Offers.Length+TradeCapacity-1)/TradeCapacity);
    RectTransform previousOffers,nextOffers;
    float nextOfferRefresh;
    readonly List<Slot> inventoryViews=new List<Slot>();
    readonly List<Slot> outputViews=new List<Slot>();
    readonly List<Slot> paymentViews=new List<Slot>();
    Action onClose;
    int page,lastDropFrame=-1;
    readonly List<RaycastResult> hits=new List<RaycastResult>();
    static readonly Color Ink=new Color(.13f,.18f,.17f);
    static readonly Color Paper=new Color(1f,1f,1f,.68f);
    class Slot {public RectTransform rect;public UnityEngine.UI.Image image;public TMP_Text count;public TradePointer pointer;}

    public static TradeWindow Create(TradeSession session,TMP_FontAsset font,Action close)
    {
        var go=new GameObject("TradeCanvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));
        var ui=go.AddComponent<TradeWindow>();ui.Session=session;ui.font=font ? font : TMP_Settings.defaultFontAsset;ui.onClose=close;ui.Build();
        session.Inventory.Changed+=ui.OnInventoryChanged;ui.Refresh();return ui;
    }
    void Build()
    {
        var c=GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=300;
        var scale=GetComponent<UnityEngine.UI.CanvasScaler>();scale.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1280,720);scale.matchWidthOrHeight=.5f;
        var shade=Panel("Shade",transform,Vector2.zero,new Color(.16f,.13f,.13f,.12f));shade.anchorMin=Vector2.zero;shade.anchorMax=Vector2.one;shade.offsetMin=shade.offsetMax=Vector2.zero;
        window=Panel("TradeLayout",shade,new Vector2(680,600),Color.clear);
        window.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
        var trades=Panel("TradeWindow",window,new Vector2(672,416),Paper);trades.anchoredPosition=new Vector2(0,90);
        Border(trades);
        SymbolButton("Close",window,new Vector2(351,282),()=>onClose(),0);
        previousOffers=SymbolButton("PreviousOffers",window,new Vector2(-288,316),()=>ChangeOfferPage(-1),-1);
        nextOffers=SymbolButton("NextOffers",window,new Vector2(288,316),()=>ChangeOfferPage(1),1);
        // The reference has three columns, each containing six vertical trades.
        for(int i=0;i<TradeCapacity;i++)
        {
            var row=Panel("Offer_"+i,trades,new Vector2(192,60),Color.clear);row.anchoredPosition=new Vector2(-216+(i/6)*216,160-(i%6)*64);
            row.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
            var payment=MakeSlot("Payment_"+i,row,new Vector2(-36,0),TradePointer.Kind.Payment,i,56);
            paymentViews.Add(payment);
            bool available=HasOffer(i);
            payment.image.enabled=available;
            outputViews.Add(MakeSlot("Output_"+i,row,new Vector2(36,0),TradePointer.Kind.Output,i,56));
        }
        var storage=Panel("Inventory",window,new Vector2(672,162),Paper);storage.anchoredPosition=new Vector2(0,-211);Border(storage);
        for(int i=0;i<16;i++)inventoryViews.Add(MakeSlot("Slot_"+i,storage,new Vector2(-259+(i%8)*74,37-(i/8)*74),TradePointer.Kind.Inventory,i,66));
        if(Session.Inventory.Capacity>16)
        {SymbolButton("Previous",window,new Vector2(-351,-211),()=>ChangePage(-1),-1);SymbolButton("Next",window,new Vector2(351,-211),()=>ChangePage(1),1);}
        ghost=Panel("DragGhost",transform,new Vector2(58,58),new Color(1,1,1,0));ghost.gameObject.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
        ghost.gameObject.AddComponent<CanvasGroup>().blocksRaycasts=false;
        ghostCount=Label("Amount",ghost,"",15,Color.white,new Vector2(52,20),new Vector2(0,-20));ghostCount.alignment=TextAlignmentOptions.BottomRight;
        ghost.gameObject.SetActive(false);
        tooltip=Panel("ItemTooltip",transform,new Vector2(180,36),new Color(.16f,.12f,.10f,.96f));
        tooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
        tooltipText=Label("Name",tooltip,"",15,Color.white,new Vector2(164,30),Vector2.zero);
        tooltipText.richText=false;tooltipText.textWrappingMode=TextWrappingModes.Normal;
        tooltipDetails=Label("Details",tooltip,"",12,new Color(.88f,.88f,.88f),new Vector2(164,20),Vector2.zero);
        tooltipDetails.richText=false;tooltipDetails.textWrappingMode=TextWrappingModes.Normal;
        tooltipDetails.gameObject.SetActive(false);
        tooltip.gameObject.SetActive(false);
    }
    bool HasOffer(int index)=>Session.HasOffer(index);
    static Sprite Icon(TradeItem side)=>side.Resolve() ? side.Resolve().icon : null;
    Slot MakeSlot(string name,Transform parent,Vector2 position,TradePointer.Kind kind,int index,float size)
    {
        bool payment=kind==TradePointer.Kind.Payment;
        var r=Panel(name,parent,new Vector2(size,size),payment?Color.clear:new Color(1,1,1,.8f));r.anchoredPosition=position;
        var inside=Panel("Recess",r,new Vector2(size-4,size-4),kind==TradePointer.Kind.Output?new Color(.23f,.24f,.25f,.96f):payment?Color.clear:new Color(1,1,1,.28f));inside.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
        var icon=Panel("Icon",inside,new Vector2(size-13,size-13),Color.white).GetComponent<UnityEngine.UI.Image>();icon.preserveAspect=true;icon.useSpriteMesh=true;icon.raycastTarget=false;
        var count=Label("Amount",r,"",15,kind==TradePointer.Kind.Output?Color.white:Ink,new Vector2(size-6,20),new Vector2(0,-size*.32f));count.alignment=TextAlignmentOptions.BottomRight;
        return new Slot{rect=r,image=icon,count=count,pointer=Pointer(r,kind,index)};
    }
    TradePointer Pointer(RectTransform rect,TradePointer.Kind kind,int index)
    {var p=rect.gameObject.AddComponent<TradePointer>();p.owner=this;p.kind=kind;p.index=index;return p;}
    public void Refresh()
    {
        Session.RefreshOffers();
        OfferPage=Mathf.Clamp(OfferPage,0,OfferPageCount-1);
        previousOffers.gameObject.SetActive(OfferPageCount>1);nextOffers.gameObject.SetActive(OfferPageCount>1);
        previousOffers.GetComponent<UnityEngine.UI.Button>().interactable=OfferPage>0;
        nextOffers.GetComponent<UnityEngine.UI.Button>().interactable=OfferPage<OfferPageCount-1;
        for(int i=0;i<16;i++)
        {
            int index=page*16+i;var v=inventoryViews[i];v.pointer.index=index;var stack=Session.Inventory.GetSlot(index);
            bool has=stack!=null&&!stack.IsEmpty;
            v.image.sprite=has ? stack.Item.icon : null;v.image.enabled=has;v.image.color=Color.white;
            v.count.text=has ? stack.Count.ToString() : "";
        }
        for(int i=0;i<outputViews.Count;i++)
        {
            int index=OfferPage*TradeCapacity+i;
            var v=outputViews[i];var payment=paymentViews[i];v.pointer.index=payment.pointer.index=index;
            bool present=HasOffer(index);var offer=present?Session.Offers[index]:null;
            bool available=present && Session.IsOfferAvailable(index);
            v.image.enabled=present;v.image.sprite=present?Icon(offer.get):null;
            v.image.color=available?Color.white:new Color(.4f,.4f,.4f,.65f);
            v.count.text="";payment.count.text="";
            payment.image.enabled=present;payment.image.sprite=present?Icon(offer.give):null;
        }
        RefreshCursor();
    }
    public void ChangeOfferPage(int amount)
    {
        if(IsDragging)return;
        OfferPage=Mathf.Clamp(OfferPage+amount,0,OfferPageCount-1);HideTooltip();Refresh();
    }

    void OnInventoryChanged(){Refresh();}
    void ChangePage(int amount){page=Mathf.Clamp(page+amount,0,(Session.Inventory.Capacity-1)/16);Refresh();}
    void RefreshCursor()
    {
        if(!ghost)return;ghost.gameObject.SetActive(IsDragging);if(!IsDragging)return;
        var img=ghost.GetComponent<UnityEngine.UI.Image>();img.sprite=Session.CursorItem.icon;img.color=Color.white;img.preserveAspect=true;img.useSpriteMesh=true;
        ghostCount.text=Session.CursorCount>1?Session.CursorCount.ToString():"";
    }
    public bool BeginDrag(TradePointer.Kind kind,int index,Vector2 position,bool single=false)
    {
        HideTooltip();
        if(!IsDragging)
        {
            bool picked=kind==TradePointer.Kind.Output?!single && Session.TryTakeOffer(index,out _):kind==TradePointer.Kind.Inventory && Session.TryPickUp(index,out _,single);
            if(!picked)return false;
        }
        DragUsesRight=single;RefreshCursor();MoveDrag(position);return true;
    }
    public void ClickOffer(int index,Vector2 position)
    {
        if(!Session.TryTakeOffer(index,out _))return;
        DragUsesRight=false;HideTooltip();RefreshCursor();MoveDrag(position);
    }
    public void MoveDrag(Vector2 position)
    {if(!IsDragging)return;RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,position,null,out var p);ghost.anchoredPosition=p;ghost.SetAsLastSibling();}
    public bool Drop(TradePointer.Kind kind,int target)
    {
        // OnDrop, OnEndDrag and the release fallback can run on the same frame.
        if(!IsDragging || lastDropFrame==Time.frameCount)return false;
        lastDropFrame=Time.frameCount;
        bool ok=kind==TradePointer.Kind.Inventory && Session.TryPlace(target,out _);
        Refresh();return ok;
    }
    public void EndDrag(Vector2 position)
    {
        if(!IsDragging || lastDropFrame==Time.frameCount)return;
        if(!Application.isFocused || position.x<0 || position.y<0 || position.x>=Screen.width || position.y>=Screen.height){CancelDrag();return;}
        var pointer=PointerAt(position);
        if(pointer){Drop(pointer.kind,pointer.index);return;}
        // Keep carrying the item when released over background or a panel gap.
    }
    public void CancelDrag(){Session?.CancelPending();RefreshCursor();}
    TradePointer PointerAt(Vector2 position)
    {
        hits.Clear();
        if(EventSystem.current)EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=position},hits);
        foreach(var hit in hits)
        {
            var pointer=hit.gameObject.GetComponentInParent<TradePointer>();
            if(pointer && pointer.owner==this)return pointer;
            if(hit.module is UnityEngine.UI.GraphicRaycaster)return null;
        }
        return null;
    }
    void Update()
    {
        if(Time.unscaledTime>=nextOfferRefresh) { nextOfferRefresh=Time.unscaledTime+.25f;Refresh(); }
        if(GameInput.HasPointer)
        {
            var position=GameInput.PointerPosition;
            if(IsDragging){MoveDrag(position);if(GameInput.ButtonReleased(DragUsesRight))EndDrag(position);}
            else
            {
                // Re-evaluate after drops, page refreshes and external inventory changes,
                // even when the mouse stays still on the same slot.
                var pointer=PointerAt(position);
                if(pointer)ShowTooltip(pointer,position);else HideTooltip();
            }
        }
        // Keep two full rows inside small game views as well as wide desktop views.
        var bounds=((RectTransform)transform).rect;float s=Mathf.Min(1,Mathf.Min((bounds.width-24)/740,(bounds.height-24)/660));window.localScale=Vector3.one*Mathf.Max(.1f,s);
    }
    public void ShowTooltip(TradePointer pointer,Vector2 screenPosition)
    {
        if(IsDragging){HideTooltip();return;}
        string name=null,details="";
        if(pointer.kind==TradePointer.Kind.Inventory)
        {var stack=Session.Inventory.GetSlot(pointer.index);if(stack!=null&&!stack.IsEmpty){name=stack.Tooltip;details=stack.TooltipDetails;}}
        else if(HasOffer(pointer.index))
        {
            var side=Session.Offers[pointer.index].give;
            name=pointer.kind==TradePointer.Kind.Output?Session.OutputTooltip(pointer.index):
                (!side.item || side.item.IsCurrency)?((side.item?side.item.CurrencyValue:side.cash)*side.Count).ToString("N0")+"원":side.Tooltip;
        }
        if(string.IsNullOrEmpty(name)){HideTooltip();return;}
        tooltipText.richText=false;tooltipText.text=name;
        tooltipDetails.text=details;bool hasDetails=!string.IsNullOrEmpty(details);tooltipDetails.gameObject.SetActive(hasDetails);
        float width=Mathf.Clamp(Mathf.Max(tooltipText.GetPreferredValues(name).x,hasDetails?tooltipDetails.GetPreferredValues(details).x:0)+24,80,260);
        float titleHeight=tooltipText.GetPreferredValues(name,width-16,0).y;
        float detailHeight=hasDetails?tooltipDetails.GetPreferredValues(details,width-16,0).y:0;
        float height=Mathf.Max(36,titleHeight+(hasDetails?detailHeight+6:0)+16);
        tooltip.sizeDelta=new Vector2(width,height);
        tooltipText.rectTransform.sizeDelta=new Vector2(width-16,hasDetails?titleHeight:height-8);
        tooltipText.rectTransform.anchoredPosition=hasDetails?new Vector2(0,(height-titleHeight)*.5f-8):Vector2.zero;
        tooltipDetails.rectTransform.sizeDelta=new Vector2(width-16,detailHeight);
        tooltipDetails.rectTransform.anchoredPosition=new Vector2(0,height*.5f-14-titleHeight-detailHeight*.5f);
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,screenPosition,null,out var p);
        var bounds=((RectTransform)transform).rect;var half=tooltip.sizeDelta*.5f;p+=new Vector2(half.x+16,-half.y-18);
        p.x=Mathf.Clamp(p.x,bounds.xMin+half.x+6,bounds.xMax-half.x-6);p.y=Mathf.Clamp(p.y,bounds.yMin+half.y+6,bounds.yMax-half.y-6);
        tooltip.anchoredPosition=p;tooltip.gameObject.SetActive(true);tooltip.SetAsLastSibling();
    }
    public void HideTooltip(){if(tooltip)tooltip.gameObject.SetActive(false);}
    void OnApplicationFocus(bool focused){if(!focused){CancelDrag();HideTooltip();}}
    void OnDisable(){CancelDrag();HideTooltip();Session?.CancelPending();}
    void OnDestroy(){if(Session!=null){Session.Inventory.Changed-=OnInventoryChanged;Session.CancelPending();}}
    static RectTransform Panel(string name,Transform parent,Vector2 size,Color color)
    {var go=new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image));var r=(RectTransform)go.transform;r.SetParent(parent,false);r.sizeDelta=size;go.GetComponent<UnityEngine.UI.Image>().color=color;return r;}
    TMP_Text Label(string name,Transform parent,string text,float size,Color color,Vector2 dimensions,Vector2 position)
    {var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));var t=go.GetComponent<TextMeshProUGUI>();t.font=font;t.text=text;t.fontSize=size;t.color=color;t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.NoWrap;t.rectTransform.SetParent(parent,false);t.rectTransform.sizeDelta=dimensions;t.rectTransform.anchoredPosition=position;return t;}
    static void Border(RectTransform parent)
    {
        foreach(bool horizontal in new[]{true,false})foreach(int side in new[]{-1,1})
        {var line=Panel("Border",parent,horizontal?new Vector2(parent.sizeDelta.x,2):new Vector2(2,parent.sizeDelta.y),new Color(1,1,1,.85f));line.anchoredPosition=horizontal?new Vector2(0,side*parent.sizeDelta.y*.5f):new Vector2(side*parent.sizeDelta.x*.5f,0);line.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;}
    }
    RectTransform SymbolButton(string name,Transform parent,Vector2 position,Action action,int direction)
    {
        var r=Panel(name,parent,new Vector2(26,28),Paper);r.anchoredPosition=position;r.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(()=>action());
        for(int i=0;i<2;i++){var line=Panel("Stroke",r,new Vector2(direction==0?15:10,2),Ink);line.localEulerAngles=new Vector3(0,0,i==0?45:-45);if(direction!=0)line.anchoredPosition=new Vector2(0,(i==0?-1:1)*direction*3.2f);line.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;}
        return r;
    }
}
