using System;
using UnityEngine;

public class BankApplication : PhoneAppBase
{
    [SerializeField]
    private BankManager bankManager;

    public event Action Refreshed;

    private void Awake()
    {
        if (bankManager == null) bankManager = FindAnyObjectByType<BankManager>();
    }

    protected override void OnOpened() => Refreshed?.Invoke();

    public bool Deposit(long amount) => bankManager != null && bankManager.Account.Deposit(amount);

    public bool Withdraw(long amount) => bankManager != null && bankManager.Account.Withdraw(amount);

    public long GetBankBalance() => bankManager != null ? bankManager.Account.Balance : 0L;

    public long GetCash() => PropertyManager.Instance != null ? PropertyManager.Instance.Money : 0L;
}
