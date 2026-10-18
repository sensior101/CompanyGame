using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Items between the inventory and the world on a sync-layer server (Docs/Multiplayer_Redesign.md 12.10.5). Dropped items
/// and placed vehicles (Net/WorldDrop) are made and removed by the server only, from the ledger (ServerLedger): their
/// contents are always the ledger's data, never a client's blob. Made and ended by ServerWorld.
///   p.drop           a slot (bag or equipment), item id (a unique item by instance id), count, place: those leave the ledger
///                    (InventoryLedgerRules.TryTake) and lie on the sender's map as an Expiring entity
///   p.vehicle.place  a vehicle instance id and place: it leaves the ledger and stands there (not on an interior map)
///   p.pickup         an entity id: a dropped item, server-owned, on the sender's map, whose whole contents fit the ledger
///                    goes in, and the entity goes (one wins a race; the other is told 다른 사람이 먼저 주웠습니다)
///   p.dismantle      an entity id: a placed vehicle, server-owned, on the sender's map, the registry allowing it
///                    (CanDismantleVehicle): it becomes a dropped item of the same contents. The ledger does not change
///   hooks            a client's Spawn of Net/WorldDrop is refused (answered Despawn); a Claim of a dropped item is refused
///                    (picking up is p.pickup) and of a vehicle only when the registry lets that account ride it
///                    (CanRideVehicle); a rider's State keeps the server's kind and contents, only its place is taken
/// The place is trusted (only the map is checked: no distance or floor check). Entities from 4b (made by clients) read
/// the same and follow these rules. Nothing here touches a screen manager.
/// </summary>
public sealed class ServerDrops
{
    public static readonly HashSet<string> Kinds = new HashSet<string> { "p.drop", "p.vehicle.place", "p.pickup", "p.dismantle" };
    const EntityFlags Placed = EntityFlags.PersistOnLeave | EntityFlags.Claimable, Dropped = Placed | EntityFlags.Expiring;
    const string Key = DropBlob.PrefabKey, Gone = "다른 사람이 먼저 주웠습니다.";

    readonly GameServer server;
    readonly ServerLedger ledger;
    readonly ServerBooks bodies;

    /// <summary>Spawns, Claims and States of clients refused or trimmed by the hooks, for QA.</summary>
    public int SpawnsRefused { get; private set; }
    public int ClaimsRefused { get; private set; }
    public int StatesTrimmed { get; private set; }

    public ServerDrops(GameServer server, ServerLedger ledger, ServerBooks bodies)
    {
        this.server = server;
        this.ledger = ledger;
        this.bodies = bodies;
        server.SpawnFilter = RefuseSpawn;
        server.ClaimFilter = AllowClaim;
        server.StateFilter = KeepContents;
    }

    public void End()
    {
        server.SpawnFilter = null;
        server.ClaimFilter = null;
        server.StateFilter = null;
    }

    // ---- hooks ----

    bool RefuseSpawn(uint connection, string prefab)
    {
        if (prefab != Key) return false;
        SpawnsRefused++;
        return true;
    }

    bool AllowClaim(uint connection, ulong id)
    {
        if (!Read(id, out _, out _, out byte kind, out _, out _, out var items)) return true; // not a drop: not this game's business
        bool ok = kind == DropBlob.VehicleKind && items != null && Vehicle(items) is ItemStack car
                  && AsServer(r => r.CanRideVehicle(car.OwnershipKey, server.AccountOf(connection)));
        if (!ok) ClaimsRefused++;
        return ok;
    }

    byte[] KeepContents(uint connection, ulong id, byte component, byte[] blob)
    {
        if (!server.TryGetEntity(id, out string prefab, out _, out _, out _, out var blobs) || prefab != Key) return blob;
        StatesTrimmed++;
        return component == 0 && blobs.Length > 0 ? DropBlob.WithPlaceOf(blobs[0], blob) : null;
    }

    // ---- requests ----

    /// <summary>Decides a p.drop / p.vehicle.place / p.pickup / p.dismantle of <paramref name="account"/>. The error (Korean), or null.</summary>
    public string Apply(uint connection, string account, string kind, string json)
    {
        var a = JsonUtility.FromJson<SharedOpArgs>(json) ?? new SharedOpArgs();
        string map = server.MapOf(connection);
        if (string.IsNullOrEmpty(map) || map == SyncProtocol.AllMaps) return "지금은 할 수 없습니다.";
        switch (kind)
        {
            case "p.drop": return Drop(account, map, a);
            case "p.vehicle.place": return Place(account, map, a);
            case "p.pickup": return PickUp(account, map, a.n);
            case "p.dismantle": return Dismantle(account, map, a.n);
            default: return "처리할 수 없는 요청입니다.";
        }
    }

    string Drop(string account, string map, SharedOpArgs a)
    {
        if (!DropBlob.TryParsePlace(a.d, out var position, out float yaw)) return "놓을 위치를 확인해 주세요.";
        if (!int.TryParse(a.c, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int slot)) slot = -1;
        int count = a.n < 1 || a.n > int.MaxValue ? 0 : (int)a.n;
        var bag = ledger.Bag(account);
        if (!InventoryLedgerRules.TryTake(bag, slot, a.f, a.a, a.b, count, out var taken, out string error)) return error;
        return Spawn(account, bag, map, DropBlob.ItemKind, position, yaw, taken);
    }

