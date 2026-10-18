using System;
using UnityEngine;

/// <summary>
/// The NPC shops' offers, out of the scenes (Docs/Multiplayer_Redesign.md 12.10.4): a server process loads one scene only,
/// yet decides every p.trade, so it reads the same offers here. Resources/Trade/TradeCatalog.asset; NpcTrader names its shop.
/// Offers with a fulfillment (the library's) are made in code and are not here.
/// </summary>
[CreateAssetMenu(fileName = "TradeCatalog", menuName = "Company Game/Trade/Trade Catalog")]
public sealed class TradeCatalog : ScriptableObject
{
    public const string ResourcePath = "Trade/TradeCatalog";
    public const string ConvenienceStore = "convenience", LibraryCafe = "library.cafe";

    [Serializable]
    public sealed class Shop
    {
        public string id;
        public TradeOffer[] offers = Array.Empty<TradeOffer>();
    }

    public Shop[] shops = Array.Empty<Shop>();
    /// <summary>Items the server gives on p.grant while the bag has none (12.10.5): the employee card.</summary>
    public ItemData[] grants = Array.Empty<ItemData>();

    static TradeCatalog loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetLoaded() => loaded = null;

    /// <summary>Every item the shops trade and the grant list (some live outside Resources/Inventory: InventorySnapshot finds them here).</summary>
    public static System.Collections.Generic.IEnumerable<ItemData> Items()
    {
        if (!loaded) loaded = Resources.Load<TradeCatalog>(ResourcePath);
        if (!loaded) yield break;
        if (loaded.grants != null) foreach (var item in loaded.grants) if (item) yield return item;
        foreach (var shop in loaded.shops)
            if (shop?.offers != null)
                foreach (var offer in shop.offers)
                {
                    if (offer == null) continue;
                    if (offer.give.item) yield return offer.give.item;
                    if (offer.get.item) yield return offer.get.item;
                }
    }

    /// <summary>The item <paramref name="itemId"/> of the grant list (12.10.5), or null when it is not on it.</summary>
    public static ItemData Grant(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        if (!loaded) loaded = Resources.Load<TradeCatalog>(ResourcePath);
        if (!loaded || loaded.grants == null) return null;
        foreach (var item in loaded.grants) if (item && item.itemId == itemId) return item;
        return null;
    }

    /// <summary>The offers of shop <paramref name="shopId"/>, or null when there is no such shop.</summary>
    public static TradeOffer[] Offers(string shopId)
    {
        if (string.IsNullOrEmpty(shopId)) return null;
        if (!loaded) loaded = Resources.Load<TradeCatalog>(ResourcePath);
        if (!loaded) return null;
        foreach (var shop in loaded.shops)
            if (shop != null && shop.id == shopId) return shop.offers ?? Array.Empty<TradeOffer>();
        return null;
    }
}
