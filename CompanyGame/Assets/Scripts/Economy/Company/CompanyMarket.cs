using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

public enum ShareModelType
{
    Proportional,
    Softmax
}

[Serializable]
public class CompanyMarketSettings
{
    [Header("Consumers (README 3-1: 10k ~ 10M)")]
    [Min(1)] public long totalConsumers = 1_000_000;
    [Min(0f)] public float purchasesPerConsumerPerDay = 0.5f;
    [Range(0f, 0.5f)] public float dailyDemandNoise = 0.05f;

    [Header("Market share model")]
    public ShareModelType shareModel = ShareModelType.Proportional;
    [Min(0f)] public float qualityExponent = 1f;
    [Min(0f)] public float marketingExponent = 0.7f;
    [Min(0f)] public float priceExponent = 1f;
    [Min(0.05f)] public float softmaxTemperature = 0.5f;

    [Header("Company growth (README 7-1)")]
    [Min(1)] public long capacityConsumersPerProductionLevel = 30_000;
    [Min(1)] public int maxCompanyLevel = 10;
    [Min(1)] public int maxSubLevel = 10;
    [Min(0)] public int subLevelRequirementOffset = 1;

    [Header("Metrics windows for stock fundamentals")]
    [Min(1)] public int metricsRecentDays = 3;
    [Min(1)] public int metricsBaselineDays = 7;
    [Min(1)] public int historyDays = 30;

    [Header("Economic events")]
    public bool eventsEnabled = true;
    public List<MarketEventDef> events = MarketEventDef.CreateDefaults();

    [Header("Incumbent companies")]
    public List<CompanyData> companies = CompanyData.CreateDefaults();
}

/// <summary>Trend numbers describing how a company is doing; stock fundamentals are built from these.</summary>
public struct CompanyMetrics
{
    public double revenueGrowth;
    public double demandTrend;
    public double shareDelta;
    public int levelDelta;
}

public struct ShareSlice
{
    public string companyId;
    public string displayName;
    public float share;
    public long consumers;
    public bool isPlayerOwned;
}

/// <summary>What the stock market needs to know about the wider economy.</summary>
public interface IMarketEnvironment
{
    bool TryGetMetrics(string companyId, out CompanyMetrics metrics);
    double GetHourlyShock(string sector);
    IReadOnlyList<ActiveMarketEvent> ActiveEvents { get; }
}

public class NullMarketEnvironment : IMarketEnvironment
{
    private static readonly IReadOnlyList<ActiveMarketEvent> NoEvents = new List<ActiveMarketEvent>();

    public bool TryGetMetrics(string companyId, out CompanyMetrics metrics)
    {
        metrics = default;
        return false;
    }

    public double GetHourlyShock(string sector) => 0.0;

    public IReadOnlyList<ActiveMarketEvent> ActiveEvents => NoEvents;
}

public interface IShareModel
{
    void ComputeShares(IReadOnlyList<CompanyState> companies, CompanyMarketSettings settings, double[] output);
}

public static class ShareMath
{
    public static double Attractiveness(CompanyState c, CompanyMarketSettings s)
    {
        double quality = Math.Pow(Math.Max(1, c.qualityLevel), s.qualityExponent);
        double marketing = Math.Pow(1.0 + Math.Max(0, c.marketingLevel), s.marketingExponent);
        double priceRatio = (double)Math.Max(1L, c.productPrice) / Math.Max(1L, c.referencePrice);
        double pricePenalty = Math.Pow(priceRatio, -s.priceExponent);
        return Math.Max(0.0, c.baseAppeal) * quality * marketing * pricePenalty;
    }
}

public class ProportionalShareModel : IShareModel
{
    public void ComputeShares(IReadOnlyList<CompanyState> companies, CompanyMarketSettings settings, double[] output)
    {
        double sum = 0.0;
        for (int i = 0; i < companies.Count; i++)
        {
            output[i] = ShareMath.Attractiveness(companies[i], settings);
            sum += output[i];
        }

        for (int i = 0; i < companies.Count; i++)
        {
            output[i] = sum > 0.0 ? output[i] / sum : 1.0 / companies.Count;
        }
    }
}

