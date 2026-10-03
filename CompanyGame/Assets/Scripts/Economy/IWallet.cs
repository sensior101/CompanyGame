/// <summary>Cash access for game systems, so their rules can be tested without a scene.</summary>
public interface IWallet
{
    long Balance { get; }
    bool CanAfford(long amount);
    bool TrySpend(long amount, MoneyChangeReason reason);
    bool AddMoney(long amount, MoneyChangeReason reason);
}

/// <summary>Wallet backed by PropertyManager, which owns the player's cash. Resolved lazily so scene load order does not matter.</summary>
public class PropertyWallet : IWallet
{
    public long Balance => PropertyManager.Instance != null ? PropertyManager.Instance.Money : 0L;

    public bool CanAfford(long amount) =>
        PropertyManager.Instance != null && PropertyManager.Instance.CanAfford(amount);

    public bool TrySpend(long amount, MoneyChangeReason reason) =>
        PropertyManager.Instance != null && PropertyManager.Instance.TrySpend(amount, reason);

    public bool AddMoney(long amount, MoneyChangeReason reason) =>
        PropertyManager.Instance != null && PropertyManager.Instance.AddMoney(amount, reason);
}
