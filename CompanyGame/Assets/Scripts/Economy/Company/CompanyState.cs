using System;
using System.Collections.Generic;

public enum SubLevelKind
{
    Production,
    Quality,
    Marketing
}

[Serializable]
public class DailyCompanyRecord
{
    public int day;
    public long revenue;
    public long demandConsumers;
    public long servedConsumers;
    public long capacityConsumers;
    public float share;
}

/// <summary>Runtime state of one company. Plain serializable data so it can go straight into a save file.</summary>
[Serializable]
public class CompanyState
{
    public string companyId;
    public string displayName;
    public string sector;
    public string ownerId;
    public bool isPlayerOwned;
    public bool listed;
    public int level;
    public int baseLevel;
    public int productionLevel;
    public int qualityLevel;
    public int marketingLevel;
    public long productPrice;
    public long referencePrice;
    public float purchaseRateScale;
    public float baseAppeal;
    public long consumers;
    public float share;
    public List<DailyCompanyRecord> history = new List<DailyCompanyRecord>();

    public int MinSubLevel => Math.Min(productionLevel, Math.Min(qualityLevel, marketingLevel));

    public static CompanyState FromData(CompanyData data, string ownerId = null, bool isPlayerOwned = false)
    {
        return new CompanyState
        {
            companyId = data.companyId,
            displayName = data.displayName,
            sector = data.sector,
            ownerId = ownerId,
            isPlayerOwned = isPlayerOwned,
            level = data.level,
            baseLevel = data.level,
            productionLevel = data.productionLevel,
            qualityLevel = data.qualityLevel,
            marketingLevel = data.marketingLevel,
            productPrice = data.productPrice,
            referencePrice = Math.Max(1L, data.referencePrice),
            purchaseRateScale = data.purchaseRateScale,
            baseAppeal = data.baseAppeal
        };
    }
}
