using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One zebra crossing painted in the map: its centre, walking direction (across the road) and extent.</summary>
public sealed class PaintedCrosswalk
{
    public Vector3 centre;
    public Vector3 walk;                // unit, across the road (the direction people walk)
    public Vector3 along;               // unit, along the road (the direction of the bars)
    public float span;                  // extent across the road (bar count x pitch)
    public float depth;                 // bar length, along the road
    public int bars;
    public bool used;
    public float yTop, pitch, barLong, barShort;   // measured: bar top height, bar-to-bar spacing, bar length and width

    /// <summary>True when p (XZ) lies inside the painted rectangle, widened by slack.</summary>
    public bool Contains(Vector3 p, float slack = .5f)
    {
        Vector3 d = RoadPolyline.Flat(p - centre);
        return Mathf.Abs(Vector3.Dot(d, walk)) <= span * .5f + slack && Mathf.Abs(Vector3.Dot(d, along)) <= depth * .5f + slack;
    }
}

/// <summary>Everything found in StreetMarkings: the zebra crossings, one zebra bar (to copy) and all other markings.</summary>
public sealed class PaintedSet
{
    public List<PaintedCrosswalk> crosswalks = new List<PaintedCrosswalk>();
    public RoadSurface others = new RoadSurface();    // triangles of lines, arrows, stop lines: everything that is not a zebra bar
    public Mesh barMesh;                               // one painted bar centred on its rectangle, long axis on +z, top at y = 0
    public Material barMaterial;
}

/// <summary>
/// Finds the zebra crossings that are already painted in the merged "StreetMarkings" mesh: split it into welded
/// components, keep the zebra-bar sized ones (oriented rectangles ~2.8 x 0.7 m), then group neighbouring parallel bars.
/// </summary>
public static class PaintedCrosswalks
{
    public const string MeshObjectName = "StreetMarkings";
    const float BarLong = 2.8f, BarShort = .7f, Tolerance = .5f, MinBars = 4;

    struct Bar { public Vector2 centre, longAxis; public float yTop, lng, sht; }

    public static List<PaintedCrosswalk> Detect(Scene scene) => DetectAll(scene).crosswalks;

