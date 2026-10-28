using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using QaPeer = SyncLayerQA.QaPeer;
using QaMsg = SyncLayerQA.QaMsg;

/// <summary>
/// QA of the server's inventory ledger, shops and cash (Docs/Multiplayer_Redesign.md 12.10.4), on the EconomySyncQA helpers.
///   -executeMethod InventoryLedgerQA.Run         in memory (with -quit): InventoryLedgerRules (SameContents, p.trade,
///                                                layouts), the cash of the ledger, the TradeCatalog, ServerBooks with a ledger
///   -executeMethod InventoryLedgerQA.RunPlay     play mode (WITHOUT -quit): in-app server + the Unity owner + a fake TCP player
///   -executeMethod InventoryLedgerQA.RunProcess  play mode (WITHOUT -quit): a built server process + the Unity client
/// Env QA_SHOTS = folder for I_*.png (needs graphics).
/// </summary>
public static class InventoryLedgerQA
{
    public static void Run() => EconomySyncQA.InventoryMemory();
    public static void RunPlay() => EconomySyncQA.StartPlay("inv-play");
    public static void RunProcess() => EconomySyncQA.BuildAndPlay("InventoryLedgerQA", "inv-process");
}

public static partial class EconomySyncQA
{
    const string InvFake = "qa-inv-fake";
    const string Store = TradeCatalog.ConvenienceStore;
    const int Gimbap = 0, LunchBox = 2, Water = 3; // offers of the convenience store (1,500 / 5,000 / 700원)

    static ItemData ItemOf(string id) => InventorySnapshot.Find(id) ?? throw new Exception("[" + tag + "] FAIL: no item " + id);
    static ItemData Cash(long value) => CashService.GetCurrency(value);
    static ItemData BlankBook => Resources.Load<ItemData>("Inventory/Books/BlankBook");

    static int CountOf(InventoryState bag, string itemId)
    {
        int n = 0;
        foreach (var s in InventoryLedgerRules.Stacks(bag)) if (s.Item.itemId == itemId) n += s.Count;
        return n;
    }

    static int EmptySlotCount(InventoryState bag)
    {
        int n = 0;
        for (int i = 0; i < bag.Capacity; i++) if (bag.GetSlot(i).IsEmpty) n++;
        return n;
    }

    static int Blanks(InventoryState bag)
    {
        int n = 0;
        foreach (var s in InventoryLedgerRules.Stacks(bag)) if (s.Item.IsBook && !s.IsUniqueBook) n += s.Count;
        return n;
    }

    static int SlotOf(InventoryState bag, Func<ItemStack, bool> match)
    {
        for (int i = 0; i < bag.Capacity; i++) if (!bag.GetSlot(i).IsEmpty && match(bag.GetSlot(i))) return i;
        return -1;
    }

    static int EmptySlot(InventoryState bag, int from = 0)
    {
        for (int i = bag.Capacity - 1; i >= from; i--) if (bag.GetSlot(i).IsEmpty) return i;
        return -1;
    }

    static string Notes(InventoryState bag)
    {
        var list = new List<string>();
        for (int i = 0; i < bag.Capacity; i++) { var s = bag.GetSlot(i); if (!s.IsEmpty && s.Item.IsCurrency) list.Add(s.Item.CurrencyValue + "x" + s.Count); }
        list.Sort(string.CompareOrdinal);
        return string.Join(",", list);
    }

