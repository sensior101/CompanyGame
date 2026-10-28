using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CompanyGame.World.Maps;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using QaPeer = SyncLayerQA.QaPeer;
using QaMsg = SyncLayerQA.QaMsg;

/// <summary>
/// QA of items between the inventory and the world (Docs/Multiplayer_Redesign.md 12.10.5), on the EconomySyncQA helpers.
///   -executeMethod WorldItemQA.Run         in memory (with -quit): GameServer's world entities (ids across restarts, the
///                                          limit), the three hooks, ServerDrops and the ledger on a world with 4b entities,
///                                          the goshiwon deeds
///   -executeMethod WorldItemQA.RunPlay     play mode (WITHOUT -quit): in-app server + the Unity owner + a fake TCP player
///   -executeMethod WorldItemQA.RunProcess  play mode (WITHOUT -quit): a built server process + the Unity client
/// Env QA_SHOTS = folder for W_*.png (needs graphics).
/// </summary>
public static class WorldItemQA
{
    public static void Run() => EconomySyncQA.WorldItemMemory();
    public static void RunPlay() => EconomySyncQA.StartPlay("item-play");
    public static void RunProcess() => EconomySyncQA.BuildAndPlay("WorldItemQA", "item-process");
}

public static partial class EconomySyncQA
{
    const string ItemFake = "qa-item-fake";
    const EntityFlags PlacedFlags = EntityFlags.PersistOnLeave | EntityFlags.Claimable;
    const string Gone = "다른 사람이 먼저 주웠습니다.", Card = "handae_employee_card";

    // ================= in memory (WorldItemQA.Run) =================

    static GameObject qaRegistry;

