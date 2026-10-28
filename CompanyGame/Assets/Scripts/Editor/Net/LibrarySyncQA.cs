using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using QaPeer = SyncLayerQA.QaPeer;

/// <summary>
/// QA of the library on the server (Docs/Multiplayer_Redesign.md 12.10.6), on the EconomySyncQA helpers.
///   -executeMethod LibrarySyncQA.Run         in memory (with -quit): the shared rules against the library's rules as they
///                                            were (a copy of the old LibraryCatalog code), ServerLibrary's requests and
///                                            days on a memory server, its save
///   -executeMethod LibrarySyncQA.RunPlay     play mode (WITHOUT -quit): in-app server + the Unity owner + two fake TCP players
///   -executeMethod LibrarySyncQA.RunProcess  play mode (WITHOUT -quit): a built server process + the Unity client + a fake
/// Env QA_SHOTS = folder for L_*.png (needs graphics).
/// </summary>
public static class LibrarySyncQA
{
    public static void Run() => EconomySyncQA.LibraryMemory();
    public static void RunPlay() => EconomySyncQA.StartPlay("lib-play");
    public static void RunProcess() => EconomySyncQA.BuildAndPlay("LibrarySyncQA", "lib-process");
}

public static partial class EconomySyncQA
{
    const string LibAuthor = "qa-lib-author", LibReader = "qa-lib-reader", Unknown = "처리할 수 없는 요청입니다.";

    // ================= the library's rules as they were (LibraryCatalog before 12.10.6, copied) =================

    static int OldOverdue(LibraryCatalog.Loan loan, int day) => Math.Max(0, Math.Min(30, day - loan.startDay) - 10);

    static LibraryCatalog.Account OldAccount(LibraryCatalog.Ledger l, string id)
    {
        var account = l.accounts.Find(x => x.playerId == id);
        if (account != null) return account;
        account = new LibraryCatalog.Account { playerId = id }; l.accounts.Add(account); return account;
    }

    static void OldAssess(LibraryCatalog.Ledger l, LibraryCatalog.Loan loan, int day)
    {
        int days = OldOverdue(loan, day);
        if (days <= loan.assessedDays) return;
        OldAccount(l, loan.borrowerId).fines += (days - loan.assessedDays) * 200;
        loan.assessedDays = days;
    }

    static void OldAdvanceTo(LibraryCatalog.Ledger l, int absoluteDay, List<string> recalled)
    {
        if (absoluteDay <= l.lastDay) return;
        foreach (var loan in l.loans)
        {
            if (!loan.active) continue;
            OldAssess(l, loan, absoluteDay);
            if (absoluteDay - loan.startDay >= 30)
            { OldAccount(l, loan.borrowerId).penaltyPoints += 5; loan.active = false; recalled.Add(loan.id); }
        }
        if (l.lastDay >= 0 && absoluteDay / GameTime.DaysPerSeason > l.lastDay / GameTime.DaysPerSeason)
            foreach (var work in l.works)
            { OldAccount(l, work.book.authorPlayerId).pendingPayment += work.royalties; work.royalties = 0; }
        l.lastDay = absoluteDay;
    }

    static long OldReturn(LibraryCatalog.Ledger l, LibraryCatalog.Loan loan, int today)
    {
        int overdueDays = OldOverdue(loan, today);
        long unassessed = (long)Math.Max(0, overdueDays - loan.assessedDays) * 200;
        var account = OldAccount(l, loan.borrowerId);
        long lateFee = Math.Min(account.fines + unassessed, (long)overdueDays * 200);
        OldAssess(l, loan, today);
        account.fines -= lateFee;
        loan.active = false;
        return lateFee;
    }

    // ================= in memory (LibrarySyncQA.Run) =================

    static ServerLibrary memLibrary;
    static ServerBooks memBooks;