    // A runtime item the snapshot can find (this process only: an in-app server and its owner share it).
    static ItemData RuntimeItem(string id, Action<ItemData> setup)
    {
        var found = InventorySnapshot.Find(id);
        if (found) return found;
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.hideFlags = HideFlags.HideAndDontSave;
        item.itemId = id; item.displayName = id; item.name = id;
        setup(item);
        var catalog = (Dictionary<string, ItemData>)typeof(InventorySnapshot).GetField("catalog", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        catalog[id] = item;
        return item;
    }

    static ItemData Shirt => RuntimeItem("qa:shirt", i => { i.itemType = ItemType.Clothing; i.clothingSlot = EquipmentSlot.Top; });
    static ItemData Backpack => RuntimeItem("qa:backpack", i => i.category = ItemCategory.Bag);

    // ================= in memory (InventoryLedgerQA.Run) =================

    internal static void InventoryMemory()
    {
        tag = "InventoryLedgerQA";
        Begin();
        root = Path.Combine(Path.GetTempPath(), "InventoryLedgerQA-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            Contents();
            Trades();
            LedgerCash();
            Layouts();
            BooksWithLedger();
            Report("RESULT passed");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    static InventoryState Sample()
    {
        var bag = new InventoryState();
        var bike = Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle")[0];
        var book = new BookInstanceData { title = "QA 책", authorPlayerId = "acc-a", authorName = "QA", isPublished = true };
        if (!bag.TryAdd(ItemOf("food:gimbap"), 3, out string e) || !bag.TryAdd(Cash(10000), 1, out e) || !bag.TryAdd(Cash(1500), 2, out e)
            || !bag.TryAddBookInstance(BlankBook, "qa-book-1", book, out e) || !bag.TryAdd(bike, 1, out e, "qa-bike-1") || !bag.TryAdd(Shirt, 1, out e))
            throw new Exception("[" + tag + "] FAIL: sample " + e);
        return bag;
    }

    static InventoryState CopyOf(InventoryState bag)
    {
        var copy = new InventoryState();
        InventorySnapshot.Import(copy, InventorySnapshot.Export(bag));
        return copy;
    }

    static void Contents()
    {
        Section("SameContents");
        var a = Sample();
        var b = CopyOf(a);
        Check(InventoryLedgerRules.SameContents(a, b), "a snapshot round trip is the same (cash notes too: the cash: fix, " + CashService.CarriedTotal(b) + "원)");
        Check(b.TryMove(0, 9, out _) && b.TryMove(1, 12, out _) && InventoryLedgerRules.SameContents(a, b), "moved stacks are the same");
        Check(b.TryEquip(SlotOf(b, s => s.Item == Shirt), EquipmentSlot.Top, out _) && InventoryLedgerRules.SameContents(a, b), "an equipped item is the same");
        Check(b.TryMoveAmount(SlotOf(b, s => s.Item.itemId == "food:gimbap"), EmptySlot(b), 1, out _) && InventoryLedgerRules.SameContents(a, b), "a split stack is the same");
        var notes = CopyOf(a);
        notes.TryRemove(SlotOf(notes, s => s.Item.CurrencyValue == 10000), 1, out _);
        notes.TryRemove(SlotOf(notes, s => s.Item.CurrencyValue == 1500), 2, out _);
        notes.TryAdd(Cash(5000), 2, out _); notes.TryAdd(Cash(3000), 1, out _);
        Check(Notes(notes) != Notes(a) && InventoryLedgerRules.SameContents(a, notes), "other notes of the same total (13,000원) are the same");
        var more = CopyOf(a); more.TryAdd(ItemOf("food:gimbap"), 1, out _);
        Check(!InventoryLedgerRules.SameContents(a, more), "one more gimbap differs");
        var cash = CopyOf(a); cash.TryAdd(Cash(100), 1, out _);
        Check(!InventoryLedgerRules.SameContents(a, cash), "100원 more differs");
        var other = CopyOf(a);
        other.TryRemove(SlotOf(other, s => s.InstanceId == "qa-book-1"), 1, out _);
        other.TryAddBookInstance(BlankBook, "qa-book-2", new BookInstanceData { title = "QA 책", authorPlayerId = "acc-a", isPublished = true }, out _);
        Check(!InventoryLedgerRules.SameContents(a, other), "another book instance (same title) differs");
        var bike = CopyOf(a);
        var bikeItem = Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle")[0];
        bike.TryRemove(SlotOf(bike, s => s.Item == bikeItem), 1, out _); bike.TryAdd(bikeItem, 1, out _, "qa-bike-2");
        Check(!InventoryLedgerRules.SameContents(a, bike), "another vehicle instance differs");
        var bigger = CopyOf(a); bigger.TryExpandWithBag(out _);
        Check(!InventoryLedgerRules.SameContents(a, bigger), "another capacity (24) differs");
    }

    static TradeOffer Offer(long cash, ItemData give, ItemData get, long getCash = 0, int getCount = 1) => new TradeOffer
    {
        give = new TradeItem { item = give, cash = cash, count = 1 },
        get = new TradeItem { item = get, cash = getCash, count = getCount }
    };

    static void Trades()
    {
        Section("p.trade on a ledger");
        var gimbap = ItemOf("food:gimbap");
        var water = ItemOf("food:water");
        var template = new BookInstanceData { title = "가게 책", authorPlayerId = "acc-writer", authorName = "작가", isPublished = true, permission = BookEditPermission.ReadOnly };
        template.pages[0] = "본문은 장부에 없음";
        var offers = new[]
        {
            Offer(1500, null, gimbap),                                     // 0 buy
            Offer(0, gimbap, null, 1000),                                  // 1 sell: a gimbap for a 1,000원 note
            Offer(700, null, water),                                       // 2 buy
            new TradeOffer { give = new TradeItem { cash = 3000, count = 1 }, get = new TradeItem { item = BlankBook, count = 1, bookTemplate = template } }, // 3 a book copy
        };
        var bag = new InventoryState();
        bag.TryAdd(Cash(10000), 1, out _);
        Check(InventoryLedgerRules.TryTrade(bag, offers, new[] { (0, 2), (2, 1) }, null, out string e) && CashService.CarriedTotal(bag) == 10000 - 3700
              && CountOf(bag, "food:gimbap") == 2 && CountOf(bag, "food:water") == 1 && Notes(bag) == "6300x1",
            "2 gimbap + 1 water from a 10,000원 note: 3,700원 paid at once, change one 6,300원 note " + e);
        var poor = CopyOf(bag);
        Check(!InventoryLedgerRules.TryTrade(poor, offers, new[] { (0, 5) }, null, out e) && InventoryLedgerRules.SameContents(poor, bag) && Notes(poor) == "6300x1",
            "7,500원 with 6,300원 is refused, nothing changes: " + e);
        Check(InventoryLedgerRules.TryTrade(bag, offers, new[] { (1, 2) }, null, out e) && CountOf(bag, "food:gimbap") == 0 && CashService.CarriedTotal(bag) == 6300 + 2000,
            "selling 2 gimbap brings two 1,000원 notes " + e);
        Check(!InventoryLedgerRules.TryTrade(bag, offers, new[] { (1, 1) }, null, out e), "selling a gimbap not held is refused: " + e);
        Check(!InventoryLedgerRules.TryTrade(bag, offers, new[] { (9, 1) }, null, out e) && !InventoryLedgerRules.TryTrade(bag, offers, new[] { (0, 0) }, null, out e)
              && !InventoryLedgerRules.TryTrade(bag, offers, new[] { (0, 1000) }, null, out e), "an unknown offer, 0 times and more than a stack are refused");

        // A full bag whose one note pays exactly: its slot frees for one new item, not for two kinds, and then nothing changes.
        InventoryState FullBag(long note)
        {
            var bag = new InventoryState();
            var filler = Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle")[0];
            for (int i = 0; i < bag.Capacity - 1; i++) bag.TryAdd(filler, 1, out _, "qa-fill-" + i);
            bag.TryAdd(Cash(note), 1, out _);
            return bag;
        }
        var full = FullBag(2200);
        var before = InventorySnapshot.Export(full);
        Check(!InventoryLedgerRules.TryTrade(full, offers, new[] { (0, 1), (2, 1) }, null, out e) && InventorySnapshot.Export(full) == before,
            "a cart (gimbap + water, 2,200원 exactly) whose goods need two slots in a full bag is refused whole, nothing changes: " + e);
        Check(!InventoryLedgerRules.TryTrade(full, offers, new[] { (0, 1) }, null, out e) && InventorySnapshot.Export(full) == before,
            "one gimbap from it is refused too: its 700원 change takes the freed slot: " + e);
        full = FullBag(1500);
        Check(InventoryLedgerRules.TryTrade(full, offers, new[] { (0, 1) }, null, out e) && CountOf(full, "food:gimbap") == 1 && CashService.CarriedTotal(full) == 0,
            "with the exact 1,500원 note one gimbap fits in its slot " + e);

        var books = new InventoryState();
        books.TryAdd(Cash(5000), 1, out _);
        Check(!InventoryLedgerRules.TryTrade(books, offers, new[] { (3, 1) }, null, out e), "a book copy without the id the client made is refused: " + e);
        Check(InventoryLedgerRules.TryTrade(books, offers, new[] { (3, 1) }, new[] { "qa-shop-copy" }, out e), "with its id it is bought " + e);
        var copy = InventoryLedgerRules.FindUnique(books, "qa-shop-copy")?.BookData;
        Check(copy != null && copy.authorPlayerId == "acc-writer" && copy.permission == BookEditPermission.ReadOnly && !copy.bodyLoaded && copy.pages[0] == "",
            "the copy's details are the catalog's (author, read-only), kept without pages");
        books.TryAdd(Cash(5000), 1, out _);
        Check(!InventoryLedgerRules.TryTrade(books, offers, new[] { (3, 1) }, new[] { "qa-shop-copy" }, out e), "an id already held is refused: " + e);

        Section("the TradeCatalog");
        var shop = TradeCatalog.Offers(Store);
        var cafe = TradeCatalog.Offers(TradeCatalog.LibraryCafe);
        Check(shop != null && shop.Length == 10 && cafe != null && cafe.Length == 3 && TradeCatalog.Offers("qa-none") == null && TradeCatalog.Offers(null) == null,
            "the convenience store has 10 offers, the library cafe 3, an unknown shop none");
        Check(shop[Gimbap].give.cash == 1500 && shop[Gimbap].get.item.itemId == "food:gimbap" && shop[Water].give.cash == 700 && shop[LunchBox].give.cash == 5000
              && cafe[0].give.cash == 3000 && cafe[0].get.item.itemId == "item_coffee", "the offers are the scenes' (gimbap 1,500, water 700, lunch box 5,000, coffee 3,000)");
        var session = new TradeSession(new InventoryState(), shop);
        bool cartable = true;
        for (int i = 0; i < shop.Length; i++) cartable &= session.IsCartable(i);
        Check(cartable && InventorySnapshot.Find("food:gimbap") == shop[Gimbap].get.item, "every offer can go in the cart, and the snapshot finds the shop's goods (outside Resources/Inventory)");
    }

    static void LedgerCash()
    {
        Section("cash in and out of a ledger");
        var wallet = new QaWallet(100000);
        var bag = new InventoryState();
        Check(CashService.LedgerWithdraw(wallet, bag, 10000, 3, out string e) && wallet.Balance == 70000 && CashService.CarriedTotal(bag) == 30000, "withdraw 10,000원 x 3 " + e);
        Check(CashService.LedgerDeposit(wallet, bag, 10000, 2, out e) && wallet.Balance == 90000 && CashService.CarriedTotal(bag) == 10000, "deposit 2 of them " + e);
        Check(!CashService.LedgerDeposit(wallet, bag, 50000, 1, out e) && wallet.Balance == 90000, "50,000원 over the 10,000원 carried is refused: " + e);
        Check(!CashService.LedgerDeposit(wallet, bag, 10000, 2, out e) && CashService.CarriedTotal(bag) == 10000, "20,000원 over the 10,000원 carried is refused: " + e);
        Check(!CashService.LedgerWithdraw(wallet, bag, 100000, 1, out e) && wallet.Balance == 90000 && CashService.CarriedTotal(bag) == 10000, "over the balance is refused: " + e);
        bag.TryAdd(Cash(500), 1, out _); bag.TryAdd(Cash(500), 1, out _);
        Check(CashService.LedgerDeposit(wallet, bag, 500, 2, out e) && wallet.Balance == 91000, "notes of one value from any stacks " + e);
        // The ledger's notes may be others than the client's (change made at once on the server): the total is what counts.
        var other = new InventoryState();
        other.TryAdd(Cash(1500), 1, out _); other.TryAdd(Cash(32200), 1, out _);
        wallet = new QaWallet(0);
        Check(CashService.LedgerDeposit(wallet, other, 33700, 1, out e) && wallet.Balance == 33700 && CashService.CarriedTotal(other) == 0,
            "a 33,700원 note deposited from a ledger holding 1,500 + 32,200: taken by its total, bank +33,700 " + e);
        other.TryAdd(Cash(1500), 1, out _); other.TryAdd(Cash(32200), 1, out _);
        Check(CashService.LedgerDeposit(wallet, other, 10000, 1, out e) && CashService.CarriedTotal(other) == 23700 && wallet.Balance == 43700,
            "10,000원 from those notes leaves 23,700원 (change as PlanPayment makes it) " + e);
    }

    static void Layouts()
    {
        Section("layouts");
        var ledger = Sample();
        var moved = CopyOf(ledger);
        moved.TryMove(0, 13, out _);
        moved.FindBook("qa-book-1").BookData.title = "클라이언트가 바꾼 제목";
        Check(InventoryLedgerRules.TryTakeLayout(ledger, InventorySnapshot.Export(moved, false), false, out string e) && !ledger.GetSlot(13).IsEmpty && ledger.GetSlot(0).IsEmpty,
            "a moved layout with the same contents is taken (slot 0 → 13) " + e);
        Check(ledger.FindBook("qa-book-1").BookData.title == "QA 책", "a unique item keeps the ledger's data (the client's title is not taken)");
        var forged = CopyOf(ledger);
        forged.TryAdd(Cash(50000), 1, out _);
        string keep = InventorySnapshot.Export(ledger);
        Check(!InventoryLedgerRules.TryTakeLayout(ledger, InventorySnapshot.Export(forged, false), false, out e) && InventorySnapshot.Export(ledger) == keep, "a layout with 50,000원 more is refused: " + e);
        Check(InventoryLedgerRules.TryTakeLayout(ledger, InventorySnapshot.Export(forged, false), true, out e) && CashService.CarriedTotal(ledger) == CashService.CarriedTotal(forged),
            "a trusted one (the library, until 4d-5) is taken as it is");
        string packed = InventoryLedgerRules.Pack(ledger);
        Check(InventoryLedgerRules.Unpack(packed) == InventorySnapshot.Export(ledger, false) && InventoryLedgerRules.Unpack("not base64") == null,
            "a packed layout is " + packed.Length + " characters and unpacks to the snapshot");
        Note("packed layout of the sample bag: " + packed.Length + " characters");
        var lines = InventoryLedgerRules.ParseLines(InventoryLedgerRules.Lines(new[] { (0, 2), (3, 19) }));
        Check(lines.Count == 2 && lines[1] == (3, 19) && InventoryLedgerRules.ParseLines("1:-2") == null && InventoryLedgerRules.ParseLines("x") == null, "p.trade lines round trip, bad ones are refused");

        var food = new InventoryState();
        food.TryAdd(ItemOf("food:gimbap"), 2, out _);
        Check(InventoryLedgerRules.TryEat(food, "food:gimbap", out e) && CountOf(food, "food:gimbap") == 1 && !InventoryLedgerRules.TryEat(food, "food:cola", out e), "p.eat takes one held food, not one that is not held");
        Check(!InventoryLedgerRules.TryBag(food, out e), "p.bag without a bag is refused: " + e);
        food.TryAdd(Backpack, 1, out _);
        Check(InventoryLedgerRules.TryBag(food, out e) && food.Capacity == InventoryState.ExpandedCapacity && CountOf(food, "qa:backpack") == 0 && !InventoryLedgerRules.TryBag(food, out _),
            "with one the bag is used up and holds 24 (only once)");
    }

    static void BooksWithLedger()
    {
        Section("book requests with a ledger");
        StartServer(WorldStore.NewId());
        var a = Join("acc-a", "QA가", "m1");
        var b = Join("acc-b", "QA나", "m1");
        var bags = new Dictionary<string, InventoryState> { { "acc-a", new InventoryState() }, { "acc-b", new InventoryState() } };
        var books = new ServerBooks(server, 0, null)
        {
            Owned = (acc, id) => InventoryLedgerRules.FindUnique(bags[acc], id)?.BookData,
            FromBlank = (acc, id, book) => InventoryLedgerRules.TryWriteBlank(bags[acc], id, book, out _)
        };
        Check(Ask(books, a, "book.new", new ServerBooks.Request { id = "qa-mine", title = "내 책" }) == "빈 책이 없습니다.", "my own new book without a blank is refused");
        bags["acc-a"].TryAdd(BlankBook, 2, out _);
        Check(Ask(books, a, "book.new", new ServerBooks.Request { id = "qa-mine", title = "내 책" }) == null && Blanks(bags["acc-a"]) == 1
              && bags["acc-a"].FindBook("qa-mine")?.BookData.IsAuthor("acc-a") == true && InventoryLedgerRules.FindUnique(bags["acc-a"], "qa-mine").Count == 1,
            "with one, a blank of the bag becomes the book (1 blank left + the book)");
        Check(Ask(books, a, "book.new", new ServerBooks.Request { id = "qa-mine", title = "내 책" }) == null, "book.new of a book of my bag the server has changes nothing (a client that lost track)");
        Check(Ask(books, b, "book.new", new ServerBooks.Request { id = "qa-copy", title = "사본", authorId = "acc-z", authorName = "남" }) == "가지고 있지 않은 책입니다.",
            "a copy that is not in the bag is refused (a made-up author)");
        var copy = new BookInstanceData { title = "도서관 사본", authorPlayerId = "acc-z", authorName = "진짜작가", isPublished = true, permission = BookEditPermission.ReadOnly, libraryLoanId = "loan-q" };
        bags["acc-b"].TryAddBookInstance(BlankBook, "qa-copy", copy, out _);
        Check(Ask(books, b, "book.new", new ServerBooks.Request { id = "qa-copy", title = "고친 제목", authorId = "acc-b", authorName = "가짜작가" }) == null
              && books.Find("qa-copy").authorName == "진짜작가" && books.Find("qa-copy").IsLibraryLoan && books.Find("qa-copy").title == "도서관 사본",
            "a copy in the bag takes the bag's details, not the request's");
        Check(Ask(books, b, "book.page", new ServerBooks.Request { id = "qa-mine", page = 0, text = "남의 책" }) == "가지고 있지 않은 책입니다."
              && Ask(books, b, "book.meta", new ServerBooks.Request { id = "qa-mine", title = "남의 제목" }) == "가지고 있지 않은 책입니다.",
            "book.page and book.meta on a book not in the sender's bag are refused");
        Check(Ask(books, a, "book.page", new ServerBooks.Request { id = "qa-mine", page = 0, text = "내가 씀" }) == null && books.Find("qa-mine").pages[0] == "내가 씀", "the owner writes it");
        Check(Ask(books, b, "book.get", new ServerBooks.Request { id = "qa-mine" }) == null, "book.get needs no ownership");
        server.Stop();
    }

    // ================= in-app server (InventoryLedgerQA.RunPlay) =================

    struct InvAnswer { public string error, inventory; public bool ledgerFirst; public long bank; public uint done; }

    static InvAnswer ReadInvLedger(byte[] blob)
    {
        using var r = SyncProtocol.Reader(blob);
        var a = new InvAnswer { done = r.ReadUInt32(), bank = r.ReadInt64() };
        SyncProtocol.ReadServerBlob(r);
        a.inventory = SectionJson(SyncProtocol.ReadServerBlob(r));
        return a;
    }

    static async Task<InvAnswer> InvOp(QaPeer fake, string kind, SharedOpArgs args)
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
                var answer = new InvAnswer();
                for (int i = 0; i < at; i++)
                    if (IsLedger(fake.inbox[i])) { answer = ReadInvLedger(fake.inbox[i].blob); answer.ledgerFirst = true; }
                using (var r = SyncProtocol.Reader(fake.inbox[at].blob))
                {
                    Check(SyncProtocol.ReadStr(r) == kind, "OpResult for " + kind);
                    answer.error = SyncProtocol.ReadStr(r);
                }
                fake.inbox.RemoveRange(0, at + 1);
                return answer;
            }
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for the answer to " + kind);
            await Awaitable.NextFrameAsync();
        }
    }

