using UnityEngine;

/// <summary>
/// 씬에 배치하는 부지 하나. 고정 id와 가격만 들고 있고, 구매·등록 상태는 LandPlotManager가 id로 관리한다.
/// </summary>
public class LandPlot : MonoBehaviour
{
    [Tooltip("저장과 서버 통신에 쓰는 고유 ID. 한번 정하면 바꾸지 않는다. 예: commercial_01")]
    [SerializeField] private string plotId;

    [Tooltip("구매 가격(원). 기획안에 가격이 없어 임시값이며 부지마다 조정한다.")]
    [SerializeField, Min(1)] private long price = DefaultPrice;

    public string Id => plotId;
    public long Price => price;

    /// <summary>The price field's default: what a plot not in the loaded scenes costs.</summary>
    public const long DefaultPrice = 1000000;

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
