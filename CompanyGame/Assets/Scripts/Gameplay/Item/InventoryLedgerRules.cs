using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

/// <summary>
/// Rules of the server's inventory ledger (Docs/Multiplayer_Redesign.md 12.10.4), shared by the server and its clients.
/// The server keeps "what and how many", the client "which slot":
///   SameContents   the same count of each item id (cash: the same total, in any notes), the same unique items (books
///                  and vehicles, by instance id) and the same capacity
///   TryTrade       a p.trade: the cash of every line in one payment (change as CashService.PlanPayment makes it), the
///                  items given, then everything got; all or nothing, as CashService.TryPurchase
///   TryTakeLayout  a client's layout (p.inv.layout) only with the same contents, unique items keeping the server's data;
///                  a trusted one (p.inv.trust) as it comes
///   TryTake / TryPutAll  the world's items (12.10.5): a drop or a placed vehicle leaves the ledger with the ledger's
///                  data; a pickup goes in whole or not at all
///   grants         the start vehicles and the goshiwon deed, the same rules on the server and off it (12.10.5)
///   Send / Trust   client side: an intent about my own inventory (never a ledger's) goes out with SharedWriteGate.
///                  SendPersonal; a change on a path not moved to the server yet (the library) asks for a trusted layout
/// Nothing here touches a screen manager, CashService's lock or its events (the server runs these in the owner's process).
/// </summary>
public static class InventoryLedgerRules
{
    /// <summary>Installed by the client sync (WorldStateSync): a trusted layout is wanted, with this reason.</summary>
    public static Action<string> TrustSender;

    const int MaxId = 64, MaxLayoutBytes = 256 * 1024;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => TrustSender = null;

    // ---- contents ----

    /// <summary>True when both hold the same things, wherever they are (12.10.4).</summary>
    public static bool SameContents(InventoryState a, InventoryState b)
    {
        if (a == null || b == null || a.Capacity != b.Capacity) return false;
        var left = Tally(a, out long cashA);
        var right = Tally(b, out long cashB);
        if (cashA != cashB || left.Count != right.Count) return false;
        foreach (var pair in left)
            if (!right.TryGetValue(pair.Key, out long n) || n != pair.Value) return false;
        return true;
    }

    static Dictionary<string, long> Tally(InventoryState inventory, out long cash)
    {
        var tally = new Dictionary<string, long>();
        cash = 0;
        foreach (var stack in Stacks(inventory))
        {
            if (stack.Item.IsCurrency) { cash = unchecked(cash + stack.Item.CurrencyValue * stack.Count); continue; }
            string key = IsUnique(stack) ? "u:" + stack.Item.itemId + "/" + stack.InstanceId : "i:" + stack.Item.itemId;
            tally.TryGetValue(key, out long n);
            tally[key] = n + stack.Count;
        }
        return tally;
    }

    /// <summary>A book or vehicle with its own identity.</summary>
    public static bool IsUnique(ItemStack stack) =>
        stack != null && !stack.IsEmpty && !string.IsNullOrEmpty(stack.InstanceId) && (stack.IsUniqueBook || stack.Item.IsVehicle);

    /// <summary>Every stack in the bag and the equipment slots that is not empty.</summary>
    public static IEnumerable<ItemStack> Stacks(InventoryState inventory)
    {
        for (int i = 0; i < inventory.Capacity; i++)
            if (!inventory.GetSlot(i).IsEmpty) yield return inventory.GetSlot(i);
        for (int e = 0; e < 6; e++)
        {
            var stack = inventory.GetEquipment((EquipmentSlot)e);
            if (stack != null && !stack.IsEmpty) yield return stack;
        }
    }

    /// <summary>The unique item <paramref name="instanceId"/> in the bag or equipment, or null.</summary>
    public static ItemStack FindUnique(InventoryState inventory, string instanceId)
    {
        if (inventory == null || string.IsNullOrEmpty(instanceId)) return null;
        foreach (var stack in Stacks(inventory)) if (IsUnique(stack) && stack.InstanceId == instanceId) return stack;
        return null;
    }

    // ---- server decisions ----

