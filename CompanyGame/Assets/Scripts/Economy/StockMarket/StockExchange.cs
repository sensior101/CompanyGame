using System;
using System.Collections.Generic;

public enum NewsKind
{
    Stock,
    Event
}

[Serializable]
public class NewsItem
{
    public GameTime time;
    public NewsKind kind;
    public string headline;
    public string relatedId;
    public float changeRate;
}

[Serializable]
public class StockState
{
    public string stockId;
    public long price;
    public long previousClose;
    public long dayOpen;
    public long dayHigh;
    public long dayLow;
    public float lastSessionChangeRate;
    public List<long> history = new List<long>();
}

public struct StockQuote
{
    public string stockId;
    public string displayName;
    public string sector;
    public string companyId;
    public long price;
    public long previousClose;
    public long change;
    public float changeRate;
    public long dayHigh;
    public long dayLow;
}

public enum ListingResult
{
    Success,
    UnknownCompany,
    LevelTooLow,
    AlreadyListed
}

[Serializable]
public class StockExchangeState
{
    public List<StockState> states = new List<StockState>();
    public List<StockData> ipoListings = new List<StockData>();
    public StockPortfolio portfolio = new StockPortfolio();
}

/// <summary>Stock market rules and simulation with no scene dependencies. Driven by hour ticks and trade calls.</summary>
public class StockExchange
{
    private readonly StockMarketSettings settings;
    private readonly IWallet wallet;
    private readonly IMarketEnvironment environment;
    private readonly Random rng;
    private readonly IStockPriceModel priceModel;
    private readonly List<StockData> baseListings = new List<StockData>();
    private readonly List<StockData> ipoListings = new List<StockData>();
    private readonly List<StockState> states = new List<StockState>();
    private readonly List<NewsItem> latestNews = new List<NewsItem>();
    private StockPortfolio portfolio = new StockPortfolio();

    public event Action PricesUpdated;
    public event Action<IReadOnlyList<NewsItem>> NewsPublished;
    public event Action<TradeReceipt> TradeExecuted;

    public StockExchange(StockMarketSettings settings, IWallet wallet, IMarketEnvironment environment, Random rng)
    {
        this.settings = settings;
        this.wallet = wallet;
        this.environment = environment ?? new NullMarketEnvironment();
        this.rng = rng ?? new Random();
        priceModel = PriceModelFactory.Create(settings);

        foreach (var data in settings.listings)
        {
            baseListings.Add(data);
            states.Add(NewState(data));
        }
    }

    public StockPortfolio Portfolio => portfolio;

    public IReadOnlyList<NewsItem> LatestNews => latestNews;

    public bool IsOpen(GameTime time) =>
        settings.alwaysOpenForTesting || (time.hour >= settings.openHour && time.hour < settings.closeHour);

    public StockData FindListing(string stockId) =>
        baseListings.Find(l => l.stockId == stockId) ?? ipoListings.Find(l => l.stockId == stockId);

    public StockState FindState(string stockId) => states.Find(s => s.stockId == stockId);

    public long GetPrice(string stockId) => FindState(stockId)?.price ?? 0L;

    public bool TryGetQuote(string stockId, out StockQuote quote)
    {
        quote = default;
        var state = FindState(stockId);
        var data = FindListing(stockId);
        if (state == null || data == null) return false;

        long change = state.price - state.previousClose;
        quote = new StockQuote
        {
            stockId = state.stockId,
            displayName = data.displayName,
            sector = data.sector,
            companyId = data.companyId,
            price = state.price,
            previousClose = state.previousClose,
            change = change,
            changeRate = state.previousClose > 0 ? (float)change / state.previousClose : 0f,
            dayHigh = state.dayHigh,
            dayLow = state.dayLow
        };
        return true;
    }

    public List<StockQuote> GetQuotes()
    {
        var quotes = new List<StockQuote>(states.Count);
        foreach (var state in states)
        {
            if (TryGetQuote(state.stockId, out var quote)) quotes.Add(quote);
        }
        return quotes;
    }

