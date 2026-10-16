using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using QaPeer = SyncLayerQA.QaPeer;
using QaMsg = SyncLayerQA.QaMsg;

/// <summary>
/// QA of placed vehicles and dropped items as entities (Docs/Multiplayer_Redesign.md 12.8).
///   -executeMethod DropSyncQA.Run      in memory (with -quit): Release rules, first Claim wins, a rider leaving, restart
///                                      from world.dat (ids, maps, states, connection numbers), a version 1 world.dat, the
///                                      limits answering Despawn, the 06:00 lost-and-found rule and its 7 days
///   -executeMethod DropSyncQA.RunPlay  play mode (WITHOUT -quit; the QA exits the editor): in-app server + the Unity player
///                                      + a fake TCP second player. Env QA_SHOTS = folder for D_*.png. Profile slot 4, its own world.
/// Each run prints the passed checks per item.
/// </summary>
[InitializeOnLoad]
public static class DropSyncQA
{
    const string PlayFlag = "DropSyncQA.play";
    const int Slot = 4;
    const string Key = DropState.PrefabKey;
    const EntityFlags Placed = EntityFlags.PersistOnLeave | EntityFlags.Claimable;
    const EntityFlags Dropped = Placed | EntityFlags.Expiring;

    static int checks;
    static string section = "";
    static readonly List<KeyValuePair<string, int>> sections = new List<KeyValuePair<string, int>>();

    static DropSyncQA() => EditorApplication.playModeStateChanged += OnPlayState;

    // ================= in memory =================

    static LoopbackTransport loopback;
    static GameServer server;
    static string root;

