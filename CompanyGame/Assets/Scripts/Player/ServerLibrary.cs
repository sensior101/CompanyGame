using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The library of a sync-layer server's world (Docs/Multiplayer_Redesign.md 12.10.6): one LibraryCatalog.Ledger (works,
/// loans, accounts), decided here with LibraryCatalog's rules, saved in the world state (WorldSaveData.library) and sent to
/// everyone as the library section (works without pages, active loans, accounts). Made and ended by ServerWorld.
/// Money and books move on the server's ledger (ServerLedger) at once, also for an account that is not connected
/// (a recall, a season's royalty); a connected one is sent its Ledger. Requests (Op event 1, counted, answered with a Ledger):
///   p.lib.buy       a: work id (empty: a blank book), b: the new book's id. A listed work: its price in the bag's cash
///                   (change as CashService.PlanPayment makes it), a read-only copy with the original's pages, its royalty +4,000
///   p.lib.borrow    a: work id, b: the new book's id, c: the loan id. Nothing borrowed yet and the work not on loan: a
///                   read-only loan copy and the loan ("누군가가 대여중입니다." for the one who came second)
///   p.lib.return    b: the book id. The sender's own active loan copy before day 30: the late fee from the bank (refused when
///                   short), the copy leaves the bag
///   p.lib.publish   b: the book id. A finished book in the bag that the sender wrote, with pages (the server's), not a loan:
///                   the fee (10,000원) from the bank, the book leaves the bag and becomes the work (a withdrawn one: the same work)
///   p.lib.withdraw  a: work id. The sender's listed work: not listed any more, its loans are assessed and recalled, its
///                   royalties go to the bank, the original back into the bag (refused when it does not fit)
///   p.lib.fine      every unpaid late fee from the bank
/// New ids are the client's (as p.trade's): only their form and that they are free are checked. Days pass with the world
/// clock (<see cref="AdvanceTo"/>, every frame from ServerWorld): fines, the 30-day recall (+5 points), season royalties.
/// Nothing here touches LibraryCatalog's own ledger, BankManager or a screen inventory: an in-app server's owner plays in
/// this very process.
/// </summary>
public sealed class ServerLibrary
{
    public static readonly HashSet<string> Kinds = new HashSet<string>
        { "p.lib.buy", "p.lib.borrow", "p.lib.return", "p.lib.publish", "p.lib.withdraw", "p.lib.fine" };

    const string BadId = "새 아이템의 식별자를 확인해 주세요.", Ended = "판매가 종료된 책입니다.";

    readonly ServerLedger personal;
    readonly ServerBooks bodies;
    readonly LibraryCatalog.Ledger ledger;
    readonly HashSet<string> touched = new HashSet<string>(); // accounts whose book or bag changed besides the requester

    /// <summary>The server's library (QA reads it).</summary>
    public LibraryCatalog.Ledger Ledger => ledger;
    /// <summary>The day the library has passed to.</summary>
    public int Today => Math.Max(0, ledger.lastDay);

    public ServerLibrary(ServerLedger personal, ServerBooks bodies, LibraryCatalog.Ledger saved)
    {
        this.personal = personal;
        this.bodies = bodies;
        ledger = (saved ?? new LibraryCatalog.Ledger()).Normalized();
        ledger.loans.RemoveAll(l => !l.active); // only loans still out matter
    }

    /// <summary>For the world state (WorldSaveData.library): the works keep their details only, the pages are the world's books.</summary>
    public LibraryCatalog.Ledger Capture() => ledger;

    /// <summary>The library section's JSON (LibraryCatalog.SectionJson).</summary>
    public string SectionJson() => LibraryCatalog.SectionJson(ledger);

    /// <summary>A speeding fine the bank could not pay (p.fine, 12.10.6): one penalty point.</summary>
    public void Penalty(string account) => LibraryCatalog.AccountOf(ledger, account).penaltyPoints++;

    /// <summary>The days up to <paramref name="day"/> pass (nothing when it is not a new day).</summary>
    public void AdvanceTo(int day)
    {
        touched.Clear();
        if (!LibraryCatalog.AdvanceDay(ledger, day, Recall, Pay)) return;
        ledger.loans.RemoveAll(l => !l.active);
        foreach (string account in touched) personal.Push(account);
        touched.Clear();
    }

