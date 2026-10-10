if (!Application.isPlaying) throw new Exception("Run in Play Mode in CivicLibraryInterior.");
var p = PlayerInteraction.Local;
if (PlayerInventory.IsAnyOpen || p.IsTradeOpen || DialogueManager.IsDialogueOpen) throw new Exception("Close menus before running input QA");
var playerInventory = p.GetComponent<PlayerInventory>(); var inventory = playerInventory.Inventory;
var movement = p.GetComponent<PlayerMovement>(); var motor = p.GetComponent<CharacterController>();
var camera = movement.viewCamera.GetComponent<PlayerCameraController>(); var manager = DialogueManager.Instance;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
var copy = typeof(InventoryState).GetMethod("Copy",flags); var replace = typeof(InventoryState).GetMethod("ReplaceWith",flags);
var notify = typeof(InventoryState).GetMethod("NotifyChanged",flags);
var savedInventory = copy.Invoke(inventory,null); long savedBank = BankManager.Instance.Money;
var ledgerField = typeof(LibraryCatalog).GetField("ledger",statics); var savedLedger = ledgerField.GetValue(null);
var files = new System.Collections.Generic.Dictionary<string,byte[]>();
foreach (var name in new[]{"books.json","library.json"}) { var path=System.IO.Path.Combine(Application.persistentDataPath,name);files[path]=System.IO.File.Exists(path)?System.IO.File.ReadAllBytes(path):null; }
var clock = UnityEngine.Object.FindAnyObjectByType<SimpleGameClock>(); bool savedPause = clock.Paused;clock.Paused=true;
var yaw=typeof(PlayerCameraController).GetField("yaw",flags);var pitch=typeof(PlayerCameraController).GetField("pitch",flags);
float savedYaw=(float)yaw.GetValue(camera), savedPitch=(float)pitch.GetValue(camera);bool savedView=camera.firstPerson;
var savedPosition=p.transform.position;var savedRotation=p.transform.rotation;
var settings=UnityEngine.InputSystem.InputSystem.settings;var test=UnityEngine.Object.Instantiate(settings);
test.backgroundBehavior=UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
test.editorInputBehaviorInPlayMode=UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
UnityEngine.InputSystem.InputSystem.settings=test;bool background=Application.runInBackground;Application.runInBackground=true;
var originalKeyboard=UnityEngine.InputSystem.Keyboard.current;var originalMouse=UnityEngine.InputSystem.Mouse.current;
var qaKeyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("Library QA Keyboard");
var qaMouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("Library QA Mouse");
qaKeyboard.MakeCurrent();qaMouse.MakeCurrent();
var chat=UnityEngine.Object.FindAnyObjectByType<ChatUIManager>();
var chatContent=chat?(Transform)typeof(ChatUIManager).GetField("chatContent",flags).GetValue(chat):null;
var oldChatChildren=chatContent?chatContent.Cast<Transform>().Select(x=>x.gameObject).ToArray():Array.Empty<GameObject>();
var popupQueue=chat?(System.Collections.Generic.Queue<string>)typeof(ChatUIManager).GetField("popupMessages",flags).GetValue(chat):null;
var oldPopup=popupQueue?.ToArray();
ledgerField.SetValue(null,new LibraryCatalog.Ledger{lastDay=LibraryCatalog.Today});
replace.Invoke(inventory,new object[]{new InventoryState()});
inventory.TryAdd(CashService.GetCurrency(20000),3,out _);inventory.TryAdd(CashService.GetCurrency(1000),2,out _);inventory.TryAdd(LibraryCatalog.BookItem,3,out _);
var draft=new BookInstanceData{title="공통 거래 검증 원고"};draft.pages[0]="드래그 및 출판 검증용 임시 원고";
inventory.TryPublishBook(2,draft,GameSession.LocalPlayerId,GameSession.LocalPlayerName,out int authoredSlot,out _);
string original=inventory.GetSlot(authoredSlot).InstanceId;BankManager.Instance.SetMoney(50000);inventory.SelectHotbar(7);notify.Invoke(inventory,null);
var report=new System.Collections.Generic.List<string>();
Action<bool,string> check=(ok,label)=>{report.Add((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);};
Action<Vector3> move=pos=>{motor.enabled=false;p.transform.SetPositionAndRotation(pos,Quaternion.identity);motor.enabled=true;camera.firstPerson=true;yaw.SetValue(camera,0f);pitch.SetValue(camera,4f);camera.SendMessage("LateUpdate");Physics.SyncTransforms();};
var libraryNpc=UnityEngine.Object.FindObjectsByType<DialogueData>().Single(x=>x.name=="Library_Desk_Female");
move(libraryNpc.transform.position+new Vector3(-.95f,0,-.95f));
yaw.SetValue(camera,45f);camera.SendMessage("LateUpdate");
var gameView=UnityEditor.EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));gameView.Focus();
int stage=0,frame=0;float began=Time.unscaledTime;
Vector2 lastPointer=Vector2.zero,dragOrigin=Vector2.zero,dragTarget=Vector2.zero;
Func<string,Vector2> point=name=>{var r=UnityEngine.Object.FindObjectsByType<RectTransform>().FirstOrDefault(x=>x.name==name && x.gameObject.activeInHierarchy);if(r)lastPointer=RectTransformUtility.WorldToScreenPoint(null,r.position);return lastPointer;};
Func<string,TMPro.TMP_Text> label=name=>UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>().First(x=>x.name==name && x.gameObject.activeInHierarchy);
Action<string> capture=name=>ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("../ArtSource/WorldDistricts/CivicLibraryV4/QA/"+name+".png"));
var steps=new System.Collections.Generic.List<Action<int>>();
bool space=false,escape=false,click=false;Vector2 pointer=Vector2.zero;
void KeyStep(bool esc,Action verify=null){steps.Add(f=>{if(esc)escape=f==3;else space=f==3;if(f==11)verify?.Invoke();});}
void ClickStep(string target,Action verify=null){steps.Add(f=>{pointer=point(target);click=f==3;if(f==11)verify?.Invoke();});}
void HoverStep(string target,Action verify){steps.Add(f=>{pointer=point(target);if(f==11)verify();});}
void DragStep(string source,string destination,Action verify){steps.Add(f=>{
    if(f==1){dragOrigin=point(source);dragTarget=point(destination);}
    pointer=f<4?dragOrigin:f<8?Vector2.Lerp(dragOrigin,dragTarget,(f-3)/4f):dragTarget;click=f>=3 && f<8;
    if(f==11)verify();});}
