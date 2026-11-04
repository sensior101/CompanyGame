using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The game's one UI look: cream panels, soft plain shading, corners rounded just enough not to look square.
/// Palette: ivory F5EEDF, sand beige E9DDC8, slate beige DCCCB4, brown 8A7258, selected orange E6A24C,
/// pressed brown-orange C97B3D, support green 8FAE8A. New UI takes its colours and parts from here.
/// </summary>
public static class UITheme
{
    public static readonly Color Ivory = Hex(0xF5EEDF), Sand = Hex(0xE9DDC8), Slate = Hex(0xDCCCB4), Brown = Hex(0x8A7258);
    public static readonly Color Selected = Hex(0xE6A24C), Pressed = Hex(0xC97B3D), Green = Hex(0x8FAE8A);
    /// <summary>Body text: the brown taken darker so small Korean text stays readable on ivory.</summary>
    public static readonly Color Ink = Hex(0x5A4636);
    public static readonly Color Danger = Hex(0xC4574E);
    /// <summary>Panels let the world show through a little.</summary>
    public const float PanelAlpha = .88f;
    /// <summary>Corner radius in reference pixels (1280x720): soft, never pill-like.</summary>
    public const float Radius = 10f, SmallRadius = 7f;

    public static Color Hex(int rgb, float alpha = 1f) =>
        new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, alpha);
    public static Color A(Color c, float alpha) { c.a = alpha; return c; }

    /// <summary>A rounded panel with a slate-beige rim; a darker strip under it gives the simple shading.</summary>
    public static RectTransform Panel(string name, Transform parent, Vector2 size, Color? fill = null, float radius = Radius, bool shade = false)
    {
        var rect = UIBuild.Rect(name, parent, size);
        if (shade)
        {
            var under = Rounded(UIBuild.Rect("Shade", rect, Vector2.zero), A(Slate, PanelAlpha), radius, 0f);
            UIBuild.Stretch(under.rectTransform); under.rectTransform.offsetMin = new Vector2(0f, -3f); under.rectTransform.offsetMax = new Vector2(0f, -3f);
        }
        var body = Rounded(UIBuild.Rect("Body", rect, Vector2.zero), fill ?? A(Ivory, PanelAlpha), radius, 2f);
        UIBuild.Stretch(body.rectTransform);
        return rect;
    }

    public static InventoryRoundedGraphic Rounded(RectTransform rect, Color color, float radius, float border)
    {
        var g = rect.gameObject.AddComponent<InventoryRoundedGraphic>();
        g.color = color; g.largeRadius = g.smallRadius = Mathf.Min(radius, 12f); g.borderWidth = border; g.borderColor = Brown; g.raycastTarget = false;
        return g;
    }

    /// <summary>Filled button: orange for the main action, sand for the rest; pressed turns brown-orange.</summary>
    public static Button Button(Transform parent, string label, Vector2 size, bool primary, TMP_FontAsset font, UnityEngine.Events.UnityAction onClick)
    {
        var rect = UIBuild.Rect(label, parent, size);
        var g = Rounded(rect, Color.white, SmallRadius, primary ? 0f : 1.5f); g.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = g;
        var colors = button.colors;
        colors.normalColor = primary ? Selected : Sand; colors.highlightedColor = primary ? Color.Lerp(Selected, Color.white, .15f) : Ivory;
        colors.pressedColor = colors.selectedColor = Pressed; colors.colorMultiplier = 1f; button.colors = colors;
        button.onClick.AddListener(onClick);
        var text = Text(rect, label, 16f, primary ? Color.white : Ink, font); UIBuild.Stretch(text.rectTransform);
        return button;
    }

    public static TextMeshProUGUI Text(Transform parent, string value, float size, Color color, TMP_FontAsset font, Vector2 dimensions = default)
    {
        var rect = UIBuild.Rect("Text", parent, dimensions);
        var t = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font) t.font = font;
        t.text = value; t.fontSize = size; t.color = color; t.raycastTarget = false; t.alignment = TextAlignmentOptions.Center;
        return t;
    }
}
