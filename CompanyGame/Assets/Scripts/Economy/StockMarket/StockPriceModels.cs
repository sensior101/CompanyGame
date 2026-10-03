using System;

public struct StockPriceContext
{
    public double currentPrice;
    public double basePrice;
    public double hourlyVolatility;
    public double eventShock;
    public double fundamentalMultiplier;
}

/// <summary>Turns the current price into next hour's price. Swap implementations to change market behaviour.</summary>
public interface IStockPriceModel
{
    double NextPrice(StockPriceContext ctx, Random rng);
}

public static class PriceMath
{
    public static double NextGaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

public class RandomWalkPriceModel : IStockPriceModel
{
    private readonly double hourlyDrift;

    public RandomWalkPriceModel(double hourlyDrift)
    {
        this.hourlyDrift = hourlyDrift;
    }

    public double NextPrice(StockPriceContext ctx, Random rng)
    {
        double logReturn = hourlyDrift + ctx.hourlyVolatility * PriceMath.NextGaussian(rng) + ctx.eventShock;
        return ctx.currentPrice * Math.Exp(logReturn);
    }
}

/// <summary>Price is pulled toward a fair value built from company performance, plus noise and event shocks.</summary>
public class FundamentalPriceModel : IStockPriceModel
{
    private readonly double reversionPerHour;

    public FundamentalPriceModel(double reversionPerHour)
    {
        this.reversionPerHour = reversionPerHour;
    }

    public double NextPrice(StockPriceContext ctx, Random rng)
    {
        double fairValue = ctx.basePrice * ctx.fundamentalMultiplier;
        double pull = reversionPerHour * Math.Log(fairValue / ctx.currentPrice);
        double logReturn = pull + ctx.hourlyVolatility * PriceMath.NextGaussian(rng) + ctx.eventShock;
        return ctx.currentPrice * Math.Exp(logReturn);
    }
}

public static class FundamentalCalculator
{
    public static double Multiplier(CompanyMetrics m, StockMarketSettings s)
    {
        double x = 1.0
            + s.revenueGrowthWeight * m.revenueGrowth
            + s.shareDeltaWeight * m.shareDelta
            + s.levelWeight * m.levelDelta
            + s.demandTrendWeight * m.demandTrend;
        return Math.Max(s.minFundamentalMultiplier, Math.Min(s.maxFundamentalMultiplier, x));
    }
}

public static class PriceModelFactory
{
    public static IStockPriceModel Create(StockMarketSettings s)
    {
        switch (s.priceModel)
        {
            case PriceModelType.RandomWalk:
                return new RandomWalkPriceModel(s.randomWalkHourlyDrift);
            default:
                return new FundamentalPriceModel(s.meanReversionPerHour);
        }
    }
}