    string Place(string account, string map, SharedOpArgs a)
    {
        if (map.Contains("/Interiors/")) return "탈것은 실외에서 설치해 주세요.";
        if (!DropBlob.TryParsePlace(a.d, out var position, out float yaw)) return "설치할 위치를 확인해 주세요.";
        var bag = ledger.Bag(account);
        var stack = InventoryLedgerRules.FindUnique(bag, a.a);
        if (stack == null || !stack.Item.IsVehicle) return "가지고 있지 않은 탈것입니다.";
        if (!InventoryLedgerRules.TryTake(bag, -1, false, stack.Item.itemId, a.a, 1, out var taken, out string error)) return error;
        return Spawn(account, bag, map, DropBlob.VehicleKind, position, yaw, taken);
    }

    // The ledger already gave the contents: they lie on the map now, or go back to the bag.
    string Spawn(string account, InventoryState bag, string map, byte kind, Vector3 position, float yaw, InventoryState taken)
    {
        byte[] blob = null;
        try { blob = DropBlob.Encode(kind, position, yaw, DropBlob.Pack(taken)); }
        catch (Exception e) { Debug.LogWarning("[ServerDrops] contents too big: " + e.Message); }
        ulong id = blob == null ? 0 : server.SpawnWorldEntity(Key, map, kind == DropBlob.ItemKind ? Dropped : Placed, new[] { blob });
        if (id == 0)
        {
            if (!InventoryLedgerRules.TryPutAll(bag, taken, out string error)) Debug.LogError("[ServerDrops] could not give back to " + account + ": " + error);
            return kind == DropBlob.ItemKind ? "지금은 내려놓을 수 없습니다." : "지금은 탈것을 설치할 수 없습니다.";
        }
        ledger.Store(account);
        return null;
    }

    string PickUp(string account, string map, long entity)
    {
        ulong id = (ulong)entity;
        if (!Read(id, out string at, out uint owner, out byte kind, out _, out _, out var items) || owner != 0) return Gone;
        if (at != map) return "지금은 주울 수 없습니다.";
        if (kind != DropBlob.ItemKind) return "설치된 탈것은 해체한 다음 주워 주세요.";
        if (items == null || Empty(items)) return "주울 수 없는 아이템입니다.";
        var bag = ledger.Bag(account);
        if (!InventoryLedgerRules.TryPutAll(bag, items, out string error)) return error;
        if (!server.RemoveWorldEntity(id)) Debug.LogError("[ServerDrops] picked " + id + " up but could not remove it"); // one thread: never
        ledger.Store(account);
        return null;
    }

    string Dismantle(string account, string map, long entity)
    {
        ulong id = (ulong)entity;
        if (!Read(id, out string at, out uint owner, out byte kind, out var position, out float yaw, out var items) || owner != 0 || at != map
            || kind != DropBlob.VehicleKind) return "지금은 해체할 수 없습니다.";
        var car = items != null ? Vehicle(items) : null;
        if (car == null || !AsServer(r => r.CanDismantleVehicle(car.OwnershipKey, account))) return "소유자가 해체를 허용하지 않았습니다.";
        server.TryGetEntity(id, out _, out _, out _, out _, out var blobs);
        DropBlob.TryDecode(blobs[0], out _, out _, out _, out byte[] contents);
        ulong made = server.SpawnWorldEntity(Key, at, Dropped, new[] { DropBlob.Encode(DropBlob.ItemKind, position, yaw, contents) });
        if (made == 0) return "지금은 해체할 수 없습니다.";
        server.RemoveWorldEntity(id);
        return null;
    }

    // ---- reading an entity ----

    // A Net/WorldDrop entity: its map, owner, kind, place and contents (a 4b drop's book pages go to the world's books).
    bool Read(ulong id, out string map, out uint owner, out byte kind, out Vector3 position, out float yaw, out InventoryState items)
    {
        kind = 0; position = default; yaw = 0; items = null;
        if (!server.TryGetEntity(id, out string prefab, out map, out owner, out _, out var blobs) || prefab != Key) return false;
        if (blobs.Length == 0 || !DropBlob.TryDecode(blobs[0], out kind, out position, out yaw, out byte[] contents)) return true;
        string json = DropBlob.UnpackJson(contents);
        var read = new InventoryState();
        if (json != null && InventorySnapshot.Import(read, bodies != null ? bodies.Strip(json) : json)) items = read;
        return true;
    }

    static bool Empty(InventoryState items)
    {
        foreach (var _ in InventoryLedgerRules.Stacks(items)) return false;
        return true;
    }

    static ItemStack Vehicle(InventoryState items)
    {
        foreach (var stack in InventoryLedgerRules.Stacks(items)) if (stack.Item.IsVehicle) return stack;
        return null;
    }

    // The registry with account ids as they are (no name → id translation of this process's own player).
    static bool AsServer(Func<PropertyRegistry, bool> check)
    {
        var registry = PropertyRegistry.Instance;
        if (!registry) return false;
        bool was = SharedWriteGate.Applying;
        SharedWriteGate.Applying = true;
        try { return check(registry); }
        finally { SharedWriteGate.Applying = was; }
    }
}
