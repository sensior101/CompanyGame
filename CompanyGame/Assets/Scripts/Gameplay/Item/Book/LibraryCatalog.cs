using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>The host's shared library ledger. One lending copy per work, separate from sold copies.</summary>
public enum LibraryTradeMode { Buy, Borrow, Withdraw }

public static class LibraryCatalog
{
    public const long BlankPrice = 1000, BookPrice = 20000, PublicationFee = 10000, Royalty = 4000;
    public const int FreeDays = 10, RecallDay = 30, DailyFine = 200;
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
    }
    sealed class Participant
    {
        public InventoryState inventory;
        public IWallet wallet;
    }
    static Ledger ledger;
    static IGameClock clock;
    static readonly Dictionary<string, Participant> participants = new Dictionary<string, Participant>();
    static bool changing;
    public static event Action Changed;
    public static ItemData BookItem => Resources.Load<ItemData>("Inventory/Books/BlankBook");
    public static IReadOnlyList<Work> Works => State.works;
    public static IReadOnlyList<Loan> Loans => State.loans;
    public static int Today => GameClock.Current?.Now.absoluteDay ?? 0;
    static string SavePath => Path.Combine(Application.persistentDataPath, "library.json");
    static Ledger State
    {
        get
        {
            if (ledger != null) return ledger;
            if (File.Exists(SavePath))
            {
                try { ledger = JsonUtility.FromJson<Ledger>(File.ReadAllText(SavePath)); }
                catch (Exception e) { Debug.LogError("[Library] Cannot load ledger: " + e.Message); throw; }
            }
            ledger ??= new Ledger();
            ledger.works ??= new List<Work>(); ledger.loans ??= new List<Loan>(); ledger.accounts ??= new List<Account>();
            return ledger;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        if (clock != null) clock.DayChanged -= OnDay;
        clock = null; ledger = null; changing = false; participants.Clear(); Changed = null;
    }
    public static void ConnectLocal(InventoryState inventory)
    {
        if (!GameSession.IsAuthority) return;
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
        if (!GameSession.IsAuthority || string.IsNullOrEmpty(id) || inventory == null) return;
        participants[id] = new Participant { inventory = inventory, wallet = wallet };
        // Loans are authoritative here; expired/withdrawn copies in offline inventories are removed on reconnect.
        for (int i = 0; i < inventory.Capacity; i++)
        {
            var stack = inventory.GetSlot(i);
            if (!(stack.BookData?.IsLibraryLoan ?? false)) continue;
            var loan = State.loans.Find(x => x.id == stack.BookData.libraryLoanId && x.active && x.borrowerId == id);
            if (loan == null) inventory.TryRemove(i, stack.Count, out _);
        }
        CollectPayment(id);
    }
    public static Account GetAccount(string id)
    {
        var account = State.accounts.Find(x => x.playerId == id);
        if (account != null) return account;
        account = new Account { playerId = id }; State.accounts.Add(account); return account;
    }
    public static Work Find(string id) => State.works.Find(x => x.id == id);
    public static Loan BorrowedBy(string playerId) => State.loans.Find(x => x.active && x.borrowerId == playerId);
    public static bool HasPublishedBooks(string playerId) => State.works.Exists(x => x.listed && x.book != null && x.book.IsAuthor(playerId));
    public static bool CanReturn(ItemStack stack, string playerId)
    {
        if (stack == null || !stack.IsUniqueBook || !stack.BookData.IsLibraryLoan) return false;
        var loan = BorrowedBy(playerId);
        return loan != null && loan.id == stack.BookData.libraryLoanId && loan.instanceId == stack.InstanceId &&
            loan.workId == stack.BookData.libraryWorkId && Today - loan.startDay < RecallDay;
    }
    public static bool IsAvailable(string workId) => Find(workId)?.listed == true && !State.loans.Exists(x => x.workId == workId && x.active);
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
    public static bool CanPublish(ItemStack stack, string playerId)
    {
        if (stack == null || !stack.IsUniqueBook || !stack.BookData.isPublished || !stack.BookData.HasContent ||
            !stack.BookData.IsAuthor(playerId) || stack.BookData.IsLibraryLoan) return false;
        if (string.IsNullOrEmpty(stack.BookData.libraryWorkId)) return true;
        var previous = Find(stack.BookData.libraryWorkId);
        return previous != null && !previous.listed && previous.originalInstanceId == stack.InstanceId;
    }
    static bool Guard(string playerId, InventoryState inventory, out string error)
    {
        error = null;
        if (!GameSession.IsAuthority) return Fail("도서관 거래는 호스트에서 처리해야 합니다.", out error);
        if (changing || string.IsNullOrEmpty(playerId) || inventory == null) return Fail("거래 정보를 확인해 주세요.", out error);
        if (!participants.TryGetValue(playerId, out var participant) || !ReferenceEquals(participant.inventory, inventory))
            return Fail("등록된 플레이어의 인벤토리가 아닙니다.", out error);
        return true;
    }
    public static bool PublishBook(string playerId, TradeSession handoff, BankManager wallet, out string error)
    {
        var inventory = handoff?.Inventory;
        if (!Guard(playerId, inventory, out error)) return false;
        var stack = handoff.HeldStack;
        if (handoff.HasExchanged || !CanPublish(stack, playerId))
            return Fail("직접 작성한 책만 출판할 수 있습니다.", out error);
        if (!wallet || wallet.Money < PublicationFee)
            return Fail("보유 금액이 부족하여 출판이 취소되었습니다.", out error);
        changing = true;
        try
        {
            if (!wallet.TrySpend(PublicationFee, MoneyChangeReason.BookPublicationFee))
                return Fail("보유 금액이 부족하여 출판이 취소되었습니다.", out error);
            var work = string.IsNullOrEmpty(stack.BookData.libraryWorkId) ? null : Find(stack.BookData.libraryWorkId);
            if (work == null)
            {
                work = new Work { id=Guid.NewGuid().ToString("N"), originalInstanceId=stack.InstanceId };
                State.works.Add(work);
            }
            work.book=stack.BookData.Clone();work.book.libraryWorkId=work.id;work.listed=true;
            handoff.ConsumeHandoff();Save();Publish();return true;
        }
        finally { changing=false; }
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
            var data=work.book.Clone();
            if(mode!=LibraryTradeMode.Withdraw)data.permission=BookEditPermission.ReadOnly;
            data.libraryLoanId=mode==LibraryTradeMode.Borrow?Guid.NewGuid().ToString("N"):null;
            return new ItemStack(BookItem,1,mode==LibraryTradeMode.Withdraw?work.originalInstanceId:Guid.NewGuid().ToString("N")){BookData=data};
        }
        public void Complete(ItemStack item)
        {
            var work=workId==null?null:Find(workId);
            long royalty=0;
            changing=true;
            try
            {
                if(mode==LibraryTradeMode.Buy && work!=null){work.royalties=checked(work.royalties+Royalty);work.sales++;}
                else if(mode==LibraryTradeMode.Borrow)
                    State.loans.Add(new Loan{id=item.BookData.libraryLoanId,workId=workId,borrowerId=player,instanceId=item.InstanceId,startDay=Today});
                else if(mode==LibraryTradeMode.Withdraw)
                {
                    work.listed=false;
                    foreach(var loan in State.loans)if(loan.active && loan.workId==workId){Assess(loan,Today);Recall(loan);}
                    royalty=work.royalties;GetAccount(player).pendingPayment+=royalty;work.royalties=0;
                    royalty=CollectPayment(player);
                }
                Save();Publish();
            }
            finally{changing=false;}
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
        if (!GameSession.IsAuthority || changing) return Fail("지금은 반납할 수 없습니다.", out error);
        var loan = BorrowedBy(playerId);
        if (loan == null || !participants.TryGetValue(playerId, out var participant))
            return Fail("대여한 책이 없습니다.", out error);
        var handoff = new TradeSession(participant.inventory, Array.Empty<TradeOffer>());
        for (int slot = 0; slot < participant.inventory.Capacity; slot++)
        {
            if (participant.inventory.GetSlot(slot).InstanceId != loan.instanceId) continue;
            if (!handoff.TryPickUpForHandoff(slot, stack => CanReturn(stack, playerId), out error)) return false;
            bool returned = Return(playerId, handoff, out _, out _, out error);
            if (!returned) handoff.TryCancel(out _);
            return returned;
        }
        return Fail("인벤토리에서 대여한 책을 확인해 주세요.", out error);
    }
    public static bool Return(string playerId, TradeSession handoff, out int overdueDays, out long lateFee, out string error)
    {
        overdueDays = 0; lateFee = 0;
        if (!Guard(playerId, handoff?.Inventory, out error)) return false;
        if (handoff.HasExchanged || !CanReturn(handoff.HeldStack, playerId))
            return Fail("직접 대여한 책만 반납할 수 있습니다.", out error);
        var loan = BorrowedBy(playerId);
        overdueDays = OverdueDays(loan, Today);
        long unassessed = (long)Math.Max(0, overdueDays - loan.assessedDays) * DailyFine;
        var account = GetAccount(playerId);
        // PayFines clears all assessed debt. Cap this return at this loan's fee so
        // already-paid days are not charged again, and older recalled loans remain separate.
        lateFee = Math.Min(account.fines + unassessed, (long)overdueDays * DailyFine);
        var wallet = participants[playerId].wallet;
        if (lateFee > 0 && (wallet == null || !wallet.CanAfford(lateFee)))
            return Fail("보유 금액이 부족하여 반납이 취소되었습니다. 연체료 " + lateFee.ToString("N0") + "원이 필요합니다.", out error);
        changing = true;
        try
        {
            if (lateFee > 0 && !wallet.TrySpend(lateFee, MoneyChangeReason.LibraryLateFee))
                return Fail("연체료 결제에 실패하여 반납이 취소되었습니다.", out error);
            Assess(loan, Today);
            account.fines -= lateFee;
            loan.active = false;
            handoff.ConsumeHandoff();
            Save(); Publish(); return true;
        }
        finally { changing = false; }
    }
    public static bool Withdraw(string player,InventoryState inventory,string workId,out string error) => Deliver(player,inventory,workId,LibraryTradeMode.Withdraw,out error);
    public static bool PayFines(string playerId, BankManager bank, out string error)
    {
        error = null;
        if (!GameSession.IsAuthority || changing || playerId != GameSession.LocalPlayerId) return Fail("벌금을 납부할 수 없습니다.", out error);
        var account = GetAccount(playerId);
        if (account.fines <= 0) return Fail("미납 연체료가 없습니다.", out error);
        if (!bank || !bank.TrySpend(account.fines, MoneyChangeReason.LibraryLateFee)) return Fail("은행 잔액이 부족합니다.", out error);
        account.fines = 0; Save(); Publish(); return true;
    }
    static void OnDay(GameTime time) => AdvanceTo(time.absoluteDay);
    public static void AdvanceTo(int absoluteDay)
    {
        if (!GameSession.IsAuthority || changing || absoluteDay <= State.lastDay) return;
        changing = true;
        try
        {
            foreach (var loan in State.loans)
            {
                if (!loan.active) continue;
                Assess(loan, absoluteDay);
                if (absoluteDay - loan.startDay >= RecallDay)
                { GetAccount(loan.borrowerId).penaltyPoints += 5; Recall(loan); }
            }
            // The existing calendar has 20-day seasons. Midnight after day 20 settles the completed period.
            if (State.lastDay >= 0 && absoluteDay / GameTime.DaysPerSeason > State.lastDay / GameTime.DaysPerSeason)
                foreach (var work in State.works)
                { GetAccount(work.book.authorPlayerId).pendingPayment += work.royalties; work.royalties = 0; }
            State.lastDay = absoluteDay; Save();
            foreach (var id in new List<string>(participants.Keys)) CollectPayment(id);
            Publish();
        }
        finally { changing = false; }
    }
    static int OverdueDays(Loan loan, int day) => Math.Max(0, Math.Min(RecallDay, day - loan.startDay) - FreeDays);
    static void Assess(Loan loan, int day)
    {
        int days = OverdueDays(loan, day);
        if (days <= loan.assessedDays) return;
        GetAccount(loan.borrowerId).fines += (days - loan.assessedDays) * DailyFine;
        loan.assessedDays = days;
    }
    static void Recall(Loan loan)
    {
        loan.active = false;
        if (!participants.TryGetValue(loan.borrowerId, out var participant)) return;
        var inventory = participant.inventory;
        for (int i = 0; i < inventory.Capacity; i++)
            if (inventory.GetSlot(i).InstanceId == loan.instanceId) inventory.TryRemove(i, 1, out _);
    }
    static long CollectPayment(string id)
    {
        var account = GetAccount(id);
        if (account.pendingPayment <= 0 || !participants.TryGetValue(id, out var participant) || participant.wallet == null) return 0;
        long amount = account.pendingPayment;
        if (!participant.wallet.AddMoney(amount, MoneyChangeReason.BookRoyalty)) return 0;
        account.pendingPayment -= amount; Save();
        return amount;
    }
    static void Save()
    {
        if (!Application.isPlaying) return;
        SaveManager.WriteJsonFile(SavePath, JsonUtility.ToJson(State, true));
    }
    static void Publish() => Changed?.Invoke();
    static bool Fail(string message, out string error) { error = message; return false; }
}