    public static void Run()
    {
        Begin();
        root = Path.Combine(Path.GetTempPath(), "DropSyncQA-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            Memory();
            Report("RESULT passed");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    static void Memory()
    {
        string w1 = WorldStore.NewId();
        StartServer(w1);
        var a = Join("acc-a", "QA가", "m1");
        var b = Join("acc-b", "QA나", "m1");
        var c = Join("acc-c", "QA다", "m2");
        Step(a, b, c);
        Clear(a, b, c);

        Section("Release");
        ulong car = Id(a, 1), onlyPersist = Id(a, 2), onlyClaim = Id(a, 3);
        a.Send(SyncProtocol.Spawn(car, Key, "m1", Placed, new[] { Blob(1) }));
        a.Send(SyncProtocol.Spawn(onlyPersist, Key, "m1", EntityFlags.PersistOnLeave, new[] { Blob(2) }));
        a.Send(SyncProtocol.Spawn(onlyClaim, Key, "m1", EntityFlags.Claimable, new[] { Blob(3) }));
        Step(a, b, c);
        Check(b.Take(ToClient.Spawn, car)?.owner == a.number, "B sees A's car, owned by A");
        b.Send(SyncProtocol.Release(car));
        a.Send(SyncProtocol.Release(onlyPersist));
        a.Send(SyncProtocol.Release(onlyClaim));
        Step(a, b, c);
        Check(!a.Has(ToClient.Owner) && !b.Has(ToClient.Owner), "Release by a non-owner, or of an entity without both PersistOnLeave and Claimable, is ignored");
        a.Send(SyncProtocol.Release(car));
        Step(a, b, c);
        Check(a.Take(ToClient.Owner, car)?.owner == 0 && b.Take(ToClient.Owner, car)?.owner == 0, "the owner's Release: everyone on the map, the sender too, gets Owner(0)");
        Check(!c.Has(ToClient.Owner), "another map hears nothing");
        a.Send(SyncProtocol.State(car, 0, Blob(9)));
        Step(a, b);
        Check(!b.Has(ToClient.State), "after the Release A's State is ignored");

        Section("Claim race");
        b.Send(SyncProtocol.Claim(car));
        a.Send(SyncProtocol.Claim(car));
        Step(a, b);
        Check(b.Take(ToClient.Owner, car)?.owner == b.number && a.Take(ToClient.Owner, car)?.owner == b.number, "two Claims in one Tick: the first (B) owns it, both hear B");
        a.inbox.Clear();
        b.Send(SyncProtocol.State(car, 0, Blob(7)));
        a.Send(SyncProtocol.State(car, 0, Blob(8)));
        Step(a, b);
        Check(a.Take(ToClient.State, car) is QaMsg s7 && s7.blob[0] == 7 && !b.Has(ToClient.State), "the winner's State passes, the loser's does not");

        Section("Rider leaves");
        b.client.Close();
        Step(a);
        Check(a.Take(ToClient.Owner, car)?.owner == 0, "B left while driving: the car stays, owned by the server");
        var d = Join("acc-d", "QA라", "m1");
        var seen = d.Take(ToClient.Spawn, car);
        Check(seen != null && seen.owner == 0 && seen.blobs[0][0] == 7, "a newcomer gets it with B's last state");
        a.Send(SyncProtocol.Claim(car));
        a.Send(SyncProtocol.Map("m2"));
        a.Send(SyncProtocol.Release(car));
        Step(a, c, d);
        Check(a.Last(ToClient.Owner, car)?.owner == 0 && d.Last(ToClient.Owner, car)?.owner == 0 && !c.Has(ToClient.Owner),
            "a Release from another map works: the car's map and the sender get Owner(0)");
        a.Send(SyncProtocol.Map("m1"));
        Step(a);

        Section("Restart");
        ulong m2Drop = Id(c, 1), passing = Id(c, 2);
        c.Send(SyncProtocol.Spawn(m2Drop, Key, "m2", Dropped, new[] { Blob(4, 5, 6) }));
        c.Send(SyncProtocol.Release(m2Drop));
        c.Send(SyncProtocol.Spawn(passing, "Net/Box", "m2", EntityFlags.None, new[] { Blob(1) }));
        Step(c);
        uint highest = d.number;
        server.Stop();
        StartServer(w1);
        var e = Join("acc-e", "QA마", "m1");
        var f = Join("acc-f", "QA바", "m2");
        Check(e.number > highest && f.number > e.number, "connection numbers go on after the old ones (" + highest + " → " + e.number + ")");
        var back = e.Take(ToClient.Spawn, car);
        Check(back != null && back.owner == 0 && back.key == Key && back.map == "m1" && back.flags == Placed && back.blobs[0][0] == 7,
            "the car is back: same id, map, flags and last state, owned by the server");
        var back2 = f.Take(ToClient.Spawn, m2Drop);
        Check(back2 != null && back2.flags == Dropped && Same(back2.blobs[0], Blob(4, 5, 6)) && !f.Has(m => m.kind == ToClient.Spawn && m.id == passing),
            "the m2 drop is back on m2; an entity without PersistOnLeave is not");
        Check(!e.Has(ToClient.Spawn) || e.inbox.TrueForAll(m => m.kind != ToClient.Spawn || m.map == "m1"), "m1 gets only m1's");
        e.Send(SyncProtocol.Claim(car));
        Step(e);
        Check(e.Take(ToClient.Owner, car)?.owner == e.number, "a restored car can be claimed");
        server.Stop();

        Section("World file version 1");
        string old = WorldStore.NewId();
        WriteVersion1(old);
        var v1 = WorldStore.Open(root, old);
        Check(v1.CharacterCount == 1 && v1.Find("acc-old")?.name == "옛사람" && v1.Entities.Count == 0 && v1.Lost.Count == 0 && v1.NextConnection == 1,
            "a version 1 world.dat reads: its character, no entities, connections from 1");
        loopback = new LoopbackTransport();
        server = new GameServer(loopback, 8, world: v1);
        var oldOne = Hello("acc-old", "m1");
        Check(oldOne.number == 1 && oldOne.Take(m => m.kind == ToClient.Character)?.name == "옛사람", "and serves it");
        server.Stop();
        Check(WorldStore.Open(root, old).CharacterCount == 1, "saved again as version " + WorldStore.FileVersion);

        Section("Limits");
        var full = WorldStore.InMemory();
        for (int i = 0; i < GameServer.MaxPersistent; i++)
            full.Entities.Add(new WorldStore.SavedEntity { id = SyncProtocol.MakeId(9000, (uint)i + 1), prefab = Key, map = "far", flags = Placed, blobs = new[] { Blob(1) } });
        loopback = new LoopbackTransport();
        server = new GameServer(loopback, 8, world: full);
        var x = Join("acc-x", "QA엑스", "m1");
        var y = Join("acc-y", "QA와이", "m1");
        Check(x.number > 9000, "numbers start past every saved id (" + x.number + ")");
        ulong over = Id(x, 1), plain = Id(x, 2);
        x.Send(SyncProtocol.Spawn(over, Key, "m1", Dropped, new[] { Blob(1) }));
        x.Send(SyncProtocol.Spawn(plain, "Net/Box", "m1", EntityFlags.None, new[] { Blob(1) }));
        Step(x, y);
        Check(x.Take(ToClient.Despawn, over) != null && !y.Has(m => m.kind == ToClient.Spawn && m.id == over), "the 4097th PersistOnLeave entity is refused: its sender gets Despawn, nobody sees it");
        Check(y.Take(ToClient.Spawn, plain) != null && x.client.IsOpen, "a plain entity still spawns");
        for (uint i = 3; i < 3 + SyncProtocol.MaxEntitiesPerConnection - 1; i++) x.Send(SyncProtocol.Spawn(Id(x, i), "Net/Box", "m1", EntityFlags.None, new[] { Blob(1) }));
        ulong extra = Id(x, 1000);
        x.Send(SyncProtocol.Spawn(extra, "Net/Box", "m1", EntityFlags.None, new[] { Blob(1) }));
        Step(x, y);
        Check(x.Take(ToClient.Despawn, extra) != null && x.client.IsOpen, "the 257th entity of one connection is refused with Despawn");
        var z = Hello("acc-z", "m1");
        ulong noChar = Id(z, 1);
        z.Send(SyncProtocol.Spawn(noChar, Key, "m1", Dropped, new[] { Blob(1) }));
        Step(z);
        Check(z.Take(ToClient.Despawn, noChar) != null, "a Spawn without a character is refused with Despawn");
        server.Stop();

        Section("Lost and found");
        string w2 = WorldStore.NewId();
        StartServer(w2);
        var p = Join("acc-p", "QA피", "m1");
        var q = Join("acc-q", "QA큐", "m1");
        var r = Join("acc-r", "QA알", "m2");
        Clear(p, q, r);
        const double Day = GameServer.DayMinutes;
        // The world clock skips 00-06 (12.9), so 05:59 never happens: 23:59 is the last minute before a 06:00.
        SetClock(23 * 60 + 58); // day 0, 23:58
        ulong early = Drop(p, 1, "m1", Dropped, 1);
        ulong parked = Drop(p, 2, "m1", Placed, 2);
        SetClock(23 * 60 + 59.5);
        Step(p, q, r);
        Check(!q.Has(ToClient.Despawn) && server.Lost.Count == 0, "23:59: the dropped item is still there");
        server.AdvanceClock(5000); // one game minute: 24:00 is in the skipped night, so 06:00 of day 1
        Step(p, q, r);
        Check(Math.Abs(server.World.WorldMinutes - (Day + 6 * 60)) < .5, "the world clock went from 23:59 to 06:00 of the next day (" + server.World.WorldMinutes.ToString("F1") + ")");
        Check(q.Take(ToClient.Despawn, early) != null && p.Take(ToClient.Despawn, early) != null && !r.Has(ToClient.Despawn),
            "06:00: everyone on its map gets Despawn (dropped at 23:58, gone 2 clock minutes later)");
        Check(server.Lost.Count == 1 && server.Lost[0].prefab == Key && Same(server.Lost[0].blobs[0], Blob(1)) && Math.Abs(server.Lost[0].movedAt - (Day + 6 * 60)) < .01,
            "it is in the lost-and-found list with its key and state");
        SetClock(Day + 23 * 60); // day 1, 23:00
        ulong late = Drop(p, 3, "m1", Dropped, 3);
        SetClock(Day + 23 * 60 + 59);
        Step(p, q);
        Check(!q.Has(m => m.kind == ToClient.Despawn && m.id == late), "dropped at 23:00: kept until the next 06:00 (23:59 that day: still there)");
        SetClock(2 * Day + 6 * 60);
        Step(p, q);
        Check(q.Take(ToClient.Despawn, late) != null && server.Lost.Count == 2, "and cleared at that 06:00");
        Check(!q.Has(m => m.kind == ToClient.Despawn && m.id == parked), "the placed vehicle (no Expiring) stays");
        SetClock(2 * Day + 7 * 60);
        ulong held = Drop(p, 4, "m1", Dropped, 4);
        q.Send(SyncProtocol.Claim(held));
        SetClock(3 * Day + 6 * 60 + .5);
        Step(p, q);
        Check(!q.Has(m => m.kind == ToClient.Despawn && m.id == held) && server.Lost.Count == 2, "an Expiring entity someone holds is not cleared");
        q.Send(SyncProtocol.Release(held));
        SetClock(3 * Day + 6 * 60 + 1);
        Step(p, q);
        Check(q.Take(ToClient.Despawn, held) != null && server.Lost.Count == 3, "given back, it goes at the next check");
        Check(!q.Has(m => m.kind == ToClient.Despawn && m.id == parked), "the placed vehicle still stays");
        server.Stop();
        StartServer(w2);
        Check(server.Lost.Count == 3, "the lost items survive a restart");
        SetClock(Day + 6 * 60 + GameServer.LostKeepMinutes - 7 * 60); // 23:00 before the 7th day's 06:00 (05:59 is skipped)
        Step();
        Check(server.Lost.Count == 3, "kept for just under 7 game days");
        SetClock(Day + 6 * 60 + GameServer.LostKeepMinutes + .5);
        Step();
        Check(server.Lost.Count == 2, "deleted 7 game days after it was moved (the later ones stay)");
        var s = Join("acc-s", "QA에스", "m1");
        Check(s.Take(ToClient.Spawn, parked) != null, "the placed vehicle is still in the world");
        server.Stop();
        Check(GameServer.ClearTime(359) == 360 && GameServer.ClearTime(360) == 360 + Day && GameServer.ClearTime(Day + 23 * 60) == 2 * Day + 360, "06:00 rule: 05:59 → today, 06:00 and 23:00 → the next");
    }

    static void StartServer(string world)
    {
        loopback = new LoopbackTransport();
        server = new GameServer(loopback, 16, world: WorldStore.Open(root, world));
        server.Log += m => Debug.Log("[DropSyncQA] server: " + m);
    }

    static QaPeer Hello(string account, string map)
    {
        var p = new QaPeer(loopback.Connect());
        p.Send(SyncProtocol.Hello(account, account, map));
        server.Tick();
        p.number = p.Take(m => m.kind == ToClient.Welcome).number;
        return p;
    }

    static QaPeer Join(string account, string name, string map)
    {
        var p = Hello(account, map);
        if (p.Take(m => m.kind == ToClient.Character)?.has != true)
        {
            p.Send(SyncProtocol.CharacterCreate(name, "male"));
            server.Tick();
            if (p.Take(m => m.kind == ToClient.CharacterResult)?.result != CharacterResult.Ok) throw new Exception("[DropSyncQA] FAIL: character " + name);
        }
        return p;
    }

    // Spawn + Release: a drop as the game makes it.
    static ulong Drop(QaPeer p, uint serial, string map, EntityFlags flags, byte mark)
    {
        ulong id = Id(p, serial);
        p.Send(SyncProtocol.Spawn(id, Key, map, flags, new[] { Blob(mark) }));
        p.Send(SyncProtocol.Release(id));
        server.Tick();
        p.Pump();
        return id;
    }

    static void SetClock(double minutes) => server.World.WorldMinutes = minutes;

    static void Step(params QaPeer[] peers) { server.Tick(); foreach (var p in peers) p.Pump(); }
    static void Clear(params QaPeer[] peers) { foreach (var p in peers) { p.Pump(); p.inbox.Clear(); } }
    static ulong Id(QaPeer p, uint serial) => SyncProtocol.MakeId(p.number, serial);
    static byte[] Blob(params byte[] bytes) => bytes;
    static bool Same(byte[] a, byte[] b) => a != null && b != null && ((ReadOnlySpan<byte>)a).SequenceEqual(b);

    // The 12.5 layout, written by hand: version 1, id, created, clock, one character.
    static void WriteVersion1(string id)
    {
        string folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        using var stream = new FileStream(Path.Combine(folder, WorldStore.FileName), FileMode.Create);
        using var w = new BinaryWriter(stream, Encoding.UTF8);
        w.Write((ushort)1);
        SyncProtocol.WriteStr(w, id);
        w.Write(DateTime.UtcNow.Ticks);
        w.Write(100.0);
        w.Write(1);
        SyncProtocol.WriteStr(w, "acc-old"); SyncProtocol.WriteStr(w, "옛사람"); SyncProtocol.WriteStr(w, "female");
        w.Write(10.0); w.Write(90.0);
        SyncProtocol.WriteBlob32(w, new byte[] { 1, 2, 3 });
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

    static string playWorld;

    static async void DrivePlay()
    {
        int original = PlayerPrefs.GetInt("Profile.Slot", 1);
        int code = 0;
        Begin();
        try { await PlayBody(); Report("RESULT play passed"); }
        catch (Exception e) { Report("RESULT play FAILED: " + e.Message + "\n" + e.StackTrace, true); code = 1; }
        try
        {
            if (SyncHost.Instance) { playWorld ??= SyncHost.Instance.Server.World.Folder; SyncHost.Instance.Stop(); }
            if (playWorld != null && Directory.Exists(playWorld)) Directory.Delete(playWorld, true); // the QA's world, not a player's
        }
        catch (Exception e) { Debug.LogWarning("[DropSyncQA] cleanup: " + e.Message); }
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    static async Task PlayBody()
    {
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy, 15, "title");
        ProfileSlot.Set(Slot);
        var client = SyncClient.Instance;
        var settings = new ServerSettings { name = "DropQA", maxPlayers = 4, port = 0 };
        var host = SyncHost.StartInApp(settings, true);
        playWorld = host.Server.World.Folder;
        await WaitFor(() => client.Connected && client.CharacterKnown, 10, "the Unity client on the in-app server");
        Check(await TitleFlow.CreateServerCharacter("QA드롭", "male") == null, "the Unity player made its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active, 90, "the game map");
        string map = SceneLoadManager.CurrentMap.path;
        var player = PlayerSpawner.Player;
        var inv = player.GetComponent<PlayerInventory>();
        var driver = player.GetComponent<PlayerVehicle>();
        var bag = InventoryManager.Instance.State;
        var carItem = Resources.LoadAll<ItemData>("Inventory/Vehicles/Cars")[0];
        ItemData stuff = null;
        foreach (var item in Resources.LoadAll<ItemData>("Inventory"))
            if (item && !item.IsVehicle && !item.IsBook && !item.IsCurrency && item.StackLimit >= 5 && InventorySnapshot.Find(item.itemId) == item) { stuff = item; break; }
        Check(stuff, "a plain stackable item to drop (" + (stuff ? stuff.itemId : "none") + ")");

        var cam = Camera.main.transform;
        var ahead = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        var side = Vector3.Cross(Vector3.up, ahead);
        Vector3 home = player.transform.position;
        float faceYaw = Quaternion.LookRotation(ahead).eulerAngles.y;

        // ---- the Unity player places a car and drops an item; the fake player gets both ----
        Section("Unity's car and drop at the fake");
        var socket = new TcpClientTransport();
        await socket.Connect("127.0.0.1", host.Port);
        var fake = new QaPeer(socket);
        fake.Send(SyncProtocol.Hello("qa-drop-fake", "fake", map));
        fake.number = (await WaitMsg(fake, m => m.kind == ToClient.Welcome, "fake welcome")).number;
        fake.Send(SyncProtocol.CharacterCreate("QA가짜", "female"));
        Check((await WaitMsg(fake, m => m.kind == ToClient.CharacterResult, "fake character")).result == CharacterResult.Ok, "the fake second player made its character");

        int carSlot = -1;
        for (int i = 0; i < InventoryState.HotbarSize; i++) if (bag.GetSlot(i).Item && bag.GetSlot(i).Item.IsCar) { carSlot = i; break; }
        Check(carSlot >= 0, "a car on the hotbar (slot " + carSlot + ")");
        string carName = bag.GetSlot(carSlot).Item.itemId;
        bag.SelectHotbar(carSlot);
        Check(WorldDroppedItem.TryPlaceVehicle(inv, out string placeError), "the Unity player placed " + carName + " " + placeError);
        var myCarMsg = await WaitMsg(fake, m => m.kind == ToClient.Spawn && m.key == Key && m.owner == client.MyConnection && m.flags == Placed, "Unity's car at the fake");
        ulong myCar = myCarMsg.id;
        var myCarState = ReadDrop(myCarMsg.blobs[0]);
        Check(myCarState.kind == DropState.VehicleKind && myCarState.itemId == carName, "the fake got it as a placed vehicle of " + carName + " (state " + myCarMsg.blobs[0].Length + " bytes)");
        Check((await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == myCar, "Owner of Unity's car")).owner == 0, "then Owner(0): the server keeps it");
        var myCarView = await FindView(myCar);
        Check(myCarView.IsPlacedVehicle && !myCarView.IsOccupied, "the Unity screen shows its own placed car (record id = entity id)");

        Check(bag.TryAdd(stuff, 2, out _), "two " + stuff.itemId + " in the bag");
        int stuffSlot = FindSlot(bag, stuff);
        Check(inv.OpenInventory(), "inventory open");
        bool dropped = WorldDroppedItem.TryDropStorage(inv, stuffSlot, out string dropError, 2);
        inv.CloseInventory();
        Check(dropped, "the Unity player dropped 2 " + stuff.itemId + " " + dropError);
        var myDropMsg = await WaitMsg(fake, m => m.kind == ToClient.Spawn && m.key == Key && m.owner == client.MyConnection && m.flags == Dropped, "Unity's drop at the fake");
        ulong myDrop = myDropMsg.id;
        var myDropState = ReadDrop(myDropMsg.blobs[0]);
        Check(myDropState.kind == DropState.ItemKind && myDropState.itemId == stuff.itemId && myDropState.count == 2, "the fake got the drop: Expiring, 2 " + stuff.itemId);
        await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == myDrop && m.owner == 0, "Owner(0) of Unity's drop");

        // ---- the fake player places a car and drops an item; the Unity screen shows them ----
        Section("the fake's car and drop on the Unity screen");
        Vector3 carSpot = Ground(home + ahead * 4.5f + side * 4.5f), dropSpot = Ground(home + ahead * 3f + side * 2.2f);
        float carYaw = faceYaw;
        ulong fakeCar = SyncProtocol.MakeId(fake.number, 1), fakeDrop = SyncProtocol.MakeId(fake.number, 2);
        fake.Send(SyncProtocol.Spawn(fakeCar, Key, map, Placed, new[] { DropBlob(DropState.VehicleKind, carSpot, carYaw, carItem, 1) }));
        fake.Send(SyncProtocol.Release(fakeCar));
        fake.Send(SyncProtocol.Spawn(fakeDrop, Key, map, Dropped, new[] { DropBlob(DropState.ItemKind, dropSpot, 0f, stuff, 3) }));
        fake.Send(SyncProtocol.Release(fakeDrop));
        var fakeCarView = await FindView(fakeCar);
        var fakeDropView = await FindView(fakeDrop);
        await WaitFor(() => !fakeCarView.IsOccupied, 3, "the fake's car given to the server");
        Check(fakeCarView.IsPlacedVehicle && fakeCarView.Item == carItem && (fakeCarView.transform.position - carSpot).magnitude < .01f
              && fakeCarView.GetComponentInChildren<MeshRenderer>(), "the fake's car stands where it was placed, visible");
        Check(!fakeDropView.IsPlacedVehicle && fakeDropView.Item == stuff && fakeDropView.Count == 3 && (fakeDropView.transform.position - dropSpot).magnitude < .01f,
            "the fake's drop lies there: 3 " + stuff.itemId);
        await StandBeside(player, home - ahead * 3f, ahead);
        await Shot("D_others_car_and_drop.png");

        // ---- both claim the fake's car at once ----
        Section("same car, two riders");
        await StandBeside(player, fakeCarView.transform.position);
        Drain(fake);
        fake.Send(SyncProtocol.Claim(fakeCar));
        fakeCarView.TryMount(inv);
        var first = await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == fakeCar, "the first Owner of the race");
        await Awaitable.WaitForSecondsAsync(.5f);
        bool unityWon = first.owner == client.MyConnection;
        Check(first.owner == fake.number || unityWon, "exactly one rider: " + (unityWon ? "the Unity player" : "the fake player") + " won (owner " + first.owner + ")");
        Check(unityWon == driver.IsRiding, "the Unity player rides only if it won");
        fake.Pump();
        Check(fake.inbox.TrueForAll(m => m.kind != ToClient.Owner || m.id != fakeCar || m.owner == first.owner), "the loser's Claim was answered with the winner");
        if (unityWon)
        {
            Check(driver.Dismount(false), "the Unity player gets out");
            await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == fakeCar && m.owner == 0, "the car given back");
            await Awaitable.WaitForSecondsAsync(.3f);
            fake.Send(SyncProtocol.Claim(fakeCar)); // the fake takes it now (no race) for the next part
            await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == fakeCar && m.owner == fake.number, "the fake in the car");
        }
        await WaitFor(() => fakeCarView.IsOccupied && !ModelShown(fakeCarView), 3, "the car hidden on the Unity screen");
        Check(WorldDroppedItem.FindNearestVehicle(inv) != fakeCarView, "while the fake drives it, the Unity screen hides the parked copy and offers no ride");
        await StandBeside(player, home - ahead * 3f, ahead); // back from the prompts (3 m reach)
        await Shot("D_hidden_while_ridden.png");

