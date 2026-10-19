using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Play-mode QA of the title menus (Docs/Multiplayer_Redesign.md 12.11: only 혼자 하기 / 서버 열기 / 참가하기 / 설정 are left)
/// and of the offline path. Uses profile slot 3 only, so slot 1 saves stay untouched. No server needed.
///   -executeMethod TitleFlowQA.Run          the main menu (no 옛 방식), settings, 혼자 하기 into the map and back to the title
///   -executeMethod TitleFlowQA.RunOffline   the game scene played directly (no session): citizens and traffic move, saves go to PC files
/// Optional env QA_SHOTS = folder: T_main.png, T_offline_citizens.png, T_offline_traffic.png.
/// Run WITHOUT -quit (the QA exits the editor itself).
/// </summary>
[InitializeOnLoad]
public static class TitleFlowQA
{
    const string Flag = "TitleFlowQA.mode";
    static int checks;

    static TitleFlowQA() => EditorApplication.playModeStateChanged += OnState;

    public static void Run() => Start("menu", TitleSceneBuilder.ScenePath);
    public static void RunOffline() => Start("offline", TitleFlow.GameScene);

    static void Start(string mode, string scene)
    {
        EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        SessionState.SetString(Flag, mode);
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange change)
    {
        string mode = SessionState.GetString(Flag, "");
        if (mode == "" || change != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.SetString(Flag, "");
        Drive(mode);
    }

    static async void Drive(string mode)
    {
        int original = PlayerPrefs.GetInt("Profile.Slot", 1);
        int code = 0;
        try
        {
            ProfileSlot.Set(3);
            Directory.Delete(Path.Combine(Application.persistentDataPath, "profile3"), true);
        }
        catch (DirectoryNotFoundException) { }
        try
        {
            if (mode == "menu") await Menu(); else await Offline();
            Debug.Log("[TitleFlowQA] RESULT " + mode + " passed, checks=" + checks);
        }
        catch (Exception e)
        {
            Debug.LogError("[TitleFlowQA] RESULT " + mode + " FAILED: " + e.Message + "\n" + e.StackTrace);
            code = 1;
        }
        try
        {
            if (SyncClient.Instance) SyncClient.Instance.Disconnect();
            if (SyncHost.Instance) SyncHost.Instance.Stop();
        }
        catch (Exception e) { Debug.LogWarning("[TitleFlowQA] cleanup: " + e.Message); }
        PlayerPrefs.DeleteKey("net.server_url.p3");
        PlayerPrefs.SetInt("Profile.Slot", original); // the in-memory slot stays 3 so the exit-time save lands in profile3
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    // ---- helpers ----

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("FAIL: " + what);
        checks++;
        Debug.Log("[TitleFlowQA] ok: " + what);
    }

    static async Task WaitFor(Func<bool> condition, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what + Where());
            await Awaitable.NextFrameAsync();
        }
    }

    // Where the title screen is when something times out: its name and all its texts.
    static string Where() => Screen ? " [screen=" + Screen.Screen + ": " + TextOf(Screen.Content).Replace('\n', '|') + "]" : "";

    static TitleScreen Screen => TitleScreen.Current;
    static bool OnTitle => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null;
    static bool InMap(string path) => SceneManager.GetActiveScene().path == path && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading;

    static async Task Click(string label)
    {
        await WaitFor(() => Screen && !Screen.Busy, 15, "screen not busy before clicking " + label);
        Check(Screen.Click(label), "clicked '" + label + "' on screen " + Screen.Screen);
        await Awaitable.NextFrameAsync();
    }

    static string TextOf(Transform root)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in root.GetComponentsInChildren<TMPro.TMP_Text>()) sb.Append(t.text).Append('\n');
        return sb.ToString();
    }

    static bool HasButton(string text)
    {
        if (!Screen) return false;
        foreach (var b in Screen.Content.GetComponentsInChildren<UnityEngine.UI.Button>())
        {
            var label = b.GetComponentInChildren<TMPro.TMP_Text>();
            if (label && label.text.Contains(text)) return true;
        }
        return false;
    }

    static async Task Shot(string file)
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (string.IsNullOrEmpty(dir) || !Camera.main) return;
        try
        {
            Directory.CreateDirectory(dir);
            await TitleShots.Snap(Camera.main, Path.Combine(dir, file));
        }
        catch (Exception e) { Debug.LogWarning("[TitleFlowQA] screenshot skipped: " + e.Message); }
    }

    // ---- the title menus ----

    static async Task Menu()
    {
        Check(OnTitle, "title scene is the starting scene");
        Check(PlayerSpawner.Player == null, "no player is spawned on the title");
        Check(ProfileSlot.Slot == 3, "QA runs in profile slot 3");
        await WaitFor(() => Screen && Screen.Screen == "main", 15, "main menu");
        foreach (string label in new[] { "혼자 하기", "서버 열기", "참가하기", "설정", "종료" })
            Check(HasButton(label), "main menu has '" + label + "'");
        foreach (string label in new[] { "옛 방식", "방 만들기", "연결 방식" })
            Check(!HasButton(label), "main menu has no '" + label + "'");
        Check(TextOf(Screen.transform).Contains("프로필 슬롯 3") && !TextOf(Screen.transform).Contains("Steam"), "footer: the profile slot only");
        Screen.UseCamera(Camera.main);
        await Shot("T_main.png");

        await CheckSettings();
        foreach (string message in new[] { "server_unreachable", "bad_address", "Password", "Full", "Version", "character_name_taken", "world_in_use", "load_failed" })
            Check(!TitleFlow.Message(message).StartsWith("알 수 없는"), "friendly message for " + message);

        // 혼자 하기: a new world on my in-app server for one, a character, the map, and back
        await Click("혼자 하기");
        Check(Screen.Screen == "worlds" && HasButton("새 월드 만들기"), "혼자 하기 lists the worlds");
        await Click("새 월드 만들기");
        await WaitFor(() => Screen && Screen.Screen == "name", 30, "the character screen");
        Screen.FirstInput().text = "QA혼자";
        await Click("확인");
        await WaitFor(() => InMap(TitleFlow.GameScene) && CharacterSync.Active, 90, "entering the game map");
        var client = SyncClient.Instance;
        Check(client.Connected && SyncHost.Instance && SyncHost.Instance.InApp, "혼자 하기: on my in-app server");
        Check(GameSession.IsAuthority && GameSession.LocalPlayerName == "QA혼자", "the owner of an in-app server is the authority; the session name is the character");
        PauseMenu.Instance.Open();
        var pause = PauseMenu.Instance.GetComponentInChildren<Canvas>().transform;
        Check(TextOf(pause).Contains("서버:") && !TextOf(pause).Contains("방"), "the pause menu shows the server, nothing of rooms");
        Check(TitleUi.ClickButton(pause, "서버 나가기"), "clicked 서버 나가기");
        await WaitFor(() => OnTitle, 60, "title scene again");
        await Awaitable.NextFrameAsync();
        Check(!client.Connected && !SyncHost.Instance, "left the server; the in-app server stopped");
        Check(!PlayerSpawner.Player || !PlayerSpawner.Player.gameObject.activeSelf, "player hidden on the title");
    }

    static async Task CheckSettings()
    {
        Screen.Open("settings");
        await Awaitable.NextFrameAsync();
        Check(Screen.Screen == "settings" && !TextOf(Screen.Content).Contains("연결 방식") && !TextOf(Screen.Content).Contains("Steam"), "settings: no connection mode, no Steam");
        Check(!HasButton("계정 초기화") && !HasButton("고급") && !TextOf(Screen.Content).Contains("서버 주소"), "settings: no account reset and no account server address (12.13: the account is EOS)");
        Check(TextOf(Screen.Content).Contains("EOS:"), "settings: shows the EOS state");
        Check(Screen.Click("뒤로") && Screen.Screen == "main", "back to the main menu");
    }

    // ---- offline: the game scene played directly ----

    static async Task Offline()
    {
        await WaitFor(() => InMap(TitleFlow.GameScene), 60, "the game map");
        Check(!SyncClient.Instance.Connected && !SyncHost.Instance, "no session: not on a server");
        Check(GameSession.IsAuthority && GameSession.Mode == SessionMode.Host, "offline: this PC is the authority");
        int moved = await NpcQa.CitizensMoved(8f);
        Check(moved > 0, "citizens walk offline (" + moved + " of " + CitizenSpawner.Current.Citizens.Count + " moved in 8 s)");
        await NpcQa.ShotNearest(false, "T_offline_citizens.png");

        Check(Path.GetDirectoryName(WorldSave.Path).Contains("profile3"), "the world file is this slot's (" + WorldSave.Path + ")");
        WorldSaveRunner.SaveNow();
        Check(File.Exists(WorldSave.Path), "the world is saved to the PC file " + WorldSave.Path);
        Check(JsonUtility.FromJson<WorldSaveData>(File.ReadAllText(WorldSave.Path)).worldId == WorldSave.WorldId, "the file holds this world");
        Check(PropertyRegistry.Instance.TryRegister("qa:offline-travel", GameSession.LocalPlayerName), "an offline change to the world (a registration)");

        const string business = "Assets/Scenes/Maps/BusinessDistrict.unity";
        Check(SceneLoadManager.TryLoadMap(business, SceneLoadManager.DefaultSpawnId), "travel to the traffic map");
        await WaitFor(() => InMap(business), 90, "BusinessDistrict");
        Check(File.ReadAllText(WorldSave.Path).Contains("qa:offline-travel"), "the map change saved the change to the world file (WorldStateSync → WorldSaveRunner.SaveNow)");
        int cars = await NpcQa.CarsMoved(10f);
        Check(cars > 0, "traffic drives offline (" + cars + " cars moved in 10 s)");
        moved = await NpcQa.CitizensMoved(8f);
        Check(moved > 0, "citizens walk on the traffic map too (" + moved + " moved)");
        await NpcQa.ShotNearest(true, "T_offline_traffic.png");
    }
}

