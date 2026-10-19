using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-game ESC menu: continue, server info (name, server code, connection kind and round trip, 12.13), leave the server
/// (back to the title; TitleFlow.ReturnToTitle also stops an in-app server, never a PC server process) and quit.
/// The game keeps running underneath (it is a multiplayer game, so there is no real pause).
/// Opens only when ESC is pressed while the player could move at the start of the frame, so ESC that closes the
/// inventory, a shop, the phone, chat or a dialogue never also opens this menu. Created automatically.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }

    readonly ControlLock controlLock = new ControlLock();
    Canvas canvas;
    RectTransform panel;
    TMP_Text note;
    bool open, wasFree;
    PlayerMovement lockedPlayer;

    public bool IsOpen => open;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (Instance) return;
        var go = new GameObject("Pause Menu");
        DontDestroyOnLoad(go);
        go.AddComponent<PauseMenu>();
    }

    void Awake() { Instance = this; }

    void OnDestroy() { if (open) Close(); if (Instance == this) Instance = null; }

    // Runs first every frame (execution order), before any other script handled ESC.
    void Update()
    {
        var player = PlayerSpawner.Player;
        wasFree = !open && player && player.isActiveAndEnabled && !SceneLoadManager.IsLoading
            && SceneManager.GetActiveScene().name != PlayerSpawner.TitleSceneName;
        if (open) RefreshNote();
    }

    // Decided after every other Update: ESC that other UI used this frame must not open (or double-close) this menu.
    void LateUpdate()
    {
        if (!GameInput.CancelPressed) return;
        if (open) Close();
        else if (wasFree) Open();
    }

    public void Open()
    {
        if (open) return;
        open = true;
        lockedPlayer = PlayerSpawner.Player;
        controlLock.Hold(lockedPlayer);
        canvas = TitleUi.NewCanvas("PauseCanvas", 4000);
        canvas.transform.SetParent(transform, false);
        ShowMain();
    }

    /// <summary>Draw through a camera instead of as an overlay (screenshots only).</summary>
    public void UseCamera(Camera camera)
    {
        if (!canvas) return;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        if (canvas) Destroy(canvas.gameObject);
        controlLock.Release(lockedPlayer, true);
    }

    // ---- screens ----

    void Begin(float width)
    {
        if (canvas) { for (int i = canvas.transform.childCount - 1; i >= 0; i--) Destroy(canvas.transform.GetChild(i).gameObject); }
        var dim = TitleUi.Full(canvas.transform, "Dim", new Color(0.05f, 0.05f, 0.12f, 0.6f));
        dim.GetComponent<Image>().raycastTarget = true;
        panel = TitleUi.Column(dim, "Panel", width, 14, TitleUi.Panel, 32);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
    }

    void ShowMain()
    {
        Begin(640);
        TitleUi.Label(panel, "일시정지", 46, TitleUi.Cream, TextAlignmentOptions.Center, FontStyles.Bold);
        note = TitleUi.Label(panel, "", 24, TitleUi.Muted);
        var resume = TitleUi.Button(panel, "계속하기", Close, 64, true, 30);
        bool onServer = SyncClient.Instance && SyncClient.Instance.Connected;
        TitleUi.Button(panel, onServer ? "서버 나가기 (타이틀로)" : "타이틀로", Leave, 64, true, 30);
        TitleUi.Button(panel, "게임 종료", Quit, 56, false, 26);
        RefreshNote();
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(resume.gameObject); // Enter continues, not copies
    }

    void RefreshNote()
    {
        if (!note) return;
        var sync = SyncClient.Instance;
        string text = "오프라인 (혼자 플레이)";
        if (sync && sync.Connected)
        {
            bool mine = SyncHost.Instance && SyncHost.Instance.InApp;
            string code = mine ? SyncHost.Instance.Code : TitleFlow.JoinedCode;
            string link = sync.Link == "relay" ? "중계(EOS)" : sync.Link == "p2p" ? "직접(EOS P2P)" : "직접(TCP)";
            text = "서버: " + sync.ServerName + (mine ? " (내가 연 서버)" : "")
                + (string.IsNullOrEmpty(code) ? "" : "\n서버 코드: <color=#FFC857>" + code + "</color>")
                + "\n연결: " + link + (sync.RoundTripMs >= 0 ? " · 왕복 " + sync.RoundTripMs + "ms" : "");
        }
        if (note.text != text) note.text = text;
    }

    void Leave()
    {
        Close();
        TitleFlow.ReturnToTitle();
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
