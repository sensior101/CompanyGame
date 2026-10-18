using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The server's personal ledger (Docs/Multiplayer_Redesign.md 12.10.1, 12.10.4): the bank money, stock holdings and
/// inventory of every character on this server, computed here and saved in the character's content. Made and ended by ServerWorld.
///   start    an account's book and bag are read from its saved content the first time they are needed (a new
///            character: the baseline, PersonalSave.BaselinePersonal and BaselineInventory); every change is written
///            back into the content at once (map and position stay), so each world.dat save has it
///   save     GameServer.CharacterSaveFilter puts the book and the bag in place of the personal part and the inventory a
///            client uploads; unreadable content is dropped and the saved one kept. Book pages never stay in the content
///            nor the bag (12.10.3): ServerBooks keeps them
///   p.*      personal requests (Op event 1) are decided on the book and the bag with the server's prices and the
///            TradeCatalog; land.buy is paid here. book.new changes the bag too (a blank is written on), so it is counted
///   layout   p.inv.layout is taken only with the same contents (InventoryLedgerRules.SameContents). Nothing is taken on
///            trust any more (p.bank and p.inv.trust are gone with the library's move to the server, 12.10.6)
///   given    a character entering (GameServer.CharacterEntering, before its Character goes out) gets the start vehicles
///            it lacks, and once per account a goshiwon deed of a room no other account was given or held (12.10.5);
///            p.grant gives one item of the TradeCatalog's grant list while the bag has none (the employee card)
///   answer   Ledger (event 3, to the requester only, before its OpResult): u32 counted requests done on that
///            connection, i64 bank money, server blob = gzip(portfolio JSON), server blob = gzip(bag snapshot, no pages)
///   night    on GameClock.NightSkipped the money part of each connection's night activity goes on its book, and that
///            connection gets a Ledger
/// The screen managers (BankManager.Instance, the exchange's wallet and portfolio) are never touched: on an in-app
/// server the owner's own screen lives in this very process.
/// </summary>
public sealed class ServerLedger
{
    /// <summary>The personal kinds (12.10.1, 12.10.4, 12.10.5).</summary>
    public static readonly HashSet<string> Kinds = new HashSet<string>
    {
        "p.stock.buy", "p.stock.sell", "p.shop.buy", "p.fine", "p.night.select",
        "p.trade", "p.cash.withdraw", "p.cash.deposit", "p.eat", "p.bag", "p.inv.layout", "p.grant"
    };

    const string NoMoney = "은행 잔액이 부족합니다.";

    /// <summary>Requests that change the book or the bag and so are counted and answered with a Ledger (also land.buy, book.new, the library's).</summary>
    public static bool Counts(string kind) => kind == "land.buy" || kind == "book.new" || Kinds.Contains(kind) || ServerDrops.Kinds.Contains(kind) || ServerLibrary.Kinds.Contains(kind);

    /// <summary>A speeding fine the bank could not pay: the library's penalty point (ServerLibrary.Penalty, 12.10.6).</summary>
    public Action<string> UnpaidFine;

    readonly GameServer server;
    readonly ulong entity;
    readonly ServerBooks bodies;
    readonly Dictionary<string, PersonalSaveData> books = new Dictionary<string, PersonalSaveData>();
    readonly Dictionary<string, InventoryState> bags = new Dictionary<string, InventoryState>();
    readonly Dictionary<uint, uint> done = new Dictionary<uint, uint>(); // per connection: its numbers are never reused
    readonly Dictionary<uint, NightActivityKind> nights = new Dictionary<uint, NightActivityKind>();
    readonly Dictionary<string, string> deeds = new Dictionary<string, string>(); // account → the goshiwon room the server gave it

    /// <summary>Ledgers sent, and layouts refused (their contents differed), for QA.</summary>
    public int Sent { get; private set; }
    public int LayoutsRefused { get; private set; }

