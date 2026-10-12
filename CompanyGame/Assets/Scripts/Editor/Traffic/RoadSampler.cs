using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>Centre line of one road piece: world points on the road top and the measured road width.</summary>
public sealed class RoadCenterline
{
    public string name;
    public float width;
    public List<Vector3> points = new List<Vector3>();
}

/// <summary>
/// Road triangles in world space. Roads here have no colliders, so "is this point on the road" is answered
/// from the mesh itself (horizontal point-in-triangle plus a height check).
/// </summary>
public sealed class RoadSurface
{
    readonly List<Vector3[]> triangles = new List<Vector3[]>();

    public int TriangleCount => triangles.Count;

    public void Add(Mesh mesh, Matrix4x4 toWorld) => Add(mesh.vertices, mesh.triangles, toWorld);

    public void Add(Vector3[] v, int[] t, Matrix4x4 toWorld)
    {
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            var tri = new[] { toWorld.MultiplyPoint3x4(v[t[i]]), toWorld.MultiplyPoint3x4(v[t[i + 1]]), toWorld.MultiplyPoint3x4(v[t[i + 2]]) };
            if (Mathf.Abs(Area(tri[0], tri[1], tri[2])) > .001f) triangles.Add(tri);   // walls of a cube are edge-on from above
        }
    }

    public bool Contains(Vector3 p, float slack = .05f, float maxDy = .6f)
    {
        foreach (Vector3[] tri in triangles)
        {
            if (Mathf.Abs(Mathf.Max(tri[0].y, tri[1].y, tri[2].y) - p.y) > maxDy && Mathf.Abs(Mathf.Min(tri[0].y, tri[1].y, tri[2].y) - p.y) > maxDy) continue;
            if (InsideXZ(tri, p, slack)) return true;
        }
        return false;
    }

    /// <summary>True when any triangle overlaps the XZ rectangle (centre c, unit axes u and v, half extents hu and hv).</summary>
    public bool Overlaps(Vector3 c, Vector3 u, Vector3 v, float hu, float hv)
    {
        var q = new Vector2[3];
        foreach (Vector3[] tri in triangles)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            for (int i = 0; i < 3; i++)
            {
                Vector3 d = tri[i] - c;
                q[i] = new Vector2(d.x * u.x + d.z * u.z, d.x * v.x + d.z * v.z);
                x0 = Mathf.Min(x0, q[i].x); x1 = Mathf.Max(x1, q[i].x); y0 = Mathf.Min(y0, q[i].y); y1 = Mathf.Max(y1, q[i].y);
            }
            if (x1 < -hu || x0 > hu || y1 < -hv || y0 > hv) continue;
            if (!SeparatedByEdge(q, hu, hv)) return true;
        }
        return false;
    }

    static bool SeparatedByEdge(Vector2[] q, float hu, float hv)
    {
        for (int i = 0; i < 3; i++)
        {
            Vector2 e = q[(i + 1) % 3] - q[i], n = new Vector2(-e.y, e.x);
            float third = Vector2.Dot(q[(i + 2) % 3] - q[i], n);
            if (Mathf.Abs(third) < 1e-9f) continue;
            bool outside = true;
            for (int k = 0; k < 4 && outside; k++)
            {
                Vector2 corner = new Vector2((k & 1) == 0 ? -hu : hu, (k & 2) == 0 ? -hv : hv);
                float d = Vector2.Dot(corner - q[i], n);
                if (third > 0f ? d > 0f : d < 0f) outside = false;
            }
            if (outside) return true;
        }
        return false;
    }

    static float Area(Vector3 a, Vector3 b, Vector3 c) => (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);

    static bool InsideXZ(Vector3[] t, Vector3 p, float slack)
    {
        float d1 = Area(t[0], t[1], p), d2 = Area(t[1], t[2], p), d3 = Area(t[2], t[0], p);
        bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
        if (!(negative && positive)) return true;
        return EdgeDistance(t[0], t[1], p) <= slack || EdgeDistance(t[1], t[2], p) <= slack || EdgeDistance(t[2], t[0], p) <= slack;
    }

    static float EdgeDistance(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ap = new Vector2(p.x - a.x, p.z - a.z);
        float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return (ap - ab * t).magnitude;
    }
}