/// <summary>Winner-takes-more variant: lower temperature concentrates consumers on the most attractive companies.</summary>
public class SoftmaxShareModel : IShareModel
{
    public void ComputeShares(IReadOnlyList<CompanyState> companies, CompanyMarketSettings settings, double[] output)
    {
        int n = companies.Count;
        double mean = 0.0;
        for (int i = 0; i < n; i++)
        {
            output[i] = ShareMath.Attractiveness(companies[i], settings);
            mean += output[i];
        }
        mean /= n;

        double temperature = Math.Max(0.05, settings.softmaxTemperature);
        double max = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            output[i] = mean > 0.0 ? output[i] / mean / temperature : 0.0;
            if (output[i] > max) max = output[i];
        }

        double sum = 0.0;
        for (int i = 0; i < n; i++)
        {
            output[i] = Math.Exp(output[i] - max);
            sum += output[i];
        }

        for (int i = 0; i < n; i++) output[i] /= sum;
    }
}

[Serializable]
public class CompanyMarketState
{
    public List<CompanyState> companies = new List<CompanyState>();
    public List<ActiveMarketEvent> activeEvents = new List<ActiveMarketEvent>();
}

/// <summary>Splits the consumer pie between companies each day and records their sales.</summary>
public class CompanyMarket : IMarketEnvironment
{
    private readonly CompanyMarketSettings settings;
    private readonly Random rng;
    private readonly List<CompanyState> companies = new List<CompanyState>();
    private readonly MarketEventScheduler events;
    private readonly IShareModel shareModel;

    public CompanyMarket(CompanyMarketSettings settings, Random rng = null)
    {
        this.settings = settings;
        this.rng = rng ?? new Random();
        events = new MarketEventScheduler(
            settings.eventsEnabled ? settings.events : new List<MarketEventDef>(), this.rng);
        shareModel = settings.shareModel == ShareModelType.Softmax
            ? (IShareModel)new SoftmaxShareModel()
            : new ProportionalShareModel();

        foreach (var data in settings.companies) AddCompany(CompanyState.FromData(data));
    }

    public IReadOnlyList<CompanyState> Companies => companies;

    public MarketEventScheduler Events => events;

    public IReadOnlyList<ActiveMarketEvent> ActiveEvents => events.Active;

    public long TotalConsumers => settings.totalConsumers;

    public CompanyState Get(string companyId) => companies.Find(c => c.companyId == companyId);

    public bool AddCompany(CompanyState company)
    {
        if (company == null || string.IsNullOrEmpty(company.companyId) || Get(company.companyId) != null) return false;

        companies.Add(company);
        RefreshShares();
        return true;
    }

    public void RefreshShares()
    {
        if (companies.Count == 0) return;

        var weights = new double[companies.Count];
        shareModel.ComputeShares(companies, settings, weights);
        for (int i = 0; i < companies.Count; i++)
        {
            companies[i].share = (float)weights[i];
            companies[i].consumers = (long)Math.Round(weights[i] * settings.totalConsumers);
        }
    }

    public List<ShareSlice> GetShareBreakdown()
    {
        var slices = new List<ShareSlice>(companies.Count);
        foreach (var c in companies)
        {
            slices.Add(new ShareSlice
            {
                companyId = c.companyId,
                displayName = c.displayName,
                share = c.share,
                consumers = c.consumers,
                isPlayerOwned = c.isPlayerOwned
            });
        }
        slices.Sort((a, b) => b.share.CompareTo(a.share));
        return slices;
    }

