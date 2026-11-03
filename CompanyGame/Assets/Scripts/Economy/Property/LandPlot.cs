using UnityEngine;

/// <summary>
/// 씬에 배치하는 부지 하나. 고정 id와 가격만 들고 있고, 구매·등록 상태는 LandPlotManager가 id로 관리한다.
/// </summary>
public class LandPlot : MonoBehaviour
{
    /// <summary>Explicit parcel sizing; this does not change ownership, price or saved plot IDs.</summary>
    public enum SizeTier
    {
        Small = 0,
        Medium = 1,
        ApartmentCommercial = 2,
        Civic = 3,
        Business = 4
    }

    [Tooltip("저장과 서버 통신에 쓰는 고유 ID. 한번 정하면 바꾸지 않는다. 예: commercial_01")]
    [SerializeField] private string plotId;

    [Tooltip("구매 가격(원). 기획안에 가격이 없어 임시값이며 부지마다 조정한다.")]
    [SerializeField, Min(1)] private long price = DefaultPrice;

    public string Id => plotId;
    public long Price => price;

    /// <summary>The price field's default: what a plot not in the loaded scenes costs.</summary>
    public const long DefaultPrice = 1000000;

    /// <summary>Legacy single-size editor API. New layouts must select a SizeTier explicitly.</summary>
    public const float StandardParcelSide = 32f;
    /// <summary>Legacy 24m frontage retained for existing callers; use TryGetBuildingSide for tier limits.</summary>
    public const float StandardBuildingSide = 24f;
    /// <summary>Minimum inset for a centred building within the standard parcel.</summary>
    public const float StandardBuildingInset = (StandardParcelSide - StandardBuildingSide) * .5f;
    public static Vector2 StandardParcelSize => new Vector2(StandardParcelSide, StandardParcelSide);
    /// <summary>Maximum X/Z envelope, not a requirement to stretch rectangular buildings into squares.</summary>
    public static Vector2 StandardBuildingFootprint => new Vector2(StandardBuildingSide, StandardBuildingSide);

    /// <summary>Square parcel side in metres. The existing business district retains its 88m parcels.</summary>
    public static float GetParcelSide(SizeTier tier)
    {
        switch (tier)
        {
            case SizeTier.Small: return 16f;
            case SizeTier.Medium: return 24f;
            case SizeTier.ApartmentCommercial: return 32f;
            case SizeTier.Civic: return 48f;
            case SizeTier.Business: return 88f;
            default: throw new System.ArgumentOutOfRangeException(nameof(tier), tier, "Unknown parcel size tier.");
        }
    }

    public static Vector2 GetParcelSize(SizeTier tier) => Vector2.one * GetParcelSide(tier);

    /// <summary>
    /// Maximum building width/depth, not a target to enlarge smaller buildings to.
    /// Business returns false: its existing building dimensions must be preserved.
    /// </summary>
    public static bool TryGetBuildingSide(SizeTier tier, out float maximumSide)
    {
        switch (tier)
        {
            case SizeTier.Small: maximumSide = 12f; return true;
            case SizeTier.Medium: maximumSide = 18f; return true;
            case SizeTier.ApartmentCommercial: maximumSide = 26f; return true;
            case SizeTier.Civic: maximumSide = 38f; return true;
            case SizeTier.Business: maximumSide = 0f; return false;
            default: throw new System.ArgumentOutOfRangeException(nameof(tier), tier, "Unknown parcel size tier.");
        }
    }

    public static bool TryGetBuildingFootprint(SizeTier tier, out Vector2 maximumFootprint)
    {
        bool hasLimit = TryGetBuildingSide(tier, out float side);
        maximumFootprint = Vector2.one * side;
        return hasLimit;
    }

    /// <summary>Per-side margin for a centred maximum-sized building; undefined for unchanged business buildings.</summary>
    public static bool TryGetMinimumInset(SizeTier tier, out float inset)
    {
        bool hasLimit = TryGetBuildingSide(tier, out float buildingSide);
        inset = hasLimit ? (GetParcelSide(tier) - buildingSide) * .5f : 0f;
        return hasLimit;
    }

    /// <summary>
    /// The price of plot <paramref name="id"/> for the server's ledger (12.10.1): the plot in the loaded scenes, else
    /// <see cref="DefaultPrice"/> (a server process loads one scene; no map places plots yet).
    /// </summary>
    public static long PriceOf(string id)
    {
        foreach (var plot in FindObjectsByType<LandPlot>(FindObjectsSortMode.None))
            if (plot.plotId == id) return plot.price;
        return DefaultPrice;
    }

    public LandPlotState State =>
        LandPlotManager.Instance != null ? LandPlotManager.Instance.GetState(plotId) : LandPlotState.Available;
}