/// <summary>
/// Finds road centre lines by cutting the road mesh across its direction. A straight Cube road and a curved
/// arc go through the same steps: take the connected piece, follow its shortest path end to end, then repeatedly
/// slice perpendicular to that path and move each point to the middle of its slice.
/// </summary>
public static class RoadSampler
{
    static readonly Regex RoadName = new Regex(@"^(Street_\d+|PerimeterRoad.*|BridgeAsphalt)$");
    const int Passes = 30;
    const float EndExtendStep = 1f;

    public static bool IsRoadName(string objectName) => RoadName.IsMatch(objectName);

    /// <summary>One centre line per connected piece of the mesh (the perimeter mesh has two arcs).</summary>
    public static List<RoadCenterline> Sample(Mesh mesh, Matrix4x4 toWorld, string name, float step = 4f) =>
        Sample(mesh.vertices, mesh.triangles, toWorld, name, step);

    public static List<RoadCenterline> Sample(Vector3[] vertices, int[] triangles, Matrix4x4 toWorld, string name, float step = 4f)
    {
        var result = new List<RoadCenterline>();
        var planar = new Planar(vertices, triangles, toWorld);
        foreach (List<int> piece in planar.Pieces())
        {
            RoadCenterline line = Trace(planar, piece, step);
            if (line != null) { line.name = name; result.Add(line); }
        }
        return result;
    }

    static RoadCenterline Trace(Planar planar, List<int> piece, float step)
    {
        List<Vector2> path = planar.EndToEndPath(piece);
        if (path == null || path.Count < 2) return null;
        List<Vector2> pts = Resample(path, step);
        for (int pass = 0; pass < Passes; pass++) pts = Resample(Recentre(planar, pts), step);
        float width = MedianWidth(planar, pts);
        if (width < .5f) return null;
        pts = TrimEnds(pts, Mathf.CeilToInt(width / step) + 1);
        if (pts.Count < 2) return null;
        ExtendEnd(planar, pts, width, true);
        ExtendEnd(planar, pts, width, false);
        float y = planar.TopY(piece);
        var line = new RoadCenterline { width = width };
        foreach (Vector2 p in Simplify(pts, .3f)) line.points.Add(new Vector3(p.x, y, p.y));
        return line;
    }

    // ---- slicing ------------------------------------------------------------------------------------------

