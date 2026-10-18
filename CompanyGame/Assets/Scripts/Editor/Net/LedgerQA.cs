using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using QaPeer = SyncLayerQA.QaPeer;
using QaMsg = SyncLayerQA.QaMsg;

/// <summary>
/// QA of the server's personal ledger (Docs/Multiplayer_Redesign.md 12.10.1), on the EconomySyncQA helpers.
///   -executeMethod LedgerQA.Run         in memory (with -quit): GameServer.CharacterSaveFilter, and StockExchange trades on
///                                       another wallet and portfolio
///   -executeMethod LedgerQA.RunPlay     play mode (WITHOUT -quit): in-app server + the Unity owner + a fake TCP player
///   -executeMethod LedgerQA.RunProcess  play mode (WITHOUT -quit): a built server process + the Unity client
/// Env QA_SHOTS = folder for L_*.png (needs graphics).
/// </summary>
public static class LedgerQA
{
    public static void Run() => EconomySyncQA.LedgerMemory();
    public static void RunPlay() => EconomySyncQA.StartPlay("ledger-play");
    public static void RunProcess() => EconomySyncQA.BuildAndPlay("LedgerQA", "ledger-process");
}

public static partial class EconomySyncQA
{
    const string LedgerFake = "qa-ledger-fake";
    const long PreLedgerBank = 777777; // under a plot's price (LandPlot.DefaultPrice)

    // ================= in memory (LedgerQA.Run) =================

