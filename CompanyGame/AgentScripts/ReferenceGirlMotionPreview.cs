using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompanyGame.Daldongne;
using CompanyGame.Editor.Characters;

// Run in Edit Mode with ReferenceGirlMotionPreview.Capture. The isolated studio
// advances Pose explicitly, so every frame represents exactly 1/24 second.
public static class ReferenceGirlMotionPreview
{
    const int Fps = 24;
    const int Frames = 48;
    const int ViewWidth = 480;
    const int Height = 640;
    static string Output => Path.GetFullPath("../ArtSource/Daldongne/Characters/MotionPreview");

    static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = scale;
        box.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
        return box;
    }

    static void RenderFrame(Camera camera, RenderTexture target, Texture2D frame, string path)
    {
        var oldActive = RenderTexture.active;
        var oldTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            // Left: front three-quarter; right: profile. The floor/grid remain
            // fixed to make foot lift, ground penetration and body sway visible.
            var viewpoints = new[] { new Vector3(3f, 1.55f, 5f), new Vector3(5f, 1.22f, 0f) };
            for (int view = 0; view < viewpoints.Length; view++)
            {
                camera.transform.position = viewpoints[view];
                camera.transform.LookAt(new Vector3(0, .91f, 0));
                camera.Render();
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0, 0, ViewWidth, Height), view * ViewWidth, 0, false);
            }
            frame.Apply(false, false);
            File.WriteAllBytes(path, frame.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
        }
    }

    static void CaptureCycle(GameObject prefab, Transform root, Camera camera,
        RenderTexture target, Texture2D frame, string name, float speed)
    {
        string directory = Path.Combine(Output, name);
        Directory.CreateDirectory(directory);
        var girl = UnityEngine.Object.Instantiate(prefab, root);
        try
        {
            girl.transform.localPosition = Vector3.zero;
            girl.transform.localRotation = Quaternion.identity;
            girl.SetActive(true);
            var motion = girl.GetComponent<DaldongneAvatarMotion>();
            if (!motion) throw new InvalidOperationException("FemaleVisual has no DaldongneAvatarMotion.");
            motion.enabled = false;
            const float dt = 1f / Fps;
            // Settle the start blend for half a second before saving frames.
            for (int warmup = 0; warmup < Fps / 2; warmup++) motion.Pose(speed, dt);
            for (int index = 0; index < Frames; index++)
            {
                motion.Pose(speed, dt);
                RenderFrame(camera, target, frame, Path.Combine(directory, "frame_" + index.ToString("D3") + ".png"));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(girl); }
    }

    public static object Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Use Edit Mode for deterministic motion preview.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceGirlImporter.VisualPath);
        if (!prefab) throw new InvalidOperationException("FemaleVisual prefab is missing.");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("URP Lit shader is missing.");

        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Reference girl deterministic gait studio");
        SceneManager.MoveGameObjectToScene(root, scene);
        var floorMaterial = new Material(shader);
        floorMaterial.SetColor("_BaseColor", new Color(.22f, .26f, .25f));
        floorMaterial.SetFloat("_Smoothness", 0);
        var gridMaterial = new Material(shader);
        gridMaterial.SetColor("_BaseColor", new Color(.30f, .35f, .33f));
        gridMaterial.SetFloat("_Smoothness", 0);
        var target = RenderTexture.GetTemporary(ViewWidth, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var frame = new Texture2D(ViewWidth * 2, Height, TextureFormat.RGB24, false);
        try
        {
            Box(root.transform, "Floor", new Vector3(0, -.025f, 0), new Vector3(12, .05f, 12), floorMaterial);
            for (int i = -12; i <= 12; i++)
            {
                float offset = i * .5f;
                Box(root.transform, "Grid X " + i, new Vector3(offset, .001f, 0), new Vector3(.004f, .001f, 12), gridMaterial);
                Box(root.transform, "Grid Z " + i, new Vector3(0, .001f, offset), new Vector3(12, .001f, .004f), gridMaterial);
            }
            var key = new GameObject("Soft key").AddComponent<Light>();
            key.transform.SetParent(root.transform, false);
            key.type = LightType.Directional;
            key.intensity = 1.8f;
            key.color = new Color(1, .94f, .84f);
            key.transform.rotation = Quaternion.Euler(40, 155, 0);
            key.shadows = LightShadows.Soft;
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.transform.SetParent(root.transform, false);
            fill.type = LightType.Directional;
            fill.intensity = .75f;
            fill.color = new Color(.79f, .88f, 1);
            fill.transform.rotation = Quaternion.Euler(25, -50, 0);
            fill.shadows = LightShadows.None;
            var camera = new GameObject("Gait camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.scene = scene;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1.08f;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 30;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.22f, .26f, .25f);
            CaptureCycle(prefab, root.transform, camera, target, frame, "Walk", 3f);
            CaptureCycle(prefab, root.transform, camera, target, frame, "Run", 4.5f);
            return new
            {
                passed = true,
                fps = Fps,
                framesPerSequence = Frames,
                secondsPerSequence = (float)Frames / Fps,
                warmupSeconds = .5f,
                width = ViewWidth * 2,
                height = Height,
                walk = new { speed = 3f, path = Path.Combine(Output, "Walk", "frame_%03d.png") },
                run = new { speed = 4.5f, path = Path.Combine(Output, "Run", "frame_%03d.png") }
            };
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(frame);
            UnityEngine.Object.DestroyImmediate(floorMaterial);
            UnityEngine.Object.DestroyImmediate(gridMaterial);
        }
    }
}