    /// <summary>Decides a p.lib.* request of <paramref name="account"/>. The error (Korean), or null.</summary>
    public string Apply(uint connection, string account, string kind, string json)
    {
        var a = JsonUtility.FromJson<SharedOpArgs>(json) ?? new SharedOpArgs();
        touched.Clear();
        string error;
        switch (kind)
        {
            case "p.lib.buy": error = Buy(account, a.a, a.b); break;
            case "p.lib.borrow": error = Borrow(account, a.a, a.b, a.c); break;
            case "p.lib.return": error = Return(account, a.b); break;
            case "p.lib.publish": error = Publish(account, a.b); break;
            case "p.lib.withdraw": error = Withdraw(account, a.a); break;
            case "p.lib.fine": error = Fine(account); break;
            default: return "처리할 수 없는 요청입니다.";
        }
        ledger.loans.RemoveAll(l => !l.active);
        if (error == null) personal.Store(account);
        touched.Remove(account); // the requester gets its own Ledger with the answer
        foreach (string other in touched) personal.Push(other);
        touched.Clear();
        return error;
    }

    string Buy(string account, string workId, string id)
    {
        if (string.IsNullOrEmpty(workId)) return Take(account, LibraryCatalog.BlankPrice, null, null);
        var work = Listed(workId);
        if (work == null) return Ended;
        if (!bodies.CanCopy(work.originalInstanceId, id)) return BadId;
        var copy = CopyOf(work, null);
        string error = Take(account, LibraryCatalog.BookPrice, copy, id);
        if (error != null) return error;
        bodies.CopyBody(work.originalInstanceId, id, copy);
        LibraryCatalog.Sold(work);
        return null;
    }

    string Borrow(string account, string workId, string id, string loanId)
    {
        var work = Listed(workId);
        if (work == null) return Ended;
        if (LibraryCatalog.ActiveLoanOf(ledger, account) != null) return "이미 대여한 책이 있습니다. 한 권씩만 대여가 가능합니다.";
        if (!LibraryCatalog.IsAvailable(ledger, workId)) return "누군가가 대여중입니다.";
        if (!ServerBooks.ValidId(loanId) || ledger.loans.Exists(l => l.id == loanId) || !bodies.CanCopy(work.originalInstanceId, id)) return BadId;
        var copy = CopyOf(work, loanId);
        string error = Take(account, 0, copy, id);
        if (error != null) return error;
        bodies.CopyBody(work.originalInstanceId, id, copy);
        ledger.loans.Add(new LibraryCatalog.Loan { id = loanId, workId = work.id, borrowerId = account, instanceId = id, startDay = Today });
        return null;
    }

    string Return(string account, string id)
    {
        var loan = LibraryCatalog.ActiveLoanOf(ledger, account);
        var bag = personal.Bag(account);
        var stack = InventoryLedgerRules.FindUnique(bag, id);
        if (loan == null || stack == null || !stack.IsUniqueBook || loan.instanceId != id || stack.BookData.libraryLoanId != loan.id
            || Today - loan.startDay >= LibraryCatalog.RecallDay) return "직접 대여한 책만 반납할 수 있습니다.";
        long fee = LibraryCatalog.ReturnFee(ledger, loan, Today, out _);
        var bank = personal.Wallet(account);
        if (fee > 0 && !bank.CanAfford(fee)) return "보유 금액이 부족하여 반납이 취소되었습니다. 연체료 " + fee.ToString("N0") + "원이 필요합니다.";
        if (!RemoveBook(bag, id)) return "직접 대여한 책만 반납할 수 있습니다.";
        if (fee > 0) bank.TrySpend(fee, MoneyChangeReason.LibraryLateFee);
        LibraryCatalog.Return(ledger, loan, Today, fee);
        return null;
    }

