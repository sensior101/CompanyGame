using System;
using System.Collections.Generic;
using UnityEngine;

public class StockApplication : PhoneAppBase
{
    [SerializeField]
    private StockMarketManager market;

    public event Action Refreshed;

    private void Awake()
    {
        if (market == null) market = FindAnyObjectByType<StockMarketManager>();
    }

    private void Start()
    {
        if (market != null) market.PricesUpdated += OnPricesUpdated;
    }

    private void OnDestroy()
    {
        if (market != null) market.PricesUpdated -= OnPricesUpdated;
    }

    protected override void OnOpened() => Refreshed?.Invoke();

    public bool IsMarketOpen => market != null && market.IsMarketOpen;

    public List<StockQuote> GetQuotes() => market != null ? market.GetQuotes() : new List<StockQuote>();

    public IReadOnlyList<NewsItem> GetNews() =>
        market != null ? market.Exchange.LatestNews : new List<NewsItem>();

    public IReadOnlyList<Holding> GetHoldings() =>
        market != null ? market.Exchange.Portfolio.holdings : new List<Holding>();

    public long GetPortfolioValue() => market != null ? market.GetPortfolioValue() : 0L;

    public TradeReceipt Buy(string stockId, int quantity) =>
        market != null ? market.Buy(stockId, quantity) : Unavailable(stockId, quantity);

    public TradeReceipt Sell(string stockId, int quantity) =>
        market != null ? market.Sell(stockId, quantity) : Unavailable(stockId, quantity);

    private static TradeReceipt Unavailable(string stockId, int quantity) =>
        new TradeReceipt { stockId = stockId, quantity = quantity, result = TradeResult.UnknownStock };

    private void OnPricesUpdated()
    {
        if (IsOpen) Refreshed?.Invoke();
    }
}
