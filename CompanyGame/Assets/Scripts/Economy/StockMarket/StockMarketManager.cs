using System;
using System.Collections.Generic;
using UnityEngine;

public class StockMarketManager : GameSystem<StockMarketManager>
{
    [SerializeField]
    private StockMarketSettings settings = new StockMarketSettings();

    [SerializeField]
    private CompanyManager companyManager;

    private StockExchange exchange;
    private IGameClock subscribedClock;

    public event Action PricesUpdated;
    public event Action<IReadOnlyList<NewsItem>> NewsPublished;

    public StockExchange Exchange
    {
        get
        {
            EnsureInitialized();
            return exchange;
        }
    }

    public GameTime Now => GameClock.Current != null ? GameClock.Current.Now : default;

    public bool IsMarketOpen => Exchange.IsOpen(Now);

    protected override void OnSystemAwake() => EnsureInitialized();

    private void Start()
    {
        subscribedClock = GameClock.Current;
        if (subscribedClock == null)
        {
            Debug.LogWarning("StockMarketManager: no game clock found, prices will not advance.");
            return;
        }
        subscribedClock.HourChanged += OnHourChanged;
    }

    protected override void OnDestroy()
    {
        if (subscribedClock != null) subscribedClock.HourChanged -= OnHourChanged;
        base.OnDestroy();
    }

    public TradeReceipt Buy(string stockId, int quantity) => Tell("p.stock.buy", Exchange.Buy(Now, stockId, quantity));

    public TradeReceipt Sell(string stockId, int quantity) => Tell("p.stock.sell", Exchange.Sell(Now, stockId, quantity));

    // On a sync-layer server the trade is shown at once and the server does it again on its ledger (12.10.1).
    private static TradeReceipt Tell(string kind, TradeReceipt receipt)
    {
        if (receipt.result == TradeResult.Success)
            SharedWriteGate.SendPersonal(kind, new SharedOpArgs { a = receipt.stockId, n = receipt.quantity });
        return receipt;
    }

    public List<StockQuote> GetQuotes() => Exchange.GetQuotes();

    public long GetPortfolioValue() => Exchange.Portfolio.MarketValue(Exchange.GetPrice);

    public ListingResult TryListCompany(string companyId)
    {
        var company = companyManager != null ? companyManager.Market.Get(companyId) : null;
        var result = Exchange.TryList(company);
        if (result == ListingResult.Success) company.listed = true;
        return result;
    }

    private void EnsureInitialized()
    {
        if (exchange != null) return;

        if (companyManager == null) companyManager = FindAnyObjectByType<CompanyManager>();

        IMarketEnvironment environment = companyManager != null
            ? companyManager.Market
            : new NullMarketEnvironment();
        var rng = settings.randomSeed != 0 ? new System.Random(settings.randomSeed) : new System.Random();

        exchange = new StockExchange(settings, new BankWallet(), environment, rng);
        exchange.PricesUpdated += () => PricesUpdated?.Invoke();
        exchange.NewsPublished += news => NewsPublished?.Invoke(news);
    }

    private void OnHourChanged(GameTime time) => Exchange.OnHourChanged(time);
}