    string Publish(string account, string id)
    {
        var bag = personal.Bag(account);
        var stack = InventoryLedgerRules.FindUnique(bag, id);
        var body = bodies.Find(id);
        // The world's copy of the book decides (its title, author and pages); the bag's says which work it came back from.
        var book = body?.Clone();
        if (book != null && stack != null && stack.IsUniqueBook) { book.libraryWorkId = stack.BookData.libraryWorkId; book.libraryLoanId = stack.BookData.libraryLoanId; }
        if (stack == null || !stack.IsUniqueBook || book == null || !book.HasContent || !LibraryCatalog.CanPublish(ledger, book, id, account))
            return "직접 작성한 책만 출판할 수 있습니다.";
        var bank = personal.Wallet(account);
        if (!bank.CanAfford(LibraryCatalog.PublicationFee)) return "보유 금액이 부족하여 출판이 취소되었습니다.";
        if (!RemoveBook(bag, id)) return "직접 작성한 책만 출판할 수 있습니다.";
        bank.TrySpend(LibraryCatalog.PublicationFee, MoneyChangeReason.BookPublicationFee);
        var work = LibraryCatalog.Publish(ledger, LibraryCatalog.Bodyless(book), id); // details only: the pages stay the original's in the world's books
        body.libraryWorkId = work.id; // withdrawn, it comes back as the same work
        return null;
    }

    string Withdraw(string account, string workId)
    {
        var work = Listed(workId);
        if (work == null) return Ended;
        if (!work.book.IsAuthor(account)) return "내가 출판한 책만 회수할 수 있습니다.";
        var original = work.book.Clone();
        original.libraryLoanId = null; original.libraryWorkId = work.id;
        string error = Take(account, 0, original, work.originalInstanceId);
        if (error != null) return error;
        long royalty = LibraryCatalog.Withdraw(ledger, work, Today, Recall);
        if (royalty > 0 && !personal.Wallet(account).AddMoney(royalty, MoneyChangeReason.BookRoyalty))
            Debug.LogError("[ServerLibrary] the royalty " + royalty + " of " + account + " did not fit the bank");
        return null;
    }

    string Fine(string account)
    {
        var debt = LibraryCatalog.AccountOf(ledger, account);
        if (debt.fines <= 0) return "미납 연체료가 없습니다.";
        if (!personal.Wallet(account).TrySpend(debt.fines, MoneyChangeReason.LibraryLateFee)) return "은행 잔액이 부족합니다.";
        debt.fines = 0;
        return null;
    }

    // ---- helpers ----

    LibraryCatalog.Work Listed(string workId)
    {
        var work = LibraryCatalog.FindWork(ledger, workId);
        return work != null && work.listed && work.book != null ? work : null;
    }

    // A reader's copy of a work: read-only, of that work, a loan's when loanId is given.
    static BookInstanceData CopyOf(LibraryCatalog.Work work, string loanId)
    {
        var copy = work.book.Clone();
        copy.permission = BookEditPermission.ReadOnly;
        copy.libraryLoanId = loanId;
        copy.libraryWorkId = work.id;
        copy.isPublished = true;
        return copy;
    }

    // The bag pays price in cash and gets one book (a blank when book is null), as a p.trade of one offer: all or nothing.
    string Take(string account, long price, BookInstanceData book, string id)
    {
        var offer = new TradeOffer
        {
            give = new TradeItem { cash = price, count = 1 },
            get = new TradeItem { item = LibraryCatalog.BookItem, count = 1, bookTemplate = book }
        };
        if (!offer.get.item) return "책을 찾을 수 없습니다.";
        var bag = personal.Bag(account);
        return InventoryLedgerRules.TryTrade(bag, new[] { offer }, new[] { (0, 1) }, book == null ? null : new[] { id }, out string error) ? null : error;
    }

    // A book leaves the bag (a loan copy too, which no transfer moves). False when the bag has no such book.
    static bool RemoveBook(InventoryState bag, string id)
    {
        for (int i = 0; i < bag.Capacity; i++)
            if (bag.GetSlot(i).IsUniqueBook && bag.GetSlot(i).InstanceId == id) return bag.TryRemove(i, 1, out _);
        return false;
    }

    // A recalled copy leaves its borrower's bag on the server, connected or not.
    void Recall(LibraryCatalog.Loan loan)
    {
        if (!RemoveBook(personal.Bag(loan.borrowerId), loan.instanceId)) return;
        personal.Store(loan.borrowerId);
        touched.Add(loan.borrowerId);
    }

    // A season's royalty goes to the author's bank on the server, connected or not.
    void Pay(string author, long amount)
    {
        if (string.IsNullOrEmpty(author)) return;
        if (!personal.Wallet(author).AddMoney(amount, MoneyChangeReason.BookRoyalty))
        {
            Debug.LogError("[ServerLibrary] the royalty " + amount + " of " + author + " did not fit the bank");
            return;
        }
        personal.Store(author);
        touched.Add(author);
    }
}
