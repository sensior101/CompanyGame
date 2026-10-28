using System;
using System.IO;
using System.Threading.Tasks;
using CompanyGame.Daldongne;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using QaPeer = SyncLayerQA.QaPeer;
using QaMsg = SyncLayerQA.QaMsg;

/// <summary>
/// QA of the player avatar entity (Docs/Multiplayer_Redesign.md 12.7).
///   -executeMethod AvatarSyncQA.Run      in-memory server: Peer names are character names, renamed when one is made (with -quit)
///   -executeMethod AvatarSyncQA.RunPlay  play mode: in-app server + the Unity player + a fake TCP second player that sends
///                                        its avatar the way AvatarSync does (WITHOUT -quit; the QA exits the editor).
///                                        Env QA_SHOTS = folder for screenshots. Uses profile slot 4 and its own new world.
/// </summary>
[InitializeOnLoad]
public static class AvatarSyncQA
{
    const string PlayFlag = "AvatarSyncQA.play";
    const int Slot = 4;
    const string Key = AvatarSync.PrefabKey;
    static int checks;

    static AvatarSyncQA() => EditorApplication.playModeStateChanged += OnPlayState;

    // ================= in-memory =================

    public static void Run()
    {
        checks = 0;
        var loopback = new LoopbackTransport();
        var server = new GameServer(loopback, 8);
        server.Log += m => Debug.Log("[AvatarSyncQA] server: " + m);
        QaPeer Hello(string account, string name)
        {
            var p = new QaPeer(loopback.Connect());
            p.Send(SyncProtocol.Hello(account, name, "m1"));
            server.Tick();
            p.number = p.Take(m => m.kind == ToClient.Welcome).number;
            return p;
        }
        void Step(params QaPeer[] peers) { server.Tick(); foreach (var p in peers) p.Pump(); }

        var a = Hello("acc-a", "<b>클라이언트가 준 이름</b>");
        var b = Hello("acc-b", "B가 고른 이름");
        Step(a, b);
        Check(b.Has(m => m.kind == ToClient.Peer && m.joined && m.number == a.number && m.name == ""), "a player without a character is announced with no name, not the Hello's");
        Check(a.Has(m => m.kind == ToClient.Peer && m.joined && m.number == b.number && m.name == ""), "and the newcomer hears the same of who is there");
        a.inbox.Clear(); b.inbox.Clear();

        a.Send(SyncProtocol.CharacterCreate("QA알파", "male"));
        Step(a, b);
        Check(a.Has(m => m.kind == ToClient.CharacterResult && m.result == CharacterResult.Ok), "A made its character");
        Check(b.Take(m => m.kind == ToClient.Peer && m.joined && m.number == a.number)?.name == "QA알파", "making it sends A's Peer(in) again with the character name");
        Check(!a.Has(ToClient.Peer), "A is not told about itself");

        b.Send(SyncProtocol.CharacterCreate("qa알파", "female"));
        Step(a, b);
        Check(b.Has(m => m.kind == ToClient.CharacterResult && m.result == CharacterResult.NameTaken) && !a.Has(ToClient.Peer), "a refused name sends no Peer");
        b.Send(SyncProtocol.CharacterCreate("QA베타", "female"));
        Step(a, b);
        Check(a.Take(m => m.kind == ToClient.Peer && m.joined && m.number == b.number)?.name == "QA베타", "B's character name reaches A");

        a.client.Close();
        Step(b);
        b.inbox.Clear();
        var again = Hello("acc-a", "다른 이름");
        Step(again, b);
        Check(b.Take(m => m.kind == ToClient.Peer && m.joined && m.number == again.number)?.name == "QA알파", "coming back with a saved character: Peer(in) carries its name at once");
        Check(again.Take(m => m.kind == ToClient.Peer && m.joined && m.number == b.number)?.name == "QA베타", "the newcomer hears the others' character names");
        Check(RemoteAvatarView.CleanName("<color=red>가나다라마바사아자차카타파</color>") == "color=red가나다" && RemoteAvatarView.CleanName(null) == "",
            "names are shown without <> and cut at 12 characters");

        var blob = StateBlob(true, "car_x", 3.47f, true, "book");
        var read = ReadState(blob);
        Check(read.female && read.seated && read.vehicle == "car_x" && read.held == "book" && Mathf.Abs(read.speed - 3.5f) < .001f,
            "AvatarState write → read (" + blob.Length + " bytes)");
        Object.DestroyImmediate(read.gameObject);
        server.Stop();
        Debug.Log("[AvatarSyncQA] RESULT passed, checks=" + checks);
    }