    static List<Vector2> Recentre(Planar planar, List<Vector2> pts)
    {
        var moved = new List<Vector2>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 tangent = (pts[Mathf.Min(pts.Count - 1, i + 3)] - pts[Mathf.Max(0, i - 3)]).normalized;
            if (planar.Slice(pts[i], tangent, out float lo, out float hi))
                moved.Add(pts[i] + Normal(tangent) * Mathf.Clamp((lo + hi) * .5f, -6f, 6f));
            else moved.Add(pts[i]);
        }
        return moved;
    }

    static Vector2 Normal(Vector2 tangent) => new Vector2(-tangent.y, tangent.x);

    static float MedianWidth(Planar planar, List<Vector2> pts)
    {
        var widths = new List<float>();
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 tangent = (pts[Mathf.Min(pts.Count - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
            if (planar.Slice(pts[i], tangent, out float lo, out float hi)) widths.Add(hi - lo);
        }
        if (widths.Count == 0) return 0f;
        widths.Sort();
        return widths[widths.Count / 2];
    }

    // The first and last few points sit in the tapered end of the path and are unreliable; probing replaces them.
    static List<Vector2> TrimEnds(List<Vector2> pts, int count)
    {
        if (pts.Count <= count * 2 + 1) return new List<Vector2>();
        return pts.GetRange(count, pts.Count - count * 2);
    }

    static void ExtendEnd(Planar planar, List<Vector2> pts, float width, bool atStart)
    {
        for (int i = 0; i < 60; i++)
        {
            int n = pts.Count;
            Vector2 tip = atStart ? pts[0] : pts[n - 1];
            Vector2 back = atStart ? pts[Mathf.Min(3, n - 1)] : pts[Mathf.Max(0, n - 4)];
            Vector2 tangent = (tip - back).normalized;
            Vector2 probe = tip + tangent * EndExtendStep;
            if (!planar.Slice(probe, tangent, out float lo, out float hi) || hi - lo < width * .93f) return;
            float mid = (lo + hi) * .5f;
            if (Mathf.Abs(mid) > width * .25f) return;
            probe += Normal(tangent) * mid;
            if (atStart) pts.Insert(0, probe); else pts.Add(probe);
        }
    }

    // ---- polyline helpers ----------------------------------------------------------------------------------

    static List<Vector2> Resample(List<Vector2> pts, float step)
    {
        var length = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++) length[i] = length[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
        float total = length[pts.Count - 1];
        int count = Mathf.Max(2, Mathf.RoundToInt(total / step) + 1);
        var result = new List<Vector2>(count);
        int seg = 1;
        for (int k = 0; k < count; k++)
        {
            float s = total * k / (count - 1);
            while (seg < pts.Count - 1 && length[seg] < s) seg++;
            float span = Mathf.Max(1e-6f, length[seg] - length[seg - 1]);
            result.Add(Vector2.Lerp(pts[seg - 1], pts[seg], Mathf.Clamp01((s - length[seg - 1]) / span)));
        }
        return result;
    }

    /// <summary>Douglas-Peucker: drops points closer than tolerance to the line between the kept neighbours.</summary>
    public static List<Vector2> Simplify(List<Vector2> pts, float tolerance)
    {
        var keep = new bool[pts.Count];
        keep[0] = keep[pts.Count - 1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            (int a, int b) = stack.Pop();
            int worst = -1;
            float worstDistance = tolerance;
            for (int i = a + 1; i < b; i++)
            {
                float d = DistanceToSegment(pts[i], pts[a], pts[b]);
                if (d > worstDistance) { worstDistance = d; worst = i; }
            }
            if (worst < 0) continue;
            keep[worst] = true;
            stack.Push((a, worst));
            stack.Push((worst, b));
        }
        var result = new List<Vector2>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) result.Add(pts[i]);
        return result;
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }

    // ---- the mesh seen from above -----------------------------------------------------------------------------

    sealed class Planar
    {
        readonly List<Vector2> pos = new List<Vector2>();
        readonly List<float> height = new List<float>();
        readonly List<int[]> tris = new List<int[]>();
        readonly List<HashSet<int>> adjacent = new List<HashSet<int>>();

        public Planar(Vector3[] v, int[] t, Matrix4x4 toWorld)
        {
            var weld = new Dictionary<(int, int), int>();
            var map = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(v[i]);
                var key = (Mathf.RoundToInt(w.x * 20f), Mathf.RoundToInt(w.z * 20f));
                if (!weld.TryGetValue(key, out int id))
                {
                    id = pos.Count;
                    weld[key] = id;
                    pos.Add(new Vector2(w.x, w.z));
                    height.Add(w.y);
                    adjacent.Add(new HashSet<int>());
                }
                height[id] = Mathf.Max(height[id], w.y);
                map[i] = id;
            }
            for (int i = 0; i + 2 < t.Length; i += 3) AddTriangle(map[t[i]], map[t[i + 1]], map[t[i + 2]]);
        }

        void AddTriangle(int a, int b, int c)
        {
            if (a == b || b == c || a == c) return;
            float area = (pos[b].x - pos[a].x) * (pos[c].y - pos[a].y) - (pos[b].y - pos[a].y) * (pos[c].x - pos[a].x);
            if (Mathf.Abs(area) < 1e-3f) return;     // edge-on wall of a cube
            tris.Add(new[] { a, b, c });
            Link(a, b); Link(b, c); Link(c, a);
        }

        void Link(int a, int b) { adjacent[a].Add(b); adjacent[b].Add(a); }

        public float TopY(List<int> piece)
        {
            float top = float.MinValue;
            foreach (int i in piece) top = Mathf.Max(top, height[i]);
            return top;
        }

        public List<List<int>> Pieces()
        {
            var pieces = new List<List<int>>();
            var seen = new bool[pos.Count];
            for (int start = 0; start < pos.Count; start++)
            {
                if (seen[start] || adjacent[start].Count == 0) continue;
                var piece = new List<int> { start };
                seen[start] = true;
                for (int i = 0; i < piece.Count; i++)
                    foreach (int next in adjacent[piece[i]])
                        if (!seen[next]) { seen[next] = true; piece.Add(next); }
                pieces.Add(piece);
            }
            return pieces;
        }

        /// <summary>Shortest path along mesh edges between the two vertices that are farthest apart on the piece.</summary>
        public List<Vector2> EndToEndPath(List<int> piece)
        {
            Vector2 centre = Vector2.zero;
            foreach (int i in piece) centre += pos[i];
            centre /= piece.Count;
            int from = Farthest(piece, v => Vector2.Distance(pos[v], centre));
            var distance = new Dictionary<int, float>();
            var previous = new Dictionary<int, int>();
            Dijkstra(piece, from, distance, previous);
            int to = Farthest(piece, v => distance[v]);
            var path = new List<Vector2>();
            for (int v = to; ; v = previous[v])
            {
                path.Add(pos[v]);
                if (v == from) break;
            }
            path.Reverse();
            return path;
        }

        static int Farthest(List<int> piece, Func<int, float> score)
        {
            int best = piece[0];
            float bestScore = float.MinValue;
            foreach (int v in piece)
            {
                float s = score(v);
                if (s > bestScore) { bestScore = s; best = v; }
            }
            return best;
        }

        // ponytail: O(V^2) Dijkstra, fine for road pieces of a few hundred vertices.
        void Dijkstra(List<int> piece, int source, Dictionary<int, float> distance, Dictionary<int, int> previous)
        {
            foreach (int v in piece) distance[v] = float.MaxValue;
            distance[source] = 0f;
            var open = new List<int>(piece);
            while (open.Count > 0)
            {
                int bestIndex = 0;
                for (int i = 1; i < open.Count; i++) if (distance[open[i]] < distance[open[bestIndex]]) bestIndex = i;
                int u = open[bestIndex];
                open.RemoveAt(bestIndex);
                foreach (int v in adjacent[u])
                {
                    float candidate = distance[u] + Vector2.Distance(pos[u], pos[v]);
                    if (candidate >= distance[v]) continue;
                    distance[v] = candidate;
                    previous[v] = u;
                }
            }
        }

        /// <summary>
        /// Cuts the mesh with the line through p perpendicular to tangent. The cut that contains p (or lies closest)
        /// is returned as lo..hi along the normal, relative to p.
        /// </summary>
        public bool Slice(Vector2 p, Vector2 tangent, out float lo, out float hi)
        {
            Vector2 normal = Normal(tangent);
            var spans = new List<Vector2>();
            foreach (int[] tri in tris)
            {
                if (!CutTriangle(tri, p, tangent, normal, out Vector2 span)) continue;
                spans.Add(span);
            }
            lo = hi = 0f;
            if (spans.Count == 0) return false;
            spans.Sort((a, b) => a.x.CompareTo(b.x));
            var merged = new List<Vector2>();
            foreach (Vector2 span in spans)
            {
                if (merged.Count > 0 && span.x <= merged[merged.Count - 1].y + .05f)
                    merged[merged.Count - 1] = new Vector2(merged[merged.Count - 1].x, Mathf.Max(merged[merged.Count - 1].y, span.y));
                else merged.Add(span);
            }
            Vector2 best = merged[0];
            float bestScore = float.MaxValue;
            foreach (Vector2 span in merged)
            {
                float score = span.x <= 0f && span.y >= 0f ? 0f : Mathf.Min(Mathf.Abs(span.x), Mathf.Abs(span.y));
                if (score < bestScore) { bestScore = score; best = span; }
            }
            lo = best.x;
            hi = best.y;
            return true;
        }

        bool CutTriangle(int[] tri, Vector2 p, Vector2 tangent, Vector2 normal, out Vector2 span)
        {
            span = Vector2.zero;
            float lowest = float.MaxValue, highest = float.MinValue;
            int hits = 0;
            for (int i = 0; i < 3; i++)
            {
                Vector2 a = pos[tri[i]] - p, b = pos[tri[(i + 1) % 3]] - p;
                float sa = Vector2.Dot(a, tangent), sb = Vector2.Dot(b, tangent);
                if ((sa <= 0f) == (sb <= 0f) && sa != 0f) continue;
                float f = sa == sb ? 0f : sa / (sa - sb);
                float u = Vector2.Dot(Vector2.Lerp(a, b, f), normal);
                lowest = Mathf.Min(lowest, u);
                highest = Mathf.Max(highest, u);
                hits++;
            }
            if (hits < 2) return false;
            span = new Vector2(lowest, highest);
            return true;
        }
    }
}