/// <summary>Shared checks that the crowd and the traffic of the current map really move (12.11: every PC computes them).</summary>
static class NpcQa
{
    /// <summary>How many citizens of the current map moved more than 0.3 m within <paramref name="seconds"/>.</summary>
    public static async Task<int> CitizensMoved(float seconds)
    {
        float end = Time.realtimeSinceStartup + 20f;
        while (!(CitizenSpawner.Current && CitizenSpawner.Current.Citizens.Count > 0))
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for the citizens of " + SceneManager.GetActiveScene().path + " (host object: " + (GameObject.Find("Citizens (runtime)") != null) + ", spawner: " + (CitizenSpawner.Current != null) + ")");
            await Awaitable.NextFrameAsync();
        }
        var walkers = new List<CitizenWalker>(CitizenSpawner.Current.Citizens);
        var start = walkers.ConvertAll(w => w ? w.transform.position : Vector3.zero);
        await Awaitable.WaitForSecondsAsync(seconds);
        int moved = 0;
        for (int i = 0; i < walkers.Count; i++)
            if (walkers[i] && (walkers[i].transform.position - start[i]).sqrMagnitude > .09f) moved++;
        return moved;
    }

    /// <summary>How many NPC cars of the current map moved more than 1 m within <paramref name="seconds"/> (0 when the map has no traffic).</summary>
    public static async Task<int> CarsMoved(float seconds)
    {
        var spawner = UnityEngine.Object.FindAnyObjectByType<TrafficSpawner>();
        if (!spawner) return 0;
        float end = Time.realtimeSinceStartup + 20f;
        while (CountCars(spawner) == 0)
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for NPC cars");
            await Awaitable.NextFrameAsync();
        }
        var cars = new List<TrafficCar>(spawner.Cars);
        var start = cars.ConvertAll(c => c ? c.Position : Vector3.zero);
        await Awaitable.WaitForSecondsAsync(seconds);
        int moved = 0;
        for (int i = 0; i < cars.Count; i++)
            if (cars[i] && (cars[i].Position - start[i]).sqrMagnitude > 1f) moved++;
        return moved;
    }

    /// <summary>QA_SHOTS only: renders the citizen or NPC car nearest to the player that a temporary camera above and
    /// behind it can see without a wall in between (the game camera itself is often blocked on the hillside).</summary>
    public static async Task ShotNearest(bool car, string file)
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        var player = PlayerSpawner.Player;
        if (string.IsNullOrEmpty(dir) || !player) return;
        var targets = new List<Transform>();
        if (car) { var spawner = UnityEngine.Object.FindAnyObjectByType<TrafficSpawner>(); if (spawner) foreach (var c in spawner.Cars) if (c) targets.Add(c.transform); }
        else if (CitizenSpawner.Current) foreach (var w in CitizenSpawner.Current.Citizens) if (w) targets.Add(w.transform);
        Vector3 me = player.transform.position;
        targets.Sort((a, b) => (a.position - me).sqrMagnitude.CompareTo((b.position - me).sqrMagnitude));
        float back = car ? 10f : 4f, up = car ? 6f : 3f;
        foreach (var t in targets)
        {
            Vector3 aim = t.position + Vector3.up;
            foreach (Vector3 dir2 in new[] { -t.forward, t.forward, t.right, -t.right })
            {
                Vector3 from = aim + Vector3.ProjectOnPlane(dir2, Vector3.up).normalized * back + Vector3.up * up;
                if (Physics.Linecast(from, aim, out var hit) && !hit.transform.IsChildOf(t)) continue; // a wall or the hill in between
                var go = new GameObject("QA shot camera");
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 50f;
                go.transform.position = from;
                go.transform.LookAt(aim);
                try
                {
                    Directory.CreateDirectory(dir);
                    await TitleShots.Snap(cam, Path.Combine(dir, file));
                }
                catch (Exception e) { Debug.LogWarning("[NpcQa] screenshot skipped: " + e.Message); }
                UnityEngine.Object.Destroy(go);
                return;
            }
        }
        Debug.LogWarning("[NpcQa] no unblocked view for " + file);
    }

    static int CountCars(TrafficSpawner spawner)
    {
        int n = 0;
        foreach (var c in spawner.Cars) if (c) n++;
        return n;
    }
}
