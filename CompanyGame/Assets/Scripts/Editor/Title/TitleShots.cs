using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the title screen and each submenu to PNG through the title camera (Camera.Render into a RenderTexture),
/// Overlay canvases (clock HUD, phone, chat) are not drawn by Camera.Render; the screen hides them anyway. Needs graphics (no -nographics).
///   -executeMethod TitleShots.Run   (env QA_SHOTS = output folder; no -quit)
/// Uses profile slot 3 like TitleFlowQA.
/// </summary>
[InitializeOnLoad]
public static class TitleShots
{
    const string Flag = "TitleShots.run";

    static TitleShots() => EditorApplication.playModeStateChanged += OnState;

    public static void Run()
    {
        EditorSceneManager.OpenScene(TitleSceneBuilder.ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Flag, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Flag, false) || change != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.SetBool(Flag, false);
        Drive();
    }

    static async void Drive()
    {
        int code = 0, original = PlayerPrefs.GetInt("Profile.Slot", 1);
        try { await Shoot(); Debug.Log("[TitleShots] done"); }
        catch (Exception e) { Debug.LogError("[TitleShots] FAILED " + e.Message + "\n" + e.StackTrace); code = 1; }
        foreach (string key in new[] { "Session.AccountId", "Session.AccountName", "Session.LocalPlayerId" })
            PlayerPrefs.DeleteKey(key + ".p3");
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    static async Task Shoot()
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS") ?? Path.Combine(Application.dataPath, "../../_temp/shots");
        Directory.CreateDirectory(dir);
        ProfileSlot.Set(3);
        await Awaitable.NextFrameAsync();
        var screen = TitleScreen.Current;
        var camera = Camera.main;
        screen.UseCamera(camera);
        foreach (string name in new[] { "main", "worlds", "serveropen", "serverjoin", "name", "settings", "error", "busy" })
        {
            screen.Open(name);
            await Snap(camera, Path.Combine(dir, "T_" + name.Replace('-', '_') + ".png"));
        }

        // the in-game ESC menu off a server (offline view), drawn over the same backdrop
        var titleCanvas = screen.GetComponentInChildren<Canvas>();
        titleCanvas.enabled = false;
        PauseMenu.Instance.Open();
        PauseMenu.Instance.UseCamera(camera);
        await Snap(camera, Path.Combine(dir, "T_pause_offline.png"));
        PauseMenu.Instance.Close();
        titleCanvas.enabled = true;
    }

    public static async Task Snap(Camera camera, string path)
    {
        await Awaitable.NextFrameAsync();
        await Awaitable.NextFrameAsync();
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);
        Debug.Log("[TitleShots] wrote " + path);
    }
}