    /// <summary>
    /// A p.trade on <paramref name="inventory"/>: <paramref name="lines"/> are (offer index, times). A book template or a
    /// vehicle got takes the next id of <paramref name="ids"/> (the client made it). Nothing changes on failure.
    /// </summary>
    public static bool TryTrade(InventoryState inventory, TradeOffer[] offers, IList<(int offer, int count)> lines, IList<string> ids, out string error)
    {
        error = null;
        if (inventory == null || offers == null || lines == null || lines.Count == 0 || lines.Count > TradeSession.CartMaxLines)
            return Fail("거래할 항목을 확인해 주세요.", out error);
        long cash = 0;
        try
        {
            foreach (var (o, n) in lines)
            {
                if (o < 0 || o >= offers.Length || offers[o] == null || !offers[o].IsValid || offers[o].fulfillment != null)
                    return Fail("거래할 항목을 확인해 주세요.", out error);
                var got = offers[o].get.Resolve();
                if (!got || n <= 0 || n > Math.Max(1, got.StackLimit)) return Fail("거래 수량을 확인해 주세요.", out error);
                var give = offers[o].give;
                if (!give.IsFree && (!give.item || give.item.IsCurrency))
                    cash = checked(cash + (give.item ? give.item.CurrencyValue : give.cash) * give.Count * (long)n);
            }
        }
        catch (OverflowException) { return Fail("금액이 처리 가능한 범위를 초과합니다.", out error); }
        if (!CashService.PlanPayment(inventory, cash, out var draft, out error)) return false;
        foreach (var (o, n) in lines)
        {
            var give = offers[o].give;
            if (give.IsFree || !give.item || give.item.IsCurrency) continue;
            var pay = give;
            pay.count = give.Count * n;
            if (!TradeSession.TryPay(draft, pay, out _, out _, out error)) return false;
        }
        int next = 0;
        foreach (var (o, n) in lines)
        {
            var get = offers[o].get;
            var item = get.Resolve();
            int count = get.Count * n;
            if (!get.IsBookTemplate && !item.IsVehicle)
            {
                if (!draft.TryAdd(item, count, out error)) return false;
                continue;
            }
            for (int i = 0; i < count; i++)
            {
                string id = ids != null && next < ids.Count ? ids[next] : null;
                next++;
                if (!ValidId(id) || FindUnique(draft, id) != null) return Fail("새 아이템의 식별자를 확인해 주세요.", out error);
                if (get.IsBookTemplate ? !draft.TryAddBookInstance(item, id, BodylessCopy(get.bookTemplate), out error) : !draft.TryAdd(item, 1, out error, id))
                    return false;
            }
        }
        inventory.ReplaceWith(draft);
        return true;
    }

    // The ledger never holds pages (the world's books do).
    static BookInstanceData BodylessCopy(BookInstanceData book)
    {
        var copy = book.Clone();
        copy.pages = new string[BookInstanceData.PageCount];
        for (int p = 0; p < copy.pages.Length; p++) copy.pages[p] = "";
        copy.bodyLoaded = false;
        return copy;
    }

    /// <summary>
    /// A client's layout (InventorySnapshot JSON) becomes the ledger's: only with the same contents unless
    /// <paramref name="trusted"/>. Unique items already in the ledger keep the ledger's data; only their places come from the client.
    /// </summary>
    public static bool TryTakeLayout(InventoryState ledger, string json, bool trusted, out string error)
    {
        error = null;
        var draft = new InventoryState();
        if (ledger == null || string.IsNullOrEmpty(json) || !InventorySnapshot.Import(draft, json)) return Fail("배치를 읽을 수 없습니다.", out error);
        if (!trusted && !SameContents(ledger, draft)) return Fail("인벤토리 내용이 서버와 다릅니다.", out error);
        var known = new Dictionary<string, ItemStack>();
        foreach (var stack in Stacks(ledger)) if (IsUnique(stack)) known[stack.InstanceId] = stack;
        foreach (var stack in Stacks(draft))
            if (IsUnique(stack) && known.TryGetValue(stack.InstanceId, out var own) && own.Item == stack.Item) stack.BookData = own.BookData?.Clone();
        ledger.ReplaceWith(draft);
        return true;
    }

