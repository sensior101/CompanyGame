using System.Collections.Generic;
using UnityEngine;

/// <summary>Side-profile point: z along the car, y up, k = width scale at this point (1 = full width).</summary>
public readonly struct ProfilePoint
{
    public readonly float z, y, k;
    public ProfilePoint(float z, float y, float k = 1f) { this.z = z; this.y = y; this.k = k; }
}

/// <summary>
/// Flat-shaded low-poly mesh writer for Editor tools. Every triangle owns its three vertices (hard normals),
/// submesh index = material slot. All primitives orient their faces from an outward hint, so winding is never hand-tuned.
/// </summary>
public sealed class LowPolyMesh
{
    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<Vector3> normals = new List<Vector3>();
    readonly List<int>[] triangles;

    /// <summary>Added to every vertex written afterwards (used to move the pivot to the driver seat).</summary>
    public Vector3 Offset;
    public int TriangleCount { get; private set; }

    public LowPolyMesh(int submeshCount)
    {
        triangles = new List<int>[submeshCount];
        for (int i = 0; i < submeshCount; i++) triangles[i] = new List<int>();
    }

    /// <summary>Adds one triangle; winding is flipped when its normal disagrees with <paramref name="outward"/>.</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, int sub)
    {
        var n = Vector3.Cross(b - a, c - a);
        if (n.sqrMagnitude < 1e-10f) return;
        if (Vector3.Dot(n, outward) < 0f) { var t = b; b = c; c = t; n = -n; }
        n.Normalize();
        var list = triangles[sub];
        foreach (var v in new[] { a, b, c })
        {
            list.Add(vertices.Count);
            vertices.Add(v + Offset);
            normals.Add(n);
        }
        TriangleCount++;
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int sub)
    {
        Tri(a, b, c, outward, sub);
        Tri(a, c, d, outward, sub);
    }

    public void Box(Vector3 center, Vector3 size, int sub)
    {
        var half = size * 0.5f;
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 n = Vector3.zero, u = Vector3.zero, v = Vector3.zero;
            n[axis] = 1f; u[(axis + 1) % 3] = 1f; v[(axis + 2) % 3] = 1f;
            float hn = half[axis], hu = half[(axis + 1) % 3], hv = half[(axis + 2) % 3];
            for (int s = -1; s <= 1; s += 2)
            {
                var fc = center + n * (hn * s);
                Quad(fc - u * hu - v * hv, fc + u * hu - v * hv, fc + u * hu + v * hv, fc - u * hu + v * hv, n * s, sub);
            }
        }
    }

    /// <summary>
    /// Extrudes a side profile (z,y) across the car. Half width at height y lerps hwBottom..hwTop between
    /// yBottom..yTop (tumblehome) and is scaled by each point's k (rounded nose / tail).
    /// </summary>
    public void Loft(IList<ProfilePoint> points, float hwBottom, float hwTop, float yBottom, float yTop, int sub)
    {
        var p = new List<ProfilePoint>(points);
        if (SignedArea(p) < 0f) p.Reverse();
        int n = p.Count;
        Vector3 V(int i, float side) => new Vector3(side * HalfWidth(p[i], hwBottom, hwTop, yBottom, yTop), p[i].y, p[i].z);
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            float dz = p[j].z - p[i].z, dy = p[j].y - p[i].y;
            Quad(V(i, -1), V(j, -1), V(j, 1), V(i, 1), new Vector3(0f, -dz, dy), sub);
        }
        foreach (var t in Triangulate(p))
        {
            Tri(V(t[0], 1), V(t[1], 1), V(t[2], 1), Vector3.right, sub);
            Tri(V(t[0], -1), V(t[1], -1), V(t[2], -1), Vector3.left, sub);
        }
    }

    /// <summary>Cylinder whose axis is X, then rotated by <paramref name="rot"/> (identity = wheel axis X).</summary>
    public void Cylinder(Vector3 center, float radius, float length, int sides, Quaternion rot, int sub)
    {
        var ringA = new Vector3[sides];
        var ringB = new Vector3[sides];
        float h = length * 0.5f;
        for (int i = 0; i < sides; i++)
        {
            float a = Mathf.PI * 2f * i / sides;
            float cy = Mathf.Cos(a) * radius, cz = Mathf.Sin(a) * radius;
            ringA[i] = center + rot * new Vector3(-h, cy, cz);
            ringB[i] = center + rot * new Vector3(h, cy, cz);
        }
        var ax = center + rot * new Vector3(-h, 0f, 0f);
        var bx = center + rot * new Vector3(h, 0f, 0f);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            var mid = (ringA[i] + ringA[j]) * 0.5f - (center + rot * new Vector3(-h, 0f, 0f));
            Quad(ringA[i], ringA[j], ringB[j], ringB[i], mid, sub);
            Tri(ax, ringA[i], ringA[j], rot * Vector3.left, sub);
            Tri(bx, ringB[i], ringB[j], rot * Vector3.right, sub);
        }
    }

    public Mesh Build(string meshName)
    {
        var mesh = new Mesh { name = meshName };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.subMeshCount = triangles.Length;
        for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateBounds();
        return mesh;
    }

    static float HalfWidth(ProfilePoint p, float hwBottom, float hwTop, float yBottom, float yTop)
    {
        float t = Mathf.Approximately(yTop, yBottom) ? 0f : Mathf.Clamp01((p.y - yBottom) / (yTop - yBottom));
        return Mathf.Lerp(hwBottom, hwTop, t) * p.k;
    }

    static float SignedArea(List<ProfilePoint> p)
    {
        float area = 0f;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Count];
            area += a.z * b.y - b.z * a.y;
        }
        return area * 0.5f;
    }

    // Ear clipping for a counter-clockwise polygon (z right, y up). Returns index triples.
    static List<int[]> Triangulate(List<ProfilePoint> p)
    {
        var result = new List<int[]>();
        var idx = new List<int>();
        for (int i = 0; i < p.Count; i++) idx.Add(i);
        while (idx.Count > 3)
        {
            int ear = FindEar(p, idx);
            int c = idx.Count;
            result.Add(new[] { idx[(ear + c - 1) % c], idx[ear], idx[(ear + 1) % c] });
            idx.RemoveAt(ear);
        }
        result.Add(new[] { idx[0], idx[1], idx[2] });
        return result;
    }

    static int FindEar(List<ProfilePoint> p, List<int> idx)
    {
        int c = idx.Count;
        for (int e = 0; e < c; e++)
        {
            var a = p[idx[(e + c - 1) % c]]; var b = p[idx[e]]; var d = p[idx[(e + 1) % c]];
            if (Cross(a, b, d) <= 1e-6f) continue;
            bool empty = true;
            for (int k = 0; k < c && empty; k++)
            {
                if (k == e || k == (e + c - 1) % c || k == (e + 1) % c) continue;
                empty = !Inside(p[idx[k]], a, b, d);
            }
            if (empty) return e;
        }
        return 0; // ponytail: degenerate/collinear input falls back to the first vertex; profiles here are simple.
    }

    static float Cross(ProfilePoint a, ProfilePoint b, ProfilePoint c) => (b.z - a.z) * (c.y - a.y) - (b.y - a.y) * (c.z - a.z);

    static bool Inside(ProfilePoint q, ProfilePoint a, ProfilePoint b, ProfilePoint c)
    {
        return Cross(a, b, q) >= 0f && Cross(b, c, q) >= 0f && Cross(c, a, q) >= 0f;
    }
}
