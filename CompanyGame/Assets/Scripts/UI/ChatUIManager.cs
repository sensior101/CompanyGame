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
        if (chatPanel && chatInput)
        {
            var font = chatInput.textComponent.font;
            var title = UITheme.Text(chatPanel.transform, "채팅", 28, UITheme.Ink, font);
            title.rectTransform.anchoredPosition = new Vector2(-135, 148);
            title.fontStyle = FontStyles.Bold;
            title.rectTransform.sizeDelta = new Vector2(160, 50);
            var close = UITheme.Button(chatPanel.transform, "×", new Vector2(40, 40), false, font, CloseChat);
            ((RectTransform)close.transform).anchoredPosition = new Vector2(205, 148);
            var minimize = UITheme.Button(chatPanel.transform, "－", new Vector2(40, 40), false, font, CloseChat);
            ((RectTransform)minimize.transform).anchoredPosition = new Vector2(155, 148);
            var send = UITheme.Button(chatPanel.transform, "전송", new Vector2(70, 50), true, font, SendChatMessage);
            ((RectTransform)send.transform).anchoredPosition = new Vector2(190, -135);
            var input = (RectTransform)chatInput.transform;
            input.offsetMin = new Vector2(20, 20); input.offsetMax = new Vector2(-100, 70);
            chatInput.textComponent.fontSize = 19;
            if (chatInput.placeholder is TMP_Text hint) { hint.fontSize = 18; hint.color = UITheme.Brown; }
            var history = (RectTransform)chatScrollRect.transform;
            history.offsetMin = new Vector2(18, 84); history.offsetMax = new Vector2(-18, -65);
            chatScrollRect.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
            chatMessagePrefab.gameObject.SetActive(false);
            chatPanel.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
            var surface = UITheme.Panel("ChatSurface", chatPanel.transform, Vector2.zero, UITheme.Hex(0xF7EAD4, .98f), 16);
            UIBuild.Stretch(surface); surface.SetAsFirstSibling();
            chatInput.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
            var inputSurface = UITheme.Panel("InputSurface", input, Vector2.zero, UITheme.Hex(0xFAF1E3), 12, false);
            UIBuild.Stretch(inputSurface); inputSurface.SetAsFirstSibling();
            if (chatInput.placeholder is TMP_Text placeholder) { placeholder.fontStyle = FontStyles.Normal; placeholder.text = "메시지를 입력하세요..."; }
        }
    }

    private void OnEnable()
    {
        BindBank();
        BindReports();
        if (!cashSubscribed)
        {
            CashService.TransactionCompleted += OnCashTransaction;
            WorldStateSync.Chatted += OnPlayerChat;
            cashSubscribed = true;
        }
    }

    // A player's line from the server (mine too: it comes back from the server).
    private void OnPlayerChat(string senderName, string message)
    {
        AddChatMessage(senderName, message);
        ShowPopup(senderName, message);
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
        newMessage.text = "";
        newMessage.gameObject.SetActive(true);
        newMessage.fontSize = 19;
        newMessage.color = UITheme.Ink;
        float width = Mathf.Max(180, ((RectTransform)chatContent).rect.width - 170);
        float height = Mathf.Max(48, chatMessagePrefab.GetPreferredValues(Literal(message), width, 0).y + 24);
        var layout = newMessage.GetComponent<UnityEngine.UI.LayoutElement>();
        if (!layout) layout = newMessage.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.preferredHeight = height;
        var row = newMessage.rectTransform;
        var badge = UITheme.Panel("Sender", row, Vector2.zero, senderName == "시스템" ? UITheme.Slate : UITheme.Pressed, 9, false);
        badge.anchorMin = badge.anchorMax = new Vector2(0, 1);
        badge.pivot = new Vector2(0, 1);
        badge.anchoredPosition = new Vector2(0, -3);
        badge.sizeDelta = new Vector2(96, 40);
        var sender = UITheme.Text(badge, Literal(senderName), 18, senderName == "시스템" ? UITheme.Ink : Color.white, newMessage.font);
        UIBuild.Stretch(sender.rectTransform); sender.enableAutoSizing = true; sender.fontSizeMin = 10; sender.fontSizeMax = 18;
        sender.fontStyle = FontStyles.Bold;
        var bubble = UITheme.Panel("Bubble", row, Vector2.zero, UITheme.Hex(0xEFDFC6), 10, false);
        UIBuild.Stretch(bubble); bubble.offsetMin = new Vector2(104, 3); bubble.offsetMax = new Vector2(-48, -3);
        var body = UITheme.Text(bubble, Literal(message), 18, crime.HasValue ? UITheme.Danger : UITheme.Ink, newMessage.font);
        UIBuild.Stretch(body.rectTransform); body.margin = new Vector4(12, 8, 12, 8); body.alignment = TextAlignmentOptions.TopLeft;
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Masking;
        // Measure the actual body font and usable bubble width, not the hidden template.
        float contentWidth = ((RectTransform)chatPanel.transform).rect.width - 36 - 17 - 10;
        float bodyWidth = Mathf.Max(80, contentWidth - 104 - 48);
        layout.preferredHeight = Mathf.Max(48, body.GetPreferredValues(body.text, bodyWidth, Mathf.Infinity).y + 12);
        layout.minHeight = layout.preferredHeight;
        layout.flexibleHeight = 0;
        var time = UITheme.Text(row, System.DateTime.Now.ToString("HH:mm"), 12, UITheme.Brown, newMessage.font);
        UIBuild.Stretch(time.rectTransform); time.rectTransform.anchorMin = new Vector2(1, 0); time.rectTransform.offsetMin = new Vector2(-44, 0); time.rectTransform.offsetMax = Vector2.zero;

        // 레이아웃 갱신 후 가장 아래로 스크롤
        StartCoroutine(ScrollToBottom());
    }

    private IEnumerator ScrollToBottom()
    {
        yield return null;

        if (chatScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            ResizeMessageRows();

            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }
    private void ResizeMessageRows()
    {
        if (!chatContent) return;
        var content = (RectTransform)chatContent;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        foreach (Transform child in content)
        {
            if (!child.gameObject.activeSelf) continue;
            var bubble = child.Find("Bubble") as RectTransform;
            var layout = child.GetComponent<UnityEngine.UI.LayoutElement>();
            if (!bubble || !layout) continue;
            var body = bubble.GetComponentInChildren<TMP_Text>();
            if (!body) continue;
            // Measure only after the layout system assigns the actual available width.
            float available = Mathf.Max(40, bubble.rect.width - body.margin.x - body.margin.z);
            Vector4 margins = body.margin;
            body.margin = Vector4.zero;
            float textHeight = body.GetPreferredValues(body.text, available, Mathf.Infinity).y;
            body.margin = margins;
            layout.minHeight = layout.preferredHeight = Mathf.Max(46, Mathf.Ceil(textHeight) + margins.y + margins.w + 6);
            layout.flexibleHeight = 0;
        }
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
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
            Canvas.ForceUpdateCanvases();
            ResizeMessageRows();
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
        // "/kick name": an admin command for the server (12.15); the server answers in this chat.
        if (message.StartsWith("/"))
        {
            AddChatMessage(playerName, message);
            WorldStateSync.SendAdmin(message.Substring(1));
            return;
        }
        // To everyone on the server; the line shows when the server sends it back (OnPlayerChat).
        if (WorldStateSync.SendChat(message))
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
        if (cashSubscribed)
        {
            CashService.TransactionCompleted -= OnCashTransaction;
            WorldStateSync.Chatted -= OnPlayerChat;
        }
        cashSubscribed = false;
        IsChatting = false;
    }
}
