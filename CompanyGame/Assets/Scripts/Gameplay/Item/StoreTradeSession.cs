using System;
using CompanyGame.World.Maps;

/// <summary>One item lives on the cursor, outside inventory, until placed or cancelled.</summary>
public sealed class StoreTradeSession
{
    public InventoryState Inventory { get; }
    public StoreOffer[] Offers { get; }
    public ItemData CursorItem { get; private set; }
    public int CursorCount { get; private set; }
    public bool HasCursorItem => CursorItem;
    public bool HasExchanged { get; private set; }
    ItemData originalItem;
    int originalSlot=-1;
    int originalCount;

    public StoreTradeSession(InventoryState inventory, StoreOffer[] offers)
    {
        Inventory=inventory ?? throw new ArgumentNullException(nameof(inventory));
        Offers=offers ?? Array.Empty<StoreOffer>();
    }
    public bool TryPickUp(int source,out string error,bool single=false)
    {
        error=null;
        if(HasCursorItem){error="커서의 아이템을 먼저 놓아 주세요.";return false;}
        var stack=Inventory.GetSlot(source);
        if(stack==null || stack.IsEmpty){error="꺼낼 아이템이 없습니다.";return false;}
        CursorItem=originalItem=stack.Item;originalSlot=source;HasExchanged=false;
        CursorCount=originalCount=single?1:stack.Count;
        if(Inventory.TryRemove(source,CursorCount,out error))return true;
        ClearCursor();return false;
    }
    public bool TryTakeOffer(int offer,out string error)
    {
        error=null;
        if(offer<0 || offer>=Offers.Length || Offers[offer]==null || !Offers[offer].item || Offers[offer].price<=0)
        {error="가져올 상품을 확인해 주세요.";return false;}
        var recipe=Offers[offer];
        if(HasCursorItem && (!HasExchanged || CursorItem!=recipe.item || originalItem.CurrencyValue!=recipe.price || CursorCount>=CursorItem.StackLimit))
        {error="커서의 아이템을 먼저 놓아 주세요.";return false;}
        for(int i=0;i<Inventory.Capacity;i++)
        {
            var payment=Inventory.GetSlot(i);
            if(payment.IsEmpty || !payment.Item.IsCurrency || payment.Item.CurrencyValue!=recipe.price)continue;
            var draft=Inventory.Copy();if(!draft.TryRemove(i,1,out error))return false;
            if(!HasCursorItem){originalItem=payment.Item;originalSlot=i;CursorItem=recipe.item;HasExchanged=true;}
            CursorCount++;originalCount++;
            Inventory.ReplaceWith(draft);Inventory.NotifyChanged();return true;
        }
        error=recipe.price.ToString("N0")+"원 화폐 1개가 필요합니다.";return false;
    }
    public bool TryPlace(int destination,out string error)
    {
        error=null;if(!CursorItem){error="커서에 아이템이 없습니다.";return false;}
        var draft=Inventory.Copy();var target=draft.GetSlot(destination);
        if(target==null || (!target.IsEmpty && (target.Item!=CursorItem || target.Count>=CursorItem.StackLimit)))
        {error="빈 칸이나 같은 아이템 칸에 놓아 주세요.";return false;}
        int amount=Math.Min(CursorCount,target.IsEmpty?CursorItem.StackLimit:CursorItem.StackLimit-target.Count);
        if(target.IsEmpty){target.Item=CursorItem;target.Count=amount;}else target.Count+=amount;
        CursorCount-=amount;
        if(CursorCount==0)ClearCursor();else originalCount=CursorCount;
        Inventory.ReplaceWith(draft);Inventory.NotifyChanged();return true;
    }
    public bool TryCancel(out string error)
    {
        error=null;if(!CursorItem)return true;
        var draft=Inventory.Copy();var source=draft.GetSlot(originalSlot);
        if(source!=null && (source.IsEmpty || (source.Item==originalItem && source.Count+originalCount<=originalItem.StackLimit)))
        {if(source.IsEmpty){source.Item=originalItem;source.Count=originalCount;}else source.Count+=originalCount;}
        else if(!draft.TryAdd(originalItem,originalCount,out error))return false;
        ClearCursor();Inventory.ReplaceWith(draft);Inventory.NotifyChanged();return true;
    }
    public void CancelPending()
    {
        if(!TryCancel(out var error))throw new InvalidOperationException(error);
    }
    void ClearCursor(){CursorItem=null;CursorCount=originalCount=0;originalItem=null;originalSlot=-1;HasExchanged=false;}
}
