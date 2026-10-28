using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Signalled arms whose map has no painted zebra get one drawn here that looks like the painted ones: the same bar mesh and
/// material copied out of StreetMarkings, the same bar spacing, bars parallel to the road across the full road width, the same
/// height above the road, placed before the stop line like the painted ones. The crossing slides along the arm until it
/// is clear of other roads, markings and crossings and its sidewalk ends are really outside the road.
/// </summary>
public static class DrawnCrosswalks
{
    public const string BarName = "Bar";
    const string MeshPath = "Assets/Art/Traffic/Meshes/ZebraBar.asset";
    const float SlideIn = 4f, SlideOut = 10f, Step = .5f, Margin = .05f;

    sealed class Style
    {
        public float alongOffset, edgeMargin, heightOffset, pitch;     // medians over the painted crossings used by the map
        public Mesh mesh; public Material material; public float barLong, barShort;
    }

    public static int Draw(RoadGraph graph, RoadSurface surface, PaintedSet set, Dictionary<RoadArm, PaintedCrosswalk> matches,
        Transform parent, Dictionary<(RoadNode, int), TrafficSignal> signalOf, List<string> report)
    {
        Style style = Measure(graph, surface, set, matches);
        if (style == null) { report.Add("no painted bar to copy: nothing drawn"); return 0; }
        report.Add($"style: stop-line offset {style.alongOffset:F2} m, edge margin {style.edgeMargin:F2} m, bar height {style.heightOffset:F3} m above road, pitch {style.pitch:F2} m, bar {style.barShort:F2} x {style.barLong:F2} m, material {style.material.name}");
        var placed = new List<(Vector3 c, Vector3 u, Vector3 v, float hu, float hv)>();
        int drawn = 0;
        foreach (RoadNode node in graph.nodes)
        {
            if (!node.Signalled) continue;
            foreach (RoadArm arm in node.arms)
            {
                if (matches.ContainsKey(arm)) continue;
                string label = $"n{node.id} r{arm.road}{(arm.forward ? "f" : "b")} ({graph.roads[arm.road].name})";
                if (TryDraw(graph, surface, set, style, node, arm, parent, signalOf[(node, arm.group)], placed, matches, out string why)) { drawn++; report.Add("drawn " + why + " " + label); }
                else report.Add("SKIPPED " + label + ": " + why);
            }
        }
        return drawn;
    }

    // ---- what the painted ones look like ---------------------------------------------------------------------------

    static Style Measure(RoadGraph graph, RoadSurface surface, PaintedSet set, Dictionary<RoadArm, PaintedCrosswalk> matches)
    {
        if (!set.barMesh || !set.barMaterial) return null;
        var offsets = new List<float>(); var margins = new List<float>(); var heights = new List<float>();
        foreach (KeyValuePair<RoadArm, PaintedCrosswalk> pair in matches)
        {
            RoadArm arm = pair.Key; PaintedCrosswalk p = pair.Value;
            Vector3 rel = RoadPolyline.Flat(p.centre - arm.node.position);
            offsets.Add(Vector3.Dot(rel, arm.dir) - arm.node.clearance);
            float width = TrafficMapBuilder.EdgeDistance(surface, p.centre, -p.walk) + TrafficMapBuilder.EdgeDistance(surface, p.centre, p.walk);
            margins.Add((width - p.span) * .5f);
            RoadPolyline line = graph.lines[arm.road];
            heights.Add(p.yTop - line.PointAt(line.Project(p.centre, out _)).y);
        }
        var pitches = matches.Values.Select(p => p.pitch).ToList();
        Mesh asset = SaveMesh(set.barMesh);
        Bounds b = asset.bounds;
        return new Style
        {
            alongOffset = Median(offsets, -3f), edgeMargin = Mathf.Max(.2f, Median(margins, .5f)), heightOffset = Median(heights, .02f), pitch = Median(pitches, 1.4f),
            mesh = asset, material = set.barMaterial, barLong = b.size.z, barShort = b.size.x,
        };
    }

    static float Median(List<float> values, float fallback)
    {
        if (values.Count == 0) return fallback;
        values.Sort();
        return values[values.Count / 2];
    }

