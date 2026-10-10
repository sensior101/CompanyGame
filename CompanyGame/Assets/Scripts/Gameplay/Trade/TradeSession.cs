using System;
using System.Collections.Generic;
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
    public BookInstanceData bookTemplate;
    public bool IsBookTemplate => item && item.IsBook && bookTemplate != null && bookTemplate.isPublished;

    public int Count => Mathf.Max(1, count);
    public bool IsFree => !item && cash == 0;
    public bool IsValid => item || cash >= 0;
    public ItemData Resolve() => item ? item : cash >= 0 ? CashService.GetCurrency(cash) : null;
    public string Tooltip => IsBookTemplate ? bookTemplate.Tooltip(item.DisplayName) : Resolve()?.DisplayName;
    public bool Matches(ItemData other) => other && (item ? other == item : other.IsCurrency && other.CurrencyValue == cash);
    public bool Matches(ItemStack stack) => stack != null && !stack.IsEmpty && Matches(stack.Item) && !stack.IsUniqueBook;
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
    public bool IsValid => give.IsValid && get.IsValid && !get.IsFree;
    // Domain state is committed only after the common cursor is placed in inventory.
    [NonSerialized] public ITradeFulfillment fulfillment;
    [NonSerialized] public Func<bool> isRented;

    /// <summary>Office registration hook: snapshot an author's book as a reusable sale template.</summary>
    public static bool TryCreateBookTemplate(ItemStack source, string ownerId, long price, out TradeOffer offer)
    {
        offer = null;
        if (source == null || !source.IsUniqueBook || !source.BookData.isPublished ||
            !source.BookData.IsAuthor(ownerId) || price <= 0) return false;
        offer = new TradeOffer
        {
            give = new TradeItem { cash = price, count = 1 },
            get = new TradeItem { item = source.Item, count = 1, bookTemplate = source.BookData.Clone() }
        };
        return true;
    }
}

/// <summary>Item-for-item exchange with an NPC. One item lives on the cursor, outside inventory, until placed or cancelled.</summary>
public interface ITradeFulfillment
{
    bool CanTake(out string error);
    ItemStack CreateItem();
    void Complete(ItemStack received);
}

public class TradeSession
{
    static readonly HashSet<TradeSession> pending = new HashSet<TradeSession>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPending() => pending.Clear();

    // The cursor is real ownership too. Include it when the inventory persists so
    // closing the application mid-drag cannot erase a book or an entire blank stack.
    public static IEnumerable<ItemStack> PendingBooks(InventoryState inventory)
    {
        foreach (var session in pending)
            if (ReferenceEquals(session.Inventory, inventory) && session.CursorItem && session.CursorItem.IsBook && session.cursorTrade?.fulfillment == null)
                yield return new ItemStack(session.CursorItem, session.CursorCount)
                    { InstanceId = session.CursorInstanceId, BookData = session.CursorBookData?.Clone() };
    }
    public InventoryState Inventory { get; }
    public TradeOffer[] Offers { get; private set; }
    readonly Func<TradeOffer[]> offerProvider;
    TradeOffer cursorTrade;
    int cursorTradeCount;
    public ItemStack HeldStack => !CursorItem ? null : new ItemStack(CursorItem, CursorCount, CursorInstanceId) { BookData = CursorBookData?.Clone() };
    public virtual bool IsOfferAvailable(int index) => HasOffer(index) && (Offers[index].fulfillment == null || Offers[index].fulfillment.CanTake(out _));
    public virtual string OutputTooltip(int index) => HasOffer(index) ? Offers[index].get.Tooltip : null;
    public void RefreshOffers() { if (offerProvider != null) Offers = offerProvider() ?? Array.Empty<TradeOffer>(); }
    public ItemData CursorItem { get; private set; }
    public int CursorCount { get; private set; }
    public string CursorInstanceId { get; private set; }
    public BookInstanceData CursorBookData { get; private set; }
    public bool HasCursorItem => CursorItem;
    public bool HasExchanged { get; private set; }
    int CursorStackLimit => CursorBookData != null ? 1 : CursorItem ? CursorItem.StackLimit : 0;
    // What Cancel gives back: the picked-up item itself, or what was paid for the cursor item.
    ItemData originalItem;
    string originalInstanceId;
    BookInstanceData originalBookData;
    int originalSlot=-1;
    int originalCount;
    int cursorOffer=-1;

