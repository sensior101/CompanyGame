using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum LibraryTradeMode { Buy, Borrow, Withdraw }

/// <summary>
/// The library ledger: works (published originals, sales, royalties), loans and accounts (fines, penalty points).
/// Off a sync-layer server (playing alone off a server, the old room's host) it is this PC's library.json, decided here.
/// On a sync-layer server (Docs/Multiplayer_Redesign.md 12.10.6) the server decides it (ServerLibrary): this class is then
/// a read-only mirror of the server's library section (<see cref="ApplyMirror"/>, never written to a file), and a library
/// action of mine shows its effect on my bag and bank at once and goes out as a request (p.lib.*); the server's Ledger
/// puts back whatever it did not take. The rules (overdue days, fines, recall, season royalties, publishing) are the
/// pure functions below, on a Ledger, shared with the server.
/// </summary>
public static class LibraryCatalog
{
    public const long BlankPrice = 1000, BookPrice = 20000, PublicationFee = 10000, Royalty = 4000;
    public const int FreeDays = 10, RecallDay = 30, DailyFine = 200, RecallPenalty = 5;
    [Serializable] public sealed class Work
    {
        public string id, originalInstanceId;
        public BookInstanceData book;
        public bool listed = true;
        public long royalties;
        public int sales;
    }
    [Serializable] public sealed class Loan
    {
        public string id, workId, borrowerId, instanceId;
        public int startDay, assessedDays;
        public bool active = true;
    }
    [Serializable] public sealed class Account
    {
        public string playerId;
        public long pendingPayment, fines;
        public int penaltyPoints;
    }
    [Serializable] public sealed class Ledger
    {
        public int version = 1, lastDay = -1;
        public List<Work> works = new List<Work>();
        public List<Loan> loans = new List<Loan>();
        public List<Account> accounts = new List<Account>();

        /// <summary>Lists a JSON without them leaves null are made empty.</summary>
        public Ledger Normalized()
        {
            works ??= new List<Work>(); loans ??= new List<Loan>(); accounts ??= new List<Account>();
            works.RemoveAll(w => w == null); loans.RemoveAll(l => l == null); accounts.RemoveAll(a => a == null);
            return this;
        }
    }

    // ================= the rules: pure functions on a ledger (ServerLibrary decides with the same) =================

    public static Account AccountOf(Ledger l, string id)
    {
        var account = l.accounts.Find(x => x.playerId == id);
        if (account != null) return account;
        account = new Account { playerId = id }; l.accounts.Add(account); return account;
    }
    public static Work FindWork(Ledger l, string id) => string.IsNullOrEmpty(id) ? null : l.works.Find(x => x.id == id);
    public static Loan ActiveLoanOf(Ledger l, string playerId) => l.loans.Find(x => x.active && x.borrowerId == playerId);
    public static bool IsAvailable(Ledger l, string workId) => FindWork(l, workId)?.listed == true && !l.loans.Exists(x => x.workId == workId && x.active);
    public static int OverdueDays(Loan loan, int day) => Math.Max(0, Math.Min(RecallDay, day - loan.startDay) - FreeDays);

    /// <summary>The fine of the overdue days not assessed yet goes on the borrower's account.</summary>
    public static void Assess(Ledger l, Loan loan, int day)
    {
        int days = OverdueDays(loan, day);
        if (days <= loan.assessedDays) return;
        AccountOf(l, loan.borrowerId).fines += (days - loan.assessedDays) * DailyFine;
        loan.assessedDays = days;
    }

    /// <summary>
    /// A book <paramref name="playerId"/> wrote (finished, its own, not a loan) may go to the library; one that came back
    /// from it (withdrawn) only as the same work again. The pages are checked by the caller (the server has them there).
    /// </summary>
    public static bool CanPublish(Ledger l, BookInstanceData book, string instanceId, string playerId)
    {
        if (book == null || string.IsNullOrEmpty(instanceId) || !book.isPublished || !book.IsAuthor(playerId) || book.IsLibraryLoan) return false;
        if (string.IsNullOrEmpty(book.libraryWorkId)) return true;
        var previous = FindWork(l, book.libraryWorkId);
        return previous != null && !previous.listed && previous.originalInstanceId == instanceId;
    }

