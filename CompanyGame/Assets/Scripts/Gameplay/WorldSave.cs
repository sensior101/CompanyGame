using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Shared world state computed where GameSession.IsAuthority (offline, or a server): clock, stock prices, companies,
/// property, reports and SNS. Personal data (bank balance, portfolio, wallet cash) is NOT here, see PersonalSave.
/// </summary>
[Serializable]
public sealed class WorldSaveData
{
    public int version = 1;
    public long clockMinutes;
    public string worldId;   // GUID created with the world
    public StockExchangeState stocks;
    public CompanyMarketState companies;
    public List<PropertySave> properties;
    public List<LandPlotSave> plots;
    public ReportServiceState reports;
    public SnsFeedState sns;
    public List<PlacedVehicleSave> vehicles;
    /// <summary>Book bodies by instance id: a sync-layer server's world only (ServerBooks, 12.10.3). A PC world leaves it empty.</summary>
    public List<BookSave> books;
    /// <summary>The goshiwon deed the server gave each account (ServerLedger, 12.10.5). A PC world leaves it empty.</summary>
    public List<StartDeedSave> startDeeds;
    /// <summary>The library of a sync-layer server's world (ServerLibrary, 12.10.6): works without pages, loans, accounts. A PC world keeps library.json instead.</summary>
    public LibraryCatalog.Ledger library;
}

[Serializable]
public sealed class BookSave
{
    public string id;
    public BookInstanceData book;
}

/// <summary>An account and the goshiwon room (property id) whose deed the server gave it once.</summary>
[Serializable]
public sealed class StartDeedSave
{
    public string account, deed;
}

/// <summary>A vehicle standing in this PC's offline world (WorldDroppedItem).</summary>
[Serializable]
public sealed class PlacedVehicleSave
{
    public string id, item, inst, scene;
    public Vector3 pos;
    public float ry;
}

public static class WorldSave
{
    public const int CurrentVersion = 1;

    /// <summary>Tests point this at a scratch file so they never touch the real world.</summary>
    public static string PathOverride;

    /// <summary>Placed vehicles live in the UI assembly (WorldDroppedItem), which installs these.</summary>
    public static Func<List<PlacedVehicleSave>> CaptureVehicles;
    public static Action<List<PlacedVehicleSave>> RestoreVehicles;

    public static string Path => PathOverride ?? System.IO.Path.Combine(ProfileSlot.DataRoot, "world", "world.json");

    /// <summary>Last JSON built, written or applied. Used for change detection.</summary>
    public static string LastJson { get; private set; }

    /// <summary>The fresh world as it was before anything was loaded; used when there is no world file.</summary>
    public static string Baseline { get; private set; }

    public static void CaptureBaseline()
    {
        if (Baseline != null) return;
        var d = Capture();
        d.worldId = null;
        Baseline = ToJson(d);
        WorldId = null;
    }

    /// <summary>Back to the untouched fresh world with no identity (the next save creates a new worldId).</summary>
    public static bool RestoreBaseline()
    {
        bool ok = !string.IsNullOrEmpty(Baseline) && Restore(Baseline);
        WorldId = null;
        return ok;
    }

    /// <summary>
    /// Offline records were owned by the old per-PC identity (GUID and the default name "Player"). Once this PC
    /// has a server account the own world's records move to the account id. Idempotent; only the own world calls it.
    /// </summary>
    public static void MigrateOwners()
    {
        if (!GameSession.HasAccount) return;
        string id = GameSession.LocalPlayerId;
        if (PropertyRegistry.Instance)
            PropertyRegistry.Instance.RemapOwners(o => o == GameSession.OfflinePlayerId || o == GameSession.OfflinePlayerName ? id : o);
        if (LandPlotManager.Instance) LandPlotManager.Instance.ClaimUnowned(id);
    }

    public static string WorldId { get; private set; }

    /// <summary>The id of the world in memory, created now if it has none yet (a fresh world gets one on its first save).</summary>
    public static string EnsureWorldId()
    {
        if (string.IsNullOrEmpty(WorldId)) WorldId = Guid.NewGuid().ToString("N");
        return WorldId;
    }

