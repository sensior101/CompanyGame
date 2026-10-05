// Append values to preserve existing serialized furniture definitions.
public enum StorageType { None = 0, General = 1, Clothing = 2, Food = 3, Books = 4 }

/// <summary>Category rules only; container inventories and refrigeration are future systems.</summary>
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
            case StorageType.Books: return item.isBook;
            case StorageType.General: return item.itemType != ItemType.Clothing && !food && !item.isBook;
            default: return false;
        }
    }
}