    /// <summary>p.eat: one <paramref name="itemId"/> that can be eaten goes.</summary>
    public static bool TryEat(InventoryState inventory, string itemId, out string error)
    {
        for (int i = 0; inventory != null && i < inventory.Capacity; i++)
        {
            var stack = inventory.GetSlot(i);
            if (!stack.IsEmpty && stack.Item.IsConsumable && stack.Item.itemId == itemId) return inventory.TryRemove(i, 1, out error);
        }
        return Fail("먹을 음식이 없습니다.", out error);
    }

    /// <summary>p.bag: the first bag is used (InventoryState.TryUseBag).</summary>
    public static bool TryBag(InventoryState inventory, out string error)
    {
        if (inventory != null && inventory.Capacity != InventoryState.BaseCapacity) return Fail("가방 확장이 이미 적용되어 있습니다.", out error);
        for (int i = 0; inventory != null && i < inventory.Capacity; i++)
            if (!inventory.GetSlot(i).IsEmpty && inventory.GetSlot(i).Item.category == ItemCategory.Bag) return inventory.TryUseBag(i, out error);
        return Fail("가방이 없습니다.", out error);
    }

    /// <summary>book.new from a blank (12.10.4): one blank book becomes <paramref name="book"/> (kept without pages) as <paramref name="instanceId"/>.</summary>
    public static bool TryWriteBlank(InventoryState inventory, string instanceId, BookInstanceData book, out string error)
    {
        for (int i = 0; inventory != null && i < inventory.Capacity; i++)
        {
            var stack = inventory.GetSlot(i);
            if (stack.IsEmpty || !stack.Item.IsBook || stack.IsUniqueBook) continue;
            var draft = inventory.Copy();
            if (!draft.TryRemove(i, 1, out error) || !draft.TryAddBookInstance(stack.Item, instanceId, BodylessCopy(book), out error, i)) return false;
            inventory.ReplaceWith(draft);
            return true;
        }
        return Fail("빈 책이 없습니다.", out error);
    }

    static bool ValidId(string id) => !string.IsNullOrEmpty(id) && id.Length <= MaxId;

    // ---- the world's items (12.10.5) ----

    static bool Matches(ItemStack stack, ItemData item, string instanceId) =>
        stack != null && !stack.IsEmpty && stack.Item == item &&
        (string.IsNullOrEmpty(instanceId) ? !IsUnique(stack) : IsUnique(stack) && stack.InstanceId == instanceId);

    /// <summary>
    /// p.drop / p.vehicle.place: <paramref name="count"/> of <paramref name="itemId"/> (a unique item by its
    /// <paramref name="instanceId"/>) leave the ledger's <paramref name="inventory"/> into <paramref name="taken"/>, with the
    /// ledger's own data (a book's details). From the slot named (a bag slot, or with <paramref name="equipment"/> an
    /// equipment slot) when it holds them, else from anywhere in the bag and equipment; cash, else by its total (change as
    /// CashService.PlanPayment makes it). Never more than one stack. Nothing changes on failure.
    /// </summary>
    public static bool TryTake(InventoryState inventory, int slot, bool equipment, string itemId, string instanceId, int count, out InventoryState taken, out string error)
    {
        taken = null;
        var item = string.IsNullOrEmpty(itemId) ? null : InventorySnapshot.Find(itemId);
        bool unique = !string.IsNullOrEmpty(instanceId);
        if (inventory == null || !item || count <= 0 || count > Math.Max(1, item.StackLimit) || unique && count != 1)
            return Fail("내려놓을 아이템을 확인해 주세요.", out error);
        var draft = inventory.Copy();
        var into = new InventoryState();
        if (!TakeAt(draft, into, slot, equipment, item, instanceId, count, out error))
        {
            draft = inventory.Copy();
            into = new InventoryState();
            if (!TakeAnywhere(draft, into, item, instanceId, count, out error))
            {
                if (unique || !item.IsCurrency) return Fail("가지고 있지 않은 아이템입니다.", out error);
                long amount;
                try { amount = checked(item.CurrencyValue * count); }
                catch (OverflowException) { return Fail("금액이 처리 가능한 범위를 초과합니다.", out error); }
                into = new InventoryState();
                if (!CashService.PlanPayment(inventory, amount, out draft, out error) || !into.TryAdd(item, count, out error)) return false;
            }
        }
        inventory.ReplaceWith(draft);
        inventory.NotifyChanged();
        taken = into;
        return true;
    }