    public void OnHourChanged(GameTime time)
    {
        if (time.hour == settings.newsHour) PublishNews(time);
        if (time.hour < settings.openHour || time.hour > settings.closeHour) return;

        bool opening = time.hour == settings.openHour;
        foreach (var state in states) Tick(state, opening);
        if (time.hour == settings.closeHour) EndSession();

        PricesUpdated?.Invoke();
    }

    public TradeReceipt Buy(GameTime now, string stockId, int quantity)
    {
        var receipt = new TradeReceipt { stockId = stockId, quantity = quantity };
        var state = FindState(stockId);

        if (quantity <= 0) return Finish(receipt, TradeResult.InvalidQuantity);
        if (quantity > settings.maxOrderQuantity) return Finish(receipt, TradeResult.QuantityTooLarge);
        if (state == null) return Finish(receipt, TradeResult.UnknownStock);
        if (!IsOpen(now)) return Finish(receipt, TradeResult.MarketClosed);

        long gross;
        try
        {
            gross = checked(state.price * quantity);
        }
        catch (OverflowException)
        {
            return Finish(receipt, TradeResult.QuantityTooLarge);
        }

        long fee = (long)Math.Ceiling(gross * (double)settings.tradeFeeRate);
        long total = gross + fee;
        receipt.unitPrice = state.price;
        receipt.fee = fee;
        receipt.total = total;

        if (!wallet.CanAfford(total) || !wallet.TrySpend(total, MoneyChangeReason.Purchase))
        {
            return Finish(receipt, TradeResult.NotEnoughMoney);
        }

        portfolio.AddShares(stockId, quantity, total);
        return Finish(receipt, TradeResult.Success);
    }

    public TradeReceipt Sell(GameTime now, string stockId, int quantity)
    {
        var receipt = new TradeReceipt { stockId = stockId, quantity = quantity };
        var state = FindState(stockId);

        if (quantity <= 0) return Finish(receipt, TradeResult.InvalidQuantity);
        if (quantity > settings.maxOrderQuantity) return Finish(receipt, TradeResult.QuantityTooLarge);
        if (state == null) return Finish(receipt, TradeResult.UnknownStock);
        if (!IsOpen(now)) return Finish(receipt, TradeResult.MarketClosed);
        if (portfolio.GetQuantity(stockId) < quantity) return Finish(receipt, TradeResult.NotEnoughShares);

        long gross = state.price * quantity;
        long fee = (long)Math.Ceiling(gross * (double)settings.tradeFeeRate);
        long net = Math.Max(0L, gross - fee);
        receipt.unitPrice = state.price;
        receipt.fee = fee;
        receipt.total = net;

        long costBasis = portfolio.RemoveShares(stockId, quantity);
        if (net > 0 && !wallet.AddMoney(net, MoneyChangeReason.Sale))
        {
            portfolio.AddShares(stockId, quantity, costBasis);
            return Finish(receipt, TradeResult.NotEnoughMoney);
        }

        receipt.realizedProfit = net - costBasis;
        return Finish(receipt, TradeResult.Success);
    }

    public ListingResult TryList(CompanyState company)
    {
        if (company == null) return ListingResult.UnknownCompany;
        if (company.level < settings.listingMinLevel) return ListingResult.LevelTooLow;

        string stockId = "stock_" + company.companyId;
        if (FindListing(stockId) != null) return ListingResult.AlreadyListed;

        var data = StockData.Make(stockId, company.displayName, company.companyId, company.sector,
            Math.Max(settings.minPrice, settings.ipoBasePricePerLevel * company.level),
            settings.ipoHourlyVolatility);
        ipoListings.Add(data);
        states.Add(NewState(data));
        return ListingResult.Success;
    }

    public StockExchangeState CaptureState()
    {
        return new StockExchangeState
        {
            states = new List<StockState>(states),
            ipoListings = new List<StockData>(ipoListings),
            portfolio = portfolio
        };
    }

