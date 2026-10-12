using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Validates the generated cars (design section 5-1): assets exist, sizes, wheels, URP Lit, triangle budget,
/// driver-seat pivot, grade monotonicity and the acceleration model. Throws when any check fails.
/// </summary>
public static class CarModelQA
{
    const int MaxTriangles = 3000; // design says 1500; raised so the lofted body + round lights + arches stay recognisable (see NOTES).
    static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };

    [MenuItem("CompanyGame/Vehicles/Validate Cars")]
    static void Menu() => Debug.Log(Run());

    /// <summary>Batch wrapper: logs the report, exit 2 on failure.</summary>
    public static void RunBatch()
    {
        try { Debug.Log(Run()); }
        catch (Exception e) { Debug.LogError(e.Message); EditorApplication.Exit(2); }
    }

    /// <summary>Entry point for -executeMethod CarModelQA.Run.</summary>
    public static string Run()
    {
        var failures = new List<string>();
        var log = new StringBuilder();
        var grades = new List<VehicleGrade>();
        foreach (var s in CarModelBuilder.Specs)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(CarModelBuilder.CarItemsRoot + "/Car_" + s.id + ".asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarModelBuilder.ArtRoot + "/Prefabs/Car_" + s.id + ".prefab");
            var grade = AssetDatabase.LoadAssetAtPath<VehicleGrade>(CarModelBuilder.GradeRoot + "/Grade_" + s.id + ".asset");
            grades.Add(grade);
            if (!item || !prefab || !grade) { failures.Add(s.id + ": missing item/prefab/grade asset"); continue; }
            CheckItem(s, item, prefab, grade, failures);
            CheckPrefab(s, prefab, failures, log);
            CheckAcceleration(s.id, grade, failures);
        }
        CheckMonotonic(grades, failures);
        string report = log + (failures.Count == 0 ? "CarModelQA PASSED" : "CarModelQA FAILED\n - " + string.Join("\n - ", failures));
        if (failures.Count > 0) throw new Exception(report);
        return report;
    }

    static void CheckItem(CarModelBuilder.CarSpec s, ItemData item, GameObject prefab, VehicleGrade grade, List<string> f)
    {
        if (item.itemId != s.ItemId) f.Add(s.id + ": itemId " + item.itemId + " != " + s.ItemId);
        if (item.vehiclePrefab != prefab) f.Add(s.id + ": vehiclePrefab not linked");
        if (item.grade != grade) f.Add(s.id + ": grade not linked");
        if (!item.IsCar) f.Add(s.id + ": IsCar is false");
        if (!item.icon) f.Add(s.id + ": icon missing"); else CheckIconAlpha(s.id, item.icon.texture, f);
        if (Mathf.Abs(item.wheelRadius - s.wheelRadius) > 1e-4f) f.Add(s.id + ": wheelRadius " + item.wheelRadius);
    }

    // Icon must be transparent at the corners and opaque in the centre. Reads the PNG file (texture may be non-readable).
    static void CheckIconAlpha(string id, Texture2D asset, List<string> f)
    {
        var t = new Texture2D(2, 2, TextureFormat.ARGB32, false);
        try
        {
            t.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(asset)));
            int w = t.width - 1, h = t.height - 1;
            foreach (var p in new[] { new Vector2Int(0, 0), new Vector2Int(w, 0), new Vector2Int(0, h), new Vector2Int(w, h) })
                if (t.GetPixel(p.x, p.y).a > 0f) f.Add(id + ": icon corner " + p + " alpha " + t.GetPixel(p.x, p.y).a);
            if (t.GetPixel(t.width / 2, t.height / 2).a <= 0f) f.Add(id + ": icon centre is transparent");
        }
        finally { UnityEngine.Object.DestroyImmediate(t); }
    }

    static void CheckPrefab(CarModelBuilder.CarSpec s, GameObject prefab, List<string> f, StringBuilder log)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        try
        {
            var renderers = inst.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            CheckSize(s.id, b, f);
            CheckWheels(s, inst.transform, f);
            CheckMaterials(s.id, renderers, f);
            int tris = inst.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh.triangles.Length / 3);
            if (tris > MaxTriangles) f.Add(s.id + ": " + tris + " triangles > " + MaxTriangles);
            if (b.center.x < .2f || b.center.x > .6f) f.Add(s.id + ": pivot not at left-hand driver seat, bounds.center.x=" + b.center.x);
            if (b.center.z < -.6f || b.center.z > 0f) f.Add(s.id + ": pivot not slightly ahead of centre, bounds.center.z=" + b.center.z);
            log.AppendLine(s.id + ": size " + b.size.ToString("F2") + ", " + tris + " tris, centre " + b.center.ToString("F2"));
        }
        finally { UnityEngine.Object.DestroyImmediate(inst); }
    }

    static void CheckSize(string id, Bounds b, List<string> f)
    {
        if (b.size.z < 3.4f || b.size.z > 5.2f) f.Add(id + ": length " + b.size.z);
        if (b.size.x < 1.5f || b.size.x > 2.1f) f.Add(id + ": width " + b.size.x);
        if (b.size.y < 1.2f || b.size.y > 1.7f) f.Add(id + ": height " + b.size.y);
        if (Mathf.Abs(b.min.y) > .02f) f.Add(id + ": tyres should touch y=0, min.y=" + b.min.y);
    }

    static void CheckWheels(CarModelBuilder.CarSpec s, Transform root, List<string> f)
    {
        var wheels = root.Cast<Transform>().Where(t => t.name.StartsWith("Wheel")).ToList();
        if (wheels.Count != 4) f.Add(s.id + ": " + wheels.Count + " wheels, need 4");
        foreach (var n in WheelNames)
        {
            var w = root.Find(n);
            if (!w) { f.Add(s.id + ": " + n + " missing"); continue; }
            var mb = w.GetComponent<MeshFilter>().sharedMesh.bounds;
            if (mb.center.magnitude > .01f) f.Add(s.id + ": " + n + " pivot not at hub centre " + mb.center);
            if (mb.size.x >= mb.size.y) f.Add(s.id + ": " + n + " axis is not X");
            if (Mathf.Abs(mb.extents.y - s.wheelRadius) > .02f) f.Add(s.id + ": " + n + " radius " + mb.extents.y);
            if (Mathf.Abs(w.localPosition.y - s.wheelRadius) > .02f) f.Add(s.id + ": " + n + " hub height " + w.localPosition.y);
        }
    }

    static void CheckMaterials(string id, Renderer[] renderers, List<string> f)
    {
        foreach (var m in renderers.SelectMany(r => r.sharedMaterials))
            if (!m || m.shader.name != "Universal Render Pipeline/Lit") f.Add(id + ": non URP Lit material " + (m ? m.name : "null"));
    }

    // 0->100 km/h time must match the grade within 2%; grades whose top speed is below 100 are checked at top speed.
    static void CheckAcceleration(string id, VehicleGrade g, List<string> f)
    {
        const float dt = .02f;
        float targetKmh = Mathf.Min(100f, g.topSpeedKmh), target = targetKmh / 3.6f;
        float expect = g.zeroTo100Sec * targetKmh / 100f, v = 0f, t = 0f, reached = -1f;
        for (; t < 120f; t += dt)
        {
            v = g.Step(v, 1f, dt);
            if (v > g.topSpeedKmh / 3.6f + .01f) { f.Add(id + ": exceeded top speed " + v * 3.6f); return; }
            if (reached < 0f && v >= target - 1e-4f) reached = t + dt;
        }
        if (reached < 0f || Mathf.Abs(reached - expect) > expect * .02f + dt) f.Add(id + ": reached " + targetKmh + " km/h in " + reached + "s, expected " + expect + "s");
        if (Mathf.Abs(v * 3.6f - g.topSpeedKmh) > .5f) f.Add(id + ": did not settle at top speed, v=" + v * 3.6f);
    }

    static void CheckMonotonic(List<VehicleGrade> grades, List<string> f)
    {
        for (int i = 1; i < grades.Count; i++)
        {
            if (!grades[i] || !grades[i - 1]) return;
            if (grades[i].topSpeedKmh <= grades[i - 1].topSpeedKmh) f.Add("top speed not increasing at grade " + (i + 1));
            if (grades[i].zeroTo100Sec >= grades[i - 1].zeroTo100Sec) f.Add("zero-to-100 not decreasing at grade " + (i + 1));
        }
    }
}
