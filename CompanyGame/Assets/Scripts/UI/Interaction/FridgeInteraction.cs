using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Space near a fridge opens a two-column window: the fridge on the left, the food in the bag on the right.
/// Clicking a row moves that whole stack across. Space, Esc or 닫기 close it.
/// </summary>
[DefaultExecutionOrder(-320)]
[DisallowMultipleComponent]
public sealed class FridgeInteraction : MonoBehaviour
{
    public static bool HasNearbyFridge { get; private set; }
    public static bool IsOpen => open;

    static bool open;
    PlayerMovement movement;
    PlayerInventory player;
    FridgeStorage fridge;
    GameObject promptRoot, windowRoot;
    TMP_Text promptLabel, statusLabel;
    RectTransform fridgeList, bagList;
    TMP_FontAsset font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { HasNearbyFridge = false; open = false; }

    void Awake() { movement = GetComponent<PlayerMovement>(); player = GetComponent<PlayerInventory>(); }

    void Update()
    {
        if (open)
        {
            if (!fridge || GameInput.CancelPressed || (GameInput.InteractPressed && !PlayerInventory.SpaceConsumedThisFrame))
            {
                PlayerInventory.ConsumeSpaceThisFrame();
                Close();
            }
            return;
        }
        HasNearbyFridge = false;
        if (!movement || !movement.isActiveAndEnabled || SceneLoadManager.IsLoading ||
            SeatInteraction.HasNearbySeat || FurnitureLightInteraction.HasNearbyLight ||
            DialogueManager.HasNearbyNpc || DialogueManager.IsDialogueOpen || DialogueManager.OwnsInput ||
            PlayerInventory.IsAnyOpen || ChatUIManager.IsChatting || UIEventSystem.IsEditingText() ||
            (PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) ||
            (PlayerInteraction.Local && PlayerInteraction.Local.IsInteractionMenuOpen)) { HidePrompt(); return; }
        var nearest = FridgeStorage.FindNearest(transform);
        HasNearbyFridge = nearest;
        if (!nearest) { HidePrompt(); return; }
        if (GameInput.InteractPressed && !PlayerInventory.SpaceConsumedThisFrame)
        {
            PlayerInventory.ConsumeSpaceThisFrame();
            Open(nearest);
            return;
        }
        ShowPrompt("냉장고 열기  (스페이스바)");
    }

    void Open(FridgeStorage target)
    {
        fridge = target; open = true; HidePrompt();
        if (!windowRoot) BuildWindow();
        windowRoot.SetActive(true);
        fridge.Contents.Changed += Refresh;
        if (player && player.Inventory != null) player.Inventory.Changed += Refresh;
        statusLabel.text = "";
        Refresh();
    }

    void Close()
    {
        if (fridge) fridge.Contents.Changed -= Refresh;
        if (player && player.Inventory != null) player.Inventory.Changed -= Refresh;
        fridge = null; open = false;
        if (windowRoot) windowRoot.SetActive(false);
    }

    void Refresh()
    {
        if (!open || !fridge) return;
        var bag = player ? player.Inventory : null;
        Fill(fridgeList, fridge.Contents, i =>
        {
            if (bag == null) return;
            statusLabel.text = FoodShelfLife.TryTakeFromFridge(fridge.Contents, i, bag, out string error) ? "꺼냈어요." : error;
        });
        Fill(bagList, bag, i =>
        {
            statusLabel.text = FoodShelfLife.TryPutInFridge(bag, i, fridge.Contents, out string error) ? "넣었어요." : error;
        }, food: true);
    }

    void Fill(RectTransform list, InventoryState state, System.Action<int> click, bool food = false)
    {
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
        if (state == null) return;
        int shown = 0;
        for (int i = 0; i < state.Capacity; i++)
        {
            var stack = state.GetSlot(i);
            if (stack == null || stack.IsEmpty || (food && !FoodShelfLife.IsPerishable(stack.Item))) continue;
            int index = i;
            string detail = stack.TooltipDetails;
            Row(list, stack.DisplayName + " x" + stack.Count + (string.IsNullOrEmpty(detail) ? "" : "\n<size=13>" + detail + "</size>"),
                stack.IsSpoiled, () => click(index));
            shown++;
        }
        if (shown == 0) Text(list, food ? "가방에 음식이 없어요." : "비어 있어요.", 15f, UITheme.Brown, 40f);
    }

