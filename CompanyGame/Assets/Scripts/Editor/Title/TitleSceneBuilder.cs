using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Regenerates Scenes/Title.unity: a low-poly dusk town around a company tower, a camera that circles it slowly and the
/// TitleScreen menu. Also writes Resources/Title/TitleStyle.asset (font + rounded sprite) and puts the scene first in the
/// build settings. Run: CompanyGame/Title/Build Title Scene, or -executeMethod TitleSceneBuilder.Build.
/// </summary>
public static class TitleSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Title.unity";
    const string AssetDir = "Assets/Scenes/Title";
    const string StylePath = "Assets/Resources/Title/TitleStyle.asset";
    const string FontPath = "Assets/Art/font/NotoSansKR-Regular SDF.asset";

    static readonly string[] WallColors = { "E8B4A0", "D98E73", "F2D0A4", "A5B4CB", "8FA3AD", "C9A7C2", "EAC96B", "B8C5A0" };
    static readonly string[] RoofColors = { "7A3E3E", "4B4F6B", "5E4A3A", "6B5B7A" };

    static Material[] walls, roofs;
    static Material windowLit, ground, tower, towerGlass;

    [MenuItem("CompanyGame/Title/Build Title Scene")]
    public static void Build()
    {
        MakeStyle();
        MakeMaterials();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildLighting();
        BuildSky();
        BuildTown();
        BuildCamera();
        new GameObject("Title Screen").AddComponent<TitleScreen>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        PutFirstInBuild();
        AssetDatabase.SaveAssets();
        Debug.Log("[TitleSceneBuilder] built " + ScenePath);
    }

    // ---- assets ----

    static void MakeStyle()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StylePath));
        var style = AssetDatabase.LoadAssetAtPath<TitleStyle>(StylePath);
        if (!style) { style = ScriptableObject.CreateInstance<TitleStyle>(); AssetDatabase.CreateAsset(style, StylePath); }
        style.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        style.rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (!style.font) Debug.LogError("[TitleSceneBuilder] Korean font asset missing: " + FontPath);
        EditorUtility.SetDirty(style);
    }

    static Material Mat(string name, string shader, Color color, float smoothness = 0.05f)
    {
        Directory.CreateDirectory(AssetDir + "/Materials");
        string path = AssetDir + "/Materials/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = Shader.Find(shader);
        mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }

    static void MakeMaterials()
    {
        const string lit = "Universal Render Pipeline/Lit", unlit = "Universal Render Pipeline/Unlit";
        var w = new List<Material>(); var r = new List<Material>();
        foreach (var hex in WallColors) w.Add(Mat("Wall_" + hex, lit, Hex(hex)));
        foreach (var hex in RoofColors) r.Add(Mat("Roof_" + hex, lit, Hex(hex)));
        walls = w.ToArray(); roofs = r.ToArray();
        windowLit = Mat("WindowLit", unlit, Hex("FFD27A"));
        ground = Mat("Ground", lit, Hex("4A4A5E"));
        tower = Mat("Tower", lit, Hex("3F4766"), 0.3f);
        towerGlass = Mat("TowerGlass", unlit, Hex("FFE6A8"));
    }

    // The sky is a big inward-facing sphere with a gradient shader (Scenes/Title/TitleSky.shader); no skybox, no fog on it.
    static void BuildSky()
    {
        var shader = Shader.Find("CompanyGame/TitleSky");
        if (!shader) throw new System.Exception("TitleSky shader not found");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDir + "/Materials/Sky.mat");
        if (!mat) { mat = new Material(shader); Directory.CreateDirectory(AssetDir + "/Materials"); AssetDatabase.CreateAsset(mat, AssetDir + "/Materials/Sky.mat"); }
        mat.shader = shader;
        EditorUtility.SetDirty(mat);
        Block("Sky", null, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 4000f, mat);
    }

    // ---- scene ----

    static void BuildLighting()
    {
        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Hex("FFB074"); sun.intensity = 2.3f; sun.shadows = LightShadows.Soft;
        sunGo.transform.rotation = Quaternion.Euler(14f, -32f, 0f);
        RenderSettings.sun = sun;

        RenderSettings.skybox = null;


        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Hex("8A80B8");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = Hex("D4857E");
        RenderSettings.fogDensity = 0.0035f;
    }

    static GameObject Block(string name, Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static void BuildTown()
    {
        var root = new GameObject("Town").transform;
        var plane = Block("Ground", root, PrimitiveType.Plane, Vector3.zero, new Vector3(200, 1, 200), ground);
        plane.isStatic = true;
        var rng = new System.Random(20261012);
        for (int gx = -6; gx <= 6; gx++)
            for (int gz = -6; gz <= 6; gz++)
            {
                if (Mathf.Abs(gx) <= 1 && Mathf.Abs(gz) <= 1) continue; // the plaza under the tower
                if (rng.NextDouble() < 0.12) continue;
                var pos = new Vector3(gx * 7.4f + Jitter(rng, 1.2f), 0, gz * 7.4f + Jitter(rng, 1.2f));
                float height = 4f + (float)rng.NextDouble() * 9f * (1.2f - Mathf.Min(1f, pos.magnitude / 55f));
                Building(root, pos, 4.4f + (float)rng.NextDouble() * 1.6f, height, 4.4f + (float)rng.NextDouble() * 1.6f, rng);
            }
        Tower(root);
    }

    static float Jitter(System.Random rng, float amount) => (float)(rng.NextDouble() * 2 - 1) * amount;

    static void Building(Transform root, Vector3 pos, float w, float h, float d, System.Random rng)
    {
        var wall = walls[rng.Next(walls.Length)];
        var b = new GameObject("Building").transform;
        b.SetParent(root, false); b.localPosition = pos; b.localRotation = Quaternion.Euler(0, (float)rng.Next(4) * 90f + Jitter(rng, 6f), 0);
        Block("Walls", b, PrimitiveType.Cube, new Vector3(0, h / 2, 0), new Vector3(w, h, d), wall);
        if (h < 9f)
            // pitched roof: a cube turned 45 degrees reads as a triangular prism
            Block("Roof", b, PrimitiveType.Cube, new Vector3(0, h + w * 0.18f, 0), new Vector3(w * 0.78f, w * 0.78f, d + 0.5f), roofs[rng.Next(roofs.Length)]).transform.localRotation = Quaternion.Euler(0, 0, 45);
        else
        {
            Block("Parapet", b, PrimitiveType.Cube, new Vector3(0, h + 0.2f, 0), new Vector3(w + 0.3f, 0.4f, d + 0.3f), roofs[rng.Next(roofs.Length)]);
            if (rng.NextDouble() < 0.6) Block("Tank", b, PrimitiveType.Cylinder, new Vector3(w * 0.2f, h + 1.1f, d * 0.1f), new Vector3(1.4f, 0.8f, 1.4f), roofs[rng.Next(roofs.Length)]);
        }
        int windows = 5 + rng.Next(6);
        for (int i = 0; i < windows; i++) Window(b, w, h, d, rng);
    }

    static void Window(Transform b, float w, float h, float d, System.Random rng)
    {
        bool onX = rng.Next(2) == 0;
        float side = rng.Next(2) == 0 ? 1f : -1f;
        float y = 1.6f + (float)rng.NextDouble() * Mathf.Max(0.5f, h - 3f);
        float along = Jitter(rng, (onX ? d : w) * 0.35f);
        var pos = onX ? new Vector3(side * (w / 2 + 0.03f), y, along) : new Vector3(along, y, side * (d / 2 + 0.03f));
        var scale = onX ? new Vector3(0.06f, 1.0f, 0.9f) : new Vector3(0.9f, 1.0f, 0.06f);
        Block("Window", b, PrimitiveType.Cube, pos, scale, windowLit);
    }

    static void Tower(Transform root)
    {
        var t = new GameObject("Company Tower").transform;
        t.SetParent(root, false);
        Block("Shaft", t, PrimitiveType.Cube, new Vector3(0, 17, 0), new Vector3(9, 34, 9), tower);
        Block("Crown", t, PrimitiveType.Cube, new Vector3(0, 35, 0), new Vector3(6, 2, 6), tower);
        Block("Mast", t, PrimitiveType.Cylinder, new Vector3(0, 40, 0), new Vector3(0.3f, 4, 0.3f), tower);
        for (int row = 0; row < 9; row++)
            for (int col = 0; col < 3; col++)
                foreach (float s in new[] { -1f, 1f })
                {
                    if ((row * 7 + col * 3 + (s > 0 ? 1 : 0)) % 4 == 0) continue; // some floors dark
                    float y = 4 + row * 3.4f, a = (col - 1) * 2.6f;
                    Block("Glass", t, PrimitiveType.Cube, new Vector3(s * 4.52f, y, a), new Vector3(0.06f, 1.6f, 1.8f), towerGlass);
                    Block("Glass", t, PrimitiveType.Cube, new Vector3(a, y, s * 4.52f), new Vector3(1.8f, 1.6f, 0.06f), towerGlass);
                }
    }

    static void BuildCamera()
    {
        var go = new GameObject("Title Camera") { tag = "MainCamera" };
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("D4857E"); cam.fieldOfView = 46; cam.nearClipPlane = 0.3f; cam.farClipPlane = 3000;
        go.AddComponent<UniversalAdditionalCameraData>();
        go.AddComponent<AudioListener>();
        var drift = go.AddComponent<TitleCameraDrift>();
        drift.center = new Vector3(0, 18, 0);
        go.transform.position = new Vector3(0, drift.height, drift.radius); go.transform.LookAt(drift.center);
    }

    static void PutFirstInBuild()
    {
        var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != ScenePath) scenes.Add(s);
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
