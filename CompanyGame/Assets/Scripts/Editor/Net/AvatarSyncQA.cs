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
    const string EditorPlayFlag = "AvatarSyncQA.editorPlay";
    const int Slot = 4;
    const string Key = AvatarSync.PrefabKey;
    static int checks;
    static string editorWorld;
    public static bool EditorRunning { get; private set; }
    public static string EditorResult { get; private set; } = "not run";

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

    /// <summary>Runs the real TCP/SyncClient path and leaves this editor running, with the previous profile restored.</summary>
    public static void StartEditorPlayTest()
    {
        if (EditorRunning || SyncHost.Instance) throw new InvalidOperationException("Stop the existing QA/server before starting AvatarSyncQA.");
        if (EditorApplication.isPlaying) { DriveEditorPlay(); return; }
        EditorSceneManager.OpenScene(TitleSceneBuilder.ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(EditorPlayFlag, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayState(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(EditorPlayFlag, false))
        {
            SessionState.SetBool(EditorPlayFlag, false);
            DriveEditorPlay();
            return;
        }
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PlayFlag, false)) return;
        SessionState.SetBool(PlayFlag, false);
        DrivePlay();
    }

    static async void DriveEditorPlay()
    {
        int original = ProfileSlot.Slot;
        bool hadProfilePreference = PlayerPrefs.HasKey("Profile.Slot");
        int oldProfilePreference = PlayerPrefs.GetInt("Profile.Slot", 1);
        string[] qaKeys = { "Session.LocalPlayerId.p" + Slot, "Session.AccountId.p" + Slot, "Session.AccountName.p" + Slot };
        bool[] hadQaKeys = Array.ConvertAll(qaKeys, PlayerPrefs.HasKey);
        string[] oldQaValues = Array.ConvertAll(qaKeys, key => PlayerPrefs.GetString(key, ""));
        string oldPersonalDir = PersonalSave.DirOverride, oldWorldPath = WorldSave.PathOverride;
        string oldPersonal = PersonalSave.Export(), oldWorld = WorldSave.ToJson(WorldSave.Capture());
        string oldInventory = InventoryManager.Instance ? InventorySnapshot.Export(InventoryManager.Instance.State) : null;
        string scratch = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CompanyGame-AvatarTcpQA-" + Guid.NewGuid().ToString("N")));
        PersonalSave.DirOverride = Path.Combine(scratch, "player");
        WorldSave.PathOverride = Path.Combine(scratch, "world.json");
        EditorRunning = true; EditorResult = "running"; editorWorld = null; checks = 0;
        try
        {
            editorWorld = await PlayBody();
            EditorResult = "passed, checks=" + checks;
            Debug.Log("[AvatarSyncQA] EDITOR RESULT " + EditorResult);
        }
        catch (Exception e)
        {
            EditorResult = "FAILED: " + e.Message;
            Debug.LogError("[AvatarSyncQA] EDITOR RESULT " + EditorResult + "\n" + e.StackTrace);
        }
        finally
        {
            try
            {
                if (SyncHost.Instance)
                {
                    editorWorld ??= SyncHost.Instance.Server.World.Folder;
                    SyncHost.Instance.Stop();
                }
                if (!string.IsNullOrEmpty(editorWorld))
                {
                    string allowed = Path.GetFullPath(ServerSettings.WorldsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string target = Path.GetFullPath(editorWorld);
                    if (target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && target.Length > allowed.Length && Directory.Exists(target))
                        Directory.Delete(target, true); // exact new world from this run, never the profile itself.
                }
            }
            catch (Exception e)
            {
                EditorResult += "; cleanup warning: " + e.Message;
                Debug.LogWarning("[AvatarSyncQA] " + EditorResult);
            }
            finally
            {
                // Server exit can restore its scratch baseline; put the original in-memory state back before lifting overrides.
                WorldSave.Restore(oldWorld);
                PersonalSave.Import(oldPersonal);
                if (oldInventory != null && InventoryManager.Instance) InventorySnapshot.Import(InventoryManager.Instance.State, oldInventory);
                PersonalSave.DirOverride = oldPersonalDir; WorldSave.PathOverride = oldWorldPath;
                for (int i = 0; i < qaKeys.Length; i++)
                    if (hadQaKeys[i]) PlayerPrefs.SetString(qaKeys[i], oldQaValues[i]); else PlayerPrefs.DeleteKey(qaKeys[i]);
                ProfileSlot.Set(original);
                if (hadProfilePreference) PlayerPrefs.SetInt("Profile.Slot", oldProfilePreference); else PlayerPrefs.DeleteKey("Profile.Slot");
                GameSession.ClearCharacter(); GameSession.ReloadAccount();
                PlayerPrefs.Save(); EditorRunning = false;
                string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (scratch.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(scratch).StartsWith("CompanyGame-AvatarTcpQA-", StringComparison.Ordinal) && Directory.Exists(scratch))
                    Directory.Delete(scratch, true);
            }
        }
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
        if (EditorRunning) editorWorld = world;
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
        var motor = player.GetComponent<CharacterController>();

        // A fresh scene's spawn is initially above the floor. Let the real controller finish falling and the
        // regular state stream reach the server before testing the spawn snapshot delivered to a joining peer.
        Vector3 settledPosition = player.transform.position;
        float groundedSince = Time.realtimeSinceStartup;
        await WaitFor(() =>
        {
            if (!motor.isGrounded || (player.transform.position - settledPosition).sqrMagnitude > .000025f)
            { settledPosition = player.transform.position; groundedSince = Time.realtimeSinceStartup; return false; }
            return Time.realtimeSinceStartup - groundedSince >= .5f &&
                host.Server.TryGetEntity(mine.Id, out _, out _, out _, out _, out var blobs) && blobs.Length >= 1 &&
                (SyncLayerQA.PosOf(blobs[0]) - player.transform.position).sqrMagnitude < .0004f;
        }, 12, "a grounded stable player and its current server transform");

        // ---- the second player ----
        var socket = new TcpClientTransport();
        await socket.Connect("127.0.0.1", host.Port);
        var fake = new QaPeer(socket);
        fake.Send(SyncProtocol.Hello("qa-avatar-fake", "<b>보이면안됨</b>", map));
        fake.number = (await WaitMsg(fake, m => m.kind == ToClient.Welcome, "fake welcome")).number;
        fake.Send(SyncProtocol.CharacterCreate("QA영희", "female"));
        Check((await WaitMsg(fake, m => m.kind == ToClient.CharacterResult, "fake character")).result == CharacterResult.Ok, "the fake second player made QA영희");
        var fakeBag = ServerWorld.Instance.Ledger.Bag("qa-avatar-fake");
        var fakeSlot = fakeBag.GetSlot(InventoryState.HotbarSize - 1);
        if (!fakeSlot.IsEmpty) fakeBag.TryRemove(InventoryState.HotbarSize - 1, fakeSlot.Count, out _);

        // reverse direction: the fake player gets the Unity player's avatar
        var got = await WaitMsg(fake, m => m.kind == ToClient.Spawn && m.id == mine.Id, "the Unity avatar at the fake client");
        Vector3 receivedPosition = got.blobs.Length > 0 ? SyncLayerQA.PosOf(got.blobs[0]) : Vector3.positiveInfinity;
        Check(got.key == Key && got.owner == client.MyConnection && got.blobs.Length == 2 && (got.flags & EntityFlags.FollowsOwnerMap) != 0
              && (receivedPosition - player.transform.position).magnitude < .1f,
            "the fake client got the Unity avatar: key, owner, 2 components, at the player (received=" + receivedPosition.ToString("F3") +
            ", current=" + player.transform.position.ToString("F3") + ", settled=" + settledPosition.ToString("F3") + ", grounded=" + motor.isGrounded + ")");
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
        Check(!avatar.transform.Find("FemaleVisual").gameObject.activeSelf && Array.TrueForAll(avatar.GetComponentsInChildren<Collider>(), c => !c.enabled), "in a car the body hides and its combat collider is disabled");
        await Shot("A_car.png");
        fake.Send(SyncProtocol.State(id, 1, StateBlob(true, "", 0f, false, "")));
        await WaitFor(() => !HasChild(avatar, car.vehiclePrefab.name) && avatar.transform.Find("FemaleVisual").gameObject.activeSelf, 5, "on foot again");

        // punch
        var motion = avatar.transform.Find("FemaleVisual").GetComponentInChildren<DaldongneAvatarMotion>();
        var before = motion.leftArm.localRotation;
        void Punch(uint sequence) => fake.Send(SyncProtocol.Event(id, 1, AvatarState.AttackIntentEvent, EventTarget.Server,
            AttackBlob(sequence, side)));
        Punch(1);
        float swing = 0f, end = Time.realtimeSinceStartup + 1f;
        while (Time.realtimeSinceStartup < end) { swing = Mathf.Max(swing, Quaternion.Angle(before, motion.leftArm.localRotation)); await Awaitable.NextFrameAsync(); }
        Check(swing > 30f, "the authenticated swing event moves the jab arm (" + (int)swing + "°)");
        await Awaitable.WaitForSecondsAsync(.5f);
        Punch(2);
        await WaitFor(() => Quaternion.Angle(before, motion.leftArm.localRotation) > 40f, 2, "the second swing");
        await Shot("A_punch.png", 0f);

        // sitting
        await WaitFor(() => string.IsNullOrEmpty(motion.CombatPoseState), 3, "the swing to recover before seating");
        var remoteView = avatar.GetComponent<RemoteAvatarView>();
        float StandingHipHeight() => motion.transform.InverseTransformPoint(motion.hips.position).y;
        float standingHip = StandingHipHeight();
        Quaternion standingThigh = motion.leftLeg.localRotation, standingKnee = motion.leftKnee.localRotation;
        fake.Send(SyncProtocol.State(id, 1, StateBlob(true, "", 0f, true, "")));
        await WaitFor(() => remoteView.seated && Mathf.Abs(StandingHipHeight() - .54f) < .015f &&
            standingHip - StandingHipHeight() > .1f && Quaternion.Angle(standingThigh, motion.leftLeg.localRotation) > 60f &&
            Quaternion.Angle(standingKnee, motion.leftKnee.localRotation) > 60f, 3, "the seated state and root-relative pelvis/leg pose");
        Check(true, "seated: the shared seat pose lowers the pelvis in avatar space and bends thigh/knee (standing=" +
            standingHip.ToString("F3") + ", seated=" + StandingHipHeight().ToString("F3") + ")");
        await Shot("A_seated.png");
        fake.Send(SyncProtocol.State(id, 1, StateBlob(true, "", 0f, false, "")));
        await WaitFor(() => !remoteView.seated && Mathf.Abs(StandingHipHeight() - standingHip) < .03f &&
            Quaternion.Angle(standingThigh, motion.leftLeg.localRotation) < 15f &&
            Quaternion.Angle(standingKnee, motion.leftKnee.localRotation) < 15f, 3, "standing pelvis and legs restored after seating");

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
        WorldStateSync.FlushBooks();
        await WaitFor(() => ServerWorld.Instance.Ledger.Bag(host.Server.AccountOf(client.MyConnection)).GetSlot(hand).IsEmpty,
            5, "the existing inventory layout operation to empty the server hotbar");
        bool punched = false;
        combat.AttackPerformed += _ => punched = true;
        await WaitFor(() => combat.CanAttack, 3, "the player able to attack");
        Check(combat.TryAttack() && punched, "the Unity player punched");
        var punch = await WaitMsg(fake, m => m.kind == ToClient.Event && m.id == mine.Id && m.eventId == AvatarState.SwingEvent, "the Unity punch at the fake");
        Check(punch.component == 1 && punch.sender == 0, "the fake client got the server-authenticated swing of the Unity avatar");

        // Real TCP peer -> ServerCombat -> SyncClient -> AvatarState -> local stats/reaction, and observer hit-stop.
        await Awaitable.WaitForSecondsAsync(1f);
        var collision = new ServerCombatCollision();
        Vector3 targetPosition = player.transform.position, attackSpot = targetPosition;
        bool clearSpot = false;
        for (int i = 0; i < 8; i++)
        {
            Vector3 offset = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward * .8f;
            var candidate = targetPosition + offset;
            if (!collision.TryCheck(map, candidate + Vector3.up * 1.3f, targetPosition + Vector3.up * 1.3f, .15f, out bool clear) || !clear) continue;
            attackSpot = candidate; clearSpot = true; break;
        }
        collision.Dispose();
        Check(clearSpot, "a clear real-map lane exists for the TCP contact check");
        Vector3 strikeDirection = (targetPosition - attackSpot).normalized;
        fake.Send(SyncProtocol.State(id, 0, SyncLayerQA.Pos(attackSpot, Quaternion.LookRotation(strikeDirection).eulerAngles.y)));
        await WaitFor(() => (avatar.transform.position - attackSpot).magnitude < .08f, 3, "the attacker's avatar in range");
        var stats = player.GetComponent<PlayerStats>();
        var reaction = player.GetComponent<PlayerHitReaction>();
        float beforeHealth = stats.Health;
        Vector3 beforePosition = player.transform.position;
        fake.inbox.Clear();
        fake.Send(SyncProtocol.Event(id, 1, AvatarState.AttackIntentEvent, EventTarget.Server, AttackBlob(3, strikeDirection)));
        bool sawReaction = false, sawRemoteStop = false, sawRemoteSwingAudio = false;
        float deadline = Time.realtimeSinceStartup + 5f;
        var frameDelta = typeof(RemoteAvatarView).GetField("combatFrameDelta", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        while (Time.realtimeSinceStartup < deadline)
        {
            sawReaction |= reaction && reaction.BlocksActions;
            var remoteAudio = avatar.GetComponent<AudioSource>();
            sawRemoteSwingAudio |= remoteAudio && remoteAudio.isPlaying && stats.Health == beforeHealth;
            sawRemoteStop |= stats.Health < beforeHealth && frameDelta != null && (float)frameDelta.GetValue(avatar.GetComponent<RemoteAvatarView>()) == 0f;
            if (stats.Health < beforeHealth && sawReaction && sawRemoteStop && (player.transform.position - beforePosition).magnitude > .03f) break;
            await Awaitable.NextFrameAsync();
        }
        Check(stats.Health < beforeHealth && Mathf.Abs(ServerWorld.Instance.Combat.HealthOf(host.Server.AccountOf(client.MyConnection)) - stats.Health) < .001f,
            "TCP contact updates the local PlayerStats to the server's exact health");
        Check(sawReaction, "authenticated TCP contact enters local hit stun");
        Check((player.transform.position - beforePosition).magnitude > .03f, "authenticated TCP contact produces collision-limited knockback");
        Check(sawRemoteStop, "confirmed hit stops the remote attacker's pose clock on the observer");
        Check(sawRemoteSwingAudio, "accepted TCP swing plays the remote attacker's spatial audio");
        var confirmed = await WaitMsg(fake, m => m.kind == ToClient.Event && m.id == id && m.eventId == AvatarState.ResultEvent,
            "the fake attacker's authenticated hit result");
        using (var resultReader = SyncProtocol.Reader(confirmed.blob))
            Check(confirmed.sender == 0 && resultReader.ReadUInt32() == 3 && resultReader.ReadBoolean(), "attacker receives a server-authenticated positive contact result");

        // A second TCP actor hits the first while Unity observes the remote victim's impact audio.
        var observerSocket = new TcpClientTransport();
        await observerSocket.Connect("127.0.0.1", host.Port);
        var aggressor = new QaPeer(observerSocket);
        aggressor.Send(SyncProtocol.Hello("qa-avatar-aggressor", "QA공격자", map));
        aggressor.number = (await WaitMsg(aggressor, m => m.kind == ToClient.Welcome, "second TCP attacker welcome")).number;
        aggressor.Send(SyncProtocol.CharacterCreate("QA공격자", "male"));
        await WaitMsg(aggressor, m => m.kind == ToClient.CharacterResult && m.result == CharacterResult.Ok, "second TCP attacker character");
        var aggressorBag = ServerWorld.Instance.Ledger.Bag("qa-avatar-aggressor");
        var aggressorSlot = aggressorBag.GetSlot(InventoryState.HotbarSize - 1);
        if (!aggressorSlot.IsEmpty) aggressorBag.TryRemove(InventoryState.HotbarSize - 1, aggressorSlot.Count, out _);
        ulong aggressorId = SyncProtocol.MakeId(aggressor.number, 1);
        Vector3 aggressorSpot = attackSpot - strikeDirection * .8f;
        aggressor.Send(SyncProtocol.Spawn(aggressorId, Key, map, EntityFlags.FollowsOwnerMap,
            new[] { SyncLayerQA.Pos(aggressorSpot, Quaternion.LookRotation(strikeDirection).eulerAngles.y), StateBlob(false, "", 0f, false, "") }));
        await WaitFor(() => client.Find(aggressorId), 3, "second TCP attacker avatar");
        await Awaitable.WaitForSecondsAsync(.5f); // the victim's earlier swing sound has ended.
        float remoteHealth = ServerWorld.Instance.Combat.HealthOf("qa-avatar-fake");
        aggressor.Send(SyncProtocol.Event(aggressorId, 1, AvatarState.AttackIntentEvent, EventTarget.Server, AttackBlob(1, strikeDirection)));
        bool sawRemoteImpactAudio = false;
        deadline = Time.realtimeSinceStartup + 4f;
        while (Time.realtimeSinceStartup < deadline)
        {
            var remoteAudio = avatar.GetComponent<AudioSource>();
            sawRemoteImpactAudio |= ServerWorld.Instance.Combat.HealthOf("qa-avatar-fake") < remoteHealth && remoteAudio && remoteAudio.isPlaying;
            if (sawRemoteImpactAudio) break;
            await Awaitable.NextFrameAsync();
        }
        Check(sawRemoteImpactAudio, "TCP damage plays spatial impact audio on the remote victim seen by Unity");
        observerSocket.Close();

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

    static byte[] AttackBlob(uint sequence, Vector3 direction)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(sequence); writer.Write((byte)0); writer.Write(false); writer.Write((byte)(InventoryState.HotbarSize - 1));
        writer.Write(direction.x); writer.Write(direction.y); writer.Write(direction.z);
        writer.Flush(); return stream.ToArray();
    }

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