/// <summary>A centre line with distance along it (metres from the first point).</summary>
public sealed class RoadPolyline
{
    public readonly List<Vector3> points;
    readonly float[] along;

    public RoadPolyline(List<Vector3> pts)
    {
        points = pts;
        along = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++) along[i] = along[i - 1] + Flat(pts[i] - pts[i - 1]).magnitude;
    }

    public float Length => along[along.Length - 1];

    public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    public Vector3 PointAt(float s)
    {
        s = Mathf.Clamp(s, 0f, Length);
        int i = SegmentAt(s);
        float span = Mathf.Max(1e-5f, along[i + 1] - along[i]);
        return Vector3.Lerp(points[i], points[i + 1], (s - along[i]) / span);
    }

    public Vector3 TangentAt(float s)
    {
        int i = SegmentAt(Mathf.Clamp(s, 0f, Length));
        return Flat(points[i + 1] - points[i]).normalized;
    }

    int SegmentAt(float s)
    {
        int i = 0;
        while (i < points.Count - 2 && along[i + 1] < s) i++;
        return i;
    }

    /// <summary>Closest point on the line to p: its distance along the line and how far p is from it.</summary>
    public float Project(Vector3 p, out float distance)
    {
        float bestS = 0f;
        distance = float.MaxValue;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector3 a = points[i], ab = Flat(points[i + 1] - a);
            float t = Mathf.Clamp01(Vector3.Dot(Flat(p - a), ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            float d = Flat(p - (a + ab * t)).magnitude;
            if (d >= distance) continue;
            distance = d;
            bestS = along[i] + t * Mathf.Sqrt(ab.sqrMagnitude);
        }
        return bestS;
    }

    /// <summary>The line between two distances, including the original vertices in between (a to b may run backwards).</summary>
    public List<Vector3> Between(float a, float b)
    {
        var result = new List<Vector3> { PointAt(a) };
        float lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
        var inner = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
            if (along[i] > lo + .01f && along[i] < hi - .01f) inner.Add(points[i]);
        if (b < a) inner.Reverse();
        result.AddRange(inner);
        result.Add(PointAt(b));
        return result;
    }
}