void CheckStep(Action verify){steps.Add(f=>{if(f==5)verify();});}
void PublishMenu(){KeyStep(false,()=>{
    if(!manager.HasChoices)report.Add("DIAGNOSTIC position="+p.transform.position+" camera="+Camera.main.transform.position+" nearby="+(manager.Nearby?manager.Nearby.name:"none")+" current="+manager.CurrentLine+" canStart="+manager.CanStart()+" input="+GameInput.InteractHeld+" focused="+Application.isFocused+" active="+((System.Collections.IEnumerable)typeof(DialogueData).GetField("Active",statics).GetValue(null)).Cast<object>().Count());
    check(manager.HasChoices && manager.Current && manager.Current.name.StartsWith("Library_Desk_"),"Space opens the librarian's shared dialogue choices");
});ClickStep("Choice2",()=>check(InventoryItemSelector.IsOpen&&!p.IsTradeOpen,"Publication opens selector, not trade window"));}
CheckStep(()=>{}); // Let focus, camera and neutral input settle before the first key press.
PublishMenu();
CheckStep(()=>{
    check(manager.CurrentLine=="어떤 책을 출판할까요?","NPC requests manuscript");
    var buttons=InventoryItemSelector.Active.GetComponentsInChildren<UnityEngine.UI.Button>();
    check(buttons.Count(x=>x.name.StartsWith("SelectionSlot_"))==16,"Selector shows exactly two rows of eight shared inventory slots");
    check(!buttons.First(x=>x.name=="SelectionSlot_0").interactable&&!buttons.First(x=>x.name=="SelectionSlot_2").interactable&&buttons.First(x=>x.name=="SelectionSlot_"+authoredSlot).interactable,"Money and blank books excluded; own authored book selectable");
    check(label("BankBalance").text=="지갑  50,000원","Selector shows same wallet amount and label as inventory");capture("PublicationSelector");
});
HoverStep("SelectionSlot_"+authoredSlot,()=>{
    var tooltip=playerInventory.UserInterface.transform.Find("ItemTooltip");var text=tooltip.GetComponentInChildren<TMPro.TMP_Text>();
    check(tooltip.gameObject.activeInHierarchy && text.text=="공통 거래 검증 원고\n저자 : "+GameSession.LocalPlayerName,"Selector hover reuses inventory title and author tooltip");
    check(tooltip.GetComponent<Canvas>().sortingOrder>InventoryItemSelector.Active.GetComponent<Canvas>().sortingOrder && ((RectTransform)tooltip).rect.height>44f && !text.richText,"Shared multiline tooltip is visible above selector without author clipping");capture("BookSelectorTooltip");
});
ClickStep("SelectionSlot_"+authoredSlot,()=>{check(inventory.FindBook(original)==null&&InventoryItemSelector.Active.SelectedItem.InstanceId==original,"Click hands exact manuscript to NPC");check(manager.VisibleOptions.Count==2&&manager.CurrentLine.Contains("10,000원"),"NPC asks publication confirmation with title and fee");capture("PublicationConfirm");});
ClickStep("Choice1",()=>check(!InventoryItemSelector.IsOpen&&inventory.FindBook(original)!=null&&BankManager.Instance.Money==50000&&LibraryCatalog.Works.Count==0,"Decline returns book without charge or listing"));
KeyStep(true);PublishMenu();ClickStep("SelectionSlot_"+authoredSlot);CheckStep(()=>BankManager.Instance.SetMoney(9999));
ClickStep("Choice0",()=>check(inventory.FindBook(original)!=null&&BankManager.Instance.Money==9999&&manager.CurrentLine=="보유 금액이 부족하여 출판이 취소되었습니다.","Insufficient wallet cancels and returns book"));
KeyStep(true);CheckStep(()=>BankManager.Instance.SetMoney(10000));PublishMenu();ClickStep("SelectionSlot_"+authoredSlot);
ClickStep("Choice0",()=>{
    check(BankManager.Instance.Money==0&&LibraryCatalog.Works.Count==1&&inventory.FindBook(original)==null,"Exact 10000 pays publication fee and lists book");
    check(manager.CurrentLine=="출판이 완료되었습니다. 많은 독자분들이 읽어주셨으면 좋겠네요!","Publication success speech matches requested line");
    check(chat&&popupQueue.Any(x=>x.Contains("10,000원이 결제되었습니다."))&&chatContent.GetComponentsInChildren<TMPro.TMP_Text>().Any(x=>x.text.Contains("10,000원이 결제되었습니다.")),"Publication payment appears in popup and permanent chat history");
    check(chatContent.GetComponentsInChildren<TMPro.TMP_Text>().Count(x=>x.text.Contains("책 출판 비용으로 10,000원이 결제되었습니다."))==1,"Publication emits exactly one shared bank system log");
});
KeyStep(true);KeyStep(false);ClickStep("Choice0",()=>check(p.IsTradeOpen&&p.TradeUI.Session.GetType()==typeof(TradeSession),"Buying opens ordinary common trade session"));
HoverStep("Payment_1",()=>{
    check(label("Name").text=="20,000원","Payment hover shows price, not book name");
    check(p.TradeUI.GetComponentsInChildren<TMPro.TMP_Text>().Where(x=>x.name=="Amount" && x.transform.parent.name.StartsWith("Output_")).All(x=>x.text=="")&&!p.TradeUI.GetComponentsInChildren<TMPro.TMP_Text>().Any(x=>x.name=="BookTitle"),"Trade offers contain icons only, no amount/name labels");
});
HoverStep("Output_1",()=>{check(label("Name").text=="공통 거래 검증 원고\n저자 : "+GameSession.LocalPlayerName,"Purchase hover shows title and author");capture("BookPurchaseTooltip");});
DragStep("Output_1","Slot_4",()=>{check(inventory.GetSlot(4).IsUniqueBook&&CashService.CarriedTotal(inventory)==42000&&LibraryCatalog.Works[0].royalties==4000,"Actual mouse drag purchases into selected slot and accrues royalty");capture("SharedBookTrade");});
HoverStep("Slot_4",()=>check(label("Name").text=="공통 거래 검증 원고\n저자 : "+GameSession.LocalPlayerName,"Trade inventory hover matches offer tooltip"));
KeyStep(true);KeyStep(false);ClickStep("Choice1",()=>check(p.TradeUI.Session is RentalTradeSession,"Rental uses shared window with reusable rental session"));
HoverStep("Payment_0",()=>check(label("Name").text=="0원"&&p.TradeUI.Session.Offers[0].give.Resolve().CurrencyValue==0,"Rental silver coin hover is zero won"));
DragStep("Output_0","Slot_5",()=>check(inventory.GetSlot(5).BookData.IsLibraryLoan&&CashService.CarriedTotal(inventory)==42000,"Actual free rental drag needs no zero currency item"));
HoverStep("Output_0",()=>{
    check(label("Name").text=="공통 거래 검증 원고\n저자 : "+GameSession.LocalPlayerName+"\n-----\n누군가가 대여중입니다.","Rented book hover retains title and author before unavailable message");
    var icon=p.TradeUI.GetComponentsInChildren<TradePointer>().First(x=>x.name=="Output_0").transform.Find("Recess/Icon").GetComponent<UnityEngine.UI.Image>();
    check(icon.color.r<.6f,"Loaned item is visually dim with no rental label");capture("SharedRentalTrade");
});
KeyStep(true);KeyStep(false);ClickStep("Choice2",()=>{
    check(InventoryItemSelector.IsOpen && manager.CurrentLine=="어떤 책을 반납하시겠습니까?","Return opens existing inventory selector with requested question");
    var buttons=InventoryItemSelector.Active.GetComponentsInChildren<UnityEngine.UI.Button>();
    check(buttons.Count(x=>x.name.StartsWith("SelectionSlot_"))==16 && buttons.Single(x=>x.name=="SelectionSlot_5").interactable &&
        !buttons.Single(x=>x.name=="SelectionSlot_4").interactable && !buttons.Single(x=>x.name=="SelectionSlot_2").interactable,"Return selector has two rows and accepts only borrowed copy, not purchased or blank books");
    check(label("BankBalance").text=="지갑  0원","Return reuses the inventory wallet display");capture("ReturnSelector");
});
ClickStep("SelectionSlot_5",()=>{
    check(!InventoryItemSelector.IsOpen && inventory.GetSlot(5).IsEmpty && LibraryCatalog.BorrowedBy(GameSession.LocalPlayerId)==null && BankManager.Instance.Money==0,"Click returns free loan without payment");
    check(manager.CurrentLine=="공통 거래 검증 원고이 반납되었습니다. 감사합니다.","Return speech includes selected title and thanks");
    check(!chatContent.GetComponentsInChildren<TMPro.TMP_Text>().Any(x=>x.text.Contains("도서 대여 연체료 납부로")),"On-time return emits no fee success log");
});
KeyStep(true);KeyStep(false);ClickStep("Choice1");
DragStep("Output_0","Slot_5",()=>{
    check(inventory.GetSlot(5).BookData.IsLibraryLoan,"Returned copy can be borrowed again");
    LibraryCatalog.BorrowedBy(GameSession.LocalPlayerId).startDay=LibraryCatalog.Today-13;BankManager.Instance.SetMoney(599);
});
KeyStep(true);KeyStep(false);ClickStep("Choice2");ClickStep("SelectionSlot_5",()=>{
    check(!InventoryItemSelector.IsOpen && inventory.GetSlot(5).BookData.IsLibraryLoan && LibraryCatalog.BorrowedBy(GameSession.LocalPlayerId)!=null && BankManager.Instance.Money==599,"Insufficient late fee restores selected loan without changing wallet");
    check(manager.CurrentLine.Contains("보유 금액이 부족") && !chatContent.GetComponentsInChildren<TMPro.TMP_Text>().Any(x=>x.text.Contains("도서 대여 연체료 납부로")),"Failed return shows cancellation and no payment log");
    BankManager.Instance.SetMoney(600);
});
KeyStep(true);KeyStep(false);ClickStep("Choice2");ClickStep("SelectionSlot_5",()=>{
    check(inventory.GetSlot(5).IsEmpty && LibraryCatalog.BorrowedBy(GameSession.LocalPlayerId)==null && BankManager.Instance.Money==0,"Overdue return consumes book and exact 600 won");
    check(manager.CurrentLine.Contains("3일 연체되었습니다. 연체비는 총 600원입니다."),"Overdue return reports days and charged fee");
    check(chatContent.GetComponentsInChildren<TMPro.TMP_Text>().Count(x=>x.text.Contains("도서 대여 연체료 납부로 600원이 결제되었습니다."))==1 && popupQueue.Any(x=>x.Contains("도서 대여 연체료 납부로 600원이 결제되었습니다.")),"Return uses one existing BankManager chat history and popup payment log");capture("OverdueReturn");
});
KeyStep(true);KeyStep(false);ClickStep("Choice3",()=>check(p.TradeUI.Session.GetType()==typeof(TradeSession)&&p.TradeUI.Session.Offers.Length==1,"Withdrawal uses ordinary trade with only own publication"));
HoverStep("Output_0",()=>check(label("Name").text=="공통 거래 검증 원고\n저자 : "+GameSession.LocalPlayerName,"Withdrawal hover shows title and author"));
DragStep("Output_0","Slot_"+authoredSlot,()=>{
    check(inventory.GetSlot(authoredSlot).InstanceId==original&&!LibraryCatalog.Works[0].listed&&BankManager.Instance.Money==4000,"Actual withdrawal drag returns original and pays wallet royalties");
    check(manager.CurrentLine.Contains("4,000원")&&popupQueue.Any(x=>x.Contains("4,000원이 지갑에 지급되었습니다.")),"Withdrawal speech and system popup report correct payout");capture("WithdrawalSettlement");
    check(chatContent.GetComponentsInChildren<TMPro.TMP_Text>().Count(x=>x.text.Contains("도서 인세 정산으로 4,000원이 지갑에 지급되었습니다."))==1,"Withdrawal emits one shared bank income log with no NPC duplicate");
});
KeyStep(true);KeyStep(true);
CheckStep(()=>{
    int count=chatContent.childCount;long balance=BankManager.Instance.Money;
    check(!BankManager.Instance.TrySpend(balance+1,MoneyChangeReason.BookPublicationFee)&&chatContent.childCount==count,"Failed bank expense emits no success log");
    chat.enabled=false;chat.enabled=true;chat.enabled=false;chat.enabled=true;
    check(BankManager.Instance.AddMoney(1000,MoneyChangeReason.Reward)&&chatContent.childCount==count+1&&popupQueue.Any(x=>x.Contains("보상으로 1,000원이 지갑에 지급되었습니다.")),"Re-enabling existing chat does not duplicate ordinary bank income logs");
    count=chatContent.childCount;
    LibraryCatalog.Works[0].royalties=4000;
    int nextSeason=(LibraryCatalog.Today/GameTime.DaysPerSeason+1)*GameTime.DaysPerSeason;
    LibraryCatalog.AdvanceTo(nextSeason);
    check(!DialogueManager.IsDialogueOpen&&BankManager.Instance.Money==balance+5000&&chatContent.childCount==count+1&&popupQueue.Any(x=>x.Contains("도서 인세 정산으로 4,000원이 지갑에 지급되었습니다.")),"Season royalty reaches wallet, popup and history without NPC dialogue");
    LibraryCatalog.AdvanceTo(nextSeason);
    check(chatContent.childCount==count+1,"Repeated season settlement cannot duplicate system log");
});
CheckStep(()=>{
    var offers=Enumerable.Range(0,37).Select(i=>new TradeOffer{give=new TradeItem{cash=0,count=1},get=new TradeItem{item=LibraryCatalog.BookItem,count=1}}).ToArray();
    check(p.OpenTrade(offers,()=>true),"Independent merchant opens same shared trade window");
});
CheckStep(()=>check(p.TradeUI.OfferPageCount==3&&p.TradeUI.OfferPage==0,"37 generic offers automatically create three pages"));
ClickStep("NextOffers",()=>check(p.TradeUI.OfferPage==1&&p.TradeUI.GetComponentsInChildren<TradePointer>().First(x=>x.name=="Output_0").index==18,"Next arrow maps first row to offer 18"));
ClickStep("NextOffers",()=>check(p.TradeUI.OfferPage==2&&p.TradeUI.GetComponentsInChildren<TradePointer>().First(x=>x.name=="Output_0").index==36,"Second page advance reaches offer 36"));
DragStep("Output_0","Slot_6",()=>check(inventory.GetSlot(6).Item==LibraryCatalog.BookItem&&inventory.GetSlot(6).Count==1,"Free drag succeeds in ordinary merchant on final page"));
ClickStep("PreviousOffers",()=>check(p.TradeUI.OfferPage==1,"Previous arrow returns one offer page"));
KeyStep(true,()=>check(!p.IsTradeOpen&&movement.enabled,"Closing all flows restores movement"));
CheckStep(()=>{
    var cashier=UnityEngine.Object.FindObjectsByType<NpcTrader>().Single(x=>x.name=="Library_Cafe_Cashier");
    check(!cashier.GetComponent<LibraryDesk>() && cashier.openAfterDialogue && cashier.offers.Length==3,"Cafe cashier uses existing NpcTrader with greeting, no library-specific trade component");
    move(cashier.transform.position+Vector3.back*1.7f);
});
KeyStep(false,()=>check(p.IsTradeOpen && manager.CurrentLine=="주문 도와드리겠습니다" && p.TradeUI.Session.GetType()==typeof(TradeSession),"Actual Space greeting opens common cafe trade window"));
KeyStep(true);
CheckStep(()=>{
    var speaker=UnityEngine.Object.FindObjectsByType<DialogueData>().Single(x=>x.name=="Library_Cafe_Conversation");
    check(!speaker.GetComponent<LibraryDesk>() && !speaker.GetComponent<NpcTrader>(),"Conversation-only cafe worker uses existing dialogue component");
    move(speaker.transform.position+Vector3.back*1.7f);
});
KeyStep(false,()=>check(!p.IsTradeOpen && manager.CurrentLine=="커피 향이랑 책 냄새, 은근 잘 어울리지 않나요?","Actual Space conversation remains speech-only"));
KeyStep(true);
CheckStep(()=>check(playerInventory.OpenInventory(),"Open normal inventory after shared cafe interaction"));
HoverStep("StorageSlot_"+(authoredSlot+1).ToString("00"),()=>{
    var tooltip=playerInventory.UserInterface.transform.Find("ItemTooltip");
    check(tooltip.gameObject.activeInHierarchy && tooltip.GetComponentInChildren<TMPro.TMP_Text>().text=="공통 거래 검증 원고\n저자 : "+GameSession.LocalPlayerName,"Normal inventory hover displays the same title and author");capture("BookInventoryTooltip");
});
KeyStep(true);
CheckStep(()=>{
    int count=chatContent.childCount;
    var ordinary=new TradeSession(inventory,new[]{new TradeOffer{give=new TradeItem{cash=1000,count=1},get=new TradeItem{item=LibraryCatalog.BookItem,count=1}}});
    check(ordinary.TryTakeOffer(0,out _) && chatContent.childCount==count && ordinary.TryCancel(out _) && chatContent.childCount==count,"Cancelled ordinary trade has no system transaction log");
    check(ordinary.TryTakeOffer(0,out _),"Ordinary cash trade prepares output");
    int empty=Enumerable.Range(0,inventory.Capacity).First(i=>inventory.GetSlot(i).IsEmpty);
    check(ordinary.TryPlace(empty,out _) && chatContent.childCount==count+1 && popupQueue.Any(x=>x.Contains("구매: 1,000원이 소지 현금에서 결제되었습니다.")),"Common cash trade displays purpose and amount once after placement");
    count=chatContent.childCount;
    check(CashService.TryPayNpc(inventory,100,out _) && chatContent.childCount==count+1 && popupQueue.Any(x=>x.Contains("NPC 거래: 100원이 소지 현금에서 결제되었습니다.")),"CashService NPC payment reaches shared system history and popup");
    var other=new InventoryState();other.TryAdd(CashService.GetCurrency(1000),1,out _);count=chatContent.childCount;
    check(CashService.TryPayNpc(other,1000,out _) && chatContent.childCount==count,"Another inventory does not emit the local player's money log");
    count=chatContent.childCount;
    check(CashService.TryWithdraw(inventory,1000,1,out _) && chatContent.childCount==count+1 && popupQueue.Last().Contains("현금 출금: 1,000원"),"Wallet withdrawal uses one shared bank log with reason and amount");
    int cashSlot=Enumerable.Range(0,inventory.Capacity).First(i=>inventory.GetSlot(i).Item==CashService.GetCurrency(1000));count=chatContent.childCount;
    check(CashService.TryDeposit(inventory,cashSlot,1,out _) && chatContent.childCount==count+1 && popupQueue.Last().Contains("현금 입금: 1,000원"),"Wallet deposit uses one shared bank log with reason and amount");
});
CheckStep(()=>{
    var victim=new GameObject("검증 대상");var stats=victim.AddComponent<PlayerStats>();
    var zoneObject=new GameObject("Temporary crime log QA zone");var zone=zoneObject.AddComponent<CompanyGame.World.Maps.PropertyZone>();
    var savedProperties=PropertyRegistry.Instance.CaptureState();
    bool Red(TMPro.TMP_Text text){
        var panel=(GameObject)typeof(ChatUIManager).GetField("chatPanel",flags).GetValue(chat);bool visible=panel.activeSelf;
        try{panel.SetActive(true);Canvas.ForceUpdateCanvases();text.ForceMeshUpdate(true,true);var glyphs=text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(x=>x.isVisible).ToArray();return glyphs.Length>0 && glyphs.All(x=>x.color.r==255 && x.color.g==85 && x.color.b==85);}
        finally{panel.SetActive(visible);}
    }
    TMPro.TMP_Text Latest()=>chatContent.GetChild(chatContent.childCount-1).GetComponent<TMPro.TMP_Text>();
    try{
        zone.propertyId="qa_crime_"+Guid.NewGuid().ToString("N");zone.displayName="검증 구역";zone.transform.position=p.transform.position-Vector3.up*.5f;
        PropertyRegistry.Instance.TryRegister(zone.propertyId,"다른 소유자");
        var controller=p.GetComponent<PropertyUseController>();var detect=typeof(PropertyUseController).GetMethod("DetectTrespass",flags);
        int count=chatContent.childCount;detect.Invoke(controller,new object[]{PropertyRegistry.Instance});
        check(chatContent.childCount==count+1 && Latest().text.Contains("[무단침입]") && Red(Latest()) && popupQueue.Last().StartsWith("<color=#FF5555>"),"Actual zone-entry crime is red in both history and popup");
        detect.Invoke(controller,new object[]{PropertyRegistry.Instance});check(chatContent.childCount==count+1,"Remaining inside a zone does not duplicate trespass log");
        check(stats.TakeDamage(10,p.gameObject,Vector3.zero) && Latest().text.Contains("[폭행]") && Red(Latest()),"Actual nonlethal player damage emits red assault log");
        count=chatContent.childCount;
        check(stats.TakeDamage(90,p.gameObject,Vector3.zero) && chatContent.childCount==count+1 && Latest().text.Contains("[살인]") && Red(Latest()),"Lethal player damage emits one red murder log");
        count=chatContent.childCount;
        check(!stats.TakeDamage(1,p.gameObject,Vector3.zero) && chatContent.childCount==count,"Repeated hit on defeated target emits no duplicate murder log");
        ReportManager.Instance.NotifyCrime(CrimeType.Theft,"검증 플레이어","검증 물품");
        check(Latest().text.Contains("[절도]") && Red(Latest()),"Other crime types share the red system-message rendering");
        chat.ShowSystemMessage("일반 시스템 로그 색상 검증");
        check(!Latest().text.Contains("<color=#FF5555>") && !Red(Latest()),"Normal system message retains its original colour after crimes");
        var popup=(TMPro.TMP_Text)typeof(ChatUIManager).GetField("popupText",flags).GetValue(chat);popup.ForceMeshUpdate(true,true);
        var glyphs=popup.textInfo.characterInfo.Take(popup.textInfo.characterCount).Where(x=>x.isVisible).ToArray();
        check(glyphs.Any(x=>x.color.r==255 && x.color.g==85 && x.color.b==85) && glyphs.Any(x=>x.color.g!=85),"Mixed popup has red crime lines and separate normal-colour lines");
        capture("SharedSystemLogs");
    }finally{zoneObject.SetActive(false);UnityEngine.Object.Destroy(zoneObject);UnityEngine.Object.Destroy(victim);PropertyRegistry.Instance.RestoreState(savedProperties);}
});
Action callback=null;
Action<string> finish=message=>{
    UnityEngine.InputSystem.InputSystem.onBeforeUpdate-=callback;
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState());
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current,new UnityEngine.InputSystem.LowLevel.MouseState());
    if(InventoryItemSelector.IsOpen)InventoryItemSelector.Active.Cancel();manager.Close();p.CloseTrade();p.SendMessage("LateUpdate");
    playerInventory.CloseInventory();
    ledgerField.SetValue(null,savedLedger);replace.Invoke(inventory,new[]{savedInventory});notify.Invoke(inventory,null);BankManager.Instance.SetMoney(savedBank);clock.Paused=savedPause;
    foreach(var file in files){if(file.Value==null){if(System.IO.File.Exists(file.Key))System.IO.File.Delete(file.Key);}else System.IO.File.WriteAllBytes(file.Key,file.Value);}
    motor.enabled=false;p.transform.SetPositionAndRotation(savedPosition,savedRotation);motor.enabled=true;yaw.SetValue(camera,savedYaw);pitch.SetValue(camera,savedPitch);camera.firstPerson=savedView;
    if(chatContent)foreach(var child in chatContent.Cast<Transform>().ToArray())if(!oldChatChildren.Contains(child.gameObject))UnityEngine.Object.Destroy(child.gameObject);
    if(popupQueue!=null){popupQueue.Clear();foreach(var text in oldPopup)popupQueue.Enqueue(text);var t=(TMPro.TMP_Text)typeof(ChatUIManager).GetField("popupText",flags).GetValue(chat);if(t)t.text=string.Join("\n",oldPopup);}
    UnityEngine.InputSystem.InputSystem.RemoveDevice(qaKeyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(qaMouse);
    if(originalKeyboard!=null)originalKeyboard.MakeCurrent();if(originalMouse!=null)originalMouse.MakeCurrent();
    UnityEngine.InputSystem.InputSystem.settings=settings;UnityEngine.Object.Destroy(test);Application.runInBackground=background;
    System.IO.File.WriteAllText("../ArtSource/WorldDistricts/CivicLibraryV4/QA/LibraryInput.txt",string.Join("\n",report)+"\n"+message);
};
callback=()=>{
    if(UnityEngine.InputSystem.LowLevel.InputState.currentUpdateType!=UnityEngine.InputSystem.LowLevel.InputUpdateType.Dynamic)return;
    try{
        if(Time.unscaledTime-began>100)throw new Exception("Timeout stage "+stage);
        frame++;space=escape=click=false;pointer=new Vector2(20,20);steps[stage](frame);
        var keys=space?new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Space):escape?new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Escape):new UnityEngine.InputSystem.LowLevel.KeyboardState();
        qaKeyboard.MakeCurrent();qaMouse.MakeCurrent();
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(qaKeyboard,keys);
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(qaMouse,new UnityEngine.InputSystem.LowLevel.MouseState{position=pointer}.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left,click));
        if(frame>=13){stage++;frame=0;if(stage==steps.Count)finish("PASS all revised real-input flows; original inventory, wallet, catalogue, files and chat history restored");}
    }catch(Exception e){finish("FAIL stage "+stage+": "+e);}
};
UnityEngine.InputSystem.InputSystem.onBeforeUpdate+=callback;
return "Queued publication/drag/zero-price/rental/withdrawal/pagination actual-input checks";