    /// <summary>
    /// The late fee of returning <paramref name="loan"/> on <paramref name="day"/>. Paying fines clears all assessed debt, so
    /// it is capped at this loan's fee: days already paid are not charged again, older recalled loans stay separate.
    /// </summary>
    public static long ReturnFee(Ledger l, Loan loan, int day, out int overdueDays)
    {
        overdueDays = OverdueDays(loan, day);
        long unassessed = (long)Math.Max(0, overdueDays - loan.assessedDays) * DailyFine;
        return Math.Min(AccountOf(l, loan.borrowerId).fines + unassessed, (long)overdueDays * DailyFine);
    }

    /// <summary>The loan ends with its late fee paid (<see cref="ReturnFee"/>).</summary>
    public static void Return(Ledger l, Loan loan, int day, long lateFee)
    {
        Assess(l, loan, day);
        AccountOf(l, loan.borrowerId).fines -= lateFee;
        loan.active = false;
    }

    /// <summary>The work of <paramref name="book"/> (its details as given), listed: the work it came back from, else a new one.</summary>
    public static Work Publish(Ledger l, BookInstanceData book, string instanceId)
    {
        var work = FindWork(l, book.libraryWorkId);
        if (work == null)
        {
            work = new Work { id = Guid.NewGuid().ToString("N"), originalInstanceId = instanceId };
            l.works.Add(work);
        }
        work.book = book.Clone(); work.book.libraryWorkId = work.id; work.listed = true;
        return work;
    }

    /// <summary>A copy was sold: its author's royalty grows.</summary>
    public static void Sold(Work work) { work.royalties = checked(work.royalties + Royalty); work.sales++; }

    /// <summary>
    /// The author takes the work back: not listed any more, its loans are assessed and recalled (<paramref name="recall"/>
    /// removes each copy), and the royalties it gathered are returned (to be paid to the author).
    /// </summary>
    public static long Withdraw(Ledger l, Work work, int day, Action<Loan> recall)
    {
        work.listed = false;
        foreach (var loan in l.loans)
            if (loan.active && loan.workId == work.id) { Assess(l, loan, day); loan.active = false; recall?.Invoke(loan); }
        long royalty = work.royalties;
        work.royalties = 0;
        return royalty;
    }

    /// <summary>
    /// The days up to <paramref name="day"/> pass: every loan is assessed, one 30 days out is recalled (penalty points +5,
    /// <paramref name="recall"/> removes the copy), and a season that ended pays each work's royalties to its author
    /// (<paramref name="royalty"/>; the existing calendar has 20-day seasons). False when the day is not new.
    /// </summary>
    public static bool AdvanceDay(Ledger l, int day, Action<Loan> recall, Action<string, long> royalty)
    {
        if (day <= l.lastDay) return false;
        foreach (var loan in l.loans)
        {
            if (!loan.active) continue;
            Assess(l, loan, day);
            if (day - loan.startDay < RecallDay) continue;
            AccountOf(l, loan.borrowerId).penaltyPoints += RecallPenalty;
            loan.active = false;
            recall?.Invoke(loan);
        }
        if (l.lastDay >= 0 && day / GameTime.DaysPerSeason > l.lastDay / GameTime.DaysPerSeason)
            foreach (var work in l.works)
            {
                long amount = work.royalties;
                work.royalties = 0;
                if (amount > 0 && work.book != null) royalty?.Invoke(work.book.authorPlayerId, amount);
            }
        l.lastDay = day;
        return true;
    }

    /// <summary>
    /// The library section a server sends (12.10.6): the works without their pages (the server's books have them), the
    /// active loans and the accounts.
    /// </summary>
    public static string SectionJson(Ledger l)
    {
        var sent = new Ledger { version = l.version, lastDay = l.lastDay, accounts = l.accounts };
        foreach (var w in l.works)
            sent.works.Add(new Work { id = w.id, originalInstanceId = w.originalInstanceId, book = Bodyless(w.book), listed = w.listed, royalties = w.royalties, sales = w.sales });
        foreach (var loan in l.loans) if (loan.active) sent.loans.Add(loan);
        return JsonUtility.ToJson(sent);
    }

    /// <summary>A book's details without its pages (marked not loaded: its pages are fetched when it is opened).</summary>
    public static BookInstanceData Bodyless(BookInstanceData book)
    {
        if (book == null) return null;
        var copy = book.Clone();
        for (int p = 0; p < copy.pages.Length; p++) copy.pages[p] = "";
        copy.bodyLoaded = false;
        return copy;
    }

