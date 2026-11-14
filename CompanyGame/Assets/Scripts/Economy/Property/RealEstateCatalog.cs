using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a real-estate desk deals in. Every kind is a map parcel owned through <see cref="LandPlotManager"/>.</summary>
public enum RealEstateKind { Land = 0, Commercial = 1, House = 2 }

/// <summary>One parcel the agency sells. Baked from the maps' MapParcels by RealEstateListingBaker.</summary>
[Serializable]
public class RealEstateListing
{
    public string id;          // the parcel's landPlotId, the key LandPlotManager owns it under
    public RealEstateKind kind;
    public string name;        // e.g. 원룸, 상가, 빈 부지
    public string district;    // e.g. 달동네
    public int number;         // 번지
    public float width, depth; // metres
    public long price;         // what the agency sells it for

    public string Address => district + " " + number + "번지";
    public string Size => width.ToString("0") + "×" + depth.ToString("0") + "m";
}

/// <summary>
/// The agency's fixed price list (Resources/RealEstate/Listings.json). A server process loads one scene only, so prices
/// come from here rather than from the scenes (<see cref="LandPlot.PriceOf"/>).
/// </summary>
public static class RealEstateCatalog
{
    public const string ResourcePath = "RealEstate/Listings";
    /// <summary>The agency buys back at this share of its own price.</summary>
    public const int BuyBackPercent = 80;

    [Serializable] class ListingFile { public RealEstateListing[] listings; }

    static RealEstateListing[] all;
    static Dictionary<string, RealEstateListing> byId;

    public static IReadOnlyList<RealEstateListing> All { get { Load(); return all; } }

    public static RealEstateListing Find(string id)
    {
        Load();
        return id != null && byId.TryGetValue(id, out var listing) ? listing : null;
    }

    /// <summary>What the agency pays for a parcel that costs <paramref name="price"/>.</summary>
    public static long BuyBackPrice(long price) => price <= 0 ? 0 : price / 100 * BuyBackPercent + price % 100 * BuyBackPercent / 100;

    public static string KindName(RealEstateKind kind) => kind == RealEstateKind.Land ? "부지" : kind == RealEstateKind.Commercial ? "상가" : "주택";

    /// <summary>"300만원", "3,000만원", or "1,234원" when it is not a whole 만.</summary>
    public static string Won(long amount) =>
        amount != 0 && amount % 10000 == 0 ? (amount / 10000).ToString("N0") + "만원" : amount.ToString("N0") + "원";

    public static void Reload() { all = null; byId = null; }

    static void Load()
    {
        if (all != null) return;
        var text = Resources.Load<TextAsset>(ResourcePath);
        all = (text ? JsonUtility.FromJson<ListingFile>(text.text)?.listings : null) ?? Array.Empty<RealEstateListing>();
        byId = new Dictionary<string, RealEstateListing>();
        foreach (var listing in all)
            if (listing != null && !string.IsNullOrEmpty(listing.id)) byId[listing.id] = listing;
    }
}
