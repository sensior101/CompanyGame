using System;
using UnityEngine;

/// <summary>Design-time definition of a listed stock. Current price and history live in StockState.</summary>
[Serializable]
public class StockData
{
    public string stockId;
    public string displayName;
    public string companyId;
    public string sector;
    [Min(1)] public long initialPrice = 10000;
    [Range(0f, 0.2f)] public float hourlyVolatility = 0.01f;

    public static StockData Make(string stockId, string name, string companyId, string sector,
        long price, float volatility)
    {
        return new StockData
        {
            stockId = stockId,
            displayName = name,
            companyId = companyId,
            sector = sector,
            initialPrice = price,
            hourlyVolatility = volatility
        };
    }
}
