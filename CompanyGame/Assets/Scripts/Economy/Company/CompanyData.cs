using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Design-time definition of a company. Runtime numbers live in CompanyState.</summary>
[Serializable]
public class CompanyData
{
    public string companyId;
    public string displayName;
    public string sector;
    [Min(1)] public int level = 5;
    [Min(1)] public int productionLevel = 5;
    [Min(1)] public int qualityLevel = 5;
    [Min(1)] public int marketingLevel = 5;
    [Min(1)] public long productPrice = 10000;
    [Min(1)] public long referencePrice = 10000;
    [Min(0f)] public float purchaseRateScale = 1f;
    [Min(0f)] public float baseAppeal = 1f;

    public static List<CompanyData> CreateDefaults()
    {
        return new List<CompanyData>
        {
            Make("company_sg_hynix", "sg하이닉스", "tech", 8, 8, 8, 7, 300000, 0.1f, 1.0f),
            Make("company_saseong", "사성전자", "tech", 9, 9, 8, 9, 500000, 0.1f, 1.2f),
            Make("company_kia", "기아자동차", "auto", 8, 8, 7, 7, 1000000, 0.02f, 1.0f),
            Make("company_krafton", "클래프톤", "game", 7, 6, 7, 8, 30000, 0.3f, 1.0f),
            Make("company_handae", "한대건설", "construction", 8, 8, 7, 6, 5000000, 0.005f, 1.0f),
            Make("company_hitejinro", "하이트진로", "beverage", 7, 7, 7, 7, 3000, 1.0f, 1.0f)
        };
    }

    private static CompanyData Make(string id, string name, string sector, int level,
        int production, int quality, int marketing, long price, float rateScale, float appeal)
    {
        return new CompanyData
        {
            companyId = id,
            displayName = name,
            sector = sector,
            level = level,
            productionLevel = production,
            qualityLevel = quality,
            marketingLevel = marketing,
            productPrice = price,
            referencePrice = price,
            purchaseRateScale = rateScale,
            baseAppeal = appeal
        };
    }
}