    static SharedOpArgs TradeArgs(string shop, params (int, int)[] lines) => new SharedOpArgs { b = shop, a = InventoryLedgerRules.Lines(lines) };
    static SharedOpArgs CashArgs(long face, int count) => new SharedOpArgs { n = face, a = count.ToString() };

    // My layout is out and taken: the ledger's bag is mine, slot for slot.
    static async Task Settle(ServerLedger ledger, string me, string what)
    {
        await Awaitable.WaitForSecondsAsync(1.2f); // the layout goes out within a second of the change
        await WaitFor(() => InventorySnapshot.Export(ledger.Bag(me), false) == InventorySnapshot.Export(InventoryManager.Instance.State, false), 6, what);
        await Awaitable.WaitForSecondsAsync(.3f);
    }

    static async Task InventoryPlayBody()
    {
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy && WorldSave.Baseline != null, 15, "title");
        ProfileSlot.Set(Slot);
        var client = SyncClient.Instance;

        Section("a character saved before 4d-3");
        var settings = new ServerSettings { name = "InventoryLedgerQA", maxPlayers = 4, port = 0, world = WorldStore.NewId() };
        var store = WorldStore.Open(ServerSettings.WorldsFolder, settings.world);
        playWorld = store.Folder;
        var old = new InventoryState();
        old.TryAdd(Cash(10000), 2, out _); old.TryAdd(ItemOf("food:gimbap"), 2, out _); old.TryAdd(BlankBook, 1, out _);
        Check(store.Create(InvFake, "QA인벤가짜", "female", out var made) == CharacterResult.Ok, "the fake's character in a world file");
        made.content = CharacterSync.Encode(new CharacterSync.Saved { map = "qa-map", personal = JsonUtility.ToJson(new PersonalSaveData { bankBalance = 500000 }), inventory = InventorySnapshot.Export(old, false) });
        store.Save();

