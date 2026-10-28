using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode round trip checks for WorldSave (shared) and PersonalSave. Managers are built on a throwaway
/// object and their singletons are torn down afterwards. Run: -executeMethod WorldSaveQA.RunCli
/// </summary>
public static class WorldSaveQA
{
    [Serializable]
    sealed class Report
    {
        public bool passed;
        public int checks;
        public int snapshotBytes;
        public List<string> failures = new List<string>();
    }

    [MenuItem("Tools/Company Game/World Save/Run QA")]
    static void Menu() => Debug.Log(Run());

    public static void RunCli() => Debug.Log(Run());

    public static string Run()
    {
        var report = new Report();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            report.failures.Add("Run in Edit mode.");
            return JsonUtility.ToJson(report, true);
        }
        var holder = new GameObject("WorldSaveQA");
        var priorMode = GameSession.Mode;
        var priorClock = GameClock.Current;
        string dir = Path.Combine(Application.temporaryCachePath, "worldsaveqa");
        string prefId = PlayerPrefs.GetString("Session.AccountId", ""), prefName = PlayerPrefs.GetString("Session.AccountName", "");
        try
        {
            GameClock.Current = null;
            GameSession.ClearAccount(); // the QA runs as the offline identity
            Execute(report, holder, dir);
            ExecuteOps(report, holder, dir);
            ExecuteVehicles(report);
        }
        catch (Exception e) { report.failures.Add("Exception: " + e); }
        finally
        {
            GameSession.Begin(priorMode);
            GameClock.Current = priorClock;
            WorldSave.PathOverride = null; PersonalSave.DirOverride = null;
            GameSession.ClearAccount();
            if (prefId != "") { PlayerPrefs.SetString("Session.AccountId", prefId); PlayerPrefs.SetString("Session.AccountName", prefName); }
            GameSession.ReloadAccount();
            Teardown();
            UnityEngine.Object.DestroyImmediate(holder);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        report.passed = report.failures.Count == 0;
        return JsonUtility.ToJson(report, true);
    }

    static void Execute(Report r, GameObject holder, string dir)
    {
        GameSession.Begin(SessionMode.Host);
        var clock = Make<SimpleGameClock>(holder);
        var bank = Make<BankManager>(holder);
        var company = Make<CompanyManager>(holder);
        var stocks = Make<StockMarketManager>(holder);
        var props = Make<PropertyRegistry>(holder);
        var sns = Make<SnsManager>(holder);
        var reports = Make<ReportManager>(holder);
        WorldSave.CaptureBaseline(); PersonalSave.CaptureBaseline();
        Check(r, GameClock.Current == clock && BankManager.Instance == bank && StockMarketManager.Instance == stocks, "Managers registered");

        // Shared state
        clock.SetNow(new GameTime(3, 10));
        props.TryRegister("qa:house", "ownerA");
        company.Market.AddCompany(CompanyState.FromData(new CompanyData { companyId = "qa_co", displayName = "QA Co" }, "ownerA", true));
        sns.Feed.TryPost(clock.Now, "u1", "User", "hello world", out _);
        // Personal state
        bank.RestoreBalance(12345);
        stocks.Exchange.Portfolio.AddShares("qa_stock", 5, 5000);
        var wallet = new InventoryState();
        long cashBefore = CashService.CarriedTotal(wallet);
        string personalJson = PersonalSave.Export();
        long marker = clock.Now.TotalMinutes;

        string json = WorldSave.ToJson(WorldSave.Capture());
        r.snapshotBytes = json.Length;
        Debug.Log("WorldSaveQA: snapshot size " + json.Length + " bytes");
        Check(r, !json.Contains("bankBalance") && !json.Contains("qa_stock"), "Shared snapshot has no bank balance or portfolio");

        // Mutate everything, then restore shared state
        clock.SetNow(new GameTime(9, 12));
        props.TryAbandon("qa:house", "ownerA");
        company.Market.AddCompany(CompanyState.FromData(new CompanyData { companyId = "qa_co2", displayName = "QA Two" }, "ownerB", true));
        bank.RestoreBalance(777);
        stocks.Exchange.Portfolio.AddShares("qa_stock", 3, 100);
        Check(r, WorldSave.Restore(json), "Restore returns true");
        Check(r, clock.Now.TotalMinutes == marker, "Clock restored");
        Check(r, props.GetOwner("qa:house") == "ownerA", "Property owner restored");
        Check(r, company.Market.Get("qa_co") != null, "Company restored");
        Check(r, sns.Feed.GetRecent(10).Exists(p => p.text == "hello world"), "SNS post restored");
        Check(r, bank.Money == 777, "Shared restore leaves personal balance untouched");
        Check(r, stocks.Exchange.Portfolio.GetQuantity("qa_stock") == 8, "Shared restore leaves portfolio untouched");
        Check(r, CashService.CarriedTotal(wallet) == cashBefore, "Wallet cash unchanged");
        Check(r, WorldSave.ToJson(WorldSave.Capture()) == json, "Capture after restore equals the snapshot");

        // Personal round trip
        Check(r, PersonalSave.Import(personalJson), "PersonalSave import");
        Check(r, bank.Money == 12345 && stocks.Exchange.Portfolio.GetQuantity("qa_stock") == 5, "PersonalSave round trip");
        Check(r, PersonalSave.SaveToDisk(Path.Combine(dir, "player", "qa.json")) &&
            PersonalSave.LoadFromDisk(Path.Combine(dir, "player", "qa.json")), "PersonalSave file round trip");

        // Disk: participants never write
        string worldFile = Path.Combine(dir, "world", "world.json");
        GameSession.Begin(SessionMode.Client);
        Check(r, !WorldSave.SaveToDisk(worldFile) && !File.Exists(worldFile), "Client does not write the world file");
        GameSession.Begin(SessionMode.Host);
        Check(r, WorldSave.SaveToDisk(worldFile) && File.Exists(worldFile), "Host writes the world file");
        Check(r, WorldSave.SaveToDisk(worldFile) && File.Exists(Path.Combine(dir, "world", "world.prev.json")), "Backup file kept");
        Check(r, WorldSave.LoadFromDisk(worldFile), "World file loads");

        // Clients never tick prices
        GameSession.Begin(SessionMode.Client);
        long price = stocks.Exchange.GetQuotes().Count > 0 ? stocks.Exchange.GetQuotes()[0].price : 0;
        stocks.Exchange.OnHourChanged(new GameTime(3, 10));
        long after = stocks.Exchange.GetQuotes().Count > 0 ? stocks.Exchange.GetQuotes()[0].price : 0;
        Check(r, price == after, "Client does not simulate prices");
    }