public sealed class RoadNode
{
    public int id;
    public Vector3 position;
    public float clearance;                       // stop lines and exits sit this far from the centre
    public readonly List<RoadArm> arms = new List<RoadArm>();
    public bool Signalled => arms.Count >= 3;
}

/// <summary>One road leaving a node in one direction.</summary>
public sealed class RoadArm
{
    public RoadNode node;
    public int road;
    public bool forward;                          // runs toward increasing distance along the road
    public float s;                               // node's distance along the road
    public Vector3 dir;                           // unit vector pointing away from the node
    public float width, length;                   // road width and free length beyond the node
    public int group;                             // signal phase group (0 or 1)
    public LaneSpec inLane, outLane;              // lane arriving at / leaving the node on this arm
}

public sealed class LaneSpec
{
    public string name;
    public int road;
    public float speedKmh = 40f;
    public bool connector;
    public RoadArm endsAt;                        // arm whose signal controls the lane end
    public readonly List<Vector3> points = new List<Vector3>();
    public readonly List<LaneSpec> next = new List<LaneSpec>();
}

/// <summary>
/// Turns road centre lines into intersections and one lane per direction (right-hand traffic).
/// Pure geometry: it creates no scene objects, so the builder and the QA can both use it.
/// </summary>
public sealed class RoadGraph
{
    const float ClusterRadius = 8f;
    const float StopBeyondRoad = 5f;              // stop line distance past the crossing road's edge
    const float FreeEndTrim = 1f;
    const float MinLinkLength = 4f;

