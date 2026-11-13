using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Writes the real-estate agency's listings (Resources/RealEstate/Listings.json, read by RealEstateCatalog) from the
/// MapParcels of the maps. Which parcels are for sale, what they are called and what they cost is the table below.
/// Run: -executeMethod RealEstateListingBaker.Bake (also run by RealEstateInteriorBuilder.Build).
/// </summary>
public static class RealEstateListingBaker
{
    public const string OutputPath = "Assets/Resources/RealEstate/Listings.json";

    static readonly string[] Maps =
    {
        "Assets/Scenes/daldongnaemap.unity",
        "Assets/Scenes/Maps/BusinessDistrict.unity",
        "Assets/Scenes/Maps/CoastalResidentialDistrict.unity",
        "Assets/Scenes/Maps/LuxuryResidentialDistrict.unity",
        "Assets/Scenes/Maps/NightlifeDistrict.unity",
    };

    // Parcel name prefix (after "Map:") -> what the agency sells it as. Fixed prices (phase 1); civic, company, tower and
    // NPC shop parcels are not for sale.
    static readonly (string prefix, RealEstateKind kind, string name, long price)[] Rules =
    {
        ("VacantPlot_", RealEstateKind.Land, "빈 부지", 30000000),
        ("RetailParcel_", RealEstateKind.Commercial, "대형 상가", 40000000),
        ("NightlifeDistrict_Shop_", RealEstateKind.Commercial, "상가", 10000000),
        ("CoastalResidentialDistrict_SeasideShops_", RealEstateKind.Commercial, "해변 상가", 8000000),
        ("Village_OneRoom_", RealEstateKind.House, "원룸", 3000000),
        ("Home_", RealEstateKind.House, "단독주택", 8000000),
        ("CoastalResidentialDistrict_BeachHouse_", RealEstateKind.House, "비치하우스", 20000000),
        ("LuxuryResidentialDistrict_LuxuryVilla_", RealEstateKind.House, "고급 빌라", 50000000),
    };

    [Serializable] class ListingFile { public RealEstateListing[] listings; }

    [MenuItem("CompanyGame/Setup/Bake Real Estate Listings")]
    public static void Bake()
    {
        var listings = new List<RealEstateListing>();
        var opened = new List<UnityEngine.SceneManagement.Scene>();
        try
        {
            foreach (string path in Maps)
            {
                var scene = EditorSceneManager.GetSceneByPath(path);
                if (!scene.isLoaded) { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened.Add(scene); }
                foreach (var parcel in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MapParcel>(true)))
                {
                    string id = string.IsNullOrEmpty(parcel.landPlotId) ? parcel.parcelId : parcel.landPlotId;
                    string local = id.Substring(id.IndexOf(':') + 1);
                    var rule = Rules.FirstOrDefault(r => local.StartsWith(r.prefix, StringComparison.Ordinal));
                    if (rule.prefix == null) continue;
                    listings.Add(new RealEstateListing
                    {
                        id = id, kind = rule.kind, name = rule.name, district = parcel.district, number = parcel.number,
                        width = parcel.size.x, depth = parcel.size.y, price = rule.price
                    });
                }
            }
        }
        finally { foreach (var scene in opened) EditorSceneManager.CloseScene(scene, true); }
        if (listings.Select(l => l.id).Distinct().Count() != listings.Count) throw new InvalidOperationException("Duplicate parcel ids in listings.");
        listings = listings.OrderBy(l => l.kind).ThenBy(l => l.number).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        File.WriteAllText(OutputPath, JsonUtility.ToJson(new ListingFile { listings = listings.ToArray() }, true));
        AssetDatabase.ImportAsset(OutputPath);
        RealEstateCatalog.Reload();
        Debug.Log("[RealEstateListings] " + listings.Count + " listings: " + string.Join(", ",
            listings.GroupBy(l => l.kind).Select(g => RealEstateCatalog.KindName(g.Key) + " " + g.Count())));
    }
}
