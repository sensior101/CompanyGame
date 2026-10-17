using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The server's personal ledger (Docs/Multiplayer_Redesign.md 12.10.1): the bank money and stock holdings of every
/// character on this server, computed here and saved in the character's content. Made and ended by ServerWorld.
///   start    an account's book is read from its saved content the first time it is needed (a new character: the
///            baseline, PersonalSave.BaselinePersonal); every change is written back into the content at once (map,
///            position and inventory stay), so each world.dat save has it
///   save     GameServer.CharacterSaveFilter puts the book in place of the personal part a client uploads; unreadable
///            content is dropped and the saved one kept. Book pages never stay in the content (12.10.3): ServerBooks
///            keeps them (and takes those of a book it does not have yet)
///   p.*      personal requests (Op event 1) are decided on the book with the server's prices; land.buy is paid here
///   answer   Ledger (event 3, to the requester only, before its OpResult): u32 personal requests done on that
///            connection, i64 bank money, server blob = gzip(portfolio JSON)
///   night    on GameClock.NightSkipped the money part of each connection's night activity goes on its book, and that
///            connection gets a Ledger
/// The screen managers (BankManager.Instance, the exchange's wallet and portfolio) are never touched: on an in-app
/// server the owner's own screen lives in this very process.
/// </summary>
public sealed class ServerLedger
{
    /// <summary>The personal kinds (12.10.1). p.bank is temporary: only the reasons in <see cref="BankReasons"/>.</summary>
    public static readonly HashSet<string> Kinds = new HashSet<string> { "p.stock.buy", "p.stock.sell", "p.shop.buy", "p.fine", "p.night.select", "p.bank" };

    // reason → sign of the amount. ponytail: trusted until 4d-2 (deposit, withdraw) and 4d-4 (the library) move these paths.
    static readonly Dictionary<string, int> BankReasons = new Dictionary<string, int>
    {
        { "deposit", 1 }, { "withdraw", -1 }, { "book.publish", -1 }, { "library.late", -1 }, { "library.fine", -1 }, { "book.royalty", 1 }
    };

    const string NoMoney = "은행 잔액이 부족합니다.";

    /// <summary>Requests that change the book and so are counted and answered with a Ledger (also land.buy).</summary>
    public static bool Counts(string kind) => kind == "land.buy" || Kinds.Contains(kind);

    readonly GameServer server;
    readonly ulong entity;
    readonly ServerBooks bodies;
    readonly Dictionary<string, PersonalSaveData> books = new Dictionary<string, PersonalSaveData>();
    readonly Dictionary<uint, uint> done = new Dictionary<uint, uint>(); // per connection: its numbers are never reused
    readonly Dictionary<uint, NightActivityKind> nights = new Dictionary<uint, NightActivityKind>();

    /// <summary>Ledgers sent, for QA.</summary>
    public int Sent { get; private set; }

    public ServerLedger(GameServer server, ulong entity, ServerBooks bodies)
    {
        this.server = server;
        this.entity = entity;
        this.bodies = bodies;
        server.CharacterSaveFilter = Filter;
        GameClock.NightSkipped += OnNight;
    }

    public void End()
    {
        server.CharacterSaveFilter = null;
        GameClock.NightSkipped -= OnNight;
    }

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

    // The book goes into the saved content now (a character that never saved gets the new character's inventory).
    void Store(string account)
    {
        var character = server.World.Find(account);
        if (character == null) return;
        if (!CharacterSync.Read(character.content, out var saved)) saved = new CharacterSync.Saved { inventory = PersonalSave.BaselineInventory };
        saved.personal = JsonUtility.ToJson(Book(account));
        saved.inventory = bodies.Strip(saved.inventory);
        character.content = CharacterSync.Encode(saved);
    }

    byte[] Filter(string account, byte[] content)
    {
        if (!CharacterSync.Read(content, out var saved))
        {
            Debug.LogWarning("[ServerLedger] unreadable character save from " + account + "; the saved one is kept");
            return null;
        }
        saved.personal = JsonUtility.ToJson(Book(account)); // the client's money and stocks are never kept
        saved.inventory = bodies.Strip(saved.inventory); // nor book pages: the world's books keep them
        return CharacterSync.Encode(saved);
    }

    // ---- requests ----

    /// <summary>Decides a p.* request of <paramref name="account"/> on connection <paramref name="connection"/>. The error (Korean), or null.</summary>
    public string Apply(uint connection, string account, string kind, string json)
    {
        var a = JsonUtility.FromJson<SharedOpArgs>(json) ?? new SharedOpArgs();
        var book = Book(account);
        var wallet = new LedgerWallet(book);
        string error;
        switch (kind)
        {
            case "p.stock.buy":
            case "p.stock.sell":
                error = Trade(kind == "p.stock.buy", a, wallet, book.portfolio);
                break;
            case "p.shop.buy":
                long price = ShopApplication.DefaultPrice(a.a);
                error = price <= 0 ? "알 수 없는 상품입니다." : wallet.TrySpend(price, MoneyChangeReason.Purchase) ? null : NoMoney;
                break;
            case "p.fine":
                error = a.n <= 0 || a.n > SpeedCamera.MaxFine ? "벌금 금액이 올바르지 않습니다." : wallet.TrySpend(a.n, MoneyChangeReason.Tax) ? null : NoMoney;
                break;
            case "p.night.select":
                var night = (NightActivityKind)a.n;
                // ponytail: 살인 청부 is refused for everyone until the unlock event exists (then: per character, saved).
                if (a.n < 0 || a.n > (long)NightActivityKind.Contract || night == NightActivityKind.Contract) return "고를 수 없는 밤 활동입니다.";
                nights[connection] = night;
                return null;
            case "p.bank":
                error = Bank(account, a, wallet);
                break;
            default:
                return "처리할 수 없는 요청입니다.";
        }
        if (error == null) Store(account);
        return error;
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

    string Bank(string account, SharedOpArgs a, IWallet wallet)
    {
        Debug.Log("[ServerLedger] p.bank " + a.a + " " + a.n + " from " + account);
        if (a.a == null || !BankReasons.TryGetValue(a.a, out int sign)) return "허용하지 않는 사유입니다.";
        if (a.n == 0 || Math.Sign(a.n) != sign || a.n == long.MinValue) return "금액이 올바르지 않습니다.";
        bool ok = sign > 0 ? wallet.AddMoney(a.n, MoneyChangeReason.Deposit) : wallet.TrySpend(-a.n, MoneyChangeReason.Withdrawal);
        return ok ? null : NoMoney;
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
        string error = WorldOps.Apply(0, account, "land.buy", json); // a taken plot fails here, and nothing is charged
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

    void Send(uint connection, string account)
    {
        var book = Book(account);
        done.TryGetValue(connection, out uint n);
        if (server.SendServerEvent(entity, 0, WorldSection.LedgerEvent, connection, ServerWorld.Bytes(w =>
            {
                w.Write(n);
                w.Write(book.bankBalance);
                SyncProtocol.WriteServerBlob(w, ServerWorld.Encode(JsonUtility.ToJson(book.portfolio)));
            })))
            Sent++;
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
