using System;
using System.Collections.Generic;
using UnityEngine;

public class CompanyManager : GameSystem<CompanyManager>
{
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

    /// <summary>Penalty check for business registration. ReportManager installs it; Economy cannot reference Gameplay.</summary>
    public static Func<string, bool> RegistrationAllowed = _ => true;

    protected override void OnSystemAwake() => EnsureInitialized();

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

    protected override void OnDestroy()
    {
        if (subscribedClock != null) subscribedClock.DayChanged -= OnDayChanged;
        base.OnDestroy();
    }

    public List<ShareSlice> GetShareBreakdown() => Market.GetShareBreakdown();

    public bool TryCreatePlayerCompany(CompanyData data, string ownerId, out string failReason)
    {
        failReason = null;
        if (!RegistrationAllowed(ownerId))
        {
            failReason = "Too many penalty points to register a business.";
            return false;
        }

        if (SharedWriteGate.TrySend("company.add", new SharedOpArgs { a = JsonUtility.ToJson(data), b = ownerId })) return true;
        if (SharedWriteGate.SendFailed) { failReason = "Could not reach the server."; return false; }
        if (!Market.AddCompany(CompanyState.FromData(data, ownerId, true)))
        {
            failReason = "A company with this id already exists.";
            return false;
        }
        return true;
    }

    /// <summary>Limits numbers of a company that came over the network (JsonUtility ignores [Min]). Null when unusable.</summary>
    public static CompanyData Sanitize(CompanyData d)
    {
        if (d == null || string.IsNullOrWhiteSpace(d.companyId) || string.IsNullOrWhiteSpace(d.displayName)) return null;
        d.companyId = Cut(d.companyId, 40); d.displayName = Cut(d.displayName, 40); d.sector = Cut(d.sector ?? "", 24);
        d.level = Mathf.Clamp(d.level, 1, 10); d.productionLevel = Mathf.Clamp(d.productionLevel, 1, 10);
        d.qualityLevel = Mathf.Clamp(d.qualityLevel, 1, 10); d.marketingLevel = Mathf.Clamp(d.marketingLevel, 1, 10);
        d.productPrice = Math.Min(Math.Max(d.productPrice, 1L), 100000000L);
        d.referencePrice = Math.Min(Math.Max(d.referencePrice, 1L), 100000000L);
        d.purchaseRateScale = Finite(d.purchaseRateScale, 0f, 10f); d.baseAppeal = Finite(d.baseAppeal, 0f, 10f);
        return d;
    }

    static string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    static float Finite(float v, float min, float max) => float.IsNaN(v) || float.IsInfinity(v) ? min : Mathf.Clamp(v, min, max);

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