    // ---- shared-world requests: WorldOps (the server applies them as the account of the connection) and the gate ----

    static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static string Apply(string from, string kind, SharedOpArgs a) => WorldOps.Apply(from, kind, JsonUtility.ToJson(a));

    static void ExecuteOps(Report r, GameObject holder, string dir)
    {
        WorldSave.PathOverride = Path.Combine(dir, "ops", "world.json");
        PersonalSave.DirOverride = Path.Combine(dir, "ops", "player");
        GameSession.Begin(SessionMode.Host);
        var props = PropertyRegistry.Instance; var land = Make<LandPlotManager>(holder);
        var bank = BankManager.Instance; var sns = SnsManager.Instance;
        try
        {
            props.RestoreState(new List<PropertySave>());

            // Security H1: the actor named in a request must be the account of the connection.
            Check(r, Apply("7", "prop.reg", new SharedOpArgs { a = "vehicle:ok", b = "7" }) == null && props.GetOwner("vehicle:ok") == "7", "a request with actor == from is applied");
            Check(r, Apply("7", "prop.reg", new SharedOpArgs { a = "vehicle:spoof", b = "42" }) != null && props.GetOwner("vehicle:spoof") == null, "a request with actor != from is rejected");
            Check(r, Apply("7", "prop.ab", new SharedOpArgs { a = "vehicle:ok", b = "42" }) != null && props.GetOwner("vehicle:ok") == "7", "abandon naming another actor is rejected");
            Check(r, Apply("7", "sns.post", new SharedOpArgs { a = "spoof", b = "42", c = "Tom" }) != null && !sns.Feed.GetRecent(10).Exists(p => p.text == "spoof"), "SNS post under another id is rejected");
            Check(r, Apply("7", "report.sub", new SharedOpArgs { a = "42", b = "ev", c = "42", d = "0" }) != null, "report under another reporter id is rejected");

            // Security H2: land has an owner; only the owner registers.
            Check(r, Apply("7", "land.buy", new SharedOpArgs { a = "plotB" }) == null && land.GetOwner("plotB") == "7" && land.GetState("plotB") == LandPlotState.Owned, "land.buy sets the owner from `from`");
            Check(r, Apply("7", "land.buy", new SharedOpArgs { a = "plotB" }) != null, "a second land.buy of the same plot fails (first wins)");
            Check(r, Apply("42", "land.reg", new SharedOpArgs { a = "plotB", b = "Stolen Co", c = "ST" }) != null && land.GetState("plotB") == LandPlotState.Owned, "land.reg by a non-owner is rejected");
            Check(r, Apply("7", "land.reg", new SharedOpArgs { a = "plotB", b = "Seven Co", c = "SC" }) == null && land.GetState("plotB") == LandPlotState.Registered, "land.reg by the owner works");
            Check(r, !land.RefundPending("plotB"), "no refund without a pending payment of this client");

            // M7 on a server: a purchase whose request cannot be sent is refunded and fails; a lost race refunds the plot price.
            SharedWriteGate.ServerCheck = () => true;
            SharedWriteGate.ServerSender = (k, j) => false;
            bank.RestoreBalance(100000);
            var plotC = MakePlot(holder, "plotC", 50000);
            Check(r, land.TryPurchase(plotC) == LandPlotResult.SendFailed && bank.Money == 100000, "unsent land purchase is refunded and reported");
            Check(r, sns.Post("x") == SnsPostResult.OnCooldown, "SNS post honours a failed send");
            SharedWriteGate.ServerSender = (k, j) => true;
            var plotD = MakePlot(holder, "plotD", 40000);
            Check(r, land.TryPurchase(plotD) == LandPlotResult.Ok && bank.Money == 60000, "sent land purchase is charged");
            Check(r, land.RefundPending("plotD") && bank.Money == 100000, "a lost race refunds the plot price");
            SharedWriteGate.ServerCheck = null; SharedWriteGate.ServerSender = null;

            // Offline the gate is open: the managers write at once.
            Check(r, !SharedWriteGate.InRoom && props.TryRegister("qa:offline", "me") && props.GetOwner("qa:offline") == "me", "offline a registration applies at once");
        }
        finally
        {
            SharedWriteGate.ServerCheck = null; SharedWriteGate.ServerSender = null;
        }
    }