    public void RestoreState(StockExchangeState state)
    {
        if (state == null) return;

        ipoListings.Clear();
        ipoListings.AddRange(state.ipoListings);
        foreach (var data in ipoListings)
        {
            if (states.Find(s => s.stockId == data.stockId) == null) states.Add(NewState(data));
        }

        foreach (var saved in state.states)
        {
            int index = states.FindIndex(s => s.stockId == saved.stockId);
            if (index >= 0) states[index] = saved;
        }

        portfolio = state.portfolio ?? new StockPortfolio();
    }

    private StockState NewState(StockData data)
    {
        long price = Math.Max(settings.minPrice, data.initialPrice);
        var state = new StockState
        {
            stockId = data.stockId,
            price = price,
            previousClose = price,
            dayOpen = price,
            dayHigh = price,
            dayLow = price
        };
        state.history.Add(price);
        return state;
    }

    private TradeReceipt Finish(TradeReceipt receipt, TradeResult result)
    {
        receipt.result = result;
        TradeExecuted?.Invoke(receipt);
        return receipt;
    }

    private void Tick(StockState state, bool opening)
    {
        var data = FindListing(state.stockId);
        if (data == null) return;

        double multiplier = 1.0;
        if (!string.IsNullOrEmpty(data.companyId) && environment.TryGetMetrics(data.companyId, out var metrics))
        {
            multiplier = FundamentalCalculator.Multiplier(metrics, settings);
        }

        var context = new StockPriceContext
        {
            currentPrice = state.price,
            basePrice = data.initialPrice,
            hourlyVolatility = data.hourlyVolatility,
            eventShock = environment.GetHourlyShock(data.sector),
            fundamentalMultiplier = multiplier
        };

        double next = priceModel.NextPrice(context, rng);
        double reference = state.previousClose > 0 ? state.previousClose : state.price;
        next = Math.Max(reference * (1.0 - settings.dailyLimitRate),
            Math.Min(reference * (1.0 + settings.dailyLimitRate), next));

        long price = double.IsNaN(next) || double.IsInfinity(next)
            ? state.price
            : Math.Max(settings.minPrice, (long)Math.Round(next));
        state.price = price;

        if (opening)
        {
            state.dayOpen = price;
            state.dayHigh = price;
            state.dayLow = price;
        }
        else
        {
            state.dayHigh = Math.Max(state.dayHigh, price);
            state.dayLow = Math.Min(state.dayLow, price);
        }

        state.history.Add(price);
        int excess = state.history.Count - settings.priceHistoryLength;
        if (excess > 0) state.history.RemoveRange(0, excess);
    }

    private void EndSession()
    {
        foreach (var state in states)
        {
            state.lastSessionChangeRate = state.previousClose > 0
                ? (float)(state.price - state.previousClose) / state.previousClose
                : 0f;
            state.previousClose = state.price;
        }
    }

    private void PublishNews(GameTime time)
    {
        latestNews.Clear();

        foreach (var e in environment.ActiveEvents)
        {
            latestNews.Add(new NewsItem
            {
                time = time,
                kind = NewsKind.Event,
                relatedId = e.eventId,
                headline = SafeFormat(settings.newsEventFormat, e.displayName, e.description, e.DaysLeft(time.absoluteDay))
            });
        }

        var byMove = new List<StockState>(states);
        byMove.Sort((a, b) => Math.Abs(b.lastSessionChangeRate).CompareTo(Math.Abs(a.lastSessionChangeRate)));
        foreach (var state in byMove)
        {
            var data = FindListing(state.stockId);
            if (data == null) continue;

            float rate = state.lastSessionChangeRate;
            string format = rate > settings.newsFlatThreshold ? settings.newsUpFormat
                : rate < -settings.newsFlatThreshold ? settings.newsDownFormat
                : settings.newsFlatFormat;
            latestNews.Add(new NewsItem
            {
                time = time,
                kind = NewsKind.Stock,
                relatedId = state.stockId,
                changeRate = rate,
                headline = SafeFormat(format, data.displayName, Math.Abs(rate * 100f), state.price)
            });
        }

        NewsPublished?.Invoke(latestNews);
    }

    private static string SafeFormat(string format, params object[] args)
    {
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