    internal static void LedgerMemory()
    {
        tag = "LedgerQA";
        Begin();
        root = Path.Combine(Path.GetTempPath(), "LedgerQA-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            SaveFilter();
            OtherWallet();
            Report("RESULT passed");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    static void SaveFilter()
    {
        Section("character save filter");
        string id = WorldStore.NewId();
        StartServer(id);
        var a = Join("acc-a", "QA가", "m1");
        Clear(a);
        a.Send(SyncProtocol.CharacterSave(Blob(1, 2, 3)));
        Step(a);
        Check(Same(server.World.Find("acc-a").content, Blob(1, 2, 3)), "without a filter the content is kept as it comes");
        string seen = null;
        server.CharacterSaveFilter = (account, content) => { seen = account; return content.Length == 1 ? null : Blob(9, content[0]); };
        a.Send(SyncProtocol.CharacterSave(Blob(4, 5)));
        Step(a);
        Check(seen == "acc-a" && Same(server.World.Find("acc-a").content, Blob(9, 4)), "the filter gets (account, content) and what it returns is kept");
        a.Send(SyncProtocol.CharacterSave(Blob(6)));
        Step(a);
        Check(Same(server.World.Find("acc-a").content, Blob(9, 4)), "a filter returning null keeps the saved content");
        server.CharacterSaveFilter = (account, content) => throw new Exception("qa");
        a.Send(SyncProtocol.CharacterSave(Blob(7, 7)));
        Step(a);
        Check(Same(server.World.Find("acc-a").content, Blob(9, 4)) && a.client.IsOpen, "a filter that throws keeps it too, and the connection stays");
        server.Stop();
        StartServer(id);
        Check(Same(server.World.Find("acc-a").content, Blob(9, 4)), "world.dat has the filtered content");
        server.Stop();
    }

    static void OtherWallet()
    {
        Section("trades on another wallet and portfolio");
        var settings = new StockMarketSettings { alwaysOpenForTesting = true };
        var own = new QaWallet(1000000);
        var exchange = new StockExchange(settings, own, null, new System.Random(1));
        int events = 0;
        exchange.TradeExecuted += _ => events++;
        string stock = settings.listings[0].stockId;
        var other = new QaWallet(1000000);
        var book = new StockPortfolio();
        long gross = exchange.GetPrice(stock) * 3, total = gross + (long)Math.Ceiling(gross * (double)settings.tradeFeeRate);
        var receipt = exchange.Buy(default, stock, 3, other, book);
        Check(receipt.result == TradeResult.Success && receipt.total == total && other.Balance == 1000000 - total && book.GetQuantity(stock) == 3,
            "a buy on another wallet: price x 3 + fee (" + total + ") from it, 3 shares in its portfolio");
        Check(own.Balance == 1000000 && exchange.Portfolio.holdings.Count == 0 && events == 0, "the exchange's own wallet and portfolio are untouched, no TradeExecuted");
        receipt = exchange.Sell(default, stock, 2, other, book);
        Check(receipt.result == TradeResult.Success && book.GetQuantity(stock) == 1 && other.Balance == 1000000 - total + receipt.total && own.Balance == 1000000 && events == 0,
            "a sell on it too (" + receipt.total + " back)");
        Check(exchange.Buy(default, stock, 1, new QaWallet(1), new StockPortfolio()).result == TradeResult.NotEnoughMoney
              && exchange.Sell(default, stock, 5, other, book).result == TradeResult.NotEnoughShares, "not enough money / shares on the other wallet and portfolio");
        receipt = exchange.Buy(default, stock, 1);
        Check(receipt.result == TradeResult.Success && own.Balance == 1000000 - receipt.total && exchange.Portfolio.GetQuantity(stock) == 1 && events == 1,
            "the old Buy still uses the exchange's own wallet and portfolio and raises TradeExecuted");
    }

    sealed class QaWallet : IWallet
    {
        public long Balance { get; private set; }
        public QaWallet(long balance) { Balance = balance; }
        public bool CanAfford(long amount) => amount > 0 && Balance >= amount;
        public bool TrySpend(long amount, MoneyChangeReason reason) { if (!CanAfford(amount)) return false; Balance -= amount; return true; }
        public bool AddMoney(long amount, MoneyChangeReason reason) { if (amount <= 0) return false; Balance += amount; return true; }
    }

    // ================= in-app server (LedgerQA.RunPlay) =================

    static async Task LedgerPlayBody()
    {
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy && WorldSave.Baseline != null, 15, "title");
        ProfileSlot.Set(Slot);
        var client = SyncClient.Instance;

        Section("a character saved before 4d");
        var settings = new ServerSettings { name = "LedgerQA", maxPlayers = 4, port = 0, world = WorldStore.NewId() };
        var store = WorldStore.Open(ServerSettings.WorldsFolder, settings.world);
        playWorld = store.Folder;
        Check(store.Create(LedgerFake, "QA장부가짜", "female", out var made) == CharacterResult.Ok, "the fake's character in a world file");
        made.content = CharacterSync.Encode(new CharacterSync.Saved { map = "qa-map", personal = JsonUtility.ToJson(new PersonalSaveData { bankBalance = PreLedgerBank }) });
        store.Save();
        using (var r = new BinaryReader(File.OpenRead(Path.Combine(store.Folder, WorldStore.FileName)))) Check(r.ReadUInt16() == 3, "world.dat version 3 with the fake's bank " + PreLedgerBank + " (as 4c saved it)");

        var host = SyncHost.StartInApp(settings, true);
        await WaitFor(() => client.Connected && client.CharacterKnown, 10, "the Unity client on the in-app server");
        Check(await TitleFlow.CreateServerCharacter("QA장부", "male") == null, "the Unity owner made its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        string map = SceneLoadManager.CurrentMap.path, me = GameSession.LocalPlayerId;
        var ledger = ServerWorld.Instance.Ledger;
        var bank = BankManager.Instance;
        var market = StockMarketManager.Instance;
        var exchange = market.Exchange;
        var fake = await JoinFake(host.Port, map, LedgerFake, "QA장부가짜");
        Check(ledger.Book(LedgerFake).bankBalance == PreLedgerBank, "the fake's ledger starts at the bank its saved content had (" + PreLedgerBank + ")");

        Section("a forged save");
        var forged = CharacterSync.Encode(new CharacterSync.Saved { map = map, position = new Vector3(1f, 2f, 3f), personal = JsonUtility.ToJson(new PersonalSaveData { bankBalance = 999999999 }), inventory = "" });
        fake.Send(SyncProtocol.CharacterSave(forged));
        await WaitFor(() => SavedOf(LedgerFake).position.x == 1f, 5, "the fake's save on the server");
        var saved = SavedOf(LedgerFake);
        Check(BankOf(saved) == PreLedgerBank && saved.map == map && saved.position == new Vector3(1f, 2f, 3f),
            "the fake uploaded bank 999,999,999: the saved bank is the ledger's (" + BankOf(saved) + "), its map and position are kept");
        fake.Send(SyncProtocol.CharacterSave(Blob(1, 2, 3)));
        await Awaitable.WaitForSecondsAsync(.3f);
        Check(BankOf(SavedOf(LedgerFake)) == PreLedgerBank && SavedOf(LedgerFake).position.x == 1f, "an unreadable save is dropped; the saved content stays");

        Section("the fake's stock trades");
        await ToTime(host, 10 * 60 + 30); // market hours
        string stock = Cheapest(exchange);
        long price = exchange.GetPrice(stock), before = ledger.Book(LedgerFake).bankBalance;
        long ownerBank = bank.Money;
        string ownerStocks = JsonUtility.ToJson(exchange.CapturePortfolio());
        int ownerLedgers = WorldStateSync.Ledgers;
        var a = await LedgerOp(fake, "p.stock.buy", new SharedOpArgs { a = stock, n = before / price + 1 });
        Check(a.error == "은행 잔액이 부족합니다." && ledger.Book(LedgerFake).bankBalance == before && ledger.Book(LedgerFake).portfolio.GetQuantity(stock) == 0,
            "a buy of " + (before / price + 1) + " (over the ledger's " + before + ") fails and changes nothing");
        long cost = Cost(exchange, stock, 2);
        a = await LedgerOp(fake, "p.stock.buy", new SharedOpArgs { a = stock, n = 2 });
        var book = ledger.Book(LedgerFake);
        Check(a.error == "" && book.bankBalance == before - cost && book.portfolio.GetQuantity(stock) == 2,
            "a buy of 2: the ledger pays price x 2 + fee at the server's price (" + cost + ") and holds 2 shares");
        Check(a.ledgerFirst && a.bank == book.bankBalance && a.stocks == JsonUtility.ToJson(book.portfolio) && a.done == 2,
            "the fake got its Ledger (requests 2, bank " + a.bank + ", stocks) before the OpResult");
        a = await LedgerOp(fake, "p.stock.sell", new SharedOpArgs { a = stock, n = 1 });
        Check(a.error == "" && ledger.Book(LedgerFake).portfolio.GetQuantity(stock) == 1 && a.bank == ledger.Book(LedgerFake).bankBalance && a.bank > before - cost, "a sell of 1 pays back on the ledger");
        Check(WorldStateSync.Ledgers == ownerLedgers && bank.Money == ownerBank && JsonUtility.ToJson(exchange.CapturePortfolio()) == ownerStocks,
            "the Unity owner got none of the fake's Ledgers, and its screen bank and stocks did not change");

        Section("land paid by the ledger");
        long left = ledger.Book(LedgerFake).bankBalance;
        a = await LedgerOp(fake, "land.buy", new SharedOpArgs { a = "qa-ledger-plot" });
        Check(a.error == "은행 잔액이 부족합니다." && LandPlotManager.Instance.GetState("qa-ledger-plot") == LandPlotState.Available && ledger.Book(LedgerFake).bankBalance == left,
            "land.buy over the ledger's balance (" + left + " < " + LandPlot.DefaultPrice + "): no plot, nothing charged");
        ledger.Book(LedgerFake).bankBalance += LandPlot.DefaultPrice; // money on the server's ledger itself (no request gives money on trust since 12.10.6)
        ledger.Store(LedgerFake);
        Check(ledger.Book(LedgerFake).bankBalance == left + LandPlot.DefaultPrice, "the fake's ledger gets the plot's price");
        a = await LedgerOp(fake, "land.buy", new SharedOpArgs { a = "qa-ledger-plot" });
        Check(a.error == "" && LandPlotManager.Instance.GetOwner("qa-ledger-plot") == LedgerFake && ledger.Book(LedgerFake).bankBalance == left && a.bank == left,
            "with the money the plot is the fake's and its price came off the ledger");
        a = await LedgerOp(fake, "land.buy", new SharedOpArgs { a = "qa-ledger-plot" });
        Check(a.error == "이미 다른 플레이어가 구매한 땅입니다." && ledger.Book(LedgerFake).bankBalance == left, "a plot already taken is not charged");

        Section("refused personal requests");
        long now = ledger.Book(LedgerFake).bankBalance;
        a = await LedgerOp(fake, "p.fine", new SharedOpArgs { n = SpeedCamera.MaxFine + 1 });
        Check(a.error != "" && ledger.Book(LedgerFake).bankBalance == now, "p.fine over the highest fine (" + SpeedCamera.MaxFine + ") is refused: " + a.error);
        a = await LedgerOp(fake, "p.fine", new SharedOpArgs { n = 30000 });
        Check(a.error == "" && ledger.Book(LedgerFake).bankBalance == now - 30000, "p.fine 30,000 is taken");
        now -= 30000;
        a = await LedgerOp(fake, "p.shop.buy", new SharedOpArgs { a = "qa-no-such-item" });
        Check(a.error == "알 수 없는 상품입니다." && ledger.Book(LedgerFake).bankBalance == now, "p.shop.buy of an unknown item is refused");
        a = await LedgerOp(fake, "p.shop.buy", new SharedOpArgs { a = "item_water" });
        Check(a.error == "" && ledger.Book(LedgerFake).bankBalance == now - ShopApplication.DefaultPrice("item_water"), "p.shop.buy item_water costs the server's price (" + ShopApplication.DefaultPrice("item_water") + ")");
        now = ledger.Book(LedgerFake).bankBalance;
        foreach (string reason in new[] { "book.royalty", "library.fine", "deposit" })
        {
            var gone = await FakeOp(fake, "p.bank", new SharedOpArgs { n = reason == "library.fine" ? -1000 : 1000, a = reason }); // not a personal kind any more: no Ledger
            Check(gone.error == "처리할 수 없는 요청입니다." && ledger.Book(LedgerFake).bankBalance == now, "p.bank " + reason + " is gone (12.10.6: no money on trust)");
        }
        a = await LedgerOp(fake, "p.night.select", new SharedOpArgs { n = (long)NightActivityKind.Contract });
        Check(a.error != "", "살인 청부 cannot be picked before it is unlocked: " + a.error);
        Check(WorldStateSync.Ledgers == ownerLedgers && bank.Money == ownerBank, "still nothing of the fake's reached the Unity owner");

        Section("night activity");
        a = await LedgerOp(fake, "p.night.select", new SharedOpArgs { n = (long)NightActivityKind.NightJob });
        Check(a.error == "", "the fake picks 야간 알바");
        int results = WorldStateSync.Results;
        Check(NightActivity.Select(NightActivityKind.NightJob), "the Unity owner picks 야간 알바");
        await WaitFor(() => WorldStateSync.Results > results, 5, "the answer to the owner's pick");
        long fakeBefore = ledger.Book(LedgerFake).bankBalance, mine = bank.Money;
        int changes = 0;
        long changed = 0;
        Action<long, long, MoneyChangeReason> watch = (balance, delta, reason) => { changes++; changed += delta; };
        bank.MoneyChanged += watch;
        try
        {
            Drain(fake);
            await PassNight(host, () => ledger.Book(LedgerFake).bankBalance == fakeBefore + NightActivity.NightJobPay);
            var night = await WaitMsg(fake, m => IsLedger(m), "the fake's Ledger after the night");
            Check(ReadLedger(night.blob).bank == fakeBefore + NightActivity.NightJobPay, "a night of 야간 알바: the fake's ledger +" + NightActivity.NightJobPay + " and its Ledger says so");
            await WaitFor(() => bank.Money == mine + NightActivity.NightJobPay, 5, "the owner's pay");
            await Awaitable.WaitForSecondsAsync(1f);
            Check(changes == 1 && changed == NightActivity.NightJobPay && bank.Money == mine + NightActivity.NightJobPay && ledger.Book(me).bankBalance == bank.Money,
                "the owner's money changed once (" + changes + " change, +" + changed + "), from its ledger");
        }
        finally { bank.MoneyChanged -= watch; }
        a = await LedgerOp(fake, "p.night.select", new SharedOpArgs { n = (long)NightActivityKind.Sleep });
        results = WorldStateSync.Results;
        NightActivity.Select(NightActivityKind.Sleep);
        await WaitFor(() => WorldStateSync.Results > results, 5, "the answer to the owner's pick");
        fakeBefore = ledger.Book(LedgerFake).bankBalance;
        mine = bank.Money;
        Drain(fake);
        int applied = 0;
        Action<NightActivityKind> count = k => applied++;
        NightActivity.Applied += count;
        try { await PassNight(host, () => applied == 1); }
        finally { NightActivity.Applied -= count; }
        await Awaitable.WaitForSecondsAsync(1f);
        Check(ledger.Book(LedgerFake).bankBalance == fakeBefore && bank.Money == mine && !fake.Has(m => IsLedger(m)), "a night of 잠자기: both ledgers unchanged, no Ledger");

        Section("the Unity owner's trades");
        await ToTime(host, 10 * 60 + 30);
        int overwrites = WorldStateSync.Overwrites, ledgers = WorldStateSync.Ledgers;
        long m0 = bank.Money, q0 = exchange.Portfolio.GetQuantity(stock);
        var receipt = market.Buy(stock, 1);
        Check(receipt.result == TradeResult.Success && bank.Money == m0 - receipt.total && exchange.Portfolio.GetQuantity(stock) == q0 + 1,
            "the owner's buy shows in the same frame (bank " + bank.Money + ", " + (q0 + 1) + " shares)");
        await WaitFor(() => WorldStateSync.Ledgers > ledgers, 5, "the owner's Ledger");
        await Awaitable.NextFrameAsync();
        Check(WorldStateSync.Overwrites == overwrites && bank.Money == m0 - receipt.total && ledger.Book(me).bankBalance == bank.Money
              && JsonUtility.ToJson(ledger.Book(me).portfolio) == JsonUtility.ToJson(exchange.CapturePortfolio()),
            "after its Ledger nothing was written over (0 times): the ledger equals the screen");
        ledgers = WorldStateSync.Ledgers;
        receipt = market.Sell(stock, 1);
        await WaitFor(() => WorldStateSync.Ledgers > ledgers, 5, "the owner's Ledger");
        await Awaitable.NextFrameAsync();
        Check(receipt.result == TradeResult.Success && WorldStateSync.Overwrites == overwrites && ledger.Book(me).bankBalance == bank.Money, "a sell too: 0 overwrites");

        Section("a changed bank goes back");
        long real = bank.Money;
        string realStocks = JsonUtility.ToJson(exchange.CapturePortfolio());
        bank.RestoreBalance(real + 5000000);
        exchange.Portfolio.AddShares(stock, 100, 0);
        ledgers = WorldStateSync.Ledgers;
        NightActivity.Select(NightActivityKind.Sleep); // any counted request brings a Ledger
        await WaitFor(() => WorldStateSync.Ledgers > ledgers, 5, "the next Ledger");
        await Awaitable.NextFrameAsync();
        Check(WorldStateSync.Overwrites == overwrites + 1 && bank.Money == real && JsonUtility.ToJson(exchange.CapturePortfolio()) == realStocks,
            "the bank changed on this PC (+5,000,000) and 100 made-up shares are written back to the ledger's");

        Section("saved in world.dat");
        long fakeBank = ledger.Book(LedgerFake).bankBalance;
        host.Stop();
        await WaitFor(() => !client.Connected && !CharacterSync.Active, 5, "leaving the server");
        store = WorldStore.Open(ServerSettings.WorldsFolder, settings.world);
        Check(Read(store, LedgerFake, out var fakeSaved) && BankOf(fakeSaved) == fakeBank && fakeSaved.position.x == 1f, "the fake's saved bank is its ledger (" + fakeBank + ")");
        Check(Read(store, me, out var mySaved) && BankOf(mySaved) == real, "the owner's saved bank is its ledger (" + real + ")");
    }

    // ================= server process (LedgerQA.RunProcess) =================

    static async Task LedgerProcessBody(string exe)
    {
        exePath = exe;
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy && WorldSave.Baseline != null, 15, "title");
        ProfileSlot.Set(Slot);
        var client = SyncClient.Instance;
        serverFolder = Path.Combine(Path.GetDirectoryName(exe), "qa-server");
        if (Directory.Exists(serverFolder)) Directory.Delete(serverFolder, true);
        Directory.CreateDirectory(serverFolder);
        serverPort = FreePort();
        settingsFile = Path.Combine(serverFolder, "settings.json");
        var settings = new ServerSettings { name = "LedgerQA 프로세스", maxPlayers = 4, port = serverPort, world = WorldStore.NewId() };
        File.WriteAllText(settingsFile, JsonUtility.ToJson(settings));
        string worldsRoot = Path.Combine(serverFolder, "worlds");

        // ---- run A: day 1 10:30 (market hours) ----
        Section("a buy on the remote client");
        SetWorldClock(worldsRoot, settings.world, Day + 10 * 60 + 30);
        await StartProcessServer("A");
        Check(await TitleFlow.JoinServer("127.0.0.1:" + serverPort, "", false) == null && GameSession.Mode == SessionMode.Client, "the Unity client joins the server process (Client)");
        Check(await TitleFlow.CreateServerCharacter("QA장부원격", "male") == null, "its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        await WaitFor(() => GameClock.Current.Now.hour == 10, 10, "the server's clock");
        string me = GameSession.LocalPlayerId;
        var bank = BankManager.Instance;
        var exchange = StockMarketManager.Instance.Exchange;
        string stock = Cheapest(exchange);
        int overwrites = WorldStateSync.Overwrites, ledgers = WorldStateSync.Ledgers;
        long m0 = bank.Money, q0 = exchange.Portfolio.GetQuantity(stock);
        PhoneManager.Instance.OpenPhone();
        PhoneManager.Instance.OpenApp(nameof(StockApplication));
        await Awaitable.WaitForSecondsAsync(.5f);
        var view = Object.FindAnyObjectByType<StockScreenView>();
        Check(view, "the stock screen is open");
        string stockName = exchange.FindListing(stock).displayName;
        typeof(StockScreenView).GetMethod("Trade", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, new object[] { stock, stockName, true });
        long paid = m0 - bank.Money;
        Check(paid > 0 && exchange.Portfolio.GetQuantity(stock) == q0 + 1, "the buy button: the bank (-" + paid + ") and the shares change in the same frame, before any answer");
        await Shot("L_remote_buy.png");
        await WaitFor(() => WorldStateSync.Ledgers > ledgers, 5, "the server's Ledger");
        await Awaitable.NextFrameAsync();
        Check(WorldStateSync.Overwrites == overwrites && bank.Money == m0 - paid && exchange.Portfolio.GetQuantity(stock) == q0 + 1, "the server's Ledger agreed: 0 overwrites");
        ledgers = WorldStateSync.Ledgers;
        Check(StockMarketManager.Instance.Buy(stock, 1).result == TradeResult.Success, "a second buy");
        await WaitFor(() => WorldStateSync.Ledgers > ledgers, 5, "the server's Ledger");
        await Awaitable.NextFrameAsync();
        Check(WorldStateSync.Overwrites == overwrites, "still 0 overwrites in normal trading");
        PhoneManager.Instance.ClosePhone();

        Section("a changed bank goes back (remote)");
        long real = bank.Money;
        bank.RestoreBalance(real + 3000000);
        await BankShot("L_bank_changed.png");
        ledgers = WorldStateSync.Ledgers;
        NightActivity.Select(NightActivityKind.Sleep); // a counted request
        await WaitFor(() => WorldStateSync.Ledgers > ledgers, 5, "the next Ledger");
        await Awaitable.NextFrameAsync();
        Check(bank.Money == real && WorldStateSync.Overwrites == overwrites + 1, "the bank changed on this PC (+3,000,000) is written back to the ledger's " + real + " (1 overwrite)");
        await BankShot("L_bank_restored.png");
        string stocks = JsonUtility.ToJson(exchange.CapturePortfolio());
        await LeaveRemote(client);
        await StopProcessServer();
        var store = WorldStore.Open(worldsRoot, settings.world);
        Check(Read(store, me, out var saved) && BankOf(saved) == real && PortfolioOf(saved) == stocks,
            "the server's world.dat (after a restart) has the bank and stocks the Unity client showed (" + real + ")");

        // ---- run B: day 2 01:57, the night 2-5 ----
        Section("joining again and a night");
        SetWorldClock(worldsRoot, settings.world, 2 * Day + 1 * 60 + 57);
        await StartProcessServer("B");
        await Rejoin(client);
        Check(bank.Money == real && JsonUtility.ToJson(exchange.CapturePortfolio()) == stocks, "joining again brings the ledger's bank and stocks");
        overwrites = WorldStateSync.Overwrites;
        int results = WorldStateSync.Results;
        Check(NightActivity.Select(NightActivityKind.NightJob), "the QA picks 야간 알바");
        await WaitFor(() => WorldStateSync.Results > results, 5, "the answer to the pick");
        long cash = bank.Money;
        int nights = WorldStateSync.NightsApplied;
        await WaitFor(() => GameClock.Current.Now.hour == 5, 40, "the server's clock over the night 2-5");
        await WaitFor(() => bank.Money != cash, 10, "the night pay");
        await Awaitable.WaitForSecondsAsync(6f);
        Check(bank.Money == cash + NightActivity.NightJobPay && WorldStateSync.NightsApplied == nights + 1,
            "the night was applied once: money +" + (bank.Money - cash) + " (from the server's Ledger, not this PC)");
        Check(WorldStateSync.Overwrites == overwrites + 1, "the server's pay is the one ledger write of the night");
        results = WorldStateSync.Results;
        NightActivity.Select(NightActivityKind.Sleep);
        await WaitFor(() => WorldStateSync.Results > results, 5, "the answer to the pick");
        long after = bank.Money;
        await LeaveRemote(client);
        await StopProcessServer();
        store = WorldStore.Open(worldsRoot, settings.world);
        Check(Read(store, me, out saved) && BankOf(saved) == after, "after the night the server's saved bank equals the Unity bank (" + after + ")");
    }

    // ================= helpers =================

    struct LedgerAnswer { public string error, stocks; public bool ledgerFirst; public long bank; public uint done; }

    static bool IsLedger(QaMsg m) => m.kind == ToClient.Event && m.id == worldId && m.eventId == WorldSection.LedgerEvent && m.sender == 0;

    static LedgerAnswer ReadLedger(byte[] blob)
    {
        using var r = SyncProtocol.Reader(blob);
        var a = new LedgerAnswer { done = r.ReadUInt32(), bank = r.ReadInt64() };
        a.stocks = SectionJson(SyncProtocol.ReadServerBlob(r));
        return a;
    }

    // A fake's request; its OpResult, and the Ledger that came before it.
    static async Task<LedgerAnswer> LedgerOp(QaPeer fake, string kind, SharedOpArgs args)
    {
        Drain(fake);
        fake.Send(OpFrame(kind, args));
        float end = Time.realtimeSinceStartup + 5f;
        while (true)
        {
            fake.Pump();
            int at = fake.inbox.FindIndex(m => m.kind == ToClient.Event && m.id == worldId && m.eventId == WorldSection.OpResultEvent && m.sender == 0);
            if (at >= 0)
            {
                var answer = new LedgerAnswer();
                for (int i = 0; i < at; i++)
                    if (IsLedger(fake.inbox[i])) { var l = ReadLedger(fake.inbox[i].blob); l.ledgerFirst = true; answer = l; }
                using (var r = SyncProtocol.Reader(fake.inbox[at].blob))
                {
                    Check(SyncProtocol.ReadStr(r) == kind, "OpResult for " + kind);
                    answer.error = SyncProtocol.ReadStr(r);
                }
                Check(answer.ledgerFirst, "a Ledger came before the OpResult of " + kind);
                fake.inbox.RemoveRange(0, at + 1);
                return answer;
            }
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for the answer to " + kind);
            await Awaitable.NextFrameAsync();
        }
    }

    // The phone's bank app (통장 잔액) on the screen.
    static async Task BankShot(string file)
    {
        PhoneManager.Instance.OpenPhone();
        PhoneManager.Instance.OpenApp(nameof(BankApplication));
        await Shot(file, 1f);
        PhoneManager.Instance.ClosePhone();
    }

    static string Cheapest(StockExchange exchange)
    {
        var quotes = exchange.GetQuotes();
        quotes.Sort((x, y) => x.price.CompareTo(y.price));
        return quotes[0].stockId;
    }

    // What the server's exchange charges now for quantity shares (a dry run on a scratch wallet).
    static long Cost(StockExchange exchange, string stock, int quantity) =>
        exchange.Buy(GameClock.Current.Now, stock, quantity, new QaWallet(long.MaxValue / 2), new StockPortfolio()).total;

    // In-app: the server's clock to the next minuteOfDay.
    static async Task ToTime(SyncHost host, double minuteOfDay)
    {
        double now = host.Server.World.WorldMinutes, target = Math.Floor(now / Day) * Day + minuteOfDay;
        if (target <= now) target += Day;
        host.Server.AdvanceClock((long)((target - now) * GameServer.RealSecondsPerGameHour * 1000 / 60));
        await WaitFor(() => GameClock.Current.Now.TotalMinutes >= (long)target, 30, "the clock at " + Clock(target));
        await Awaitable.WaitForSecondsAsync(1.2f); // the sections after the price ticks
    }

    static CharacterSync.Saved SavedOf(string account)
    {
        CharacterSync.Read(ServerWorld.Instance ? SyncHost.Instance.Server.World.Find(account)?.content : null, out var saved);
        return saved;
    }

    static bool Read(WorldStore store, string account, out CharacterSync.Saved saved)
    {
        saved = default;
        return store.Find(account) is WorldStore.Character c && CharacterSync.Read(c.content, out saved);
    }

    static long BankOf(CharacterSync.Saved saved) => string.IsNullOrEmpty(saved.personal) ? -1 : JsonUtility.FromJson<PersonalSaveData>(saved.personal).bankBalance;
    static string PortfolioOf(CharacterSync.Saved saved) => JsonUtility.ToJson(JsonUtility.FromJson<PersonalSaveData>(saved.personal).portfolio);
}
