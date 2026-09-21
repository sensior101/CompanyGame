using UnityEngine;

public class BankApplication : PhoneAppBase
{
    [SerializeField]
    private EconomyManager economyManager;

    private void Awake()
    {
        if (economyManager == null) economyManager = FindAnyObjectByType<EconomyManager>();
    }

    public bool Deposit(long amount)
    {
        return economyManager != null && economyManager.AddMoney(amount, MoneyChangeReason.Earned);
    }

    public bool Withdraw(long amount)
    {
        return economyManager != null && economyManager.TrySpend(amount, MoneyChangeReason.Spent);
    }

    public long GetBalance()
    {
        return economyManager != null ? economyManager.money : 0L;
    }
}
