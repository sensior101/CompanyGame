using System;
using System.Collections.Generic;
using UnityEngine;

public class CompanyManager : MonoBehaviour
{
    public static CompanyManager Instance { get; private set; }

    [SerializeField]
    private CompanyMarketSettings settings = new CompanyMarketSettings();

    [SerializeField, Tooltip("0 = different every run")]
    private int randomSeed;

    private CompanyMarket market;
    private IGameClock subscribedClock;

    public event Action DayProcessed;

    public CompanyMarket Market
    {
        get
        {
            EnsureInitialized();
            return market;
        }
    }

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

    private void Start()
    {
        subscribedClock = GameClock.Current;
        if (subscribedClock == null)
        {
            Debug.LogWarning("CompanyManager: no game clock found, daily sales will not advance.");
            return;
        }
        subscribedClock.DayChanged += OnDayChanged;
    }

    private void OnDestroy()
    {
        if (subscribedClock != null) subscribedClock.DayChanged -= OnDayChanged;
        if (Instance == this) Instance = null;
    }

    public List<ShareSlice> GetShareBreakdown() => Market.GetShareBreakdown();

    public bool TryCreatePlayerCompany(CompanyData data, string ownerId, out string failReason)
    {
        failReason = null;
        var reports = ReportManager.Instance;
        if (reports != null && !reports.CanRegisterBusiness(ownerId))
        {
            failReason = "Too many penalty points to register a business.";
            return false;
        }

        if (!Market.AddCompany(CompanyState.FromData(data, ownerId, true)))
        {
            failReason = "A company with this id already exists.";
            return false;
        }
        return true;
    }

    private void EnsureInitialized()
    {
        if (market != null) return;

        var rng = randomSeed != 0 ? new System.Random(randomSeed) : new System.Random();
        market = new CompanyMarket(settings, rng);
    }

    private void OnDayChanged(GameTime time)
    {
        Market.SimulateDay(time.absoluteDay);
        DayProcessed?.Invoke();
    }
}