        var host = SyncHost.StartInApp(settings, true);
        await WaitFor(() => client.Connected && client.CharacterKnown, 10, "the Unity client on the in-app server");
        Check(await TitleFlow.CreateServerCharacter("QA인벤", "male") == null, "the Unity owner made its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        string map = SceneLoadManager.CurrentMap.path, me = GameSession.LocalPlayerId;
        var ledger = ServerWorld.Instance.Ledger;
        var bag = InventoryManager.Instance.State;
        var bank = BankManager.Instance;
        await WaitFor(() => WorldStateSync.LayoutsSent > 0 && WorldStateSync.Ledgers > 0, 10, "the first layout and its Ledger");
        await Settle(ledger, me, "the owner's start");
        Check(WorldStateSync.Overwrites == 0 && WorldStateSync.InventoryOverwrites == 0, "right after joining: the ledger's bag is the owner's, nothing written over");
        var fake = await JoinFake(host.Port, map, InvFake, "QA인벤가짜");
        var fb = ledger.Bag(InvFake);
        // 12.10.5: entering, the server gave it the start vehicles and a deed; this QA's trades need the room, so they go.
        for (int i = 0; i < fb.Capacity; i++)
            if (!fb.GetSlot(i).IsEmpty && (fb.GetSlot(i).Item.IsVehicle || InventoryLedgerRules.IsStartingDeed(fb.GetSlot(i).Item))) fb.TryRemove(i, 1, out _);
        ledger.Store(InvFake);
        Check(CashService.CarriedTotal(fb) == 20000 && CountOf(fb, "food:gimbap") == 2 && Blanks(fb) == 1,
            "the fake's ledger starts from its saved inventory, its 20,000원 in notes too (the cash: fix)");

        Section("forged saves and layouts");
        var rich = CopyOf(fb);
        rich.TryAdd(Cash(10000), 50, out _); rich.TryAdd(ItemOf("food:gimbap"), 15, out _);
        string fakeLedger = InventorySnapshot.Export(fb, false);
        fake.Send(SyncProtocol.CharacterSave(CharacterSync.Encode(new CharacterSync.Saved { map = map, position = new Vector3(4f, 0f, 4f), personal = "{}", inventory = InventorySnapshot.Export(rich, false) })));
        await WaitFor(() => SavedOf(InvFake).position.x == 4f, 5, "the fake's save");
        Check(SavedOf(InvFake).inventory == fakeLedger && InventorySnapshot.Export(fb, false) == fakeLedger,
            "a save with 500,000원 and 15 gimbap more keeps the ledger's inventory (its position is kept)");
        int refused = ledger.LayoutsRefused;
        var a = await InvOp(fake, "p.inv.layout", new SharedOpArgs { b = InventoryLedgerRules.Pack(rich) });
        Check(a.error == "인벤토리 내용이 서버와 다릅니다." && a.ledgerFirst && a.inventory == fakeLedger && ledger.LayoutsRefused == refused + 1,
            "a layout with more is refused, and the fake's Ledger before the answer has the ledger's bag");
        var moved = CopyOf(fb);
        int from = SlotOf(moved, s => s.Item.itemId == "food:gimbap"), to = EmptySlot(moved);
        moved.TryMove(from, to, out _);
        a = await InvOp(fake, "p.inv.layout", new SharedOpArgs { b = InventoryLedgerRules.Pack(moved) });
        Check(a.error == "" && fb.GetSlot(to).Item?.itemId == "food:gimbap" && fb.GetSlot(from).IsEmpty, "a moved layout is taken (gimbap " + from + " → " + to + ")");

        Section("the fake's trades");
        long ownerBank = bank.Money;
        string ownerBag = InventorySnapshot.Export(bag, false);
        int ownerLedgers = WorldStateSync.Ledgers;
        a = await InvOp(fake, "p.trade", TradeArgs(Store, (LunchBox, 5)));
        Check(a.error != "" && CashService.CarriedTotal(fb) == 20000, "5 lunch boxes (25,000원) with 20,000원 are refused: " + a.error);
        a = await InvOp(fake, "p.trade", TradeArgs(Store, (Gimbap, 1), (Water, 2)));
        Check(a.error == "" && CashService.CarriedTotal(fb) == 20000 - 1500 - 1400 && CountOf(fb, "food:gimbap") == 3 && CountOf(fb, "food:water") == 2,
            "a gimbap and 2 water: 2,900원 at the catalog's prices come off the ledger's notes, the goods go in");
        Check(a.ledgerFirst && a.inventory == InventorySnapshot.Export(fb, false), "its Ledger has the ledger's bag");
        a = await InvOp(fake, "p.trade", TradeArgs("qa-no-shop", (0, 1)));
        Check(a.error == "알 수 없는 상점입니다.", "an unknown shop is refused");
        a = await InvOp(fake, "p.trade", TradeArgs(Store, (99, 1)));
        Check(a.error != "" && CashService.CarriedTotal(fb) == 17100, "an unknown offer is refused: " + a.error);
        a = await InvOp(fake, "p.trade", TradeArgs(TradeCatalog.LibraryCafe, (0, 1)));
        Check(a.error == "" && CountOf(fb, "item_coffee") == 1 && CashService.CarriedTotal(fb) == 14100, "the library cafe's coffee (3,000원) too");

        Section("cash in and out");
        long fakeBank = ledger.Book(InvFake).bankBalance;
        a = await InvOp(fake, "p.cash.withdraw", CashArgs(10000, 3));
        Check(a.error == "" && ledger.Book(InvFake).bankBalance == fakeBank - 30000 && CashService.CarriedTotal(fb) == 44100 && a.bank == fakeBank - 30000,
            "withdraw 10,000원 x 3: bank -30,000, notes +30,000 together");
        a = await InvOp(fake, "p.cash.deposit", CashArgs(10000, 1));
        Check(a.error == "" && ledger.Book(InvFake).bankBalance == fakeBank - 20000 && CashService.CarriedTotal(fb) == 34100, "deposit one 10,000원 note");
        a = await InvOp(fake, "p.cash.deposit", CashArgs(50000, 1));
        Check(a.error != "" && ledger.Book(InvFake).bankBalance == fakeBank - 20000, "50,000원 over the 34,100원 it carries is refused: " + a.error);
        a = await InvOp(fake, "p.cash.withdraw", CashArgs(10000000, 1));
        Check(a.error != "" && CashService.CarriedTotal(fb) == 34100, "a withdrawal over the bank is refused: " + a.error);
        a = await InvOp(fake, "p.bank", new SharedOpArgs { n = 100000, a = "deposit" });
        Check(a.error == "처리할 수 없는 요청입니다.", "p.bank deposit is gone (p.bank itself since 12.10.6)");
        a = await InvOp(fake, "p.bank", new SharedOpArgs { n = -1000, a = "withdraw" });
        Check(a.error == "처리할 수 없는 요청입니다." && ledger.Book(InvFake).bankBalance == fakeBank - 20000, "p.bank withdraw too");

        Section("eat, bag, trust, books");
        a = await InvOp(fake, "p.eat", new SharedOpArgs { a = "food:gimbap" });
        Check(a.error == "" && CountOf(fb, "food:gimbap") == 2, "p.eat of a held gimbap takes one");
        a = await InvOp(fake, "p.eat", new SharedOpArgs { a = "food:cola" });
        Check(a.error != "", "p.eat of a food not held is refused: " + a.error);
        a = await InvOp(fake, "p.bag", new SharedOpArgs());
        Check(a.error == "가방이 없습니다." && fb.Capacity == InventoryState.BaseCapacity, "p.bag without a bag is refused");
        a = await InvOp(fake, "p.inv.trust", new SharedOpArgs { a = "library", b = InventoryLedgerRules.Pack(rich) });
        Check(a.error == "처리할 수 없는 요청입니다." && CashService.CarriedTotal(fb) == 34100, "p.inv.trust is refused (gone in 12.10.6)");
        var books = ServerWorld.Instance.Books;
        var br = await FakeBookOp(fake, "book.new", new ServerBooks.Request { id = "qa-inv-fake-book", title = "가짜의 책", permission = (int)BookEditPermission.FullEdit });
        Check(br.error == "" && books.Find("qa-inv-fake-book")?.IsAuthor(InvFake) == true, "the fake writes a book of its own");
        Check(Blanks(fb) == 0 && InventoryLedgerRules.FindUnique(fb, "qa-inv-fake-book") != null, "the fake's own new book used up its blank in the ledger");
        br = await FakeBookOp(fake, "book.new", new ServerBooks.Request { id = "qa-inv-fake-2", title = "또" });
        Check(br.error == "빈 책이 없습니다.", "another one without a blank is refused");
        br = await FakeBookOp(fake, "book.new", new ServerBooks.Request { id = "qa-inv-forged", title = "남의 이름", authorId = "acc-famous", authorName = "유명작가" });
        Check(br.error == "가지고 있지 않은 책입니다.", "a copy it never got (a made-up author) is refused");
        Check(WorldStateSync.Ledgers == ownerLedgers && bank.Money == ownerBank && InventorySnapshot.Export(bag, false) == ownerBag,
            "none of the fake's Ledgers reached the Unity owner; its screen bag and bank did not change");

        Section("the Unity owner: in the same frame, nothing written over");
        int overwrites = WorldStateSync.Overwrites;
        string err;
        Note("the owner's start bag: " + EmptySlotCount(bag) + " of " + bag.Capacity + " slots free");
        Give(ledger, me, Backpack, 1); // no request brings items in on trust (12.10.6): the ledger is given it here
        Check(bag.TryAdd(Backpack, 1, out err), "a backpack (a runtime bag item; the game has none yet) " + err);
        await Settle(ledger, me, "the backpack");
        Check(bag.TryUseBag(SlotOf(bag, s => s.Item == Backpack), out err) && bag.Capacity == 24, "using the backpack shows 24 slots at once " + err);
        await Settle(ledger, me, "p.bag");
        Check(ledger.Bag(me).Capacity == 24 && WorldStateSync.InventoryOverwrites == 0, "the ledger's bag is 24 too (p.bag), nothing written over");
        long cash0 = CashService.CarriedTotal(bag), bank0 = bank.Money;
        Check(CashService.TryWithdraw(bag, 50000, 1, out err) && CashService.TryWithdraw(bag, 10000, 1, out err) && CashService.TryWithdraw(bag, 1500, 1, out err)
              && CashService.CarriedTotal(bag) == cash0 + 61500 && bank.Money == bank0 - 61500, "withdrawals show at once (notes +61,500, bank -61,500) " + err);
        await Settle(ledger, me, "the withdrawals");
        Check(ledger.Book(me).bankBalance == bank.Money && WorldStateSync.InventoryOverwrites == 0, "the ledger agrees (bank " + bank.Money + ")");

        var interaction = PlayerInteraction.Local;
        Check(interaction.OpenTrade(TradeCatalog.Offers(Store), null, Store), "the convenience store's trade window (catalog offers)");
        var window = interaction.TradeUI;
        var session = window.Session;
        Check(session.ShopId == Store, "the session knows its shop");
        Check(session.TryAddToCart(Water, out err) && session.TryChangeCart(session.Cart[0], 18, out err) && session.TryAddToCart(Gimbap, out err), "a cart of 19 water and a gimbap " + err);
        long before = CashService.CarriedTotal(bag);
        Check(session.TryCheckout(out err) && CashService.CarriedTotal(bag) == before - 19 * 700 - 1500 && CountOf(bag, "food:water") == 19, "checkout shows at once (14,800원) " + err);
        window.Refresh();
        await Shot("I_owner_cart.png");
        await Settle(ledger, me, "the checkout");
        Check(WorldStateSync.InventoryOverwrites == 0 && CashService.CarriedTotal(ledger.Bag(me)) == CashService.CarriedTotal(bag), "the cart's Ledger agreed: nothing written over");

        // Change of other notes: the client pays each exchange, the server both at once.
        string notesBefore = Notes(bag);
        Check(session.TryTakeOffer(Gimbap, out err) && session.TryTakeOffer(Gimbap, out err) && session.CursorCount == 2, "two gimbap on the cursor " + err);
        int sent = WorldStateSync.LayoutsSent;
        await Awaitable.WaitForSecondsAsync(1.3f);
        Check(WorldStateSync.LayoutsSent == sent && TradeSession.Holding(bag), "while the cursor holds them no layout goes out");
        int far = EmptySlot(bag, 8);
        Check(session.TryPlace(far, out err) && bag.GetSlot(far).Count == 2, "placed in slot " + far + " (the server puts them in its first free slot) " + err);
        Note("notes before the cursor trade " + notesBefore + ", after " + Notes(bag));
        // Its change note goes to the bank at once, before the layout (the ledger made other notes of the same total).
        int change = -1;
        for (int i = 0; i < bag.Capacity; i++)
            if (!bag.GetSlot(i).IsEmpty && bag.GetSlot(i).Item.IsCurrency && (change < 0 || bag.GetSlot(i).Item.CurrencyValue > bag.GetSlot(change).Item.CurrencyValue)) change = i;
        long changeFace = bag.GetSlot(change).Item.CurrencyValue, bankBefore = bank.Money;
        sent = WorldStateSync.LayoutsSent;
        Check(CashService.TryDeposit(bag, change, 1, out err) && bank.Money == bankBefore + changeFace && WorldStateSync.LayoutsSent == sent,
            "the " + changeFace + "원 change note deposited in the same frame, before any layout " + err);
        await Settle(ledger, me, "the cursor trade");
        Check(WorldStateSync.InventoryOverwrites == 0 && WorldStateSync.Overwrites == overwrites && ledger.Book(me).bankBalance == bank.Money,
            "other slot, other change notes and that deposit: nothing written over, the ledger's bank agrees");

        int waterSlot = SlotOf(bag, s => s.Item.itemId == "food:water");
        Check(session.TryTakeOffer(Water, out err) && session.TryTakeOffer(Water, out err) && session.TryTakeOffer(Water, out err), "3 water on the cursor " + err);
        Check(session.TryPlace(waterSlot, out err) && bag.GetSlot(waterSlot).Count == 20 && session.CursorCount == 2, "one fits on the stack of 19, two stay on the cursor (the trade stands) " + err);
        sent = WorldStateSync.LayoutsSent;
        await Awaitable.WaitForSecondsAsync(1.5f);
        Check(WorldStateSync.LayoutsSent == sent && WorldStateSync.InventoryOverwrites == 0 && CountOf(ledger.Bag(me), "food:water") == 22,
            "the server has all 3 (22 water), the screen 20 + 2 on the cursor: no layout, nothing written over");
        Check(session.TryPlace(EmptySlot(bag), out err) && !session.HasCursorItem, "the rest placed " + err);
        await Settle(ledger, me, "the partial placement");
        Check(WorldStateSync.InventoryOverwrites == 0, "still nothing written over");

        Check(session.TryTakeOffer(LunchBox, out err) && session.TryCancel(out err), "a lunch box taken and cancelled (5,000원 back as one note) " + err);
        await Settle(ledger, me, "the cancel");
        Check(WorldStateSync.InventoryOverwrites == 0, "a cancel sends nothing and writes nothing over");
        interaction.CloseTrade();

        int note = SlotOf(bag, s => s.Item.IsCurrency && s.Item.CurrencyValue > 0);
        long face = bag.GetSlot(note).Item.CurrencyValue;
        bank0 = bank.Money;
        Check(CashService.TryDeposit(bag, note, 1, out err) && bank.Money == bank0 + face, "a deposit of a " + face + "원 note shows at once " + err);
        await Settle(ledger, me, "the deposit");
        var stats = PlayerSpawner.Player.GetComponent<PlayerStats>();
        stats.health = 50f;
        int gimbaps = CountOf(bag, "food:gimbap");
        Check(stats.TryConsume(bag, SlotOf(bag, s => s.Item.itemId == "food:gimbap"), out err) && CountOf(bag, "food:gimbap") == gimbaps - 1, "eating a gimbap " + err);
        await Settle(ledger, me, "eating");
        Check(CountOf(ledger.Bag(me), "food:gimbap") == gimbaps - 1, "the ledger's gimbap went too (p.eat)");
        int x = SlotOf(bag, s => s.Item.itemId == "food:water"), y = EmptySlot(bag);
        Check(bag.TryMove(x, y, out err), "a move " + x + " → " + y);
        await Settle(ledger, me, "the move");
        Give(ledger, me, Shirt, 1);
        Check(bag.TryAdd(Shirt, 1, out err), "a shirt (a runtime clothing item; the game has none yet)");
        await Settle(ledger, me, "the shirt");
        Check(bag.TryEquip(SlotOf(bag, s => s.Item == Shirt), EquipmentSlot.Top, out err), "equipped " + err);
        await Settle(ledger, me, "equipping");
        Check(ledger.Bag(me).GetEquipment(EquipmentSlot.Top).Item == Shirt && WorldStateSync.InventoryOverwrites == 0, "moves and equipment reach the ledger, nothing written over");

        Section("the library, the world's items");
        LibraryCatalog.ConnectLocal(bag);
        long cash1 = CashService.CarriedTotal(bag);
        int blanks = Blanks(bag);
        Check(LibraryCatalog.Purchase(me, bag, null, out err) && Blanks(bag) == blanks + 1 && CashService.CarriedTotal(bag) == cash1 - LibraryCatalog.BlankPrice,
            "a blank bought at the library desk (1,000원) " + err);
        await Settle(ledger, me, "the library");
        Check(WorldStateSync.InventoryOverwrites == 0 && Blanks(ledger.Bag(me)) == blanks + 1, "the library's sale went as p.lib.buy (12.10.6): nothing written over");
        var draft = new BookInstanceData { title = "장부 책" };
        draft.pages[0] = "빈 책에 씀";
        int requests = WorldStateSync.BookRequests;
        Check(bag.TryPublishBook(SlotOf(bag, s => s.Item.IsBook && !s.IsUniqueBook), draft, me, GameSession.LocalPlayerName, out int bookSlot, out err), "a book written on that blank " + err);
        string bookId = bag.GetSlot(bookSlot).InstanceId;
        await Settle(ledger, me, "the new book");
        Check(InventoryLedgerRules.FindUnique(ledger.Bag(me), bookId) != null && Blanks(ledger.Bag(me)) == blanks && books.Find(bookId)?.pages[0] == "빈 책에 씀"
              && WorldStateSync.LastBookError == null && WorldStateSync.InventoryOverwrites == 0, "book.new took the ledger's blank and the server has its page");
        // Drops and pickups are the server's since 12.10.5 (WorldItemQA): one of each here, in normal play.
        var invUi = PlayerSpawner.Player.GetComponent<PlayerInventory>();
        int waterNow = CountOf(bag, "food:water");
        Check(invUi.OpenInventory(), "inventory open");
        bool dropped = WorldDroppedItem.TryDropStorage(invUi, SlotOf(bag, s => s.Item.itemId == "food:water"), out err, 1);
        invUi.CloseInventory();
        Check(dropped && CountOf(bag, "food:water") == waterNow - 1, "a water dropped (p.drop) " + err);
        WorldDroppedItem waterDrop = null;
        await WaitFor(() => (waterDrop = Array.Find(Object.FindObjectsByType<WorldDroppedItem>(), v => v && v.NetId != 0 && v.Item && v.Item.itemId == "food:water")) != null, 5, "the water on the ground");
        await Settle(ledger, me, "the drop");
        Check(CountOf(ledger.Bag(me), "food:water") == waterNow - 1 && WorldStateSync.InventoryOverwrites == 0, "it left the ledger too");
        var mover = PlayerSpawner.Player.GetComponent<CharacterController>();
        mover.enabled = false; PlayerSpawner.Player.transform.position = waterDrop.transform.position + Vector3.right; mover.enabled = true;
        Physics.SyncTransforms();
        Check(waterDrop.TryPickUp(invUi, out err) && CountOf(bag, "food:water") == waterNow, "picked up again (p.pickup) " + err);
        await Settle(ledger, me, "the pickup");
        Check(CountOf(ledger.Bag(me), "food:water") == waterNow && WorldStateSync.InventoryOverwrites == 0 && WorldStateSync.Overwrites == overwrites,
            "the ledger has it back: 0 overwrites in all of normal play");

        Section("a changed bag goes back");
        int waters = CountOf(bag, "food:water");
        Check(bag.TryAdd(ItemOf("food:water"), 5, out err), "5 water made up on this PC");
        await WaitFor(() => WorldStateSync.InventoryOverwrites == 1, 6, "the Ledger after its layout");
        await Awaitable.NextFrameAsync();
        Check(CountOf(bag, "food:water") == waters && InventoryLedgerRules.SameContents(bag, ledger.Bag(me)) && WorldStateSync.Overwrites == overwrites + 1,
            "the made-up water is written back to the ledger's bag (1 overwrite)");

        Section("cash notes saved and back");
        long carried = CashService.CarriedTotal(bag);
        await Settle(ledger, me, "before leaving");
        string mine = InventorySnapshot.Export(bag, false);
        host.Stop();
        await WaitFor(() => !client.Connected && !CharacterSync.Active, 5, "leaving the server");
        store = WorldStore.Open(ServerSettings.WorldsFolder, settings.world);
        Check(Read(store, me, out var saved) && saved.inventory == mine && saved.inventory.Contains("cash:"), "world.dat has the owner's bag as its screen had it, notes included (" + carried + "원)");
        var check = new InventoryState();
        Check(InventorySnapshot.Import(check, saved.inventory) && CashService.CarriedTotal(check) == carried, "and it reads back with the notes");
        int counted = WorldStateSync.Overwrites;
        host = SyncHost.StartInApp(settings, true);
        await WaitFor(() => client.Connected && CharacterSync.Active && WorldStateSync.World, 30, "back on the restarted server");
        bag = InventoryManager.Instance.State;
        Check(CashService.CarriedTotal(bag) == carried && InventorySnapshot.Export(bag, false) == mine, "joining again: the same notes (" + carried + "원) and the same layout");
        await Awaitable.WaitForSecondsAsync(2.5f);
        Check(WorldStateSync.Overwrites == counted && WorldStateSync.LayoutsSent > 0, "and nothing is written over after joining again");
    }

