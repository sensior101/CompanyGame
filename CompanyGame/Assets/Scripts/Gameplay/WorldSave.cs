using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Shared world state computed by the host (or single player): clock, stock prices, companies, property,
/// reports and SNS. Personal data (bank balance, portfolio, wallet cash) is NOT here, see PersonalSave.
/// </summary>
[Serializable]
public sealed class WorldSaveData
{
    public int version = 1;
    public long clockMinutes;
    public string worldId;   // GUID created with the world; copies share it
    public int generation;   // +1 on every host handover; the higher copy is the valid one
    public long opSeq;   // last shared-world op already reflected in this snapshot
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

/// <summary>A vehicle standing in the world (WorldDroppedItem). rider = room member driving it.</summary>
[Serializable]
public sealed class PlacedVehicleSave
{
    public string id, item, inst, scene, rider;
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

    /// <summary>Last shared-world op seq reflected in local state. Written into snapshots; the net layer keeps it current.</summary>
    public static long OpSeq { get; set; }

    /// <summary>The fresh world as it was before anything was loaded; a visitor falls back to it when it has no world file.</summary>
    public static string Baseline { get; private set; }

    public static void CaptureBaseline()
    {
        if (Baseline != null) return;
        var d = Capture();
        d.worldId = null; d.generation = 0; d.opSeq = 0;
        Baseline = ToJson(d);
        WorldId = null; Generation = 0;
    }

    /// <summary>Back to the untouched fresh world with no identity (the next save creates a new worldId).</summary>
    public static bool RestoreBaseline()
    {
        bool ok = !string.IsNullOrEmpty(Baseline) && Restore(Baseline, false);
        WorldId = null; Generation = 0; OpSeq = 0;
        return ok;
    }

    /// <summary>Worldid and generation of the locked (handed-over) file, if any.</summary>
    public static bool TryReadLocked(out string worldId, out int generation)
    {
        worldId = null; generation = 0;
        try
        {
            if (!File.Exists(HandoverPath)) return false;
            var d = JsonUtility.FromJson<WorldSaveData>(File.ReadAllText(HandoverPath));
            if (d == null) return false;
            worldId = d.worldId; generation = d.generation;
            return !string.IsNullOrEmpty(worldId);
        }
        catch (Exception) { return false; }
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
    public static int Generation { get; private set; }

    /// <summary>The new host calls this when it takes the world over from a previous host.</summary>
    public static void BumpGeneration() { Generation++; Remember(); }

    static string KnownKey(string id) => ProfileSlot.Key("World.Gen." + id);

    // Highest generation this PC has seen per world; a lower-generation copy is refused on load.
    static void Remember()
    {
        if (string.IsNullOrEmpty(WorldId)) return;
        if (PlayerPrefs.GetInt(KnownKey(WorldId), -1) < Generation) PlayerPrefs.SetInt(KnownKey(WorldId), Generation);
    }

    public static string HandoverPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "world.handover.json");

    /// <summary>A loadable local world exists (a file that is not locked by a handover).</summary>
    public static bool HasLocalWorld => File.Exists(Path) && !HandedOver;

