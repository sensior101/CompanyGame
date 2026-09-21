public class ShopApplication : PhoneAppBase
{
    private readonly IWallet wallet = new PropertyWallet();

    public bool CanAfford(long price) => wallet.CanAfford(price);

    public bool Purchase(long price) => wallet.TrySpend(price, MoneyChangeReason.Purchase);
}
