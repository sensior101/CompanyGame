using System;
using UnityEngine;

public class BankApplication : PhoneAppBase
{
    [SerializeField]
    private BankManager bankManager;

    [SerializeField]
    private EconomyManager economyManager;

    public event Action Refreshed;

    private void Awake()
    {
        if (bankManager == null) bankManager = FindAnyObjectByType<BankManager>();
        if (economyManager == null) economyManager = FindAnyObjectByType<EconomyManager>();
    }

    protected override void OnOpened() => Refreshed?.Invoke();

    public bool Deposit(long amount) => bankManager != null && bankManager.Account.Deposit(amount);

    public bool Withdraw(long amount) => bankManager != null && bankManager.Account.Withdraw(amount);

    public long GetBankBalance() => bankManager != null ? bankManager.Account.Balance : 0L;

    public long GetCash() => economyManager != null ? economyManager.money : 0L;
}
