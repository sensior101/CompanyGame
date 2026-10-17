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
///   Send / Trust   client side: an intent about my own inventory (never a ledger's) goes out with SharedWriteGate.
///                  SendPersonal; a change on a path not moved to the server yet asks for a trusted layout
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

    /// <summary>
    /// A change to my inventory on a path the server does not decide yet (drop, pickup, vehicle, storage, grant, library):
    /// the layout goes out as p.inv.trust with <paramref name="reason"/>. ponytail: trusted until 4d-4 and 4d-5 move these paths.
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
