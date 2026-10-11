using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Disposable edit-mode checks for the trade cart, cart checkout, wallet withdrawal and change-making single trades.</summary>
public static class TradeCartQA
{
    [Serializable]
    public sealed class Report
    {
        public bool passed;
        public int checks;
        public List<string> failures = new List<string>();
    }

    sealed class StubFulfillment : ITradeFulfillment
    {
        public bool CanTake(out string error) { error = null; return true; }
        public ItemStack CreateItem() => null;
        public void Complete(ItemStack received) { }
    }

    [MenuItem("Tools/Company Game/Inventory/Validate Trade Cart")]
    static void MenuRun() { Debug.Log(Run()); }

    public static string Run()
    {
        var report = new Report();
        if (Application.isPlaying)
        {
            report.failures.Add("Run in edit mode so the live bank is untouched.");
            return JsonUtility.ToJson(report, true);
        }
        var bankHost = new GameObject("Trade cart QA disposable bank") { hideFlags = HideFlags.HideAndDontSave };
        var bank = bankHost.AddComponent<BankManager>();
        var items = new List<ItemData>();
        try
        {
            Check(report, GameSession.IsAuthority, "QA needs authority (not a client session)");
            ValidateEligibility(report, items);
            ValidateCart(report, items);
            ValidateCheckout(report, items);
            ValidatePruneAndCursor(report, items);
            ValidateWithdraw(report, bank);
            ValidateSingleTrades(report, items);
            report.passed = report.failures.Count == 0;
        }
        catch (Exception exception) { report.failures.Add(exception.ToString()); report.passed = false; }
        finally
        {
            foreach (ItemData item in items) UnityEngine.Object.DestroyImmediate(item);
            UnityEngine.Object.DestroyImmediate(bankHost);
        }
        return JsonUtility.ToJson(report, true);
    }

    static void ValidateEligibility(Report report, List<ItemData> items)
    {
        ItemData apple = Item("Apple", ItemCategory.General, items), car = Item("Car", ItemCategory.Vehicle, items);
        var session = new TradeSession(new InventoryState(), new[]
        {
            Offer(700, apple),                                                                      // 0 ok
            new TradeOffer { give = new TradeItem { cash = 100, count = 1 }, get = new TradeItem { cash = 500, count = 1 } }, // 1 currency out
            Offer(700, car),                                                                        // 2 vehicle
            new TradeOffer { give = new TradeItem { item = apple, count = 1 }, get = new TradeItem { cash = 500, count = 1 } }, // 3 sell
            new TradeOffer { give = new TradeItem { item = apple, count = 1 }, get = new TradeItem { item = car, count = 1 } }, // 4 item-for-item
            new TradeOffer { give = new TradeItem { cash = 700, count = 1 }, get = new TradeItem { item = apple, count = 1 }, fulfillment = new StubFulfillment() }, // 5 fulfillment
            Offer(0, apple),                                                                        // 6 free
            new TradeOffer { give = new TradeItem { item = CashService.GetCurrency(100), count = 2 }, get = new TradeItem { item = apple, count = 1 } }, // 7 currency item give
        });
        Check(report, session.IsCartable(0) && session.IsCartable(7), "Cash and currency-item offers are cartable");
        Check(report, !session.IsCartable(1) && !session.IsCartable(2) && !session.IsCartable(3) && !session.IsCartable(4) &&
            !session.IsCartable(5) && !session.IsCartable(6) && !session.IsCartable(99), "Currency output, vehicle, sell, item-for-item, fulfillment, free and missing offers are not cartable");
        Check(report, !session.TryAddToCart(3, out _) && !session.TryAddToCart(4, out _) && !session.TryAddToCart(5, out _) &&
            session.Cart.Count == 0, "Ineligible offers are rejected by TryAddToCart");
        Check(report, TradeSession.UnitPrice(session.Offers[7]) == 200, "Unit price of a currency-item give is face value x count");
    }