    public static WorldSaveData Capture()
    {
        EnsureWorldId();
        var data = new WorldSaveData { worldId = WorldId };
        if (GameClock.Current != null) data.clockMinutes = GameClock.Current.Now.TotalMinutes;
        if (StockMarketManager.Instance) data.stocks = StockMarketManager.Instance.Exchange.CaptureState();
        if (CompanyManager.Instance) data.companies = CompanyManager.Instance.Market.CaptureState();
        if (PropertyRegistry.Instance) data.properties = PropertyRegistry.Instance.CaptureState();
        if (LandPlotManager.Instance) data.plots = LandPlotManager.Instance.CaptureState();
        if (ReportManager.Instance) data.reports = ReportManager.Instance.Service.CaptureState();
        if (SnsManager.Instance) data.sns = SnsManager.Instance.Feed.CaptureState();
        data.vehicles = CaptureVehicles?.Invoke();
        return data;
    }

    public static string ToJson(WorldSaveData data) => JsonUtility.ToJson(data);

    // ---- the shared economy of a sync-layer server (12.9): six sections, no clock, no vehicles, no identity ----

    /// <summary>The shared sections in their WorldSaveData field order (world entity sections 2..7).</summary>
    public static readonly string[] SectionNames = { "stocks", "companies", "properties", "plots", "reports", "sns" };

    /// <summary>The managers' shared state now. Unlike Capture it gives this world no id and leaves the clock and vehicles out.</summary>
    public static WorldSaveData CaptureShared()
    {
        var data = new WorldSaveData();
        if (StockMarketManager.Instance) data.stocks = StockMarketManager.Instance.Exchange.CaptureState();
        if (CompanyManager.Instance) data.companies = CompanyManager.Instance.Market.CaptureState();
        if (PropertyRegistry.Instance) data.properties = PropertyRegistry.Instance.CaptureState();
        if (LandPlotManager.Instance) data.plots = LandPlotManager.Instance.CaptureState();
        if (ReportManager.Instance) data.reports = ReportManager.Instance.Service.CaptureState();
        if (SnsManager.Instance) data.sns = SnsManager.Instance.Feed.CaptureState();
        return data;
    }

    /// <summary>
    /// JSON of one section of <paramref name="all"/> (the other fields left empty). reports and sns keep only the newest
    /// <paramref name="recent"/> entries (0 = all): their lists have no upper bound, the server keeps them whole.
    /// </summary>
    public static string SectionJson(WorldSaveData all, int section, int recent = 0)
    {
        var one = new WorldSaveData();
        switch (section)
        {
            case 0: one.stocks = all.stocks; break;
            case 1: one.companies = all.companies; break;
            case 2: one.properties = all.properties; break;
            case 3: one.plots = all.plots; break;
            case 4:
                one.reports = all.reports;
                if (recent > 0 && all.reports != null && all.reports.reports.Count > recent)
                    one.reports = new ReportServiceState { reports = all.reports.reports.GetRange(all.reports.reports.Count - recent, recent), ledger = all.reports.ledger, nextId = all.reports.nextId };
                break;
            case 5:
                one.sns = all.sns;
                if (recent > 0 && all.sns != null && all.sns.posts.Count > recent) // newest first
                    one.sns = new SnsFeedState { posts = all.sns.posts.GetRange(0, recent), nextId = all.sns.nextId };
                break;
            default: throw new ArgumentOutOfRangeException(nameof(section));
        }
        return ToJson(one);
    }

    /// <summary>Applies one section from <see cref="SectionJson"/>; nothing else (not the clock, not another section) changes.</summary>
    public static bool ApplySection(int section, string json)
    {
        WorldSaveData d;
        try { d = JsonUtility.FromJson<WorldSaveData>(json); }
        catch (Exception e) { Debug.LogWarning("WorldSave: unreadable section " + section + ". " + e.Message); return false; }
        if (d == null) return false;
        var failed = new List<string>();
        ApplyShared(failed, d, section);
        return failed.Count == 0;
    }

    /// <summary>Applies all six sections of <paramref name="d"/> (a server world's state, or the baseline for a new one).</summary>
    public static bool RestoreShared(WorldSaveData d)
    {
        if (d == null) return false;
        var failed = new List<string>();
        for (int i = 0; i < SectionNames.Length; i++) ApplyShared(failed, d, i);
        if (failed.Count > 0) Debug.LogWarning("WorldSave: failed to restore " + string.Join(", ", failed));
        return failed.Count == 0;
    }

