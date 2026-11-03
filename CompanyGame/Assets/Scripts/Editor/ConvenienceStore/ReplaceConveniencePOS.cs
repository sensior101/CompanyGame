using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Swaps the box-built register baked into the convenience store's 03_Checkout meshes for the low-poly POS.
/// Only triangles inside the old register's volume on top of the counter are removed; the counter, coffee machine and
/// hot case stay. Safe to run again: nothing is left to cut and the existing RetailPOS instance is replaced.
/// </summary>
public static class ReplaceConveniencePOS
{
    const string ScenePath = "Assets/Scenes/Interiors/ConvenienceStoreInterior.unity";
    const string PrefabPath = "Assets/Art/Interiors/ConvenienceStore/ConvenienceStoreInterior.prefab";
    const string MeshFolder = "Assets/Art/Interiors/ConvenienceStore/Meshes/";
    const string PosPath = "Assets/Art/Items/RetailPOS_LowPoly/LowPolyPOS.fbx";
    const string InstanceName = "RetailPOS";
    const float AuthoredWidth = .625f;
    // Old register volume in the interior's local space (checkout counter top is y = 1.015).
    static readonly Bounds OldRegister = new Bounds(new Vector3(-3.05f, 1.36f, 4.20f), new Vector3(.50f, .68f, 1.40f));
    static readonly Vector3 Spot = new Vector3(-3.05f, 1.015f, 4.10f);
    static readonly string[] CutMaterials = { "Charcoal", "Screen", "Blue", "Cream", "Steel" };

    [MenuItem("CompanyGame/Setup/Replace Convenience Store POS")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        int removed = CutOldRegister();
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try { Place(prefab.transform); PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = scene.GetRootGameObjects().Select(g => g.transform).FirstOrDefault(t => Find(t, "03_Checkout"));
        if (!root) throw new InvalidOperationException("03_Checkout not found in " + ScenePath);
        var pos = Place(root);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log($"[ReplaceConveniencePOS] removed {removed} old register triangles; RetailPOS at {pos.position}");
        string shot = Environment.GetCommandLineArgs().SkipWhile(a => a != "-posPreview").Skip(1).FirstOrDefault();
        if (!string.IsNullOrEmpty(shot)) Preview(pos, shot);
    }

    static int CutOldRegister()
    {
        int removed = 0;
        foreach (var material in CutMaterials)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + "03_Checkout__" + material + ".asset");
            if (!mesh) continue;
            var v = mesh.vertices; var t = mesh.triangles; var keep = new List<int>(t.Length);
            for (int i = 0; i < t.Length; i += 3)
            {
                if (OldRegister.Contains(v[t[i]]) && OldRegister.Contains(v[t[i + 1]]) && OldRegister.Contains(v[t[i + 2]])) { removed++; continue; }
                keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
            }
            if (keep.Count == t.Length) continue;
            mesh.triangles = keep.ToArray(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
        }
        return removed;
    }

    static Transform Place(Transform interior)
    {
        var checkout = Find(interior, "03_Checkout") ?? throw new InvalidOperationException("03_Checkout group missing");
        var old = checkout.Find(InstanceName);
        if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
        var pos = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PosPath), checkout);
        pos.name = InstanceName; pos.isStatic = true;
        foreach (var t in pos.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
        // Face the customer display toward the shop floor (+X); the cashier stands on the -X side.
        // Keep the FBX root's axis-conversion rotation and only turn the model about the up axis.
        pos.transform.localPosition = Vector3.zero;
        var monitor = Find(pos.transform, "MainMonitor"); var customer = Find(pos.transform, "CustomerDisplayHead");
        Vector3 toCustomer = checkout.InverseTransformVector(customer.position - monitor.position); toCustomer.y = 0;
        float yaw = Vector3.SignedAngle(toCustomer, Vector3.right, Vector3.up);
        pos.transform.localRotation = Quaternion.AngleAxis(yaw, Vector3.up) * pos.transform.localRotation;
        // Normalise to the authored width in case the FBX import scale differs.
        var raw = LocalBounds(pos, checkout);
        pos.transform.localScale *= AuthoredWidth / Mathf.Max(raw.size.x, raw.size.z);
        // Sit the bounds' centre on the old register spot and the base on the counter top.
        var bounds = LocalBounds(pos, checkout);
        pos.transform.localPosition = new Vector3(Spot.x - bounds.center.x, Spot.y - bounds.min.y, Spot.z - bounds.center.z);
        return pos.transform;
    }

    static Bounds LocalBounds(GameObject go, Transform space)
    {
        var b = new Bounds(); bool any = false;
        foreach (var f in go.GetComponentsInChildren<MeshFilter>())
            foreach (var c in Corners(f.sharedMesh.bounds))
            {
                var p = space.InverseTransformPoint(f.transform.TransformPoint(c));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        return b;
    }

    static IEnumerable<Vector3> Corners(Bounds b)
    {
        for (int i = 0; i < 8; i++)
            yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
    }

    static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root) { var hit = Find(child, name); if (hit) return hit; }
        return null;
    }

    // Batch-mode check: a customer-side view of the counter written to a PNG.
    static void Preview(Transform pos, string path)
    {
        var go = new GameObject("POS_Preview_Camera"); var cam = go.AddComponent<Camera>();
        try
        {
            // The interior root may be scaled, so frame the POS by its world bounds.
            var b = pos.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, n) => { a.Encapsulate(n); return a; });
            Vector3 c = b.center; float s = b.size.magnitude;
            var counter = pos.parent.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith("03_Checkout__")).Select(r => r.bounds).Aggregate((a, n) => { a.Encapsulate(n); return a; });
            Debug.Log($"[ReplaceConveniencePOS] pos bounds {b.min:F3}..{b.max:F3}; checkout bounds {counter.min:F3}..{counter.max:F3}");
            go.transform.position = c + new Vector3(1.2f, .8f, -.8f) * s * 1.8f; go.transform.LookAt(c);
            cam.nearClipPlane = s * .02f; cam.farClipPlane = s * 40f;
            cam.fieldOfView = 45; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.gray;
            var rt = new RenderTexture(1024, 768, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(1024, 768, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1024, 768), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
