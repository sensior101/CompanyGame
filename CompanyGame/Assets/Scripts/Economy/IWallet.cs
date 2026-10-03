/// <summary>Cash access for game systems, so their rules can be tested without a scene.</summary>
public interface IWallet
{
    long Balance { get; }
    bool CanAfford(long amount);
    bool TrySpend(long amount, MoneyChangeReason reason);
    bool AddMoney(long amount, MoneyChangeReason reason);
}

/// <summary>Wallet backed by BankManager, which owns the player's cash. Resolved lazily so scene load order does not matter.</summary>
public class BankWallet : IWallet
{
    public long Balance => BankManager.Instance != null ? BankManager.Instance.Money : 0L;

    public bool CanAfford(long amount) =>
        BankManager.Instance != null && BankManager.Instance.CanAfford(amount);

    public bool TrySpend(long amount, MoneyChangeReason reason) =>
        BankManager.Instance != null && BankManager.Instance.TrySpend(amount, reason);

    public bool AddMoney(long amount, MoneyChangeReason reason) =>
        BankManager.Instance != null && BankManager.Instance.AddMoney(amount, reason);
}