    static void ValidateCart(Report report, List<ItemData> items)
    {
        ItemData apple = Item("Cart apple", ItemCategory.General, items), pear = Item("Cart pear", ItemCategory.General, items);
        var offers = new List<TradeOffer> { Offer(700, apple), Offer(500, pear) };
        for (int i = 0; i < TradeSession.CartMaxLines; i++) offers.Add(Offer(100 + i, apple));
        var session = new TradeSession(new InventoryState(), offers.ToArray());

        Check(report, session.TryAddToCart(0, out _) && session.TryAddToCart(0, out _) && session.Cart.Count == 1 &&
            session.Cart[0].count == 2 && session.CartTotal == 1400 && session.CartItemCount == 2, "Same offer twice merges into one line; total = unit x 2");
        Check(report, session.TryAddToCart(1, out _) && session.Cart.Count == 2 && session.CartTotal == 1900 &&
            session.CartItemCount == 3, "Different offers sum into the total");
        Check(report, session.TryChangeCart(session.Cart[0], -1, out _) && session.Cart[0].count == 1 && session.CartTotal == 1200, "Decreasing a line updates the total");
        Check(report, session.TryChangeCart(session.Cart[0], -1, out _) && session.Cart.Count == 1 && session.Cart[0].offer == offers[1],
            "Changing a line to 0 removes it");

        session.ClearCart();
        Check(report, session.Cart.Count == 0 && session.CartTotal == 0, "ClearCart empties the cart");
        for (int i = 0; i < TradeSession.CartMaxLines; i++) session.TryAddToCart(2 + i, out _);
        Check(report, session.Cart.Count == TradeSession.CartMaxLines && !session.TryAddToCart(0, out _) &&
            session.Cart.Count == TradeSession.CartMaxLines, "CartMaxLines rejects a new line");
        Check(report, session.TryAddToCart(2, out _) && session.Cart[0].count == 2, "Existing line still accepts more when the cart is full");

        session.ClearCart();
        session.TryAddToCart(0, out _);
        int limit = apple.StackLimit;
        Check(report, limit > 1 && session.TryChangeCart(session.Cart[0], limit - 1, out _) && session.Cart[0].count == limit, "Line can reach the stack limit");
        Check(report, !session.TryChangeCart(session.Cart[0], 1, out _) && !session.TryAddToCart(0, out _) && session.Cart[0].count == limit,
            "Line cannot exceed the stack limit");

        var pair = new TradeOffer { give = new TradeItem { cash = 100, count = 1 }, get = new TradeItem { item = apple, count = 2 } };
        var pairSession = new TradeSession(new InventoryState(), new[] { pair });
        Check(report, pairSession.TryAddToCart(0, out _) && pairSession.TryChangeCart(pairSession.Cart[0], limit / 2 - 1, out _) &&
            pairSession.Cart[0].count == limit / 2 && !pairSession.TryAddToCart(0, out _), "Per-line max is StackLimit / get.Count");
    }

    static void ValidateCheckout(Report report, List<ItemData> items)
    {
        ItemData apple = Item("Buy apple", ItemCategory.General, items), top = Item("Filler", ItemCategory.Top, items);
        TradeOffer offer = Offer(700, apple);

        var poor = new InventoryState(); poor.TryAdd(CashService.GetCurrency(1000), 1, out _);
        var poorSession = new TradeSession(poor, new[] { offer });
        poorSession.TryAddToCart(0, out _); poorSession.TryChangeCart(poorSession.Cart[0], 2, out _);
        Check(report, poorSession.CartTotal == 2100 && poorSession.CartShortfall == 1100, "Shortfall = total - carried cash");
        Check(report, !poorSession.TryCheckout(out _) && CashService.CarriedTotal(poor) == 1000 && Count(poor, apple) == 0 &&
            poorSession.Cart.Count == 1 && poorSession.Cart[0].count == 3, "Insufficient cash fails with inventory unchanged and cart kept");

        var rich = new InventoryState(); rich.TryAdd(CashService.GetCurrency(10000), 1, out _);
        var session = new TradeSession(rich, new[] { offer });
        session.TryAddToCart(0, out _); session.TryChangeCart(session.Cart[0], 2, out _);
        int notifications = 0; long lastDelta = 0;
        bool reentryBlocked = false;
        Action<InventoryState, long, MoneyChangeReason, string> listener = (inv, delta, reason, label) =>
        {
            notifications++; lastDelta = delta;
            reentryBlocked = !CashService.TryPurchase(rich, 100, new[] { (apple, 1) }, out _);
        };
        CashService.TransactionCompleted += listener;
        bool ok;
        try { ok = session.TryCheckout(out _); }
        finally { CashService.TransactionCompleted -= listener; }
        Check(report, ok && CashService.CarriedTotal(rich) == 7900 && Count(rich, apple) == 3 && session.Cart.Count == 0,
            "Checkout pays 3 x 700 from a 10,000 coin with change, adds items, clears the cart");
        Check(report, notifications == 1 && lastDelta == -2100, "Checkout publishes exactly one transaction of -total");
        Check(report, reentryBlocked && Count(rich, apple) == 3 && CashService.CarriedTotal(rich) == 7900, "TryPurchase from a transaction listener is rejected");
        Check(report, !session.TryCheckout(out _), "Empty cart cannot be checked out");

        var full = new InventoryState();
        full.TryAdd(top, 15, out _); full.TryAdd(CashService.GetCurrency(10000), 1, out _);
        var fullSession = new TradeSession(full, new[] { offer });
        fullSession.TryAddToCart(0, out _);
        Check(report, !fullSession.TryCheckout(out _) && CashService.CarriedTotal(full) == 10000 && Count(full, apple) == 0 &&
            Count(full, top) == 15 && fullSession.Cart.Count == 1, "Full inventory fails atomically: no payment, no items, cart kept");
    }