    static void ApplyShared(List<string> failed, WorldSaveData d, int section)
    {
        switch (section)
        {
            case 0: Apply(failed, "stocks", () => { if (d.stocks != null && StockMarketManager.Instance) StockMarketManager.Instance.Exchange.RestoreState(d.stocks); }); break;
            case 1: Apply(failed, "companies", () => { if (d.companies != null && CompanyManager.Instance) CompanyManager.Instance.Market.RestoreState(d.companies); }); break;
            case 2: Apply(failed, "properties", () => { if (d.properties != null && PropertyRegistry.Instance) PropertyRegistry.Instance.RestoreState(d.properties); }); break;
            case 3: Apply(failed, "plots", () => { if (d.plots != null && LandPlotManager.Instance) LandPlotManager.Instance.RestoreState(d.plots); }); break;
            case 4: Apply(failed, "reports", () => { if (d.reports != null && ReportManager.Instance) ReportManager.Instance.Service.RestoreState(d.reports); }); break;
            case 5: Apply(failed, "sns", () => { if (d.sns != null && SnsManager.Instance) SnsManager.Instance.Feed.RestoreState(d.sns); }); break;
        }
    }

    /// <summary>Applies shared state in memory only. Never touches disk or personal values.</summary>
    public static bool Restore(string json)
    {
        WorldSaveData d;
        try { d = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<WorldSaveData>(json); }
        catch (Exception e) { Debug.LogWarning("WorldSave: unreadable snapshot. " + e.Message); return false; }
        if (d == null || d.version != CurrentVersion) { Debug.LogWarning("WorldSave: unsupported snapshot version."); return false; }

        var failed = new List<string>();
        Apply(failed, "clock", () => { if (GameClock.Current != null) GameClock.Current.SetNow(GameTime.FromTotalMinutes(d.clockMinutes)); });
        Apply(failed, "stocks", () => { if (d.stocks != null && StockMarketManager.Instance) StockMarketManager.Instance.Exchange.RestoreState(d.stocks); });
        Apply(failed, "companies", () => { if (d.companies != null && CompanyManager.Instance) CompanyManager.Instance.Market.RestoreState(d.companies); });
        Apply(failed, "properties", () => { if (d.properties != null && PropertyRegistry.Instance) PropertyRegistry.Instance.RestoreState(d.properties); });
        Apply(failed, "plots", () => { if (d.plots != null && LandPlotManager.Instance) LandPlotManager.Instance.RestoreState(d.plots); });
        Apply(failed, "reports", () => { if (d.reports != null && ReportManager.Instance) ReportManager.Instance.Service.RestoreState(d.reports); });
        Apply(failed, "sns", () => { if (d.sns != null && SnsManager.Instance) SnsManager.Instance.Feed.RestoreState(d.sns); });
        Apply(failed, "vehicles", () => RestoreVehicles?.Invoke(d.vehicles));
        LastJson = json;
        if (!string.IsNullOrEmpty(d.worldId)) WorldId = d.worldId;
        if (failed.Count > 0) Debug.LogWarning("WorldSave: failed to restore " + string.Join(", ", failed));
        return failed.Count == 0;
    }

    static void Apply(List<string> failed, string name, Action restore)
    {
        try { restore(); }
        catch (Exception e) { failed.Add(name); Debug.LogException(e); }
    }

    /// <summary>Authority only (offline). Keeps one backup (world.prev.json) of the previous file.</summary>
    public static bool SaveToDisk() => SaveToDisk(Path);

    public static bool SaveToDisk(string path)
    {
        if (!GameSession.IsAuthority) return false;
        try
        {
            string json = ToJson(Capture());
            if (File.Exists(path)) File.Copy(path, System.IO.Path.ChangeExtension(path, ".prev.json"), true);
            SaveManager.WriteJsonFile(path, json);
            LastJson = json;
            return true;
        }
        catch (Exception e) { Debug.LogWarning("WorldSave: save failed. " + e.Message); return false; }
    }

    public static bool LoadFromDisk() => LoadFromDisk(Path);

    public static bool LoadFromDisk(string path)
    {
        try
        {
            bool ok = File.Exists(path) && Restore(File.ReadAllText(path));
            if (ok) MigrateOwners();
            return ok;
        }
        catch (Exception e) { Debug.LogWarning("WorldSave: load failed. " + e.Message); return false; }
    }
}