    static bool TakeAt(InventoryState draft, InventoryState into, int slot, bool equipment, ItemData item, string instanceId, int count, out string error)
    {
        error = null;
        if (!equipment)
        {
            var stack = draft.GetSlot(slot);
            return Matches(stack, item, instanceId) && stack.Count >= count && draft.TryTransferTo(into, slot, count, out error);
        }
        var worn = slot >= 0 && slot < 6 ? draft.GetEquipment((EquipmentSlot)slot) : null;
        return Matches(worn, item, instanceId) && worn.Count == count && into.TryAdd(worn.Item, count, out error, worn.InstanceId)
               && draft.TryRemoveEquipment((EquipmentSlot)slot, out error);
    }

    static bool TakeAnywhere(InventoryState draft, InventoryState into, ItemData item, string instanceId, int count, out string error)
    {
        error = null;
        long held = 0;
        foreach (var stack in Stacks(draft)) if (Matches(stack, item, instanceId)) held += stack.Count;
        if (held < count) return false;
        int left = count;
        for (int i = 0; i < draft.Capacity && left > 0; i++)
        {
            var stack = draft.GetSlot(i);
            if (!Matches(stack, item, instanceId)) continue;
            int take = Math.Min(left, stack.Count);
            if (!draft.TryTransferTo(into, i, take, out error)) return false;
            left -= take;
        }
        for (int e = 0; e < 6 && left > 0; e++)
        {
            var worn = draft.GetEquipment((EquipmentSlot)e);
            if (!Matches(worn, item, instanceId) || worn.Count > left) continue;
            int take = worn.Count;
            if (!into.TryAdd(worn.Item, take, out error, worn.InstanceId) || !draft.TryRemoveEquipment((EquipmentSlot)e, out error)) return false;
            left -= take;
        }
        return left == 0;
    }

    /// <summary>p.pickup: everything in <paramref name="contents"/> goes into <paramref name="inventory"/>, or nothing does.</summary>
    public static bool TryPutAll(InventoryState inventory, InventoryState contents, out string error)
    {
        error = null;
        if (inventory == null || contents == null) return Fail("주울 수 없습니다.", out error);
        var draft = inventory.Copy();
        foreach (var stack in Stacks(contents))
        {
            if (IsUnique(stack) && FindUnique(draft, stack.InstanceId) != null) return Fail("이미 가지고 있는 아이템입니다.", out error);
            bool ok = stack.IsUniqueBook ? draft.TryAddBookInstance(stack.Item, stack.InstanceId, stack.BookData, out error)
                : draft.TryAdd(stack.Item, stack.Count, out error, stack.InstanceId);
            if (!ok) return Fail("가방에 자리가 없습니다.", out error);
        }
        inventory.ReplaceWith(draft);
        inventory.NotifyChanged();
        return true;
    }

    /// <summary>True when <paramref name="inventory"/> holds any <paramref name="itemId"/> (bag or equipment).</summary>
    public static bool Holds(InventoryState inventory, string itemId)
    {
        if (inventory == null || string.IsNullOrEmpty(itemId)) return false;
        foreach (var stack in Stacks(inventory)) if (stack.Item.itemId == itemId) return true;
        return false;
    }

    // ---- what a character is given (12.10.5): the same rules on the server and off it ----

    public const string StartingDeedPrefix = "goshiwon_";

