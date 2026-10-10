using System;
using System.Collections;
using UnityEngine;

/// <summary>NPC dialogue supplies catalogue offers to the shared trading and item-selection flows.</summary>
[RequireComponent(typeof(DialogueData))]
public sealed class LibraryDesk : MonoBehaviour
{
    public enum Service { Library, CafeCashier, CafeConversation }
    // Retained for existing serialized scenes/API. New cafe NPCs use NpcTrader directly.
    [HideInInspector] public Service service;
    [HideInInspector] public TradeOffer[] cafeOffers = Array.Empty<TradeOffer>();
    DialogueData dialogue;
    DialogueManager manager;
    InventoryItemSelector selector;
    public static DialogueOption[] CreateOptions() => new[]
    {
        new DialogueOption("Buy", "책을 구매한다"),
        new DialogueOption("Borrow", "책을 대여한다"),
        new DialogueOption("Return", "책을 반납한다")
        { isAvailable = () => LibraryCatalog.BorrowedBy(GameSession.LocalPlayerId) != null },
        new DialogueOption("Publish", "책을 출판한다"),
        new DialogueOption("Withdraw", "책을 회수한다")
        { isAvailable = () => LibraryCatalog.HasPublishedBooks(GameSession.LocalPlayerId) }
    };
    void Start()
    {
        dialogue=GetComponent<DialogueData>();
        if(service!=Service.Library)
        {
            if(service==Service.CafeCashier)
            {
                var trader=GetComponent<NpcTrader>();
                if(!trader){trader=gameObject.AddComponent<NpcTrader>();trader.offers=cafeOffers;}
                trader.openAfterDialogue=true;
                trader.radius=dialogue.radius;trader.heightTolerance=2f;
            }
            return;
        }
        DialogueManager.EnsureInstance();manager=DialogueManager.Instance;
        manager.OptionSelected+=OnOption;
        // Rebind runtime conditions after scene loading; delegates are not serialized.
        dialogue.options=CreateOptions();
    }
    void OnOption(DialogueData npc,string id)
    {
        if(npc!=dialogue || service!=Service.Library)return;
        if(id=="PublishYes") { ConfirmPublication();return; }
        if(id=="PublishNo") { if(selector)selector.Cancel();return; }
        StartCoroutine(OpenAfterInput(id));
    }
    IEnumerator OpenAfterInput(string id)
    {
        yield return null;
        var interaction=PlayerInteraction.Local;
        if(!interaction || !dialogue.InRange(interaction.transform))yield break;
        var inventory=interaction.GetComponent<PlayerInventory>();
        if(!inventory)yield break;
        LibraryCatalog.ConnectLocal(inventory.Inventory);
        if(id=="Return")
        {
            selector=InventoryItemSelector.Open(inventory,stack=>LibraryCatalog.CanReturn(stack,GameSession.LocalPlayerId),
                SelectedReturn,CancelledReturn,()=>this && dialogue.InRange(interaction.transform));
            if(selector)manager.Present(dialogue,"어떤 책을 반납하시겠습니까?",waitForResponse:true);
        }
        else if(id=="Publish")
        {
            selector=InventoryItemSelector.Open(inventory,stack=>LibraryCatalog.CanPublish(stack,GameSession.LocalPlayerId),
                SelectedPublication,CancelledPublication,()=>this && dialogue.InRange(interaction.transform));
            if(selector)manager.Present(dialogue,"어떤 책을 출판할까요?",waitForResponse:true);
        }
        else if(Enum.TryParse<LibraryTradeMode>(id,out var mode))
        {
            if(mode==LibraryTradeMode.Borrow && !LibraryCatalog.CanOpenBorrowing(GameSession.LocalPlayerId,out var message))
            {
                manager.PresentNotice(dialogue,message);
                yield break;
            }
            var session=LibraryCatalog.CreateTrade(GameSession.LocalPlayerId,inventory.Inventory,mode,Withdrawn,Borrowed);
            interaction.OpenTrade(session,()=>this && dialogue.InRange(interaction.transform));
        }
    }
    void SelectedPublication(InventoryItemSelector selection)
    {
        manager.Present(dialogue,KoreanText.Object(selection.SelectedItem.DisplayName)+" 출판하시겠습니까? 출판 비용은 10,000원입니다.",new[]{
            new DialogueOption("PublishYes","네, 그렇게 해주세요"),
            new DialogueOption("PublishNo","아니요, 다시 생각해볼게요")});
    }
    void SelectedReturn(InventoryItemSelector selection)
    {
        string title=selection.SelectedItem.DisplayName;
        bool returned=LibraryCatalog.Return(GameSession.LocalPlayerId,selection.Handoff,out int days,out long fee,out var error);
        selection.Close();selector=null;
        string message=returned?KoreanText.Subject(title)+" 반납되었습니다. 감사합니다.":error;
        if(returned && days>0)message+="\n"+days+"일 연체되었습니다. 연체비는 총 "+fee.ToString("N0")+"원입니다.";
        manager.PresentNotice(dialogue,message);
    }
    void CancelledReturn()
    {
        selector=null;
        if(this && manager)manager.PresentNotice(dialogue,"책 반납이 취소되었습니다.");
    }
    void ConfirmPublication()
    {
        if(!selector || selector.SelectedItem==null)return;
        bool ok=LibraryCatalog.PublishBook(GameSession.LocalPlayerId,selector.Handoff,BankManager.Instance,out var error);
        selector.Close();selector=null;
        if(ok)
        {
            manager.PresentNotice(dialogue,"출판이 완료되었습니다. 많은 독자분들이 읽어주셨으면 좋겠네요!");
        }
        else manager.PresentNotice(dialogue,error);
    }
    void CancelledPublication()
    {
        selector=null;
        if(this && manager)manager.PresentNotice(dialogue,"아쉽네요. 다음에 기회가 된다면 꼭 출판해주세요.");
    }
    void Withdrawn(string title,long royalty)
    {
        manager.Present(dialogue,KoreanText.Subject(title)+" 회수되었습니다. 지금까지 누적된 "+royalty.ToString("N0")+"원을 받아주세요.");
    }
    void Borrowed(string title)
    {
        if(this && manager)manager.Present(dialogue,KoreanText.Object(title)+" 대여하셨습니다. 대여 가능 기간은 "+
            LibraryCatalog.FreeDays+"일이며, 이후부터 하루 "+LibraryCatalog.DailyFine.ToString("N0")+"원의 연체료가 부과됩니다.");
    }
    void OnDestroy()
    {
        if(selector)selector.Cancel();
        if(!manager)return;
        manager.OptionSelected-=OnOption;
    }
}
