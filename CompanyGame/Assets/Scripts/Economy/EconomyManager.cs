using UnityEngine;

public class EconomyManager : MonoBehaviour
{
    [SerializeField]
    private PropertyManager propertyManager;

    /// <summary>Compatibility facade for systems that already use EconomyManager.</summary>
    public long money => propertyManager != null ? propertyManager.Money : 0L;

    public PropertyManager MoneySource => propertyManager;

    private void Awake()
    {
        if (!propertyManager) propertyManager = FindAnyObjectByType<PropertyManager>();
    }

    public bool AddMoney(long amount, MoneyChangeReason reason = MoneyChangeReason.Earned)
    {
        return propertyManager != null && propertyManager.AddMoney(amount, reason);
    }

    public bool TrySpend(long amount, MoneyChangeReason reason = MoneyChangeReason.Spent)
    {
        return propertyManager != null && propertyManager.TrySpend(amount, reason);
    }

    public bool CanAfford(long amount)
    {
        return propertyManager != null && propertyManager.CanAfford(amount);
    }
}
