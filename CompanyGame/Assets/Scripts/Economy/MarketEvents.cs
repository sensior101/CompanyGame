using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

/// <summary>Hourly log-return applied to a sector while an event is active. Sector "*" hits every sector.</summary>
[Serializable]
public class SectorShock
{
    public string sector = "*";
    public float hourlyLogReturn;
}

[Serializable]
public class MarketEventDef
{
    public string id;
    public string displayName;
    public string description;
    [Range(0f, 1f)] public float dailyProbability = 0.01f;
    [Min(1)] public int durationDays = 5;
    [Range(0.1f, 2f)] public float demandMultiplier = 1f;
    public List<SectorShock> shocks = new List<SectorShock>();

    public static List<MarketEventDef> CreateDefaults()
    {
        return new List<MarketEventDef>
        {
            new MarketEventDef
            {
                id = "event_plague",
                displayName = "역병",
                description = "역병이 번져 소비가 위축되고 시장이 흔들리고 있습니다.",
                dailyProbability = 0.01f,
                durationDays = 5,
                demandMultiplier = 0.85f,
                shocks = new List<SectorShock> { new SectorShock { sector = "*", hourlyLogReturn = -0.002f } }
            },
            new MarketEventDef
            {
                id = "event_war",
                displayName = "전쟁",
                description = "전쟁 여파로 공급망이 불안정해졌습니다.",
                dailyProbability = 0.005f,
                durationDays = 10,
                demandMultiplier = 0.9f,
                shocks = new List<SectorShock>
                {
                    new SectorShock { sector = "*", hourlyLogReturn = -0.0015f },
                    new SectorShock { sector = "auto", hourlyLogReturn = -0.0025f },
                    new SectorShock { sector = "tech", hourlyLogReturn = -0.0025f }
                }
            },
            new MarketEventDef
            {
                id = "event_boom",
                displayName = "호황",
                description = "경기가 살아나며 소비가 늘고 있습니다.",
                dailyProbability = 0.01f,
                durationDays = 7,
                demandMultiplier = 1.1f,
                shocks = new List<SectorShock> { new SectorShock { sector = "*", hourlyLogReturn = 0.001f } }
            }
        };
    }
}

[Serializable]
public class ActiveMarketEvent
{
    public string eventId;
    public string displayName;
    public string description;
    public int startDay;
    public int endDay;
    public float demandMultiplier = 1f;
    public List<SectorShock> shocks = new List<SectorShock>();

    public int DaysLeft(int today) => Math.Max(0, endDay - today);
}

/// <summary>Rolls random economic events once a day and reports their combined effect.</summary>
public class MarketEventScheduler
{
    private readonly List<MarketEventDef> defs;
    private readonly Random rng;
    private readonly List<ActiveMarketEvent> active = new List<ActiveMarketEvent>();
    private readonly List<ActiveMarketEvent> startedToday = new List<ActiveMarketEvent>();

    public MarketEventScheduler(IEnumerable<MarketEventDef> defs, Random rng)
    {
        this.defs = new List<MarketEventDef>(defs ?? new List<MarketEventDef>());
        this.rng = rng ?? new Random();
    }

    public IReadOnlyList<ActiveMarketEvent> Active => active;

    public IReadOnlyList<ActiveMarketEvent> StartedToday => startedToday;

    public double DemandMultiplier
    {
        get
        {
            double result = 1.0;
            foreach (var e in active) result *= e.demandMultiplier;
            return result;
        }
    }

    public void OnNewDay(int day)
    {
        startedToday.Clear();
        active.RemoveAll(e => e.endDay <= day);

        foreach (var def in defs)
        {
            if (IsActive(def.id)) continue;
            if (rng.NextDouble() < def.dailyProbability) Start(def, day);
        }
    }

    public bool ForceStart(string eventId, int day)
    {
        var def = defs.Find(d => d.id == eventId);
        if (def == null || IsActive(eventId)) return false;

        Start(def, day);
        return true;
    }

    public double GetHourlyShock(string sector)
    {
        double total = 0.0;
        foreach (var e in active)
        {
            foreach (var shock in e.shocks)
            {
                if (shock.sector == "*" || shock.sector == sector) total += shock.hourlyLogReturn;
            }
        }
        return total;
    }

    public List<ActiveMarketEvent> CaptureState() => new List<ActiveMarketEvent>(active);

    public void RestoreState(IEnumerable<ActiveMarketEvent> events)
    {
        active.Clear();
        startedToday.Clear();
        if (events != null) active.AddRange(events);
    }

    private bool IsActive(string eventId) => active.Exists(e => e.eventId == eventId);

    private void Start(MarketEventDef def, int day)
    {
        var started = new ActiveMarketEvent
        {
            eventId = def.id,
            displayName = def.displayName,
            description = def.description,
            startDay = day,
            endDay = day + def.durationDays,
            demandMultiplier = def.demandMultiplier,
            shocks = def.shocks.ConvertAll(s => new SectorShock { sector = s.sector, hourlyLogReturn = s.hourlyLogReturn })
        };
        active.Add(started);
        startedToday.Add(started);
    }
}
