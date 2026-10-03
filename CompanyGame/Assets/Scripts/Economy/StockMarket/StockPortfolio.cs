using System;
using System.Collections.Generic;

public enum TradeResult
{
    Success,
    MarketClosed,
    UnknownStock,
    InvalidQuantity,
    QuantityTooLarge,
    NotEnoughMoney,
    NotEnoughShares
}

public struct TradeReceipt
{
    public TradeResult result;
    public string stockId;
    public int quantity;
    public long unitPrice;
    public long fee;
    public long total;
    public long realizedProfit;

    public bool Success => result == TradeResult.Success;
}

[Serializable]
public class Holding
{
    public string stockId;
    public long quantity;
    public long totalCost;

    public long AverageCost => quantity > 0 ? totalCost / quantity : 0L;
}

/// <summary>Shares owned by the player. Cost basis uses the average-cost method and includes buy fees.</summary>
[Serializable]
public class StockPortfolio
{
    public List<Holding> holdings = new List<Holding>();

    public Holding Find(string stockId) => holdings.Find(h => h.stockId == stockId);

    public long GetQuantity(string stockId) => Find(stockId)?.quantity ?? 0L;

    public void AddShares(string stockId, long quantity, long cost)
    {
        var holding = Find(stockId);
        if (holding == null)
        {
            holding = new Holding { stockId = stockId };
            holdings.Add(holding);
        }
        holding.quantity += quantity;
        holding.totalCost += cost;
    }

    /// <summary>Removes shares and returns the cost basis that left with them.</summary>
    public long RemoveShares(string stockId, long quantity)
    {
        var holding = Find(stockId);
        if (holding == null || quantity <= 0 || quantity > holding.quantity) return 0L;

        long removedCost = quantity == holding.quantity
            ? holding.totalCost
            : (long)((decimal)holding.totalCost * quantity / holding.quantity);
        holding.quantity -= quantity;
        holding.totalCost -= removedCost;
        if (holding.quantity == 0) holdings.Remove(holding);
        return removedCost;
    }

    public long MarketValue(Func<string, long> priceOf)
    {
        long total = 0;
        foreach (var h in holdings) total += h.quantity * priceOf(h.stockId);
        return total;
    }
}
