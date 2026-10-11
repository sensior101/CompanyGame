using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Cart and wallet UI of the trade window: a top bar, a cart overlay and a withdrawal popup.</summary>
public sealed partial class TradeWindow
{
    class CartRow {public RectTransform root;public Image icon;public TMP_Text name,subtotal;public CartLine line;}
    static readonly Color Warn=new Color(.64f,.23f,.2f);
    readonly System.Collections.Generic.List<CartRow> cartRows=new System.Collections.Generic.List<CartRow>();
    RectTransform cartView,withdrawModal;
    Graphic cartButtonImage;
    RectTransform walletButton;
    TMP_Text cartLabel,walletLabel,statusLabel,emptyLabel,totalLabel,carriedLabel,walletInfoLabel,shortfallLabel,withdrawError;
    Button checkoutButton,withdrawConfirm;
    TMP_InputField amountInput,quantityInput;
    float statusUntil;

    public void AddOfferToCart(int index)
    {
        if(Busy())return;
        if(!Session.TryAddToCart(index,out var error))Say(error,true);
        Refresh();
    }
    void Say(string message,bool error=false)
    {
        if(!statusLabel)return;
        statusLabel.text=message;statusLabel.color=error?Warn:Ink;statusUntil=Time.unscaledTime+2.5f;
    }
    bool Busy()
    {
        if(!IsDragging)return false;
        Say("커서의 아이템을 먼저 놓아 주세요.",true);return true;
    }
    static void NoRay(RectTransform r){r.GetComponent<Image>().raycastTarget=false;}
    // Inventory-style rounded widgets (same look as InventoryUI.Panel/MakeButton/Icon).
    static readonly Color Brown=new Color(.29f,.205f,.19f);
    static readonly Color HintColor=new Color(.54f,.46f,.425f);
    static RectTransform RPanel(string name,Transform parent,Vector2 size,Color color,float large,float small)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(InventoryRoundedGraphic));var r=(RectTransform)go.transform;r.SetParent(parent,false);r.sizeDelta=size;
        var g=go.GetComponent<InventoryRoundedGraphic>();g.color=color;g.largeRadius=large;g.smallRadius=small;g.borderWidth=large>0?1.5f:0f;g.raycastTarget=false;return r;
    }
    static Button RButton(RectTransform r,Action action)
    {
        var g=r.GetComponent<InventoryRoundedGraphic>();g.raycastTarget=true;
        var b=r.gameObject.AddComponent<Button>();b.targetGraphic=g;
        var c=b.colors;c.normalColor=Color.white;c.highlightedColor=new Color(1,.975f,.92f);c.selectedColor=Color.white;c.pressedColor=new Color(.89f,.83f,.75f);c.disabledColor=new Color(.76f,.76f,.76f,.68f);b.colors=c;
        var n=b.navigation;n.mode=Navigation.Mode.None;b.navigation=n;b.onClick.AddListener(()=>action());return b;
    }
    static void At(RectTransform r,bool top,float x,float y){r.anchorMin=r.anchorMax=new Vector2(.5f,top?1:0);r.anchoredPosition=new Vector2(x,y);}
    Button TextButton(string name,Transform parent,string text,Vector2 size,Vector2 position,Action action,float fontSize=13)
    {
        var r=RPanel(name,parent,size,new Color(.9f,.865f,.82f,.85f),15,7);r.anchoredPosition=position;
        var b=RButton(r,action);Label("Text",r,text,fontSize,Brown,size,Vector2.zero);return b;
    }
    TMP_Text Left(TMP_Text t){t.alignment=TextAlignmentOptions.MidlineLeft;return t;}

    void BuildCart()
    {
        var cartButton=RPanel("CartButton",window,new Vector2(230,51),new Color(1,.99f,.96f,.66f),24,12);cartButton.anchoredPosition=new Vector2(-147.5f,327);
        RButton(cartButton,ToggleCartView);cartButtonImage=cartButton.GetComponent<InventoryRoundedGraphic>();
        var cart=Panel("CartIcon",cartButton,new Vector2(30,30),Color.clear);cart.anchoredPosition=new Vector2(-90,0);NoRay(cart);
        foreach(var (size,pos) in new[]{(new Vector2(9,3),new Vector2(-11,10)),(new Vector2(20,13),new Vector2(3,2)),(new Vector2(4,4),new Vector2(-2,-10)),(new Vector2(4,4),new Vector2(10,-10))})
        {var part=Panel("Part",cart,size,Brown);part.anchoredPosition=pos;NoRay(part);}
        cartLabel=Label("Text",cartButton,"",20,Brown,new Vector2(160,43),new Vector2(20,0));cartLabel.fontStyle=FontStyles.Bold;cartLabel.enableAutoSizing=true;cartLabel.fontSizeMin=12;cartLabel.fontSizeMax=20;
        // Same wallet pill as the inventory (InventoryUI.BuildWalletDisplay).
        walletButton=RPanel("BankBalanceButton",window,new Vector2(285,51),new Color(1,.99f,.96f,.66f),24,12);walletButton.anchoredPosition=new Vector2(120,327);
        RButton(walletButton,OpenWithdraw);
        var coin=new GameObject("BankCoin",typeof(RectTransform),typeof(InventoryGlyphGraphic)).GetComponent<InventoryGlyphGraphic>();
        coin.kind=InventoryGlyphGraphic.Glyph.Coin;coin.color=new Color(.94f,.65f,.18f);coin.raycastTarget=false;
        coin.rectTransform.SetParent(walletButton,false);coin.rectTransform.sizeDelta=new Vector2(30,30);coin.rectTransform.anchoredPosition=new Vector2(-113,0);
        walletLabel=Label("BankBalance",walletButton,"",23,Brown,new Vector2(218,43),new Vector2(20,0));walletLabel.fontStyle=FontStyles.Bold;walletLabel.enableAutoSizing=true;walletLabel.fontSizeMin=12;walletLabel.fontSizeMax=23;
        statusLabel=Label("Status",window,"",13,Ink,new Vector2(620,22),new Vector2(0,-314));
        BuildCartView();BuildWithdrawModal();
    }
    void BuildCartView()
    {
        cartView=Panel("CartView",window,new Vector2(672,416),new Color(1,1,1,.97f));cartView.anchoredPosition=new Vector2(0,90);Border(cartView);
        for(int i=0;i<TradeSession.CartMaxLines;i++)
        {
            var row=new CartRow{root=Panel("CartRow_"+i,cartView,new Vector2(640,30),Color.clear)};NoRay(row.root);row.root.anchoredPosition=new Vector2(0,170-i*32);
            row.icon=Panel("Icon",row.root,new Vector2(28,28),Color.white).GetComponent<Image>();row.icon.rectTransform.anchoredPosition=new Vector2(-300,0);row.icon.preserveAspect=true;row.icon.raycastTarget=false;
            row.name=Left(Label("Name",row.root,"",14,Ink,new Vector2(280,28),new Vector2(-150,0)));
            row.subtotal=Label("Subtotal",row.root,"",14,Ink,new Vector2(120,28),new Vector2(70,0));row.subtotal.alignment=TextAlignmentOptions.MidlineRight;
            TextButton("Minus",row.root,"-",new Vector2(30,26),new Vector2(170,0),()=>ChangeLine(row,-1));
            TextButton("Plus",row.root,"+",new Vector2(30,26),new Vector2(206,0),()=>ChangeLine(row,1));
            TextButton("Remove",row.root,"삭제",new Vector2(50,26),new Vector2(255,0),()=>{if(Busy())return;Session.RemoveFromCart(row.line);Refresh();},12);
            cartRows.Add(row);
        }
        emptyLabel=Label("Empty",cartView,"물건을 우클릭하면 담깁니다.",15,Ink,new Vector2(400,30),new Vector2(0,60));
        totalLabel=Left(Label("Total",cartView,"",16,Ink,new Vector2(360,26),new Vector2(-140,-90)));
        carriedLabel=Left(Label("Carried",cartView,"",14,Ink,new Vector2(360,24),new Vector2(-140,-118)));
        walletInfoLabel=Left(Label("Wallet",cartView,"",14,Ink,new Vector2(360,24),new Vector2(-140,-144)));
        shortfallLabel=Left(Label("Shortfall",cartView,"",14,Warn,new Vector2(360,24),new Vector2(-140,-170)));
        checkoutButton=TextButton("Checkout",cartView,"결제",new Vector2(110,32),new Vector2(165,-150),Checkout,15);
        TextButton("Clear",cartView,"비우기",new Vector2(80,32),new Vector2(265,-150),()=>{if(Busy())return;Session.ClearCart();Refresh();},14);
        cartView.gameObject.SetActive(false);
    }
    void BuildWithdrawModal()
    {
        withdrawModal=UIRect("WithdrawModal",window,new Vector2(720,720));
        var blocker=RPanel("Blocker",withdrawModal,new Vector2(720,720),Color.clear,0,0);RButton(blocker,CloseWithdraw);
        var card=RPanel("WithdrawalWindow",withdrawModal,new Vector2(320,234),new Color(.995f,.982f,.955f,.99f),30,14);
        card.GetComponent<InventoryRoundedGraphic>().raycastTarget=true;card.pivot=new Vector2(.5f,1);
        card.anchoredPosition=walletButton.anchoredPosition+new Vector2(0,-walletButton.sizeDelta.y*.5f-8);
        var title=Label("WithdrawalTitle",card,"출금",23,Brown,new Vector2(288,40),Vector2.zero);title.fontStyle=FontStyles.Bold;card.GetComponent<RectTransform>();TopAt(title.rectTransform,0,-25);
        TopAt(Left(Label("AmountLabel",card,"금액",15,Brown,new Vector2(176,28),Vector2.zero)).rectTransform,-56,-59);
        TopAt(Left(Label("QuantityLabel",card,"장수",15,Brown,new Vector2(100,28),Vector2.zero)).rectTransform,94,-59);
        amountInput=NumberField("WithdrawalAmount",card,new Vector2(176,48),"개당 금액",19);TopAt(amountInput.GetComponent<RectTransform>(),-56,-99);
        quantityInput=NumberField("WithdrawalQuantity",card,new Vector2(100,48),"1",9);TopAt(quantityInput.GetComponent<RectTransform>(),94,-99);
        var an=amountInput.navigation;an.mode=Navigation.Mode.Explicit;an.selectOnRight=quantityInput;an.selectOnDown=quantityInput;amountInput.navigation=an;
        var qn=quantityInput.navigation;qn.mode=Navigation.Mode.Explicit;qn.selectOnLeft=amountInput;qn.selectOnUp=amountInput;quantityInput.navigation=qn;
        withdrawError=Label("WithdrawalError",card,"",12,Warn,new Vector2(288,40),Vector2.zero);withdrawError.textWrappingMode=TextWrappingModes.Normal;TopAt(withdrawError.rectTransform,0,-151);
        var cancel=TextButton("CancelWithdrawal",card,"취소",new Vector2(100,38),Vector2.zero,CloseWithdraw,15);BottomAt(cancel.GetComponent<RectTransform>(),-79,31);
        withdrawConfirm=TextButton("ConfirmWithdrawal",card,"확인 (Enter)",new Vector2(137,38),Vector2.zero,SubmitWithdraw,14);
        var confirmRect=withdrawConfirm.GetComponent<RectTransform>();BottomAt(confirmRect,54,31);
        confirmRect.GetComponent<InventoryRoundedGraphic>().color=new Color(.72f,.49f,.33f,1);
        confirmRect.Find("Text").GetComponent<TMP_Text>().color=Color.white;
        withdrawModal.gameObject.SetActive(false);
    }
    static void TopAt(RectTransform r,float x,float y){r.anchorMin=r.anchorMax=new Vector2(.5f,1);r.anchoredPosition=new Vector2(x,y);}
    static void BottomAt(RectTransform r,float x,float y){r.anchorMin=r.anchorMax=new Vector2(.5f,0);r.anchoredPosition=new Vector2(x,y);}
    static RectTransform UIRect(string name,Transform parent,Vector2 size)
    {var r=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;r.SetParent(parent,false);r.sizeDelta=size;return r;}
    TMP_InputField NumberField(string name,Transform parent,Vector2 size,string placeholder,int limit)
    {
        var rect=RPanel(name,parent,size,new Color(1,1,1,.97f),19,8);rect.GetComponent<InventoryRoundedGraphic>().raycastTarget=true;
        var viewport=UIRect("TextViewport",rect,size-new Vector2(24,10));viewport.gameObject.AddComponent<RectMask2D>();
        var text=Left(Label("Text",viewport,"",18,Brown,size-new Vector2(24,10),Vector2.zero));text.overflowMode=TextOverflowModes.Overflow;
        var hint=Left(Label("Placeholder",viewport,placeholder,15,HintColor,size-new Vector2(24,10),Vector2.zero));
        foreach(var t in new[]{text,hint}){t.rectTransform.anchorMin=Vector2.zero;t.rectTransform.anchorMax=Vector2.one;t.rectTransform.offsetMin=t.rectTransform.offsetMax=Vector2.zero;}
        var input=rect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic=rect.GetComponent<InventoryRoundedGraphic>();input.textViewport=viewport;input.textComponent=(TextMeshProUGUI)text;input.placeholder=hint;
        input.contentType=TMP_InputField.ContentType.IntegerNumber;input.lineType=TMP_InputField.LineType.SingleLine;
        input.caretColor=Brown;input.customCaretColor=true;input.selectionColor=new Color(.84f,.68f,.46f,.45f);
        input.characterValidation=TMP_InputField.CharacterValidation.Digit;input.characterLimit=limit;
        input.onValueChanged.AddListener(_=>ValidateWithdraw());input.onSubmit.AddListener(_=>SubmitWithdraw());
        return input;
    }

    void ToggleCartView()
    {
        if(Busy())return;
        HideTooltip();cartView.gameObject.SetActive(!cartView.gameObject.activeSelf);Refresh();
    }
    void ChangeLine(CartRow row,int delta)
    {
        if(Busy())return;
        if(!Session.TryChangeCart(row.line,delta,out var error))Say(error,true);
        Refresh();
    }
    void Checkout()
    {
        if(Busy())return;
        int kinds=Session.Cart.Count,items=Session.CartItemCount;long total=Session.CartTotal;
        if(Session.TryCheckout(out var error))Say(kinds+"종 "+items+"개를 "+total.ToString("N0")+"원에 구매했습니다.");else Say(error,true);
        Refresh();
    }
    void OpenWithdraw()
    {
        if(Busy())return;
        HideTooltip();withdrawModal.gameObject.SetActive(true);withdrawModal.SetAsLastSibling();
        amountInput.SetTextWithoutNotify("");quantityInput.SetTextWithoutNotify("1");withdrawError.text="";
        ValidateWithdraw();amountInput.Select();amountInput.ActivateInputField();
    }
    void CloseWithdraw()
    {
        amountInput.DeactivateInputField();quantityInput.DeactivateInputField();withdrawModal.gameObject.SetActive(false);
        if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
    }
    // Same rules as InventoryUI.TryWithdrawalValues.
    bool TryWithdrawValues(out long amount,out int quantity,out string error)
    {
        error="";
        bool a=long.TryParse(amountInput.text,NumberStyles.None,CultureInfo.InvariantCulture,out amount);
        bool q=int.TryParse(quantityInput.text,NumberStyles.None,CultureInfo.InvariantCulture,out quantity);
        if(!a || amount<0){error="한 개의 금액을 0원 이상으로 입력해 주세요.";return false;}
        if(!q || quantity<1){error="장수를 1 이상으로 입력해 주세요.";return false;}
        if(amount>long.MaxValue/quantity){error="출금할 합계 금액이 너무 큽니다.";return false;}
        return true;
    }
    void ValidateWithdraw()
    {
        if(!withdrawConfirm)return;
        bool valid=TryWithdrawValues(out long amount,out int quantity,out string error);
        bool affordable=valid && amount*quantity<=CashService.BankBalance;
        withdrawConfirm.interactable=affordable;
        withdrawError.text=!valid ? (string.IsNullOrEmpty(amountInput.text)?"":error) : affordable ? "" : "지갑 잔액이 부족합니다.";
    }
    void SubmitWithdraw()
    {
        if(!withdrawModal.gameObject.activeSelf || !withdrawConfirm.interactable)return;
        if(!TryWithdrawValues(out long amount,out int quantity,out string error)){withdrawError.text=error;return;}
        if(!CashService.TryWithdraw(Session.Inventory,amount,quantity,out error)){withdrawError.text=error;return;}
        CloseWithdraw();Say(amount.ToString("N0")+"원 × "+quantity+(amount<10000?"개를 꺼냈습니다.":"장을 꺼냈습니다."));Refresh();
    }

    void RefreshCart()
    {
        if(!cartView)return;
        var s=Session;
        cartLabel.text=s.CartItemCount+"개 · "+s.CartTotal.ToString("N0")+"원";
        walletLabel.text="지갑  "+CashService.BankBalance.ToString("N0")+"원";
        cartButtonImage.color=cartView.gameObject.activeSelf?new Color(.85f,.78f,.65f,.85f):new Color(1,.99f,.96f,.66f);
        if(Time.unscaledTime>statusUntil && statusLabel.text.Length>0)statusLabel.text="";
        if(cartView.gameObject.activeSelf)RefreshCartView();
    }
    void RefreshCartView()
    {
        var s=Session;long total=s.CartTotal,carried=CashService.CarriedTotal(s.Inventory),shortfall=s.CartShortfall;
        for(int i=0;i<cartRows.Count;i++)
        {
            var r=cartRows[i];bool on=i<s.Cart.Count;r.root.gameObject.SetActive(on);if(!on)continue;
            r.line=s.Cart[i];var get=r.line.offer.get;
            r.icon.sprite=Icon(get);r.icon.enabled=r.icon.sprite;
            r.name.text=get.Tooltip+" × "+get.Count*r.line.count;r.subtotal.text=s.LineTotal(r.line).ToString("N0")+"원";
        }
        emptyLabel.gameObject.SetActive(s.Cart.Count==0);
        totalLabel.text="합계 "+total.ToString("N0")+"원";
        carriedLabel.text="소지 현금 "+carried.ToString("N0")+"원";
        walletInfoLabel.text="지갑 "+CashService.BankBalance.ToString("N0")+"원";
        shortfallLabel.text=shortfall>0?"부족 "+shortfall.ToString("N0")+"원 (지갑에서 꺼내 주세요)":"";
        checkoutButton.interactable=s.Cart.Count>0 && carried>=total;
    }
}
