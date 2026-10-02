using System;
using System.Collections.Generic;
using CompanyGame.World.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>Scene-local transport prompt and destination picker, owned by PlayerInteraction.</summary>
public sealed class TransitUI : MonoBehaviour
{
    TMP_FontAsset font;
    GameObject prompt;
    GameObject modal;
    RectTransform cards;
    TMP_Text promptLabel;
    TMP_Text title;
    TMP_Text subtitle;
    TMP_Text status;
    UnityEngine.UI.Button close;
    readonly List<UnityEngine.UI.Button> destinationButtons = new List<UnityEngine.UI.Button>();
    static readonly Color Ink = new Color(.075f, .12f, .17f);
    static readonly Color Paper = new Color(.94f, .96f, .97f);
    static readonly Color Mint = new Color(.3f, .88f, .72f);

    public static TransitUI Create(TMP_FontAsset font, Action onClose)
    {
        var host = new GameObject("TransitCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        var ui = host.AddComponent<TransitUI>();
        ui.font = font ? font : TMP_Settings.defaultFontAsset;
        ui.Build(onClose);
        return ui;
    }

    void Build(Action onClose)
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = .5f;
        EnsureEventSystem();

        var promptRect = Panel("BoardingPrompt", transform, new Vector2(342f, 62f), Ink);
        prompt = promptRect.gameObject;
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(.5f, 0f);
        promptRect.pivot = new Vector2(.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 100f);
        var stripe = Panel("Accent", promptRect, new Vector2(5f, 42f), Mint);
        stripe.anchoredPosition = new Vector2(-164f, 0f);
        promptLabel = Label("Action", promptRect, "", 23f, Paper, new Vector2(214f, 52f));
        promptLabel.rectTransform.anchoredPosition = new Vector2(-44f, 0f);
        var cap = Panel("SpaceKeycap", promptRect, new Vector2(88f, 34f), new Color(.86f, .91f, .94f));
        cap.anchoredPosition = new Vector2(110f, 0f);
        var key = Label("Space", cap, "SPACE", 16f, Ink, new Vector2(88f, 32f));
        key.fontStyle = FontStyles.Bold;
        var capEdge = Panel("KeycapEdge", cap, new Vector2(74f, 2f), new Color(.4f, .5f, .57f));
        capEdge.anchoredPosition = new Vector2(0f, -13f);
        prompt.SetActive(false);

        var shade = Panel("TransitModal", transform, Vector2.zero, new Color(.015f, .025f, .04f, .78f));
        Stretch(shade);
        shade.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        modal = shade.gameObject;
        var panel = Panel("DestinationWindow", shade, new Vector2(586f, 636f), Ink);
        panel.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        var topLine = Panel("HeaderAccent", panel, new Vector2(530f, 3f), Mint);
        topLine.anchoredPosition = new Vector2(0f, 296f);
        title = Label("Title", panel, "", 30f, Paper, new Vector2(430f, 46f));
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.rectTransform.anchoredPosition = new Vector2(-48f, 257f);
        subtitle = Label("Subtitle", panel, "", 17f, new Color(.68f, .76f, .82f), new Vector2(530f, 42f));
        subtitle.alignment = TextAlignmentOptions.MidlineLeft;
        subtitle.rectTransform.anchoredPosition = new Vector2(0f, 207f);

        var closeRect = Panel("Close", panel, new Vector2(68f, 40f), new Color(.19f, .25f, .3f));
        closeRect.anchoredPosition = new Vector2(231f, 257f);
        close = closeRect.gameObject.AddComponent<UnityEngine.UI.Button>();
        close.onClick.AddListener(() => onClose());
        closeRect.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        Label("Label", closeRect, "닫기", 17f, Paper, new Vector2(68f, 40f));

        cards = Rect("Destinations", panel, new Vector2(530f, 400f));
        cards.anchoredPosition = new Vector2(0f, -17f);
        var layout = cards.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        status = Label("Status", panel, "목적지를 선택하세요.   ESC  닫기", 16f,
            new Color(.72f, .79f, .83f), new Vector2(530f, 62f));
        status.rectTransform.anchoredPosition = new Vector2(0f, -270f);
        status.textWrappingMode = TextWrappingModes.Normal;
        modal.SetActive(false);
    }

