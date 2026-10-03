using System;
using System.Collections.Generic;
using UnityEngine;

public class ReportApplication : PhoneAppBase
{
    [SerializeField]
    private ReportManager reports;

    public event Action Refreshed;

    private void Awake()
    {
        if (reports == null) reports = FindAnyObjectByType<ReportManager>();
    }

    private void Start()
    {
        if (reports != null) reports.ReportResolved += OnReportResolved;
    }

    private void OnDestroy()
    {
        if (reports != null) reports.ReportResolved -= OnReportResolved;
    }

    protected override void OnOpened() => Refreshed?.Invoke();

    public ReportSubmitResult Submit(CrimeType type, string suspectId, string evidenceId,
        long stolenValue, out CrimeReport report)
    {
        report = null;
        return reports != null
            ? reports.Submit(type, suspectId, evidenceId, stolenValue, out report)
            : ReportSubmitResult.UnknownCrimeType;
    }

    public List<CrimeReport> GetMyReports()
    {
        var mine = new List<CrimeReport>();
        if (reports == null) return mine;

        foreach (var report in reports.Service.Reports)
        {
            if (report.reporterId == reports.LocalPlayerId) mine.Add(report);
        }
        return mine;
    }

    public int GetMyPenaltyPoints() => reports != null ? reports.GetPenaltyPoints(reports.LocalPlayerId) : 0;

    private void OnReportResolved(CrimeReport report)
    {
        if (IsOpen) Refreshed?.Invoke();
    }
}
