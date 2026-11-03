using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vitals above the hotbar: a row of 10 faceted hearts (health 20, half a heart each), and under it fullness, stamina
/// and stress as 10 square cells of 2 points each. Hearts and cells are generated sprites, filled with Image.fillAmount.
/// </summary>
public sealed partial class InventoryUI
{
    const int Cells = 10;
    static readonly Color HeartRed = new Color(.86f, .29f, .32f);
    static readonly Color[] VitalColors = { new Color(.45f, .74f, .43f), new Color(.38f, .55f, .9f), new Color(.96f, .6f, .27f) };
    static readonly string[] VitalNames = { "포만감", "기력", "스트레스" };
    static Sprite heartFrame, heartFill, cellSprite;
    readonly Image[] hearts = new Image[Cells];
    readonly Image[][] vitalCells = { new Image[Cells], new Image[Cells], new Image[Cells] };

    TMP_Text foodPoisonLabel;

    void BuildStatusBars()
    {
        playerStats = owner.GetComponent<PlayerStats>();
        if (!playerStats) playerStats = owner.gameObject.AddComponent<PlayerStats>();
        MakeVitalSprites();

        var block = UIBuild.Rect("PlayerVitals", quickSlots, new Vector2(618f, 70f));
        block.anchorMin = block.anchorMax = new Vector2(.5f, 1f);
        block.pivot = new Vector2(.5f, 0f);
        block.anchoredPosition = new Vector2(0f, 8f);

        foodPoisonLabel = Label("FoodPoisonStatus", block, "", 13f, new Color(.72f, .9f, .4f), new Vector2(290f, 28f));
        foodPoisonLabel.alignment = TextAlignmentOptions.MidlineRight;
        foodPoisonLabel.rectTransform.anchoredPosition = new Vector2(164f, 21f);

        // Hearts, left-aligned on the top line.
        for (int i = 0; i < Cells; i++)
        {
            var frame = Img(block, "Heart" + i, heartFrame, Color.white, new Vector2(30f, 28f));
            frame.rectTransform.anchorMin = frame.rectTransform.anchorMax = frame.rectTransform.pivot = new Vector2(0f, 1f);
            frame.rectTransform.anchoredPosition = new Vector2(4f + i * 31f, 0f);
            var fill = Img(frame.rectTransform, "Fill", heartFill, Color.white, Vector2.zero);
            UIBuild.Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
            hearts[i] = fill;
        }

        // Fullness, stamina, stress: label then ten cells, side by side on the second line.
        for (int v = 0; v < 3; v++)
        {
            var group = UITheme.Panel(VitalNames[v], block, new Vector2(198f, 30f));
            group.anchorMin = group.anchorMax = group.pivot = new Vector2(0f, 0f);
            group.anchoredPosition = new Vector2(v * 210f, 0f);
            var label = Label("Label", group, VitalNames[v], 12f, UITheme.Ink, new Vector2(52f, 30f));
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0f, .5f);
            label.rectTransform.anchoredPosition = new Vector2(2f, 0f);
            for (int c = 0; c < Cells; c++)
            {
                var slot = Img(group, "Cell" + c, cellSprite, UITheme.Slate, new Vector2(12f, 16f));
                slot.rectTransform.anchorMin = slot.rectTransform.anchorMax = slot.rectTransform.pivot = new Vector2(0f, .5f);
                slot.rectTransform.anchoredPosition = new Vector2(55f + c * 14f, 0f);
                var fill = Img(slot.rectTransform, "Fill", cellSprite, VitalColors[v], Vector2.zero);
                UIBuild.Stretch(fill.rectTransform);
                fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
                vitalCells[v][c] = fill;
            }
        }
        UpdateStatusBars();
    }

    void UpdateStatusBars()
    {
        if (!playerStats) return;
        foodPoisonLabel.text = playerStats.IsFoodPoisoned
            ? "식중독 " + (playerStats.foodPoisonUntil - FoodShelfLife.Now.TotalMinutes) + "분 · 포만감 회복 불가" : "";
        SetCells(hearts, playerStats.health);
        SetCells(vitalCells[0], playerStats.fullness);
        SetCells(vitalCells[1], playerStats.stamina);
        SetCells(vitalCells[2], playerStats.stress);
    }

    // value is 0..PlayerStats.Max; each of the ten cells holds 2 HUD points and fills in halves.
    static void SetCells(Image[] cells, float value)
    {
        float points = float.IsNaN(value) ? 0f : Mathf.Clamp(value / PlayerStats.Point, 0f, PlayerStats.Points);
        int halves = Mathf.CeilToInt(points - .001f); // a sliver left still shows half a cell
        for (int i = 0; i < cells.Length; i++)
        {
            if (!cells[i]) continue;
            float amount = Mathf.Clamp(halves - i * 2, 0, 2) * .5f;
            if (!Mathf.Approximately(cells[i].fillAmount, amount)) cells[i].fillAmount = amount;
        }
    }

    Image Img(Transform parent, string name, Sprite sprite, Color color, Vector2 size)
    {
        var rect = UIBuild.Rect(name, parent, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite; image.color = color; image.raycastTarget = false; image.preserveAspect = false;
        return image;
    }

    // The heart from the reference: an octagonal heart with a bevelled grey frame and a hollow middle.
    static readonly Vector2[] HeartOuter = { new Vector2(270, 215), new Vector2(515, 215), new Vector2(652, 340), new Vector2(800, 215),
        new Vector2(1045, 215), new Vector2(1220, 380), new Vector2(1220, 665), new Vector2(650, 1170), new Vector2(85, 665), new Vector2(85, 380) };
    static readonly Vector2[] HeartInner = { new Vector2(305, 370), new Vector2(480, 370), new Vector2(652, 510), new Vector2(830, 370),
        new Vector2(1008, 370), new Vector2(1080, 435), new Vector2(1080, 625), new Vector2(650, 1015), new Vector2(230, 625), new Vector2(230, 435) };

    static void MakeVitalSprites()
    {
        if (heartFrame) return;
        const int W = 64, H = 58;
        var frame = new Texture2D(W, H, TextureFormat.RGBA32, false); var fill = new Texture2D(W, H, TextureFormat.RGBA32, false);
        Color outline = UITheme.Brown, light = UITheme.Ivory, shade = UITheme.Slate, clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            // Texture rows go bottom-up; the reference is 1300 x 1200 top-down with the heart at x 85-1220, y 215-1170.
            var p = new Vector2(85f + (x + .5f) / W * 1135f, 1170f - (y + .5f) / H * 955f);
            bool outer = Inside(p, HeartOuter), inner = Inside(p, HeartInner);
            bool edge = outer && (!Inside(p + new Vector2(28, 0), HeartOuter) || !Inside(p - new Vector2(28, 0), HeartOuter) ||
                !Inside(p + new Vector2(0, 28), HeartOuter) || !Inside(p - new Vector2(0, 28), HeartOuter) ||
                (!inner && (Inside(p + new Vector2(24, 0), HeartInner) || Inside(p - new Vector2(24, 0), HeartInner) ||
                Inside(p + new Vector2(0, 24), HeartInner) || Inside(p - new Vector2(0, 24), HeartInner))));
            // Bevel: the upper-left half of the band catches the light, the lower-right half is in shade.
            float lit = (p.x - 650f) * .4f + (p.y - 690f);
            frame.SetPixel(x, y, !outer ? clear : inner ? clear : edge ? outline : lit < 0f ? light : shade);
            // The filled heart (second reference): the whole shape in red, light bevel on top, dark bevel below, a gloss
            // stripe at the upper left and a dark red outline. Its colours are baked, so the Image stays white.
            Color red = HeartRed, top = Color.Lerp(red, Color.white, .38f), low = Color.Lerp(red, Color.black, .32f),
                rim = Color.Lerp(red, Color.black, .45f);
            bool gloss = p.y > 370f && p.y < 515f && p.x > 238f && p.x < 420f && (p.x - 238f) + (p.y - 370f) < 180f && (p.x - 238f) + (p.y - 370f) > 0f &&
                p.x + p.y > 600f && p.x + p.y < 790f;
            fill.SetPixel(x, y, !outer ? clear : edge && !inner ? rim : inner ? (gloss ? top : red) : lit < 0f ? top : low);
        }
        frame.Apply(); fill.Apply();
        frame.filterMode = fill.filterMode = FilterMode.Bilinear;
        heartFrame = Sprite.Create(frame, new Rect(0, 0, W, H), new Vector2(.5f, .5f), 100f);
        heartFill = Sprite.Create(fill, new Rect(0, 0, W, H), new Vector2(.5f, .5f), 100f);
        // Cells: slightly rounded squares (corner pixels cut), matching the panels' soft corners.
        const int CW = 12, CH = 16;
        var cell = new Texture2D(CW, CH, TextureFormat.RGBA32, false);
        for (int y = 0; y < CH; y++) for (int x = 0; x < CW; x++)
        {
            int dx = Mathf.Min(x, CW - 1 - x), dy = Mathf.Min(y, CH - 1 - y);
            cell.SetPixel(x, y, dx + dy < 2 ? clear : Color.white);
        }
        cell.Apply(); cell.filterMode = FilterMode.Bilinear;
        cellSprite = Sprite.Create(cell, new Rect(0, 0, CW, CH), new Vector2(.5f, .5f), 100f);
    }

    static bool Inside(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
        return inside;
    }
}
