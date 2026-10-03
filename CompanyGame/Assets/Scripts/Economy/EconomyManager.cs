using UnityEngine;

public class EconomyManager : MonoBehaviour
{
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("bank")]
    private BankManager bank;

    /// <summary>Compatibility facade for systems that already use EconomyManager.</summary>
    public long money => MoneySource != null ? MoneySource.Money : 0L;

    public BankManager MoneySource => BankManager.Instance ? BankManager.Instance : bank;
    public long CarriedMoney => InventoryManager.Instance ? CashService.CarriedTotal(InventoryManager.Instance.State) : 0;

    private void Awake()
    {
        if (!bank) bank = FindAnyObjectByType<BankManager>();
    }

    public bool AddMoney(long amount, MoneyChangeReason reason = MoneyChangeReason.Earned)
    {
        BankManager bank = MoneySource ? MoneySource : BankManager.EnsureInstance();
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
