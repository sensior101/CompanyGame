using System;
using System.Collections.Generic;

/// <summary>One cart row: an offer reference and how many times to buy it.</summary>
public sealed class CartLine { public TradeOffer offer; public int count; }

/// <summary>Cart is plain session state: no money moves until <see cref="TryCheckout"/>.</summary>
public partial class TradeSession
{
    public const int CartMaxLines = 8;
    readonly List<CartLine> cart = new List<CartLine>();
    public IReadOnlyList<CartLine> Cart => cart;

    // Cash-for-item offers only; free, exchange, sale, vehicle, book template and fulfillment offers keep the single-trade flow.
    public bool IsCartable(int offer)
    {
        if (!HasOffer(offer) || !IsOfferAvailable(offer)) return false;
        var o = Offers[offer]; var g = o.give; var r = o.get;
        return o.fulfillment == null && !r.IsBookTemplate && r.item && !r.item.IsCurrency && !r.item.IsVehicle
            && (!g.item || g.item.IsCurrency) && UnitPrice(o) > 0;
    }
    public static long UnitPrice(TradeOffer o) { try { return checked((o.give.item ? o.give.item.CurrencyValue : o.give.cash) * o.give.Count); } catch (OverflowException) { return 0; } }
    public long LineTotal(CartLine l) { try { return checked(UnitPrice(l.offer) * l.count); } catch (OverflowException) { return long.MaxValue; } }
    public long CartTotal
    {
        get { try { long sum = 0; foreach (var l in cart) sum = checked(sum + LineTotal(l)); return sum; } catch (OverflowException) { return long.MaxValue; } }
    }
    public int CartItemCount { get { int n = 0; foreach (var l in cart) n += l.count; return n; } }
    public long CartShortfall => Math.Max(0, CartTotal - CashService.CarriedTotal(Inventory));

    int IndexOfOffer(TradeOffer o) { for (int i = 0; i < Offers.Length; i++) if (ReferenceEquals(Offers[i], o)) return i; return -1; }
    bool CanEditCart(out string error)
    {
        error = null;
        if (HasCursorItem) error = "커서의 아이템을 먼저 놓아 주세요.";
        return error == null;
    }

    public bool TryAddToCart(int offer, out string error)
    {
        if (!CanEditCart(out error)) return false;
        if (!IsCartable(offer)) { error = "장바구니에 담을 수 없는 물건입니다."; return false; }
        var line = cart.Find(l => ReferenceEquals(l.offer, Offers[offer]));
        if (line == null)
        {
            if (cart.Count >= CartMaxLines) { error = "장바구니가 가득 찼습니다."; return false; }
            line = new CartLine { offer = Offers[offer] };
            cart.Add(line);
            if (TryChangeCart(line, 1, out error)) return true;
            cart.Remove(line); return false;
        }
        return TryChangeCart(line, 1, out error);
    }

    public bool TryChangeCart(CartLine line, int delta, out string error)
    {
        if (!CanEditCart(out error)) return false;
        int index = line == null ? -1 : IndexOfOffer(line.offer);
        if (index < 0 || !cart.Contains(line) || !IsCartable(index)) { error = "장바구니에 담을 수 없는 물건입니다."; return false; }
        int next = line.count + delta;
        if (next <= 0) { cart.Remove(line); return true; }
        var get = line.offer.get;
        if (next > Math.Max(1, get.item.StackLimit / get.Count)) { error = "한 번에 담을 수 있는 수량을 넘었습니다."; return false; }
        int prev = line.count; line.count = next;
        if (CartTotal == long.MaxValue) { line.count = prev; error = "금액이 처리 가능한 범위를 초과합니다."; return false; }
        return true;
    }

    public void RemoveFromCart(CartLine line) => cart.Remove(line);
    public void ClearCart() => cart.Clear();

    public bool TryCheckout(out string error)
    {
        if (!CanEditCart(out error)) return false;
        PruneCart();
        if (cart.Count == 0) { error = "장바구니가 비어 있습니다."; return false; }
        var items = new (ItemData item, int count)[cart.Count];
        try
        {
            for (int i = 0; i < cart.Count; i++)
            {
                if (!IsCartable(IndexOfOffer(cart[i].offer))) { error = "거래할 수 없는 물건이 있습니다."; return false; }
                var get = cart[i].offer.get;
                items[i] = (get.item, checked(get.Count * cart[i].count));
            }
        }
        catch (OverflowException) { error = "금액이 처리 가능한 범위를 초과합니다."; return false; }
        long total = CartTotal;
        if (total == long.MaxValue) { error = "금액이 처리 가능한 범위를 초과합니다."; return false; }
        var lines = new (int offer, int count)[cart.Count];
        for (int i = 0; i < cart.Count; i++) lines[i] = (IndexOfOffer(cart[i].offer), cart[i].count);
        if (!CashService.TryPurchase(Inventory, total, items, out error)) return false;
        ClearCart();
        SendTrade(lines, null);
        return true;
    }

    void PruneCart() => cart.RemoveAll(l => { int i = IndexOfOffer(l.offer); return i < 0 || !IsCartable(i); });
}