    public TradeSession(InventoryState inventory, TradeOffer[] offers)
    {
        Inventory=inventory ?? throw new ArgumentNullException(nameof(inventory));
        Offers=offers ?? Array.Empty<TradeOffer>();
    }
    public TradeSession(InventoryState inventory, Func<TradeOffer[]> offers) : this(inventory, Array.Empty<TradeOffer>())
    { offerProvider = offers; RefreshOffers(); }
    public bool ConsumeHandoff()
    {
        if (!HasCursorItem || HasExchanged) return false;
        ClearCursor(); Inventory.NotifyChanged(); return true;
    }
    public bool HasOffer(int index)=>index>=0 && index<Offers.Length && Offers[index]!=null && Offers[index].IsValid;
    public bool TryPickUp(int source,out string error,bool single=false)
        => PickUp(source,out error,single,false);

    /// <summary>Filtered, single-item delivery to an NPC. Protected items remain blocked in ordinary trades.</summary>
    public bool TryPickUpForHandoff(int source,Predicate<ItemStack> filter,out string error)
    {
        error=null;
        var stack=Inventory.GetSlot(source);
        if(!GameSession.IsAuthority || Offers.Length!=0 || offerProvider!=null || filter==null ||
            stack==null || stack.IsEmpty || !filter(stack))
        { error="전달할 수 없는 아이템입니다.";return false; }
        return PickUp(source,out error,true,true);
    }

