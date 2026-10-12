using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JSON form of an inventory for the player-state server. Items are keyed by itemId and keep their
/// instance IDs. Import builds a detached draft first, so a bad snapshot never half-replaces the live inventory.
/// A stack that cannot be placed (unknown item, duplicate instance) is skipped; the server copy wins over local books.
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

    public static string Export(InventoryState inventory)
    {
        var data = new Data { capacity = inventory.Capacity, selected = inventory.SelectedHotbarIndex };
        for (int i = 0; i < inventory.Capacity; i++) Add(data, inventory.GetSlot(i), i, -1);
        for (int e = 0; e < 6; e++) Add(data, inventory.GetEquipment((EquipmentSlot)e), 0, e);
        return JsonUtility.ToJson(data);
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

    static void Add(Data data, ItemStack stack, int slot, int equip)
    {
        if (stack == null || stack.IsEmpty) return;
        data.items.Add(new Entry
        {
            itemId = stack.Item.itemId, count = stack.Count, instanceId = stack.InstanceId,
            slot = slot, equip = equip, hasBook = stack.BookData != null, book = stack.BookData?.Clone()
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
        return new ItemStack(item, entry.count, entry.instanceId) { BookData = entry.hasBook ? entry.book?.Clone() : null };
    }

    static ItemData Find(string itemId)
    {
        if (catalog == null)
        {
            catalog = new Dictionary<string, ItemData>();
            foreach (var item in Resources.LoadAll<ItemData>("Inventory"))
                if (item && !string.IsNullOrEmpty(item.itemId)) catalog[item.itemId] = item;
        }
        return itemId != null && catalog.TryGetValue(itemId, out var found) ? found : null;
    }
}