    static void ValidatePruneAndCursor(Report report, List<ItemData> items)
    {
        ItemData apple = Item("Prune apple", ItemCategory.General, items), pear = Item("Prune pear", ItemCategory.General, items);
        TradeOffer a = Offer(700, apple), b = Offer(500, pear);
        TradeOffer[] current = { a, b };
        var inventory = new InventoryState();
        inventory.TryAdd(CashService.GetCurrency(10000), 1, out _); inventory.TryAdd(pear, 1, out _);
        var session = new TradeSession(inventory, () => current);
        session.TryAddToCart(0, out _); session.TryAddToCart(1, out _);
        current = new[] { b };
        session.RefreshOffers();
        Check(report, session.Cart.Count == 1 && session.Cart[0].offer == b && session.CartTotal == 500, "PruneCart drops offers that vanish from the provider");

        int pearSlot = -1;
        for (int i = 0; i < inventory.Capacity; i++) if (inventory.GetSlot(i).Item == pear) pearSlot = i;
        Check(report, session.TryPickUp(pearSlot, out _), "Cursor pickup for the block test");
        Check(report, !session.TryAddToCart(0, out _) && !session.TryChangeCart(session.Cart[0], 1, out _) && !session.TryCheckout(out _) &&
            session.Cart[0].count == 1 && CashService.CarriedTotal(inventory) == 10000, "Cursor-held item blocks add, change and checkout");
        session.TryCancel(out _);
        Check(report, session.TryCheckout(out _) && CashService.CarriedTotal(inventory) == 9500 && Count(inventory, pear) == 2,
            "Checkout works again once the cursor is released");
    }

    static void ValidateWithdraw(Report report, BankManager bank)
    {
        bank.SetMoney(10000);
        var inventory = new InventoryState();
        Check(report, CashService.TryWithdraw(bank, inventory, 1000, 3, out _) && CashService.CarriedTotal(inventory) == 3000 && bank.Money == 7000,
            "Wallet popup backend: 1,000 x 3 adds 3,000 carried and debits the bank");
    }

    static void ValidateSingleTrades(Report report, List<ItemData> items)
    {
        ItemData apple = Item("Single apple", ItemCategory.General, items), pear = Item("Single pear", ItemCategory.General, items);
        TradeOffer offer = Offer(700, apple);

        var change = new InventoryState(); change.TryAdd(CashService.GetCurrency(1000), 1, out _);
        var trade = new TradeSession(change, new[] { offer });
        Check(report, trade.TryTakeOffer(0, out _) && trade.TryPlace(8, out _) && CashService.CarriedTotal(change) == 300 &&
            Count(change, apple) == 1 && CurrencyStacks(change) == 1 && change.GetSlot(8).Item == apple, "Single trade 1,000 -> 700 item returns one 300 coin");

        var cancel = new InventoryState(); cancel.TryAdd(CashService.GetCurrency(1000), 1, out _);
        var cancelTrade = new TradeSession(cancel, new[] { offer });
        Check(report, cancelTrade.TryTakeOffer(0, out _) && cancelTrade.TryCancel(out _) && CashService.CarriedTotal(cancel) == 1000 &&
            Count(cancel, apple) == 0 && !cancelTrade.HasCursorItem, "Cancel after a change-making take conserves value");

        var stacked = new InventoryState(); stacked.TryAdd(CashService.GetCurrency(1000), 2, out _);
        var stackedTrade = new TradeSession(stacked, new[] { offer });
        Check(report, stackedTrade.TryTakeOffer(0, out _) && stackedTrade.TryTakeOffer(0, out _) && stackedTrade.CursorCount == 2 &&
            stackedTrade.TryCancel(out _) && CashService.CarriedTotal(stacked) == 2000 && Count(stacked, apple) == 0,
            "Two stacked takes then cancel conserve value");

        var exchange = new TradeOffer { give = new TradeItem { item = pear, count = 1 }, get = new TradeItem { item = apple, count = 1 } };
        var empty = new InventoryState();
        Check(report, !new TradeSession(empty, new[] { exchange }).TryTakeOffer(0, out _) && Count(empty, apple) == 0, "Item-for-item trade fails without the item");
        var holder = new InventoryState(); holder.TryAdd(pear, 1, out _);
        var exchangeTrade = new TradeSession(holder, new[] { exchange });
        Check(report, exchangeTrade.TryTakeOffer(0, out _) && exchangeTrade.TryPlace(5, out _) && Count(holder, pear) == 0 && Count(holder, apple) == 1,
            "Item-for-item trade works with the exact item");
    }

    static TradeOffer Offer(long price, ItemData get) => new TradeOffer
    {
        give = new TradeItem { cash = price, count = 1 },
        get = new TradeItem { item = get, count = 1 }
    };

    static ItemData Item(string name, ItemCategory category, List<ItemData> items)
    {
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.itemId = item.name = item.displayName = name;
        item.category = category;
        item.maxStack = 99;
        items.Add(item);
        return item;
    }

    static int CurrencyStacks(InventoryState state)
    {
        int stacks = 0;
        for (int i = 0; i < state.Capacity; i++) if (state.GetSlot(i).Item && state.GetSlot(i).Item.IsCurrency) stacks++;
        return stacks;
    }

    static int Count(InventoryState state, ItemData item)
    {
        int count = 0;
        for (int i = 0; i < state.Capacity; i++) if (state.GetSlot(i).Item == item) count += state.GetSlot(i).Count;
        return count;
    }

    static void Check(Report report, bool pass, string description)
    {
        report.checks++;
        if (!pass) report.failures.Add(description);
    }
}
