using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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

    [Header("Player")]
    [SerializeField] private string playerName = "Player";

    [Header("Settings")]
    [SerializeField] private float popupDuration = 5f;

    // 다른 게임 스크립트에서 채팅 상태 확인
    public static bool IsChatting { get; private set; }

    private bool isChatOpen = false;

    private Coroutine popupCoroutine;

    private void Awake()
    {
        IsChatting = false;
    }

    private void AddChatMessage(string senderName, string message)
    {
        if (chatContent == null || chatMessagePrefab == null)
            return;

        // 채팅 메시지 생성
        TMP_Text newMessage = Instantiate(
            chatMessagePrefab,
            chatContent
        );

        // 닉네임과 메시지 표시
        newMessage.text = $"[{senderName}] {message}";

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
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        // Enter 키
        if (keyboard.enterKey.wasPressedThisFrame ||
            keyboard.numpadEnterKey.wasPressedThisFrame)
        {
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
        if (keyboard.escapeKey.wasPressedThisFrame && isChatOpen)
        {
            CloseChat();
        }
    }

    // =========================
    // 채팅창 열기
    // =========================

    private void OpenChat()
    {
        if (isChatOpen)
            return;

        isChatOpen = true;

        // 게임 조작 차단
        IsChatting = true;

        // 채팅 중 플레이어 이동 스크립트 비활성화
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

        // 한글 조합 중인 마지막 글자가 입력창에 반영될 시간을 줌
        yield return null;

        SendChatMessage();

        isSending = false;
    }

    // =========================
    // 메시지 전송
    // =========================

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

    // =========================
    // 채팅창 닫기
    // =========================

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

    // =========================
    // 채팅 메시지 팝업
    // =========================

    public void ShowPopup(string senderName, string message)
    {
        if (popupCoroutine != null)
        {
            StopCoroutine(popupCoroutine);

            popupCoroutine = null;
        }

        if (popupText != null)
        {
            popupText.text = $"[{senderName}] {message}";
        }

        // 채팅창이 열려 있으면 팝업 숨기기
        if (isChatOpen)
            return;

        if (chatPopupPanel != null)
        {
            chatPopupPanel.SetActive(true);
        }

        popupCoroutine = StartCoroutine(HidePopupAfterDelay());
    }

    // =========================
    // 팝업 자동 숨김
    // =========================

    private IEnumerator HidePopupAfterDelay()
    {
        yield return new WaitForSecondsRealtime(popupDuration);

        if (chatPopupPanel != null)
        {
            chatPopupPanel.SetActive(false);
        }

        popupCoroutine = null;
    }

    private void OnDisable()
    {
        if (playerMovement != null && wasMovementEnabled)
        {
            playerMovement.enabled = true;
        }

        isChatOpen = false;
        IsChatting = false;
    }

    private void OnDestroy()
    {
        IsChatting = false;
    }
}