    void Row(RectTransform list, string text, bool spoiled, UnityEngine.Events.UnityAction onClick)
    {
        var rect = UIBuild.Rect("Row", list, new Vector2(0f, 52f));
        rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
        var g = UITheme.Rounded(rect, Color.white, UITheme.SmallRadius, 1.5f); g.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = g;
        var colors = button.colors;
        colors.normalColor = spoiled ? Color.Lerp(UITheme.Sand, UITheme.Danger, .25f) : UITheme.Sand;
        colors.highlightedColor = UITheme.Ivory; colors.pressedColor = colors.selectedColor = UITheme.Selected; button.colors = colors;
        button.onClick.AddListener(onClick);
        var label = Text(rect, text, 16f, spoiled ? UITheme.Danger : UITheme.Ink, 0f);
        UIBuild.Stretch(label.rectTransform); label.margin = new Vector4(12f, 4f, 12f, 4f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
    }

    TMP_Text Text(Transform parent, string text, float size, Color color, float height)
    {
        var label = UITheme.Text(parent, text, size, color, font, new Vector2(0f, height));
        if (height > 0f) label.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return label;
    }

    void BuildWindow()
    {
        font = PlayerInteraction.Local ? PlayerInteraction.Local.uiFont : null;
        windowRoot = Canvas("FridgeCanvas", 210);
        var card = UITheme.Panel("Window", windowRoot.transform, new Vector2(760f, 500f));
        var title = Text(card, "냉장고", 24f, UITheme.Ink, 40f); title.fontStyle = FontStyles.Bold;
        Place(title.rectTransform, new Vector2(0f, 222f), new Vector2(700f, 40f));
        fridgeList = Column(card, "냉장고 · 누르면 꺼내기", -186f);
        bagList = Column(card, "가방 음식 · 누르면 넣기", 186f);
        statusLabel = Text(card, "", 15f, UITheme.Pressed, 28f);
        Place(statusLabel.rectTransform, new Vector2(-80f, -222f), new Vector2(520f, 28f));
        var close = UITheme.Button(card, "닫기", new Vector2(120f, 36f), true, font, Close);
        Place((RectTransform)close.transform, new Vector2(300f, -222f), new Vector2(120f, 36f));
    }

    RectTransform Column(RectTransform card, string heading, float x)
    {
        var head = Text(card, heading, 15f, UITheme.Brown, 30f);
        Place(head.rectTransform, new Vector2(x, 184f), new Vector2(350f, 30f));
        var view = UIBuild.Rect("View", card, new Vector2(350f, 360f));
        Place(view, new Vector2(x, -10f), new Vector2(350f, 360f));
        UITheme.Rounded(view, UITheme.A(UITheme.Sand, .55f), UITheme.Radius, 0f).raycastTarget = true;
        view.gameObject.AddComponent<RectMask2D>();
        var content = UIBuild.Rect("Content", view, Vector2.zero);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f; layout.padding = new RectOffset(8, 8, 8, 8);
        layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.horizontal = false; scroll.viewport = view;
        return content;
    }

    static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size; rect.anchoredPosition = position;
    }

    GameObject Canvas(string name, int order)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f); scaler.matchWidthOrHeight = .5f;
        return root;
    }

    void ShowPrompt(string text)
    {
        if (!promptRoot)
        {
            if (!font) font = PlayerInteraction.Local ? PlayerInteraction.Local.uiFont : null;
            promptRoot = Canvas("FridgePromptCanvas", 195);
            var panel = UITheme.Panel("Panel", promptRoot.transform, new Vector2(300f, 44f));
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, 0f); panel.pivot = new Vector2(.5f, 0f); panel.anchoredPosition = new Vector2(0f, 168f);
            promptLabel = Text(panel, "", 18f, UITheme.Ink, 0f);
            UIBuild.Stretch(promptLabel.rectTransform);
        }
        promptLabel.text = text; promptRoot.SetActive(true);
    }

    void HidePrompt() { if (promptRoot) promptRoot.SetActive(false); }
    void OnDisable() { HasNearbyFridge = false; HidePrompt(); if (open) Close(); }
}