    /// <summary>The start vehicles (one of each bicycle colour and car) <paramref name="inventory"/> lacks go in. True when any did.</summary>
    public static bool GrantStartVehicles(InventoryState inventory)
    {
        var items = new List<ItemData>(Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle"));
        items.AddRange(Resources.LoadAll<ItemData>("Inventory/Vehicles/Cars"));
        items.Sort((a, b) => string.CompareOrdinal(a.itemId, b.itemId));
        bool granted = false;
        foreach (var item in items)
        {
            bool owned = false;
            for (int i = 0; i < inventory.Capacity; i++)
                if (inventory.GetSlot(i).Item == item) { owned = true; break; }
            if (owned) continue;
            if (inventory.TryAdd(item, 1, out string error)) granted = true;
            else Debug.LogWarning(error);
        }
        return granted;
    }

    public static bool IsStartingDeed(ItemData item) =>
        item && item.IsDeed && item.propertyId != null && item.propertyId.StartsWith(StartingDeedPrefix, StringComparison.Ordinal);

    /// <summary>A goshiwon deed is in the bag.</summary>
    public static bool HasStartingDeed(InventoryState inventory)
    {
        for (int i = 0; inventory != null && i < inventory.Capacity; i++)
            if (!inventory.GetSlot(i).IsEmpty && IsStartingDeed(inventory.GetSlot(i).Item)) return true;
        return false;
    }

    /// <summary>The first goshiwon deed (by item id) that <paramref name="free"/> allows, or null.</summary>
    public static ItemData NextStartingDeed(Func<ItemData, bool> free)
    {
        var deeds = new List<ItemData>(Resources.LoadAll<ItemData>("Inventory/Deeds"));
        deeds.Sort((a, b) => string.CompareOrdinal(a.itemId, b.itemId));
        foreach (var deed in deeds) if (IsStartingDeed(deed) && free(deed)) return deed;
        return null;
    }

    // ---- client side ----

    /// <summary>True for this PC's own session inventory (never a server ledger's, a drop's or a merchant's).</summary>
    public static bool IsMine(InventoryState inventory)
    {
        var manager = inventory != null ? InventoryManager.Instance : null;
        return manager && ReferenceEquals(manager.State, inventory);
    }

    /// <summary>Tells the server about a change to my inventory this client already made (12.10.4). Only on a sync-layer server.</summary>
    public static void Send(InventoryState inventory, string kind, SharedOpArgs args)
    {
        if (IsMine(inventory)) SharedWriteGate.SendPersonal(kind, args);
    }

    /// <summary>On a sync-layer server with my character (the server decides what I am given, 12.10.5).</summary>
    public static bool OnServer => SharedWriteGate.OnServer;

    /// <summary>
    /// p.grant (12.10.5): this client gave itself one <paramref name="item"/> of the server's grant list already; the server
    /// gives it too while the ledger has none.
    /// </summary>
    public static void SendGrant(InventoryState inventory, ItemData item)
    {
        if (item) Send(inventory, "p.grant", new SharedOpArgs { a = item.itemId });
    }

    /// <summary>
    /// A change to my inventory on a path the server does not decide yet (the library): the layout goes out as
    /// p.inv.trust with <paramref name="reason"/>. ponytail: trusted until 4d-5 moves the library.
    /// </summary>
    public static void Trust(InventoryState inventory, string reason)
    {
        if (IsMine(inventory) && SharedWriteGate.OnServer) TrustSender?.Invoke(reason);
    }

    /// <summary>The layout of an inventory for a request: gzip(InventorySnapshot without pages), base64.</summary>
    public static string Pack(InventoryState inventory)
    {
        byte[] raw = Encoding.UTF8.GetBytes(InventorySnapshot.Export(inventory, false));
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, System.IO.Compression.CompressionLevel.Optimal, true)) gzip.Write(raw, 0, raw.Length);
        return Convert.ToBase64String(stream.ToArray());
    }

    /// <summary>The snapshot JSON of a packed layout, or null (unreadable, or over 256KB unpacked).</summary>
    public static string Unpack(string packed)
    {
        try
        {
            using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(packed ?? "")), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            for (int read; (read = gzip.Read(buffer, 0, buffer.Length)) > 0;)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxLayoutBytes) return null;
            }
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (Exception) { return null; }
    }

    /// <summary>p.trade lines as sent: "offer:times,offer:times".</summary>
    public static string Lines(IEnumerable<(int offer, int count)> lines)
    {
        var text = new StringBuilder();
        foreach (var (o, n) in lines) text.Append(text.Length > 0 ? "," : "").Append(o.ToString(CultureInfo.InvariantCulture)).Append(':').Append(n.ToString(CultureInfo.InvariantCulture));
        return text.ToString();
    }

    /// <summary>The lines of <see cref="Lines"/>, or null when unreadable.</summary>
    public static List<(int offer, int count)> ParseLines(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 256) return null;
        var lines = new List<(int, int)>();
        foreach (string part in text.Split(','))
        {
            int colon = part.IndexOf(':');
            if (colon <= 0 || !int.TryParse(part.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int o)
                || !int.TryParse(part.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return null;
            lines.Add((o, n));
        }
        return lines;
    }

    static bool Fail(string message, out string error) { error = message; return false; }
}
