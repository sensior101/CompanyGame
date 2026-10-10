using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class ChatUIManager : MonoBehaviour
{
    [Header("Chat UI")]
    [SerializeField] private GameObject chatPanel;
    [SerializeField] private TMP_InputField chatInput;

    [Header("Chat History")]
    [SerializeField] private Transform chatContent;
    [SerializeField] private TMP_Text chatMessagePrefab;
    [SerializeField] private UnityEngine.UI.ScrollRect chatScrollRect;

    [Header("Popup UI")]
    [SerializeField] private GameObject chatPopupPanel;
    [SerializeField] private TMP_Text popupText;

    [Header("Player Control")]
    [SerializeField] private PlayerMovement playerMovement;

    private bool wasMovementEnabled;
    private BankManager subscribedBank;
    private ReportManager subscribedReports;
    private bool cashSubscribed;

    [Header("Player")]
    [SerializeField] private string playerName = "Player";

    [Header("Settings")]
    [SerializeField] private float popupDuration = 3f;
    [SerializeField] private int maxPopupMessages = 6;

    // 다른 게임 스크립트에서 채팅 상태 확인
    public static bool IsChatting { get; private set; }

    private bool isChatOpen = false;

    private Coroutine popupCoroutine;

    private readonly Queue<string> popupMessages = new Queue<string>();

    private void Awake()
    {
        IsChatting = false;
    }

    private void OnEnable()
    {
        BindBank();
        BindReports();
        if (!cashSubscribed)
        {
            CashService.TransactionCompleted += OnCashTransaction;
            cashSubscribed = true;
        }
    }

    private void BindBank()
    {
        var bank = BankManager.Instance;
        if (subscribedBank == bank) return;
        UnbindBank();
        subscribedBank = bank;
        if (subscribedBank != null) subscribedBank.MoneyChanged += OnMoneyChanged;
    }

    private void UnbindBank()
    {
        if (subscribedBank != null) subscribedBank.MoneyChanged -= OnMoneyChanged;
        subscribedBank = null;
    }

    private void OnMoneyChanged(long balance, long delta, MoneyChangeReason reason)
    {
        if (delta == 0) return;
        string description;
        switch (reason)
        {
            case MoneyChangeReason.Earned: description = "수입으로"; break;
            case MoneyChangeReason.Spent: description = "비용 결제로"; break;
            case MoneyChangeReason.Reward: description = "보상으로"; break;
            case MoneyChangeReason.Sale: description = "판매 대금으로"; break;
            case MoneyChangeReason.Purchase: description = "구매 비용으로"; break;
            case MoneyChangeReason.Tax: description = "세금 납부로"; break;
            case MoneyChangeReason.PropertyUpgrade: description = "부동산 개선 비용으로"; break;
            case MoneyChangeReason.BookPublicationFee: description = "책 출판 비용으로"; break;
            case MoneyChangeReason.BookRoyalty: description = "도서 인세 정산으로"; break;
            case MoneyChangeReason.LibraryLateFee: description = "도서 대여 연체료 납부로"; break;
            case MoneyChangeReason.Deposit:
                ShowSystemMessage("현금 입금: " + delta.ToString("N0") + "원을 지갑에 도로 넣었다.");
                return;
            case MoneyChangeReason.Withdrawal:
                ShowSystemMessage("현금 출금: " + (-delta).ToString("N0") + "원을 지갑에서 꺼냈습니다.");
                return;
            // Initialization and manual adjustments are not completed transactions.
            case MoneyChangeReason.InitialBalance:
            case MoneyChangeReason.ManualAdjustment: return;
            default: description = delta > 0 ? "수입으로" : "지출로"; break;
        }
        string amount = System.Math.Abs(delta).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        ShowSystemMessage(description + " " + amount +
            (delta > 0 ? "원이 지갑에 지급되었습니다." : "원이 결제되었습니다."));
    }

    private void OnCashTransaction(InventoryState inventory, long delta, MoneyChangeReason reason, string purpose)
    {
        // A merchant or another player's inventory must not produce my money log.
        if (!InventoryManager.Instance || !ReferenceEquals(inventory, InventoryManager.Instance.State) || delta == 0) return;
        string amount = System.Math.Abs(delta).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(purpose)) purpose = delta > 0 ? "현금 수입" : "현금 지출";
        ShowSystemMessage(purpose + ": " + amount +
            (delta > 0 ? "원이 소지 현금으로 지급되었습니다." : "원이 소지 현금에서 결제되었습니다."));
    }

    private void BindReports()
    {
        var reports = ReportManager.Instance;
        if (subscribedReports == reports) return;
        UnbindReports();
        subscribedReports = reports;
        if (subscribedReports == null) return;
        subscribedReports.CrimeOccurred += OnCrimeOccurred;
        subscribedReports.ReportResolved += OnReportResolved;
    }

    private void UnbindReports()
    {
        if (subscribedReports != null)
        {
            subscribedReports.CrimeOccurred -= OnCrimeOccurred;
            subscribedReports.ReportResolved -= OnReportResolved;
        }
        subscribedReports = null;
    }

    private void OnCrimeOccurred(CrimeType type, string actorName, string targetName)
    {
        string actor = Literal(actorName), target = Literal(targetName);
        string message;
        switch (type)
        {
            case CrimeType.Trespass: message = actor + "님이 " + target + "에 무단으로 침입하였습니다."; break;
            case CrimeType.Assault: message = actor + "님이 " + target + "을(를) 폭행하였습니다."; break;
            case CrimeType.Murder: message = actor + "님이 " + target + "을(를) 살해하였습니다."; break;
            default: message = actor + "님의 " + CrimeName(type) + " 행위가 발생했습니다. 대상: " + target; break;
        }
        ShowSystemMessage("[" + CrimeName(type) + "] " + message, type);
    }

    private void OnReportResolved(CrimeReport report)
    {
        if (report == null || report.verdict == null) return;
        string local = GameSession.LocalPlayerId, reporter = subscribedReports.LocalPlayerId;
        if (report.reporterId != local && report.suspectId != local && report.reporterId != reporter && report.suspectId != reporter) return;
        bool convicted = report.status == ReportStatus.Convicted;
        string message = "[" + CrimeName(report.type) + "] " + Literal(report.suspectId) + "님에 대한 신고가 " +
            (convicted ? "유죄로 판정되었습니다. 벌점 " + report.verdict.penaltyPoints + "점" : "기각되었습니다.");
        if (convicted && report.verdict.fine > 0) message += ", 벌금 " + report.verdict.fine.ToString("N0") + "원 부과";
        ShowSystemMessage(message, convicted ? report.type : (CrimeType?)null);
    }

    private static string CrimeName(CrimeType type)
    {
        switch (type)
        {
            case CrimeType.Trespass: return "무단침입";
            case CrimeType.Assault: return "폭행";
            case CrimeType.Murder: return "살인";
            case CrimeType.Theft: return "절도";
            default: return "범죄";
        }
    }

    // Dynamic names must not be able to close the crime colour tag.
    private static string Literal(string value) => (value ?? "").Replace("<", "＜").Replace(">", "＞");

    private static string FormatMessage(string senderName, string message, CrimeType? crime)
    {
        string text = "[" + senderName + "] " + message;
        return crime.HasValue ? "<color=#FF5555>" + text + "</color>" : text;
    }

    private void AddChatMessage(string senderName, string message, CrimeType? crime = null)
    {
        if (chatContent == null || chatMessagePrefab == null)
            return;

        // 채팅 메시지 생성
        TMP_Text newMessage = Instantiate(
            chatMessagePrefab,
            chatContent
        );

        // 닉네임과 메시지 표시
        newMessage.richText = true;
        newMessage.text = FormatMessage(senderName, message, crime);

        // 레이아웃 갱신 후 가장 아래로 스크롤
        StartCoroutine(ScrollToBottom());
    }

    private IEnumerator ScrollToBottom()
    {
        yield return null;

        if (chatScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();

            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }
    private void Start()
    {
        if (chatPanel != null)
        {
            chatPanel.SetActive(false);
        }

        if (chatPopupPanel != null)
        {
            chatPopupPanel.SetActive(false);
        }
    }

    private void Update()
    {
        if (subscribedBank != BankManager.Instance) BindBank();
        if (subscribedReports != ReportManager.Instance) BindReports();
        // Enter in another text field (phone, withdrawal) belongs to that field.
        if (!isChatOpen && UIEventSystem.IsEditingText())
            return;

        // Enter 키
        if (GameInput.SubmitPressed)
        {
            if (!isChatOpen && FloorStairsMenu.OwnsEnter) return;
            if (!isChatOpen)
            {
                OpenChat();
            }
            else
            {
                StartCoroutine(SendChatAfterInputUpdate());
            }

            return;
        }

        // ESC 키
        if (GameInput.CancelPressed && isChatOpen)
        {
            CloseChat();
        }
    }

    private void OpenChat()
    {
        if (isChatOpen)
            return;

        isChatOpen = true;

        IsChatting = true;

        if (playerMovement == null) playerMovement = PlayerSpawner.Player;
        if (playerMovement != null)
        {
            wasMovementEnabled = playerMovement.enabled;
            playerMovement.enabled = false;
        }
        // 팝업 숨기기
        if (chatPopupPanel != null)
        {
            chatPopupPanel.SetActive(false);
        }

        // 채팅창 표시
        if (chatPanel != null)
        {
            chatPanel.SetActive(true);
        }

        // 입력창 활성화
        if (chatInput != null)
        {
            chatInput.text = "";

            chatInput.Select();

            chatInput.ActivateInputField();
        }
    }
    private bool isSending = false;

    private IEnumerator SendChatAfterInputUpdate()
    {
        if (isSending)
            yield break;

        isSending = true;

        yield return null;

        SendChatMessage();

        isSending = false;
    }

    private void SendChatMessage()
    {
        if (chatInput == null)
            return;

        string message = chatInput.text.Trim();

        // 채팅창 닫기
        CloseChat();

        if (string.IsNullOrEmpty(message))
            return;

        // Console 출력
        Debug.Log($"[{playerName}] {message}");

        // 채팅 기록에 추가
        AddChatMessage(playerName, message);

        // 팝업 표시
        ShowPopup(playerName, message);
    }

    private void CloseChat()
    {
        if (!isChatOpen)
            return;

        isChatOpen = false;

        // 입력창 비활성화
        if (chatInput != null)
        {
            chatInput.DeactivateInputField();
        }

        // UI 선택 해제
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        // 채팅창 숨기기
        if (chatPanel != null)
        {
            chatPanel.SetActive(false);
        }

        // 게임 조작 다시 활성화
        IsChatting = false;

        if (playerMovement != null && wasMovementEnabled)
        {
            playerMovement.enabled = true;
        }
    }

    public void ShowSystemMessage(string message) => ShowSystemMessage(message, null);

    public void ShowSystemMessage(string message, CrimeType? crime)
    {
        Debug.Log("[시스템] " + message);
        AddChatMessage("시스템", message, crime);
        ShowPopup("시스템", message, crime);
    }

    public void ShowPopup(string senderName, string message) => ShowPopup(senderName, message, null);

    public void ShowPopup(string senderName, string message, CrimeType? crime)
    {
        string newMessage = FormatMessage(senderName, message, crime);
        popupMessages.Enqueue(newMessage);

        while (popupMessages.Count > maxPopupMessages)
        {
            popupMessages.Dequeue();
        }

        if (popupText != null)
        {
            popupText.richText = true;
            popupText.text = string.Join("\n", popupMessages);

            Canvas.ForceUpdateCanvases();

            float textHeight = popupText.preferredHeight;

            RectTransform textRect = popupText.rectTransform;
            textRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                textHeight
            );

            if (chatPopupPanel != null)
            {
                RectTransform panelRect =
                    chatPopupPanel.GetComponent<RectTransform>();

                panelRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    textHeight + 20f
                );
            }
        }

        if (isChatOpen)
            return;

        if (chatPopupPanel != null)
        {
            chatPopupPanel.SetActive(true);
        }

        if (popupCoroutine != null)
        {
            StopCoroutine(popupCoroutine);
        }

        popupCoroutine = StartCoroutine(HidePopupAfterDelay());
    }


    private IEnumerator HidePopupAfterDelay()
    {
        yield return new WaitForSecondsRealtime(popupDuration);

        if (chatPopupPanel != null)
        {
            chatPopupPanel.SetActive(false);
        }

        popupMessages.Clear();

        if (popupText != null)
        {
            popupText.text = "";
        }

        popupCoroutine = null;
    }

    private void OnDisable()
    {
        // ControlLock disables chat input during transactions. Keep money events
        // subscribed until destruction so system history/popups still work.
        if (playerMovement != null && wasMovementEnabled)
        {
            playerMovement.enabled = true;
        }

        isChatOpen = false;
        IsChatting = false;
    }

    private void OnDestroy()
    {
        UnbindBank();
        UnbindReports();
        if (cashSubscribed) CashService.TransactionCompleted -= OnCashTransaction;
        cashSubscribed = false;
        IsChatting = false;
    }
}