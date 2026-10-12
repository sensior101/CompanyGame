using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-game ESC menu: continue, room info, hand the host role to another player, leave the room (back to the title)
/// and quit. The game keeps running underneath (it is a multiplayer game, so there is no real pause).
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
        var net = NetSession.Instance;
        bool inRoom = net && net.InRoom;
        TitleUi.Label(panel, "일시정지", 46, TitleUi.Cream, TextAlignmentOptions.Center, FontStyles.Bold);
        note = TitleUi.Label(panel, "", 24, TitleUi.Muted);
        if (inRoom) { TitleUi.Label(panel, "방 코드", 24, TitleUi.Muted); RoomCodeCard.AddCode(panel, net.RoomId, false); }
        var resume = TitleUi.Button(panel, "계속하기", Close, 64, true, 30);
        if (inRoom && net.IsSteam) TitleUi.Button(panel, "친구 초대 (Steam)", () => net.InviteFriends(), 64, true, 30);
        if (inRoom && net.IsHost && net.Peers.Count > 1) TitleUi.Button(panel, "방장 넘기기", ShowTransfer, 64, true, 30);
        TitleUi.Button(panel, inRoom ? "방 나가기 (타이틀로)" : "타이틀로", Leave, 64, true, 30);
        TitleUi.Button(panel, "게임 종료", Quit, 56, false, 26);
        RefreshNote();
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(resume.gameObject); // Enter continues, not copies
    }

    void RefreshNote()
    {
        if (!note) return;
        var net = NetSession.Instance;
        string text = "오프라인 (혼자 플레이)";
        if (net && net.InRoom)
            text = (net.IsHost ? "방장" : "참가자") + " · " + net.Peers.Count + "명" + (string.IsNullOrEmpty(net.StatusText) ? "" : "\n" + net.StatusText);
        if (notice != null) text += "\n<color=#FF9A8A>" + notice + "</color>";
        if (note.text != text) note.text = text;
    }

    string notice;

    void ShowTransfer()
    {
        Begin(640);
        var net = NetSession.Instance;
        TitleUi.Label(panel, "방장 넘기기", 46, TitleUi.Cream, TextAlignmentOptions.Center, FontStyles.Bold);
        TitleUi.Label(panel, "고른 사람이 방장이 되고, 월드 데이터가 그 사람의 PC로 넘어갑니다. 넘긴 뒤 이 PC의 월드 파일은 정리됩니다.", 24, TitleUi.Muted);
        foreach (var peer in new List<PeerInfo>(net.Peers))
        {
            if (peer.id == net.MyId) continue;
            string id = peer.id;
            TitleUi.Button(panel, peer.name, () => { net.TransferHost(id); Close(); }, 60, true, 28);
        }
        TitleUi.Button(panel, "뒤로", ShowMain, 56, false, 26);
        TitleUi.FocusFirst(panel);
    }

    void Leave()
    {
        string why = TitleFlow.WhyCannotLeave();
        if (why != null) { notice = TitleFlow.Message(why); RefreshNote(); return; }
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