    // ================= play mode =================

    public static void RunPlay()
    {
        EditorSceneManager.OpenScene(TitleSceneBuilder.ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PlayFlag, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayState(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PlayFlag, false)) return;
        SessionState.SetBool(PlayFlag, false);
        DrivePlay();
    }

    static async void DrivePlay()
    {
        int original = PlayerPrefs.GetInt("Profile.Slot", 1);
        int code = 0;
        checks = 0;
        string world = null;
        try { world = await PlayBody(); Debug.Log("[AvatarSyncQA] RESULT play passed, checks=" + checks); }
        catch (Exception e) { Debug.LogError("[AvatarSyncQA] RESULT play FAILED: " + e.Message + "\n" + e.StackTrace); code = 1; }
        try
        {
            if (SyncHost.Instance) { world ??= SyncHost.Instance.Server.World.Folder; SyncHost.Instance.Stop(); }
            if (world != null && Directory.Exists(world)) Directory.Delete(world, true); // the QA's world, not a player's
        }
        catch (Exception e) { Debug.LogWarning("[AvatarSyncQA] cleanup: " + e.Message); }
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    static async Task<string> PlayBody()
    {
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy, 15, "title");
        ProfileSlot.Set(Slot);
        var client = SyncClient.Instance;
        var host = SyncHost.StartInApp(new ServerSettings { name = "AvatarQA", maxPlayers = 4, port = 0 }, true);
        string world = host.Server.World.Folder;
        await WaitFor(() => client.Connected && client.CharacterKnown, 10, "the Unity client on the in-app server");
        Check(await TitleFlow.CreateServerCharacter("QA유니티", "male") == null, "the Unity player made its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active, 90, "the game map");
        NetEntity mine = null;
        await WaitFor(() => mine = AvatarSync.Mine, 5, "my avatar entity");
        Check(mine.IsMine && mine.PrefabKey == Key && (mine.Flags & EntityFlags.FollowsOwnerMap) != 0, "AvatarSync shared my avatar as " + Key + " with FollowsOwnerMap");
        Check(Array.TrueForAll(mine.GetComponentsInChildren<Renderer>(), r => !r.enabled || !r.gameObject.activeInHierarchy), "my own copy renders nothing here");
        string map = SceneLoadManager.CurrentMap.path;
        var player = PlayerSpawner.Player;

        // ---- the second player ----
        var socket = new TcpClientTransport();
        await socket.Connect("127.0.0.1", host.Port);
        var fake = new QaPeer(socket);
        fake.Send(SyncProtocol.Hello("qa-avatar-fake", "<b>보이면안됨</b>", map));
        fake.number = (await WaitMsg(fake, m => m.kind == ToClient.Welcome, "fake welcome")).number;
        fake.Send(SyncProtocol.CharacterCreate("QA영희", "female"));
        Check((await WaitMsg(fake, m => m.kind == ToClient.CharacterResult, "fake character")).result == CharacterResult.Ok, "the fake second player made QA영희");

        // reverse direction: the fake player gets the Unity player's avatar
        var got = await WaitMsg(fake, m => m.kind == ToClient.Spawn && m.id == mine.Id, "the Unity avatar at the fake client");
        Check(got.key == Key && got.owner == client.MyConnection && got.blobs.Length == 2 && (got.flags & EntityFlags.FollowsOwnerMap) != 0
              && (SyncLayerQA.PosOf(got.blobs[0]) - player.transform.position).magnitude < .1f,
            "the fake client got the Unity avatar: key, owner, 2 components, at the player (" + SyncLayerQA.PosOf(got.blobs[0]).ToString("F2") + ")");
        var gotState = ReadState(got.blobs[1]);
        Check(!gotState.female && gotState.vehicle == "" && !gotState.seated, "its AvatarState says male, on foot, standing");
        Object.DestroyImmediate(gotState.gameObject);

        var cam = Camera.main.transform;
        var ahead = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        var side = Vector3.Cross(Vector3.up, ahead);
        Vector3 spot = player.transform.position + ahead * 3f + side * 1.6f; // beside my own player and its tag, not behind
        float faceUs = Quaternion.LookRotation(-ahead).eulerAngles.y;
        ulong id = SyncProtocol.MakeId(fake.number, 1);
        fake.Send(SyncProtocol.Spawn(id, Key, map, EntityFlags.FollowsOwnerMap, new[] { SyncLayerQA.Pos(spot, faceUs), StateBlob(true, "", 0f, false, "") }));
        NetEntity remote = null;
        await WaitFor(() => remote = client.Find(id), 5, "the fake player's avatar");
        var avatar = remote.gameObject;
        Check(avatar.activeInHierarchy && avatar.GetComponent<RemoteAvatarView>() && !remote.IsMine && remote.Owner == fake.number
              && (avatar.transform.position - spot).magnitude < .01f, "the other player's avatar appeared where it stands");
        await WaitFor(() => avatar.transform.Find("FemaleVisual").gameObject.activeSelf, 2, "the female look");
        Check(!avatar.transform.Find("MaleVisual").gameObject.activeSelf, "female look");
        var tag = avatar.GetComponent<NameTag>();
        await WaitFor(() => tag || (tag = avatar.GetComponent<NameTag>()), 2, "the name tag");
        await WaitFor(() => tag.Text == "QA영희", 3, "name tag text");
        Check(AvatarSync.NameOf(fake.number) == "QA영희", "the name tag shows the server's character name, not the Hello name");
        await Shot("A_female.png");

        // walking across, 10 states a second like the real sender
        Vector3 from = spot - side * 2f, to = spot + side * 2f;
        float walkYaw = Quaternion.LookRotation(side).eulerAngles.y;
        for (int i = 0; i <= 15; i++)
        {
            fake.Send(SyncProtocol.State(id, 0, SyncLayerQA.Pos(Vector3.Lerp(from, to, i / 15f), walkYaw)));
            if (i == 9) _ = Shot("A_walking_nametag.png", 0f);
            await Awaitable.WaitForSecondsAsync(.1f);
        }
        await WaitFor(() => (avatar.transform.position - to).magnitude < .02f && Mathf.Abs(Mathf.DeltaAngle(avatar.transform.eulerAngles.y, walkYaw)) < 1f, 3, "the avatar to reach the last state");
        Check(true, "the avatar followed the walk to its last position and turn");
        fake.Send(SyncProtocol.State(id, 0, SyncLayerQA.Pos(spot, walkYaw))); // side on: the arm, the seat and the hand show best
        await WaitFor(() => (avatar.transform.position - spot).magnitude < .02f, 3, "the avatar back on its spot");

        // vehicle
        var car = Resources.LoadAll<ItemData>("Inventory/Vehicles/Cars")[0];
        fake.Send(SyncProtocol.State(id, 1, StateBlob(true, car.itemId, 0f, false, "")));
        await WaitFor(() => HasChild(avatar, car.vehiclePrefab.name), 5, "the car model");
        Check(!avatar.transform.Find("FemaleVisual").gameObject.activeSelf && avatar.GetComponentsInChildren<Collider>().Length == 0, "in a car the body hides, the car has no colliders");
        await Shot("A_car.png");
        fake.Send(SyncProtocol.State(id, 1, StateBlob(true, "", 0f, false, "")));
        await WaitFor(() => !HasChild(avatar, car.vehiclePrefab.name) && avatar.transform.Find("FemaleVisual").gameObject.activeSelf, 5, "on foot again");

        // punch
        var motion = avatar.transform.Find("FemaleVisual").GetComponentInChildren<DaldongneAvatarMotion>();
        var before = motion.rightArm.localRotation;
        fake.Send(SyncProtocol.Event(id, 1, AvatarState.PunchEvent, EventTarget.Map, new byte[0]));
        float swing = 0f, end = Time.realtimeSinceStartup + 1f;
        while (Time.realtimeSinceStartup < end) { swing = Mathf.Max(swing, Quaternion.Angle(before, motion.rightArm.localRotation)); await Awaitable.NextFrameAsync(); }
        Check(swing > 30f, "the Punch event swings the arm (" + (int)swing + "°)");
        await Awaitable.WaitForSecondsAsync(.5f);
        fake.Send(SyncProtocol.Event(id, 1, AvatarState.PunchEvent, EventTarget.Map, new byte[0]));
        await WaitFor(() => Quaternion.Angle(before, motion.rightArm.localRotation) > 40f, 2, "the second swing");
        await Shot("A_punch.png", 0f);

        // sitting
        fake.Send(SyncProtocol.State(id, 1, StateBlob(true, "", 0f, true, "")));
        await WaitFor(() => Mathf.Abs(motion.hips.localPosition.y - .54f) < .01f, 3, "the seated pose");
        Check(true, "seated: the same pose as a local player on a seat (hips at .54)");
        await Shot("A_seated.png");
        fake.Send(SyncProtocol.State(id, 1, StateBlob(true, "", 0f, false, "")));
        await WaitFor(() => Mathf.Abs(motion.hips.localPosition.y - .54f) > .05f, 3, "standing again");

        // held item
        ItemData held = null;
        foreach (var item in Resources.LoadAll<ItemData>("Inventory"))
            if (item && item.IsBook && InventorySnapshot.Find(item.itemId) == item) { held = item; break; }
        Check(held, "a book to hold (" + (held ? held.itemId : "none") + ")");
        fake.Send(SyncProtocol.State(id, 1, StateBlob(true, "", 0f, false, held.itemId)));
        Transform heldRoot = null;
        await WaitFor(() => (heldRoot = avatar.transform.Find("RightHandHeldItem")) && heldRoot.gameObject.activeInHierarchy && heldRoot.GetComponentsInChildren<Renderer>().Length > 0, 3, "the held item");
        Check(heldRoot.GetComponentsInChildren<Collider>().Length == 0, "the held book shows in the right hand (" + heldRoot.GetComponentsInChildren<Renderer>().Length + " renderers, no colliders)");
        await Shot("A_held.png");
        fake.Send(SyncProtocol.State(id, 1, StateBlob(true, "", 0f, false, "")));
        await WaitFor(() => !avatar.transform.Find("RightHandHeldItem"), 3, "the held item gone");

        // another map and back
        fake.inbox.Clear();
        fake.Send(SyncProtocol.Map("QA/Elsewhere"));
        await WaitFor(() => !client.Find(id) && !avatar, 5, "the avatar to go when its player leaves the map");
        Check(true, "the other player went to another map: the avatar is gone here");
        await WaitMsg(fake, m => m.kind == ToClient.Despawn && m.id == mine.Id, "the Unity avatar gone at the fake (other map)");
        fake.Send(SyncProtocol.Map(map));
        await WaitFor(() => remote = client.Find(id), 5, "the avatar back");
        avatar = remote.gameObject;
        await WaitFor(() => avatar.transform.Find("FemaleVisual") && avatar.transform.Find("FemaleVisual").gameObject.activeSelf, 2, "the female look again");
        Check((avatar.transform.position - spot).magnitude < .01f, "back on the same map: seen again where it stands, female");
        await WaitMsg(fake, m => m.kind == ToClient.Spawn && m.id == mine.Id, "the Unity avatar at the fake again");

        // reverse direction: the Unity player's moves, look and punch reach the fake player
        fake.inbox.Clear();
        await Awaitable.WaitForSecondsAsync(2f);
        fake.Pump();
        int still = Count(fake, mine.Id);
        Check(still == 0, "standing still for 2s: " + still + " States");

        var motor = player.GetComponent<CharacterController>();
        fake.inbox.Clear();
        float walkStart = Time.realtimeSinceStartup, settleAt = walkStart + 1f, walkEnd = walkStart + 4f;
        int settled = -1;
        while (Time.realtimeSinceStartup < walkEnd)
        {
            motor.Move(side * player.moveSpeed * Time.deltaTime);
            if (settled < 0 && Time.realtimeSinceStartup >= settleAt) { fake.Pump(); settled = Count(fake, mine.Id); }
            await Awaitable.NextFrameAsync();
        }
        fake.Pump();
        int walked = Count(fake, mine.Id);
        int transforms = fake.inbox.FindAll(m => m.kind == ToClient.State && m.id == mine.Id && m.component == 0).Count;
        float steady = (walked - settled) / 3f; // walkEnd - settleAt: the float subtraction gave 2.9999998 and 10.0000002 failed (12.15)
        Debug.Log($"[AvatarSyncQA] BANDWIDTH standing_2s={still} walking_4s={walked} (transform {transforms}, avatar state {walked - transforms}) first_1s={settled} steady_per_s={steady:F1} all_per_s={walked / 4f:F1}");
        Check(walked > 0 && steady <= 10f, "walking: " + steady.ToString("F1") + " States/s after the first second (" + walked + " in 4s, " + (walked - transforms) + " of them AvatarState)");
        var last = fake.inbox.FindLast(m => m.kind == ToClient.State && m.id == mine.Id && m.component == 0);
        Check((SyncLayerQA.PosOf(last.blob) - player.transform.position).magnitude < .5f, "the fake client follows the Unity player's walk");

        GameSession.SetCharacter("QA유니티", "female"); // CharacterSync puts the look on the player, AvatarSync shares it
        var look = await WaitMsg(fake, m => m.kind == ToClient.State && m.id == mine.Id && m.component == 1 && ReadFemale(m.blob), "the Unity avatar's female look at the fake");
        Check(look != null, "the fake client got the Unity player's AvatarState change (female)");
        GameSession.SetCharacter("QA유니티", "male");
        var combat = player.GetComponent<PlayerCombat>();
        var inventory = InventoryManager.Instance.State;
        // A new character's hotbar is full of vehicles (a selected vehicle is placed, food eaten): free the last slot, punch bare-handed.
        int hand = InventoryState.HotbarSize - 1, free = InventoryState.HotbarSize;
        while (free < inventory.Capacity - 1 && !inventory.GetSlot(free).IsEmpty) free++;
        Check(inventory.GetSlot(hand).IsEmpty || inventory.TryMove(hand, free, out _), "hotbar " + hand + " emptied for a bare-handed punch");
        inventory.SelectHotbar(hand);
        bool punched = false;
        combat.AttackPerformed += _ => punched = true;
        await WaitFor(() => combat.CanAttack, 3, "the player able to attack");
        Check(combat.TryAttack() && punched, "the Unity player punched");
        var punch = await WaitMsg(fake, m => m.kind == ToClient.Event && m.id == mine.Id, "the Unity punch at the fake");
        Check(punch.component == 1 && punch.eventId == AvatarState.PunchEvent && punch.sender == client.MyConnection, "the fake client got the Punch event of the Unity avatar");

        // leaving
        socket.Close();
        await WaitFor(() => !avatar, 5, "the avatar to go when its player leaves");
        Check(client.Find(id) == null && AvatarSync.NameOf(fake.number) == "", "the other player left: avatar and name gone");

        TitleFlow.ReturnToTitle();
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && !SyncHost.Instance, 60, "title after leaving");
        Check(!AvatarSync.Mine, "after leaving the server my avatar entity is gone");
        return world;
    }

