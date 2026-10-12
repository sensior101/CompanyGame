using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the title screen and each submenu to PNG through the title camera (Camera.Render into a RenderTexture),
/// Overlay canvases (clock HUD, phone, chat) are not drawn by Camera.Render; the screen hides them anyway. Needs graphics (no -nographics) and the test server for the room list.
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
        foreach (string key in new[] { "Session.AccountId", "Session.AccountName", "Session.LocalPlayerId", "auth_token", "net.server_url", "net.accounts" })
            PlayerPrefs.DeleteKey(key + ".p3");
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    static async Task Shoot()
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS") ?? Path.Combine(Application.dataPath, "../../_temp/shots");
        Directory.CreateDirectory(dir);
        string server = Environment.GetEnvironmentVariable("QA_SERVER") ?? "http://localhost:3000";
        ProfileSlot.Set(3);
        LocalAccount.ServerUrl = server;
        TitleFlow.PrepareWorld(true); // so "create" shows an existing world
        await Awaitable.NextFrameAsync();
        var screen = TitleScreen.Current;
        var camera = Camera.main;
        screen.UseCamera(camera);
        foreach (string name in new[] { "main", "create", "confirm", "name", "settings", "settings-advanced", "error", "busy" })
        {
            screen.Open(name);
            await Snap(camera, Path.Combine(dir, "T_" + name.Replace('-', '_') + ".png"));
        }

        // the in-game ESC menu without a room (offline view), drawn over the same backdrop
        var titleCanvas = screen.GetComponentInChildren<Canvas>();
        titleCanvas.enabled = false;
        PauseMenu.Instance.Open();
        PauseMenu.Instance.UseCamera(camera);
        await Snap(camera, Path.Combine(dir, "T_pause_offline.png"));
        PauseMenu.Instance.Close();
        titleCanvas.enabled = true;

        // with a real server: join-by-code screens, the room list, then a real room for the code card and the ESC menu
        using (var host = new QaRawClient(server))
        {
            string tag = UnityEngine.Random.Range(100, 999).ToString();
            await host.Register("원격방장" + tag);
            await host.Connect();
            await host.CreateRoom("달동네 테스트방");
            using var host2 = new QaRawClient(server);
            await host2.Register("두번째" + tag);
            await host2.Connect();
            await host2.CreateRoom("방이 가득 찬 방");
            Check(await TitleFlow.SignIn("촬영용" + tag, "male"), "sign in");
            Check(await TitleFlow.Connect(), "connect");

            screen.Open("join");
            screen.FirstInput().text = "k7m-2qx"; // typed lowercase with a hyphen: the field shows K7M2QX
            await Snap(camera, Path.Combine(dir, "T_join.png"));
            screen.FirstInput().text = "zzzzzz";
            screen.Click("참가");
            await WaitFor(() => screen.Screen == "error");
            await Snap(camera, Path.Combine(dir, "T_join_notfound.png"));
            screen.Click("뒤로");

            screen.Open("rooms");
            await Task.Delay(1500);
            await Awaitable.NextFrameAsync();
            await Snap(camera, Path.Combine(dir, "T_rooms.png"));

            Check(await TitleFlow.HostOnline("촬영방"), "host a room");
            var net = NetSession.Instance;
            titleCanvas.enabled = false;
            RoomCodeCard.Instance.Show(net.RoomId);
            RoomCodeCard.Instance.UseCamera(camera);
            await Snap(camera, Path.Combine(dir, "T_create_code.png"));
            RoomCodeCard.Instance.Hide();
            PauseMenu.Instance.Open();
            PauseMenu.Instance.UseCamera(camera);
            await Snap(camera, Path.Combine(dir, "T_pause.png"));
            PauseMenu.Instance.Close();
            net.Leave();
            titleCanvas.enabled = true;
            TitleFlow.ResetAccount();
        }
    }

    static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 600 && !condition(); i++) await Awaitable.NextFrameAsync();
        if (!condition()) throw new Exception("TIMEOUT in TitleShots");
    }

    static void Check(string error, string what)
    {
        if (error != null) throw new Exception(what + " failed: " + error);
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
