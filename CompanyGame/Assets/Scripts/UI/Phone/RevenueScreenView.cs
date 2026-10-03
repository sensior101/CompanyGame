using System.Text;
using TMPro;
using UnityEngine;

public class RevenueScreenView : MonoBehaviour
{
    [SerializeField] private RevenueApplication app;
    [SerializeField] private TMP_Text totalText;
    [SerializeField] private TMP_Text listText;
    [SerializeField] private TMP_Text eventsText;

    private void Awake()
    {
        if (app == null) app = GetComponent<RevenueApplication>();
        app.Refreshed += Refresh;
    }

    private void OnDestroy()
    {
        if (app != null) app.Refreshed -= Refresh;
    }

    private void Refresh()
    {
        totalText.text = "전체 소비자   " + app.TotalConsumers.ToString("N0") + "명";

        var sb = new StringBuilder();
        int rank = 1;
        foreach (var slice in app.GetShareBreakdown())
        {
            var company = app.GetCompany(slice.companyId);
            long revenue = company != null && company.history.Count > 0
                ? company.history[company.history.Count - 1].revenue
                : 0L;

            sb.AppendLine($"{rank++}. {slice.displayName}{(slice.isPlayerOwned ? " (내 회사)" : string.Empty)}");
            sb.AppendLine($"    점유율 {PhoneFormat.Percent(slice.share)}   소비자 {slice.consumers:N0}명");
            sb.AppendLine($"    일 매출 {PhoneFormat.Won(revenue)}");
        }
        listText.text = sb.ToString().TrimEnd();

        var events = app.GetActiveEvents();
        if (events.Count == 0)
        {
            eventsText.text = "진행 중인 경제 이벤트 없음";
            return;
        }

        var eb = new StringBuilder();
        int today = PhoneFormat.Today();
        foreach (var e in events) eb.AppendLine($"· {e.displayName} (남은 기간 {e.DaysLeft(today)}일)");
        eventsText.text = eb.ToString().TrimEnd();
    }
}