    public void SimulateDay(int day)
    {
        events.OnNewDay(day);
        RefreshShares();

        double demandMultiplier = events.DemandMultiplier;
        foreach (var c in companies)
        {
            double noise = 1.0 + (rng.NextDouble() * 2.0 - 1.0) * settings.dailyDemandNoise;
            long demand = (long)Math.Round(c.consumers * demandMultiplier * noise);
            long capacity = c.productionLevel * settings.capacityConsumersPerProductionLevel;
            long served = Math.Min(demand, capacity);
            double units = served * settings.purchasesPerConsumerPerDay * c.purchaseRateScale;

            c.history.Add(new DailyCompanyRecord
            {
                day = day,
                revenue = (long)Math.Round(units * c.productPrice),
                demandConsumers = demand,
                servedConsumers = served,
                capacityConsumers = capacity,
                share = c.share
            });

            int excess = c.history.Count - settings.historyDays;
            if (excess > 0) c.history.RemoveRange(0, excess);
        }
    }

    public int RequiredSubLevelForNextLevel(CompanyState c) => c.level + settings.subLevelRequirementOffset;

    public bool CanLevelUp(CompanyState c) =>
        c != null && c.level < settings.maxCompanyLevel && c.MinSubLevel >= RequiredSubLevelForNextLevel(c);

    public bool TryLevelUp(string companyId)
    {
        var c = Get(companyId);
        if (!CanLevelUp(c)) return false;

        c.level++;
        RefreshShares();
        return true;
    }

    public bool TryRaiseSubLevel(string companyId, SubLevelKind kind)
    {
        var c = Get(companyId);
        if (c == null) return false;

        switch (kind)
        {
            case SubLevelKind.Production:
                if (c.productionLevel >= settings.maxSubLevel) return false;
                c.productionLevel++;
                break;
            case SubLevelKind.Quality:
                if (c.qualityLevel >= settings.maxSubLevel) return false;
                c.qualityLevel++;
                break;
            default:
                if (c.marketingLevel >= settings.maxSubLevel) return false;
                c.marketingLevel++;
                break;
        }

        RefreshShares();
        return true;
    }

    public bool TryGetMetrics(string companyId, out CompanyMetrics metrics)
    {
        metrics = default;
        var c = Get(companyId);
        if (c == null) return false;

        metrics.levelDelta = c.level - c.baseLevel;

        int recentN = settings.metricsRecentDays;
        int baselineN = settings.metricsBaselineDays;
        var h = c.history;
        if (h.Count <= recentN) return true;

        int recentStart = h.Count - recentN;
        int baselineStart = Math.Max(0, recentStart - baselineN);
        int baselineCount = recentStart - baselineStart;

        double recentRevenue = 0, baseRevenue = 0, recentDemand = 0, baseDemand = 0, recentShare = 0, baseShare = 0;
        for (int i = recentStart; i < h.Count; i++)
        {
            recentRevenue += h[i].revenue;
            recentDemand += h[i].demandConsumers;
            recentShare += h[i].share;
        }
        for (int i = baselineStart; i < recentStart; i++)
        {
            baseRevenue += h[i].revenue;
            baseDemand += h[i].demandConsumers;
            baseShare += h[i].share;
        }

        metrics.revenueGrowth = Growth(recentRevenue / recentN, baseRevenue / baselineCount);
        metrics.demandTrend = Growth(recentDemand / recentN, baseDemand / baselineCount);
        metrics.shareDelta = recentShare / recentN - baseShare / baselineCount;
        return true;
    }

    public double GetHourlyShock(string sector) => events.GetHourlyShock(sector);

    public CompanyMarketState CaptureState()
    {
        return new CompanyMarketState
        {
            companies = new List<CompanyState>(companies),
            activeEvents = events.CaptureState()
        };
    }

    public void RestoreState(CompanyMarketState state)
    {
        if (state == null) return;

        companies.Clear();
        companies.AddRange(state.companies);
        events.RestoreState(state.activeEvents);
        RefreshShares();
    }

    private static double Growth(double recent, double baseline)
    {
        if (baseline <= 0.0) return 0.0;
        return Math.Max(-0.5, Math.Min(1.0, recent / baseline - 1.0));
    }
}
