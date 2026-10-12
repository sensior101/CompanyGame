using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// The room code, big, with a copy button. As a small card at the top of the screen it appears once when the host
/// arrives in the game map after 방 만들기 (<see cref="RequestOnArrival"/>): it does not lock the game and stays until dismissed.
/// <see cref="AddCode"/> is the same code + copy button for the ESC menu. Created automatically, like the pause menu.
/// </summary>
public sealed class RoomCodeCard : MonoBehaviour
{
    public static RoomCodeCard Instance { get; private set; }

    static bool pending;
    Canvas canvas;

    public bool Shown => canvas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; pending = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (Instance) return;
        var go = new GameObject("Room Code Card");
        DontDestroyOnLoad(go);
        go.AddComponent<RoomCodeCard>();
    }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Show the card as soon as the player stands in the game map (the map is still loading when the room is made).</summary>
    public static void RequestOnArrival() { pending = true; }

    void Update()
    {
        var net = NetSession.Instance;
        if (!net || !net.InRoom) { pending = false; Hide(); return; }
        if (pending && InGameMap()) { pending = false; Show(net.RoomId); }
    }

    static bool InGameMap()
    {
        var player = PlayerSpawner.Player;
        return player && player.isActiveAndEnabled && !SceneLoadManager.IsLoading
            && SceneManager.GetActiveScene().name != PlayerSpawner.TitleSceneName;
    }

    public void Show(string rid)
    {
        Hide();
        canvas = TitleUi.NewCanvas("RoomCodeCanvas", 3500);
        canvas.transform.SetParent(transform, false);
        var card = TitleUi.Column(canvas.transform, "Card", 560, 8, TitleUi.Panel, 24);
        card.anchoredPosition = new Vector2(0, -36);
        TitleUi.Label(card, "방 코드", 24, TitleUi.Muted);
        AddCode(card, rid, true);
        TitleUi.Label(card, "친구에게 이 코드를 알려 주세요.\nESC 메뉴에서 다시 볼 수 있어요.", 21, TitleUi.Muted);
        TitleUi.Button(card, "닫기", Hide, 52, false, 26);
    }

    public void Hide()
    {
        if (!canvas) return;
        Destroy(canvas.gameObject);
        canvas = null;
    }

    /// <summary>Draw through a camera instead of as an overlay (screenshots only).</summary>
    public void UseCamera(Camera camera)
    {
        if (!canvas) return;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
    }

    /// <summary>The big code and a copy button below it. releaseFocus: give the keyboard back to the game after a click.</summary>
    public static void AddCode(Transform parent, string rid, bool releaseFocus)
    {
        var code = TitleUi.Label(parent, rid, 76, TitleUi.Amber, TextAlignmentOptions.Center, FontStyles.Bold);
        code.characterSpacing = 12; // wide gaps keep 2/Z, 5/S and 8/B apart
        var copy = TitleUi.Button(parent, "코드 복사", null, 60, true, 28);
        copy.onClick.AddListener(() =>
        {
            RoomCode.Copy(rid);
            copy.GetComponentInChildren<TMP_Text>().text = "복사했어요!";
            if (releaseFocus && EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        });
    }
}
