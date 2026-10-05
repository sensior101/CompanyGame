using System.Collections.Generic;
using UnityEngine;

/// <summary>Rounded translucent speech panel with a downward tail, sized by its RectTransform.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DialogueBubbleGraphic : UnityEngine.UI.MaskableGraphic
{
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper helper)
    {
        helper.Clear();
        Rect bounds = rectTransform.rect;
        var body = new Rect(bounds.x, bounds.y + 14f, bounds.width, bounds.height - 14f);
        Color outline = new Color(.10f, .12f, .13f, .88f);
        Triangle(helper, new Vector2(-17f, body.y + 3f), new Vector2(0f, bounds.y), new Vector2(17f, body.y + 3f), outline);
        Rounded(helper, body, 20f, outline);
        Rect inside = new Rect(body.x + 2.5f, body.y + 2.5f, body.width - 5f, body.height - 5f);
        Rounded(helper, inside, 18f, color);
        Triangle(helper, new Vector2(-12f, inside.y + 2f), new Vector2(0f, bounds.y + 4f), new Vector2(12f, inside.y + 2f), color);
    }

    static void Triangle(UnityEngine.UI.VertexHelper helper, Vector2 a, Vector2 b, Vector2 c, Color tint)
    {
        int start = helper.currentVertCount;
        helper.AddVert(a, tint, Vector2.zero); helper.AddVert(b, tint, Vector2.zero); helper.AddVert(c, tint, Vector2.zero);
        helper.AddTriangle(start, start + 1, start + 2);
    }

    static void Rounded(UnityEngine.UI.VertexHelper helper, Rect rect, float radius, Color tint)
    {
        radius = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * .5f);
        var points = new List<Vector2>();
        var centers = new[] { new Vector2(rect.xMax-radius,rect.yMax-radius), new Vector2(rect.xMin+radius,rect.yMax-radius),
            new Vector2(rect.xMin+radius,rect.yMin+radius), new Vector2(rect.xMax-radius,rect.yMin+radius) };
        for (int corner = 0; corner < 4; corner++)
            for (int step = 0; step <= 8; step++)
            {
                float angle = (corner * 90f + step * 90f / 8f) * Mathf.Deg2Rad;
                points.Add(centers[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        int start = helper.currentVertCount;
        helper.AddVert(rect.center, tint, Vector2.zero);
        foreach (var point in points) helper.AddVert(point, tint, Vector2.zero);
        for (int i = 0; i < points.Count; i++) helper.AddTriangle(start, start+1+i, start+1+(i+1)%points.Count);
    }
}
