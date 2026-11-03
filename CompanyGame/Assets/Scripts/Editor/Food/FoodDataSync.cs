using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes FoodMenu (price, fullness, stamina, stress relief, fridge days, 든든함) into every food ItemData and the shops'
/// cash prices. A food is matched by its itemId, then by its asset name (the food library id). Safe to run again.
/// Batch: Unity -batchmode -quit -projectPath ... -executeMethod FoodDataSync.Run
/// </summary>
public static class FoodDataSync
{
    [MenuItem("CompanyGame/Setup/Sync Food Data")]
    public static void Run()
    {
        int items = 0, prices = 0;
        var found = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (!item || !FoodMenu.TryGet(item, out var food)) continue;
            found.Add(food.id);
            if (!Apply(item, food)) continue;
            EditorUtility.SetDirty(item);
            items++;
        }
        var catalog = Resources.Load<TradeCatalog>(TradeCatalog.ResourcePath);
        if (catalog)
        {
            foreach (var shop in catalog.shops)
            {
                if (shop?.offers == null) continue;
                foreach (var offer in shop.offers)
                {
                    // Cash for a menu food charges the menu price.
                    if (offer == null || offer.give.item || !offer.get.item || !FoodMenu.TryGet(offer.get.item, out var food)) continue;
                    long price = food.price * offer.get.Count;
                    if (offer.give.cash == price) continue;
                    offer.give.cash = price;
                    prices++;
                }
            }
            if (prices > 0) EditorUtility.SetDirty(catalog);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[FoodDataSync] {items} food items and {prices} shop prices updated; " +
            $"{FoodMenu.All.Length - found.Count} of {FoodMenu.All.Length} menu foods have no item yet.");
    }

    internal static bool Apply(ItemData item, FoodMenu.Entry food)
    {
        float shelfLife = food.fridgeDays * 24f;
        if (item.price == food.price && item.fullnessRestore == food.fullness && item.staminaRestore == food.stamina &&
            item.stressRelief == food.stressRelief && item.restaurantMeal == food.RestaurantMeal &&
            item.consumableType == food.Type && item.shelfLifeHours == shelfLife && item.healthRestore == 0f) return false;
        item.price = food.price;
        item.fullnessRestore = food.fullness;
        item.staminaRestore = food.stamina;
        item.stressRelief = food.stressRelief;
        item.restaurantMeal = food.RestaurantMeal;
        item.consumableType = food.Type;
        item.shelfLifeHours = shelfLife;
        item.healthRestore = 0f; // Food fills you up now; health comes back from being full (ServerCombat).
        return true;
    }
}