    static Mesh SaveMesh(Mesh mesh)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art/Traffic")) AssetDatabase.CreateFolder("Assets/Art", "Traffic");
        if (!AssetDatabase.IsValidFolder("Assets/Art/Traffic/Meshes")) AssetDatabase.CreateFolder("Assets/Art/Traffic", "Meshes");
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (!existing) { AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(mesh), MeshPath); existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath); }
        else { existing.Clear(); EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); }
        return existing;
    }

    // ---- placement ----------------------------------------------------------------------------------------------------

    sealed class Layout
    {
        public Vector3 centre, along, across; public float width; public int bars;
    }

    static bool TryDraw(RoadGraph graph, RoadSurface surface, PaintedSet set, Style style, RoadNode node, RoadArm arm, Transform parent,
        TrafficSignal signal, List<(Vector3 c, Vector3 u, Vector3 v, float hu, float hv)> placed, Dictionary<RoadArm, PaintedCrosswalk> matches, out string why)
    {
        var candidates = new List<float>();
        for (float o = -SlideIn; o <= SlideOut; o += Step) candidates.Add(o);
        candidates.Sort((x, y) => Mathf.Abs(x).CompareTo(Mathf.Abs(y)));
        var reasons = new SortedDictionary<string, int>();
        foreach (float o in candidates)
        {
            Layout layout = Fit(graph, surface, set, style, arm, node.clearance + style.alongOffset + o, placed, out string reason);
            if (layout == null) { reasons[reason] = reasons.TryGetValue(reason, out int n) ? n + 1 : 1; continue; }
            Build(graph, style, node, arm, parent, signal, layout, placed);
            var stop = new PaintedCrosswalk { centre = layout.centre, depth = style.barLong };
            float pulled = TrafficMapBuilder.FitStopLine(arm, stop);
            why = $"{layout.bars} bars, {layout.width:F1} m wide, shift {o:+0.0;-0.0;0.0} m (nominal blocked by: {string.Join(", ", reasons.Keys)}), stop line pulled {pulled:F1} m";
            return true;
        }
        why = "no clear spot within " + SlideIn + " m in / " + SlideOut + " m out: " + string.Join(", ", reasons.Select(r => r.Key + " x" + r.Value));
        return false;
    }

    static Layout Fit(RoadGraph graph, RoadSurface surface, PaintedSet set, Style style, RoadArm arm, float alongDist,
        List<(Vector3 c, Vector3 u, Vector3 v, float hu, float hv)> placed, out string reason)
    {
        reason = "";
        RoadPolyline line = graph.lines[arm.road];
        float sign = arm.forward ? 1f : -1f, s = arm.s + sign * alongDist;
        if (s < 0f || s > line.Length) { reason = "off the road end"; return null; }
        Vector3 centre = line.PointAt(s), along = RoadPolyline.Flat(line.TangentAt(s)).normalized * sign;
        Vector3 across = new Vector3(-along.z, 0f, along.x);
        float dA = TrafficMapBuilder.EdgeDistance(surface, centre, -across), dB = TrafficMapBuilder.EdgeDistance(surface, centre, across), width = dA + dB;
        if (Mathf.Abs(width - arm.width) > 1.5f) { reason = "road not a plain strip here"; return null; }
        centre += across * ((dB - dA) * .5f);
        Vector3 a = centre - across * (width * .5f + 1f), b = centre + across * (width * .5f + 1f);
        if (surface.Contains(a, 0f, 1f) || surface.Contains(b, 0f, 1f)) { reason = "sidewalk end lands on a road"; return null; }
        int bars = Mathf.FloorToInt((width - 2f * style.edgeMargin - style.barShort) / style.pitch + .001f) + 1;
        var layout = new Layout { centre = centre, along = along, across = across, width = width, bars = bars };
        reason = Blocked(graph, set, style, arm, layout, placed);
        return reason == null ? layout : null;
    }

    // null when the crossing's bars are clear; otherwise the reason.
    static string Blocked(RoadGraph graph, PaintedSet set, Style style, RoadArm arm, Layout l,
        List<(Vector3 c, Vector3 u, Vector3 v, float hu, float hv)> placed)
    {
        float hv = style.barLong * .5f + Margin, hu = style.barShort * .5f + Margin;
        for (int k = 0; k < l.bars; k++)
        {
            Vector3 c = l.centre + l.across * ((k - (l.bars - 1) * .5f) * style.pitch);
            if (set.others.Overlaps(c, l.across, l.along, hu, hv)) return "overlaps road markings";
            foreach (Vector3 corner in Corners(c, l.across, l.along, hu - Margin, hv - Margin))
                if (OnOtherRoad(graph, arm.road, corner)) return "reaches another road";
        }
        float spanHalf = (l.bars - 1) * style.pitch * .5f + style.barShort * .5f;
        foreach (PaintedCrosswalk p in set.crosswalks)
            if (RectsOverlap(l.centre, l.across, l.along, spanHalf, hv, p.centre, p.walk, p.along, p.span * .5f, p.depth * .5f)) return "overlaps a painted crossing";
        foreach (var o in placed)
            if (RectsOverlap(l.centre, l.across, l.along, spanHalf, hv, o.c, o.u, o.v, o.hu, o.hv)) return "overlaps another drawn crossing";
        return null;
    }

    static IEnumerable<Vector3> Corners(Vector3 c, Vector3 u, Vector3 v, float hu, float hv)
    {
        yield return c + u * hu + v * hv; yield return c + u * hu - v * hv; yield return c - u * hu + v * hv; yield return c - u * hu - v * hv;
    }

    static bool OnOtherRoad(RoadGraph graph, int own, Vector3 p)
    {
        for (int j = 0; j < graph.lines.Length; j++)
        {
            if (j == own) continue;
            graph.lines[j].Project(p, out float distance);
            if (distance < graph.roads[j].width * .5f + .2f) return true;
        }
        return false;
    }

    // Two rectangles in XZ (separating-axis test on both rectangles' axes).
    static bool RectsOverlap(Vector3 c1, Vector3 u1, Vector3 v1, float hu1, float hv1, Vector3 c2, Vector3 u2, Vector3 v2, float hu2, float hv2)
    {
        foreach (Vector3 axis in new[] { u1, v1, u2, v2 })
        {
            float d = Mathf.Abs(Vector3.Dot(c2 - c1, axis));
            float r1 = hu1 * Mathf.Abs(Vector3.Dot(u1, axis)) + hv1 * Mathf.Abs(Vector3.Dot(v1, axis));
            float r2 = hu2 * Mathf.Abs(Vector3.Dot(u2, axis)) + hv2 * Mathf.Abs(Vector3.Dot(v2, axis));
            if (d > r1 + r2) return false;
        }
        return true;
    }

    // ---- scene objects ------------------------------------------------------------------------------------------------

    static void Build(RoadGraph graph, Style style, RoadNode node, RoadArm arm, Transform parent, TrafficSignal signal, Layout l,
        List<(Vector3 c, Vector3 u, Vector3 v, float hu, float hv)> placed)
    {
        RoadPolyline line = graph.lines[arm.road];
        Vector3 centre = l.centre;
        centre.y = line.PointAt(line.Project(centre, out _)).y;
        Vector3 a = centre - l.across * (l.width * .5f + 1f), b = centre + l.across * (l.width * .5f + 1f);
        Crosswalk walk = TrafficMapBuilder.NewCrosswalk(parent, node, arm, centre, l.along, l.across, a, b, signal);
        var holder = new GameObject("Bars").transform;
        holder.SetParent(walk.transform, false);
        for (int k = 0; k < l.bars; k++)
        {
            Vector3 p = l.centre + l.across * ((k - (l.bars - 1) * .5f) * style.pitch);
            p.y = line.PointAt(line.Project(p, out _)).y + style.heightOffset;
            var go = new GameObject(BarName + k);
            go.transform.SetParent(holder, false);
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(l.along));
            go.AddComponent<MeshFilter>().sharedMesh = style.mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = style.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        placed.Add((l.centre, l.across, l.along, (l.bars - 1) * style.pitch * .5f + style.barShort * .5f, style.barLong * .5f + Margin));
    }
}
