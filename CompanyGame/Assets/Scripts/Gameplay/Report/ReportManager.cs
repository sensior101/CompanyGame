using System;
using UnityEngine;

public class ReportManager : MonoBehaviour
{
    public static ReportManager Instance { get; private set; }

    [SerializeField]
    private ReportSettings settings = new ReportSettings();

    private ReportService service;

    public event Action<CrimeReport> ReportResolved;

    public ReportService Service
    {
        get
        {
            EnsureInitialized();
            return service;
        }
    }

    public string LocalPlayerId => settings.localPlayerId;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        EnsureInitialized();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public ReportSubmitResult Submit(CrimeType type, string suspectId, string evidenceId,
        long stolenValue, out CrimeReport report) =>
        Service.Submit(settings.localPlayerId, suspectId, type, evidenceId, stolenValue, out report);

    public bool CanRegisterBusiness(string playerId) => Service.CanRegisterBusiness(playerId);

    public bool ShouldSeizeAssets(string playerId) => Service.ShouldSeizeAssets(playerId);

    public int GetPenaltyPoints(string playerId) => Service.Ledger.GetPoints(playerId);

    public void SetJudge(IReportJudge judge) => Service.SetJudge(judge);

    private void EnsureInitialized()
    {
        if (service != null) return;

        service = new ReportService(settings, new EvidenceRequiredJudge(),
            () => GameClock.Current != null ? GameClock.Current.Now : default);
        service.ReportResolved += report => ReportResolved?.Invoke(report);
    }
}