    // ================= this PC: its own library, or the server's mirror =================

    sealed class Participant
    {
        public InventoryState inventory;
        public IWallet wallet;
    }
    static Ledger local, mirror;
    static IGameClock clock;
    static readonly Dictionary<string, Participant> participants = new Dictionary<string, Participant>();
    static bool changing;
    public static event Action Changed;
    public static ItemData BookItem => Resources.Load<ItemData>("Inventory/Books/BlankBook");
    public static IReadOnlyList<Work> Works => State.works;
    public static IReadOnlyList<Loan> Loans => State.loans;
    public static int Today => GameClock.Current?.Now.absoluteDay ?? 0;
    static string SavePath => Path.Combine(Application.persistentDataPath, "library.json");

    /// <summary>On a sync-layer server with my character: the library is the server's, shown here as its mirror (12.10.6).</summary>
    public static bool Mirrored => SharedWriteGate.ServerCheck != null && SharedWriteGate.ServerCheck();

    static Ledger State => Mirrored ? mirror ??= new Ledger() : Local;
    static Ledger Local
    {
        get
        {
            if (local != null) return local;
            if (File.Exists(SavePath))
            {
                try { local = JsonUtility.FromJson<Ledger>(File.ReadAllText(SavePath)); }
                catch (Exception e) { Debug.LogError("[Library] Cannot load ledger: " + e.Message); throw; }
            }
            local = (local ?? new Ledger()).Normalized();
            return local;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        if (clock != null) clock.DayChanged -= OnDay;
        clock = null; local = mirror = null; changing = false; participants.Clear(); Changed = null;
    }

    /// <summary>The mirror as JSON (QA compares it with the server's section), or null.</summary>
    public static string MirrorJson => mirror == null ? null : JsonUtility.ToJson(mirror);

    /// <summary>The server's library section (WorldStateSync), or null when this client leaves the server. Never saved.</summary>
    public static void ApplyMirror(string json)
    {
        Ledger received = null;
        if (json != null)
        {
            try { received = JsonUtility.FromJson<Ledger>(json); }
            catch (Exception e) { Debug.LogWarning("[Library] unreadable library section: " + e.Message); return; }
            if (received == null) return;
            received.Normalized();
            foreach (var work in received.works) if (work.book != null) work.book.bodyLoaded = false; // the pages are the server's
        }
        mirror = received;
        Publish();
    }

    public static void ConnectLocal(InventoryState inventory)
    {
        if (Mirrored || !GameSession.IsAuthority) return;
        if (clock != GameClock.Current)
        {
            if (clock != null) clock.DayChanged -= OnDay;
            clock = GameClock.Current;
            if (clock != null) clock.DayChanged += OnDay;
        }
        RegisterPlayer(GameSession.LocalPlayerId, inventory, new BankWallet());
        AdvanceTo(Today);
    }
    public static void RegisterPlayer(string id, InventoryState inventory, IWallet wallet)
    {
        if (Mirrored || !GameSession.IsAuthority || string.IsNullOrEmpty(id) || inventory == null) return;
        participants[id] = new Participant { inventory = inventory, wallet = wallet };
        // Loans are authoritative here; expired/withdrawn copies in offline inventories are removed on reconnect.
        for (int i = 0; i < inventory.Capacity; i++)
        {
            var stack = inventory.GetSlot(i);
            if (!(stack.BookData?.IsLibraryLoan ?? false)) continue;
            var loan = Local.loans.Find(x => x.id == stack.BookData.libraryLoanId && x.active && x.borrowerId == id);
            if (loan == null) inventory.TryRemove(i, stack.Count, out _);
        }
        CollectPayment(id);
    }
    // The mirror is only what the server sent: a missing account is shown as an empty one, never added to it.
    public static Account GetAccount(string id) =>
        Mirrored ? State.accounts.Find(x => x.playerId == id) ?? new Account { playerId = id } : AccountOf(State, id);
    public static Work Find(string id) => FindWork(State, id);
    public static Loan BorrowedBy(string playerId) => ActiveLoanOf(State, playerId);
    public static bool HasPublishedBooks(string playerId) => State.works.Exists(x => x.listed && x.book != null && x.book.IsAuthor(playerId));
    public static bool CanReturn(ItemStack stack, string playerId)
    {
        if (stack == null || !stack.IsUniqueBook || !stack.BookData.IsLibraryLoan) return false;
        var loan = BorrowedBy(playerId);
        return loan != null && loan.id == stack.BookData.libraryLoanId && loan.instanceId == stack.InstanceId &&
            loan.workId == stack.BookData.libraryWorkId && Today - loan.startDay < RecallDay;
    }
    public static bool IsAvailable(string workId) => IsAvailable(State, workId);
    // Derive display values from the same game-day ledger used for fines/recall.
    // Book instances keep the loan identity; no duplicate countdown is saved.
    public static bool TryGetLoanStatus(BookInstanceData book, out int remainingDays, out int overdueDays, out long accruedFine)
    {
        remainingDays = overdueDays = 0; accruedFine = 0;
        if (book == null || !book.IsLibraryLoan) return false;
        var loan = State.loans.Find(x => x.active && x.id == book.libraryLoanId && x.workId == book.libraryWorkId);
        if (loan == null) return false;
        int elapsed = Math.Max(0, Today - loan.startDay);
        remainingDays = Math.Max(0, FreeDays - elapsed);
        overdueDays = OverdueDays(loan, Today);
        accruedFine = (long)overdueDays * DailyFine;
        return true;
    }
    public static bool CanOpenBorrowing(string playerId, out string message)
    {
        var loan = BorrowedBy(playerId);
        if (loan != null)
        {
            string title = Find(loan.workId)?.book?.title;
            if (string.IsNullOrWhiteSpace(title)) title = "책";
            return Fail("현재 " + KoreanText.Object(title) + " 대여중입니다. 한 권씩만 대여가 가능합니다.", out message);
        }
        bool hasPublishedBooks = false;
        foreach (var work in State.works)
        {
            if (!work.listed || work.book == null) continue;
            hasPublishedBooks = true;
            if (IsAvailable(work.id)) { message = null; return true; }
        }
        return Fail(hasPublishedBooks
            ? "죄송합니다. 현재 대여 가능한 책이 없습니다."
            : "죄송합니다. 현재 저희 도서관에 출판된 책이 없습니다.", out message);
    }
    public static bool CanPublish(ItemStack stack, string playerId) =>
        stack != null && stack.IsUniqueBook && stack.BookData.HasContent && CanPublish(State, stack.BookData, stack.InstanceId, playerId);
    static bool Guard(string playerId, InventoryState inventory, out string error)
    {
        error = null;
        // On a server only my own bag asks; the server decides (12.10.6).
        if (Mirrored)
            return playerId == GameSession.LocalPlayerId && InventoryLedgerRules.IsMine(inventory) || Fail("거래 정보를 확인해 주세요.", out error);
        if (!GameSession.IsAuthority) return Fail("도서관 거래는 호스트에서 처리해야 합니다.", out error);
        if (changing || string.IsNullOrEmpty(playerId) || inventory == null) return Fail("거래 정보를 확인해 주세요.", out error);
        if (!participants.TryGetValue(playerId, out var participant) || !ReferenceEquals(participant.inventory, inventory))
            return Fail("등록된 플레이어의 인벤토리가 아닙니다.", out error);
        return true;
    }
    // My bank on a server: the request's money shows at once; the server's Ledger settles it.
    static IWallet MyBank() { BankManager.EnsureInstance(); return new BankWallet(); }
    public static bool PublishBook(string playerId, TradeSession handoff, BankManager wallet, out string error)
    {
        var inventory = handoff?.Inventory;
        if (!Guard(playerId, inventory, out error)) return false;
        var stack = handoff.HeldStack;
        if (handoff.HasExchanged || !CanPublish(stack, playerId))
            return Fail("직접 작성한 책만 출판할 수 있습니다.", out error);
        if (!wallet || wallet.Money < PublicationFee)
            return Fail("보유 금액이 부족하여 출판이 취소되었습니다.", out error);
        if (Mirrored)
        {
            if (!wallet.TrySpend(PublicationFee, MoneyChangeReason.BookPublicationFee))
                return Fail("보유 금액이 부족하여 출판이 취소되었습니다.", out error);
            handoff.ConsumeHandoff();
            SharedWriteGate.SendPersonal("p.lib.publish", new SharedOpArgs { b = stack.InstanceId });
            return true;
        }
        changing = true;
        try
        {
            if (!wallet.TrySpend(PublicationFee, MoneyChangeReason.BookPublicationFee))
                return Fail("보유 금액이 부족하여 출판이 취소되었습니다.", out error);
            Publish(Local, stack.BookData, stack.InstanceId);
            handoff.ConsumeHandoff(); Save(); Publish(); return true;
        }
        finally { changing = false; }
    }

    // UI consumes these offers with the exact same TradeSession/TradeWindow drag path as every shop.
    public static TradeSession CreateTrade(string playerId, InventoryState inventory, LibraryTradeMode mode,
        Action<string,long> withdrawn = null)
        => CreateTrade(playerId, inventory, mode, withdrawn, null);

    public static TradeSession CreateTrade(string playerId, InventoryState inventory, LibraryTradeMode mode,
        Action<string,long> withdrawn, Action<string> borrowed)
    {
        Func<TradeOffer[]> offers = () =>
        {
            var list = new List<TradeOffer>();
            if (mode == LibraryTradeMode.Buy) list.Add(MakeOffer(playerId, inventory, mode, null, withdrawn, borrowed));
            foreach (var work in State.works)
                if (work.listed && work.book != null && (mode != LibraryTradeMode.Withdraw || work.book.IsAuthor(playerId)))
                    list.Add(MakeOffer(playerId, inventory, mode, work, withdrawn, borrowed));
            return list.ToArray();
        };
        return mode == LibraryTradeMode.Borrow ? new RentalTradeSession(inventory, offers) : new TradeSession(inventory, offers);
    }
    static TradeOffer MakeOffer(string player, InventoryState inventory, LibraryTradeMode mode, Work work, Action<string,long> withdrawn,
        Action<string> borrowed = null)
    {
        return new TradeOffer
        {
            give = new TradeItem { cash=mode==LibraryTradeMode.Buy ? (work==null?BlankPrice:BookPrice) : 0, count=1 },
            get = new TradeItem { item=BookItem, count=1, bookTemplate=work?.book.Clone() },
            fulfillment = new LibraryFulfillment(player,inventory,mode,work?.id,withdrawn,borrowed),
            isRented = mode==LibraryTradeMode.Borrow ? () => !IsAvailable(work.id) : null
        };
    }
    sealed class LibraryFulfillment : ITradeFulfillment
    {
        readonly string player,workId;
        readonly InventoryState inventory;
        readonly LibraryTradeMode mode;
        readonly Action<string,long> withdrawn;
        readonly Action<string> borrowed;
        public LibraryFulfillment(string player,InventoryState inventory,LibraryTradeMode mode,string workId,Action<string,long> withdrawn,Action<string> borrowed)
        {this.player=player;this.inventory=inventory;this.mode=mode;this.workId=workId;this.withdrawn=withdrawn;this.borrowed=borrowed;}
        public bool CanTake(out string error)
        {
            if(!Guard(player,inventory,out error))return false;
            var work=workId==null?null:Find(workId);
            if(workId!=null && (work==null || !work.listed))return Fail("판매가 종료된 책입니다.",out error);
            if(mode==LibraryTradeMode.Withdraw && (work==null || !work.book.IsAuthor(player)))return Fail("내가 출판한 책만 회수할 수 있습니다.",out error);
            if(mode==LibraryTradeMode.Borrow && (BorrowedBy(player)!=null || !IsAvailable(workId)))return Fail("대여 중이거나 이미 대여한 책이 있습니다.",out error);
            return true;
        }
        public ItemStack CreateItem()
        {
            var work=workId==null?null:Find(workId);
            if(work==null)return new ItemStack(BookItem,1);
            var data=work.book.Clone(); // on a server: without pages, fetched when opened
            if(mode!=LibraryTradeMode.Withdraw)data.permission=BookEditPermission.ReadOnly;
            data.libraryLoanId=mode==LibraryTradeMode.Borrow?Guid.NewGuid().ToString("N"):null;
            return new ItemStack(BookItem,1,mode==LibraryTradeMode.Withdraw?work.originalInstanceId:Guid.NewGuid().ToString("N")){BookData=data};
        }
        public void Complete(ItemStack item)
        {
            var work=workId==null?null:Find(workId);
            long royalty=0;
            if(Mirrored)
            {
                // My bag has it already; the server makes it on its ledger with its own copy of the pages (12.10.6).
                var request=new SharedOpArgs{a=workId??"",b=item.InstanceId??""};
                if(mode==LibraryTradeMode.Borrow)request.c=item.BookData?.libraryLoanId;
                if(mode==LibraryTradeMode.Withdraw && (royalty=work.royalties)>0)MyBank().AddMoney(royalty,MoneyChangeReason.BookRoyalty);
                SharedWriteGate.SendPersonal(mode==LibraryTradeMode.Buy?"p.lib.buy":mode==LibraryTradeMode.Borrow?"p.lib.borrow":"p.lib.withdraw",request);
            }
            else
            {
                changing=true;
                try
                {
                    if(mode==LibraryTradeMode.Buy && work!=null)Sold(work);
                    else if(mode==LibraryTradeMode.Borrow)
                        Local.loans.Add(new Loan{id=item.BookData.libraryLoanId,workId=workId,borrowerId=player,instanceId=item.InstanceId,startDay=Today});
                    else if(mode==LibraryTradeMode.Withdraw)
                    {
                        GetAccount(player).pendingPayment+=Withdraw(Local,work,Today,Recall);
                        royalty=CollectPayment(player);
                    }
                    Save();Publish();
                }
                finally{changing=false;}
            }
            if(mode==LibraryTradeMode.Withdraw)withdrawn?.Invoke(work.book.title,royalty);
            if(mode==LibraryTradeMode.Borrow)borrowed?.Invoke(item.DisplayName);
        }
    }
    static bool Deliver(string player,InventoryState inventory,string workId,LibraryTradeMode mode,out string error)
    {
        error=null;var work=workId==null?null:Find(workId);
        if(workId!=null && work==null)return Fail("등록된 책이 없습니다.",out error);
        var session=new TradeSession(inventory,new[]{MakeOffer(player,inventory,mode,work,null)});
        if(!session.TryTakeOffer(0,out error))return false;
        for(int i=0;i<inventory.Capacity;i++)if(session.TryPlace(i,out error))return true;
        session.TryCancel(out _);return false;
    }
    public static bool Purchase(string buyer,InventoryState inventory,string workId,out string error) => Deliver(buyer,inventory,workId,LibraryTradeMode.Buy,out error);
    public static bool PurchaseForNpc(string npcId,InventoryState inventory,string workId,out string error) => Purchase(npcId,inventory,workId,out error);
    public static bool Borrow(string player,InventoryState inventory,string workId,out string error) => Deliver(player,inventory,workId,LibraryTradeMode.Borrow,out error);
    public static bool Return(string playerId, out string error)
    {
        error = null;
        var loan = BorrowedBy(playerId);
        InventoryState inventory = null;
        if (Mirrored) inventory = playerId == GameSession.LocalPlayerId && InventoryManager.Instance ? InventoryManager.Instance.State : null;
        else if (participants.TryGetValue(playerId, out var participant)) inventory = participant.inventory;
        if (changing) return Fail("지금은 반납할 수 없습니다.", out error);
        if (loan == null || inventory == null) return Fail("대여한 책이 없습니다.", out error);
        var handoff = new TradeSession(inventory, Array.Empty<TradeOffer>());
        for (int slot = 0; slot < inventory.Capacity; slot++)
        {
            if (inventory.GetSlot(slot).InstanceId != loan.instanceId) continue;
            if (!handoff.TryPickUpForHandoff(slot, stack => CanReturn(stack, playerId), out error)) return false;
            bool returned = Return(playerId, handoff, out _, out _, out error);
            if (!returned) handoff.TryCancel(out _);
            return returned;
        }
        return Fail("인벤토리에서 대여한 책을 확인해 주세요.", out error);
    }
    /// <summary>
    /// Returns the loan copy held by <paramref name="handoff"/>. On a server the fee and the book leave here at once and
    /// p.lib.return goes out; true then means sent (the answer says whether the server took it, 12.10.6).
    /// </summary>
    public static bool Return(string playerId, TradeSession handoff, out int overdueDays, out long lateFee, out string error)
    {
        overdueDays = 0; lateFee = 0;
        if (!Guard(playerId, handoff?.Inventory, out error)) return false;
        if (handoff.HasExchanged || !CanReturn(handoff.HeldStack, playerId))
            return Fail("직접 대여한 책만 반납할 수 있습니다.", out error);
        var state = State;
        var loan = ActiveLoanOf(state, playerId);
        lateFee = ReturnFee(state, loan, Today, out overdueDays);
        var wallet = Mirrored ? MyBank() : participants[playerId].wallet;
        if (lateFee > 0 && (wallet == null || !wallet.CanAfford(lateFee)))
            return Fail("보유 금액이 부족하여 반납이 취소되었습니다. 연체료 " + lateFee.ToString("N0") + "원이 필요합니다.", out error);
        if (Mirrored)
        {
            if (lateFee > 0 && !wallet.TrySpend(lateFee, MoneyChangeReason.LibraryLateFee))
                return Fail("연체료 결제에 실패하여 반납이 취소되었습니다.", out error);
            string id = handoff.HeldStack.InstanceId;
            handoff.ConsumeHandoff();
            SharedWriteGate.SendPersonal("p.lib.return", new SharedOpArgs { b = id });
            return true;
        }
        changing = true;
        try
        {
            if (lateFee > 0 && !wallet.TrySpend(lateFee, MoneyChangeReason.LibraryLateFee))
                return Fail("연체료 결제에 실패하여 반납이 취소되었습니다.", out error);
            Return(state, loan, Today, lateFee);
            handoff.ConsumeHandoff();
            Save(); Publish(); return true;
        }
        finally { changing = false; }
    }
    public static bool Withdraw(string player,InventoryState inventory,string workId,out string error) => Deliver(player,inventory,workId,LibraryTradeMode.Withdraw,out error);
    public static bool PayFines(string playerId, BankManager bank, out string error)
    {
        error = null;
        if (!(Mirrored || GameSession.IsAuthority) || changing || playerId != GameSession.LocalPlayerId) return Fail("벌금을 납부할 수 없습니다.", out error);
        var account = GetAccount(playerId);
        if (account.fines <= 0) return Fail("미납 연체료가 없습니다.", out error);
        if (!bank || !bank.TrySpend(account.fines, MoneyChangeReason.LibraryLateFee)) return Fail("은행 잔액이 부족합니다.", out error);
        if (Mirrored) { SharedWriteGate.SendPersonal("p.lib.fine", new SharedOpArgs()); return true; }
        account.fines = 0; Save(); Publish(); return true;
    }
    static void OnDay(GameTime time) => AdvanceTo(time.absoluteDay);
    /// <summary>This PC's library passes the days up to <paramref name="absoluteDay"/>. On a server the server does (12.10.6).</summary>
    public static void AdvanceTo(int absoluteDay)
    {
        if (Mirrored || !GameSession.IsAuthority || changing || absoluteDay <= Local.lastDay) return;
        changing = true;
        try
        {
            AdvanceDay(Local, absoluteDay, Recall, (author, amount) => AccountOf(Local, author).pendingPayment += amount);
            Save();
            foreach (var id in new List<string>(participants.Keys)) CollectPayment(id);
            Publish();
        }
        finally { changing = false; }
    }
    // A recalled copy leaves the bag of a borrower who plays here; an offline one's goes when it comes back (RegisterPlayer).
    static void Recall(Loan loan)
    {
        if (!participants.TryGetValue(loan.borrowerId, out var participant)) return;
        var inventory = participant.inventory;
        for (int i = 0; i < inventory.Capacity; i++)
            if (inventory.GetSlot(i).InstanceId == loan.instanceId) inventory.TryRemove(i, 1, out _);
    }
    static long CollectPayment(string id)
    {
        var account = AccountOf(Local, id);
        if (account.pendingPayment <= 0 || !participants.TryGetValue(id, out var participant) || participant.wallet == null) return 0;
        long amount = account.pendingPayment;
        if (!participant.wallet.AddMoney(amount, MoneyChangeReason.BookRoyalty)) return 0;
        account.pendingPayment -= amount; Save();
        return amount;
    }
    static void Save()
    {
        if (!Application.isPlaying || local == null || Mirrored) return; // the server's mirror is never written
        SaveManager.WriteJsonFile(SavePath, JsonUtility.ToJson(local, true));
    }
    static void Publish() => Changed?.Invoke();
    static bool Fail(string message, out string error) { error = message; return false; }
}
