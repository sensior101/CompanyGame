using TMPro;
using UnityEngine;

/// <summary>Uses the persistent player's existing Space input and prompt style.</summary>
[DefaultExecutionOrder(-320)]
[DisallowMultipleComponent]
public sealed class FurnitureLightInteraction : MonoBehaviour
{
    public static bool HasNearbyLight { get; private set; }
    PlayerMovement movement;
    GameObject canvasRoot;
    TMP_Text label;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => HasNearbyLight = false;

    void Awake() => movement = GetComponent<PlayerMovement>();

    void Update()
    {
        HasNearbyLight = false;
        if (!movement || !movement.isActiveAndEnabled || SceneLoadManager.IsLoading ||
            SeatInteraction.HasNearbySeat || (PlayerSeating.Local && PlayerSeating.Local.IsSeated) ||
            DialogueManager.HasNearbyNpc || DialogueManager.IsDialogueOpen || DialogueManager.OwnsInput ||
            PlayerInventory.IsAnyOpen || ChatUIManager.IsChatting || UIEventSystem.IsEditingText() ||
            (PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) ||
            (PlayerInteraction.Local && PlayerInteraction.Local.IsInteractionMenuOpen)) { Hide(); return; }
        var lamp = FurnitureLight.FindNearest(transform);
        HasNearbyLight = lamp;
        if (!lamp) { Hide(); return; }
        if (GameInput.InteractPressed && !PlayerInventory.SpaceConsumedThisFrame)
        {
            PlayerInventory.ConsumeSpaceThisFrame();
            lamp.Toggle();
        }
        Show(lamp.IsOn ? "조명 끄기  (스페이스바)" : "조명 켜기  (스페이스바)");
    }

    void Show(string text)
    {
        if (!canvasRoot)
        {
            canvasRoot = new GameObject("FurnitureLightPromptCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 195;
            var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f); scaler.matchWidthOrHeight = .5f;
            var panel = UIBuild.Rect("Panel", canvasRoot.transform, new Vector2(340f, 48f));
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, 0f); panel.pivot = new Vector2(.5f, 0f); panel.anchoredPosition = new Vector2(0f, 148f);
            var image = panel.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.075f, .12f, .17f, .92f); image.raycastTarget = false;
            var rect = UIBuild.Rect("Label", panel, Vector2.zero); UIBuild.Stretch(rect);
            label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (PlayerInteraction.Local && PlayerInteraction.Local.uiFont) label.font = PlayerInteraction.Local.uiFont;
            label.fontSize = 20f; label.color = new Color(.94f, .96f, .97f); label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        }
        label.text = text; canvasRoot.SetActive(true);
    }
    void Hide() { if (canvasRoot) canvasRoot.SetActive(false); }
    void OnDisable() { HasNearbyLight = false; Hide(); }
}
