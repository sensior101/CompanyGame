using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ReportScreenView : MonoBehaviour
{
    [SerializeField] private ReportApplication app;
    [SerializeField] private Button typeButton;
    [SerializeField] private TMP_Text typeLabel;
    [SerializeField] private TMP_InputField suspectInput;
    [SerializeField] private TMP_InputField evidenceInput;
    [SerializeField] private TMP_InputField valueInput;
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text pointsText;
    [SerializeField] private TMP_Text historyText;

    private static readonly CrimeType[] Types = (CrimeType[])Enum.GetValues(typeof(CrimeType));
    private int typeIndex;

    private void Awake()
    {
        if (app == null) app = GetComponent<ReportApplication>();
        app.Refreshed += Refresh;
        typeButton.onClick.AddListener(() =>
        {
            typeIndex = (typeIndex + 1) % Types.Length;
            UpdateTypeLabel();
        });
        submitButton.onClick.AddListener(OnSubmit);
        UpdateTypeLabel();
    }

    private void OnDestroy()
    {
        if (app != null) app.Refreshed -= Refresh;
    }

    private static string TypeName(CrimeType type)
    {
        switch (type)
        {
            case CrimeType.Theft: return "절도";
            case CrimeType.Trespass: return "주거침입";
            case CrimeType.Assault: return "폭행";
            default: return "살인";
        }
    }

    private static string StatusName(ReportStatus status)
    {
        switch (status)
        {
            case ReportStatus.Convicted: return "유죄";
            case ReportStatus.Dismissed: return "기각";
            default: return "판독 중";
        }
    }

    private void UpdateTypeLabel() => typeLabel.text = "범죄 유형:  " + TypeName(Types[typeIndex]) + "  (눌러서 변경)";

    private void Refresh()
    {
        pointsText.text = "내 벌점   " + app.GetMyPenaltyPoints() + "점";

        var mine = app.GetMyReports();
        if (mine.Count == 0)
        {
            historyText.text = "신고 내역이 없습니다.";
            return;
        }

        var sb = new StringBuilder();
        for (int i = mine.Count - 1, shown = 0; i >= 0 && shown < 5; i--, shown++)
        {
            var report = mine[i];
            sb.AppendLine($"· {TypeName(report.type)}  →  {report.suspectId}   [{StatusName(report.status)}]");
        }
        historyText.text = sb.ToString().TrimEnd();
    }

    private void OnSubmit()
    {
        long stolenValue = 0;
        if (!string.IsNullOrWhiteSpace(valueInput.text)) PhoneFormat.TryParseAmount(valueInput.text, out stolenValue);

        var result = app.Submit(Types[typeIndex], suspectInput.text.Trim(), evidenceInput.text.Trim(),
            stolenValue, out CrimeReport report);

        switch (result)
        {
            case ReportSubmitResult.Submitted:
                messageText.text = Describe(report);
                suspectInput.text = string.Empty;
                evidenceInput.text = string.Empty;
                valueInput.text = string.Empty;
                break;
            case ReportSubmitResult.InvalidSuspect:
                messageText.text = "신고 대상 ID를 확인하세요.";
                break;
            case ReportSubmitResult.MissingEvidence:
                messageText.text = "증거(스크린샷) ID가 필요합니다.";
                break;
            case ReportSubmitResult.OnCooldown:
                messageText.text = "잠시 후 다시 신고할 수 있습니다.";
                break;
            default:
                messageText.text = "신고를 처리할 수 없습니다.";
                break;
        }
        Refresh();
    }

    private static string Describe(CrimeReport report)
    {
        if (report.status == ReportStatus.Pending) return "신고가 접수되어 판독 중입니다.";

        var verdict = report.verdict;
        if (!verdict.guilty) return "신고가 기각되었습니다. " + verdict.reason;

        var sb = new StringBuilder($"유죄 판결. 벌점 +{verdict.penaltyPoints}");
        if (verdict.fine > 0) sb.Append($", 벌금 {PhoneFormat.Won(verdict.fine)}");
        if (verdict.jailHours > 0) sb.Append($", 감옥 {verdict.jailHours}시간");
        if (verdict.settlementAllowed) sb.Append(" (합의 가능)");
        return sb.ToString();
    }
}