    static int Count(QaPeer p, ulong id) => p.inbox.FindAll(m => m.kind == ToClient.State && m.id == id).Count;

    static bool HasChild(GameObject go, string prefix) => Array.Exists(go.GetComponentsInChildren<Transform>(), t => t.name.StartsWith(prefix));

    static bool InGame => SceneManager.GetActiveScene().path == TitleFlow.GameScene && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading;

    // The real codec of AvatarState, through a throwaway component.
    static byte[] StateBlob(bool female, string vehicle, float speed, bool seated, string held)
    {
        var state = new GameObject("AvatarSyncQA state").AddComponent<AvatarState>();
        state.female = female; state.vehicle = vehicle; state.speed = speed; state.seated = seated; state.held = held;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        state.Write(w);
        w.Flush();
        Object.DestroyImmediate(state.gameObject);
        return stream.ToArray();
    }

    static AvatarState ReadState(byte[] blob)
    {
        var state = new GameObject("AvatarSyncQA state").AddComponent<AvatarState>();
        using var r = SyncProtocol.Reader(blob);
        state.Read(r);
        return state;
    }

    static bool ReadFemale(byte[] blob)
    {
        var state = ReadState(blob);
        bool female = state.female;
        Object.DestroyImmediate(state.gameObject);
        return female;
    }

    static async Task<QaMsg> WaitMsg(QaPeer p, Func<QaMsg, bool> match, string what, float seconds = 5f)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (true)
        {
            var m = p.Take(match);
            if (m != null) return m;
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what);
            await Awaitable.NextFrameAsync();
        }
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

    static async Task Shot(string file, float settle = .5f)
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (string.IsNullOrEmpty(dir) || !Camera.main) return;
        Directory.CreateDirectory(dir);
        if (settle > 0f) await Awaitable.WaitForSecondsAsync(settle);
        await TitleShots.Snap(Camera.main, Path.Combine(dir, file));
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("[AvatarSyncQA] FAIL: " + what);
        checks++;
        Debug.Log("[AvatarSyncQA] ok: " + what);
    }
}
