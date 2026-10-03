using System;
using UnityEngine;

/// <summary>
/// One side of a trade: an item asset, or cash of one face value.
/// Cash items are created at runtime, so they are named by value instead of by asset.
/// </summary>
[Serializable]
public struct TradeItem
{
    public ItemData item;
    [Tooltip("Cash face value used when no item is set, e.g. 1500.")]
    [Min(0)] public long cash;
    [Min(1)] public int count;

    public int Count => Mathf.Max(1, count);
    public bool IsValid => item || cash > 0;
    public ItemData Resolve() => item ? item : cash > 0 ? CashService.GetCurrency(cash) : null;
    public bool Matches(ItemData other) => other && (item ? other == item : other.IsCurrency && other.CurrencyValue == cash);
}

/// <summary>
/// The player gives <see cref="give"/> (left slot) and gets <see cref="get"/> (right slot).
/// Cash is just another item: cash on the left buys, cash on the right sells.
/// </summary>
[Serializable]
public sealed class TradeOffer
{
    public TradeItem give;
    public TradeItem get;
    public bool IsValid => give.IsValid && get.IsValid;
}

/// <summary>Item-for-item exchange with an NPC. One item lives on the cursor, outside inventory, until placed or cancelled.</summary>
public sealed class TradeSession
{
    public InventoryState Inventory { get; }
    public TradeOffer[] Offers { get; }
    public ItemData CursorItem { get; private set; }
    public int CursorCount { get; private set; }
    public bool HasCursorItem => CursorItem;
    public bool HasExchanged { get; private set; }
    // What Cancel gives back: the picked-up item itself, or what was paid for the cursor item.
    ItemData originalItem;
    int originalSlot=-1;
    int originalCount;
    int cursorOffer=-1;

    public TradeSession(InventoryState inventory, TradeOffer[] offers)
    {
        Inventory=inventory ?? throw new ArgumentNullException(nameof(inventory));
        Offers=offers ?? Array.Empty<TradeOffer>();
    }
    public bool HasOffer(int index)=>index>=0 && index<Offers.Length && Offers[index]!=null && Offers[index].IsValid;
    public bool TryPickUp(int source,out string error,bool single=false)
    {
        error=null;
        if(HasCursorItem){error="커서의 아이템을 먼저 놓아 주세요.";return false;}
        var stack=Inventory.GetSlot(source);
        if(stack==null || stack.IsEmpty){error="꺼낼 아이템이 없습니다.";return false;}
        CursorItem=originalItem=stack.Item;originalSlot=source;HasExchanged=false;cursorOffer=-1;
        CursorCount=originalCount=single?1:stack.Count;
        if(Inventory.TryRemove(source,CursorCount,out error))return true;
        ClearCursor();return false;
    }
    /// <summary>Pays the offer's left side from the inventory and puts its right side on the cursor. Repeating stacks on the cursor.</summary>
    public bool TryTakeOffer(int offer,out string error)
    {
        error=null;
        if(!HasOffer(offer)){error="거래할 항목을 확인해 주세요.";return false;}
        var trade=Offers[offer];var received=trade.get.Resolve();
        if(HasCursorItem && (!HasExchanged || cursorOffer!=offer || CursorCount+trade.get.Count>CursorItem.StackLimit))
        {error="커서의 아이템을 먼저 놓아 주세요.";return false;}
        var draft=Inventory.Copy();int need=trade.give.Count;int firstSlot=-1;ItemData paid=null;
        for(int i=0;i<Inventory.Capacity && need>0;i++)
        {
            var stack=draft.GetSlot(i);
            if(stack.IsEmpty || !trade.give.Matches(stack.Item))continue;
            if(firstSlot<0){firstSlot=i;paid=stack.Item;}
            int take=Math.Min(need,stack.Count);
            if(!draft.TryRemove(i,take,out error))return false;
            need-=take;
        }
        if(need>0){error=trade.give.Resolve().DisplayName+" "+trade.give.Count+"개가 필요합니다.";return false;}
        if(!HasCursorItem){originalItem=paid;originalSlot=firstSlot;originalCount=0;CursorItem=received;HasExchanged=true;cursorOffer=offer;}
        CursorCount+=trade.get.Count;originalCount+=trade.give.Count;
        Inventory.ReplaceWith(draft);Inventory.NotifyChanged();return true;
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
        if(CursorCount==0)ClearCursor();
        else
        {
            // Part of the trade is placed, so the trade stands; cancelling now returns the remaining items themselves.
            originalItem=CursorItem;originalCount=CursorCount;originalSlot=-1;HasExchanged=false;cursorOffer=-1;
        }
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
    void ClearCursor(){CursorItem=null;CursorCount=originalCount=0;originalItem=null;originalSlot=-1;HasExchanged=false;cursorOffer=-1;}
}