    // ================= server process (InventoryLedgerQA.RunProcess) =================

    static async Task InventoryProcessBody(string exe)
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
        var settings = new ServerSettings { name = "InventoryLedgerQA 프로세스", maxPlayers = 4, port = serverPort, world = WorldStore.NewId() };
        File.WriteAllText(settingsFile, JsonUtility.ToJson(settings));
        string worldsRoot = Path.Combine(serverFolder, "worlds");

        Section("a new character on the remote client");
        SetWorldClock(worldsRoot, settings.world, Day + 10 * 60);
        await StartProcessServer("A");
        Check(await TitleFlow.JoinServer("127.0.0.1:" + serverPort, "", false) == null && GameSession.Mode == SessionMode.Client, "the Unity client joins the server process (Client)");
        Check(await TitleFlow.CreateServerCharacter("QA인벤원격", "male") == null, "its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        string me = GameSession.LocalPlayerId;
        var bag = InventoryManager.Instance.State;
        await WaitFor(() => WorldStateSync.Ledgers > 0, 10, "the first Ledger");
        await Awaitable.WaitForSecondsAsync(2.5f);
        Check(WorldStateSync.Overwrites == 0 && WorldStateSync.InventoryOverwrites == 0, "joining as a new character: nothing written over (its start bag settles with the server)");

        Section("cash and the convenience store");
        int ledgers = WorldStateSync.Ledgers;
        Check(CashService.TryWithdraw(bag, 10000, 2, out string err) && CashService.TryWithdraw(bag, 1000, 3, out err), "withdraw 10,000원 x 2 and 1,000원 x 3 " + err);
        await WaitFor(() => WorldStateSync.Ledgers >= ledgers + 2, 5, "the Ledgers");
        var player = PlayerSpawner.Player;
        // 12.10.5: the server gave this new character a deed too; it goes on the ground, so the cart and the cursor have room.
        var dropper = player.GetComponent<PlayerInventory>();
        Check(dropper.OpenInventory(), "inventory open");
        bool deedDropped = WorldDroppedItem.TryDropStorage(dropper, SlotOf(bag, s => InventoryLedgerRules.IsStartingDeed(s.Item)), out err);
        dropper.CloseInventory();
        Check(deedDropped, "the deed dropped (p.drop) " + err);
        ledgers = WorldStateSync.Ledgers;
        await WaitFor(() => WorldStateSync.Ledgers > ledgers, 5, "its Ledger");
        Check(SceneLoadManager.TryLoadMap("Assets/Scenes/Interiors/ConvenienceStoreInterior.unity", "store_entry", player), "into the convenience store");
        await WaitFor(() => !SceneLoadManager.IsLoading && SceneLoadManager.CurrentMap.name == "ConvenienceStoreInterior" && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled, 60, "the convenience store");
        await Awaitable.WaitForSecondsAsync(1f);
        var trader = Object.FindAnyObjectByType<NpcTrader>();
        Check(trader && trader.shopId == Store && trader.Offers?.Length == 10, "the clerk's NpcTrader names shop " + Store + " (10 offers from the catalog)");
        await StandBeside(player.GetComponent<PlayerMovement>(), trader.transform.position);
        var interaction = PlayerInteraction.Local;
        Check(interaction.TryTradeWithNearest() && interaction.TradeUI && interaction.TradeUI.Session.ShopId == Store, "the trade window opens from the clerk");
        var window = interaction.TradeUI;
        var session = window.Session;
        Check(session.TryAddToCart(Gimbap, out err) && session.TryChangeCart(session.Cart[0], 1, out err) && session.TryAddToCart(Water, out err), "a cart of 2 gimbap and a water " + err);
        long cash = CashService.CarriedTotal(bag);
        ledgers = WorldStateSync.Ledgers;
        Check(session.TryCheckout(out err) && CashService.CarriedTotal(bag) == cash - 3700 && CountOf(bag, "food:gimbap") == 2, "checkout: 3,700원 paid, shown before any answer " + err);
        window.Refresh();
        await Shot("I_remote_cart.png", .3f);
        await WaitFor(() => WorldStateSync.Ledgers > ledgers, 5, "the server's Ledger");
        await Awaitable.WaitForSecondsAsync(2f);
        Check(WorldStateSync.InventoryOverwrites == 0, "the server agreed: nothing written over");
        Check(session.TryTakeOffer(LunchBox, out err) && session.TryPlace(EmptySlot(bag), out err), "a lunch box by the cursor " + err);
        interaction.CloseTrade();
        int a = SlotOf(bag, s => s.Item.itemId == "food:water"), b = SlotOf(bag, s => s.Item.itemId == "food:gimbap");
        Check(bag.TryMove(a, b, out err) && bag.GetSlot(a).Item.itemId == "food:gimbap", "a move (water and gimbap swap places) " + err);
        ledgers = WorldStateSync.Ledgers;
        await WaitFor(() => WorldStateSync.Ledgers > ledgers + 1, 6, "the Ledgers");
        await Awaitable.WaitForSecondsAsync(2f);
        Check(WorldStateSync.InventoryOverwrites == 0 && WorldStateSync.Overwrites == 0, "trade, cursor, move: 0 overwrites");

        Section("a changed bag goes back (remote)");
        var inventory = player.GetComponent<PlayerInventory>();
        int gimbaps = CountOf(bag, "food:gimbap");
        Check(inventory.OpenInventory(), "the inventory open");
        // The layout would go (and come back refused) within a frame here: hold it a moment for the picture.
        typeof(WorldStateSync).GetField("nextBookScan", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, Time.unscaledTime + 3f);
        Check(bag.TryAdd(ItemOf("food:gimbap"), 10, out err) && CountOf(bag, "food:gimbap") == gimbaps + 10, "10 gimbap made up on this PC " + err);
        await Shot("I_bag_changed.png", .3f);
        await WaitFor(() => WorldStateSync.InventoryOverwrites == 1, 6, "the Ledger after its layout");
        await Awaitable.NextFrameAsync();
        Check(CountOf(bag, "food:gimbap") == gimbaps, "10 made-up gimbap are written back (1 overwrite)");
        await Shot("I_bag_restored.png", .3f);
        inventory.CloseInventory();
        await Awaitable.WaitForSecondsAsync(2f);
        string screen = InventorySnapshot.Export(bag, false);
        await LeaveRemote(client);
        await StopProcessServer();
        var store = WorldStore.Open(worldsRoot, settings.world);
        Check(Read(store, me, out var saved) && saved.inventory == screen, "the server's world.dat (after a restart) has the bag the Unity client showed, slot for slot");

        Section("joining again");
        int counted = WorldStateSync.Overwrites;
        await StartProcessServer("B");
        await Rejoin(client);
        bag = InventoryManager.Instance.State;
        Check(InventorySnapshot.Export(bag, false) == screen, "joining again brings the same layout");
        await Awaitable.WaitForSecondsAsync(2.5f);
        Check(WorldStateSync.Overwrites == counted, "and nothing is written over");
        await LeaveRemote(client);
        await StopProcessServer();
    }
}
