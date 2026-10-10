if (UnityEditor.EditorApplication.isPlaying) throw new System.Exception("Run ledger tests outside play mode.");
var result = new System.Collections.Generic.List<string>();
void Check(bool value, string message) { result.Add((value ? "PASS " : "FAIL ") + message); if (!value) throw new System.Exception(message); }
var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var ledgerField = typeof(LibraryCatalog).GetField("ledger", flags);
var peopleField = typeof(LibraryCatalog).GetField("participants", flags);
var oldLedger = ledgerField.GetValue(null);
var oldClock = GameClock.Current;
var oldMode = GameSession.Mode;
var people = (System.Collections.IDictionary)peopleField.GetValue(null);
var savedPeople = new System.Collections.Generic.List<System.Collections.DictionaryEntry>();
foreach (System.Collections.DictionaryEntry entry in people) savedPeople.Add(entry);
var oldChanging = typeof(LibraryCatalog).GetField("changing", flags).GetValue(null);
var cashEvents=new System.Collections.Generic.List<(InventoryState inventory,long delta,string purpose)>();
Action<InventoryState,long,MoneyChangeReason,string> cashObserver=(inventory,delta,reason,purpose)=>cashEvents.Add((inventory,delta,purpose));
CashService.TransactionCompleted+=cashObserver;
try
{
    people.Clear(); ledgerField.SetValue(null, new LibraryCatalog.Ledger()); GameClock.Current = null; GameSession.Begin(SessionMode.Host);
    var author=new InventoryState();var reader=new InventoryState();var other=new InventoryState();var npc=new InventoryState();
    long credited=0;
    LibraryCatalog.RegisterPlayer("writer",author,new BankWallet());
    LibraryCatalog.RegisterPlayer("reader",reader,null);LibraryCatalog.RegisterPlayer("other",other,null);LibraryCatalog.RegisterPlayer("npc",npc,null);
    LibraryCatalog.AdvanceTo(0);
    var walletGO=new GameObject("Temporary Library QA Wallet");var wallet=walletGO.AddComponent<BankManager>();
    var bankInstance=typeof(BankManager).GetField("<Instance>k__BackingField",flags);
    var previousBank=bankInstance.GetValue(null);bankInstance.SetValue(null,wallet);
    var moneyEvents=new System.Collections.Generic.List<(long delta,MoneyChangeReason reason)>();
    wallet.MoneyChanged+=(balance,delta,reason)=>{moneyEvents.Add((delta,reason));if(reason==MoneyChangeReason.BookRoyalty)credited+=delta;};
    try
    {
        var book=LibraryCatalog.BookItem;author.TryAdd(book,3,out _);
        Check(!LibraryCatalog.CanPublish(author.GetSlot(0),"writer"),"Blank book cannot be published");
        var manuscript=new BookInstanceData{title="검증 원고"};manuscript.pages[0]="원고 내용";
        Check(author.TryPublishBook(0,manuscript,"writer","작가",out int authored,out _),"Authored original exists separately from blank stack");
        string original=author.GetSlot(authored).InstanceId;
        string bookTooltip="검증 원고\n저자 : 작가";
        Check(author.GetSlot(authored).Tooltip==bookTooltip,"Inventory book tooltip includes title and author");
        Check(!author.TryPlaceStack(5,author.GetSlot(authored),false,out _,out _) && author.GetSlot(5).IsEmpty,"Inventory rejects duplicate unique identity without mutation");
        var handoff=new TradeSession(author,Array.Empty<TradeOffer>());
        Check(handoff.TryPickUp(authored,out _) && author.FindBook(original)==null,"Selected manuscript leaves inventory for NPC handoff");
        Check(TradeSession.PendingBooks(author).Any(x=>x.InstanceId==original),"Pending handoff remains recoverable in book save");
        wallet.SetMoney(9999);
        Check(!LibraryCatalog.PublishBook("writer",handoff,wallet,out _) && wallet.Money==9999,"Insufficient publication balance is not charged");
        Check(handoff.TryCancel(out _) && author.FindBook(original)!=null,"Failed or declined publication returns exact original");
        handoff.TryPickUp(authored,out _);wallet.SetMoney(10000);
        Check(LibraryCatalog.PublishBook("writer",handoff,wallet,out _) && wallet.Money==0 && credited==0,"Exactly 10000 pays publication fee; author receives no manuscript payment");
        Check(!handoff.HasCursorItem && author.FindBook(original)==null && LibraryCatalog.Works.Count==1,"Published manuscript is owned by catalogue");
        Check(!LibraryCatalog.PublishBook("writer",handoff,wallet,out _),"Repeated confirmation cannot publish or charge twice");
        Check(moneyEvents.Count(x=>x.reason==MoneyChangeReason.BookPublicationFee)==1 && moneyEvents.Single(x=>x.reason==MoneyChangeReason.BookPublicationFee).delta==-10000,"Publication emits one BankManager expense event; failure and repeated confirmation emit none");
        string work=LibraryCatalog.Works[0].id;
        reader.TryAdd(CashService.GetCurrency(20000),3,out _);reader.TryAdd(CashService.GetCurrency(1000),1,out _);
        var buy=LibraryCatalog.CreateTrade("reader",reader,LibraryTradeMode.Buy);
        Check(buy.OutputTooltip(1)==bookTooltip,"Shared sale template preserves title and author");
        Check(buy.TryTakeOffer(1,out _) && buy.HasCursorItem && LibraryCatalog.Find(work).royalties==0,"Purchase starts on shared cursor without early royalty");
        Check(!TradeSession.PendingBooks(reader).Any(),"Unpaid preview is excluded from owned book saves");
        Check(!buy.TryPlace(0,out _) && CashService.CarriedTotal(reader)==61000,"Occupied destination cannot charge payment");
        Check(buy.TryCancel(out _) && CashService.CarriedTotal(reader)==61000,"Cancelled purchase leaves money and catalogue unchanged");
        Check(cashEvents.Count==0,"Failed destination and cancelled library trade emit no cash success log");
        Check(buy.TryTakeOffer(1,out _) && buy.TryPlace(2,out _),"Dragged book can be placed in chosen inventory slot");
        Check(reader.GetSlot(2).IsUniqueBook && reader.GetSlot(2).InstanceId!=original && CashService.CarriedTotal(reader)==41000,"Purchased copy and exact physical payment preserve identity");
        Check(cashEvents.Count==1 && cashEvents[0].inventory==reader && cashEvents[0].delta==-20000 && cashEvents[0].purpose=="검증 원고 구매","Completed library trade publishes exact owner, purpose and amount through CashService");
        Check(LibraryCatalog.Find(work).royalties==4000,"Completed purchase accrues 4000 once");
        Check(buy.TryTakeOffer(0,out _) && buy.TryPlace(3,out _) && CashService.CarriedTotal(reader)==40000,"Blank book uses the same drag session at 1000");
        npc.TryAdd(CashService.GetCurrency(20000),1,out _);
        Check(LibraryCatalog.PurchaseForNpc("npc",npc,work,out _) && LibraryCatalog.Find(work).royalties==8000,"NPC uses common paid fulfilment and royalties");
        var rental=LibraryCatalog.CreateTrade("reader",reader,LibraryTradeMode.Borrow);
        var competing=LibraryCatalog.CreateTrade("other",other,LibraryTradeMode.Borrow);
        Check(rental.OutputTooltip(0)==bookTooltip,"Available rental reuses full common book tooltip");
        Check(rental is RentalTradeSession && rental.Offers[0].give.Resolve().CurrencyValue==0,"Rental extends common trade and uses actual silver zero icon");
        Check(rental.TryTakeOffer(0,out _) && competing.TryTakeOffer(0,out _),"Uncommitted previews do not claim the loan");
        Check(rental.TryPlace(4,out _) && CashService.CarriedTotal(reader)==40000,"Zero-price loan completes without zero-currency inventory item");
        Check(!competing.TryPlace(0,out _) && competing.TryCancel(out _),"Second concurrent borrower cannot commit same work");
        Check(!competing.IsOfferAvailable(0) && competing.OutputTooltip(0)==bookTooltip+"\n-----\n누군가가 대여중입니다.","Rented item retains title and author before rental status");
        var loan=LibraryCatalog.BorrowedBy("reader");
        LibraryCatalog.AdvanceTo(10);Check(LibraryCatalog.GetAccount("reader").fines==0,"First ten elapsed game days are free");
        LibraryCatalog.AdvanceTo(11);Check(LibraryCatalog.GetAccount("reader").fines==200,"Day 11 adds 200");
        LibraryCatalog.AdvanceTo(11);Check(LibraryCatalog.GetAccount("reader").fines==200,"Day events cannot charge twice");
        LibraryCatalog.AdvanceTo(19);Check(credited==0,"No royalty before season end");
        LibraryCatalog.AdvanceTo(20);Check(credited==8000,"20-day season end pays both player and NPC royalties");
        Check(wallet.Money==8000 && moneyEvents.Count(x=>x.reason==MoneyChangeReason.BookRoyalty)==1,"Season payout credits the existing BankManager wallet once with royalty reason");
        LibraryCatalog.AdvanceTo(20);Check(wallet.Money==8000 && moneyEvents.Count(x=>x.reason==MoneyChangeReason.BookRoyalty)==1,"Repeated season event cannot duplicate income or its notification");
        LibraryCatalog.AdvanceTo(30);Check(!loan.active && reader.FindBook(loan.instanceId)==null && LibraryCatalog.GetAccount("reader").penaltyPoints==5,"Day 30 recalls loan and applies five points");
        Check(LibraryCatalog.GetAccount("reader").fines==4000 && reader.GetSlot(2).IsUniqueBook,"Recall keeps fines and independently purchased book");
        Check(LibraryCatalog.Purchase("reader",reader,work,out _),"Further paid sale succeeds");
        long paidOnWithdrawal=-1;string recalledTitle=null;
        var withdraw=LibraryCatalog.CreateTrade("writer",author,LibraryTradeMode.Withdraw,(title,amount)=>{recalledTitle=title;paidOnWithdrawal=amount;});
        Check(withdraw.OutputTooltip(0)==bookTooltip,"Withdrawal reuses title and author tooltip");
        Check(withdraw.GetType()==typeof(TradeSession) && withdraw.TryTakeOffer(0,out _),"Withdrawal uses ordinary TradeSession cursor");
        Check(withdraw.TryCancel(out _) && LibraryCatalog.Find(work).listed && credited==8000,"Cancelling withdrawal leaves listing and royalties intact");
        Check(withdraw.TryTakeOffer(0,out _) && withdraw.TryPlace(authored,out _),"Dropping withdrawal returns original to chosen slot");
        Check(author.GetSlot(authored).InstanceId==original && !LibraryCatalog.Find(work).listed && credited==12000 && paidOnWithdrawal==4000 && recalledTitle=="검증 원고","Withdrawal settles and reports exact accrued royalty");
        Check(wallet.Money==12000 && moneyEvents.Count(x=>x.reason==MoneyChangeReason.BookRoyalty)==2,"Withdrawal uses the same BankManager income event as season settlement");
        Check(!LibraryCatalog.Purchase("reader",reader,work,out _) && !LibraryCatalog.PurchaseForNpc("npc",npc,work,out _),"Withdrawn works cannot be bought by players or NPCs");
        Check(LibraryCatalog.CreateTrade("other",other,LibraryTradeMode.Withdraw).Offers.Length==0,"Withdrawal offers contain only author's works");
        var free=new TradeSession(other,new[]{new TradeOffer{give=new TradeItem{cash=0,count=1},get=new TradeItem{item=book,count=1}}});
        Check(free.TryTakeOffer(0,out _) && free.TryCancel(out _) && other.GetSlot(0).IsEmpty,"Ordinary zero-price trade cancellation is safe");
        Check(free.TryTakeOffer(0,out _) && free.TryPlace(0,out _) && other.GetSlot(0).Count==1,"Ordinary zero-price trade uses same free-payment rule");
        Check(CashService.TryPayNpc(new InventoryState(),0,out _),"Shared CashService supports zero payment");
        var ordinary=new InventoryState();ordinary.TryAdd(CashService.GetCurrency(1000),4,out _);ordinary.TryAdd(book,98,out _);
        var ordinaryTrade=new TradeSession(ordinary,new[]{new TradeOffer{give=new TradeItem{cash=1000,count=1},get=new TradeItem{item=book,count=2}}});
        int logs=cashEvents.Count;
        Check(ordinaryTrade.TryTakeOffer(0,out _) && ordinaryTrade.TryCancel(out _) && cashEvents.Count==logs,"Cancelling ordinary trade emits no success log");
        Check(ordinaryTrade.TryTakeOffer(0,out _) && ordinaryTrade.TryTakeOffer(0,out _) && cashEvents.Count==logs,"Repeated cursor pickup defers cash log until placement");
        Check(ordinaryTrade.TryPlace(1,out _) && cashEvents.Count==logs+1 && cashEvents.Last().delta==-2000,"Partial placement commits stacked ordinary trades once with full payment");
        Check(ordinaryTrade.TryCancel(out _) && cashEvents.Count==logs+1 && CashService.CarriedTotal(ordinary)==2000,"Returning committed cursor remainder does not duplicate or refund transaction log");
        var sale=new TradeSession(ordinary,new[]{new TradeOffer{give=new TradeItem{item=book,count=1},get=new TradeItem{cash=500,count=1}}});
        Check(sale.TryTakeOffer(0,out _) && sale.TryPlace(3,out _) && cashEvents.Count==logs+2 && cashEvents.Last().delta==500 && cashEvents.Last().purpose.EndsWith(" 판매"),"Item sale emits positive physical income with sale purpose");
        var vehicle=Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle").First();var bikes=new InventoryState();bikes.TryAdd(vehicle,2,out _);
        string first=bikes.GetSlot(0).InstanceId,second=bikes.GetSlot(1).InstanceId;
        var trade=new TradeSession(bikes,new[]{new TradeOffer{give=new TradeItem{item=vehicle,count=1},get=new TradeItem{cash=1000,count=1}}});
        Check(trade.TryTakeOffer(0,out _) && !trade.TryTakeOffer(0,out _) && trade.TryCancel(out _) && bikes.GetSlot(0).InstanceId==first && bikes.GetSlot(1).InstanceId==second,"Shared changes preserve two vehicle identities and cancelled sales");
        // Returns use the shared filtered handoff and registered BankWallet.
        var returnClock=walletGO.AddComponent<SimpleGameClock>();
        var nowField=typeof(SimpleGameClock).GetField("now",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        GameClock.Current=returnClock;
        var returnWork=new LibraryCatalog.Work{id="return-qa-work",originalInstanceId="return-qa-original",book=LibraryCatalog.Works[0].book.Clone()};
        returnWork.book.libraryWorkId=returnWork.id;
        ((LibraryCatalog.Ledger)ledgerField.GetValue(null)).works.Add(returnWork);
        int returnCase=0;
        foreach(int elapsed in new[]{0,10,11,13,29})
        {
            int day=100+returnCase++*40;nowField.SetValue(returnClock,new GameTime(day,8));
            string borrower="return-qa-"+elapsed;
            var bag=new InventoryState();LibraryCatalog.RegisterPlayer(borrower,bag,new BankWallet());
            Check(LibraryCatalog.Borrow(borrower,bag,returnWork.id,out _),"Return fixture borrows at elapsed day "+elapsed);
            var currentLoan=LibraryCatalog.BorrowedBy(borrower);currentLoan.startDay=day-elapsed;
            if(elapsed==13)LibraryCatalog.AdvanceTo(day);
            var borrowed=bag.FindBook(currentLoan.instanceId);
            Check(LibraryCatalog.CanReturn(borrowed,borrower) && !LibraryCatalog.CanReturn(borrowed,"different-player"),"Return verifies borrower and item identity at day "+elapsed);
            var delivery=new TradeSession(bag,Array.Empty<TradeOffer>());
            Check(!delivery.TryPickUp(0,out _) && !delivery.TryPickUpForHandoff(0,_=>false,out _),"Ordinary trade and rejected filter cannot take a loan at day "+elapsed);
            Check(delivery.TryPickUpForHandoff(0,stack=>LibraryCatalog.CanReturn(stack,borrower),out _) && bag.FindBook(currentLoan.instanceId)==null,"Filtered handoff carries actual loan at day "+elapsed);
            Check(TradeSession.PendingBooks(bag).Any(x=>x.InstanceId==currentLoan.instanceId),"Selected loan remains recoverable before return at day "+elapsed);
            int lateDays=Math.Max(0,elapsed-LibraryCatalog.FreeDays);long due=lateDays*LibraryCatalog.DailyFine;
            wallet.SetMoney(due);int feeEvents=moneyEvents.Count(x=>x.reason==MoneyChangeReason.LibraryLateFee);
            Check(LibraryCatalog.Return(borrower,delivery,out int actualDays,out long actualFee,out _) && actualDays==lateDays && actualFee==due,"Return calculates only overdue days at day "+elapsed);
            Check(wallet.Money==0 && !currentLoan.active && !delivery.HasCursorItem && bag.FindBook(currentLoan.instanceId)==null && LibraryCatalog.GetAccount(borrower).fines==0,"Return settles one book and exact fee at day "+elapsed);
            Check(moneyEvents.Count(x=>x.reason==MoneyChangeReason.LibraryLateFee)==feeEvents+(due>0?1:0),"One existing fee event, or none when free, at day "+elapsed);
            Check(!LibraryCatalog.Return(borrower,delivery,out _,out _,out _) && moneyEvents.Count(x=>x.reason==MoneyChangeReason.LibraryLateFee)==feeEvents+(due>0?1:0),"Repeated return cannot charge twice at day "+elapsed);
        }
        nowField.SetValue(returnClock,new GameTime(400,8));
        var failingBag=new InventoryState();LibraryCatalog.RegisterPlayer("return-poor",failingBag,new BankWallet());
        Check(LibraryCatalog.Borrow("return-poor",failingBag,returnWork.id,out _),"Insufficient-balance fixture borrows");
        var failingLoan=LibraryCatalog.BorrowedBy("return-poor");failingLoan.startDay=387;
        var failedDelivery=new TradeSession(failingBag,Array.Empty<TradeOffer>());
        failedDelivery.TryPickUpForHandoff(0,stack=>LibraryCatalog.CanReturn(stack,"return-poor"),out _);
        wallet.SetMoney(599);int beforeFailure=moneyEvents.Count(x=>x.reason==MoneyChangeReason.LibraryLateFee);
        Check(!LibraryCatalog.Return("return-poor",failedDelivery,out _,out long needed,out _) && needed==600 && wallet.Money==599 && failingLoan.active,"One won short rejects payment and leaves loan active");
        Check(failedDelivery.TryCancel(out _) && failingBag.FindBook(failingLoan.instanceId)!=null && moneyEvents.Count(x=>x.reason==MoneyChangeReason.LibraryLateFee)==beforeFailure,"Failed return restores selected book without success event");
        wallet.SetMoney(600);
        Check(LibraryCatalog.Return("return-poor",out _) && wallet.Money==0,"Existing Return API uses same paid return path");
        var prepaidBag=new InventoryState();string prepaidPlayer=GameSession.LocalPlayerId;
        LibraryCatalog.RegisterPlayer(prepaidPlayer,prepaidBag,new BankWallet());
        Check(LibraryCatalog.Borrow(prepaidPlayer,prepaidBag,returnWork.id,out _),"Prepaid-fee fixture borrows");
        var prepaidLoan=LibraryCatalog.BorrowedBy(prepaidPlayer);prepaidLoan.startDay=387;
        LibraryCatalog.GetAccount(prepaidPlayer).fines=800;LibraryCatalog.AdvanceTo(400);
        wallet.SetMoney(1400);Check(LibraryCatalog.PayFines(prepaidPlayer,wallet,out _) && wallet.Money==0,"Existing payment clears older debt and current assessed days");
        nowField.SetValue(returnClock,new GameTime(401,8));wallet.SetMoney(200);
        var prepaidDelivery=new TradeSession(prepaidBag,Array.Empty<TradeOffer>());
        prepaidDelivery.TryPickUpForHandoff(0,stack=>LibraryCatalog.CanReturn(stack,prepaidPlayer),out _);
        Check(LibraryCatalog.Return(prepaidPlayer,prepaidDelivery,out int prepaidDays,out long remainingFee,out _) && prepaidDays==4 && remainingFee==200 && wallet.Money==0,"Return charges only newly overdue days after prior fee payment");
        GameClock.Current=null;
        GameSession.Begin(SessionMode.Client);Check(!LibraryCatalog.Purchase("reader",reader,null,out _),"Client cannot mutate host catalogue");
        int eventCount=moneyEvents.Count,cashCount=cashEvents.Count;long balanceBefore=wallet.Money,carriedBefore=CashService.CarriedTotal(ordinary);
        Check(!wallet.AddMoney(100,MoneyChangeReason.Reward) && !wallet.TrySpend(100,MoneyChangeReason.Purchase) && wallet.Money==balanceBefore && moneyEvents.Count==eventCount,"Client cannot change bank money or emit success event");
        Check(!CashService.TryPayNpc(ordinary,100,out _) && !CashService.TryPay(ordinary,new InventoryState(),100,out _) && !CashService.TryTransfer(ordinary,new InventoryState(),0,1,out _) &&
            !CashService.TryWithdraw(wallet,ordinary,100,1,out _) && !CashService.TryDeposit(wallet,ordinary,0,1,out _) && CashService.CarriedTotal(ordinary)==carriedBefore && cashEvents.Count==cashCount,"Client cash payments, transfers and bank exchanges are rejected without asset or log changes");
        Check(!ordinaryTrade.TryTakeOffer(0,out _) && CashService.CarriedTotal(ordinary)==carriedBefore && cashEvents.Count==cashCount,"Common trade requires host authority before payment or output ownership");
        GameSession.Begin(SessionMode.Host);
        var fullCash=new InventoryState();fullCash.TryAdd(CashService.GetCurrency(long.MaxValue),1,out _);
        var smallCash=new InventoryState();smallCash.TryAdd(CashService.GetCurrency(1),1,out _);
        Check(!fullCash.TryPlaceStack(1,smallCash.GetSlot(0),false,out _,out _) && fullCash.GetSlot(1).IsEmpty && CashService.CarriedTotal(fullCash)==long.MaxValue,"Placement cannot bypass physical-cash overflow checks");
        var savePath=System.IO.Path.GetFullPath("../ArtSource/WorldDistricts/CivicLibraryV4/QA/SaveManagerProbe.json");
        try
        {
            SaveManager.WriteJsonFile(savePath,"{\"version\":1}");SaveManager.WriteJsonFile(savePath,"{\"version\":2}");
            Check(System.IO.File.ReadAllText(savePath)=="{\"version\":2}" && !System.IO.File.Exists(savePath+".tmp"),"Existing SaveManager atomically creates and replaces JSON snapshots");
        }
        finally { if(System.IO.File.Exists(savePath))System.IO.File.Delete(savePath); }
        Check(DialogueManager.ReadingDuration("긴 문장을 읽을 수 있도록 충분한 시간을 줍니다.")>DialogueManager.ReadingDuration("안녕"),"Dynamic dialogue still scales reading time");
    }
    finally{UnityEngine.Object.DestroyImmediate(walletGO);bankInstance.SetValue(null,previousBank);}
    return string.Join("\n",result);
}
finally
{
    CashService.TransactionCompleted-=cashObserver;
    System.IO.Directory.CreateDirectory("../ArtSource/WorldDistricts/CivicLibraryV4/QA");
    System.IO.File.WriteAllLines("../ArtSource/WorldDistricts/CivicLibraryV4/QA/LibraryLedger.txt",result);
    ledgerField.SetValue(null,oldLedger); people.Clear(); foreach(var entry in savedPeople)people.Add(entry.Key,entry.Value);
    typeof(LibraryCatalog).GetField("changing",flags).SetValue(null,oldChanging);
    GameClock.Current=oldClock;GameSession.Begin(oldMode);
}
