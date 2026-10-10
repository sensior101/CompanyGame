using System;

/// <summary>The common drag transaction with rental availability and tooltip decoration.</summary>
public sealed class RentalTradeSession : TradeSession
{
    public RentalTradeSession(InventoryState inventory, Func<TradeOffer[]> offers) : base(inventory, offers) { }
    public RentalTradeSession(InventoryState inventory, TradeOffer[] offers) : base(inventory, offers) { }
    public bool IsRented(int index) => HasOffer(index) && (Offers[index].isRented?.Invoke() ?? false);
    public override bool IsOfferAvailable(int index) => !IsRented(index) && base.IsOfferAvailable(index);
    public override string OutputTooltip(int index) => base.OutputTooltip(index) +
        (IsRented(index) ? "\n-----\n누군가가 대여중입니다." : "");
}
