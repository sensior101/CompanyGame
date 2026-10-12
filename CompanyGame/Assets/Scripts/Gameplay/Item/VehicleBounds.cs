using UnityEngine;

/// <summary>Measures a vehicle prefab or instance in its own root-local space (works on unplaced prefab assets).</summary>
public static class VehicleBounds
{
    // Bicycle-sized fallback for models without meshes.
    static readonly Bounds Fallback = new Bounds(new Vector3(0f, .5f, 0f), new Vector3(.6f, 1f, 1.8f));

    public static Bounds Measure(GameObject root)
    {
        if (!root) return Fallback;
        bool any = false;
        var result = new Bounds();
        var toLocal = root.transform.worldToLocalMatrix;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.sharedMesh) continue;
            Encapsulate(ref result, ref any, filter.sharedMesh.bounds, toLocal * filter.transform.localToWorldMatrix);
        }
        foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            Encapsulate(ref result, ref any, skinned.localBounds, toLocal * skinned.transform.localToWorldMatrix);
        return any ? result : Fallback;
    }

    static void Encapsulate(ref Bounds total, ref bool any, Bounds local, Matrix4x4 toRoot)
    {
        for (int i = 0; i < 8; i++)
        {
            var corner = local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var point = toRoot.MultiplyPoint3x4(corner);
            if (!any) { total = new Bounds(point, Vector3.zero); any = true; }
            else total.Encapsulate(point);
        }
    }
}