    public ServerLedger(GameServer server, ulong entity, ServerBooks bodies, List<StartDeedSave> givenDeeds = null)
    {
        this.server = server;
        this.entity = entity;
        this.bodies = bodies;
        if (givenDeeds != null)
            foreach (var d in givenDeeds) if (d != null && !string.IsNullOrEmpty(d.account) && !string.IsNullOrEmpty(d.deed)) deeds[d.account] = d.deed;
        server.CharacterSaveFilter = Filter;
        server.CharacterEntering = Enter;
        GameClock.NightSkipped += OnNight;
        bodies.Owned = (account, id) => InventoryLedgerRules.FindUnique(Bag(account), id)?.BookData;
        bodies.FromBlank = (account, id, book) =>
        {
            if (!InventoryLedgerRules.TryWriteBlank(Bag(account), id, book, out _)) return false;
            Store(account);
            return true;
        };
    }

    public void End()
    {
        server.CharacterSaveFilter = null;
        server.CharacterEntering = null;
        GameClock.NightSkipped -= OnNight;
    }

    /// <summary>The deeds given, for the world state (WorldSaveData.startDeeds).</summary>
    public List<StartDeedSave> CaptureDeeds()
    {
        var list = new List<StartDeedSave>(deeds.Count);
        foreach (var pair in deeds) list.Add(new StartDeedSave { account = pair.Key, deed = pair.Value });
        return list;
    }

    /// <summary>The goshiwon room the server gave <paramref name="account"/> (QA), or null.</summary>
    public string DeedOf(string account) => account != null && deeds.TryGetValue(account, out var deed) ? deed : null;

    /// <summary>The book of <paramref name="account"/> (read from its content the first time). QA reads it too.</summary>
    public PersonalSaveData Book(string account)
    {
        if (books.TryGetValue(account, out var book)) return book;
        var character = server.World.Find(account);
        if (character != null && CharacterSync.Read(character.content, out var saved)) book = Parse(saved.personal);
        if (book == null && character != null && character.content != null && character.content.Length > 0)
            Debug.LogWarning("[ServerLedger] the saved money of " + account + " is unreadable; it starts from the new character state");
        book ??= Parse(PersonalSave.BaselinePersonal) ?? new PersonalSaveData();
        book.portfolio ??= new StockPortfolio();
        books[account] = book;
        return book;
    }

