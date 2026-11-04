using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class InventoryUI
{
    void BuildHotbar()
    {
        var bar = Panel("QuickSlots", transform, new Vector2(618f, 78f), UITheme.A(UITheme.Ivory, .93f), 25f, 11f);
        quickSlots = bar;
        interaction = owner.GetComponent<PlayerInteraction>();
        bar.GetComponent<InventoryRoundedGraphic>().raycastTarget = true;
        bar.anchorMin = bar.anchorMax = new Vector2(.5f, 0f);
        bar.pivot = new Vector2(.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 12f);
        var row = UIBuild.Rect("Slots", bar, new Vector2(568f, 64f));
        row.anchoredPosition = new Vector2(-12f, 0f);
        var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        for (int i = 0; i < InventoryState.HotbarSize; i++)
        {
            int index = i;
            var slot = CreateSlot("QuickSlot_" + (i + 1), row, 64f, (i + 1).ToString(), () => owner.SelectHotbar(index));
            InventorySlotPointer.AttachStorage(slot.background.gameObject, owner, index);
            hotbar.Add(slot);
        }
        var key = Label("InventoryKey", bar, "E", 14f, Ink, new Vector2(24f, 28f));
        key.fontStyle = FontStyles.Bold;
        key.rectTransform.anchoredPosition = new Vector2(287f, 0f);
    }

    void BuildPortrait()
    {
        var card = Panel("CurrentPlayer", window, new Vector2(344f, 368f), UITheme.Slate, 10f, 10f);
        card.GetComponent<InventoryRoundedGraphic>().borderWidth = 0f;
        card.GetComponent<InventoryRoundedGraphic>().chamfered = true;
        AtTop(card, -180f, -246f);
        var caption = Label("Caption", card, "착용", 16f, Ink, new Vector2(100f, 24f));
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.fontStyle = FontStyles.Bold;
        AtTop(caption.rectTransform, -104f, -18f);
        var ground = Icon("PortraitGround", card, InventoryGlyphGraphic.Glyph.Coin,
            new Vector2(94f, 13f), UITheme.A(UITheme.Brown, .18f));
        ground.anchoredPosition = new Vector2(-44f, -137f);
        var portraitRect = UIBuild.Rect("CharacterPortrait", card, new Vector2(210f, 344f));
        portraitRect.anchoredPosition = new Vector2(-44f, -3f);
        var portraitImage = portraitRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        portraitImage.raycastTarget = false;
        portraitImage.color = Color.white;
        preview = gameObject.AddComponent<InventoryCharacterPreview>();
        preview.Initialize(owner.GetComponent<PlayerMovement>(), portraitImage);
    }

    void BuildEquipment()
    {
        var group = UIBuild.Rect("Clothing", window, new Vector2(344f, 368f));
        AtTop(group, -180f, -246f);
        // Preserve the original single vertical column: top, bottom, socks, shoes.
        for (int i = 0; i < ClothingSlots.Length; i++)
        {
            EquipmentSlot slot = ClothingSlots[i];
            var view = CreateSlot("Equipment_" + slot, group, 60f, "", () => owner.ClickEquipmentSlot(slot));
            StyleWardrobeSlot(view);
            view.background.rectTransform.anchoredPosition = new Vector2(124f, 132f - i * 66f);
            view.emptyLabel.text = EquipmentName(slot);
            view.emptyLabel.fontSize = 11f;
            view.emptyLabel.rectTransform.anchoredPosition = new Vector2(0f, -18f);
            var glyph = Icon("EquipmentSilhouette", view.background.transform, (InventoryGlyphGraphic.Glyph)(i + 1), new Vector2(30f, 30f), UITheme.A(UITheme.Brown, .65f));
            glyph.anchoredPosition = new Vector2(0f, 6f);
            InventorySlotPointer.AttachEquipment(view.background.gameObject, owner, slot);
            equipment.Add(slot, view);
        }
    }

    void BuildPet()
    {
        var group = UIBuild.Rect("CompanionPet", window, new Vector2(60f, 60f));
        AtTop(group, -56f, -378f);
        var view = CreateSlot("Equipment_Pet", group, 60f, "", () => owner.ClickEquipmentSlot(EquipmentSlot.Pet));
        StyleWardrobeSlot(view);
        view.emptyLabel.fontSize = 11f;
        view.emptyLabel.rectTransform.anchoredPosition = new Vector2(0f, -18f);
        Icon("EquipmentSilhouette", view.background.transform, InventoryGlyphGraphic.Glyph.Pet,
            new Vector2(30f, 30f), UITheme.A(UITheme.Brown, .65f)).anchoredPosition = new Vector2(0f, 6f);
        InventorySlotPointer.AttachEquipment(view.background.gameObject, owner, EquipmentSlot.Pet);
        equipment.Add(EquipmentSlot.Pet, view);
    }

    static void StyleWardrobeSlot(SlotView view)
    {
        view.flatSurface = true;
        var graphic = view.background;
        graphic.chamfered = true;
        graphic.largeRadius = graphic.smallRadius = 7f;
        graphic.bevelEdges = true;
        graphic.borderWidth = 1.25f;
        // A shallow matte recess; no dark outline around every pocket.
        graphic.edgeTopColor = Color.Lerp(UITheme.Sand, UITheme.Brown, .28f);
        graphic.edgeBottomColor = UITheme.Ivory;
    }

    void Heading(RectTransform card, string text, InventoryGlyphGraphic.Glyph glyph, float left)
    {
        var icon = Icon("HeadingIcon", card, glyph, new Vector2(19f, 19f), Ink);
        icon.anchoredPosition = new Vector2(left, card.sizeDelta.y * .5f - 14f);
        var label = Label("Caption", card, text, 15f, Ink, new Vector2(card.sizeDelta.x - 40f, 24f));
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.rectTransform.anchoredPosition = new Vector2(12f, card.sizeDelta.y * .5f - 14f);
    }

    SlotView CreateSlot(string name, Transform parent, float size, string key, Action onClick)
    {
        var rect = Panel(name, parent, Vector2.one * size, SlotColor, size * .28f, size * .11f);
        var view = new SlotView { background = rect.GetComponent<InventoryRoundedGraphic>() };
        view.button = MakeButton(rect, onClick);
        var iconRect = UIBuild.Rect("Icon", rect, Vector2.one * size * .68f);
        iconRect.anchoredPosition = Vector2.zero;
        view.icon = iconRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        view.icon.preserveAspect = true;
        view.icon.useSpriteMesh = true;
        view.icon.raycastTarget = false;
        view.count = Label("Quantity", rect, "", size < 80 ? 12f : 16f, Ink, new Vector2(size - 16f, size < 80 ? 29f : 36f));
        view.count.alignment = TextAlignmentOptions.TopRight;
        view.count.fontStyle = FontStyles.Bold;
        view.count.rectTransform.anchorMin = view.count.rectTransform.anchorMax = new Vector2(1f, 1f);
        view.count.rectTransform.pivot = new Vector2(1f, 1f);
        view.count.rectTransform.anchoredPosition = new Vector2(-7f, -4f);
        view.number = Label("Key", rect, key, size < 80 ? 11f : 12f, Muted, new Vector2(24f, 22f));
        view.number.rectTransform.anchorMin = view.number.rectTransform.anchorMax = new Vector2(0f, 1f);
        view.number.rectTransform.pivot = new Vector2(0f, 1f);
        view.number.rectTransform.anchoredPosition = new Vector2(8f, -4f);
        view.emptyLabel = Label("Empty", rect, "+", size < 80 ? 20f : 28f, UITheme.A(UITheme.Brown, .6f), new Vector2(size - 10f, Mathf.Min(size - 10f, 64f)));
        return view;
    }

    void FitWindow()
    {
        var canvasRect = (RectTransform)transform;
        float width = Mathf.Max(1f, canvasRect.rect.width - 36f);
        float height = Mathf.Max(1f, canvasRect.rect.height - 112f);
        window.localScale = Vector3.one * Mathf.Min(1f, width / WindowWidth, height / windowHeight);
        window.anchoredPosition = Vector2.zero;
        PositionWithdrawal();
    }

    static string EquipmentName(EquipmentSlot slot)
    {
        switch (slot)
        {
            case EquipmentSlot.Top: return "상의";
            case EquipmentSlot.Bottom: return "하의";
            case EquipmentSlot.Socks: return "양말";
            case EquipmentSlot.Shoes: return "신발";
            case EquipmentSlot.Pet: return "펫";
            default: return "";
        }
    }

    UnityEngine.UI.Button MakeButton(RectTransform rect, Action onClick)
    {
        var graphic = rect.GetComponent<InventoryRoundedGraphic>(); graphic.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = graphic;
        var colors = button.colors;
        colors.normalColor = Color.white; colors.highlightedColor = UITheme.Ivory;
        colors.selectedColor = Color.white; colors.pressedColor = UITheme.Slate;
        colors.disabledColor = new Color(.76f, .76f, .76f, .68f); button.colors = colors;
        button.onClick.AddListener(() => onClick());
        var navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
        return button;
    }

    RectTransform Panel(string name, Transform parent, Vector2 size, Color color, float large = 28f, float small = 11f)
    {
        var rect = UIBuild.Rect(name, parent, size);
        var graphic = rect.gameObject.AddComponent<InventoryRoundedGraphic>();
        // Theme corners: soft but never pill-like, whatever radius the caller asked for.
        graphic.color = color; graphic.largeRadius = graphic.smallRadius = Mathf.Min(large, UITheme.Radius * 1.4f);
        graphic.borderColor = UITheme.Brown;
        graphic.borderWidth = large > 0f ? 2f : 0f; graphic.raycastTarget = false; return rect;
    }

    static RectTransform Icon(string name, Transform parent, InventoryGlyphGraphic.Glyph kind, Vector2 size, Color color)
    {
        var rect = UIBuild.Rect(name, parent, size); var glyph = rect.gameObject.AddComponent<InventoryGlyphGraphic>();
        glyph.kind = kind; glyph.color = color; glyph.raycastTarget = false; return rect;
    }

    TMP_Text Label(string name, Transform parent, string value, float size, Color color, Vector2 dimensions)
    {
        var rect = UIBuild.Rect(name, parent, dimensions); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = size; text.color = color;
        text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; return text;
    }

    static void AutoSize(TMP_Text label, float min, float max) { label.enableAutoSizing = true; label.fontSizeMin = min; label.fontSizeMax = max; }

    static void AtTop(RectTransform rect, float x, float y) { rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f); rect.anchoredPosition = new Vector2(x, y); }

    static void AtBottom(RectTransform rect, float x, float y) { rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f); rect.anchoredPosition = new Vector2(x, y); }
}