    public List<RoadCenterline> roads;
    public RoadPolyline[] lines;
    public readonly List<RoadNode> nodes = new List<RoadNode>();
    public readonly List<LaneSpec> lanes = new List<LaneSpec>();

    public static float LaneOffset(float roadWidth) => roadWidth * .25f;

    public static RoadGraph Build(List<RoadCenterline> roads)
    {
        var graph = new RoadGraph { roads = roads, lines = new RoadPolyline[roads.Count] };
        for (int i = 0; i < roads.Count; i++) graph.lines[i] = new RoadPolyline(roads[i].points);
        graph.MakeNodes();
        foreach (RoadNode node in graph.nodes) graph.MakeArms(node);
        graph.nodes.RemoveAll(n => n.arms.Count < 2);
        for (int i = 0; i < graph.nodes.Count; i++) graph.nodes[i].id = i;
        for (int road = 0; road < roads.Count; road++) graph.MakeLanes(road);
        foreach (RoadNode node in graph.nodes) graph.Connect(node);
        return graph;
    }

    // ---- junction spots -> nodes -------------------------------------------------------------------------

    void MakeNodes()
    {
        var spots = new List<Vector3>();
        for (int i = 0; i < roads.Count; i++)
            for (int j = i + 1; j < roads.Count; j++) FindSpots(i, j, spots);
        var sums = new List<Vector3>();
        var counts = new List<int>();
        foreach (Vector3 spot in spots)
        {
            int hit = -1;
            for (int k = 0; k < sums.Count && hit < 0; k++)
                if (RoadPolyline.Flat(sums[k] / counts[k] - spot).magnitude < ClusterRadius) hit = k;
            if (hit < 0) { sums.Add(spot); counts.Add(1); }
            else { sums[hit] += spot; counts[hit]++; }
        }
        for (int k = 0; k < sums.Count; k++) nodes.Add(new RoadNode { position = sums[k] / counts[k] });
    }

    void FindSpots(int a, int b, List<Vector3> spots)
    {
        List<Vector3> pa = roads[a].points, pb = roads[b].points;
        for (int i = 0; i + 1 < pa.Count; i++)
            for (int j = 0; j + 1 < pb.Count; j++)
                if (Cross(pa[i], pa[i + 1], pb[j], pb[j + 1], out Vector3 hit)) spots.Add(hit);
        EndTouches(a, b, spots);
        EndTouches(b, a, spots);
    }

    // An end that stops inside the other road's width is a T junction even if the lines never cross.
    void EndTouches(int ending, int other, List<Vector3> spots)
    {
        List<Vector3> pts = roads[ending].points;
        foreach (Vector3 end in new[] { pts[0], pts[pts.Count - 1] })
        {
            float s = lines[other].Project(end, out float distance);
            if (distance <= roads[other].width * .5f + 1f) spots.Add(lines[other].PointAt(s));
        }
    }

