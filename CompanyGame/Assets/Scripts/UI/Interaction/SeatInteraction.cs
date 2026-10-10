using TMPro;
using UnityEngine;

[DefaultExecutionOrder(-325)]
public sealed class SeatInteraction : MonoBehaviour
{
    public static bool HasNearbySeat { get; private set; }
    PlayerSeating seating;
    GameObject canvasRoot;
    TMP_Text label;
    bool armed;
    void Awake() { seating = GetComponent<PlayerSeating>(); if (!seating) seating = gameObject.AddComponent<PlayerSeating>(); }
    void Update()
    {
        HasNearbySeat = false;
        if (seating.IsSeated)
        {
            Show("일어나기  (스페이스바 / WASD)");
            if (!GameInput.InteractHeld) armed = true;
            if (armed && (GameInput.InteractPressed || GameInput.Move.sqrMagnitude > 0f))
            {
                PlayerInventory.ConsumeSpaceThisFrame();
                bool stood = GameInput.InteractPressed ? seating.TryStand() : seating.TryStand(GameInput.Move);
                if (stood) { Hide(); armed = false; }
            }
            return;
        }
        if (SceneLoadManager.IsLoading || BookReader.BlocksInventoryInput ||
            PlayerInventory.IsAnyOpen || ChatUIManager.IsChatting || UIEventSystem.IsEditingText() ||
            (PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) ||
            (PlayerInteraction.Local && PlayerInteraction.Local.IsInteractionMenuOpen)) { Hide(); return; }
        var seat = Seat.FindNearest(transform);
        HasNearbySeat = seat;
        if (!seat) { Hide(); return; }
        Show("앉기  (스페이스바)");
        if (GameInput.InteractPressed && !PlayerInventory.SpaceConsumedThisFrame)
        {
            PlayerInventory.ConsumeSpaceThisFrame(); armed = false;
            if (seating.TrySit(seat)) Show("일어나기  (스페이스바 / WASD)");
        }
    }
    void Show(string text)
    {
        if (!canvasRoot)
        {
            canvasRoot = new GameObject("SeatPromptCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 195;
            var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f,720f); scaler.matchWidthOrHeight = .5f;
            var panel = UIBuild.Rect("Panel", canvasRoot.transform, new Vector2(340f,48f));
            panel.anchorMin = panel.anchorMax = new Vector2(.5f,0f); panel.pivot = new Vector2(.5f,0f); panel.anchoredPosition = new Vector2(0f,148f);
            var image = panel.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.075f,.12f,.17f,.92f); image.raycastTarget = false;
            var rect = UIBuild.Rect("Label",panel,Vector2.zero); UIBuild.Stretch(rect);
            label = rect.gameObject.AddComponent<TextMeshProUGUI>(); label.font = PlayerInteraction.Local.uiFont;
            label.fontSize = 20f; label.color = new Color(.94f,.96f,.97f); label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        }
        label.text = text; canvasRoot.SetActive(true);
    }
    void Hide() { if (canvasRoot) canvasRoot.SetActive(false); }
    void OnDisable() { HasNearbySeat = false; Hide(); }
}
