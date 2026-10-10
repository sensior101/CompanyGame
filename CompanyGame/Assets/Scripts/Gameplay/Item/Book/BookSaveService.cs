using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Persists authored books without changing the existing session-only rules for other items.</summary>
public static class BookSaveService
{
    [Serializable]
    public sealed class SavedBook
    {
        public string itemId;
        public string instanceId;
        public int count = 1;
        public BookInstanceData data;
        public int slot;
        public string dropId;
        public string scenePath;
        public Vector3 position;
    }

    [Serializable]
    sealed class SaveFile
    {
        public int version = 2;
        public bool starterGranted;
        public List<SavedBook> inventory = new List<SavedBook>();
        public List<SavedBook> drops = new List<SavedBook>();
    }

    static SaveFile current;
    static string lastSavedJson;
    static string PathName => Path.Combine(Application.persistentDataPath, "books.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() { current = null; lastSavedJson = null; }

    static SaveFile Current
    {
        get
        {
            if (current != null) return current;
            try
            {
                if (File.Exists(PathName))
                {
                    lastSavedJson = File.ReadAllText(PathName);
                    current = JsonUtility.FromJson<SaveFile>(lastSavedJson);
                }
            }
            catch (Exception e) { Debug.LogWarning("[Book] Save load failed: " + e.Message); }
            current ??= new SaveFile();
            current.inventory ??= new List<SavedBook>();
            current.drops ??= new List<SavedBook>();
            MigrateLegacy(current.inventory);
            MigrateLegacy(current.drops);
            current.version = 2;
            return current;
        }
    }

    public static void RestoreInventory(InventoryState inventory)
    {
        if (inventory == null) return;
        var item = Resources.Load<ItemData>("Inventory/Books/BlankBook");
        if (!item || !item.IsBook) { Debug.LogWarning("[Book] BlankBook item asset is missing."); return; }
        if (Current.inventory.Exists(saved => saved != null && saved.slot >= InventoryState.BaseCapacity) &&
            inventory.Capacity == InventoryState.BaseCapacity) inventory.TryExpandWithBag(out _);
        foreach (var saved in Current.inventory)
        {
            if (saved == null || saved.itemId != item.itemId) continue;
            RestoreBook(inventory, item, saved, out _);
        }
        if (!Current.starterGranted)
        {
            bool alreadyOwnsBook = Current.inventory.Count > 0 || Current.drops.Count > 0;
            Current.starterGranted = true;
            if (!alreadyOwnsBook) inventory.TryAdd(item, 1, out _);
            SaveInventory(inventory);
        }
    }

    public static SavedBook[] GetWorldDrops() => Current.drops.ToArray();

    static void MigrateLegacy(List<SavedBook> entries)
    {
        foreach (var saved in entries)
        {
            if (saved == null) continue;
            saved.count = Math.Max(1, saved.count);
            saved.data = saved.data?.Clone();
            if (current.version >= 2 || saved.data == null || saved.data.isPublished) continue;
            if (saved.data.HasContent || !string.IsNullOrWhiteSpace(saved.data.title))
            {
                // Version 1 was local-only and had no author field.
                saved.data.authorPlayerId = GameSession.LocalPlayerId;
                saved.data.authorName = GameSession.LocalPlayerName;
                saved.data.isPublished = true;
                if (string.IsNullOrWhiteSpace(saved.data.title)) saved.data.title = "제목 없는 책";
            }
            else { saved.instanceId = null; saved.data = null; }
        }
    }

    public static bool RestoreBook(InventoryState inventory, ItemData item, SavedBook saved, out string error)
    {
        if (saved.data != null && saved.data.isPublished)
            return inventory.TryAddBookInstance(item, saved.instanceId, saved.data, out error, saved.slot);
        return inventory.TryAdd(item, Math.Max(1, saved.count), out error);
    }

    public static void SaveInventory(InventoryState inventory)
    {
        if (inventory == null) return;
        var entries = new List<SavedBook>();
        for (int i = 0; i < inventory.Capacity; i++)
        {
            var stack = inventory.GetSlot(i);
            if (stack == null || stack.IsEmpty || !stack.Item.IsBook) continue;
            entries.Add(new SavedBook
            {
                itemId = stack.Item.itemId, instanceId = stack.InstanceId,
                data = stack.BookData?.Clone(), count = stack.Count, slot = i
            });
        }
        Current.inventory = entries;
        foreach (var stack in TradeSession.PendingBooks(inventory))
            entries.Add(new SavedBook { itemId = stack.Item.itemId, instanceId = stack.InstanceId,
                data = stack.BookData?.Clone(), count = stack.Count, slot = -1 });
        Write();
    }

    public static void SaveWorldDrop(string dropId, string scenePath, Vector3 position, ItemStack stack)
    {
        if (stack == null || stack.IsEmpty || !stack.Item.IsBook) return;
        Current.drops.RemoveAll(x => x.dropId == dropId);
        Current.drops.Add(new SavedBook
        {
            itemId = stack.Item.itemId, instanceId = stack.InstanceId,
            data = stack.BookData?.Clone(), count = stack.Count, dropId = dropId,
            scenePath = scenePath, position = position
        });
        Write();
    }

    public static void RemoveWorldDrop(string dropId)
    {
        if (Current.drops.RemoveAll(x => x.dropId == dropId) > 0) Write();
    }

    static void Write()
    {
        try
        {
            string json = JsonUtility.ToJson(Current, true);
            if (json == lastSavedJson) return;
            SaveManager.WriteJsonFile(PathName, json);
            lastSavedJson = json;
        }
        catch (Exception e) { Debug.LogError("[Book] Save failed: " + e.Message); }
    }
}