    static LandPlot MakePlot(GameObject holder, string id, long price)
    {
        var go = new GameObject(id); go.transform.SetParent(holder.transform);
        var plot = go.AddComponent<LandPlot>();
        typeof(LandPlot).GetField("plotId", Any).SetValue(plot, id);
        typeof(LandPlot).GetField("price", Any).SetValue(plot, price);
        return plot;
    }

    static T Make<T>(GameObject holder) where T : MonoBehaviour
    {
        var child = new GameObject(typeof(T).Name);
        child.transform.SetParent(holder.transform); // a parent keeps Awake from calling DontDestroyOnLoad
        var c = child.AddComponent<T>();
        var awake = typeof(T).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        awake?.Invoke(c, null);
        return c;
    }

    // Placed vehicles of this PC's offline world: they ride along in the world snapshot (world.json).
    static void ExecuteVehicles(Report r)
    {
        var cars = Resources.LoadAll<ItemData>("Inventory/Vehicles/Cars");
        Check(r, cars.Length > 0, "a car item exists for the vehicle checks");
        if (cars.Length == 0) return;
        WorldDroppedItem.RestoreVehicles(null);
        var car = new PlacedVehicleSave { id = "qacar", item = cars[0].itemId, inst = "qainst", scene = "Assets/Scenes/none.unity", pos = new Vector3(1, 2, 3), ry = 90 };
        PlacedVehicleSave Only() { var all = WorldDroppedItem.CaptureVehicles(); return all.Count == 1 ? all[0] : null; }

        WorldDroppedItem.RestoreVehicles(new List<PlacedVehicleSave> { car });
        Check(r, Only()?.inst == "qainst" && Only().pos == car.pos, "a saved car comes back, keeping its instance id");
        WorldDroppedItem.RestoreVehicles(new List<PlacedVehicleSave> { car });
        Check(r, WorldDroppedItem.CaptureVehicles().Count == 1, "restoring the same world again adds nothing");
        var moved = new PlacedVehicleSave { id = "qacar", item = car.item, inst = car.inst, scene = car.scene, pos = new Vector3(5, 0, 5), ry = 180 };
        WorldDroppedItem.RestoreVehicles(new List<PlacedVehicleSave> { moved });
        Check(r, Only() != null && Only().pos == moved.pos && Mathf.Approximately(Only().ry, 180), "a moved car is parked where the world has it");

        var snap = JsonUtility.FromJson<WorldSaveData>(JsonUtility.ToJson(new WorldSaveData { vehicles = WorldDroppedItem.CaptureVehicles() }));
        WorldDroppedItem.RestoreVehicles(new List<PlacedVehicleSave>());
        Check(r, WorldDroppedItem.CaptureVehicles().Count == 0, "a snapshot without the car removes it");
        WorldDroppedItem.RestoreVehicles(snap.vehicles);
        Check(r, Only()?.id == "qacar" && Only().pos == new Vector3(5, 0, 5), "the car survives the world snapshot JSON");
        WorldDroppedItem.RestoreVehicles(null);
        Check(r, WorldDroppedItem.CaptureVehicles().Count == 0, "an empty world has no cars");
    }

    static void Teardown()
    {
        foreach (Type t in new[] { typeof(CompanyManager), typeof(StockMarketManager), typeof(PropertyRegistry), typeof(SnsManager), typeof(ReportManager), typeof(LandPlotManager) })
        {
            Type g = typeof(GameSystem<>).MakeGenericType(t);
            g.GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { null });
        }
        typeof(BankManager).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { null });
    }

    static void Check(Report r, bool ok, string name)
    {
        r.checks++;
        if (!ok) r.failures.Add(name);
    }
}
