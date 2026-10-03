using System;
using System.Collections.Generic;

/// <summary>Decides a report's outcome. Callback style so an asynchronous (AI/server) judge can replace the default.</summary>
public interface IReportJudge
{
    void Judge(CrimeReport report, CrimeRule rule, Action<ReportVerdict> onResult);
}

public static class ReportVerdicts
{
    public static ReportVerdict FromRule(CrimeRule rule, CrimeReport report, bool guilty, string reason = null)
    {
        if (!guilty)
        {
            return new ReportVerdict { guilty = false, reason = reason ?? "Not enough evidence." };
        }

        return new ReportVerdict
        {
            guilty = true,
            penaltyPoints = rule.penaltyPoints,
            fine = rule.fixedFine + (long)Math.Round(report.stolenValue * (double)rule.fineMultiplierOfStolenValue),
            jailHours = rule.jailHours,
            settlementAllowed = rule.allowSettlement,
            creditDelta = -rule.creditPenalty,
            reason = reason
        };
    }
}

/// <summary>Placeholder for the README 9 "AI judges the screenshot" step: convicts whenever evidence is attached.</summary>
public class EvidenceRequiredJudge : IReportJudge
{
    public void Judge(CrimeReport report, CrimeRule rule, Action<ReportVerdict> onResult)
    {
        bool hasEvidence = !string.IsNullOrEmpty(report.evidenceId);
        onResult(ReportVerdicts.FromRule(rule, report, hasEvidence));
    }
}

[Serializable]
public class PenaltyEntry
{
    public string playerId;
    public int points;
}

[Serializable]
public class PenaltyLedger
{
    public List<PenaltyEntry> entries = new List<PenaltyEntry>();

    public int GetPoints(string playerId) => entries.Find(e => e.playerId == playerId)?.points ?? 0;

    public void Add(string playerId, int points)
    {
        if (points <= 0) return;

        var entry = entries.Find(e => e.playerId == playerId);
        if (entry == null)
        {
            entry = new PenaltyEntry { playerId = playerId };
            entries.Add(entry);
        }
        entry.points += points;
    }

    public void Remove(string playerId, int points)
    {
        var entry = entries.Find(e => e.playerId == playerId);
        if (entry == null || points <= 0) return;

        entry.points = Math.Max(0, entry.points - points);
    }
}

[Serializable]
public class ReportServiceState
{
    public List<CrimeReport> reports = new List<CrimeReport>();
    public PenaltyLedger ledger = new PenaltyLedger();
    public int nextId = 1;
}

/// <summary>Accepts crime reports, has them judged, and tracks penalty points. Fines and jail are left to listeners.</summary>
public class ReportService
{
    private readonly ReportSettings settings;
    private readonly Func<GameTime> clock;
    private readonly List<CrimeReport> reports = new List<CrimeReport>();
    private readonly Dictionary<string, long> lastSubmitMinutes = new Dictionary<string, long>();
    private IReportJudge judge;
    private int nextId = 1;

    public event Action<CrimeReport> ReportResolved;

    public ReportService(ReportSettings settings, IReportJudge judge, Func<GameTime> clock)
    {
        this.settings = settings;
        this.judge = judge ?? new EvidenceRequiredJudge();
        this.clock = clock;
    }

    public PenaltyLedger Ledger { get; private set; } = new PenaltyLedger();

    public IReadOnlyList<CrimeReport> Reports => reports;

    public void SetJudge(IReportJudge newJudge)
    {
        judge = newJudge ?? new EvidenceRequiredJudge();
    }

    public bool CanRegisterBusiness(string playerId) =>
        Ledger.GetPoints(playerId) < settings.businessBlockPenaltyPoints;

    public bool ShouldSeizeAssets(string playerId) =>
        Ledger.GetPoints(playerId) >= settings.assetSeizurePenaltyPoints;

    public ReportSubmitResult Submit(string reporterId, string suspectId, CrimeType type,
        string evidenceId, long stolenValue, out CrimeReport report)
    {
        report = null;
        if (string.IsNullOrEmpty(suspectId) || suspectId == reporterId) return ReportSubmitResult.InvalidSuspect;
        if (string.IsNullOrEmpty(evidenceId)) return ReportSubmitResult.MissingEvidence;

        var rule = settings.rules.Find(r => r.type == type);
        if (rule == null) return ReportSubmitResult.UnknownCrimeType;

        GameTime now = clock();
        if (lastSubmitMinutes.TryGetValue(reporterId, out long last)
            && now.TotalMinutes - last < settings.reportCooldownGameMinutes)
        {
            return ReportSubmitResult.OnCooldown;
        }

        report = new CrimeReport
        {
            id = "report_" + nextId++,
            reporterId = reporterId,
            suspectId = suspectId,
            type = type,
            evidenceId = evidenceId,
            stolenValue = Math.Max(0L, stolenValue),
            submittedAt = now,
            status = ReportStatus.Pending
        };
        reports.Add(report);
        lastSubmitMinutes[reporterId] = now.TotalMinutes;
        TrimStoredReports();

        var submitted = report;
        judge.Judge(submitted, rule, verdict => Resolve(submitted, verdict));
        return ReportSubmitResult.Submitted;
    }

    public ReportServiceState CaptureState()
    {
        return new ReportServiceState
        {
            reports = new List<CrimeReport>(reports),
            ledger = Ledger,
            nextId = nextId
        };
    }

    public void RestoreState(ReportServiceState state)
    {
        if (state == null) return;

        reports.Clear();
        reports.AddRange(state.reports);
        Ledger = state.ledger ?? new PenaltyLedger();
        nextId = Math.Max(1, state.nextId);
    }

    private void Resolve(CrimeReport report, ReportVerdict verdict)
    {
        if (report.status != ReportStatus.Pending || verdict == null) return;

        report.verdict = verdict;
        report.resolvedAt = clock();
        report.status = verdict.guilty ? ReportStatus.Convicted : ReportStatus.Dismissed;
        if (verdict.guilty) Ledger.Add(report.suspectId, verdict.penaltyPoints);

        ReportResolved?.Invoke(report);
    }

    private void TrimStoredReports()
    {
        int excess = reports.Count - settings.maxStoredReports;
        for (int i = 0; i < reports.Count && excess > 0;)
        {
            if (reports[i].status == ReportStatus.Pending)
            {
                i++;
                continue;
            }
            reports.RemoveAt(i);
            excess--;
        }
    }
}