    bool PickUp(int source,out string error,bool single,bool handoff)
    {
        error=null;
        if(HasCursorItem){error="커서의 아이템을 먼저 놓아 주세요.";return false;}
        var stack=Inventory.GetSlot(source);
        if(stack==null || stack.IsEmpty || (!handoff && (stack.BookData?.IsLibraryLoan ?? false))){error="꺼낼 아이템이 없습니다.";return false;}
        CursorItem=originalItem=stack.Item;originalSlot=source;HasExchanged=false;cursorOffer=-1;
        CursorInstanceId=originalInstanceId=stack.InstanceId;
        CursorBookData=originalBookData=stack.BookData?.Clone();
        CursorCount=originalCount=single?1:stack.Count;
        pending.Add(this);
        if(Inventory.TryRemove(source,CursorCount,out error))return true;
        ClearCursor();return false;
    }
    /// <summary>Pays the offer's left side from the inventory and puts its right side on the cursor. Repeating stacks on the cursor.</summary>
    public bool TryTakeOffer(int offer,out string error)
    {
        error=null;
        if(!GameSession.IsAuthority){error="거래는 호스트의 승인이 필요합니다.";return false;}
        if(!HasOffer(offer)){error="거래할 항목을 확인해 주세요.";return false;}
        var trade=Offers[offer];var received=trade.get.Resolve();
        if (!IsOfferAvailable(offer)) { error="지금은 거래할 수 없는 아이템입니다."; return false; }
        if (trade.fulfillment != null)
        {
            if (HasCursorItem) { error="커서의 아이템을 먼저 놓아 주세요."; return false; }
            var stack=trade.fulfillment.CreateItem();
            if (stack == null || stack.IsEmpty) { error="거래할 아이템이 없습니다."; return false; }
            CursorItem=stack.Item; CursorCount=stack.Count; CursorInstanceId=stack.InstanceId; CursorBookData=stack.BookData?.Clone();
            HasExchanged=true; cursorOffer=offer; cursorTrade=trade; cursorTradeCount=1; pending.Add(this); return true;
        }
        if ((received.IsVehicle && trade.get.Count != 1) || (trade.give.Resolve().IsVehicle && trade.give.Count != 1))
        { error="탈것은 한 대씩 거래해 주세요.";return false; }
        if (trade.get.IsBookTemplate && trade.get.Count != 1)
        { error="책은 한 권씩만 거래할 수 있습니다."; return false; }
        if(HasCursorItem && (!HasExchanged || cursorOffer!=offer || trade.give.Resolve().IsVehicle || CursorCount+trade.get.Count>CursorStackLimit))
        {error="커서의 아이템을 먼저 놓아 주세요.";return false;}
        var draft=Inventory.Copy();
        if(!TryPay(draft,trade.give,out int firstSlot,out var paid,out error))return false;
        if(paid!=null){originalInstanceId=paid.InstanceId;originalBookData=paid.BookData?.Clone();}
        if(!HasCursorItem)
        {
            originalItem=paid?.Item;originalSlot=firstSlot;originalCount=0;CursorItem=received;HasExchanged=true;cursorOffer=offer;
            CursorInstanceId=trade.get.IsBookTemplate || received.IsVehicle ? Guid.NewGuid().ToString("N") : null;
            CursorBookData=trade.get.IsBookTemplate?trade.get.bookTemplate.Clone():null;
        }
        CursorCount+=trade.get.Count;originalCount+=trade.give.IsFree?0:trade.give.Count;
        cursorTrade=trade;cursorTradeCount++;
        pending.Add(this);
        Inventory.ReplaceWith(draft);Inventory.NotifyChanged();return true;
    }
    public bool TryPlace(int destination,out string error)
    {
        error=null;if(!CursorItem){error="커서에 아이템이 없습니다.";return false;}
        if(HasExchanged && !GameSession.IsAuthority){error="거래는 호스트의 승인이 필요합니다.";return false;}
        if (cursorTrade?.fulfillment != null)
        {
            if (!cursorTrade.fulfillment.CanTake(out error)) return false;
            var purchase=Inventory.Copy();
            if (!TryPay(purchase,cursorTrade.give,out _,out _,out error)) return false;
            var held=HeldStack;
            if(!purchase.TryPlaceStack(destination,held,false,out _,out error))return false;
            var completion=cursorTrade.fulfillment;
            var committedTrade=cursorTrade;
            ClearCursor();Inventory.ReplaceWith(purchase);completion.Complete(held);Inventory.NotifyChanged();
            NotifyCashTrade(committedTrade,1);return true;
        }
        var draft=Inventory.Copy();
        if(!draft.TryPlaceStack(destination,HeldStack,true,out int amount,out error))return false;
        var committedOffer=cursorTrade;int committedCount=cursorTradeCount;
        CursorCount-=amount;
        if(CursorCount==0)ClearCursor();
        else
        {
            // Part of the trade is placed, so the trade stands; cancelling now returns the remaining items themselves.
            originalItem=CursorItem;originalCount=CursorCount;originalSlot=-1;HasExchanged=false;cursorOffer=-1;
            originalInstanceId=CursorInstanceId;originalBookData=CursorBookData?.Clone();
            cursorTrade=null;cursorTradeCount=0;
        }
        Inventory.ReplaceWith(draft);Inventory.NotifyChanged();NotifyCashTrade(committedOffer,committedCount);return true;
    }
    public bool TryCancel(out string error)
    {
        error=null;if(!CursorItem)return true;
        if(cursorTrade?.fulfillment != null || originalCount==0) { ClearCursor();Inventory.NotifyChanged();return true; }
        var draft=Inventory.Copy();
        var refund=new ItemStack(originalItem,originalCount,originalInstanceId){BookData=originalBookData?.Clone()};
        if(draft.TryPlaceStack(originalSlot,refund,false,out _,out _)) { }
        else if(originalItem.IsBook && originalBookData != null)
        {
            if(!draft.TryAddBookInstance(originalItem,originalInstanceId,originalBookData,out error))return false;
        }
        else if(!draft.TryAdd(originalItem,originalCount,out error,originalInstanceId))return false;
        ClearCursor();Inventory.ReplaceWith(draft);Inventory.NotifyChanged();return true;
    }
    public void CancelPending()
    {
        if(!TryCancel(out var error))throw new InvalidOperationException(error);
    }
    void ClearCursor()
    {
        pending.Remove(this);
        CursorItem=null;CursorCount=originalCount=0;originalItem=null;originalSlot=-1;HasExchanged=false;cursorOffer=-1;
        CursorInstanceId=originalInstanceId=null;CursorBookData=originalBookData=null;
        cursorTrade=null;
        cursorTradeCount=0;
    }
    void NotifyCashTrade(TradeOffer trade,int exchanges)
    {
        if(trade==null || exchanges<=0)return;
        var given=trade.give.Resolve();var received=trade.get.Resolve();
        long paid=given.IsCurrency?given.CurrencyValue*(long)trade.give.Count*exchanges:0;
        long income=received.IsCurrency?received.CurrencyValue*(long)trade.get.Count*exchanges:0;
        long delta=income-paid;
        string itemName=delta<0?(trade.get.IsBookTemplate?trade.get.bookTemplate.title:received.DisplayName):given.DisplayName;
        CashService.NotifyTransaction(Inventory,delta,delta<0?MoneyChangeReason.Purchase:MoneyChangeReason.Sale,
            itemName+(delta<0?" 구매":" 판매"));
    }
    static bool TryPay(InventoryState inventory, TradeItem payment, out int firstSlot, out ItemStack firstPaid, out string error)
    {
        error=null;firstSlot=-1;firstPaid=null;if(payment.IsFree)return true;
        int need=payment.Count;
        for(int i=0;i<inventory.Capacity && need>0;i++)
        {
            var stack=inventory.GetSlot(i);if(!payment.Matches(stack))continue;
            if(firstSlot<0){firstSlot=i;firstPaid=stack.Copy();}
            int count=Math.Min(need,stack.Count);if(!inventory.TryRemove(i,count,out error))return false;need-=count;
        }
        if(need==0)return true;
        error=payment.Resolve().DisplayName+" "+payment.Count+"개가 필요합니다.";return false;
    }
}