    static PersonalSaveData Parse(string json)
    {
        try
        {
            var data = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PersonalSaveData>(json);
            return data != null && data.version == 1 ? data : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// The bag of <paramref name="account"/> (12.10.4): its saved inventory the first time (a new character: the
    /// baseline), never with pages (pages the world's books lack go to them). QA reads it too.
    /// </summary>
    public InventoryState Bag(string account)
    {
        if (bags.TryGetValue(account, out var bag)) return bag;
        var character = server.World.Find(account);
        string json = character != null && CharacterSync.Read(character.content, out var saved) ? saved.inventory : PersonalSave.BaselineInventory;
        bag = new InventoryState();
        if (!string.IsNullOrEmpty(json) && !InventorySnapshot.Import(bag, bodies.Strip(json)))
            Debug.LogWarning("[ServerLedger] the saved inventory of " + account + " is unreadable; it starts empty");
        bags[account] = bag;
        return bag;
    }

    /// <summary>The book and the bag go into the saved content now (map and position stay). ServerDrops calls it after a change.</summary>
    public void Store(string account)
    {
        var character = server.World.Find(account);
        if (character == null) return;
        CharacterSync.Read(character.content, out var saved);
        character.content = CharacterSync.Encode(Ledgered(account, saved));
    }

    CharacterSync.Saved Ledgered(string account, CharacterSync.Saved saved)
    {
        saved.personal = JsonUtility.ToJson(Book(account)); // the client's money and stocks are never kept
        saved.inventory = InventorySnapshot.Export(Bag(account), false); // nor its items, nor book pages
        return saved;
    }

    byte[] Filter(string account, byte[] content)
    {
        if (!CharacterSync.Read(content, out var saved))
        {
            Debug.LogWarning("[ServerLedger] unreadable character save from " + account + "; the saved one is kept");
            return null;
        }
        return CharacterSync.Encode(Ledgered(account, saved));
    }

    // ---- requests ----

    /// <summary>Decides a p.* request of <paramref name="account"/> on connection <paramref name="connection"/>. The error (Korean), or null.</summary>
    public string Apply(uint connection, string account, string kind, string json)
    {
        var a = JsonUtility.FromJson<SharedOpArgs>(json) ?? new SharedOpArgs();
        var book = Book(account);
        var bag = Bag(account);
        var wallet = new LedgerWallet(book);
        string error;
        switch (kind)
        {
            case "p.trade":
                var lines = InventoryLedgerRules.ParseLines(a.a);
                var offers = TradeCatalog.Offers(a.b);
                if (offers == null) error = "알 수 없는 상점입니다.";
                else if (lines == null) error = "거래할 항목을 확인해 주세요.";
                else if (InventoryLedgerRules.TryTrade(bag, offers, lines, string.IsNullOrEmpty(a.c) ? null : a.c.Split(','), out error)) error = null;
                break;
            case "p.cash.withdraw":
            case "p.cash.deposit":
                if (!int.TryParse(a.a, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int count)) { error = "수량을 확인하세요."; break; }
                if (kind == "p.cash.withdraw" ? CashService.LedgerWithdraw(wallet, bag, a.n, count, out error) : CashService.LedgerDeposit(wallet, bag, a.n, count, out error)) error = null;
                break;
            case "p.eat":
                if (InventoryLedgerRules.TryEat(bag, a.a, out error)) error = null;
                break;
            case "p.bag":
                if (InventoryLedgerRules.TryBag(bag, out error)) error = null;
                break;
            case "p.inv.layout":
                error = Layout(account, a, bag);
                break;
            case "p.stock.buy":
            case "p.stock.sell":
                error = Trade(kind == "p.stock.buy", a, wallet, book.portfolio);
                break;
            case "p.shop.buy":
                long price = ShopApplication.DefaultPrice(a.a);
                error = price <= 0 ? "알 수 없는 상품입니다." : wallet.TrySpend(price, MoneyChangeReason.Purchase) ? null : NoMoney;
                break;
            case "p.fine":
                if (a.n <= 0 || a.n > SpeedCamera.MaxFine) { error = "벌금 금액이 올바르지 않습니다."; break; }
                if (!wallet.TrySpend(a.n, MoneyChangeReason.Tax)) UnpaidFine?.Invoke(account); // the bank cannot: a penalty point instead (12.10.6)
                error = null;
                break;
            case "p.night.select":
                var night = (NightActivityKind)a.n;
                // ponytail: 살인 청부 is refused for everyone until the unlock event exists (then: per character, saved).
                if (a.n < 0 || a.n > (long)NightActivityKind.Contract || night == NightActivityKind.Contract) return "고를 수 없는 밤 활동입니다.";
                nights[connection] = night;
                return null;
            case "p.grant":
                var given = TradeCatalog.Grant(a.a);
                if (!given) error = "지급할 수 없는 아이템입니다.";
                else if (InventoryLedgerRules.Holds(bag, given.itemId)) error = "이미 가지고 있습니다.";
                else if (bag.TryAdd(given, 1, out error)) error = null;
                break;
            default:
                return "처리할 수 없는 요청입니다.";
        }
        if (error == null) Store(account);
        return error;
    }

    string Layout(string account, SharedOpArgs a, InventoryState bag)
    {
        string json = InventoryLedgerRules.Unpack(a.b);
        if (json != null && InventoryLedgerRules.TryTakeLayout(bag, json, false, out _)) return null;
        LayoutsRefused++;
        Debug.LogWarning("[ServerLedger] p.inv.layout from " + account + " refused ("
                         + (json == null ? "unreadable" : "the contents differ") + "); its Ledger puts the client back");
        return json == null ? "배치를 읽을 수 없습니다." : "인벤토리 내용이 서버와 다릅니다.";
    }

    string Trade(bool buy, SharedOpArgs a, IWallet wallet, StockPortfolio portfolio)
    {
        var market = StockMarketManager.Instance;
        if (!market || GameClock.Current == null) return "주식 시장을 찾을 수 없습니다.";
        int quantity = a.n > int.MaxValue ? int.MaxValue : a.n < 0 ? 0 : (int)a.n;
        var now = GameClock.Current.Now;
        var receipt = buy ? market.Exchange.Buy(now, a.a, quantity, wallet, portfolio) : market.Exchange.Sell(now, a.a, quantity, wallet, portfolio);
        switch (receipt.result)
        {
            case TradeResult.Success: return null;
            case TradeResult.MarketClosed: return "지금은 장이 열려 있지 않습니다.";
            case TradeResult.NotEnoughMoney: return NoMoney;
            case TradeResult.NotEnoughShares: return "보유 수량이 부족합니다.";
            case TradeResult.QuantityTooLarge: return "한 번에 주문할 수 있는 수량을 넘었습니다.";
            case TradeResult.UnknownStock: return "알 수 없는 종목입니다.";
            default: return "수량을 확인하세요.";
        }
    }

    /// <summary>
    /// land.buy paid on the book (12.10.1): a free plot needs its price in the bank first, and is charged only when the
    /// shared purchase went through. The error, or null.
    /// </summary>
    public string BuyLand(string account, string json)
    {
        var a = JsonUtility.FromJson<SharedOpArgs>(json);
        var plots = LandPlotManager.Instance;
        var wallet = new LedgerWallet(Book(account));
        long price = LandPlot.PriceOf(a?.a);
        bool free = plots && a != null && plots.GetState(a.a) == LandPlotState.Available;
        if (free && !wallet.CanAfford(price)) return NoMoney;
        string error = WorldOps.Apply(account, "land.buy", json); // a taken plot fails here, and nothing is charged
        if (error == null && wallet.TrySpend(price, MoneyChangeReason.Purchase)) Store(account);
        return error;
    }

    /// <summary>A counted request of this connection was decided: its Ledger goes out (before the OpResult).</summary>
    public void Answered(uint connection, string account)
    {
        done.TryGetValue(connection, out uint n);
        done[connection] = n + 1;
        Send(connection, account);
    }

    /// <summary>
    /// The server changed <paramref name="account"/>'s book or bag by itself (a library recall, a royalty, 12.10.6): every
    /// connection of it that is still here gets its Ledger.
    /// </summary>
    public void Push(string account)
    {
        foreach (uint connection in new List<uint>(done.Keys))
            if (server.AccountOf(connection) == account) Send(connection, account);
    }

    /// <summary>An IWallet on <paramref name="account"/>'s bank money in this ledger (never BankManager).</summary>
    public IWallet Wallet(string account) => new LedgerWallet(Book(account));

    void Send(uint connection, string account)
    {
        var book = Book(account);
        done.TryGetValue(connection, out uint n);
        if (server.SendServerEvent(entity, 0, WorldSection.LedgerEvent, connection, ServerWorld.Bytes(w =>
            {
                w.Write(n);
                w.Write(book.bankBalance);
                SyncProtocol.WriteServerBlob(w, ServerWorld.Encode(JsonUtility.ToJson(book.portfolio)));
                SyncProtocol.WriteServerBlob(w, ServerWorld.Encode(InventorySnapshot.Export(Bag(account), false)));
            })))
            Sent++;
    }

    // ---- given on entering (12.10.5) ----

    // Before the Character goes out, so what it is given is in the content it gets (a new one gets it in its first Ledger).
    void Enter(uint connection, string account)
    {
        if (!done.ContainsKey(connection)) done[connection] = 0; // known from now on: a Push reaches it before its first request
        var character = server.World.Find(account);
        if (character == null) return;
        bool fresh = character.content == null || character.content.Length == 0;
        var bag = Bag(account);
        bool vehicles = InventoryLedgerRules.GrantStartVehicles(bag);
        string deed = GiveDeed(account, bag);
        if (!vehicles && deed == null) return;
        if (!fresh) Store(account); // a new character keeps its empty content: CharacterSync takes the first Ledger's bag
        Debug.Log("[ServerLedger] " + account + " entering:" + (vehicles ? " start vehicles" : "") + (deed != null ? " deed " + deed : ""));
    }

    // Once per account (a deed it holds already counts), a room nobody was given or held. The room given, or null.
    string GiveDeed(string account, InventoryState bag)
    {
        if (deeds.ContainsKey(account)) return null;
        for (int i = 0; i < bag.Capacity; i++)
            if (!bag.GetSlot(i).IsEmpty && InventoryLedgerRules.IsStartingDeed(bag.GetSlot(i).Item)) { deeds[account] = bag.GetSlot(i).Item.propertyId; return null; }
        var deed = InventoryLedgerRules.NextStartingDeed(d => !RoomTaken(d));
        if (!deed) { Debug.Log("[ServerLedger] no goshiwon room left for " + account); return null; }
        if (!bag.TryAdd(deed, 1, out string error)) { Debug.LogWarning("[ServerLedger] the deed for " + account + " does not fit (next time): " + error); return null; }
        deeds[account] = deed.propertyId;
        return deed.propertyId;
    }

    // Given to an account, on the registry (owned or given up), or in any character's bag.
    // ponytail: reads every character's saved inventory per new account; keep a set of held deeds if worlds grow past a few hundred characters.
    bool RoomTaken(ItemData deed)
    {
        if (deeds.ContainsValue(deed.propertyId)) return true;
        var registry = PropertyRegistry.Instance;
        if (registry && registry.CaptureState().Exists(p => p.id == deed.propertyId)) return true;
        string mark = "\"itemId\":\"" + deed.itemId + "\"";
        foreach (var c in server.World.Characters)
        {
            if (bags.TryGetValue(c.account, out var held)) { if (InventoryLedgerRules.Holds(held, deed.itemId)) return true; }
            else if (CharacterSync.Read(c.content, out var saved) && saved.inventory != null && saved.inventory.Contains(mark)) return true;
        }
        return false;
    }

    // ---- the night (9장-7) ----

    void OnNight(GameTime after)
    {
        foreach (var pair in new List<KeyValuePair<uint, NightActivityKind>>(nights))
        {
            string account = server.AccountOf(pair.Key);
            if (account == null) { nights.Remove(pair.Key); continue; } // gone: a new connection picks again
            var wallet = new LedgerWallet(Book(account));
            bool changed = pair.Value == NightActivityKind.NightJob ? wallet.AddMoney(NightActivity.NightJobPay, MoneyChangeReason.Earned)
                : pair.Value == NightActivityKind.GoOut && wallet.TrySpend(NightActivity.GoOutCost, MoneyChangeReason.Spent); // no money: sleeps
            if (!changed) continue;
            Store(account);
            Send(pair.Key, account);
        }
    }

    /// <summary>An IWallet on a book's bank money (never BankManager).</summary>
    sealed class LedgerWallet : IWallet
    {
        readonly PersonalSaveData book;
        public LedgerWallet(PersonalSaveData book) { this.book = book; }
        public long Balance => book.bankBalance;
        public bool CanAfford(long amount) => amount > 0 && book.bankBalance >= amount;

        public bool TrySpend(long amount, MoneyChangeReason reason)
        {
            if (!CanAfford(amount)) return false;
            book.bankBalance -= amount;
            return true;
        }

        public bool AddMoney(long amount, MoneyChangeReason reason)
        {
            if (amount <= 0 || book.bankBalance > long.MaxValue - amount) return false;
            book.bankBalance += amount;
            return true;
        }
    }
}
