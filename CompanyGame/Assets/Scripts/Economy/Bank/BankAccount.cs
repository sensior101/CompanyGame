using System;

[Serializable]
public class BankAccountState
{
    public long balance;
}

/// <summary>A bank balance separate from wallet cash. Deposits and withdrawals only move money between the two.</summary>
public class BankAccount
{
    private readonly IWallet wallet;
    private long balance;

    public event Action<long> BalanceChanged;

    public BankAccount(IWallet wallet, long initialBalance = 0)
    {
        this.wallet = wallet;
        balance = Math.Max(0L, initialBalance);
    }

    public long Balance => balance;

    public bool Deposit(long amount)
    {
        if (amount <= 0 || balance > long.MaxValue - amount) return false;
        if (!wallet.TrySpend(amount, MoneyChangeReason.BankDeposit)) return false;

        balance += amount;
        BalanceChanged?.Invoke(balance);
        return true;
    }

    public bool Withdraw(long amount)
    {
        if (amount <= 0 || amount > balance) return false;
        if (!wallet.AddMoney(amount, MoneyChangeReason.BankWithdrawal)) return false;

        balance -= amount;
        BalanceChanged?.Invoke(balance);
        return true;
    }

    public BankAccountState CaptureState() => new BankAccountState { balance = balance };

    public void RestoreState(BankAccountState state)
    {
        balance = state != null ? Math.Max(0L, state.balance) : 0L;
        BalanceChanged?.Invoke(balance);
    }
}
