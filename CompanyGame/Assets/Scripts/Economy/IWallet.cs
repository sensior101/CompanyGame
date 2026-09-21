/// <summary>Cash access for game systems, so their rules can be tested without a scene.</summary>
public interface IWallet
{
    long Balance { get; }
    bool CanAfford(long amount);
    bool TrySpend(long amount, MoneyChangeReason reason);
    bool AddMoney(long amount, MoneyChangeReason reason);
}

public class EconomyWallet : IWallet
{
    private readonly EconomyManager economy;

    public EconomyWallet(EconomyManager economy)
    {
        this.economy = economy;
    }

    public long Balance => economy != null ? economy.money : 0L;

    public bool CanAfford(long amount) => economy != null && economy.CanAfford(amount);

    public bool TrySpend(long amount, MoneyChangeReason reason) =>
        economy != null && economy.TrySpend(amount, reason);

    public bool AddMoney(long amount, MoneyChangeReason reason) =>
        economy != null && economy.AddMoney(amount, reason);
}
