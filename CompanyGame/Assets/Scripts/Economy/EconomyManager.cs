using UnityEngine;

public class EconomyManager : MonoBehaviour
{
    [SerializeField]
    private PropertyManager propertyManager;

    /// <summary>Compatibility facade for systems that already use EconomyManager.</summary>
    public long money => MoneySource != null ? MoneySource.Money : 0L;

    public PropertyManager MoneySource => PropertyManager.Instance ? PropertyManager.Instance : propertyManager;
    public long CarriedMoney => InventoryManager.Instance ? CashService.CarriedTotal(InventoryManager.Instance.State) : 0;

    private void Awake()
    {
        if (!propertyManager) propertyManager = FindAnyObjectByType<PropertyManager>();
    }

    public bool AddMoney(long amount, MoneyChangeReason reason = MoneyChangeReason.Earned)
    {
        PropertyManager bank = MoneySource ? MoneySource : PropertyManager.EnsureInstance();
        return bank != null && bank.AddMoney(amount, reason);
    }

    public bool TrySpend(long amount, MoneyChangeReason reason = MoneyChangeReason.Spent)
    {
        return InventoryManager.Instance && CashService.TryPayNpc(InventoryManager.Instance.State, amount, out _);
    }

    public bool CanAfford(long amount)
    {
        return amount > 0 && CarriedMoney >= amount;
    }
}
