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
            GameSession.ClearAccount(); // the QA runs as the legacy offline identity until the fake `hello`
            Execute(report, holder, dir);
            ExecuteRoom(report, holder, dir);
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

    // ---- room flow: real NetSession + NetWorldBridge fed with the messages the server sends ----

    static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static void Feed(NetSession session, NetMsg m) => typeof(NetSession).GetMethod("Handle", Any).Invoke(session, new object[] { m });

    static NetMsg Op(long seq, string from, string kind, SharedOpArgs a) =>
        new NetMsg { t = "op", seq = seq, from = from, ev = kind, data = JsonUtility.ToJson(a) };

    static NetMsg Joined(string host, string snap) => new NetMsg
    {
        t = "joined", rid = "r1", host = host, snap = snap,
        players = new[] { new PeerInfo { id = "42", name = "Tom" }, new PeerInfo { id = "7", name = "Bob" } }
    };

    static string WithIdentity(string json, string worldId, int generation, long opSeq)
    {
        var d = JsonUtility.FromJson<WorldSaveData>(json);
        d.worldId = worldId; d.generation = generation; d.opSeq = opSeq;
        return JsonUtility.ToJson(d);
    }

    static string Ack(string worldId, int generation) =>
        "{\"handover\":\"ok\",\"worldId\":\"" + worldId + "\",\"generation\":" + generation + "}";

    static void ExecuteRoom(Report r, GameObject holder, string dir)
    {
        WorldSave.PathOverride = Path.Combine(dir, "room", "world.json");
        PersonalSave.DirOverride = Path.Combine(dir, "room", "player");
        GameSession.Begin(SessionMode.Host);
        var props = PropertyRegistry.Instance; var land = Make<LandPlotManager>(holder);
        var bank = BankManager.Instance; var sns = SnsManager.Instance;
        var sessionGo = new GameObject("QA Session"); sessionGo.transform.SetParent(holder.transform);
        var session = sessionGo.AddComponent<NetSession>();
        var bridge = sessionGo.AddComponent<NetWorldBridge>();
        typeof(NetWorldBridge).GetMethod("Bind", Any).Invoke(bridge, new object[] { session });
        try
        {
            // Own world made offline under the legacy identity ("Player" and the per-PC GUID).
            props.RestoreState(new List<PropertySave>());
            props.TryRegister("qa:own", GameSession.LocalPlayerName);
            land.ApplyPurchase("plotOld", GameSession.OfflinePlayerId);
            Check(r, WorldSave.SaveToDisk(), "own world saved before login");

            // First login: records move to the account id; identity comes from hello.
            Feed(session, new NetMsg { t = "hello", id = "42", name = "Tom" });
            Check(r, GameSession.LocalPlayerId == "42" && GameSession.LocalPlayerName == "Tom", "hello sets LocalPlayerId/Name from the account");
            Check(r, props.CaptureState().Exists(p => p.id == "qa:own" && p.owner == "42"), "legacy property owner migrated to the account id");
            Check(r, props.GetOwner("qa:own") == "Tom", "UI still sees the own record as LocalPlayerName");
            Check(r, land.GetOwner("plotOld") == "42", "legacy land owner migrated to the account id");
            Check(r, WorldSave.SaveToDisk(), "own world saved after migration");
            string ownFile = File.ReadAllText(WorldSave.Path), ownId = WorldSave.WorldId;

            // H1: op numbering restarts with every room.
            WorldSave.OpSeq = 9;
            Feed(session, Joined("7", ""));
            Check(r, WorldSave.OpSeq == 0 && session.LastOpSeq == 0, "joining a room resets OpSeq");
            Feed(session, Op(1, "7", "prop.reg", new SharedOpArgs { a = "vehicle:first", b = "7" }));
            Check(r, props.GetOwner("vehicle:first") == "Bob", "seq 1 of a new room is applied (not skipped as old)");
            session.Leave();
            Check(r, WorldSave.OpSeq == 0 && props.GetOwner("vehicle:first") == null, "leaving a room resets OpSeq and drops the room's changes");

            // The room's world as the host would send it.
            props.TryRegister("qa:room", "7");
            string roomSnap = WithIdentity(WorldSave.ToJson(WorldSave.Capture()), "roomworld", 4, 3);
            Check(r, WorldSave.Restore(ownFile), "own world restored for the next step");
            Feed(session, Joined("7", roomSnap));
            Check(r, GameSession.Mode == SessionMode.Client && WorldSave.OpSeq == 3 && session.LastOpSeq == 3 && props.GetOwner("qa:room") == "Bob",
                "joined adopts the snapshot and its opSeq");

            // H2 / security H1: the actor named in an op must be the server-attached `from`.
            Feed(session, Op(4, "7", "prop.reg", new SharedOpArgs { a = "vehicle:ok", b = "7" }));
            Feed(session, Op(5, "7", "prop.reg", new SharedOpArgs { a = "vehicle:spoof", b = "42" }));
            Check(r, props.GetOwner("vehicle:ok") == "Bob", "op with actor == from is applied");
            Check(r, props.GetOwner("vehicle:spoof") == null, "op with actor != from is rejected");
            Feed(session, Op(6, "7", "prop.ab", new SharedOpArgs { a = "vehicle:ok", b = "42" }));
            Check(r, props.GetOwner("vehicle:ok") == "Bob", "abandon naming another actor is rejected");
            Check(r, WorldOps.Apply(0, "7", "sns.post", JsonUtility.ToJson(new SharedOpArgs { a = "spoof", b = "42", c = "Tom" })) != null &&
                !sns.Feed.GetRecent(10).Exists(p => p.text == "spoof"), "SNS post under another id is rejected");
            Check(r, WorldOps.Apply(0, "7", "report.sub", JsonUtility.ToJson(new SharedOpArgs { a = "42", b = "ev", c = "42", d = "0" })) != null,
                "report under another reporter id is rejected");

            // Security H2: land has an owner; only the owner registers; refunds use the plot price.
            Feed(session, Op(7, "7", "land.buy", new SharedOpArgs { a = "plotB" }));
            Check(r, land.GetOwner("plotB") == "7" && land.GetState("plotB") == LandPlotState.Owned, "land.buy sets the owner from `from`");
            Feed(session, Op(8, "42", "land.reg", new SharedOpArgs { a = "plotB", b = "Stolen Co", c = "ST" }));
            Check(r, land.GetState("plotB") == LandPlotState.Owned, "land.reg by a non-owner is rejected");
            Feed(session, Op(9, "7", "land.reg", new SharedOpArgs { a = "plotB", b = "Seven Co", c = "SC" }));
            Check(r, land.GetState("plotB") == LandPlotState.Registered, "land.reg by the owner works");
            Check(r, !land.RefundPending("plotB"), "no refund without a pending payment of this client");

            // M7: a purchase whose op cannot be sent is refunded and fails; a lost race refunds the plot price.
            bank.RestoreBalance(100000);
            var plotC = MakePlot(holder, "plotC", 50000);
            Check(r, land.TryPurchase(plotC) == LandPlotResult.SendFailed && bank.Money == 100000, "unsent land purchase is refunded and reported");
            var plotD = MakePlot(holder, "plotD", 40000);
            var realSender = SharedWriteGate.Sender;
            SharedWriteGate.Sender = (k, j) => true;
            Check(r, land.TryPurchase(plotD) == LandPlotResult.Ok && bank.Money == 60000, "sent land purchase is charged");
            SharedWriteGate.Sender = realSender;
            Feed(session, Op(10, "7", "land.buy", new SharedOpArgs { a = "plotD" }));
            Feed(session, Op(11, "42", "land.buy", new SharedOpArgs { a = "plotD", n = 999999999L }));
            Check(r, bank.Money == 100000 && land.GetOwner("plotD") == "7", "lost race refunds the plot price, not the op's n");
            Check(r, sns.Post("x") == SnsPostResult.OnCooldown, "SNS post honours a failed send");

            // M6: a snapshot older than the ops already applied is ignored.
            Feed(session, new NetMsg { t = "state", snap = roomSnap });
            Check(r, props.GetOwner("vehicle:ok") == "Bob" && session.LastOpSeq == 11, "older snapshot does not undo applied ops");

            // M4: a visitor leaving gets its own world back and does not overwrite its file.
            string before = File.ReadAllText(WorldSave.Path);
            session.Leave();
            Check(r, GameSession.Mode == SessionMode.Host && props.GetOwner("qa:room") == null && props.GetOwner("qa:own") == "Tom",
                "visitor leave restores the own world");
            Check(r, File.ReadAllText(WorldSave.Path) == before && before == ownFile && WorldSave.WorldId == ownId,
                "visitor leave leaves world.json untouched");

            // Creating a room: numbering restarts, the first op counts.
            Feed(session, Joined("42", ""));
            Feed(session, Op(1, "42", "prop.reg", new SharedOpArgs { a = "vehicle:mine", b = "42" }));
            Check(r, props.GetOwner("vehicle:mine") == "Tom", "own op in a newly created room is applied");

            // H3 / M5: handover lock.
            Feed(session, new NetMsg { t = "host", host = "7", snap = "" });
            Check(r, WorldSave.HandedOver, "losing the host role locks the world");
            session.Leave();
            Check(r, WorldSave.HandedOver, "leaving the room does not undo the handover lock");
            Feed(session, Joined("7", ""));
            Feed(session, new NetMsg { t = "host", host = "42", snap = "" });
            Check(r, !WorldSave.HandedOver, "the server giving the room back undoes the lock");
            Feed(session, new NetMsg { t = "host", host = "7", snap = "" });
            bool locked = WorldSave.TryReadLocked(out string lockedId, out int lockedGen);
            Check(r, WorldSave.HandedOver && locked, "locked again");
            Feed(session, new NetMsg { t = "msg", from = "7", data = Ack("other", lockedGen + 1) });
            Check(r, WorldSave.HandedOver, "ack for another world is ignored");
            Feed(session, new NetMsg { t = "msg", from = "7", data = Ack(lockedId, lockedGen - 1) });
            Check(r, WorldSave.HandedOver, "ack with an older generation is ignored");
            Feed(session, new NetMsg { t = "msg", from = "7", data = Ack(lockedId, lockedGen + 1) });
            Check(r, !WorldSave.HandedOver && !File.Exists(WorldSave.Path), "matching ack deletes the locked world");
            session.Leave();

            // M8: another account on this PC starts fresh and keeps nothing of the previous one.
            bank.RestoreBalance(555);
            Feed(session, new NetMsg { t = "hello", id = "43", name = "Ann" });
            Check(r, GameSession.LocalPlayerId == "43" && bank.Money == 100000, "second account starts with the fresh bank");
            Check(r, File.Exists(PersonalSave.PathFor("42")) && File.ReadAllText(PersonalSave.PathFor("42")).Contains("555"),
                "first account's data kept in its own file");
        }
        finally
        {
            SharedWriteGate.Sender = null; SharedWriteGate.InRoomCheck = null; SharedWriteGate.NameResolver = null;
            typeof(NetWorldBridge).GetMethod("OnDestroy", Any).Invoke(bridge, null);
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