    internal static void LibraryMemory()
    {
        tag = "LibrarySyncQA";
        Begin();
        root = Path.Combine(Path.GetTempPath(), "LibrarySyncQA-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            LibraryRulesAsBefore();
            LibraryRequests();
            Report("RESULT passed");
        }
        catch (Exception e) { Report("RESULT FAILED: " + e.Message + "\n" + e.StackTrace, true); throw; }
        finally
        {
            EndParts();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    static LibraryCatalog.Ledger Copy(LibraryCatalog.Ledger l) => JsonUtility.FromJson<LibraryCatalog.Ledger>(JsonUtility.ToJson(l));

    static void LibraryRulesAsBefore()
    {
        Section("the rules as they were");
        var start = new LibraryCatalog.Ledger();
        var book = new BookInstanceData { title = "규칙 책", authorPlayerId = "acc-a", isPublished = true }.Clone();
        start.works.Add(new LibraryCatalog.Work { id = "w1", originalInstanceId = "o1", book = book, royalties = 12000 });
        start.works.Add(new LibraryCatalog.Work { id = "w2", originalInstanceId = "o2", book = book.Clone(), royalties = 4000 });
        start.loans.Add(new LibraryCatalog.Loan { id = "l1", workId = "w1", borrowerId = "acc-b", instanceId = "i1", startDay = 3 });
        start.loans.Add(new LibraryCatalog.Loan { id = "l2", workId = "w2", borrowerId = "acc-c", instanceId = "i2", startDay = 9 });
        start.loans.Add(new LibraryCatalog.Loan { id = "l3", workId = "w1", borrowerId = "acc-d", instanceId = "i3", startDay = 0, active = false });
        start.lastDay = 2;
        var now = Copy(start);
        var old = Copy(start);
        var recalledNow = new List<string>();
        var recalledOld = new List<string>();
        var rnd = new System.Random(7);
        int day = 2, steps = 0;
        bool same = true;
        while (day < 120)
        {
            day += rnd.Next(0, 4) == 0 ? rnd.Next(5, 25) : 1; // single days and jumps
            LibraryCatalog.AdvanceDay(now, day, loan => recalledNow.Add(loan.id), (author, amount) => LibraryCatalog.AccountOf(now, author).pendingPayment += amount);
            OldAdvanceTo(old, day, recalledOld);
            steps++;
            if (JsonUtility.ToJson(now) != JsonUtility.ToJson(old) || string.Join(",", recalledNow) != string.Join(",", recalledOld)) { same = false; break; }
            if (day > 40 && !now.loans.Exists(l => l.active)) // new loans once the first are gone, so fines and recalls keep coming
            {
                var loan = new LibraryCatalog.Loan { id = "l" + steps, workId = "w2", borrowerId = "acc-b", instanceId = "i" + steps, startDay = day };
                now.loans.Add(loan); old.loans.Add(JsonUtility.FromJson<LibraryCatalog.Loan>(JsonUtility.ToJson(loan)));
                now.works[1].royalties += 4000; old.works[1].royalties += 4000;
            }
        }
        Check(same && steps > 20, "over " + steps + " day steps (single days and jumps to day " + day + ") the fines, the recalls with their points and the season royalties equal the old code's");
        Check(recalledNow.Count >= 3 && LibraryCatalog.AccountOf(now, "acc-b").penaltyPoints == recalledNow.FindAll(id => id != "l2").Count * 5, "recalls happened (" + string.Join(",", recalledNow) + "), +5 each");
        Check(LibraryCatalog.AccountOf(now, "acc-a").pendingPayment > 16000, "season royalties were paid (" + LibraryCatalog.AccountOf(now, "acc-a").pendingPayment + ")");

        Section("the late fee as it was");
        int checkedFees = 0;
        for (int returnDay = 5; returnDay < 40; returnDay += 3)
            for (int assessDay = 3; assessDay <= returnDay; assessDay += 4)
                foreach (bool paid in new[] { false, true })
                {
                    var a = new LibraryCatalog.Ledger();
                    a.loans.Add(new LibraryCatalog.Loan { id = "x", workId = "w", borrowerId = "p", instanceId = "i", startDay = 3 });
                    LibraryCatalog.AccountOf(a, "p").fines = 600; // from an older recalled loan
                    a.lastDay = 3;
                    var b = Copy(a);
                    LibraryCatalog.AdvanceDay(a, assessDay, null, null);
                    OldAdvanceTo(b, assessDay, new List<string>());
                    if (paid) { LibraryCatalog.AccountOf(a, "p").fines = 0; OldAccount(b, "p").fines = 0; } // p.lib.fine
                    var loanA = a.loans[0];
                    if (!loanA.active) continue;
                    long fee = LibraryCatalog.ReturnFee(a, loanA, returnDay, out _);
                    LibraryCatalog.Return(a, loanA, returnDay, fee);
                    long oldFee = OldReturn(b, b.loans[0], returnDay);
                    if (fee != oldFee || JsonUtility.ToJson(a) != JsonUtility.ToJson(b)) { Check(false, "return on day " + returnDay + " after day " + assessDay + (paid ? " paid" : "") + ": " + fee + " vs " + oldFee); }
                    checkedFees++;
                }
        Check(checkedFees > 50, checkedFees + " returns (days, fines assessed or paid before): the same late fee and account as the old code");
        var plain = new LibraryCatalog.Loan { startDay = 0 };
        Check(LibraryCatalog.OverdueDays(plain, 10) == 0 && LibraryCatalog.OverdueDays(plain, 11) == 1 && LibraryCatalog.OverdueDays(plain, 30) == 20 && LibraryCatalog.OverdueDays(plain, 99) == 20,
            "overdue days: none for 10 days, then one a day, at most 20");
    }

    static string Lib(QaPeer who, string kind, SharedOpArgs args)
    {
        string account = server.AccountOf(who.number);
        string error = memLibrary.Apply(who.number, account, kind, JsonUtility.ToJson(args));
        memLedger.Answered(who.number, account); // as ServerWorld answers a counted request
        server.Tick();
        return error ?? "";
    }

    static void LibraryParts(LibraryCatalog.Ledger saved = null, List<BookSave> savedBooks = null)
    {
        ulong world = server.SpawnServerEntity(WorldKey, AllMaps, EntityFlags.None, new byte[ServerWorld.SectionCount][]);
        memBooks = new ServerBooks(server, world, savedBooks);
        memLedger = new ServerLedger(server, world, memBooks);
        memLibrary = new ServerLibrary(memLedger, memBooks, saved);
        memLedger.UnpaidFine = memLibrary.Penalty;
        worldId = world; // IsLedger reads it
    }

    static void WriteBook(QaPeer who, string id, string title, string text)
    {
        string account = server.AccountOf(who.number);
        memLedger.Bag(account).TryAdd(Blank, 1, out _);
        Check(Ask(memBooks, who, "book.new", new ServerBooks.Request { id = id, title = title, permission = (int)BookEditPermission.FullEdit }) == null
              && (text == null || Ask(memBooks, who, "book.page", new ServerBooks.Request { id = id, page = 0, text = text }) == null), account + " writes " + title);
    }

    static long BankOfAcc(string account) => memLedger.Book(account).bankBalance;

    static void SetBank(string account, long money) { memLedger.Book(account).bankBalance = money; memLedger.Store(account); }

    static void LibraryRequests()
    {
        Section("p.lib.publish");
        const int Start = 21; // a season starts on day 20
        string w = WorldStore.NewId();
        StartServer(w);
        var a = Join("acc-a", "QA작가", "m1");
        var b = Join("acc-b", "QA독자", "m1");
        var c = Join("acc-c", "QA다", "m1");
        LibraryParts();
        memLibrary.AdvanceTo(Start);
        string text = Text(900, 51);
        WriteBook(a, "qa-orig", "원본 책", text);
        WriteBook(a, "qa-empty", "빈 내용", null);
        SetBank("acc-a", 5000);
        Check(Lib(a, "p.lib.publish", new SharedOpArgs { b = "qa-orig" }) == "보유 금액이 부족하여 출판이 취소되었습니다." && memLedger.Bag("acc-a").FindBook("qa-orig") != null,
            "without 10,000원 in the bank: refused, the book stays");
        SetBank("acc-a", 100000);
        Check(Lib(b, "p.lib.publish", new SharedOpArgs { b = "qa-orig" }) == "직접 작성한 책만 출판할 수 있습니다.", "someone else's book: refused");
        Check(Lib(a, "p.lib.publish", new SharedOpArgs { b = "qa-empty" }) == "직접 작성한 책만 출판할 수 있습니다.", "a book without pages: refused");
        Check(Lib(a, "p.lib.publish", new SharedOpArgs { b = "qa-orig" }) == "" && BankOfAcc("acc-a") == 90000 && memLedger.Bag("acc-a").FindBook("qa-orig") == null,
            "published: 10,000원 from the bank, the book out of the bag");
        var work = memLibrary.Ledger.works.Find(x => x.originalInstanceId == "qa-orig");
        Check(work != null && work.listed && work.book.title == "원본 책" && work.book.IsAuthor("acc-a") && !work.book.HasContent && memBooks.Find("qa-orig").pages[0] == text,
            "the work: listed, its details without pages; the pages stay the original's in the world's books");
        string section = memLibrary.SectionJson();
        Check(!section.Contains(text.Substring(0, 20)) && section.Contains("원본 책"), "the library section has no page text (" + section.Length + " chars)");
        Check(Lib(a, "p.lib.publish", new SharedOpArgs { b = "qa-orig" }) != "", "the same book again: refused (it is not in the bag)");

        Section("p.lib.buy");
        memLedger.Bag("acc-b").TryAdd(Cash(10000), 1, out _); memLedger.Bag("acc-b").TryAdd(Cash(5000), 1, out _); memLedger.Store("acc-b");
        Check(Lib(b, "p.lib.buy", new SharedOpArgs { a = work.id, b = "qa-copy-b" }) != "" && CashService.CarriedTotal(memLedger.Bag("acc-b")) == 15000 && memBooks.Find("qa-copy-b") == null,
            "15,000원 for a 20,000원 book: refused, nothing taken, no copy");
        memLedger.Bag("acc-b").TryAdd(Cash(10000), 1, out _);
        Check(Lib(b, "p.lib.buy", new SharedOpArgs { a = work.id, b = "qa-copy-b" }) == "" && CashService.CarriedTotal(memLedger.Bag("acc-b")) == 5000, "bought: 20,000원 of the bag's cash (change made)");
        var copy = memLedger.Bag("acc-b").FindBook("qa-copy-b")?.BookData;
        Check(copy != null && copy.permission == BookEditPermission.ReadOnly && copy.libraryWorkId == work.id && !copy.IsLibraryLoan && copy.IsAuthor("acc-a") && !copy.HasContent,
            "a read-only copy of the work in the bag, its author's, without pages");
        Check(SamePages(memBooks.Find("qa-copy-b"), memBooks.Find("qa-orig")) && memBooks.Find("qa-copy-b").permission == BookEditPermission.ReadOnly, "the server copied the original's pages to the copy");
        Check(work.royalties == 4000 && work.sales == 1, "the work's royalty +4,000, sales 1");
        memLedger.Bag("acc-b").TryAdd(Cash(50000), 1, out _);
        Check(Lib(b, "p.lib.buy", new SharedOpArgs { a = work.id, b = "qa-copy-b" }) == "새 아이템의 식별자를 확인해 주세요." && work.sales == 1, "an id that exists: refused");
        Check(Lib(b, "p.lib.buy", new SharedOpArgs { a = work.id, b = "" }) == "새 아이템의 식별자를 확인해 주세요.", "no id: refused");
        Check(Lib(b, "p.lib.buy", new SharedOpArgs { a = "qa-no-work", b = "qa-x" }) == "판매가 종료된 책입니다.", "an unknown work: refused");
        int blanks = Blanks(memLedger.Bag("acc-b"));
        long cash = CashService.CarriedTotal(memLedger.Bag("acc-b"));
        Check(Lib(b, "p.lib.buy", new SharedOpArgs()) == "" && Blanks(memLedger.Bag("acc-b")) == blanks + 1 && CashService.CarriedTotal(memLedger.Bag("acc-b")) == cash - 1000, "a blank book: 1,000원");
        Check(Ask(memBooks, b, "book.page", new ServerBooks.Request { id = "qa-copy-b", page = 0, text = "위조 본문" }) != null && memBooks.Find("qa-copy-b").pages[0] == text,
            "the buyer cannot write its own pages on the copy");
        Ask(memBooks, b, "book.new", new ServerBooks.Request { id = "qa-copy-b", title = "위조 제목" });
        Check(memBooks.Find("qa-copy-b").title == "원본 책", "nor make it anew");

        Section("p.lib.borrow");
        WriteBook(a, "qa-orig2", "둘째 책", Text(300, 52));
        Check(Lib(a, "p.lib.publish", new SharedOpArgs { b = "qa-orig2" }) == "" && BankOfAcc("acc-a") == 80000, "a second work");
        var work2 = memLibrary.Ledger.works.Find(x => x.originalInstanceId == "qa-orig2");
        Check(Lib(b, "p.lib.borrow", new SharedOpArgs { a = work.id, b = "qa-loan-b", c = "loan-b" }) == "", "acc-b borrows the first work");
        var loanB = memLibrary.Ledger.loans.Find(l => l.id == "loan-b");
        var lent = memLedger.Bag("acc-b").FindBook("qa-loan-b")?.BookData;
        Check(loanB != null && loanB.active && loanB.startDay == Start && lent != null && lent.libraryLoanId == "loan-b" && lent.permission == BookEditPermission.ReadOnly
              && SamePages(memBooks.Find("qa-loan-b"), memBooks.Find("qa-orig")), "a loan copy with the original's pages, the loan from day " + Start);
        Check(Lib(c, "p.lib.borrow", new SharedOpArgs { a = work.id, b = "qa-loan-c", c = "loan-c" }) == "누군가가 대여중입니다." && memLedger.Bag("acc-c").FindBook("qa-loan-c") == null,
            "acc-c asks for the same work: 누군가가 대여중입니다.");
        Check(Lib(b, "p.lib.borrow", new SharedOpArgs { a = work2.id, b = "qa-loan-b2", c = "loan-b2" }) != "" && memLedger.Bag("acc-b").FindBook("qa-loan-b2") == null, "a second loan of acc-b: refused");
        Check(Lib(c, "p.lib.borrow", new SharedOpArgs { a = work2.id, b = "qa-loan-c", c = "" }) == "새 아이템의 식별자를 확인해 주세요.", "a loan without its id: refused");
        Check(Lib(c, "p.lib.borrow", new SharedOpArgs { a = work2.id, b = "qa-loan-c", c = "loan-c" }) == "", "acc-c borrows the second work");
        Check(Lib(b, "p.lib.withdraw", new SharedOpArgs { a = work.id }) == "내가 출판한 책만 회수할 수 있습니다." && work.listed, "acc-b cannot withdraw acc-a's work");

        Section("days, fines and returns");
        var oracle = Copy(memLibrary.Ledger);
        memLibrary.AdvanceTo(Start + 15);
        OldAdvanceTo(oracle, Start + 15, new List<string>());
        Check(LibraryCatalog.AccountOf(memLibrary.Ledger, "acc-b").fines == 1000 && JsonUtility.ToJson(memLibrary.Ledger.loans) == JsonUtility.ToJson(oracle.loans)
              && JsonUtility.ToJson(memLibrary.Ledger.accounts) == JsonUtility.ToJson(oracle.accounts), "15 days later: 5 overdue days, 1,000원 assessed on acc-b, as the old code");
        SetBank("acc-b", 0);
        Check(Lib(b, "p.lib.fine", new SharedOpArgs()) == "은행 잔액이 부족합니다." && LibraryCatalog.AccountOf(memLibrary.Ledger, "acc-b").fines == 1000, "p.lib.fine with no money: refused");
        SetBank("acc-b", 50000);
        Check(Lib(b, "p.lib.fine", new SharedOpArgs()) == "" && BankOfAcc("acc-b") == 49000 && LibraryCatalog.AccountOf(memLibrary.Ledger, "acc-b").fines == 0, "p.lib.fine: 1,000원 from the bank");
        Check(Lib(b, "p.lib.fine", new SharedOpArgs()) == "미납 연체료가 없습니다.", "again: nothing to pay");
        OldAccount(oracle, "acc-b").fines = 0;
        memLibrary.AdvanceTo(Start + 17);
        OldAdvanceTo(oracle, Start + 17, new List<string>());
        Check(Lib(c, "p.lib.return", new SharedOpArgs { b = "qa-loan-b" }) == "직접 대여한 책만 반납할 수 있습니다.", "acc-c cannot return acc-b's loan");
        long oldFee = OldReturn(oracle, oracle.loans.Find(l => l.id == "loan-b"), Start + 17);
        string returned = Lib(b, "p.lib.return", new SharedOpArgs { b = "qa-loan-b" });
        Check(returned == "" && BankOfAcc("acc-b") == 49000 - oldFee && oldFee == 400 && memLedger.Bag("acc-b").FindBook("qa-loan-b") == null
              && LibraryCatalog.ActiveLoanOf(memLibrary.Ledger, "acc-b") == null, "returned on day 17: the 2 days not paid (400원, as the old code) from the bank, the copy out of the bag ("
              + returned + ", bank " + BankOfAcc("acc-b") + ", old fee " + oldFee + ")");
        SetBank("acc-c", 0);
        Check(Lib(c, "p.lib.return", new SharedOpArgs { b = "qa-loan-c" }) == "보유 금액이 부족하여 반납이 취소되었습니다. 연체료 1,400원이 필요합니다." && memLedger.Bag("acc-c").FindBook("qa-loan-c") != null,
            "acc-c without the 1,400원 fee: refused, the copy stays");

        Section("the 30-day recall and the season, offline");
        work2.royalties = 0;
        memLedger.Bag("acc-b").TryAdd(Cash(50000), 1, out _); memLedger.Store("acc-b");
        Check(Lib(b, "p.lib.buy", new SharedOpArgs { a = work2.id, b = "qa-copy-b2" }) == "" && work2.royalties == 4000, "acc-b buys the second work (+4,000 royalty)");
        c.client.Close(); a.client.Close();
        server.Tick();
        long authorBank = BankOfAcc("acc-a");
        long royalties = work.royalties + work2.royalties;
        memLibrary.AdvanceTo(Start + 30);
        var recalled = new List<string>();
        OldAdvanceTo(oracle, Start + 30, recalled);
        Check(memLedger.Bag("acc-c").FindBook("qa-loan-c") == null && LibraryCatalog.AccountOf(memLibrary.Ledger, "acc-c").penaltyPoints == 5
              && LibraryCatalog.AccountOf(memLibrary.Ledger, "acc-c").fines == LibraryCatalog.AccountOf(oracle, "acc-c").fines && recalled.Contains("loan-c"),
            "day 30 for acc-c (not connected): the copy left its bag on the server, +5 points, fines " + LibraryCatalog.AccountOf(memLibrary.Ledger, "acc-c").fines + " as the old code");
        Check(CharacterSync.Read(server.World.Find("acc-c").content, out var savedC) && !savedC.inventory.Contains("qa-loan-c"), "and its saved content has no copy");
        Check(BankOfAcc("acc-a") == authorBank + royalties && work.royalties == 0 && work2.royalties == 0 && royalties == 8000,
            "the season ended (day 40): acc-a (not connected) has the 8,000원 royalties in its bank");
        Check(CharacterSync.Read(server.World.Find("acc-a").content, out var savedA) && BankOf(savedA) == authorBank + royalties, "and in its saved content");
        Check(memLibrary.Ledger.loans.TrueForAll(l => l.active) && memLibrary.Ledger.loans.Count == 0, "ended loans are not kept");

        Section("p.lib.withdraw");
        a = Join("acc-a", "QA작가", "m1");
        memLedger.Bag("acc-b").TryAdd(Cash(50000), 1, out _);
        Check(Lib(b, "p.lib.buy", new SharedOpArgs { a = work.id, b = "qa-copy-b3" }) == "" && Lib(b, "p.lib.borrow", new SharedOpArgs { a = work.id, b = "qa-loan-b4", c = "loan-b4" }) == "",
            "acc-b buys the first work again and borrows it");
        var aBag = memLedger.Bag("acc-a");
        var filler = Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle")[0];
        for (int i = 0; EmptySlot(aBag) >= 0; i++) aBag.TryAdd(filler, 1, out _, "qa-lib-fill-" + i);
        Check(Lib(a, "p.lib.withdraw", new SharedOpArgs { a = work.id }) != "" && work.listed && memLedger.Bag("acc-b").FindBook("qa-loan-b4") != null, "with a full bag: refused, nothing changed");
        for (int i; (i = SlotOf(aBag, s => s.InstanceId != null && s.InstanceId.StartsWith("qa-lib-fill-"))) >= 0;) aBag.TryRemove(i, 1, out _);
        authorBank = BankOfAcc("acc-a");
        Clear(a, b);
        Check(Lib(a, "p.lib.withdraw", new SharedOpArgs { a = work.id }) == "" && !work.listed && BankOfAcc("acc-a") == authorBank + 4000 && work.royalties == 0,
            "withdrawn: not listed, the 4,000원 royalty in the bank");
        var back = aBag.FindBook("qa-orig");
        Check(back != null && back.BookData.libraryWorkId == work.id && back.BookData.IsAuthor("acc-a") && back.BookData.permission == BookEditPermission.FullEdit,
            "the original is back in the author's bag (the same id, its own permission)");
        Check(memLedger.Bag("acc-b").FindBook("qa-loan-b4") == null && LibraryCatalog.ActiveLoanOf(memLibrary.Ledger, "acc-b") == null, "acc-b's loan was recalled: the copy left its bag");
        b.Pump();
        Check(b.Has(m => IsLedger(m)), "acc-b (connected) got its Ledger");
        Check(Ask(memBooks, a, "book.page", new ServerBooks.Request { id = "qa-orig", page = 1, text = "다시 씀" }) == null, "the author can write on it again");
        Check(Lib(a, "p.lib.publish", new SharedOpArgs { b = "qa-orig" }) == "" && work.listed && memLibrary.Ledger.works.FindAll(x => x.originalInstanceId == "qa-orig").Count == 1,
            "published again: the same work, listed");

        Section("p.fine without money");
        SetBank("acc-b", 0);
        int points = LibraryCatalog.AccountOf(memLibrary.Ledger, "acc-b").penaltyPoints;
        Check(memLedger.Apply(b.number, "acc-b", "p.fine", JsonUtility.ToJson(new SharedOpArgs { n = 30000 })) == null && LibraryCatalog.AccountOf(memLibrary.Ledger, "acc-b").penaltyPoints == points + 1,
            "a fine the bank cannot pay: one penalty point on the server's library");
        SetBank("acc-b", 40000);
        Check(memLedger.Apply(b.number, "acc-b", "p.fine", JsonUtility.ToJson(new SharedOpArgs { n = 30000 })) == null && BankOfAcc("acc-b") == 10000
              && LibraryCatalog.AccountOf(memLibrary.Ledger, "acc-b").penaltyPoints == points + 1, "one it can: paid, no point");

        Section("saved and back");
        string before = memLibrary.SectionJson();
        var state = JsonUtility.FromJson<WorldSaveData>(JsonUtility.ToJson(new WorldSaveData { library = memLibrary.Capture(), books = memBooks.Capture() }));
        EndParts();
        server.Stop();
        StartServer(w);
        LibraryParts(state.library, state.books);
        string after = memLibrary.SectionJson();
        int at = 0;
        while (at < Math.Min(before.Length, after.Length) && before[at] == after[at]) at++;
        Check(after == before && memBooks.Find("qa-copy-b")?.pages[0] == text, "a restarted server has the same library and the copies' pages"
              + (after == before ? "" : " (differs at " + at + ": " + before.Substring(Math.Max(0, at - 40), Math.Min(120, before.Length - Math.Max(0, at - 40))) + " | " + after.Substring(Math.Max(0, at - 40), Math.Min(120, after.Length - Math.Max(0, at - 40))) + ")"));
        EndParts();
        server.Stop();
    }

    // ================= in-app server (LibrarySyncQA.RunPlay) =================

    // The server's clock to noon of day lib.Today + days (in-app: AdvanceClock); the library's days pass on the way.
    static async Task PassDays(SyncHost host, ServerLibrary lib, int days)
    {
        int target = lib.Today + days;
        double now = host.Server.World.WorldMinutes, to = target * Day + 12 * 60;
        host.Server.AdvanceClock((long)((to - now) * GameServer.RealSecondsPerGameHour * 1000 / 60));
        await WaitFor(() => lib.Today >= target && GameClock.Current.Now.absoluteDay >= target, 30, "day " + target);
        await Awaitable.WaitForSecondsAsync(1.2f); // the library section
    }

    static bool MirrorIsServer(ServerLibrary lib) => LibraryCatalog.MirrorJson == lib.SectionJson();

    static async Task FakeWrites(QaPeer fake, string account, ServerLedger ledger, string id, string title, string text)
    {
        if (ledger != null) Give(ledger, account, Blank, 1);
        Check((await FakeBookOp(fake, "book.new", new ServerBooks.Request { id = id, title = title, permission = (int)BookEditPermission.FullEdit })).error == ""
              && (await FakeBookOp(fake, "book.page", new ServerBooks.Request { id = id, page = 0, text = text })).error == "", account + " writes " + title);
    }

    static async Task LibraryPlayBody()
    {
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy && WorldSave.Baseline != null, 15, "title");
        ProfileSlot.Set(Slot);
        var client = SyncClient.Instance;
        string libraryFile = Path.Combine(Application.persistentDataPath, "library.json"), libraryHash = Hash(libraryFile);

        Section("the server's library");
        var settings = new ServerSettings { name = "LibraryQA", maxPlayers = 4, port = 0, world = WorldStore.NewId() };
        var host = SyncHost.StartInApp(settings, true);
        playWorld = host.Server.World.Folder;
        await WaitFor(() => client.Connected && client.CharacterKnown, 10, "the Unity client on the in-app server");
        Check(await TitleFlow.CreateServerCharacter("QA도서관", "male") == null, "the Unity owner made its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        string map = SceneLoadManager.CurrentMap.path, me = GameSession.LocalPlayerId;
        var ledger = ServerWorld.Instance.Ledger;
        var lib = ServerWorld.Instance.Library;
        var books = ServerWorld.Instance.Books;
        var bag = InventoryManager.Instance.State;
        var bank = BankManager.Instance;
        await WaitFor(() => WorldStateSync.LayoutsSent > 0 && WorldStateSync.Ledgers > 0, 10, "the first layout and its Ledger");
        await Settle(ledger, me, "the owner's start");
        await WaitFor(() => LibraryCatalog.MirrorJson != null, 5, "the library section");
        Check(LibraryCatalog.Mirrored && LibraryCatalog.Works.Count == 0 && MirrorIsServer(lib), "the owner's library is the server's mirror (a new world: empty), not this PC's library.json");
        var author = await JoinFake(host.Port, map, LibAuthor, "QA작가");
        var reader = await JoinFake(host.Port, map, LibReader, "QA독자");

        Section("publish, buy, withdraw: the money");
        string text = Text(900, 61);
        await FakeWrites(author, LibAuthor, ledger, "qa-lib-orig", "작가의 책", text);
        long authorStart = ledger.Book(LibAuthor).bankBalance;
        long ownerBank = bank.Money;
        string ownerBag = InventorySnapshot.Export(bag, false);
        int ownerLedgers = WorldStateSync.Ledgers;
        var a = await InvOp(author, "p.lib.publish", new SharedOpArgs { b = "qa-lib-orig" });
        var work = lib.Ledger.works.Find(x => x.originalInstanceId == "qa-lib-orig");
        Check(a.error == "" && a.ledgerFirst && a.bank == authorStart - LibraryCatalog.PublicationFee && work != null && work.listed, "the author publishes: 10,000원 from its ledger's bank (its Ledger first)");
        await WaitFor(() => LibraryCatalog.Find(work.id) != null, 5, "the work in the owner's mirror");
        Check(!LibraryCatalog.Find(work.id).book.HasContent && !LibraryCatalog.Find(work.id).book.bodyLoaded && MirrorIsServer(lib), "the mirror has it without pages; the mirror is the server's section");
        Give(ledger, LibReader, Cash(10000), 2);
        a = await InvOp(reader, "p.lib.buy", new SharedOpArgs { a = work.id, b = "qa-lib-copy-r" });
        Check(a.error == "" && CashService.CarriedTotal(ledger.Bag(LibReader)) == 0 && work.royalties == LibraryCatalog.Royalty, "the reader buys a copy: 20,000원 of its cash, the work's royalty +4,000");
        Check(SamePages(books.Find("qa-lib-copy-r"), books.Find("qa-lib-orig")), "the server copied the published pages to the copy");
        var forged = await FakeBookOp(reader, "book.page", new ServerBooks.Request { id = "qa-lib-copy-r", page = 0, text = "독자가 바꾼 본문" });
        await FakeBookOp(reader, "book.new", new ServerBooks.Request { id = "qa-lib-copy-r", title = "독자가 바꾼 제목" });
        var got = await FakeGet(reader, "qa-lib-copy-r");
        Check(forged.error != "" && SamePages(got.book, books.Find("qa-lib-orig")) && got.book.title == "작가의 책", "the reader cannot put other pages on its copy; book.get gives the published pages");
        a = await InvOp(reader, "p.lib.publish", new SharedOpArgs { b = "qa-lib-copy-r" });
        Check(a.error == "직접 작성한 책만 출판할 수 있습니다.", "someone else's book: publishing refused");
        a = await InvOp(reader, "p.lib.withdraw", new SharedOpArgs { a = work.id });
        Check(a.error == "내가 출판한 책만 회수할 수 있습니다." && work.listed, "someone else's work: withdrawing refused");
        foreach (string kind in new[] { "p.bank", "p.inv.trust" })
        {
            a = await InvOp(reader, kind, new SharedOpArgs { n = 1000000, a = kind == "p.bank" ? "book.royalty" : "library", b = InventoryLedgerRules.Pack(ledger.Bag(LibReader)) });
            Check(a.error == Unknown, kind + " is refused");
        }
        Check(WorldStateSync.Ledgers == ownerLedgers && bank.Money == ownerBank && InventorySnapshot.Export(bag, false) == ownerBag, "none of the fakes' library requests reached the owner's bank or bag");

        // The owner buys a copy too, and opens it.
        int overwrites = WorldStateSync.Overwrites;
        string err;
        Check(CashService.TryWithdraw(bag, 10000, 2, out err), "the owner takes 20,000원 from its bank " + err);
        await Settle(ledger, me, "the withdrawal");
        long cash = CashService.CarriedTotal(bag);
        Check(LibraryCatalog.Purchase(me, bag, work.id, out err) && CashService.CarriedTotal(bag) == cash - LibraryCatalog.BookPrice, "the owner buys a copy (20,000원 at once) " + err);
        var mine = FirstOf(bag, s => s.IsUniqueBook && s.BookData.libraryWorkId == work.id);
        Check(mine != null && !mine.BookData.bodyLoaded, "the copy is in its bag, its pages still on the server");
        await Settle(ledger, me, "the purchase");
        Check(InventoryLedgerRules.FindUnique(ledger.Bag(me), mine.InstanceId) != null && SamePages(books.Find(mine.InstanceId), books.Find("qa-lib-orig")) && work.royalties == 2 * LibraryCatalog.Royalty,
            "the server made the same copy (its pages the published ones), the royalty 8,000 now");
        Check(WorldStateSync.FetchBook(mine.InstanceId), "the owner opens it (book.get)");
        await WaitFor(() => !WorldStateSync.BookLoading(mine.InstanceId), 5, "the pages");
        Check(SamePages(bag.FindBook(mine.InstanceId).BookData, books.Find("qa-lib-orig")) && bag.FindBook(mine.InstanceId).BookData.bodyLoaded, "opened, the copy has the published pages");

        long authorBefore = ledger.Book(LibAuthor).bankBalance;
        a = await InvOp(author, "p.lib.withdraw", new SharedOpArgs { a = work.id });
        Check(a.error == "" && a.bank == authorBefore + 2 * LibraryCatalog.Royalty && !work.listed && InventoryLedgerRules.FindUnique(ledger.Bag(LibAuthor), "qa-lib-orig") != null,
            "the author withdraws: the 8,000원 royalties in its bank, the original back in its bag");
        Check(ledger.Book(LibAuthor).bankBalance - authorStart == -LibraryCatalog.PublicationFee + 2 * LibraryCatalog.Royalty && CashService.CarriedTotal(ledger.Bag(LibReader)) == 0
              && CashService.CarriedTotal(ledger.Bag(me)) == cash - LibraryCatalog.BookPrice,
            "the sums: the author -10,000 + 8,000, the reader and the owner -20,000 each");
        a = await InvOp(author, "p.lib.publish", new SharedOpArgs { b = "qa-lib-orig" });
        Check(a.error == "" && work.listed && lib.Ledger.works.Count == 1, "published again: the same work");

        Section("a season's royalty for an author who is not connected");
        Give(ledger, LibReader, Cash(10000), 2);
        a = await InvOp(reader, "p.lib.buy", new SharedOpArgs { a = work.id, b = "qa-lib-copy-r2" });
        Check(a.error == "" && work.royalties == LibraryCatalog.Royalty, "the reader buys another copy (+4,000)");
        author.client.Close();
        await Awaitable.WaitForSecondsAsync(.5f);
        authorBefore = ledger.Book(LibAuthor).bankBalance;
        int seasonEnd = (lib.Today / GameTime.DaysPerSeason + 1) * GameTime.DaysPerSeason;
        await PassDays(host, lib, seasonEnd - lib.Today);
        Check(ledger.Book(LibAuthor).bankBalance == authorBefore + LibraryCatalog.Royalty && work.royalties == 0 && BankOf(SavedOf(LibAuthor)) == authorBefore + LibraryCatalog.Royalty,
            "day " + seasonEnd + " (a new season): the author, not connected, has the 4,000원 royalty in its bank and its saved content");

        Section("the owner's library, nothing written over");
        overwrites = WorldStateSync.Overwrites;
        int bagOverwrites = WorldStateSync.InventoryOverwrites;
        Check(CashService.TryWithdraw(bag, 1000, 1, out err) && LibraryCatalog.Purchase(me, bag, null, out err), "a blank bought at the library " + err);
        await Settle(ledger, me, "the blank");
        var draft = new BookInstanceData { title = "주인의 책" }.Clone();
        draft.pages[0] = Text(600, 62);
        Check(bag.TryPublishBook(SlotOf(bag, s => s.Item.IsBook && !s.IsUniqueBook), draft, me, GameSession.LocalPlayerName, out int written, out err), "written on it " + err);
        string myBook = bag.GetSlot(written).InstanceId;
        await Settle(ledger, me, "the new book");
        long money = bank.Money;
        var handoff = new TradeSession(bag, Array.Empty<TradeOffer>());
        Check(handoff.TryPickUpForHandoff(written, s => LibraryCatalog.CanPublish(s, me), out err) && LibraryCatalog.PublishBook(me, handoff, bank, out err)
              && bank.Money == money - LibraryCatalog.PublicationFee && bag.FindBook(myBook) == null, "published: 10,000원 and the book gone at once " + err);
        await Settle(ledger, me, "publishing");
        var myWork = lib.Ledger.works.Find(x => x.originalInstanceId == myBook);
        Check(myWork != null && myWork.listed && ledger.Book(me).bankBalance == bank.Money, "the server has the work and the same bank");
        Give(ledger, LibReader, Cash(10000), 2);
        a = await InvOp(reader, "p.lib.buy", new SharedOpArgs { a = myWork.id, b = "qa-lib-copy-mine" });
        Check(a.error == "" && myWork.royalties == LibraryCatalog.Royalty, "the reader buys the owner's book");

        // the race: the owner has not heard of the reader's loan yet
        WorldStateSync.HoldSections = true;
        a = await InvOp(reader, "p.lib.borrow", new SharedOpArgs { a = work.id, b = "qa-lib-loan-r", c = "qa-loan-r" });
        Check(a.error == "" && LibraryCatalog.IsAvailable(work.id), "the reader borrows the author's work first; the owner's mirror does not know yet");
        int results = WorldStateSync.Results, raced = WorldStateSync.InventoryOverwrites;
        Check(LibraryCatalog.Borrow(me, bag, work.id, out err), "the owner borrows it too: in its bag at once " + err);
        string lostCopy = FirstOf(bag, s => s.IsUniqueBook && s.BookData.IsLibraryLoan)?.InstanceId;
        await WaitFor(() => WorldStateSync.Results > results, 5, "the owner's answer");
        Check(WorldStateSync.LastMessage == "누군가가 대여중입니다.", "only one: the owner is told 누군가가 대여중입니다.");
        WorldStateSync.HoldSections = false;
        await WaitFor(() => lostCopy != null && bag.FindBook(lostCopy) == null, 6, "the lost copy leaving the owner's bag");
        Check(WorldStateSync.InventoryOverwrites == raced + 1 && lib.Ledger.loans.Count == 1 && lib.Ledger.loans[0].borrowerId == LibReader, "its Ledger took the copy back (1 overwrite); the reader has the loan");
        await Shot("L_borrow_lost.png", .3f);
        a = await InvOp(reader, "p.lib.borrow", new SharedOpArgs { a = myWork.id, b = "qa-lib-loan-r2", c = "qa-loan-r2" });
        Check(a.error != "" && InventoryLedgerRules.FindUnique(ledger.Bag(LibReader), "qa-lib-loan-r2") == null, "a second loan of the reader: refused (" + a.error + ")");
        a = await InvOp(reader, "p.lib.return", new SharedOpArgs { b = "qa-lib-loan-r" });
        Check(a.error == "" && lib.Ledger.loans.Count == 0, "the reader returns it (no fee on day 0)");
        await Awaitable.WaitForSecondsAsync(1.2f);
        overwrites = WorldStateSync.Overwrites; bagOverwrites = WorldStateSync.InventoryOverwrites;

        money = bank.Money;
        long change = 0;
        Action<string, string, string> answered = (kind, json, error) => { if (kind == "p.lib.withdraw") change = WorldStateSync.LastLedgerBankChange; };
        WorldStateSync.Answered += answered;
        try
        {
            Check(LibraryCatalog.Withdraw(me, bag, myWork.id, out err) && bag.FindBook(myBook) != null && bank.Money == money + LibraryCatalog.Royalty, "the owner withdraws its work: the original and the 4,000원 at once " + err);
            await Settle(ledger, me, "withdrawing");
        }
        finally { WorldStateSync.Answered -= answered; }
        Check(change == LibraryCatalog.Royalty && !myWork.listed && ledger.Book(me).bankBalance == bank.Money, "the answer's Ledger paid the same 4,000원 (what LibraryDesk tells)");

        Check(LibraryCatalog.Borrow(me, bag, work.id, out err), "the owner borrows the author's work " + err);
        var loanCopy = FirstOf(bag, s => s.IsUniqueBook && s.BookData.IsLibraryLoan);
        await Settle(ledger, me, "borrowing");
        Check(loanCopy != null && loanCopy.TooltipDetails == "남은 대여기간 : 10일" && lib.Ledger.loans.Exists(l => l.borrowerId == me && l.instanceId == loanCopy.InstanceId), "the loan on the server; the tooltip says 10 days");
        await PassDays(host, lib, 15);
        await Settle(ledger, me, "15 days");
        Check(LibraryCatalog.GetAccount(me).fines == 5 * LibraryCatalog.DailyFine && loanCopy.TooltipDetails.Contains("연체 기간 : 5일") && MirrorIsServer(lib), "15 days later: 1,000원 assessed, shown from the mirror");
        money = bank.Money;
        Check(LibraryCatalog.PayFines(me, bank, out err) && bank.Money == money - 1000, "the owner pays its fines: 1,000원 at once " + err);
        await Settle(ledger, me, "the fines");
        Check(LibraryCatalog.AccountOf(lib.Ledger, me).fines == 0 && ledger.Book(me).bankBalance == bank.Money, "the server took them");
        await PassDays(host, lib, 2);
        money = bank.Money;
        Check(LibraryCatalog.Return(me, out err) && bank.Money == money - 400 && bag.FindBook(loanCopy.InstanceId) == null, "returned on day 17: the 400원 not paid yet at once, the copy gone " + err);
        await Settle(ledger, me, "the return");
        Check(lib.Ledger.loans.Count == 0 && ledger.Book(me).bankBalance == bank.Money, "the server took the 400원 from its bank");
        Check(WorldStateSync.Overwrites == overwrites && WorldStateSync.InventoryOverwrites == bagOverwrites,
            "buying, writing, publishing, withdrawing, borrowing, paying fines, returning: 0 overwrites");
        Check(MirrorIsServer(lib), "the owner's mirror is the server's library");

        Section("day 30, not connected");
        a = await InvOp(reader, "p.lib.borrow", new SharedOpArgs { a = work.id, b = "qa-lib-loan-r3", c = "qa-loan-r3" });
        Check(a.error == "", "the reader borrows again");
        reader.client.Close();
        await Awaitable.WaitForSecondsAsync(.5f);
        await PassDays(host, lib, 30);
        Check(InventoryLedgerRules.FindUnique(ledger.Bag(LibReader), "qa-lib-loan-r3") == null && LibraryCatalog.AccountOf(lib.Ledger, LibReader).penaltyPoints == LibraryCatalog.RecallPenalty
              && LibraryCatalog.AccountOf(lib.Ledger, LibReader).fines == 20 * LibraryCatalog.DailyFine && !SavedOf(LibReader).inventory.Contains("qa-lib-loan-r3"),
            "the reader's copy left its bag and saved content on the server; +5 points, 4,000원 fines");
        Check(LibraryCatalog.GetAccount(LibReader).penaltyPoints == LibraryCatalog.RecallPenalty, "the owner's mirror shows the reader's points too (the accounts go to everyone)");

        Section("a speeding fine on the server");
        reader = await JoinFake(host.Port, map, LibReader, "QA독자");
        ledger.Book(LibReader).bankBalance = 0;
        ledger.Store(LibReader);
        a = await InvOp(reader, "p.fine", new SharedOpArgs { n = 30000 });
        Check(a.error == "" && LibraryCatalog.AccountOf(lib.Ledger, LibReader).penaltyPoints == LibraryCatalog.RecallPenalty + 1, "a fine the bank cannot pay: one point on the server's library");
        await Awaitable.WaitForSecondsAsync(1.2f);
        Check(LibraryCatalog.GetAccount(LibReader).penaltyPoints == LibraryCatalog.RecallPenalty + 1, "and in the mirror");

        Section("leaving");
        Check(Hash(libraryFile) == libraryHash, "this PC's library.json was not written while on the server");
        host.Stop();
        await WaitFor(() => !client.Connected && !CharacterSync.Active, 5, "leaving the server");
        await Awaitable.NextFrameAsync();
        Check(!LibraryCatalog.Mirrored && LibraryCatalog.MirrorJson == null && LibraryCatalog.Find(work.id) == null, "off the server the library is this PC's again (the server's works are gone)");
    }

    // ================= server process (LibrarySyncQA.RunProcess) =================

    static async Task LibraryProcessBody(string exe)
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
        var settings = new ServerSettings { name = "LibraryQA 프로세스", maxPlayers = 4, port = serverPort, world = WorldStore.NewId() };
        File.WriteAllText(settingsFile, JsonUtility.ToJson(settings));
        string worldsRoot = Path.Combine(serverFolder, "worlds");

        Section("publishing and borrowing on a remote client");
        SetWorldClock(worldsRoot, settings.world, Day + 10 * 60);
        await StartProcessServer("A");
        Check(await TitleFlow.JoinServer("127.0.0.1:" + serverPort, "", false) == null && GameSession.Mode == SessionMode.Client, "the Unity client joins the server process (Client)");
        Check(await TitleFlow.CreateServerCharacter("QA도서관원격", "male") == null, "its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        string map = SceneLoadManager.CurrentMap.path, me = GameSession.LocalPlayerId;
        var bag = InventoryManager.Instance.State;
        var bank = BankManager.Instance;
        await WaitFor(() => WorldStateSync.Ledgers > 0 && LibraryCatalog.MirrorJson != null, 10, "the first Ledger and the library section");
        await Awaitable.WaitForSecondsAsync(2.5f);
        var author = await JoinFake(serverPort, map, LibAuthor, "QA원격작가");
        // The author writes with what a new character has: a note from its bank, a blank from the library.
        var a = await InvOp(author, "p.cash.withdraw", CashArgs(1000, 1));
        Check(a.error == "", "the author takes 1,000원 from its bank");
        a = await InvOp(author, "p.lib.buy", new SharedOpArgs());
        var authorBag = new InventoryState();
        Check(a.error == "" && InventorySnapshot.Import(authorBag, a.inventory) && Blanks(authorBag) == 1, "and buys a blank at the library (its Ledger has it)");
        string text = Text(800, 71);
        await FakeWrites(author, LibAuthor, null, "qa-remote-orig", "원격 작가의 책", text);
        a = await InvOp(author, "p.lib.publish", new SharedOpArgs { b = "qa-remote-orig" });
        Check(a.error == "", "the author publishes it");
        await WaitFor(() => LibraryCatalog.Works.Count == 1, 5, "the work on the Unity client");
        string workId = LibraryCatalog.Works[0].id;
        int overwrites = WorldStateSync.Overwrites;
        string err;
        Check(CashService.TryWithdraw(bag, 1000, 1, out err) && LibraryCatalog.Purchase(me, bag, null, out err), "the Unity client buys a blank " + err);
        await Awaitable.WaitForSecondsAsync(1.5f);
        var draft = new BookInstanceData { title = "원격 주인의 책" }.Clone();
        draft.pages[0] = Text(500, 72);
        Check(bag.TryPublishBook(SlotOf(bag, s => s.Item.IsBook && !s.IsUniqueBook), draft, me, GameSession.LocalPlayerName, out int written, out err), "writes on it " + err);
        string myBook = bag.GetSlot(written).InstanceId;
        await Awaitable.WaitForSecondsAsync(2f);
        var handoff = new TradeSession(bag, Array.Empty<TradeOffer>());
        Check(handoff.TryPickUpForHandoff(written, s => LibraryCatalog.CanPublish(s, me), out err) && LibraryCatalog.PublishBook(me, handoff, bank, out err), "and publishes it " + err);
        await WaitFor(() => LibraryCatalog.Works.Count == 2, 5, "its work in the mirror");
        Check(LibraryCatalog.Borrow(me, bag, workId, out err), "it borrows the author's work " + err);
        var loan = FirstOf(bag, s => s.IsUniqueBook && s.BookData.IsLibraryLoan);
        string loanId = loan?.InstanceId;
        await WaitFor(() => LibraryCatalog.BorrowedBy(me) != null, 5, "the loan in the mirror");
        await Awaitable.WaitForSecondsAsync(1.5f);
        Check(loan != null && loan.TooltipDetails == "남은 대여기간 : 10일" && WorldStateSync.Overwrites == overwrites, "the loan copy says 10 days left; nothing written over");
        var inv = PlayerSpawner.Player.GetComponent<PlayerInventory>();
        Check(inv.OpenInventory(), "the inventory open");
        await Awaitable.WaitForSecondsAsync(.3f);
        int slot = SlotOf(bag, s => s.InstanceId == loanId);
        var pointer = Array.Find(Object.FindObjectsByType<InventorySlotPointer>(), p => p && p.isActiveAndEnabled && !p.IsEquipment && !p.IsDropZone && p.InventoryIndex == slot);
        Check(pointer, "the slot of the loan copy (" + slot + ")");
        inv.UserInterface.ShowItemTooltip(pointer, RectTransformUtility.WorldToScreenPoint(null, pointer.transform.position));
        await Shot("L_remote_loan_tooltip.png", .4f);
        inv.UserInterface.HideItemTooltip();
        inv.CloseInventory();
        Check(WorldStateSync.FetchBook(loanId), "the copy opened (book.get)");
        await WaitFor(() => !WorldStateSync.BookLoading(loanId), 5, "its pages");
        Check(bag.FindBook(loanId).BookData.pages[0] == text, "it has the author's pages (copied on the server)");
        await LeaveRemote(client);
        await StopProcessServer();

        Section("after a restart");
        var store = WorldStore.Open(worldsRoot, settings.world);
        var saved = JsonUtility.FromJson<WorldSaveData>(SectionJson(store.WorldState));
        Check(saved.library != null && saved.library.works.Count == 2 && saved.library.loans.Count == 1 && saved.library.loans[0].borrowerId == me && saved.library.loans[0].instanceId == loanId,
            "world.dat has the library: 2 works, the Unity client's loan");
        var copyBody = saved.books.Find(x => x.id == loanId)?.book;
        Check(copyBody != null && copyBody.pages[0] == text && saved.books.Exists(x => x.id == myBook), "and the copy's pages, and the published original's");
        await StartProcessServer("B");
        await Rejoin(client);
        bag = InventoryManager.Instance.State;
        await WaitFor(() => LibraryCatalog.Works.Count == 2 && LibraryCatalog.BorrowedBy(me)?.instanceId == loanId, 10, "the library after the restart");
        var back = bag.FindBook(loanId);
        Check(back != null && back.BookData.IsLibraryLoan && !back.BookData.bodyLoaded, "the loan copy is in the bag, its pages on the server");
        Check(WorldStateSync.FetchBook(loanId), "opened again");
        await WaitFor(() => !WorldStateSync.BookLoading(loanId), 5, "its pages");
        Check(bag.FindBook(loanId).BookData.pages[0] == text, "the same pages after the restart");
        await LeaveRemote(client);
        await StopProcessServer();
    }
}
