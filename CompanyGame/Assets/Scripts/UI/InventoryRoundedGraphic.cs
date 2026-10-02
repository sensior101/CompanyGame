using System.Collections.Generic;
using UnityEngine;

/// <summary>Native vector panel: top-left and bottom-right are the softer diagonal.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class InventoryRoundedGraphic : UnityEngine.UI.MaskableGraphic
{
    public float largeRadius = 30f;
    public float smallRadius = 11f;
    public float borderWidth = 1.5f;
    public Color borderColor = new Color(1f, 1f, 1f, .9f);
    readonly List<Vector2> outer = new List<Vector2>(52);
    readonly List<Vector2> inner = new List<Vector2>(52);

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float limit = Mathf.Min(rect.width, rect.height) * .5f;
        float large = Mathf.Min(largeRadius, limit), small = Mathf.Min(smallRadius, limit);
        float border = Mathf.Min(borderWidth, limit);
        Outline(rect, large, small, outer);
        var inset = new Rect(rect.xMin + border, rect.yMin + border,
            Mathf.Max(0f, rect.width - border * 2f), Mathf.Max(0f, rect.height - border * 2f));
        Outline(inset, Mathf.Max(0f, large - border), Mathf.Max(0f, small - border), inner);
        mesh.AddVert(rect.center, color, Vector2.zero);
        for (int i = 0; i < inner.Count; i++) mesh.AddVert(inner[i], color, Vector2.zero);
        for (int i = 0; i < inner.Count; i++) mesh.AddTriangle(0, 1 + i, 1 + (i + 1) % inner.Count);
        int start = mesh.currentVertCount;
        Color edge = borderColor;
        edge.a *= color.a;
        for (int i = 0; i < outer.Count; i++)
        {
            mesh.AddVert(outer[i], edge, Vector2.zero);
            mesh.AddVert(inner[i], edge, Vector2.zero);
        }
        for (int i = 0; i < outer.Count; i++)
        {
            int a = start + i * 2, b = start + ((i + 1) % outer.Count) * 2;
            mesh.AddTriangle(a, b, a + 1);
            mesh.AddTriangle(b, b + 1, a + 1);
        }
    }

    static void Outline(Rect rect, float large, float small, List<Vector2> points)
    {
        points.Clear();
        Arc(points, new Vector2(rect.xMax - small, rect.yMax - small), small, 0f);
        Arc(points, new Vector2(rect.xMin + large, rect.yMax - large), large, 90f);
        Arc(points, new Vector2(rect.xMin + small, rect.yMin + small), small, 180f);
        Arc(points, new Vector2(rect.xMax - large, rect.yMin + large), large, 270f);
    }

    static void Arc(List<Vector2> points, Vector2 centre, float radius, float start)
    {
        const int segments = 12;
        for (int i = 0; i <= segments; i++)
        {
            float angle = (start + i * 90f / segments) * Mathf.Deg2Rad;
            points.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
    }
}
