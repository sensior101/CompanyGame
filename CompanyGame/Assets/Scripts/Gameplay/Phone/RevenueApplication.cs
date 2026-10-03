using System;
using System.Collections.Generic;
using UnityEngine;

public class RevenueApplication : PhoneAppBase
{
    [SerializeField]
    private CompanyManager companies;

    public event Action Refreshed;

    private void Awake()
    {
        if (companies == null) companies = FindAnyObjectByType<CompanyManager>();
    }

    private void Start()
    {
        if (companies != null) companies.DayProcessed += OnDayProcessed;
    }

    private void OnDestroy()
    {
        if (companies != null) companies.DayProcessed -= OnDayProcessed;
    }

    protected override void OnOpened() => Refreshed?.Invoke();

    public long TotalConsumers => companies != null ? companies.Market.TotalConsumers : 0L;

    public List<ShareSlice> GetShareBreakdown() =>
        companies != null ? companies.GetShareBreakdown() : new List<ShareSlice>();

    public CompanyState GetCompany(string companyId) =>
        companies != null ? companies.Market.Get(companyId) : null;

    public IReadOnlyList<DailyCompanyRecord> GetRevenueHistory(string companyId) =>
        GetCompany(companyId)?.history ?? new List<DailyCompanyRecord>();

    public IReadOnlyList<ActiveMarketEvent> GetActiveEvents() =>
        companies != null ? companies.Market.ActiveEvents : new List<ActiveMarketEvent>();

    private void OnDayProcessed()
    {
        if (IsOpen) Refreshed?.Invoke();
    }
}