    static bool Cross(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 hit)
    {
        hit = Vector3.zero;
        Vector3 r = RoadPolyline.Flat(b - a), q = RoadPolyline.Flat(d - c);
        float denom = r.x * q.z - r.z * q.x;
        if (Mathf.Abs(denom) < 1e-6f) return false;
        Vector3 ac = RoadPolyline.Flat(c - a);
        float t = (ac.x * q.z - ac.z * q.x) / denom, u = (ac.x * r.z - ac.z * r.x) / denom;
        if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
        hit = Vector3.Lerp(a, b, t);
        return true;
    }

    // ---- arms ------------------------------------------------------------------------------------------------

    void MakeArms(RoadNode node)
    {
        var onNode = new List<int>();
        float halfWidth = 0f;
        for (int r = 0; r < roads.Count; r++)
        {
            lines[r].Project(node.position, out float distance);
            if (distance > roads[r].width * .5f + 1.5f) continue;
            onNode.Add(r);
            halfWidth = Mathf.Max(halfWidth, roads[r].width * .5f);
        }
        node.clearance = halfWidth + StopBeyondRoad;
        foreach (int r in onNode)
        {
            float s = lines[r].Project(node.position, out _);
            AddArm(node, r, true, s);
            AddArm(node, r, false, s);
        }
    }

    void AddArm(RoadNode node, int road, bool forward, float s)
    {
        float length = forward ? lines[road].Length - s : s;
        if (length < node.clearance + 3f) return;
        Vector3 tangent = lines[road].TangentAt(s);
        node.arms.Add(new RoadArm
        {
            node = node, road = road, forward = forward, s = s, length = length, width = roads[road].width,
            dir = forward ? tangent : -tangent,
        });
    }

    static RoadArm FindArm(RoadNode node, int road, bool forward) =>
        node.arms.Find(a => a.road == road && a.forward == forward);

    // ---- lanes -------------------------------------------------------------------------------------------------

    sealed class Stop
    {
        public float s;
        public RoadNode node;                     // null for a free road end
    }

    void MakeLanes(int road)
    {
        var stops = new List<Stop>();
        foreach (RoadNode node in nodes)
        {
            RoadArm any = node.arms.Find(a => a.road == road);
            if (any != null) stops.Add(new Stop { node = node, s = any.s });
        }
        if (stops.Count == 0) return;
        stops.Sort((a, b) => a.s.CompareTo(b.s));
        if (FindArm(stops[0].node, road, false) != null) stops.Insert(0, new Stop { s = 0f });
        Stop last = stops[stops.Count - 1];
        if (last.node != null && FindArm(last.node, road, true) != null) stops.Add(new Stop { s = lines[road].Length });
        for (int i = 0; i + 1 < stops.Count; i++) MakeLink(road, stops[i], stops[i + 1]);
    }

    void MakeLink(int road, Stop from, Stop to)
    {
        float a = from.s + (from.node != null ? from.node.clearance : FreeEndTrim);
        float b = to.s - (to.node != null ? to.node.clearance : FreeEndTrim);
        if (b - a < MinLinkLength) return;
        List<Vector3> centre = lines[road].Between(a, b);
        float offset = LaneOffset(roads[road].width);
        RoadArm armAtFrom = from.node != null ? FindArm(from.node, road, true) : null;
        RoadArm armAtTo = to.node != null ? FindArm(to.node, road, false) : null;
        AddLane(road, centre, offset, armAtTo, armAtFrom, "fwd");
        var back = new List<Vector3>(centre);
        back.Reverse();
        AddLane(road, back, offset, armAtFrom, armAtTo, "back");
    }

