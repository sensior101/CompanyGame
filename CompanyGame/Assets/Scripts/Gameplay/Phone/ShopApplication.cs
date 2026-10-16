using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Placeholder catalogue entry until the item system exists.</summary>
[Serializable]
public class ShopItem
{
    public string itemId;
    public string displayName;
    [Min(1)] public long price = 1000;
}

public class ShopApplication : PhoneAppBase
{
    [SerializeField]
    private List<ShopItem> items = CreateDefaultItems();

    private readonly IWallet wallet = new BankWallet();

    public event Action Refreshed;
    public event Action<ShopItem> ItemPurchased;

    public IReadOnlyList<ShopItem> Items => items;

    protected override void OnOpened() => Refreshed?.Invoke();

    public long GetCash() => wallet.Balance;

    public bool CanAfford(long price) => wallet.CanAfford(price);

    public bool Purchase(long price) => wallet.TrySpend(price, MoneyChangeReason.Purchase);

    public bool Purchase(ShopItem item)
    {
        if (item == null || !Purchase(item.price)) return false;

        SharedWriteGate.SendPersonal("p.shop.buy", new SharedOpArgs { a = item.itemId }); // the server prices it (12.10.1)
        ItemPurchased?.Invoke(item);
        return true;
    }

    /// <summary>The server's price list for p.shop.buy (12.10.1): the default catalogue, never the client's.</summary>
    public static long DefaultPrice(string itemId) => CreateDefaultItems().Find(i => i.itemId == itemId)?.price ?? 0;

    private static List<ShopItem> CreateDefaultItems()
    {
        return new List<ShopItem>
        {
            new ShopItem { itemId = "item_triangle_kimbap", displayName = "삼각김밥", price = 1500 },
            new ShopItem { itemId = "item_water", displayName = "생수", price = 1000 },
            new ShopItem { itemId = "item_cup_noodle", displayName = "컵라면", price = 1200 },
            new ShopItem { itemId = "item_coffee", displayName = "캔커피", price = 2500 },
            new ShopItem { itemId = "item_lunchbox", displayName = "도시락", price = 4500 }
        };
    }
}
