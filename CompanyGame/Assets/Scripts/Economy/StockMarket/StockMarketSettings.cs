using System;
using System.Collections.Generic;
using UnityEngine;

public enum PriceModelType
{
    RandomWalk,
    Fundamental
}

/// <summary>Every tunable number of the stock market. Edit these in the Inspector; no code change needed.</summary>
[Serializable]
public class StockMarketSettings
{
    [Header("Hours (README 4-5)")]
    [Range(0, 23)] public int openHour = 9;
    [Range(1, 23)] public int closeHour = 16;
    [Range(0, 23)] public int newsHour = 8;
    public bool alwaysOpenForTesting;

    [Header("Price model")]
    public PriceModelType priceModel = PriceModelType.Fundamental;
    public float randomWalkHourlyDrift = 0.0002f;
    [Range(0f, 1f)] public float meanReversionPerHour = 0.01f;

    [Header("Fundamental weights (README 4-5: revenue, share, level, demand)")]
    public float revenueGrowthWeight = 0.5f;
    public float shareDeltaWeight = 5f;
    public float levelWeight = 0.02f;
    public float demandTrendWeight = 0.3f;
    [Min(0.01f)] public float minFundamentalMultiplier = 0.3f;
    [Min(0.01f)] public float maxFundamentalMultiplier = 3f;

    [Header("Trading rules")]
    [Range(0f, 1f)] public float dailyLimitRate = 0.3f;
    [Min(1)] public long minPrice = 100;
    [Range(0f, 0.05f)] public float tradeFeeRate = 0.0015f;
    [Min(1)] public int maxOrderQuantity = 100_000;
    [Min(1)] public int priceHistoryLength = 240;

    [Header("Listing (README 7-2)")]
    [Min(1)] public int listingMinLevel = 7;
    [Min(1)] public long ipoBasePricePerLevel = 5000;
    [Range(0f, 0.2f)] public float ipoHourlyVolatility = 0.015f;

    [Header("News text. {0}=name {1}=change % {2}=price")]
    [Range(0f, 0.1f)] public float newsFlatThreshold = 0.001f;
    public string newsUpFormat = "{0} 전일 대비 {1:0.0}% 상승 마감 ({2:N0}원)";
    public string newsDownFormat = "{0} 전일 대비 {1:0.0}% 하락 마감 ({2:N0}원)";
    public string newsFlatFormat = "{0} 보합 마감 ({2:N0}원)";
    public string newsEventFormat = "[{0}] {1} (남은 기간 {2}일)";

    [Header("Randomness (0 = different every run)")]
    public int randomSeed;

    [Header("Listed stocks (README 4-5)")]
    public List<StockData> listings = CreateDefaultListings();

    public static List<StockData> CreateDefaultListings()
    {
        return new List<StockData>
        {
            StockData.Make("stock_sg_hynix", "sg하이닉스", "company_sg_hynix", "tech", 180000, 0.012f),
            StockData.Make("stock_saseong", "사성전자", "company_saseong", "tech", 75000, 0.010f),
            StockData.Make("stock_kia", "기아자동차", "company_kia", "auto", 95000, 0.010f),
            StockData.Make("stock_krafton", "클래프톤", "company_krafton", "game", 250000, 0.015f),
            StockData.Make("stock_handae", "한대건설", "company_handae", "construction", 40000, 0.012f),
            StockData.Make("stock_hitejinro", "하이트진로", "company_hitejinro", "beverage", 25000, 0.008f)
        };
    }
}
