using UnityEngine;

/// <summary>A small cream hand with a brown outline, restored to the default cursor on close.</summary>
[DisallowMultipleComponent]
public sealed class InventoryHandCursor : MonoBehaviour
{
    Texture2D pointing;
    Texture2D holding;
    bool shown;
    public bool IsVisible => shown;

    public void Show()
    {
        if (!pointing) pointing = DrawHand(false);
        if (!holding) holding = DrawHand(true);
        shown = true;
        SetDragging(false);
    }
    public void SetDragging(bool value)
    {
        if (!shown) return;
        Cursor.SetCursor(value ? holding : pointing, new Vector2(19f, 5f), CursorMode.Auto);
    }
    public void Hide()
    {
        if (!shown) return;
        shown = false;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    static Texture2D DrawHand(bool closed)
    {
        const int size = 48, supersampling = 4;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = closed ? "InventoryHoldingHand" : "InventoryPointingHand",
            filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
        };
        // Coordinates use screen convention, with y downward from the fingertip.
        Vector2[] polygon = closed ? new[] {
            new Vector2(14,34),new Vector2(8,26),new Vector2(8,20),new Vector2(12,18),new Vector2(16,21),
            new Vector2(16,14),new Vector2(19,11),new Vector2(23,12),new Vector2(25,15),new Vector2(28,13),
            new Vector2(32,15),new Vector2(34,17),new Vector2(38,17),new Vector2(40,21),new Vector2(40,30),
            new Vector2(35,38),new Vector2(34,42),new Vector2(17,42)
        } : new[] {
            new Vector2(15,34),new Vector2(8,25),new Vector2(8,21),new Vector2(11,19),new Vector2(14,20),
            new Vector2(17,24),new Vector2(16,9),new Vector2(17,5),new Vector2(20,4),new Vector2(23,6),
            new Vector2(24,18),new Vector2(27,15),new Vector2(31,16),new Vector2(32,19),new Vector2(35,18),
            new Vector2(38,20),new Vector2(38,23),new Vector2(41,23),new Vector2(42,27),new Vector2(39,34),
            new Vector2(35,38),new Vector2(34,42),new Vector2(18,42)
        };
        var pixels = new Color[size * size];
        var fill = new Color(.99f, .96f, .87f, 1f);
        var outline = new Color(.35f, .23f, .16f, 1f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Color sum = Color.clear;
            int covered = 0;
            for (int sy = 0; sy < supersampling; sy++)
            for (int sx = 0; sx < supersampling; sx++)
            {
                var p = new Vector2(x + (sx + .5f) / supersampling, y + (sy + .5f) / supersampling);
                if (!Inside(p, polygon)) continue;
                float edge = float.PositiveInfinity;
                for (int i = 0; i < polygon.Length; i++)
                    edge = Mathf.Min(edge, DistanceToSegment(p, polygon[i], polygon[(i + 1) % polygon.Length]));
                sum += edge < 1.6f ? outline : fill;
                covered++;
            }
            if (covered > 0)
            {
                var color = sum / covered;
                color.a = (float)covered / (supersampling * supersampling);
                pixels[(size - 1 - y) * size + x] = color;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    static bool Inside(Vector2 p, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x) inside = !inside;
        return inside;
    }
    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var line = b - a;
        return Vector2.Distance(p, a + line * Mathf.Clamp01(Vector2.Dot(p - a, line) / line.sqrMagnitude));
    }
    void OnDisable() { Hide(); }
    void OnDestroy()
    {
        Hide();
        if (pointing) Destroy(pointing);
        if (holding) Destroy(holding);
    }
}
