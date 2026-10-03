using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class InventoryUI
{
    void BuildStatusBars()
    {
        playerStats = owner.GetComponent<PlayerStats>();
        if (!playerStats) playerStats = owner.gameObject.AddComponent<PlayerStats>();
        var row = UIBuild.Rect("PlayerStatusBars", quickSlots, new Vector2(618f, 36f));
        row.anchorMin = row.anchorMax = new Vector2(.5f, 1f);
        row.pivot = new Vector2(.5f, 0f);
        row.anchoredPosition = new Vector2(0f, 10f);
        var layout = row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        string[] names = { "Health", "Stamina", "Stress" };
        var colors = new[] { new Color(.91f,.19f,.25f), new Color(.36f,.36f,.94f), new Color(1f,.49f,.13f) };
        var glyphs = new[] { InventoryGlyphGraphic.Glyph.Heart, InventoryGlyphGraphic.Glyph.Energy, InventoryGlyphGraphic.Glyph.Stress };
        for (int i = 0; i < 3; i++)
        {
            var card = Panel(names[i], row, new Vector2(198f,36f), WindowColor, 15f, 15f);
            Icon("Icon", card, glyphs[i], new Vector2(24f,24f), colors[i]).anchoredPosition = new Vector2(-77f,0f);
            if (i == 2)
            {
                // Dark eyes/brows and mouth are geometry, never font characters.
                var face = card.Find("Icon");
                foreach (float x in new[] { -4.5f, 4.5f })
                {
                    var brow = Panel("Brow", face, new Vector2(6f,2f), Ink, 0f, 0f);
                    brow.anchoredPosition = new Vector2(x,3f);
                    brow.localRotation = Quaternion.Euler(0f,0f,x < 0 ? -20f : 20f);
                }
                var mouth = Panel("Mouth", face, new Vector2(8f,2f), Ink, 1f, 1f);
                mouth.GetComponent<InventoryRoundedGraphic>().borderWidth = 0f;
                mouth.anchoredPosition = new Vector2(0f,-4f);
            }
            var track = Panel("Track", card, new Vector2(140f,12f), new Color(.16f,.17f,.22f,.22f), 6f, 6f);
            track.anchoredPosition = new Vector2(17f,0f);
            var fill = Panel("Fill", track, Vector2.zero, colors[i], 5f, 5f);
            fill.GetComponent<InventoryRoundedGraphic>().borderWidth = 0f;
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            statusFills[i] = fill;
        }
        UpdateStatusBars();
    }

    void UpdateStatusBars()
    {
        if (!playerStats) return;
        SetStatusFill(0, playerStats.health);
        SetStatusFill(1, playerStats.stamina);
        SetStatusFill(2, playerStats.stress);
    }

    void SetStatusFill(int index, float value)
    {
        var fill = statusFills[index];
        if (!fill) return;
        float ratio = float.IsNaN(value) ? 0f : Mathf.Clamp01(value / 100f);
        if (!Mathf.Approximately(fill.anchorMax.x, ratio)) fill.anchorMax = new Vector2(ratio,1f);
        if (fill.gameObject.activeSelf != (ratio > 0f)) fill.gameObject.SetActive(ratio > 0f);
    }

    void BuildHotbar()
    {
        var bar = Panel("QuickSlots", transform, new Vector2(618f, 78f), new Color(.985f, .976f, .945f, .82f), 25f, 11f);
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
        var card = Panel("CurrentPlayer", window, new Vector2(312f, 336f), SurfaceColor, 34f, 15f);
        AtTop(card, -362f, -257f);
        Heading(card, "플레이어", InventoryGlyphGraphic.Glyph.Person, -111f);
        var portraitRect = UIBuild.Rect("CharacterPortrait", card, new Vector2(235.556f, 265f));
        portraitRect.anchoredPosition = new Vector2(0f, -11f);
        var portraitImage = portraitRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        portraitImage.raycastTarget = false;
        portraitImage.color = Color.white;
        preview = gameObject.AddComponent<InventoryCharacterPreview>();
        preview.Initialize(owner.GetComponent<PlayerMovement>(), portraitImage);
    }

    void BuildEquipment()
    {
        var group = Panel("Clothing", window, new Vector2(374f, 336f), SurfaceColor, 34f, 15f);
        AtTop(group, -3f, -257f);
        Heading(group, "착용", InventoryGlyphGraphic.Glyph.Top, -142f);
        for (int i = 0; i < ClothingSlots.Length; i++)
        {
            EquipmentSlot slot = ClothingSlots[i];
            var view = CreateSlot("Equipment_" + slot, group, 128f, "", () => owner.ClickEquipmentSlot(slot));
            view.background.rectTransform.anchoredPosition = new Vector2(i % 2 == 0 ? -73f : 73f, 51f - i / 2 * 142f);
            view.emptyLabel.text = EquipmentName(slot);
            view.emptyLabel.fontSize = 18f;
            view.emptyLabel.rectTransform.anchoredPosition = new Vector2(0f, -43f);
            var glyph = Icon("EquipmentSilhouette", view.background.transform, (InventoryGlyphGraphic.Glyph)(i + 1), new Vector2(67f, 67f), new Color(.62f, .53f, .48f, .42f));
            glyph.anchoredPosition = new Vector2(0f, 9f);
            InventorySlotPointer.AttachEquipment(view.background.gameObject, owner, slot);
            equipment.Add(slot, view);
        }
    }

    void BuildPet()
    {
        var group = Panel("CompanionPet", window, new Vector2(318f, 336f), SurfaceColor, 34f, 15f);
        AtTop(group, 359f, -257f);
        Heading(group, "동행 펫", InventoryGlyphGraphic.Glyph.Pet, -113f);
        var view = CreateSlot("Equipment_Pet", group, 228f, "", () => owner.ClickEquipmentSlot(EquipmentSlot.Pet));
        view.background.rectTransform.anchoredPosition = new Vector2(0f, -4f);
        view.emptyLabel.text = "펫";
        view.emptyLabel.fontSize = 21f;
        view.emptyLabel.rectTransform.anchoredPosition = new Vector2(0f, -139f);
        Icon("EquipmentSilhouette", view.background.transform, InventoryGlyphGraphic.Glyph.Pet,
            new Vector2(92f, 92f), new Color(.62f, .53f, .48f, .27f));
        InventorySlotPointer.AttachEquipment(view.background.gameObject, owner, EquipmentSlot.Pet);
        equipment.Add(EquipmentSlot.Pet, view);
    }

    void Heading(RectTransform card, string text, InventoryGlyphGraphic.Glyph glyph, float left)
    {
        var icon = Icon("HeadingIcon", card, glyph, new Vector2(29f, 29f), Ink);
        icon.anchoredPosition = new Vector2(left, 140f);
        var label = Label("Caption", card, text, 23f, Ink, new Vector2(card.sizeDelta.x - 84f, 37f));
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.rectTransform.anchoredPosition = new Vector2(27f, 140f);
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
        view.emptyLabel = Label("Empty", rect, "+", size < 80 ? 20f : 28f, new Color(.61f, .57f, .54f, .64f), new Vector2(size - 10f, Mathf.Min(size - 10f, 64f)));
        return view;
    }

    void FitWindow()
    {
        var canvasRect = (RectTransform)transform;
        float width = Mathf.Max(1f, canvasRect.rect.width - 36f);
        float height = Mathf.Max(1f, canvasRect.rect.height - 112f);
        window.localScale = Vector3.one * Mathf.Min(1f, width / 1100f, height / windowHeight);
        window.anchoredPosition = new Vector2(0f, 43f);
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
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, .975f, .92f);
        colors.selectedColor = Color.white; colors.pressedColor = new Color(.89f, .83f, .75f);
        colors.disabledColor = new Color(.76f, .76f, .76f, .68f); button.colors = colors;
        button.onClick.AddListener(() => onClick());
        var navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
        return button;
    }

    RectTransform Panel(string name, Transform parent, Vector2 size, Color color, float large = 28f, float small = 11f)
    {
        var rect = UIBuild.Rect(name, parent, size);
        var graphic = rect.gameObject.AddComponent<InventoryRoundedGraphic>();
        graphic.color = color; graphic.largeRadius = large; graphic.smallRadius = small;
        graphic.borderWidth = large > 0f ? 1.5f : 0f; graphic.raycastTarget = false; return rect;
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