    public void ShowPrompt(string label)
    {
        promptLabel.text = label;
        prompt.SetActive(true);
    }

    public void HidePrompt() { if (prompt) prompt.SetActive(false); }
    public void HideModal() { if (modal) modal.SetActive(false); }

    public void ShowDestinations(TransitKind kind, string currentName,
        IReadOnlyList<TransitDestination> destinations, Action<TransitDestination> onChoose)
    {
        HidePrompt();
        foreach (Transform child in cards)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        destinationButtons.Clear();
        title.text = kind == TransitKind.Subway ? "지하철로 이동" : "버스로 이동";
        subtitle.text = "현재 위치  " + currentName;
        status.text = destinations.Count == 0 ? "현재 이동할 수 있는 지구가 없습니다." : "목적지를 선택하세요.   ESC  닫기";
        modal.SetActive(true);
        foreach (var destination in destinations)
        {
            var selectedDestination = destination;
            var row = Panel("Destination_" + destination.displayName, cards, new Vector2(530f, 70f), new Color(.15f, .21f, .27f));
            var element = row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.preferredHeight = 70f;
            var button = row.gameObject.AddComponent<UnityEngine.UI.Button>();
            row.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.65f, 1f, .89f);
            colors.selectedColor = new Color(.65f, 1f, .89f);
            colors.pressedColor = new Color(.45f, .78f, .69f);
            colors.disabledColor = new Color(.55f, .6f, .64f);
            button.colors = colors;
            button.onClick.AddListener(() => onChoose(selectedDestination));
            var name = Label("Name", row, destination.displayName, 22f, Paper, new Vector2(438f, 42f));
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.rectTransform.anchoredPosition = new Vector2(-23f, 12f);
            var detail = Label("Description", row, destination.description, 14f,
                new Color(.7f, .8f, .86f), new Vector2(438f, 23f));
            detail.alignment = TextAlignmentOptions.MidlineLeft;
            detail.rectTransform.anchoredPosition = new Vector2(-23f, -17f);
            var arrow = Label("Arrow", row, ">", 24f, Mint, new Vector2(32f, 50f));
            arrow.rectTransform.anchoredPosition = new Vector2(230f, 0f);
            destinationButtons.Add(button);
        }
        WireNavigation();
        SetInteractable(false);
    }

    public void SetInteractable(bool enabled)
    {
        foreach (var button in destinationButtons) button.interactable = enabled;
        close.interactable = enabled;
    }

    public void FocusFirstDestination()
    {
        if (!EventSystem.current) return;
        EventSystem.current.SetSelectedGameObject(destinationButtons.Count > 0
            ? destinationButtons[0].gameObject : close.gameObject);
    }

    public void ShowStatus(string message) { status.text = message; }

    void WireNavigation()
    {
        for (int i = 0; i < destinationButtons.Count; i++)
        {
            var navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit };
            navigation.selectOnUp = i == 0 ? close : destinationButtons[i - 1];
            navigation.selectOnDown = i == destinationButtons.Count - 1 ? close : destinationButtons[i + 1];
            destinationButtons[i].navigation = navigation;
        }
        var closeNavigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit };
        if (destinationButtons.Count > 0)
        {
            closeNavigation.selectOnDown = destinationButtons[0];
            closeNavigation.selectOnUp = destinationButtons[destinationButtons.Count - 1];
        }
        close.navigation = closeNavigation;
    }

    void EnsureEventSystem()
    {
        var existing = FindFirstObjectByType<EventSystem>();
        if (existing && existing.GetComponent<BaseInputModule>()) return;
        var host = existing ? existing.gameObject : new GameObject("TransitEventSystem", typeof(EventSystem));
        if (!existing) host.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM
        host.AddComponent<InputSystemUIInputModule>();
#else
        host.AddComponent<StandaloneInputModule>();
#endif
    }

    static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        return rect;
    }

    static RectTransform Panel(string name, Transform parent, Vector2 size, Color color)
    {
        var rect = Rect(name, parent, size);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    TMP_Text Label(string name, Transform parent, string value, float size, Color color, Vector2 dimensions)
    {
        var rect = Rect(name, parent, dimensions);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.text = value;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
