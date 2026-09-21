using UnityEngine;

public class ShopApplication : PhoneAppBase
{
    [SerializeField]
    private EconomyManager economyManager;

    private void Awake()
    {
        if (economyManager == null) economyManager = FindAnyObjectByType<EconomyManager>();
    }

    public bool CanAfford(long price)
    {
        return economyManager != null && economyManager.CanAfford(price);
    }

    public bool Purchase(long price)
    {
        return economyManager != null && economyManager.TrySpend(price, MoneyChangeReason.Purchase);
    }
}