        // ---- the fake drives off and leaves in the car ----
        Section("rider leaves");
        Vector3 lastSpot = Ground(carSpot - ahead * 2f + side * .5f);
        fake.Send(SyncProtocol.State(fakeCar, 0, DropBlob(DropState.VehicleKind, lastSpot, carYaw + 30f, carItem, 1)));
        await Awaitable.WaitForSecondsAsync(.3f);
        socket.Close();
        await WaitFor(() => !fakeCarView.IsOccupied && ModelShown(fakeCarView), 5, "the car parked again on the Unity screen");
        Check((fakeCarView.transform.position - lastSpot).magnitude < .01f && Mathf.Abs(Mathf.DeltaAngle(fakeCarView.transform.eulerAngles.y, carYaw + 30f)) < .5f,
            "the fake left while driving: the car stands where it was last seen");
        await StandBeside(player, fakeCarView.transform.position);
        Check(fakeCarView.TryMount(inv), "the Unity player can ask to ride it");
        await WaitFor(() => driver.IsRiding, 3, "riding the left car");
        Check(true, "and gets in");
        Check(driver.Dismount(false), "gets out again");
        await WaitFor(() => client.Find(fakeCar) && client.Find(fakeCar).Owner == 0, 3, "the left car given back");
        fake = await Rejoin(host.Port, map, "fake again");

