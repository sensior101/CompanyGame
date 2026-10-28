using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JSON form of an inventory for the player-state server. Items are keyed by itemId and keep their
/// instance IDs. Import builds a detached draft first, so a bad snapshot never half-replaces the live inventory.
/// A stack that cannot be placed (unknown item, duplicate instance) is skipped; the server copy wins over local books.
/// Book bodies (12.10.3): a sync-layer server keeps the pages apart, so a book exported without them carries only its
/// title, author, permission and library ids (bodyless), and comes back with BookInstanceData.bodyLoaded false.
/// Cash notes ("cash:금액") are found by value, and shop goods through the TradeCatalog, so both survive a save (12.10.4);
/// so are the TradeCatalog's grants (the employee card, 12.10.5).
/// </summary>
public static class InventorySnapshot
{
    [Serializable]
    sealed class Entry
    {
        public string itemId;
        public int count;
        public string instanceId;
        public int slot;
        public int equip = -1; // EquipmentSlot index, -1 = in the bag
        public bool hasBook;
        public BookInstanceData book;
        public bool bodyless; // the pages were left out (12.10.3)
    }

    [Serializable]
    sealed class Data
    {
        public int version = 1;
        public int capacity;
        public int selected;
        public List<Entry> items = new List<Entry>();
    }

    static Dictionary<string, ItemData> catalog;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCatalog() { catalog = null; }

    /// <summary><paramref name="bodies"/> false leaves the pages of every book with an identity out (a server's character and drops).</summary>
    public static string Export(InventoryState inventory, bool bodies = true)
    {
        var data = new Data { capacity = inventory.Capacity, selected = inventory.SelectedHotbarIndex };
        for (int i = 0; i < inventory.Capacity; i++) Add(data, inventory.GetSlot(i), i, -1, bodies);
        for (int e = 0; e < 6; e++) Add(data, inventory.GetEquipment((EquipmentSlot)e), 0, e, bodies);
        return JsonUtility.ToJson(data);
    }

    /// <summary>
    /// The snapshot <paramref name="json"/> without book pages (the server's save filter and the move of older saves,
    /// 12.10.3): each book that still has its pages is handed to <paramref name="body"/> (instance id, the book) first.
    /// The same string when nothing was left out or it is unreadable.
    /// </summary>
    public static string StripBodies(string json, Action<string, BookInstanceData> body)
    {
        Data data;
        try { data = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Data>(json); }
        catch (Exception) { return json; }
        if (data?.items == null) return json;
        bool changed = false;
        foreach (var entry in data.items)
        {
            if (!entry.hasBook || entry.book == null || entry.bodyless || string.IsNullOrEmpty(entry.instanceId)) continue;
            var book = entry.book.Clone();
            if (book.HasContent) body?.Invoke(entry.instanceId, book);
            entry.book.pages = Array.Empty<string>();
            entry.bodyless = changed = true;
        }
        return changed ? JsonUtility.ToJson(data) : json;
    }

    public static bool Import(InventoryState inventory, string json)
    {
        if (inventory == null || string.IsNullOrEmpty(json)) return false;
        Data data;
        try { data = JsonUtility.FromJson<Data>(json); }
        catch (Exception) { return false; }
        if (data == null || data.items == null || data.version != 1) return false;

        var draft = new InventoryState();
        if (data.capacity >= InventoryState.ExpandedCapacity) draft.TryExpandWithBag(out _);
        foreach (var entry in data.items) if (entry.equip >= 0) PlaceEquipment(draft, entry);
        foreach (var entry in data.items) if (entry.equip < 0) PlaceStack(draft, entry);
        draft.SelectHotbar(data.selected);

        inventory.ReplaceWith(draft);
        inventory.NotifyChanged();
        return true;
    }

    static void Add(Data data, ItemStack stack, int slot, int equip, bool bodies)
    {
        if (stack == null || stack.IsEmpty) return;
        var book = stack.BookData?.Clone();
        bool bodyless = book != null && !string.IsNullOrEmpty(stack.InstanceId) && (!bodies || !book.bodyLoaded);
        if (bodyless) book.pages = Array.Empty<string>();
        data.items.Add(new Entry
        {
            itemId = stack.Item.itemId, count = stack.Count, instanceId = stack.InstanceId,
            slot = slot, equip = equip, hasBook = book != null, book = book, bodyless = bodyless
        });
    }

    static void PlaceStack(InventoryState draft, Entry entry)
    {
        var stack = Build(entry);
        string error = "unknown item";
        if (stack == null || !draft.TryPlaceStack(entry.slot, stack, false, out _, out error))
            Debug.LogWarning("[InventorySnapshot] Skipped " + entry.itemId + ": " + error);
    }

    // Equipment goes through slot 0 of the empty draft; TryEquip leaves that slot empty again.
    static void PlaceEquipment(InventoryState draft, Entry entry)
    {
        var stack = Build(entry);
        if (stack == null || !draft.TryPlaceStack(0, stack, false, out _, out _) ||
            !draft.TryEquip(0, (EquipmentSlot)entry.equip, out string error))
        {
            Debug.LogWarning("[InventorySnapshot] Skipped equipment " + entry.itemId);
            if (!draft.GetSlot(0).IsEmpty) draft.TryRemove(0, draft.GetSlot(0).Count, out _);
        }
    }

    static ItemStack Build(Entry entry)
    {
        var item = Find(entry.itemId);
        if (!item || entry.count <= 0) return null;
        var book = entry.hasBook ? entry.book?.Clone() : null;
        if (book != null) book.bodyLoaded = !entry.bodyless;
        return new ItemStack(item, entry.count, entry.instanceId) { BookData = book };
    }

    public static ItemData Find(string itemId)
    {
        // Cash is made at runtime (CashService.GetCurrency), never a Resources asset (12.10.4).
        if (itemId != null && itemId.StartsWith("cash:", StringComparison.Ordinal) &&
            long.TryParse(itemId.Substring(5), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long value))
            return CashService.GetCurrency(value);
        if (catalog == null)
        {
            catalog = new Dictionary<string, ItemData>();
            foreach (var item in Resources.LoadAll<ItemData>("Inventory"))
                if (item && !string.IsNullOrEmpty(item.itemId)) catalog[item.itemId] = item;
            // Shop goods (the convenience store's food) and the grant list (the employee card) are kept outside Resources/Inventory (12.10.4, 12.10.5).
            foreach (var item in TradeCatalog.Items())
                if (item && !string.IsNullOrEmpty(item.itemId) && !catalog.ContainsKey(item.itemId)) catalog[item.itemId] = item;
        }
        return itemId != null && catalog.TryGetValue(itemId, out var found) ? found : null;
    }
}
