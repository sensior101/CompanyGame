// Append values to preserve existing serialized furniture definitions.
public enum StorageType { None = 0, General = 1, Clothing = 2, Food = 3, Books = 4 }

/// <summary>Shared storage filters, atomic inventory transfers and world-save records.</summary>
public static class FurnitureStorageRules
{
    public static bool Accepts(StorageType storage, ItemData item)
    {
        if (!item) return false;
        bool food = item.itemType == ItemType.Consumable && item.consumableType != ConsumableType.Medicine;
        switch (storage)
        {
            case StorageType.Clothing: return item.itemType == ItemType.Clothing;
            case StorageType.Food: return food;
            case StorageType.Books: return item.IsBook || item.isBook;
            case StorageType.General: return true;
            default: return false;
        }
    }

    [System.Serializable] public sealed class Saved { public string id, inventory; }
    [System.Serializable] public sealed class Location
    {
        public string id, map, label;
        public UnityEngine.Vector3 position;
        public StorageType type;
        public bool refrigerated;
        public FurnitureFunction functions;
        public float radius = 2f;
    }
    [System.Serializable] public sealed class Locations { public Location[] items = System.Array.Empty<Location>(); }
    static readonly System.Collections.Generic.Dictionary<string, InventoryState> offline = new();
    static readonly System.Collections.Generic.Dictionary<string, InventoryState> remote = new();
    public static event System.Action<string> RemoteChanged;
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { offline.Clear(); remote.Clear(); RemoteChanged = null; }
    public static InventoryState Contents(string id, bool network)
    {
        var states = network ? remote : offline;
        if (!states.TryGetValue(id, out var state)) states[id] = state = new InventoryState();
        return state;
    }
    public static bool HasOffline(string id) => offline.ContainsKey(id);
    public static void ClearRemote() => remote.Clear();
    public static void Receive(string id, string json)
    {
        if (string.IsNullOrEmpty(id)) return;
        var draft = new InventoryState();
        if (!InventorySnapshot.Import(draft, json)) return;
        var state = Contents(id, true); state.ReplaceWith(draft); state.NotifyChanged(); RemoteChanged?.Invoke(id);
    }
    public static System.Collections.Generic.List<Saved> CaptureOffline()
    {
        var result = new System.Collections.Generic.List<Saved>();
        foreach (var pair in offline) result.Add(new Saved { id = pair.Key, inventory = InventorySnapshot.Export(pair.Value) });
        return result;
    }
    public static void RestoreOffline(System.Collections.Generic.List<Saved> saved)
    {
        offline.Clear();
        if (saved == null) return;
        foreach (var row in saved) if (row != null && !string.IsNullOrEmpty(row.id)) InventorySnapshot.Import(Contents(row.id, false), row.inventory);
    }
    public static string Fingerprint(ItemStack stack) => stack == null || stack.IsEmpty ? "" :
        stack.Item.itemId + "|" + stack.InstanceId + "|" + stack.Count + "|" + stack.ExpiresAt + "|" + stack.MadeDay;
    public static bool Transfer(StorageType type, bool refrigerated, bool withdraw, InventoryState bag, InventoryState storage,
        int slot, int count, string expected, out string error)
    {
        error = null;
        var from = withdraw ? storage : bag; var to = withdraw ? bag : storage;
        var stack = from?.GetSlot(slot);
        if (to == null || stack == null || stack.IsEmpty || count <= 0 || count > stack.Count ||
            (expected != null && Fingerprint(stack) != expected)) { error = "아이템이 변경됐어요. 다시 선택해 주세요."; return false; }
        if (!withdraw && !Accepts(type, stack.Item)) { error = type == StorageType.Clothing ? "옷장에는 의류만 넣을 수 있어요." : type == StorageType.Food ? "냉장고에는 음식과 음료만 넣을 수 있어요." : type == StorageType.Books ? "책장에는 책만 넣을 수 있어요." : "보관할 수 없는 아이템입니다."; return false; }
        long? expires = null;
        if (refrigerated && FoodShelfLife.IsPerishable(stack.Item))
            expires = FoodShelfLife.TransferExpiry(stack, !withdraw);
        return from.TryTransferTo(to, slot, count, out error, expires);
    }
}