        // ---- the Unity player drives its own car and gets out ----
        Section("ride and park");
        await StandBeside(player, myCarView.transform.position);
        Check(myCarView.TryMount(inv), "the Unity player asks to ride its car");
        await WaitFor(() => driver.IsRiding, 3, "riding");
        Check((await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == myCar, "Owner while riding")).owner == client.MyConnection, "the fake sees the Unity player own the car");
        Drain(fake);
        player.transform.position += side * 4f;
        await Awaitable.WaitForSecondsAsync(1.3f);
        var moving = fake.Last(ToClient.State, myCar);
        Check(moving != null && (ReadDrop(moving.blob).position - player.transform.position).magnitude < .5f, "moving over 1 m sends the place within a second");
        player.transform.position += ahead * 2f;
        Vector3 parkAt = player.transform.position;
        Check(driver.Dismount(false), "the Unity player gets out");
        var released = await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == myCar && m.owner == 0, "Owner(0) after getting out");
        var parkedState = fake.Last(ToClient.State, myCar);
        Check(parkedState != null && (ReadDrop(parkedState.blob).position - parkAt).magnitude < .05f, "the fake's last state of the car is where it was left (" + (parkedState != null ? ReadDrop(parkedState.blob).position.ToString("F2") : "none") + ")");

        // ---- one drop, two pickers ----
        Section("one drop, two pickers");
        await StandBeside(player, fakeDropView.transform.position);
        int before = CountOf(bag, stuff);
        Drain(fake);
        fake.Send(SyncProtocol.Claim(fakeDrop));
        fakeDropView.TryPickUp(inv, out _);
        var winner = await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == fakeDrop, "the pickup race");
        int fakeGot = 0;
        if (winner.owner == fake.number) { fakeGot = 3; fake.Send(SyncProtocol.Despawn(fakeDrop)); }
        else await WaitMsg(fake, m => m.kind == ToClient.Despawn && m.id == fakeDrop, "the Unity pickup's Despawn");
        await WaitFor(() => !fakeDropView, 3, "the drop gone from the Unity screen");
        await Awaitable.WaitForSecondsAsync(.3f);
        int unityGot = CountOf(bag, stuff) - before;
        Check(unityGot + fakeGot == 3 && (unityGot == 0 || fakeGot == 0), "only one picked it up (" + (fakeGot > 0 ? "the fake" : "the Unity player") + "): " + unityGot + " + " + fakeGot + " = 3, nothing more");
        await Awaitable.WaitForSecondsAsync(.3f);
        fake.Pump();
        Check(client.Find(fakeDrop) == null && !fake.Has(m => m.kind == ToClient.Spawn && m.id == fakeDrop), "and it is gone for both");

        // ---- dismantling the Unity car ----
        Section("dismantle");
        await StandBeside(player, myCarView.transform.position);
        Drain(fake);
        Check(myCarView.TryDismantleVehicle(GameSession.LocalPlayerName, out string dismantleError), "the Unity player dismantles its car " + dismantleError);
        var pieces = await WaitMsg(fake, m => m.kind == ToClient.Spawn && m.key == Key && m.flags == Dropped, "the dismantled car as a drop at the fake");
        await WaitMsg(fake, m => m.kind == ToClient.Despawn && m.id == myCar, "the car's Despawn at the fake");
        var piecesState = ReadDrop(pieces.blobs[0]);
        Check(piecesState.kind == DropState.ItemKind && piecesState.itemId == carName, "the fake sees it become an ordinary dropped " + carName);
        var piecesView = await FindView(pieces.id);
        Check(!piecesView.IsPlacedVehicle && piecesView.Item.IsCar && !myCarView, "the Unity screen shows the same: a dropped car item, no placed car");
        await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == pieces.id && m.owner == 0, "Owner(0) of the pieces");
        await WaitFor(() => client.Find(pieces.id) && client.Find(pieces.id).Owner == 0, 3, "the pieces given to the server");
        int carsBefore = CountOfId(bag, carName);
        piecesView.TryPickUp(inv, out _);
        await WaitFor(() => !piecesView, 3, "the Unity player picking the pieces up");
        Check(CountOfId(bag, carName) == carsBefore + 1, "the dropped car can be picked up: it is back in the bag");
        Check(await WaitFor(() => fake.Has(m => m.kind == ToClient.Despawn && m.id == pieces.id), 3), "the fake saw it go");

        // ---- interest: another map and back ----
        Section("another map");
        await Awaitable.WaitForSecondsAsync(.3f);
        int cars = WorldDroppedItem.ServerDropCount(true), drops = WorldDroppedItem.ServerDropCount(false);
        Check(cars == 1 && drops == 1, "on the map now: the fake's car and the Unity drop (" + cars + " cars, " + drops + " drops)");
        Drain(fake);
        fake.Send(SyncProtocol.Map("QA/Elsewhere"));
        await WaitFor(() => fake.Has(m => m.kind == ToClient.Despawn && m.id == fakeCar) && fake.Has(m => m.kind == ToClient.Despawn && m.id == myDrop), 3, "the fake leaving the map");
        Check(true, "the fake on another map: the car and drop despawn for it");
        Drain(fake);
        fake.Send(SyncProtocol.Map(map));
        await WaitFor(() => fake.Has(m => m.kind == ToClient.Spawn && m.id == fakeCar) && fake.Has(m => m.kind == ToClient.Spawn && m.id == myDrop), 3, "the fake back");
        Check(true, "back on the map: they spawn again");
        client.SetMap("QA/Elsewhere");
        await WaitFor(() => WorldDroppedItem.ServerDropCount(true) + WorldDroppedItem.ServerDropCount(false) == 0 && NetViews().Count == 0, 3, "the Unity side away");
        Check(true, "the Unity side on another map: no server car or drop on screen");
        client.SetMap(map);
        await WaitFor(() => WorldDroppedItem.ServerDropCount(true) == cars && WorldDroppedItem.ServerDropCount(false) == drops && NetViews().Count == cars + drops, 5, "the Unity side back");
        Check(true, "back: both shown again");
        fakeCarView = await FindView(fakeCar);
        Vector3 carAt = fakeCarView.transform.position;

        // ---- server off, this PC's drop, server on ----
        Section("restart and leaving");
        string worldId = host.Server.World.Id;
        host.Stop();
        await WaitFor(() => !client.Connected && !CharacterSync.Active, 5, "leaving the server");
        await Awaitable.NextFrameAsync();
        Check(WorldDroppedItem.ServerDropCount(true) + WorldDroppedItem.ServerDropCount(false) == 0 && NetViews().Count == 0, "off the server: no server car or drop is left on screen");
        Check(bag.TryAdd(stuff, 1, out _), "this PC's own inventory has a " + stuff.itemId);
        Check(inv.OpenInventory(), "inventory open (offline)");
        dropped = WorldDroppedItem.TryDropStorage(inv, FindSlot(bag, stuff), out dropError, 1);
        inv.CloseInventory();
        Check(dropped, "an offline drop on this PC " + dropError);
        WorldDroppedItem pcDrop = null;
        await WaitFor(() => pcDrop = PcView(stuff), 2, "the PC drop");
        string pcDropId = pcDrop.DropId;
        settings.world = worldId;
        host = SyncHost.StartInApp(settings, true);
        await WaitFor(() => client.Connected && CharacterSync.Active, 15, "back on the restarted server");
        await WaitFor(() => WorldDroppedItem.ServerDropCount(true) == cars && WorldDroppedItem.ServerDropCount(false) == drops, 5, "the car and drop after the restart");
        fakeCarView = await FindView(fakeCar);
        var myDropView = await FindView(myDrop);
        Check((fakeCarView.transform.position - carAt).magnitude < .01f && myDropView.Count == 2, "after a restart of the same world the car stands in the same place and the drop is there");
        await Awaitable.NextFrameAsync(); // Destroy lands at the end of the frame
        var shown = PcView(null);
        Check(!pcDrop && !shown, "on the server, this PC's own drops are not shown" + (shown ? " (shown: " + shown.name + " " + shown.Item + " in " + shown.gameObject.scene.name + ")" : ""));
        await StandBeside(player, home - ahead * 3f, ahead); // back from the prompts (3 m reach)
        await Shot("D_after_restart.png");

        // ---- 06:00 ----
        Section("06:00");
        double now = host.Server.World.WorldMinutes;
        double minutes = GameServer.ClearTime(now) - now + .5;
        host.Server.AdvanceClock((long)(minutes * 60 * 1000 * GameServer.RealSecondsPerGameHour / 3600));
        await WaitFor(() => !myDropView, 5, "the drop cleared at 06:00");
        await Awaitable.WaitForSecondsAsync(.5f);
        Check(fakeCarView && ModelShown(fakeCarView) && WorldDroppedItem.ServerDropCount(false) == 0, "06:00 passed (+" + minutes.ToString("F0") + " game minutes): the dropped item is gone, the car stays");
        Check(host.Server.Lost.Count == 1, "the server moved it to the lost-and-found list");

        host.Stop();
        await WaitFor(() => !client.Connected, 5, "leaving at the end");
        await WaitFor(() => pcDrop = Array.Find(Object.FindObjectsByType<WorldDroppedItem>(), v => v && v.DropId == pcDropId), 2, "this PC's drop shown again");
        await Awaitable.NextFrameAsync();
        Check(NetViews().Count == 0, "after leaving, this PC's own drop is back and no server item is left");
        await StandBeside(player, pcDrop.transform.position);
        Check(pcDrop.TryPickUp(inv, out _), "the PC drop picked up again (clean slot " + Slot + ")");
    }

    // ---- play helpers ----

    struct DropInfo
    {
        public byte kind;
        public Vector3 position;
        public string itemId;
        public int count;
    }

    static DropInfo ReadDrop(byte[] blob)
    {
        using var r = SyncProtocol.Reader(blob);
        var d = new DropInfo { kind = r.ReadByte(), position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) };
        r.ReadSingle();
        var contents = new InventoryState();
        if (DropState.Unpack(SyncProtocol.ReadBlob(r), contents)) { d.itemId = contents.GetSlot(0).Item ? contents.GetSlot(0).Item.itemId : ""; d.count = contents.GetSlot(0).Count; }
        return d;
    }

    // What the fake sends: the real DropState codec.
    static byte[] DropBlob(byte kind, Vector3 position, float yaw, ItemData item, int count)
    {
        var contents = new InventoryState();
        if (!contents.TryAdd(item, count, out string error)) throw new Exception("[DropSyncQA] FAIL: " + error);
        var go = new GameObject("DropSyncQA state");
        var state = go.AddComponent<DropState>();
        state.kind = kind; state.position = position; state.yaw = yaw; state.contents = DropState.Pack(contents);
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        state.Write(w);
        w.Flush();
        Object.DestroyImmediate(go);
        return stream.ToArray();
    }

    // The walkable floor under <paramref name="at"/> nearest its height (not a terrace above or a street below).
    static Vector3 Ground(Vector3 at)
    {
        Vector3 best = at;
        float nearest = 1.5f;
        foreach (var hit in Physics.RaycastAll(at + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.normal.y > .7f && !hit.transform.IsChildOf(PlayerSpawner.Player.transform) && Mathf.Abs(hit.point.y - at.y) < nearest)
            { nearest = Mathf.Abs(hit.point.y - at.y); best = hit.point + Vector3.up * .03f; }
        return best;
    }

    // Puts the player 1.6 m from a spot (or at it, facing <paramref name="face"/>), the way a walk there would.
    static async Task StandBeside(PlayerMovement player, Vector3 spot, Vector3? face = null)
    {
        var motor = player.GetComponent<CharacterController>();
        var to = face.HasValue ? spot : spot - Vector3.ProjectOnPlane(spot - player.transform.position, Vector3.up).normalized * 1.6f;
        if (!face.HasValue && (spot - player.transform.position).sqrMagnitude < 1e-4f) to = spot + Vector3.right * 1.6f;
        motor.enabled = false;
        player.transform.position = to;
        player.transform.rotation = Quaternion.LookRotation(face ?? Vector3.ProjectOnPlane(spot - to, Vector3.up).normalized);
        motor.enabled = true;
        Physics.SyncTransforms();
        await Awaitable.WaitForSecondsAsync(.3f);
    }

    static async Task<WorldDroppedItem> FindView(ulong id)
    {
        WorldDroppedItem found = null;
        await WaitFor(() => found = Array.Find(Object.FindObjectsByType<WorldDroppedItem>(), v => v.NetId == id), 5, "the view of entity " + id);
        return found;
    }

    static List<WorldDroppedItem> NetViews() => new List<WorldDroppedItem>(Array.FindAll(Object.FindObjectsByType<WorldDroppedItem>(), v => v && v.NetId != 0));
    // This PC's own drop (of <paramref name="item"/>, or any).
    static WorldDroppedItem PcView(ItemData item) => Array.Find(Object.FindObjectsByType<WorldDroppedItem>(), v => v && v.NetId == 0 && v.DropId != null && (!item || v.Item == item && v.Count == 1));

    static bool ModelShown(WorldDroppedItem view)
    {
        var model = view.transform.Find("PlacedVehicleModel");
        return model && model.gameObject.activeSelf;
    }

    static async Task<QaPeer> Rejoin(int port, string map, string what)
    {
        var socket = new TcpClientTransport();
        await socket.Connect("127.0.0.1", port);
        var p = new QaPeer(socket);
        p.Send(SyncProtocol.Hello("qa-drop-fake", "fake", map));
        p.number = (await WaitMsg(p, m => m.kind == ToClient.Welcome, what)).number;
        Check((await WaitMsg(p, m => m.kind == ToClient.Character, what + " character")).has, "the fake comes back with its character");
        return p;
    }

    static void Drain(QaPeer p) { p.Pump(); p.inbox.Clear(); }

    static int FindSlot(InventoryState bag, ItemData item)
    {
        for (int i = 0; i < bag.Capacity; i++) if (bag.GetSlot(i).Item == item) return i;
        throw new Exception("[DropSyncQA] FAIL: no " + item.itemId + " in the bag");
    }

    static int CountOf(InventoryState bag, ItemData item)
    {
        int n = 0;
        for (int i = 0; i < bag.Capacity; i++) if (item && bag.GetSlot(i).Item == item) n += bag.GetSlot(i).Count;
        return n;
    }

    static int CountOfId(InventoryState bag, string itemId)
    {
        int n = 0;
        for (int i = 0; i < bag.Capacity; i++) if (bag.GetSlot(i).Item && bag.GetSlot(i).Item.itemId == itemId) n += bag.GetSlot(i).Count;
        return n;
    }

    static bool InGame => SceneManager.GetActiveScene().path == TitleFlow.GameScene && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading;

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
        if (!await WaitFor(condition, seconds)) throw new Exception("TIMEOUT waiting for " + what);
    }

    static async Task<bool> WaitFor(Func<bool> condition, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) return false;
            await Awaitable.NextFrameAsync();
        }
        return true;
    }

    static async Task Shot(string file, float settle = .5f)
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (string.IsNullOrEmpty(dir) || !Camera.main) return;
        Directory.CreateDirectory(dir);
        if (settle > 0f) await Awaitable.WaitForSecondsAsync(settle);
        await TitleShots.Snap(Camera.main, Path.Combine(dir, file));
    }

    // ---- counting ----

    static void Begin() { checks = 0; section = ""; sections.Clear(); }

    static void Section(string name)
    {
        section = name;
        sections.Add(new KeyValuePair<string, int>(name, 0));
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("[DropSyncQA] FAIL (" + section + "): " + what);
        checks++;
        if (sections.Count > 0) sections[sections.Count - 1] = new KeyValuePair<string, int>(section, sections[sections.Count - 1].Value + 1);
        Debug.Log("[DropSyncQA] ok: " + what);
    }

    static void Report(string result, bool failed = false)
    {
        var text = new StringBuilder("[DropSyncQA] " + result + ", checks=" + checks);
        foreach (var s in sections) text.Append("\n  ").Append(s.Key).Append(": ").Append(s.Value);
        if (failed) Debug.LogError(text.ToString()); else Debug.Log(text.ToString());
    }
}