    internal static void WorldItemMemory()
    {
        tag = "WorldItemQA";
        Begin();
        root = Path.Combine(Path.GetTempPath(), "WorldItemQA-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            WorldEntityIds();
            WorldEntityLimit();
            Hooks();
            DropsOnOldWorld();
            StartingDeeds();
            Report("RESULT passed");
        }
        finally
        {
            SetRegistry(null);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    // A registry the server code can find (PropertyRegistry.Instance) without play mode; null removes it.
    static PropertyRegistry SetRegistry(List<PropertySave> state)
    {
        var instance = typeof(GameSystem<PropertyRegistry>).GetProperty("Instance").GetSetMethod(true);
        if (state == null)
        {
            instance.Invoke(null, new object[] { null });
            if (qaRegistry) Object.DestroyImmediate(qaRegistry);
            return null;
        }
        if (!qaRegistry) qaRegistry = new GameObject("WorldItemQA registry") { hideFlags = HideFlags.HideAndDontSave };
        var registry = qaRegistry.GetComponent<PropertyRegistry>() ?? qaRegistry.AddComponent<PropertyRegistry>();
        instance.Invoke(null, new object[] { registry });
        registry.RestoreState(state);
        return registry;
    }

    static bool Exists(ulong id) => server.TryGetEntity(id, out _, out _, out _, out _, out _);

    static byte[] Stored(ulong id) => server.TryGetEntity(id, out _, out _, out _, out _, out var blobs) ? blobs[0] : null;

    static void WorldEntityIds()
    {
        Section("SpawnWorldEntity ids");
        string w = WorldStore.NewId();
        StartServer(w);
        var a = Join("acc-a", "QA가", "m1");
        var b = Join("acc-b", "QA나", "m1");
        var c = Join("acc-c", "QA다", "m2");
        Clear(a, b, c);
        ulong first = server.SpawnWorldEntity(DropKey, "m1", DroppedFlags, new[] { Blob(1, 2) });
        uint mine = SyncProtocol.ConnectionOf(first);
        Step(a, b, c);
        Check(first != 0 && mine != a.number && mine != b.number && mine != c.number, "a world entity has the server's own connection number (" + mine + "), no client's");
        var seen = b.Take(ToClient.Spawn, first);
        Check(seen != null && seen.owner == 0 && seen.flags == DroppedFlags && Same(seen.blobs[0], Blob(1, 2)) && a.Take(ToClient.Spawn, first) != null && !c.Has(ToClient.Spawn),
            "everyone on its map gets its Spawn (owner 0, the blob as made); another map nothing");
        Check(server.TryGetEntity(first, out string prefab, out string map, out uint owner, out var flags, out var blobs)
              && prefab == DropKey && map == "m1" && owner == 0 && flags == DroppedFlags && Same(blobs[0], Blob(1, 2)), "TryGetEntity reads it back");
        Check(server.MapOf(a.number) == "m1" && server.MapOf(c.number) == "m2" && server.MapOf(9999) == null, "MapOf: each connection's map, null for none");
        ulong second = server.SpawnWorldEntity(DropKey, "m1", PlacedFlags, new[] { Blob(3) });
        Check(SyncProtocol.ConnectionOf(second) == mine && second == first + 1, "the next one: the same number, the next serial");
        var d = Join("acc-d", "QA라", "m1");
        Check(d.number != mine && d.number > mine, "a connection after it never gets that number (" + d.number + ")");
        Check(server.SpawnWorldEntity(DropKey, AllMaps, DroppedFlags, new[] { Blob(1) }) == 0 && server.SpawnWorldEntity(DropKey, "", DroppedFlags, new[] { Blob(1) }) == 0
              && server.SpawnWorldEntity(DropKey, "m1", DroppedFlags, new[] { new byte[SyncProtocol.MaxBlob + 1] }) == 0, "map *, no map and a blob over 4KB are refused (0)");
        Clear(a, b, c, d);
        Check(server.RemoveWorldEntity(first) && !Exists(first), "RemoveWorldEntity removes it");
        Step(a, b, c, d);
        Check(a.Take(ToClient.Despawn, first) != null && b.Take(ToClient.Despawn, first) != null && d.Take(ToClient.Despawn, first) != null && !c.Has(ToClient.Despawn), "its map gets Despawn");
        a.Send(SyncProtocol.Claim(second));
        Step(a, b);
        Check(a.Last(ToClient.Owner, second)?.owner == a.number && !server.RemoveWorldEntity(second), "one someone holds (a car being driven) is not removed");
        Clear(a, b);
        a.Send(SyncProtocol.Despawn(second));
        Step(a, b);
        Check(Exists(second) && !b.Has(m => m.kind == ToClient.Despawn && m.id == second), "and the one who claimed it cannot despawn it (it made it not)");
        a.Send(SyncProtocol.Release(second));
        Step(a, b);
        ulong game = server.SpawnServerEntity(WorldKey, AllMaps, EntityFlags.None, new byte[1][]);
        Check(!server.RemoveWorldEntity(game), "nor the game's own server entity (the world entity)");
        uint before = d.number;
        server.Stop();

        StartServer(w);
        Check(server.TryGetEntity(second, out _, out map, out owner, out _, out blobs) && map == "m1" && owner == 0 && Same(blobs[0], Blob(3)), "after a restart it is back (same id, map, state, owner 0)");
        ulong third = server.SpawnWorldEntity(DropKey, "m1", DroppedFlags, new[] { Blob(4) });
        uint next = SyncProtocol.ConnectionOf(third);
        Check(third != 0 && next > mine && next > before, "a new one after the restart takes a new number (" + mine + " → " + next + "): ids never repeat");
        var e = Join("acc-e", "QA마", "m1");
        Check(e.number != next && e.number != mine && e.number > before, "and so does the next connection (" + e.number + ")");
        server.Stop();
    }

    static void WorldEntityLimit()
    {
        Section("the limit");
        var full = WorldStore.InMemory();
        for (int i = 0; i < GameServer.MaxPersistent; i++)
            full.Entities.Add(new WorldStore.SavedEntity { id = SyncProtocol.MakeId(9000, (uint)i + 1), prefab = DropKey, map = "far", flags = PlacedFlags, blobs = new[] { Blob(1) } });
        loopback = new LoopbackTransport();
        server = new GameServer(loopback, 8, world: full);
        Check(server.SpawnWorldEntity(DropKey, "m1", DroppedFlags, new[] { Blob(1) }) == 0, "the 4097th PersistOnLeave entity of a world is refused (0)");
        ulong plain = server.SpawnWorldEntity("Net/Box", "m1", EntityFlags.None, new[] { Blob(1) });
        Check(plain != 0 && SyncProtocol.ConnectionOf(plain) > 9000, "one that is not saved still goes, its number past every saved id");
        server.Stop();
    }

    static void Hooks()
    {
        Section("SpawnFilter");
        StartServer(WorldStore.NewId());
        var a = Join("acc-a", "QA가", "m1");
        var b = Join("acc-b", "QA나", "m1");
        Clear(a, b);
        var asked = new List<string>();
        server.SpawnFilter = (conn, prefab) => { asked.Add(conn + ":" + prefab); return prefab == DropKey; };
        ulong refused = SyncProtocol.MakeId(a.number, 1), box = SyncProtocol.MakeId(a.number, 2), thrown = SyncProtocol.MakeId(a.number, 3);
        a.Send(SyncProtocol.Spawn(refused, DropKey, "m1", DroppedFlags, new[] { Blob(1) }));
        a.Send(SyncProtocol.Spawn(box, "Net/Box", "m1", EntityFlags.None, new[] { Blob(2) }));
        Step(a, b);
        Check(a.Take(ToClient.Despawn, refused) != null && !b.Has(m => m.kind == ToClient.Spawn && m.id == refused) && !Exists(refused), "refusing: the sender gets Despawn, nobody sees it, nothing is kept");
        Check(b.Take(ToClient.Spawn, box) != null && asked.Contains(a.number + ":Net/Box"), "a Spawn it lets through goes on (asked with the connection and the prefab)");
        server.SpawnFilter = (conn, prefab) => throw new Exception("qa");
        a.Send(SyncProtocol.Spawn(thrown, "Net/Box", "m1", EntityFlags.None, new[] { Blob(3) }));
        Step(a, b);
        Check(a.Take(ToClient.Despawn, thrown) != null && a.client.IsOpen, "a filter that throws refuses (the connection stays)");
        server.SpawnFilter = null;

        Section("ClaimFilter");
        ulong car = server.SpawnWorldEntity(DropKey, "m1", PlacedFlags, new[] { Blob(9, 9) });
        Step(a, b);
        Clear(a, b);
        server.ClaimFilter = (conn, id) => conn != a.number;
        a.Send(SyncProtocol.Claim(car));
        Step(a, b);
        Check(a.Take(ToClient.Owner, car)?.owner == 0 && !b.Has(ToClient.Owner), "refusing: the claimer alone gets Owner (still 0)");
        b.Send(SyncProtocol.Claim(car));
        Step(a, b);
        Check(b.Take(ToClient.Owner, car)?.owner == b.number && a.Take(ToClient.Owner, car)?.owner == b.number, "allowing: the claimer owns it, its map is told");

        Section("StateFilter");
        byte[] given = null;
        server.StateFilter = (conn, id, component, blob) => { given = blob; return blob[0] == 1 ? null : new byte[] { 9, blob[1] }; };
        b.Send(SyncProtocol.State(car, 0, Blob(1, 5)));
        Step(a, b);
        Check(!a.Has(ToClient.State) && Same(Stored(car), Blob(9, 9)), "null: the State is dropped, the server keeps what it had");
        b.Send(SyncProtocol.State(car, 0, Blob(2, 6)));
        Step(a, b);
        Check(a.Take(ToClient.State, car) is QaMsg st && Same(st.blob, Blob(9, 6)) && Same(Stored(car), Blob(9, 6)) && Same(given, Blob(2, 6)),
            "a changed blob is the one kept and sent (the filter got the rider's)");
        server.StateFilter = null; server.ClaimFilter = null;
        b.Send(SyncProtocol.Despawn(car));
        Step(a, b);
        Check(Exists(car), "the rider cannot despawn the car");
        b.Send(SyncProtocol.Release(car));
        Step(a, b);

        Section("CharacterEntering");
        var entered = new List<string>();
        server.CharacterEntering = (conn, account) => { entered.Add(conn + ":" + account); server.World.Find(account).content = new byte[] { 42 }; };
        var z = Join("acc-z", "QA제트", "m1");
        Check(entered.Contains(z.number + ":acc-z") && server.World.Find("acc-z").content[0] == 42, "a new character: called with its connection and account");
        z.client.Close();
        server.Tick();
        server.World.Find("acc-z").content = new byte[] { 1 };
        var again = new QaPeer(loopback.Connect());
        again.Send(SyncProtocol.Hello("acc-z", "acc-z", "m1"));
        server.Tick();
        again.Pump();
        var character = again.Take(m => m.kind == ToClient.Character);
        Check(character != null && character.has && Same(character.blob, Blob(42)), "coming back: called before its Character goes out (the content it gets is the changed one)");
        server.CharacterEntering = null;
        server.Stop();
    }

    // ---- ServerDrops and the ledger, on a world with a dropped item and a car of 4b (made by clients) ----

    static ServerLedger memLedger;
    static ServerDrops memDrops;

    static void ServerParts(List<StartDeedSave> deeds = null)
    {
        ulong world = server.SpawnServerEntity(WorldKey, AllMaps, EntityFlags.None, new byte[ServerWorld.SectionCount][]);
        var books = new ServerBooks(server, world, null);
        memLedger = new ServerLedger(server, world, books, deeds);
        memDrops = new ServerDrops(server, memLedger, books);
    }

    static void EndParts()
    {
        memDrops?.End(); memLedger?.End();
        memDrops = null; memLedger = null;
    }

    static string Do(QaPeer who, string kind, SharedOpArgs args) => Do(who, kind, JsonUtility.ToJson(args));

    static string Do(QaPeer who, string kind, string json)
    {
        string account = server.AccountOf(who.number);
        string error = ServerDrops.Kinds.Contains(kind) ? memDrops.Apply(who.number, account, kind, json) : memLedger.Apply(who.number, account, kind, json);
        server.Tick();
        return error ?? "";
    }

    static SharedOpArgs DropArgs(string itemId, string instance, int count, Vector3 at, int slot = -1, bool equipment = false) =>
        new SharedOpArgs { a = itemId, b = instance, n = count, c = slot.ToString(), f = equipment, d = DropBlob.FormatPlace(at, 0f) };

    static int VehicleKinds => Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle").Length + Resources.LoadAll<ItemData>("Inventory/Vehicles/Cars").Length;

    static int Vehicles(InventoryState bag)
    {
        var kinds = new HashSet<ItemData>();
        foreach (var s in InventoryLedgerRules.Stacks(bag)) if (s.Item.IsVehicle) kinds.Add(s.Item);
        return kinds.Count;
    }

    static int Deeds(InventoryState bag)
    {
        int n = 0;
        foreach (var s in InventoryLedgerRules.Stacks(bag)) if (InventoryLedgerRules.IsStartingDeed(s.Item)) n++;
        return n;
    }

    static ItemStack FirstOf(InventoryState bag, Func<ItemStack, bool> match)
    {
        foreach (var s in InventoryLedgerRules.Stacks(bag)) if (match(s)) return s;
        return null;
    }

    // "itemId xN" of a drop's state (its first stack).
    static string ItemsOf(byte[] blob)
    {
        var contents = new InventoryState();
        if (!DropBlob.TryDecode(blob, out _, out _, out _, out byte[] packed) || !DropBlob.Unpack(packed, contents)) return "";
        var s = FirstOf(contents, x => true);
        return s == null ? "" : s.Item.itemId + " x" + s.Count;
    }

    static void DropsOnOldWorld()
    {
        Section("4b entities read");
        var gimbap = ItemOf("food:gimbap");
        var bike = Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle")[0];
        string w = WorldStore.NewId();
        var store = WorldStore.Open(root, w);
        var oldDrop = new InventoryState(); oldDrop.TryAdd(gimbap, 3, out _);
        var oldCar = new InventoryState(); oldCar.TryAdd(bike, 1, out _, "qa-4b-bike");
        ulong dropId = SyncProtocol.MakeId(77, 1), carId = SyncProtocol.MakeId(77, 2);
        byte[] carContents = DropBlob.Pack(oldCar);
        store.Entities.Add(new WorldStore.SavedEntity { id = dropId, prefab = DropKey, map = "m1", flags = DroppedFlags, created = Day + 400, blobs = new[] { DropBlob.Encode(DropBlob.ItemKind, new Vector3(1, 0, 1), 0, DropBlob.Pack(oldDrop)) } });
        store.Entities.Add(new WorldStore.SavedEntity { id = carId, prefab = DropKey, map = "m1", flags = PlacedFlags, created = Day + 400, blobs = new[] { DropBlob.Encode(DropBlob.VehicleKind, new Vector3(5, 0, 5), 90, carContents) } });
        store.WorldMinutes = Day + 420;
        store.Save();
        var registry = SetRegistry(new List<PropertySave> { new PropertySave { id = "vehicle:qa-4b-bike", owner = "acc-b", vehicleLocked = true } });
        StartServer(w);
        ServerParts();
        var a = Join("acc-a", "QA가", "m1");
        var b = Join("acc-b", "QA나", "m1");
        Clear(a, b);
        var bagA = memLedger.Bag("acc-a");
        Check(Do(a, "p.pickup", new SharedOpArgs { n = (long)dropId }) == "" && CountOf(bagA, "food:gimbap") == 3 && !Exists(dropId),
            "a 4b dropped item is picked up by request: its 3 gimbap are in the ledger, the entity is gone");
        Check(a.Take(ToClient.Despawn, dropId) != null && b.Take(ToClient.Despawn, dropId) != null, "everyone on the map gets its Despawn");
        Check(CharacterSync.Read(server.World.Find("acc-a").content, out var saved) && saved.inventory.Contains("food:gimbap"), "the ledger went into the character's content at once");
        Check(Do(a, "p.pickup", new SharedOpArgs { n = (long)dropId }) == Gone && CountOf(bagA, "food:gimbap") == 3, "again: 다른 사람이 먼저 주웠습니다, nothing more");
        Check(Do(a, "p.pickup", new SharedOpArgs { n = (long)carId }) == "설치된 탈것은 해체한 다음 주워 주세요.", "a placed car is not picked up");

        Section("ServerDrops hooks");
        ulong forged = SyncProtocol.MakeId(a.number, 1);
        a.Send(SyncProtocol.Spawn(forged, DropKey, "m1", DroppedFlags, new[] { DropBlob.Encode(DropBlob.ItemKind, Vector3.zero, 0, DropBlob.Pack(oldDrop)) }));
        Step(a, b);
        Check(a.Take(ToClient.Despawn, forged) != null && !Exists(forged) && memDrops.SpawnsRefused == 1, "a client's Spawn of Net/WorldDrop is refused (Despawn)");
        a.Send(SyncProtocol.Claim(carId));
        Step(a, b);
        Check(a.Take(ToClient.Owner, carId)?.owner == 0 && memDrops.ClaimsRefused == 1, "the 4b car is locked by acc-b: acc-a's Claim is refused");
        b.Send(SyncProtocol.Claim(carId));
        Step(a, b);
        Check(b.Take(ToClient.Owner, carId)?.owner == b.number, "its owner's Claim goes");
        var other = new InventoryState(); other.TryAdd(Resources.LoadAll<ItemData>("Inventory/Vehicles/Cars")[0], 1, out _, "qa-forged-car");
        Clear(a, b);
        b.Send(SyncProtocol.State(carId, 0, DropBlob.Encode(DropBlob.ItemKind, new Vector3(7, 0, 7), 45, DropBlob.Pack(other))));
        Step(a, b);
        DropBlob.TryDecode(Stored(carId), out byte kind, out var at, out float yaw, out byte[] contents);
        Check(kind == DropBlob.VehicleKind && Same(contents, carContents) && at == new Vector3(7, 0, 7) && yaw == 45f,
            "a rider's State with another kind and contents: only its place is taken");
        Check(a.Take(ToClient.State, carId) is QaMsg st && ItemsOf(st.blob) == bike.itemId + " x1", "and that is what the others get");
        b.Send(SyncProtocol.Release(carId));
        Step(a, b);
        Check(Do(a, "p.dismantle", new SharedOpArgs { n = (long)carId }) == "소유자가 해체를 허용하지 않았습니다." && Exists(carId), "acc-a may not dismantle acc-b's car");
        string bBag = InventorySnapshot.Export(memLedger.Bag("acc-b"), false);
        Clear(a, b);
        Check(Do(b, "p.dismantle", new SharedOpArgs { n = (long)carId }) == "" && !Exists(carId), "its owner dismantles it");
        var pieces = a.Take(m => m.kind == ToClient.Spawn && m.key == DropKey && m.flags == DroppedFlags);
        Check(pieces != null && ItemsOf(pieces.blobs[0]) == bike.itemId + " x1" && InventorySnapshot.Export(memLedger.Bag("acc-b"), false) == bBag,
            "it became a dropped item of the same contents; the ledger did not change");
        Check(Do(a, "p.pickup", new SharedOpArgs { n = (long)pieces.id }) == "" && InventoryLedgerRules.FindUnique(bagA, "qa-4b-bike")?.Item == bike, "picked up, it is the 4b car itself (qa-4b-bike)");

        Section("p.drop and p.vehicle.place");
        Clear(a, b);
        Check(Do(a, "p.drop", DropArgs("food:gimbap", null, 5, new Vector3(2, 0, 2))) == "가지고 있지 않은 아이템입니다." && CountOf(bagA, "food:gimbap") == 3, "5 gimbap with 3: refused");
        Check(Do(a, "p.drop", DropArgs("food:water", null, 1, new Vector3(2, 0, 2))) != "", "an item not held: refused");
        Check(Do(a, "p.drop", DropArgs("food:gimbap", null, 2, new Vector3(2, 0, 2), 0)) == "" && CountOf(bagA, "food:gimbap") == 1, "2 gimbap named in another slot: taken from where they are");
        var dropped = b.Take(m => m.kind == ToClient.Spawn && m.key == DropKey && m.flags == DroppedFlags);
        Check(dropped != null && dropped.owner == 0 && ItemsOf(dropped.blobs[0]) == "food:gimbap x2", "the drop: server-owned, 2 gimbap");
        ulong gimbapDrop = dropped.id;
        Check(Do(a, "p.drop", DropArgs("food:gimbap", null, 1, new Vector3(float.NaN, 0, 0))) != "" && CountOf(bagA, "food:gimbap") == 1, "a place that is no number: refused");
        var book = new BookInstanceData { title = "장부의 제목", authorPlayerId = "acc-a", authorName = "QA가", isPublished = true };
        bagA.TryAddBookInstance(BlankBook, "qa-mem-book", book, out _);
        string json = JsonUtility.ToJson(DropArgs(BlankBook.itemId, "qa-mem-book", 1, new Vector3(3, 0, 3))).TrimEnd('}') + ",\"title\":\"가짜 제목\",\"book\":{\"title\":\"가짜 제목\"}}";
        Clear(a, b);
        Check(Do(a, "p.drop", json) == "" && bagA.FindBook("qa-mem-book") == null, "a book (by its instance id) leaves the ledger");
        var bookDrop = b.Take(m => m.kind == ToClient.Spawn && m.key == DropKey);
        Check(bookDrop != null && BookIn(SnapshotOf(bookDrop.blobs[0]), "qa-mem-book")?.title == "장부의 제목", "its drop has the ledger's title, not the one in the request");
        bagA.TryAdd(Cash(10000), 1, out _); bagA.TryAdd(Cash(5000), 1, out _);
        Clear(a, b);
        Check(Do(a, "p.drop", DropArgs("cash:1000", null, 3, new Vector3(3, 0, 3))) == "" && CashService.CarriedTotal(bagA) == 12000, "three 1,000원 notes not held as such: paid from the 15,000원 (12,000원 left)");
        Check(b.Take(m => m.kind == ToClient.Spawn && m.key == DropKey) is QaMsg cash && ItemsOf(cash.blobs[0]) == "cash:1000 x3", "the drop holds exactly 3,000원");
        Check(bagA.TryAdd(Shirt, 1, out _) && bagA.TryEquip(SlotOf(bagA, s => s.Item == Shirt), EquipmentSlot.Top, out _), "a shirt worn in the ledger");
        Check(Do(a, "p.drop", DropArgs(Shirt.itemId, null, 1, new Vector3(3, 0, 3), (int)EquipmentSlot.Top, true)) == "" && bagA.GetEquipment(EquipmentSlot.Top).IsEmpty, "dropped from its equipment slot");
        var ownCar = FirstOf(bagA, s => s.Item.IsCar);
        Clear(a, b);
        Check(Do(a, "p.vehicle.place", new SharedOpArgs { a = ownCar.InstanceId, d = DropBlob.FormatPlace(new Vector3(9, 0, 9), 30) }) == "" && InventoryLedgerRules.FindUnique(bagA, ownCar.InstanceId) == null,
            "p.vehicle.place: the car leaves the ledger");
        var placed = b.Take(m => m.kind == ToClient.Spawn && m.key == DropKey && m.flags == PlacedFlags);
        Check(placed != null && ItemsOf(placed.blobs[0]) == ownCar.Item.itemId + " x1", "and stands there (a placed vehicle, no Expiring)");
        Check(Do(a, "p.vehicle.place", new SharedOpArgs { a = "qa-not-mine", d = DropBlob.FormatPlace(Vector3.zero, 0) }) == "가지고 있지 않은 탈것입니다.", "a car not in the ledger: refused");
        var c = Join("acc-c", "QA다", "Assets/Scenes/Interiors/QA.unity");
        var inside = FirstOf(memLedger.Bag("acc-c"), s => s.Item.IsVehicle);
        Check(Do(c, "p.vehicle.place", new SharedOpArgs { a = inside.InstanceId, d = DropBlob.FormatPlace(Vector3.zero, 0) }) == "탈것은 실외에서 설치해 주세요."
              && InventoryLedgerRules.FindUnique(memLedger.Bag("acc-c"), inside.InstanceId) != null, "on an interior map: refused, the car stays in the ledger");
        Check(Do(c, "p.pickup", new SharedOpArgs { n = (long)gimbapDrop }) == "지금은 주울 수 없습니다." && Exists(gimbapDrop), "a pickup from another map: refused");

        Section("restart");
        string deedA = memLedger.DeedOf("acc-a");
        var deeds = memLedger.CaptureDeeds();
        string carKind = ownCar.Item.itemId;
        Check(Do(a, "p.drop", DropArgs(FirstOf(bagA, s => InventoryLedgerRules.IsStartingDeed(s.Item)).Item.itemId, null, 1, new Vector3(4, 0, 4))) == "" && Deeds(bagA) == 0, "acc-a drops its deed");
        a.client.Close();
        server.Tick();
        EndParts();
        server.Stop();
        StartServer(w);
        ServerParts(deeds);
        Check(Exists(gimbapDrop) && ItemsOf(Stored(gimbapDrop)) == "food:gimbap x2" && Exists(placed.id), "after a restart the drop and the placed car are there");
        bagA = memLedger.Bag("acc-a");
        Check(CountOf(bagA, "food:gimbap") == 1 && InventoryLedgerRules.FindUnique(bagA, "qa-4b-bike") != null && CashService.CarriedTotal(bagA) == 12000,
            "and the ledger read from the saved content has what was left (1 gimbap, the 4b car, 12,000원)");
        a = Join("acc-a", "QA가", "m1");
        Check(Deeds(bagA) == 0 && memLedger.DeedOf("acc-a") == deedA, "coming back without its deed: no second one (once per account)");
        Check(Vehicles(bagA) == VehicleKinds && FirstOf(bagA, s => s.Item.itemId == carKind) != null, "the start vehicles it lacked (the car it placed) are given again");
        Check(Do(a, "p.pickup", new SharedOpArgs { n = (long)gimbapDrop }) == "" && CountOf(bagA, "food:gimbap") == 3 && !Exists(gimbapDrop), "picked up after the restart: in the ledger");
        EndParts();
        server.Stop();
        SetRegistry(null);
    }

    static void StartingDeeds()
    {
        Section("the goshiwon deeds");
        var deeds = new List<ItemData>(Resources.LoadAll<ItemData>("Inventory/Deeds")).FindAll(InventoryLedgerRules.IsStartingDeed);
        deeds.Sort((x, y) => string.CompareOrdinal(x.itemId, y.itemId));
        Check(deeds.Count == 20, "20 goshiwon rooms");
        string w = WorldStore.NewId();
        var store = WorldStore.Open(root, w);
        var held = new InventoryState(); held.TryAdd(deeds[0], 1, out _);
        store.Create("acc-old", "옛사람", "female", out var old);
        old.content = CharacterSync.Encode(new CharacterSync.Saved { map = "m1", personal = "", inventory = InventorySnapshot.Export(held, false) });
        store.Save();
        SetRegistry(new List<PropertySave> { new PropertySave { id = deeds[1].propertyId, owner = "" } }); // given up once: someone had it
        StartServer(w);
        ServerParts();
        var given = new HashSet<string>();
        int none = 0;
        for (int i = 1; i <= 20; i++)
        {
            var p = Join("acc-" + i, "QA" + i, "m1");
            string room = memLedger.DeedOf("acc-" + i);
            if (room == null) none++;
            else if (!given.Add(room)) Check(false, "room " + room + " given twice");
            p.client.Close();
            server.Tick();
        }
        Check(given.Count == 18 && none == 2, "20 new accounts: 18 rooms, each once; the last 2 get none (" + given.Count + " given)");
        Check(!given.Contains(deeds[0].propertyId) && !given.Contains(deeds[1].propertyId), "never the room an older character holds nor one on the registry");
        var again = Join("acc-1", "QA1", "m1");
        Check(Deeds(memLedger.Bag("acc-1")) == 1, "coming back with it: still one");
        EndParts();
        server.Stop();
        SetRegistry(null);
    }

    // ================= in-app server (WorldItemQA.RunPlay) =================

    struct ItemAnswer { public string error, inventory; public List<QaMsg> before; }

    static Task<ItemAnswer> ItemOp(QaPeer fake, string kind, SharedOpArgs args) => ItemOpJson(fake, kind, JsonUtility.ToJson(args));

    // A fake's request: its error, its Ledger's bag and every message before its OpResult (the Spawns it made).
    static async Task<ItemAnswer> ItemOpJson(QaPeer fake, string kind, string json)
    {
        Drain(fake);
        fake.Send(SyncProtocol.Event(worldId, 0, WorldSection.OpEvent, EventTarget.Server, Str2(kind, json)));
        float end = Time.realtimeSinceStartup + 5f;
        while (true)
        {
            fake.Pump();
            int at = fake.inbox.FindIndex(m => m.kind == ToClient.Event && m.id == worldId && m.eventId == WorldSection.OpResultEvent && m.sender == 0);
            if (at >= 0)
            {
                var answer = new ItemAnswer { before = fake.inbox.GetRange(0, at) };
                foreach (var m in answer.before) if (IsLedger(m)) answer.inventory = ReadInvLedger(m.blob).inventory;
                using (var r = SyncProtocol.Reader(fake.inbox[at].blob))
                {
                    Check(SyncProtocol.ReadStr(r) == kind, "OpResult for " + kind);
                    answer.error = SyncProtocol.ReadStr(r);
                }
                fake.inbox.RemoveRange(0, at + 1);
                return answer;
            }
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for the answer to " + kind);
            await Awaitable.NextFrameAsync();
        }
    }

    static QaMsg SpawnIn(ItemAnswer a, EntityFlags flags) => a.before.Find(m => m.kind == ToClient.Spawn && m.key == DropKey && m.flags == flags);

    static void Give(ServerLedger ledger, string account, ItemData item, int count)
    {
        if (!ledger.Bag(account).TryAdd(item, count, out string error)) throw new Exception("[" + tag + "] FAIL: give " + error);
        ledger.Store(account);
    }

    // At a spot, facing a way (StandBeside stands 1.6 m from one).
    static async Task StandAt(PlayerMovement player, Vector3 at, Vector3 face)
    {
        var motor = player.GetComponent<CharacterController>();
        motor.enabled = false;
        player.transform.SetPositionAndRotation(at, Quaternion.LookRotation(face));
        motor.enabled = true;
        Physics.SyncTransforms();
        await Awaitable.WaitForSecondsAsync(.3f);
    }

    static bool InWorld(ulong id) => SyncHost.Instance.Server.TryGetEntity(id, out _, out _, out _, out _, out _);

    static async Task<WorldDroppedItem> ViewOf(Func<WorldDroppedItem, bool> match, string what)
    {
        WorldDroppedItem found = null;
        await WaitFor(() => (found = Array.Find(Object.FindObjectsByType<WorldDroppedItem>(), v => v && v.NetId != 0 && match(v))) != null, 5, what);
        return found;
    }

    static async Task<bool> DropFromBag(PlayerInventory inv, int slot, int count)
    {
        if (!inv.OpenInventory()) return false;
        bool ok = WorldDroppedItem.TryDropStorage(inv, slot, out string error, count);
        inv.CloseInventory();
        if (!ok) Debug.LogWarning("[" + tag + "] drop: " + error);
        await Awaitable.NextFrameAsync();
        return ok;
    }

    static async Task ItemPlayBody()
    {
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy && WorldSave.Baseline != null, 15, "title");
        ProfileSlot.Set(Slot);
        var client = SyncClient.Instance;

        Section("entering: what the server gives");
        var settings = new ServerSettings { name = "WorldItemQA", maxPlayers = 4, port = 0, world = WorldStore.NewId() };
        var host = SyncHost.StartInApp(settings, true);
        playWorld = host.Server.World.Folder;
        await WaitFor(() => client.Connected && client.CharacterKnown, 10, "the Unity client on the in-app server");
        Check(await TitleFlow.CreateServerCharacter("QA아이템", "male") == null, "the Unity owner made its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        string map = SceneLoadManager.CurrentMap.path, me = GameSession.LocalPlayerId;
        var ledger = ServerWorld.Instance.Ledger;
        var drops = ServerWorld.Instance.Drops;
        var bag = InventoryManager.Instance.State;
        var player = PlayerSpawner.Player;
        var inv = player.GetComponent<PlayerInventory>();
        var driver = player.GetComponent<PlayerVehicle>();
        await WaitFor(() => WorldStateSync.LayoutsSent > 0 && WorldStateSync.Ledgers > 0, 10, "the first layout and its Ledger");
        await Settle(ledger, me, "the owner's start");
        await Awaitable.WaitForSecondsAsync(1f);
        Check(Vehicles(ledger.Bag(me)) == VehicleKinds && ledger.DeedOf(me) != null && Deeds(ledger.Bag(me)) == 1,
            "a new character: the " + VehicleKinds + " start vehicles and one goshiwon deed (" + ledger.DeedOf(me) + ") in its ledger");
        Check(Deeds(bag) == 1 && InventoryLedgerRules.SameContents(bag, ledger.Bag(me)) && WorldStateSync.InventoryOverwrites == 0,
            "the screen's bag is the server's start bag (this client gave itself nothing), nothing written over");
        var fake = await JoinFake(host.Port, map, ItemFake, "QA아이템가짜");
        var fb = ledger.Bag(ItemFake);
        Check(ledger.DeedOf(ItemFake) != null && ledger.DeedOf(ItemFake) != ledger.DeedOf(me) && Vehicles(fb) == VehicleKinds,
            "the fake gets another room (" + ledger.DeedOf(ItemFake) + ") and its start vehicles");
        var cam = Camera.main.transform;
        var ahead = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        var side = Vector3.Cross(Vector3.up, ahead);
        Vector3 home = player.transform.position;
        float faceYaw = Quaternion.LookRotation(ahead).eulerAngles.y;
        string ownerBag = InventorySnapshot.Export(bag, false);
        int ownerLedgers = WorldStateSync.Ledgers;

        Section("a client's Spawn of a drop");
        int spawnsRefused = drops.SpawnsRefused;
        var forgedContents = new InventoryState(); forgedContents.TryAdd(ItemOf("food:gimbap"), 50, out _);
        ulong forged = SyncProtocol.MakeId(fake.number, 1);
        Drain(fake);
        fake.Send(SyncProtocol.Spawn(forged, DropKey, map, DroppedFlags, new[] { DropOf(InventorySnapshot.Export(forgedContents, false), home + ahead * 2f) }));
        await WaitMsg(fake, m => m.kind == ToClient.Despawn && m.id == forged, "the Despawn of the forged drop");
        await Awaitable.WaitForSecondsAsync(.5f);
        Check(drops.SpawnsRefused == spawnsRefused + 1 && !InWorld(forged) && !Array.Exists(Object.FindObjectsByType<WorldDroppedItem>(), v => v && v.NetId == forged),
            "refused: the fake gets Despawn, nothing is in the world or on the Unity screen");

        Section("p.drop");
        var gimbap = ItemOf("food:gimbap");
        Give(ledger, ItemFake, gimbap, 3);
        var book = new BookInstanceData { title = "장부의 제목", authorPlayerId = ItemFake, authorName = "QA아이템가짜", isPublished = true };
        Check(fb.TryAddBookInstance(BlankBook, "qa-item-book", book, out _), "a book in the fake's ledger");
        ledger.Store(ItemFake);
        Vector3 spot = Ground(home + ahead * 3f + side * 1.5f);
        var a = await ItemOp(fake, "p.drop", DropArgs(gimbap.itemId, null, 5, spot));
        Check(a.error != "" && CountOf(fb, gimbap.itemId) == 3 && SpawnIn(a, DroppedFlags) == null, "5 gimbap with 3 in the ledger: refused, nothing made (" + a.error + ")");
        a = await ItemOp(fake, "p.drop", DropArgs("food:water", null, 1, spot));
        Check(a.error != "" && SpawnIn(a, DroppedFlags) == null, "a water it does not have: refused (" + a.error + ")");
        a = await ItemOp(fake, "p.drop", DropArgs(gimbap.itemId, null, 2, spot));
        var spawn = SpawnIn(a, DroppedFlags);
        Check(a.error == "" && CountOf(fb, gimbap.itemId) == 1 && spawn != null && spawn.owner == 0 && ItemsOf(spawn.blobs[0]) == "food:gimbap x2",
            "2 of them: out of the ledger (1 left), a server-owned drop of 2 gimbap");
        ulong gimbapDrop = spawn.id;
        string json = JsonUtility.ToJson(DropArgs(BlankBook.itemId, "qa-item-book", 1, spot + side)).TrimEnd('}') + ",\"title\":\"가짜가 말한 제목\"}";
        a = await ItemOpJson(fake, "p.drop", json);
        spawn = SpawnIn(a, DroppedFlags);
        Check(a.error == "" && fb.FindBook("qa-item-book") == null && spawn != null && BookIn(SnapshotOf(spawn.blobs[0]), "qa-item-book")?.title == "장부의 제목",
            "the book: its drop has the ledger's title, not what the fake said");
        ulong bookDrop = spawn.id;
        await ViewOf(v => v.NetId == gimbapDrop, "the fake's drop on the Unity screen");

        Section("p.pickup");
        fake.Send(SyncProtocol.Map("QA/Elsewhere"));
        a = await ItemOp(fake, "p.pickup", new SharedOpArgs { n = (long)gimbapDrop });
        Check(a.error == "지금은 주울 수 없습니다." && InWorld(gimbapDrop), "from another map: refused");
        fake.Send(SyncProtocol.Map(map));
        var fillers = new List<string>();
        var filler = Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle")[0];
        for (int i = 0; EmptySlot(fb) >= 0; i++) { fb.TryAdd(filler, 1, out _, "qa-fill-" + i); fillers.Add("qa-fill-" + i); }
        a = await ItemOp(fake, "p.pickup", new SharedOpArgs { n = (long)bookDrop });
        Check(a.error == "가방에 자리가 없습니다." && InWorld(bookDrop), "with a full bag: refused, the drop stays");
        foreach (string id in fillers) fb.TryRemove(SlotOf(fb, s => s.InstanceId == id), 1, out _);
        ledger.Store(ItemFake);
        a = await ItemOp(fake, "p.pickup", new SharedOpArgs { n = (long)gimbapDrop });
        Check(a.error == "" && CountOf(fb, gimbap.itemId) == 3 && !InWorld(gimbapDrop) && a.before.Exists(m => m.kind == ToClient.Despawn && m.id == gimbapDrop),
            "picked up: the 2 are back in the ledger, the drop is gone");
        a = await ItemOp(fake, "p.pickup", new SharedOpArgs { n = (long)gimbapDrop });
        Check(a.error == Gone && CountOf(fb, gimbap.itemId) == 3, "once more: " + Gone);
        a = await ItemOp(fake, "p.pickup", new SharedOpArgs { n = (long)bookDrop });
        Check(a.error == "" && fb.FindBook("qa-item-book")?.BookData.title == "장부의 제목", "the book back, as the ledger had it");

        Section("a rider's State");
        var fakeCar = FirstOf(fb, s => s.Item.IsCar);
        string carInstance = fakeCar.InstanceId;
        var carItem = fakeCar.Item;
        Vector3 carSpot = Ground(home + ahead * 5f + side * 4.5f);
        a = await ItemOp(fake, "p.vehicle.place", new SharedOpArgs { a = carInstance, d = DropBlob.FormatPlace(carSpot, faceYaw) });
        spawn = SpawnIn(a, PlacedFlags);
        Check(a.error == "" && spawn != null && InventoryLedgerRules.FindUnique(fb, carInstance) == null, "the fake places its car (out of its ledger)");
        ulong car = spawn.id;
        var carView = await ViewOf(v => v.NetId == car, "the fake's car on the Unity screen");
        fake.Send(SyncProtocol.Claim(car));
        Check((await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == car, "the fake's Claim")).owner == fake.number, "the fake gets in");
        var forgedCar = new InventoryState(); forgedCar.TryAdd(filler, 1, out _, "qa-forged-bike");
        Vector3 moved = carSpot + side * 1.5f;
        fake.Send(SyncProtocol.State(car, 0, DropBlob.Encode(DropBlob.VehicleKind, moved, faceYaw + 20f, DropBlob.Pack(forgedCar))));
        await Awaitable.WaitForSecondsAsync(.5f);
        host.Server.TryGetEntity(car, out _, out _, out _, out _, out var carBlobs);
        var kept = new InventoryState();
        DropBlob.TryDecode(carBlobs[0], out _, out var keptAt, out _, out byte[] keptContents);
        DropBlob.Unpack(keptContents, kept);
        Check(InventoryLedgerRules.FindUnique(kept, carInstance)?.Item == carItem && InventoryLedgerRules.FindUnique(kept, "qa-forged-bike") == null && (keptAt - moved).sqrMagnitude < 1e-4f,
            "a State with another vehicle inside: the server keeps its car, takes only the place");
        Check(carView && carView.VehicleOwnershipKey == "vehicle:" + carInstance, "the Unity screen still has the fake's own car");
        fake.Send(SyncProtocol.Release(car));
        await WaitMsg(fake, m => m.kind == ToClient.Owner && m.id == car && m.owner == 0, "the fake out of the car");
        a = await ItemOp(fake, "p.dismantle", new SharedOpArgs { n = (long)car });
        spawn = SpawnIn(a, DroppedFlags);
        Check(a.error == "" && spawn != null && !InWorld(car), "dismantled: a dropped item");
        a = await ItemOp(fake, "p.pickup", new SharedOpArgs { n = (long)spawn.id });
        Check(a.error == "" && InventoryLedgerRules.FindUnique(fb, carInstance)?.Item == carItem, "picked up, it is the original car (" + carInstance + ")");

        Section("trust and grant");
        foreach (string reason in new[] { "drop", "pickup", "vehicle", "storage", "grant", "library" })
        {
            a = await ItemOp(fake, "p.inv.trust", new SharedOpArgs { a = reason, b = InventoryLedgerRules.Pack(fb) });
            Check(a.error == "처리할 수 없는 요청입니다.", "p.inv.trust " + reason + ": refused (library too since 12.10.6)");
        }
        a = await ItemOp(fake, "p.grant", new SharedOpArgs { a = "food:gimbap" });
        Check(a.error == "지급할 수 없는 아이템입니다." && CountOf(fb, gimbap.itemId) == 3, "p.grant of an item not on the list: refused");
        a = await ItemOp(fake, "p.grant", new SharedOpArgs { a = Card });
        Check(a.error == "" && CountOf(fb, Card) == 1, "p.grant of the employee card: one");
        a = await ItemOp(fake, "p.grant", new SharedOpArgs { a = Card });
        Check(a.error == "이미 가지고 있습니다." && CountOf(fb, Card) == 1, "again while it has one: refused");
        Check(WorldStateSync.Ledgers == ownerLedgers && InventorySnapshot.Export(bag, false) == ownerBag && InventoryLedgerRules.SameContents(bag, ledger.Bag(me)),
            "none of the fake's requests changed the Unity owner's bag or reached it as a Ledger");

        Section("one drop, two pickers");
        int raceOverwrites = 0, races = 0;
        bool unityLost = false;
        while (!unityLost && races < 3)
        {
            races++;
            Give(ledger, ItemFake, gimbap, 3);
            Vector3 near = Ground(player.transform.position + ahead * 1.2f);
            a = await ItemOp(fake, "p.drop", DropArgs(gimbap.itemId, null, 3, near));
            ulong race = SpawnIn(a, DroppedFlags).id;
            var view = await ViewOf(v => v.NetId == race, "the race drop on the Unity screen");
            int screen = CountOf(bag, gimbap.itemId), total = CountOf(ledger.Bag(me), gimbap.itemId) + CountOf(fb, gimbap.itemId) + 3;
            int overwrites = WorldStateSync.InventoryOverwrites, results = WorldStateSync.Results;
            Drain(fake);
            fake.Send(SyncProtocol.Event(worldId, 0, WorldSection.OpEvent, EventTarget.Server, Str2("p.pickup", JsonUtility.ToJson(new SharedOpArgs { n = (long)race }))));
            bool asked = view.TryPickUp(inv, out string pickError);
            Check(asked && CountOf(bag, gimbap.itemId) == screen + 3 && !view.gameObject.activeSelf, "race " + races + ": the Unity owner's 3 are in its bag at once, the drop hidden " + pickError);
            var fakeAnswer = await WaitOp(fake);
            await WaitFor(() => WorldStateSync.Results > results, 5, "the Unity owner's answer");
            unityLost = fakeAnswer.error == "";
            if (unityLost)
            {
                await WaitFor(() => WorldStateSync.LastMessage == Gone, 3, "the loser's message");
                await Shot("W_race_lost.png", .2f);
            }
            await WaitFor(() => InventoryLedgerRules.SameContents(bag, ledger.Bag(me)), 6, "the Unity bag agreeing with its ledger");
            await Awaitable.WaitForSecondsAsync(.3f);
            int unityGot = CountOf(bag, gimbap.itemId) - screen, fakeGot = unityLost ? 3 : 0;
            Check(unityGot + fakeGot == 3 && (unityGot == 0 || fakeGot == 0), "only one got it (" + (unityLost ? "the fake" : "the Unity owner") + "): " + unityGot + " + " + fakeGot + " = 3");
            Check(!InWorld(race) && !view && CountOf(ledger.Bag(me), gimbap.itemId) + CountOf(fb, gimbap.itemId) == total, "the world and the two ledgers hold as many as before (" + total + ")");
            if (unityLost) Check(WorldStateSync.InventoryOverwrites == overwrites + 1, "the loser's bag was put back by its Ledger (1 overwrite: " + Gone + ")");
            raceOverwrites += WorldStateSync.InventoryOverwrites - overwrites;
        }
        Note("pickup races until the Unity owner lost: " + races);

        Section("the Unity owner: 0 overwrites");
        int ow = WorldStateSync.InventoryOverwrites, all = WorldStateSync.Overwrites;
        string err;
        await StandAt(player, home, ahead);
        Check(CashService.TryWithdraw(bag, 10000, 2, out err), "withdraw 2 x 10,000원 " + err);
        await Settle(ledger, me, "the withdrawal");
        long cash = CashService.CarriedTotal(bag);
        Check(await DropFromBag(inv, SlotOf(bag, s => s.Item.IsCurrency && s.Item.CurrencyValue == 10000), 1) && CashService.CarriedTotal(bag) == cash - 10000, "a 10,000원 note dropped (out of the bag at once)");
        var cashView = await ViewOf(v => v.Item && v.Item.IsCurrency, "the note on the ground");
        await Settle(ledger, me, "the cash drop");
        Check(CashService.CarriedTotal(ledger.Bag(me)) == cash - 10000, "the ledger's cash too");
        await StandBeside(player, cashView.transform.position);
        Check(cashView.TryPickUp(inv, out err) && CashService.CarriedTotal(bag) == cash, "picked up again " + err);
        await Settle(ledger, me, "the cash pickup");
        Give(ledger, me, Shirt, 1);
        Check(bag.TryAdd(Shirt, 1, out err), "a shirt (in the ledger too)");
        await Settle(ledger, me, "the shirt");
        Check(bag.TryEquip(SlotOf(bag, s => s.Item == Shirt), EquipmentSlot.Top, out err), "worn " + err);
        await Settle(ledger, me, "wearing");
        Check(inv.OpenInventory(), "inventory open");
        bool droppedShirt = WorldDroppedItem.TryDropEquipment(inv, EquipmentSlot.Top, out err);
        inv.CloseInventory();
        Check(droppedShirt && bag.GetEquipment(EquipmentSlot.Top).IsEmpty, "the worn shirt dropped " + err);
        var shirtView = await ViewOf(v => v.Item == Shirt, "the shirt on the ground");
        await Settle(ledger, me, "the equipment drop");
        Check(ledger.Bag(me).GetEquipment(EquipmentSlot.Top).IsEmpty && CountOf(ledger.Bag(me), Shirt.itemId) == 0, "out of the ledger's equipment");
        await StandBeside(player, shirtView.transform.position);
        Check(shirtView.TryPickUp(inv, out err), "picked up " + err);
        await Settle(ledger, me, "the shirt pickup");
        Check(await DropFromBag(inv, SlotOf(bag, s => s.Item == gimbap), 1) || CountOf(bag, gimbap.itemId) == 0, "a gimbap dropped (when it has any)");
        await Settle(ledger, me, "the gimbap");

        await StandAt(player, home, ahead);
        int bikeSlot = -1;
        for (int i = 0; i < InventoryState.HotbarSize; i++) if (bag.GetSlot(i).Item && bag.GetSlot(i).Item.IsVehicle && !bag.GetSlot(i).Item.IsCar) { bikeSlot = i; break; }
        Check(bikeSlot >= 0, "a bicycle on the hotbar");
        string bikeId = bag.GetSlot(bikeSlot).InstanceId;
        bag.SelectHotbar(bikeSlot);
        Check(WorldDroppedItem.TryPlaceVehicle(inv, out err) && InventoryLedgerRules.FindUnique(bag, bikeId) == null, "placed: out of the bag at once " + err);
        var bikeView = await ViewOf(v => v.IsPlacedVehicle && v.VehicleOwnershipKey == "vehicle:" + bikeId, "the bicycle standing");
        await Settle(ledger, me, "placing");
        Check(InventoryLedgerRules.FindUnique(ledger.Bag(me), bikeId) == null, "and out of the ledger");
        await StandBeside(player, bikeView.transform.position);
        Check(bikeView.TryMount(inv), "asks to ride it");
        await WaitFor(() => driver.IsRiding, 3, "riding");
        player.transform.position += side * 3f;
        await Awaitable.WaitForSecondsAsync(1.3f);
        Check(driver.Dismount(false), "gets off");
        await WaitFor(() => client.Find(bikeView.NetId) && client.Find(bikeView.NetId).Owner == 0, 3, "the bicycle given back");
        await StandBeside(player, bikeView.transform.position);
        Check(bikeView.TryDismantleVehicle(GameSession.LocalPlayerName, out err), "dismantles it (asks) " + err);
        var bikeItemView = await ViewOf(v => !v.IsPlacedVehicle && v.Item && v.Item.IsVehicle, "the dismantled bicycle as a drop");
        await StandBeside(player, bikeItemView.transform.position);
        Check(bikeItemView.TryPickUp(inv, out err) && InventoryLedgerRules.FindUnique(bag, bikeId) != null, "the bicycle picked up: the same one " + err);
        await Settle(ledger, me, "the bicycle back");
        Check(InventoryLedgerRules.FindUnique(ledger.Bag(me), bikeId) != null, "back in the ledger");

        var cardItem = TradeCatalog.Grant(Card);
        Check(cardItem && InventorySnapshot.Find(Card) == cardItem, "the employee card is on the grant list, and the snapshot finds it (outside Resources)");
        var desk = new GameObject("WorldItemQA card").AddComponent<EmployeeCardPickup>();
        desk.card = cardItem;
        Check(desk.TryCollect(bag) && CountOf(bag, Card) == 1, "the employee card picked up (p.grant)");
        await Settle(ledger, me, "the card");
        Check(CountOf(ledger.Bag(me), Card) == 1, "the server gave it too");
        var second = new GameObject("WorldItemQA card 2").AddComponent<EmployeeCardPickup>();
        second.card = cardItem;
        Check(!second.TryCollect(bag) && CountOf(bag, Card) == 1, "a second card while it has one: not given");
        await Settle(ledger, me, "the second card");
        Object.Destroy(desk.gameObject); Object.Destroy(second.gameObject);
        Check(WorldStateSync.InventoryOverwrites == ow && WorldStateSync.Overwrites == all,
            "drops (bag, equipment, cash), pickups, placing, riding, getting off, dismantling, the car back, the card: 0 overwrites");

        Section("entering again");
        string deedBefore = ledger.DeedOf(ItemFake);
        var fakeDeed = FirstOf(fb, s => InventoryLedgerRules.IsStartingDeed(s.Item));
        a = await ItemOp(fake, "p.drop", DropArgs(fakeDeed.Item.itemId, null, 1, spot));
        Check(a.error == "" && Deeds(fb) == 0, "the fake drops its deed");
        var fakeBike = FirstOf(fb, s => s.Item.IsVehicle && !s.Item.IsCar);
        a = await ItemOp(fake, "p.vehicle.place", new SharedOpArgs { a = fakeBike.InstanceId, d = DropBlob.FormatPlace(carSpot - side * 3f, faceYaw) });
        Check(a.error == "" && Vehicles(fb) == VehicleKinds - 1, "and places a bicycle");
        fake.client.Close();
        await Awaitable.WaitForSecondsAsync(.5f);
        fake = await JoinFake(host.Port, map, ItemFake, "QA아이템가짜");
        Check(Deeds(fb) == 0 && ledger.DeedOf(ItemFake) == deedBefore, "coming back: no second deed (once per account)");
        Check(Vehicles(fb) == VehicleKinds && InventoryLedgerRules.FindUnique(fb, fakeBike.InstanceId) == null, "the start vehicle it lacked is given again (a new one)");
        string myDeed = ledger.DeedOf(me);
        int counted = WorldStateSync.Overwrites;
        await Settle(ledger, me, "before leaving");
        host.Stop();
        await WaitFor(() => !client.Connected && !CharacterSync.Active, 5, "leaving the server");
        host = SyncHost.StartInApp(settings, true);
        await WaitFor(() => client.Connected && CharacterSync.Active && WorldStateSync.World, 30, "back on the restarted server");
        bag = InventoryManager.Instance.State;
        ledger = ServerWorld.Instance.Ledger;
        await Awaitable.WaitForSecondsAsync(2.5f);
        Check(ledger.DeedOf(me) == myDeed && Deeds(bag) == 1 && Vehicles(bag) == VehicleKinds, "the owner after a restart: the same deed record, one deed, its vehicles");
        Check(WorldStateSync.Overwrites == counted, "nothing written over after joining again");
    }

    // ================= server process (WorldItemQA.RunProcess) =================

    static async Task ItemProcessBody(string exe)
    {
        exePath = exe;
        await WaitFor(() => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null && !TitleScreen.Current.Busy && WorldSave.Baseline != null, 15, "title");
        ProfileSlot.Set(Slot);
        var client = SyncClient.Instance;
        serverFolder = Path.Combine(Path.GetDirectoryName(exe), "qa-server");
        if (Directory.Exists(serverFolder)) Directory.Delete(serverFolder, true);
        Directory.CreateDirectory(serverFolder);
        serverPort = FreePort();
        settingsFile = Path.Combine(serverFolder, "settings.json");
        var settings = new ServerSettings { name = "WorldItemQA 프로세스", maxPlayers = 4, port = serverPort, world = WorldStore.NewId() };
        File.WriteAllText(settingsFile, JsonUtility.ToJson(settings));
        string worldsRoot = Path.Combine(serverFolder, "worlds");

        Section("dropping and placing on the remote client");
        SetWorldClock(worldsRoot, settings.world, Day + 10 * 60);
        await StartProcessServer("A");
        Check(await TitleFlow.JoinServer("127.0.0.1:" + serverPort, "", false) == null && GameSession.Mode == SessionMode.Client, "the Unity client joins the server process (Client)");
        Check(await TitleFlow.CreateServerCharacter("QA아이템원격", "male") == null, "its character");
        Check(TitleFlow.EnterServerWorld() == null, "entering the world");
        await WaitFor(() => InGame && CharacterSync.Active && WorldStateSync.World, 90, "the game map");
        string me = GameSession.LocalPlayerId;
        var bag = InventoryManager.Instance.State;
        var player = PlayerSpawner.Player;
        var inv = player.GetComponent<PlayerInventory>();
        await WaitFor(() => WorldStateSync.Ledgers > 0, 10, "the first Ledger");
        await Awaitable.WaitForSecondsAsync(2.5f);
        Check(Vehicles(bag) == VehicleKinds && Deeds(bag) == 1 && WorldStateSync.InventoryOverwrites == 0, "a new character gets the server's start vehicles and a deed; nothing written over");
        string err;
        int ledgers = WorldStateSync.Ledgers;
        Check(CashService.TryWithdraw(bag, 10000, 3, out err), "withdraw 3 x 10,000원 " + err);
        await WaitFor(() => WorldStateSync.Ledgers > ledgers, 5, "its Ledger");
        await Awaitable.WaitForSecondsAsync(1.5f);
        long cash = CashService.CarriedTotal(bag);
        Check(await DropFromBag(inv, SlotOf(bag, s => s.Item.IsCurrency && s.Item.CurrencyValue == 10000), 1), "a 10,000원 note dropped");
        var cashView = await ViewOf(v => v.Item && v.Item.IsCurrency, "the note on the ground (the server's Spawn)");
        ulong cashDrop = cashView.NetId;
        await Shot("W_remote_drop.png", .2f);
        int bikeSlot = -1;
        for (int i = 0; i < InventoryState.HotbarSize; i++) if (bag.GetSlot(i).Item && bag.GetSlot(i).Item.IsVehicle && !bag.GetSlot(i).Item.IsCar) { bikeSlot = i; break; }
        Check(bikeSlot >= 0, "a bicycle on the hotbar");
        string bikeId = bag.GetSlot(bikeSlot).InstanceId;
        bag.SelectHotbar(bikeSlot);
        Check(WorldDroppedItem.TryPlaceVehicle(inv, out err), "a bicycle placed " + err);
        var bikeView = await ViewOf(v => v.IsPlacedVehicle && v.VehicleOwnershipKey == "vehicle:" + bikeId, "the bicycle standing");
        ulong bikeEntity = bikeView.NetId;
        await Awaitable.WaitForSecondsAsync(2.5f);
        Check(WorldStateSync.InventoryOverwrites == 0 && WorldStateSync.Overwrites == 0, "dropping and placing: 0 overwrites");
        await LeaveRemote(client);
        await StopProcessServer();
        var store = WorldStore.Open(worldsRoot, settings.world);
        var drop = store.Entities.Find(e => e.id == cashDrop);
        var stand = store.Entities.Find(e => e.id == bikeEntity);
        Check(drop != null && ItemsOf(drop.blobs[0]) == "cash:10000 x1" && (drop.flags & EntityFlags.Expiring) != 0, "the restarted server's world.dat has the dropped note");
        Check(stand != null && DropBlob.TryDecode(stand.blobs[0], out byte kind, out _, out _, out _) && kind == DropBlob.VehicleKind, "and the placed bicycle");
        var saved = new InventoryState();
        Check(Read(store, me, out var content) && InventorySnapshot.Import(saved, content.inventory) && CashService.CarriedTotal(saved) == cash - 10000
              && InventoryLedgerRules.FindUnique(saved, bikeId) == null, "the character's ledger has neither (" + CashService.CarriedTotal(saved) + "원)");

        Section("after a restart");
        await StartProcessServer("B");
        await Rejoin(client);
        bag = InventoryManager.Instance.State;
        player = PlayerSpawner.Player;
        inv = player.GetComponent<PlayerInventory>();
        cashView = await ViewOf(v => v.NetId == cashDrop, "the note after the restart");
        await ViewOf(v => v.NetId == bikeEntity, "the bicycle after the restart");
        Check(CashService.CarriedTotal(bag) == cash - 10000, "the bag as left");
        await StandBeside(player, cashView.transform.position);
        Check(cashView.TryPickUp(inv, out err) && CashService.CarriedTotal(bag) == cash, "the note picked up " + err);
        await WaitFor(() => !cashView, 5, "the note gone (the server's Despawn)");
        await Awaitable.WaitForSecondsAsync(2.5f);
        Check(WorldStateSync.InventoryOverwrites == 0, "0 overwrites");
        await LeaveRemote(client);
        await StopProcessServer();
        store = WorldStore.Open(worldsRoot, settings.world);
        saved = new InventoryState();
        Check(store.Entities.Find(e => e.id == cashDrop) == null && Read(store, me, out content) && InventorySnapshot.Import(saved, content.inventory) && CashService.CarriedTotal(saved) == cash,
            "world.dat: the note is no longer on the ground, it is in the character's ledger");
    }
}