    public static PaintedSet DetectAll(Scene scene)
    {
        var set = new PaintedSet();
        var bars = new List<Bar>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name == MeshObjectName && filter.sharedMesh)
                    bars.AddRange(Bars(filter.sharedMesh, filter.transform.localToWorldMatrix, filter.GetComponent<MeshRenderer>(), set));
        set.crosswalks = Cluster(bars);
        return set;
    }

    static List<Bar> Bars(Mesh mesh, Matrix4x4 toWorld, MeshRenderer renderer, PaintedSet set)
    {
        Vector3[] v = mesh.vertices;
        int[] parent = new int[v.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        var weld = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < v.Length; i++)
        {
            var key = (Mathf.RoundToInt(v[i].x * 100f), Mathf.RoundToInt(v[i].y * 100f), Mathf.RoundToInt(v[i].z * 100f));
            if (weld.TryGetValue(key, out int other)) parent[Find(i)] = Find(other); else weld[key] = i;
        }
        int[] t = mesh.triangles;
        for (int i = 0; i + 2 < t.Length; i += 3) { parent[Find(t[i + 1])] = Find(t[i]); parent[Find(t[i + 2])] = Find(t[i]); }
        var groups = new Dictionary<int, List<Vector2>>();
        var top = new Dictionary<int, float>();
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 w = toWorld.MultiplyPoint3x4(v[i]);
            int r = Find(i);
            if (!groups.TryGetValue(r, out List<Vector2> list)) { groups[r] = list = new List<Vector2>(); top[r] = float.MinValue; }
            list.Add(new Vector2(w.x, w.z));
            top[r] = Mathf.Max(top[r], w.y);
        }
        var result = new List<Bar>();
        var isBar = new Dictionary<int, bool>();
        foreach (KeyValuePair<int, List<Vector2>> pair in groups)
        {
            bool zebra = FitRectangle(pair.Value, out Bar bar);
            isBar[pair.Key] = zebra;
            if (!zebra) continue;
            bar.yTop = top[pair.Key];
            result.Add(bar);
        }
        CollectOthersAndBar(mesh, toWorld, renderer, set, v, t, Find, isBar);
        return result;
    }

    // Non-zebra triangles go to set.others; the first zebra bar found is copied as the template mesh.
    static void CollectOthersAndBar(Mesh mesh, Matrix4x4 toWorld, MeshRenderer renderer, PaintedSet set, Vector3[] v, int[] t,
        Func<int, int> find, Dictionary<int, bool> isBar)
    {
        var others = new List<Vector3>();
        var otherTris = new List<int>();
        var barTris = new List<int>();
        int firstBarRoot = -1;
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            int r = find(t[i]);
            if (!isBar[r])
            {
                for (int k = 0; k < 3; k++) { otherTris.Add(others.Count); others.Add(toWorld.MultiplyPoint3x4(v[t[i + k]])); }
                continue;
            }
            if (firstBarRoot < 0 && set.barMesh == null) firstBarRoot = r;
            if (r == firstBarRoot) { barTris.Add(t[i]); barTris.Add(t[i + 1]); barTris.Add(t[i + 2]); }
        }
        set.others.Add(others.ToArray(), otherTris.ToArray(), Matrix4x4.identity);
        if (firstBarRoot < 0) return;
        set.barMesh = CopyBar(mesh, toWorld, barTris);
        set.barMaterial = BarMaterial(mesh, renderer, barTris[0]);
    }

    static Material BarMaterial(Mesh mesh, MeshRenderer renderer, int firstVertex)
    {
        if (!renderer) return null;
        Material[] materials = renderer.sharedMaterials;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
            if (Array.IndexOf(mesh.GetIndices(sub), firstVertex) >= 0) return materials[Mathf.Min(sub, materials.Length - 1)];
        return materials[0];
    }

    // The bar as drawn (positions, normals, uvs) at world scale, re-centred on its rectangle, long axis on +z, top at y = 0.
    static Mesh CopyBar(Mesh mesh, Matrix4x4 toWorld, List<int> tris)
    {
        Vector3[] src = mesh.vertices, normals = mesh.normals;
        Vector2[] uvs = mesh.uv;
        var pts = new List<Vector2>();
        float top = float.MinValue;
        foreach (int i in tris) { Vector3 w = toWorld.MultiplyPoint3x4(src[i]); pts.Add(new Vector2(w.x, w.z)); top = Mathf.Max(top, w.y); }
        FitRectangle(pts, out Bar bar);
        Quaternion toLocal = Quaternion.Inverse(Quaternion.LookRotation(new Vector3(bar.longAxis.x, 0f, bar.longAxis.y)));
        var map = new Dictionary<int, int>();
        var vs = new List<Vector3>(); var ns = new List<Vector3>(); var uv = new List<Vector2>(); var outTris = new List<int>();
        bool mirrored = toWorld.determinant < 0f;       // a mirrored transform flips the winding the renderer shows
        for (int n = 0; n + 2 < tris.Count; n += 3)
            for (int k = 0; k < 3; k++)
            {
                int i = tris[n + (mirrored ? 2 - k : k)];
                if (!map.TryGetValue(i, out int j))
                {
                    j = map[i] = vs.Count;
                    Vector3 w = toWorld.MultiplyPoint3x4(src[i]);
                    vs.Add(toLocal * new Vector3(w.x - bar.centre.x, w.y - top, w.z - bar.centre.y));
                    ns.Add(normals.Length > i ? toLocal * toWorld.MultiplyVector(normals[i]).normalized : Vector3.up);
                    uv.Add(uvs.Length > i ? uvs[i] : Vector2.zero);
                }
                outTris.Add(j);
            }
        var copy = new Mesh { name = "ZebraBar", vertices = vs.ToArray(), normals = ns.ToArray(), uv = uv.ToArray(), triangles = outTris.ToArray() };
        copy.RecalculateBounds();
        return copy;
    }

    // Minimum-area oriented rectangle by sweeping the angle; accepted when it is zebra-bar sized.
    static bool FitRectangle(List<Vector2> points, out Bar bar)
    {
        bar = default;
        float bestArea = float.MaxValue, bestAngle = 0f;
        Vector2 bestMin = default, bestMax = default;
        for (int deg = 0; deg < 90; deg++)
        {
            float rad = deg * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)), w = new Vector2(-u.y, u.x);
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = -mn;
            foreach (Vector2 p in points)
            {
                var q = new Vector2(Vector2.Dot(p, u), Vector2.Dot(p, w));
                mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q);
            }
            float area = (mx.x - mn.x) * (mx.y - mn.y);
            if (area < bestArea) { bestArea = area; bestAngle = rad; bestMin = mn; bestMax = mx; }
        }
        float a = bestMax.x - bestMin.x, b = bestMax.y - bestMin.y;
        bool along = a >= b;
        float lng = Mathf.Max(a, b), sht = Mathf.Min(a, b);
        if (Mathf.Abs(lng - BarLong) > Tolerance || Mathf.Abs(sht - BarShort) > Tolerance * .5f) return false;
        Vector2 ux = new Vector2(Mathf.Cos(bestAngle), Mathf.Sin(bestAngle)), uz = new Vector2(-ux.y, ux.x);
        Vector2 mid = ux * ((bestMin.x + bestMax.x) * .5f) + uz * ((bestMin.y + bestMax.y) * .5f);
        bar = new Bar { centre = mid, longAxis = along ? ux : uz, lng = lng, sht = sht };
        return true;
    }

    static List<PaintedCrosswalk> Cluster(List<Bar> raw)
    {
        var bars = new List<Bar>();
        foreach (Bar b in raw)                     // coincident duplicates (two overlapping meshes) count once
            if (!bars.Exists(o => (o.centre - b.centre).sqrMagnitude < .04f)) bars.Add(b);
        int[] parent = new int[bars.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int i = 0; i < bars.Count; i++)
            for (int j = i + 1; j < bars.Count; j++)
                if (Neighbours(bars[i], bars[j])) parent[Find(j)] = Find(i);
        var groups = new Dictionary<int, List<Bar>>();
        for (int i = 0; i < bars.Count; i++)
        {
            int r = Find(i);
            if (!groups.TryGetValue(r, out List<Bar> list)) groups[r] = list = new List<Bar>();
            list.Add(bars[i]);
        }
        var result = new List<PaintedCrosswalk>();
        foreach (List<Bar> group in groups.Values)
            if (group.Count >= MinBars) result.Add(Describe(group));
        return result;
    }

    // Parallel bars side by side: same long axis, offset mostly across it (one pitch ~1.4 m, a little tolerance).
    static bool Neighbours(Bar a, Bar b)
    {
        if (Mathf.Abs(Vector2.Dot(a.longAxis, b.longAxis)) < .995f) return false;
        Vector2 d = b.centre - a.centre, across = new Vector2(-a.longAxis.y, a.longAxis.x);
        return Mathf.Abs(Vector2.Dot(d, a.longAxis)) < .5f && Mathf.Abs(Vector2.Dot(d, across)) < 2.2f;
    }

    static PaintedCrosswalk Describe(List<Bar> group)
    {
        Vector2 along = group[0].longAxis, walk = new Vector2(-along.y, along.x), sum = Vector2.zero;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (Bar b in group)
        {
            sum += b.centre;
            float s = Vector2.Dot(b.centre, walk);
            lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s);
        }
        Vector2 c = sum / group.Count;
        // The centre of the extent (not the mean of bar centres) is the middle of the painted block.
        Vector2 mid = c + walk * ((lo + hi) * .5f - Vector2.Dot(c, walk));
        var offsets = group.ConvertAll(b => Vector2.Dot(b.centre, walk));
        offsets.Sort();
        var steps = new List<float>();
        for (int i = 1; i < offsets.Count; i++) if (offsets[i] - offsets[i - 1] > .3f) steps.Add(offsets[i] - offsets[i - 1]);
        steps.Sort();
        return new PaintedCrosswalk
        {
            centre = new Vector3(mid.x, 0f, mid.y), walk = new Vector3(walk.x, 0f, walk.y), along = new Vector3(along.x, 0f, along.y),
            span = hi - lo + BarShort, depth = BarLong, bars = group.Count,
            yTop = group.Average(b => b.yTop), barLong = group.Average(b => b.lng), barShort = group.Average(b => b.sht),
            pitch = steps.Count > 0 ? steps[steps.Count / 2] : 1.4f,
        };
    }
}
