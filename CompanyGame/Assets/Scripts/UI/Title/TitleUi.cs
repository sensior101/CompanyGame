using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Small uGUI builders shared by the title screen and the pause menu, so both look the same.
/// Everything is made in code (no prefabs); layout comes from VerticalLayoutGroup.
/// </summary>
public static class TitleUi
{
    public static readonly Color Ink = new Color32(0x3B, 0x2A, 0x20, 255);
    public static readonly Color Cream = new Color32(0xFF, 0xF1, 0xD6, 255);
    public static readonly Color Amber = new Color32(0xFF, 0xC8, 0x57, 255);
    public static readonly Color Panel = new Color32(0x1B, 0x1D, 0x3A, 235);
    public static readonly Color Muted = new Color32(0xC9, 0xC3, 0xD9, 255);
    public static readonly Color Warn = new Color32(0xFF, 0x9A, 0x8A, 255);
    public static readonly Color Good = new Color32(0xA8, 0xE6, 0xA1, 255);

    public static TMP_FontAsset Font
    {
        get { var style = TitleStyle.Load(); return style && style.font ? style.font : TMP_Settings.defaultFontAsset; }
    }

    static Sprite Rounded { get { var style = TitleStyle.Load(); return style ? style.rounded : null; } }

    public static Canvas NewCanvas(string name, int order)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    public static RectTransform Full(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    /// <summary>A vertical stack of children, as wide as <paramref name="width"/> (root columns) and as tall as its content.</summary>
    public static RectTransform Column(Transform parent, string name, float width, float spacing, Color? background = null, int padding = 0, bool root = true)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(width, 0);
        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        // A root column sizes itself to its content; a nested one is sized by its parent's layout group.
        if (root) go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        if (background.HasValue)
        {
            var image = go.AddComponent<Image>();
            image.color = background.Value; image.sprite = Rounded; image.type = Image.Type.Sliced;
        }
        return rt;
    }

    public static TMP_Text Label(Transform parent, string text, int size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center, FontStyles style = FontStyles.Normal)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.font = Font; label.text = text; label.fontSize = size; label.color = color;
        label.alignment = align; label.fontStyle = style; label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    /// <summary>A horizontal row. equalShares: children split the width equally; otherwise size them with <see cref="Width"/>.</summary>
    public static RectTransform Row(Transform parent, float height, float spacing = 12f, bool equalShares = true)
    {
        var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = equalShares; layout.childForceExpandHeight = true;
        return (RectTransform)go.transform;
    }

    /// <summary>Gives a child of a Row a fixed width (flexible = takes the rest instead).</summary>
    public static void Width(Component child, float preferred, float flexible = 0f)
    {
        var element = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = preferred; element.flexibleWidth = flexible;
    }

    public static void Spacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
    }

    public static Button Button(Transform parent, string text, Action onClick, float height = 64f, bool primary = true, int fontSize = 30)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var image = go.GetComponent<Image>();
        image.sprite = Rounded; image.type = Image.Type.Sliced; image.color = Color.white;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        // Keyboard focus (Selected) must look like mouse hover, otherwise arrow-key navigation is invisible.
        Color tint = primary ? Cream : (Color)new Color32(0xB9, 0xB3, 0xCC, 255);
        button.colors = new ColorBlock
        {
            normalColor = tint, highlightedColor = Amber, selectedColor = Amber,
            pressedColor = new Color32(0xE0, 0xA0, 0x30, 255), disabledColor = new Color32(0x6A, 0x68, 0x80, 255),
            colorMultiplier = 1f, fadeDuration = 0.08f
        };
        var label = Label(go.transform, text, fontSize, Ink);
        var rt = (RectTransform)label.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        button.onClick.AddListener(() => onClick?.Invoke());
        return button;
    }

    public static TMP_InputField Input(Transform parent, string placeholder, int maxLength, Action<string> onSubmit = null, float height = 60f)
    {
        var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var image = go.GetComponent<Image>();
        image.sprite = Rounded; image.type = Image.Type.Sliced; image.color = new Color32(0xF7, 0xF3, 0xFF, 255);

        var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var areaRt = (RectTransform)area.transform;
        areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one; areaRt.offsetMin = new Vector2(16, 6); areaRt.offsetMax = new Vector2(-16, -6);

        var text = Label(area.transform, "", 28, Ink, TextAlignmentOptions.MidlineLeft);
        var hint = Label(area.transform, placeholder, 28, new Color32(0x8A, 0x84, 0x9C, 255), TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
        foreach (var t in new[] { text, hint })
        {
            var rt = (RectTransform)t.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            t.textWrappingMode = TextWrappingModes.NoWrap;
        }

        go.SetActive(false); // wire the references first: TMP_InputField reads them in OnEnable
        var field = go.AddComponent<TMP_InputField>();
        field.textViewport = areaRt; field.textComponent = text; field.placeholder = hint; field.fontAsset = Font;
        field.targetGraphic = image; field.characterLimit = maxLength; field.pointSize = 28;
        field.selectionColor = new Color32(0xFF, 0xC8, 0x57, 140); field.caretColor = Ink; field.customCaretColor = true;
        if (onSubmit != null) field.onSubmit.AddListener(v => onSubmit(v));
        go.SetActive(true);
        return field;
    }

    /// <summary>Invokes the first enabled button under <paramref name="root"/> whose text contains <paramref name="text"/> (headless QA).</summary>
    public static bool ClickButton(Transform root, string text)
    {
        foreach (var b in root.GetComponentsInChildren<Button>())
        {
            var label = b.GetComponentInChildren<TMP_Text>();
            if (b.interactable && label && label.text.Contains(text)) { b.onClick.Invoke(); return true; }
        }
        return false;
    }

    /// <summary>The first enabled Button/InputField under <paramref name="root"/> gets keyboard focus.</summary>
    public static void FocusFirst(Transform root)
    {
        if (!EventSystem.current) return;
        foreach (var s in root.GetComponentsInChildren<Selectable>())
            if (s.IsActive() && s.interactable) { EventSystem.current.SetSelectedGameObject(s.gameObject); return; }
    }

    public static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
    }
}