    /// <summary>When the local world was last saved and how far its clock had run, for the title screen. False if unreadable.</summary>
    public static bool TryPeek(out DateTime savedAt, out long clockMinutes)
    {
        savedAt = default; clockMinutes = 0;
        try
        {
            if (!HasLocalWorld) return false;
            var d = JsonUtility.FromJson<WorldSaveData>(File.ReadAllText(Path));
            if (d == null) return false;
            savedAt = File.GetLastWriteTime(Path); clockMinutes = d.clockMinutes;
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// "New world": the current world file (and a handover-locked one) is MOVED to a timestamped backup next to it,
    /// never deleted, then the fresh baseline world with a new identity is saved as the new world.json.
    /// Returns false (and changes nothing in memory) if the backup could not be made.
    /// </summary>
    public static bool StartNewWorld(out string backupPath)
    {
        backupPath = null;
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try
        {
            foreach (string f in new[] { Path, HandoverPath })
            {
                if (!File.Exists(f)) continue;
                string dst = System.IO.Path.ChangeExtension(f, null) + ".backup-" + stamp + ".json";
                File.Move(f, dst);
                backupPath ??= dst;
            }
        }
        catch (Exception e) { Debug.LogWarning("WorldSave: backup failed, world kept. " + e.Message); return false; }
        RestoreBaseline();
        return SaveToDisk();
    }

    /// <summary>True while the local world file is locked because it was handed to another host.</summary>
    public static bool HandedOver => File.Exists(HandoverPath);

    /// <summary>The id of the world in memory, created now if it has none yet (a fresh world gets one on its first save).</summary>
    public static string EnsureWorldId()
    {
        if (string.IsNullOrEmpty(WorldId)) { WorldId = Guid.NewGuid().ToString("N"); Remember(); }
        return WorldId;
    }

    public static WorldSaveData Capture()
    {
        EnsureWorldId();
        var data = new WorldSaveData { opSeq = OpSeq, worldId = WorldId, generation = Generation };
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
    public static bool Restore(string json) => Restore(json, true);

    /// <summary>Like Restore, but ignores a snapshot older than the ops already applied (opSeq below <paramref name="appliedSeq"/>).</summary>
    public static bool RestoreIfNotOlder(string json, long appliedSeq) => Restore(json, true, appliedSeq);

    static bool Restore(string json, bool adoptOpSeq, long minOpSeq = 0)
    {
        WorldSaveData d;
        try { d = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<WorldSaveData>(json); }
        catch (Exception e) { Debug.LogWarning("WorldSave: unreadable snapshot. " + e.Message); return false; }
        if (d == null || d.version != CurrentVersion) { Debug.LogWarning("WorldSave: unsupported snapshot version."); return false; }
        if (d.opSeq < minOpSeq) return false; // older than what this peer already applied: the ops after it would be lost

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
        OpSeq = adoptOpSeq ? d.opSeq : 0;
        if (!string.IsNullOrEmpty(d.worldId)) { WorldId = d.worldId; Generation = d.generation; Remember(); }
        if (failed.Count > 0) Debug.LogWarning("WorldSave: failed to restore " + string.Join(", ", failed));
        return failed.Count == 0;
    }

    static void Apply(List<string> failed, string name, Action restore)
    {
        try { restore(); }
        catch (Exception e) { failed.Add(name); Debug.LogException(e); }
    }

    /// <summary>Host/single only. Keeps one backup (world.prev.json) of the previous file.</summary>
    public static bool SaveToDisk() => SaveToDisk(Path);

    public static bool SaveToDisk(string path)
    {
        if (!GameSession.IsAuthority || HandedOver) return false;
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
            bool ok = File.Exists(path) && !HandedOver && !IsStale(path) && Restore(File.ReadAllText(path), false); // a new room restarts op numbering
            if (ok) MigrateOwners();
            return ok;
        }
        catch (Exception e) { Debug.LogWarning("WorldSave: load failed. " + e.Message); return false; }
    }

    static bool IsStale(string path)
    {
        var d = JsonUtility.FromJson<WorldSaveData>(File.ReadAllText(path));
        if (d == null || string.IsNullOrEmpty(d.worldId) || PlayerPrefs.GetInt(KnownKey(d.worldId), -1) <= d.generation) return false;
        Debug.LogWarning("WorldSave: refusing an outdated copy of this world (a newer host has it).");
        return true;
    }

    /// <summary>Locks the local world (renames it) once it is handed to another host. Locked worlds cannot be loaded or saved.</summary>
    public static bool LockForHandover()
    {
        try
        {
            if (File.Exists(Path)) { if (File.Exists(HandoverPath)) File.Delete(HandoverPath); File.Move(Path, HandoverPath); }
            else SaveManager.WriteJsonFile(HandoverPath, LastJson ?? ToJson(Capture())); // no file yet: still leave a lock marker
            return File.Exists(HandoverPath);
        }
        catch (Exception e) { Debug.LogWarning("WorldSave: lock failed. " + e.Message); return false; }
    }

    /// <summary>Rollback: the server made this PC host again before the new host confirmed. Restores the locked file.</summary>
    public static bool UnlockHandover()
    {
        try
        {
            if (!File.Exists(HandoverPath)) return false;
            if (File.Exists(Path)) File.Delete(Path);
            File.Move(HandoverPath, Path);
            return true;
        }
        catch (Exception e) { Debug.LogWarning("WorldSave: unlock failed. " + e.Message); return false; }
    }

    /// <summary>Removes the local world file and its backup (after the world was handed to a new host).</summary>
    public static bool Delete()
    {
        try
        {
            string path = Path;
            foreach (string f in new[] { path, HandoverPath, System.IO.Path.ChangeExtension(path, ".prev.json"), path + ".tmp" })
                if (File.Exists(f)) File.Delete(f);
            return true;
        }
        catch (Exception e) { Debug.LogWarning("WorldSave: delete failed. " + e.Message); return false; }
    }
}
