using System;
using System.IO;
using System.Threading.Tasks;
using CompanyGame.Daldongne;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Play-mode QA of what one player sees of another, against a running test server (default http://localhost:3000, env QA_SERVER).
/// The editor hosts a room, a raw client joins and sends pos/op messages; checks the remote avatar's name label, look,
/// ridden car and punch, and the placed-car ops (put / ride hides it / park shows it). Uses profile slot 5.
///   -executeMethod RemoteAvatarQA.Run   (WITHOUT -quit; the QA exits the editor). Env QA_SHOTS = folder for screenshots.
/// </summary>
[InitializeOnLoad]
public static class RemoteAvatarQA
{
    const string Flag = "RemoteAvatarQA.run";
    static int checks;

    static RemoteAvatarQA() => EditorApplication.playModeStateChanged += OnState;

    public static void Run()
    {
        EditorSceneManager.OpenScene(TitleSceneBuilder.ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Flag, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Flag, false)) return;
        SessionState.SetBool(Flag, false);
        Drive();
    }

    static async void Drive()
    {
        int original = PlayerPrefs.GetInt("Profile.Slot", 1);
        int code = 0;
        try { await Body(); Debug.Log("[RemoteAvatarQA] RESULT passed, checks=" + checks); }
        catch (Exception e) { Debug.LogError("[RemoteAvatarQA] RESULT FAILED: " + e.Message + "\n" + e.StackTrace); code = 1; }
        foreach (string key in new[] { "Session.AccountId", "Session.AccountName", "Session.LocalPlayerId", "auth_token", "net.server_url", "net.accounts" })
            PlayerPrefs.DeleteKey(key + ".p5");
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    static async Task Body()
    {
        string server = Environment.GetEnvironmentVariable("QA_SERVER") ?? "http://localhost:3000";
        ProfileSlot.Set(5);
        LocalAccount.ServerUrl = server;
        Check(await TitleFlow.SignIn(null, null) == null, "host signed in (generated account)");
        Check(await TitleFlow.Connect() == null, "host connected");
        Check(TitleFlow.PrepareWorld(true) == null, "host prepared a new world");
        Check(await TitleFlow.LoadCharacter() == null && !GameSession.HasCharacter, "a new world has no character yet");
        Check(TitleFlow.CreateCharacter("QA방장", "male"), "host character made");
        Check(await TitleFlow.HostOnline("QA방") == null, "room created");
        Check(TitleFlow.EnterGame(), "map load started");
        await WaitFor(() => SceneManager.GetActiveScene().path == TitleFlow.GameScene && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading, 90, "game map");
        if (RoomCodeCard.Instance) RoomCodeCard.Instance.Hide();
        var net = NetSession.Instance;

        using var guest = new QaRawClient(server);
        await guest.Register("QA원격" + UnityEngine.Random.Range(100, 999));
        await guest.Connect();
        Check((await guest.JoinRoom(net.RoomId)).t == "joined", "raw guest joined the room");

        var player = PlayerSpawner.Player.transform;
        var camera = Camera.main;
        var ahead = Vector3.ProjectOnPlane(camera ? camera.transform.forward : player.forward, Vector3.up).normalized;
        Vector3 spot = player.position + ahead * 4f;
        string map = SceneLoadManager.CurrentMap.path;
        float faceUs = Quaternion.LookRotation(-ahead).eulerAngles.y;
        int act = 0;
        Task Pos(string veh) => guest.Say(new NetMsg { t = "pos", map = map, x = spot.x, y = spot.y, z = spot.z, ry = faceUs, nm = "원격영희", g = 1, veh = veh, act = act });

        await Pos("");
        GameObject avatar = null;
        await WaitFor(() => (avatar = GameObject.Find("RemotePlayer(Clone)")) && avatar.activeInHierarchy, 10, "remote avatar");
        var tag = avatar.GetComponent<NameTag>();
        await WaitFor(() => tag && tag.Text == "원격영희", 5, "name tag text");
        Check(NameTag.Font && NameTag.Font.HasCharacter('영'), "the name tag font has Hangul");
        Check(PlayerSpawner.Player.GetComponent<NameTag>().Text == "QA방장", "the local tag shows the character, not the account nickname");
        Check(avatar.transform.Find("FemaleVisual").gameObject.activeSelf && !avatar.transform.Find("MaleVisual").gameObject.activeSelf, "female look shown");
        await Shot("R_remote_onfoot.png");

        var motion = avatar.transform.Find("FemaleVisual").GetComponentInChildren<DaldongneAvatarMotion>();
        Check(motion && motion.rightArm, "remote avatar has an animated right arm");
        var before = motion.rightArm.localRotation;
        act++;
        await Pos("");
        float swing = 0f;
        float end = Time.realtimeSinceStartup + 1.5f;
        while (Time.realtimeSinceStartup < end) { swing = Mathf.Max(swing, Quaternion.Angle(before, motion.rightArm.localRotation)); await Awaitable.NextFrameAsync(); }
        Check(swing > 30f, "an attack counter change swings the remote arm (" + (int)swing + "°)");

        var car = Resources.LoadAll<ItemData>("Inventory/Vehicles/Cars")[0];
        await Pos(car.itemId);
        await WaitFor(() => avatar.transform.childCount > 0 && Array.Exists(avatar.GetComponentsInChildren<Transform>(), t => t.name.StartsWith(car.vehiclePrefab.name)), 5, "remote car model");
        Check(!avatar.transform.Find("FemaleVisual").gameObject.activeSelf, "the body hides in a car");
        Check(avatar.GetComponentsInChildren<Collider>().Length == 0, "the remote car has no colliders");
        await Shot("R_remote_car.png");
        await Pos("");
        await WaitFor(() => !Array.Exists(avatar.GetComponentsInChildren<Transform>(), t => t.name.StartsWith(car.vehiclePrefab.name)), 5, "remote car gone on foot");

        // a car the guest places is in this world too; their ride hides it here, their park brings it back
        var placed = new PlacedVehicleSave { id = "qaveh" + act, item = car.itemId, inst = Guid.NewGuid().ToString("N"), scene = map, pos = spot + ahead * 3f, ry = 0f };
        await guest.Say(new NetMsg { t = "op", ev = "veh.put", data = JsonUtility.ToJson(placed) });
        WorldDroppedItem drop = null;
        await WaitFor(() => drop = Array.Find(UnityEngine.Object.FindObjectsByType<WorldDroppedItem>(), d => d.DropId == placed.id), 5, "the guest's placed car here");
        Check(drop.IsPlacedVehicle && !drop.IsOccupied, "placed car is free");
        await Shot("R_placed_car.png");
        await guest.Say(new NetMsg { t = "op", ev = "veh.ride", data = JsonUtility.ToJson(placed) });
        await WaitFor(() => drop.IsOccupied, 5, "car taken by the guest");
        Check(!drop.transform.Find("PlacedVehicleModel").gameObject.activeSelf, "the parked copy hides while the guest drives it");
        placed.pos += ahead * 2f;
        await guest.Say(new NetMsg { t = "op", ev = "veh.park", data = JsonUtility.ToJson(placed) });
        await WaitFor(() => !drop.IsOccupied, 5, "car parked again");
        Check(drop.transform.Find("PlacedVehicleModel").gameObject.activeSelf && (drop.transform.position - placed.pos).sqrMagnitude < .01f, "parked where the guest left it, visible again");

        net.Leave();
        await Awaitable.NextFrameAsync();
    }

    static async Task Shot(string file)
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (string.IsNullOrEmpty(dir) || !Camera.main) return;
        Directory.CreateDirectory(dir);
        await Awaitable.WaitForSecondsAsync(.5f);
        await TitleShots.Snap(Camera.main, Path.Combine(dir, file));
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("FAIL: " + what);
        checks++;
        Debug.Log("[RemoteAvatarQA] ok: " + what);
    }

    static async Task WaitFor(Func<bool> condition, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what);
            await Awaitable.NextFrameAsync();
        }
    }
}