    void AddLane(int road, List<Vector3> centre, float offset, RoadArm endArm, RoadArm startArm, string tag)
    {
        var lane = new LaneSpec
        {
            road = road, endsAt = endArm, speedKmh = roads[road].name.StartsWith("Perimeter") ? 50f : 40f,
            name = $"Lane_{roads[road].name}_{tag}_{lanes.Count}",
        };
        lane.points.AddRange(OffsetRight(centre, offset));
        lanes.Add(lane);
        if (endArm != null) endArm.inLane = lane;
        if (startArm != null) startArm.outLane = lane;
    }

    // Right-hand traffic: each lane sits on the right of its travel direction.
    static List<Vector3> OffsetRight(List<Vector3> pts, float offset)
    {
        var result = new List<Vector3>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 t = RoadPolyline.Flat(pts[Mathf.Min(pts.Count - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
            result.Add(pts[i] + new Vector3(t.z, 0f, -t.x) * offset);
        }
        return result;
    }

    // ---- connections inside a node ---------------------------------------------------------------------------

    void Connect(RoadNode node)
    {
        if (node.Signalled) AssignGroups(node);
        foreach (RoadArm from in node.arms)
        {
            if (from.inLane == null) continue;
            foreach (RoadArm to in ChooseExits(from)) LinkLanes(from, to);
        }
    }

    static void AssignGroups(RoadNode node)
    {
        RoadArm longest = node.arms[0];
        foreach (RoadArm arm in node.arms) if (arm.length > longest.length) longest = arm;
        foreach (RoadArm arm in node.arms) arm.group = Mathf.Abs(Vector3.Dot(arm.dir, longest.dir)) >= .707f ? 0 : 1;
    }

    // Straight and right turns only; a left turn is used when nothing else is possible (a corner or the stem of a T).
    static List<RoadArm> ChooseExits(RoadArm from)
    {
        var easy = new List<RoadArm>();
        var left = new List<RoadArm>();
        foreach (RoadArm to in from.node.arms)
        {
            if (to == from || to.outLane == null) continue;
            float turn = Vector3.SignedAngle(-from.dir, to.dir, Vector3.up);
            if (Mathf.Abs(turn) > 150f) continue;
            if (turn < -35f) left.Add(to); else easy.Add(to);
        }
        return easy.Count > 0 ? easy : left;
    }

    static void LinkLanes(RoadArm from, RoadArm to)
    {
        LaneSpec inLane = from.inLane, outLane = to.outLane;
        float turn = Mathf.Abs(Vector3.SignedAngle(-from.dir, to.dir, Vector3.up));
        if (turn < 35f) { inLane.next.Add(outLane); return; }
        var connector = new LaneSpec
        {
            name = $"Turn_n{from.node.id}_{from.road}to{to.road}_{inLane.next.Count}", connector = true, road = from.road, speedKmh = 20f,
        };
        connector.points.Add(Corner(inLane, outLane));
        connector.next.Add(outLane);
        inLane.next.Add(connector);
    }

    // Where the incoming lane's line meets the outgoing lane's line; their midpoint when they are nearly parallel.
    static Vector3 Corner(LaneSpec inLane, LaneSpec outLane)
    {
        Vector3 pIn = inLane.points[inLane.points.Count - 1], pOut = outLane.points[0];
        Vector3 hIn = RoadPolyline.Flat(pIn - inLane.points[inLane.points.Count - 2]).normalized;
        Vector3 hOut = RoadPolyline.Flat(outLane.points[1] - pOut).normalized;
        float denom = hIn.x * hOut.z - hIn.z * hOut.x;
        Vector3 middle = (pIn + pOut) * .5f;
        if (Mathf.Abs(denom) < .2f) return middle;
        Vector3 d = RoadPolyline.Flat(pOut - pIn);
        float t = (d.x * hOut.z - d.z * hOut.x) / denom;
        if (t < -2f || t > 40f) return middle;
        Vector3 corner = pIn + hIn * t;
        corner.y = middle.y;
        return corner;
    }
}
