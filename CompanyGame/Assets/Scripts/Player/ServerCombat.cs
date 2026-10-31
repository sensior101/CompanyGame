using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// ServerWorld's combat service. Clients send a swing intent, never a target or damage amount. The server checks its
/// ledger, schedules one contact, selects a target, checks map collision and publishes authenticated avatar events.
/// Transform positions still come from the existing owner movement protocol; combat bounds their displacement during
/// a swing but does not replace movement simulation. Missing collision data always refuses damage.
/// </summary>
public sealed class ServerCombat
{
    public const byte AvatarComponent = 1;
    public const float Contact = .36f, ComboWindow = .65f;
    const float BodyRadius = .35f, AimDot = -.174f;
    const float HeavyWindow = 2f, DownDuration = 1.7f;
    readonly GameServer server;
    readonly ServerLedger ledger;
    readonly ServerCombatCollision collision = new ServerCombatCollision();
    readonly Func<uint, string, bool> previousSpawn;
    readonly Func<uint, ulong, byte, byte[], byte[]> previousState;
    readonly Func<uint, ulong, byte, byte, EventTarget, byte[], bool> previousEvent;
    readonly Dictionary<uint, Fighter> fighters = new Dictionary<uint, Fighter>();
    readonly Dictionary<string, float> health = new Dictionary<string, float>();
    readonly List<uint> gone = new List<uint>();
    readonly HashSet<string> warnedMaps = new HashSet<string>();
    readonly float punchDamage, punchRange, punchRate;
    readonly float contact, comboWindow, meleeDot, punchRadius;
    readonly Func<double> now;

    sealed class Fighter
    {
        public uint owner, sequence;
        public ulong entity;
        public string account, map;
        public bool seen, hasSequence;
        public int lastStep = -1, heavyHits;
        public double end = double.NegativeInfinity, stunnedUntil, lastHeavy = double.NegativeInfinity;
        public double nextHealth;
        public Swing swing;
    }

    sealed class Swing
    {
        public uint sequence;
        public int step, slot;
        public bool shot;
        public string itemId, map;
        public float damage, range, duration;
        public double started, contactAt;
        public Vector3 direction, position;
    }

    public int Accepted { get; private set; }
    public int Rejected { get; private set; }
    public int Hits { get; private set; }
    public int GeometryRefused { get; private set; }

    // An injected clock makes authoritative wind-up and replay tests deterministic without running Play Mode.
    public ServerCombat(GameServer server, ServerLedger ledger, Func<double> clock = null)
    {
        this.server = server;
        this.ledger = ledger;
        now = clock ?? (() => Time.realtimeSinceStartupAsDouble);
        var prefab = Resources.Load<GameObject>("Player");
        var settings = prefab ? prefab.GetComponent<PlayerCombat>() : null;
        punchDamage = settings ? Mathf.Clamp(settings.punchDamage, .1f, 100f) : 10f;
        punchRange = settings ? Mathf.Clamp(settings.punchRange, .1f, 3f) : .52f;
        punchRate = settings ? Mathf.Clamp(settings.punchesPerSecond, .1f, 8f) : 2f;
        contact = settings ? Mathf.Clamp(settings.contactNormalized, .15f, .7f) : Contact;
        comboWindow = settings ? Mathf.Clamp(settings.comboWindow, .05f, 2f) : ComboWindow;
        meleeDot = Mathf.Cos((settings ? Mathf.Clamp(settings.attackHalfAngle, 10f, 85f) : 65f) * Mathf.Deg2Rad);
        punchRadius = settings ? Mathf.Clamp(settings.punchRadius, .01f, .5f) : .1f;
        previousSpawn = server.SpawnFilter;
        previousState = server.StateFilter;
        previousEvent = server.EventFilter;
        server.SpawnFilter = RefuseSpawn;
        server.StateFilter = FilterState;
        server.EventFilter = FilterEvent;
        server.ServerEvent += OnIntent;
        ledger.CombatHealth = HealthOf;
        ledger.CanConsumeFood = account => HealthOf(account) > 0f;
        ledger.FoodConsumed += OnFood;
    }

    public void End()
    {
        server.ServerEvent -= OnIntent;
        server.SpawnFilter = previousSpawn;
        server.StateFilter = previousState;
        server.EventFilter = previousEvent;
        ledger.CombatHealth = null;
        ledger.CanConsumeFood = null;
        ledger.FoodConsumed -= OnFood;
        collision.Dispose();
        fighters.Clear();
    }

    public float HealthOf(string account)
    {
        if (health.TryGetValue(account, out float value)) return value;
        var character = server.World.Find(account);
        value = character != null && CharacterSync.Read(character.content, out var saved) && saved.hasCombatHealth
            ? Mathf.Clamp(saved.combatHealth, 0f, PlayerStats.Max) : PlayerStats.Max;
        health[account] = value;
        return value;
    }

    void OnFood(string account, ItemData food)
    {
        float before = HealthOf(account);
        // Local consumption already rejects a collapsed player. The server must enforce that rule too.
        if (food && before > 0f) health[account] = Mathf.Clamp(before + Mathf.Max(0f, food.healthRestore), 0f, PlayerStats.Max);
    }

    /// <summary>Reconciles predicted eating even when the inventory request failed.</summary>
    public void Reconcile(string account)
    {
        foreach (var fighter in fighters.Values) if (fighter.account == account) SendHealth(fighter);
    }

    public void Tick()
    {
        foreach (var f in fighters.Values) f.seen = false;
        foreach (ulong id in server.EntityIds)
        {
            if (!ReadAvatar(id, out uint owner, out string map, out _, out _, out _, out _)) continue;
            string account = server.AccountOf(owner);
            if (account == null || server.World.Find(account) == null || server.MapOf(owner) != map) continue;
            if (!fighters.TryGetValue(owner, out var f))
                fighters[owner] = f = new Fighter { owner = owner, account = account };
            if (f.seen) continue; // one combat body per connection, even for an old pre-filter spawn.
            f.seen = true;
            if (f.entity != id || f.map != map)
            {
                Cancel(f);
                f.entity = id; f.map = map; f.lastStep = -1; f.nextHealth = 0;
            }
            if (now() >= f.nextHealth) SendHealth(f);
        }
        gone.Clear();
        foreach (var pair in fighters)
        {
            var f = pair.Value;
            if (!f.seen)
            {
                Cancel(f); f.entity = 0;
                if (server.AccountOf(pair.Key) == null) gone.Add(pair.Key);
                continue; // Despawn/map travel must not erase rate, replay or stun history on a live connection.
            }
            if (f.swing != null && now() >= f.swing.contactAt) Resolve(f);
        }
        foreach (uint owner in gone) fighters.Remove(owner);
    }

    bool RefuseSpawn(uint connection, string prefab)
    {
        if (previousSpawn != null && previousSpawn(connection, prefab)) return true;
        if (prefab != AvatarSync.PrefabKey) return false;
        foreach (ulong id in server.EntityIds)
            if (server.TryGetEntity(id, out string key, out _, out uint owner, out _, out _) && key == prefab && owner == connection) return true;
        return false;
    }

    byte[] FilterState(uint connection, ulong id, byte component, byte[] blob)
    {
        if (previousState != null) blob = previousState(connection, id, component, blob);
        if (blob == null) return null;
        if (!server.TryGetEntity(id, out string key, out _, out _, out _, out _) || key != AvatarSync.PrefabKey) return blob;
        if (component == 0) return TryPosition(blob, out _, out _) ? blob : null;
        if (component == AvatarComponent) return TryLook(blob, out _, out _) ? blob : null;
        return null;
    }

    bool FilterEvent(uint sender, ulong id, byte component, byte eventId, EventTarget target, byte[] payload)
    {
        if (previousEvent != null && !previousEvent(sender, id, component, eventId, target, payload)) return false;
        if (!server.TryGetEntity(id, out string key, out string map, out uint owner, out _, out _)) return false;
        if (key != AvatarSync.PrefabKey) return true;
        // Legacy HitEvent and PunchEvent never relay: an attacker cannot forge damage or someone else's swing.
        return owner == sender && server.MapOf(sender) == map && component == AvatarComponent &&
            eventId == AvatarState.AttackIntentEvent && target == EventTarget.Server && payload != null && payload.Length == 19;
    }

    void OnIntent(uint sender, ulong id, byte component, byte eventId, byte[] payload)
    {
        if (component != AvatarComponent || eventId != AvatarState.AttackIntentEvent ||
            !ReadAvatar(id, out uint owner, out string map, out Vector3 position, out float yaw, out bool seated, out string vehicle) || owner != sender) return;
        Tick(); // makes the first intent usable immediately after Spawn.
        if (!fighters.TryGetValue(sender, out var f) || f.entity != id) return;
        uint sequence; int step, slot; bool shot; Vector3 direction;
        try
        {
            using var r = SyncProtocol.Reader(payload);
            sequence = r.ReadUInt32(); step = r.ReadByte(); shot = r.ReadBoolean(); slot = r.ReadByte();
            direction = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            SyncProtocol.End(r);
        }
        catch (Exception) { Rejected++; return; }
        if (sequence == 0 || (f.hasSequence && unchecked((int)(sequence - f.sequence)) <= 0)) { Rejected++; return; }
        f.sequence = sequence; f.hasSequence = true;
        double time = now();
        if (step > 2 || slot >= InventoryState.HotbarSize || !Finite(direction) || direction.sqrMagnitude < .5f ||
            direction.sqrMagnitude > 1.5f || seated || !string.IsNullOrEmpty(vehicle) || HealthOf(f.account) <= 0f ||
            f.swing != null || time + .015 < f.end || time < f.stunnedUntil || f.map != map)
        { Reject(f, sequence, position); return; }
        direction.Normalize();
        Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (!shot && horizontal.sqrMagnitude < .1f)
        { Reject(f, sequence, position); return; }
        int expected = f.lastStep >= 0 && time <= f.end + comboWindow ? (f.lastStep + 1) % 3 : 0;
        if ((!shot && step != 0 && step != expected) || (shot && step != 0)) { Reject(f, sequence, position); return; }
        var stack = ledger.Bag(f.account).GetSlot(slot);
        var item = stack != null && !stack.IsEmpty ? stack.Item : null;
        if (item && (item.IsVehicle || item.IsConsumable)) { Reject(f, sequence, position); return; }
        var weapon = item && item.IsWeapon ? item : null;
        bool firearm = weapon && weapon.weaponKind == WeaponKind.Firearm;
        if (shot != firearm) { Reject(f, sequence, position); return; }
        float rate = weapon ? Mathf.Clamp(weapon.attacksPerSecond, .1f, firearm ? 12f : 8f) : punchRate;
        float duration = shot ? 1f / rate : PlayerCombat.DurationForStep(step, rate);
        float damage = weapon ? Mathf.Clamp(weapon.weaponDamage, .1f, 100f) : punchDamage;
        if (!shot) damage = PlayerCombat.DamageForStep(step, damage);
        Vector3 origin = position + Vector3.up * 1.3f;
        if (!collision.TryCheck(map, origin, origin + direction * .02f, .1f, out bool originClear))
        { GeometryUnavailable(f, sequence, position); return; }
        if (!originClear) { Reject(f, sequence, position); return; }
        f.end = time + duration;
        f.lastStep = shot ? -1 : step;
        f.swing = new Swing { sequence = sequence, step = step, slot = slot, shot = shot, itemId = item ? item.itemId : "", map = map,
            direction = direction, position = position, duration = duration, damage = Mathf.Min(damage, 100f), started = time,
            range = weapon ? Mathf.Clamp(weapon.weaponRange, .1f, firearm ? 120f : 3f) : PlayerCombat.RangeForStep(step, punchRange),
            contactAt = time + (shot ? .02f : duration * contact) };
        Accepted++;
        server.SendServerEvent(id, AvatarComponent, AvatarState.SwingEvent, 0, ServerWorld.Bytes(w =>
        { w.Write(sequence); w.Write((byte)step); w.Write(shot); w.Write(duration); w.Write(shot ? .02f / duration : contact); }));
    }

    void Resolve(Fighter attacker)
    {
        var swing = attacker.swing;
        attacker.swing = null; // this accepted sequence can have only one contact, including exceptions/misses.
        if (!ReadAvatar(attacker.entity, out uint owner, out string map, out Vector3 position, out float yaw, out bool seated, out string vehicle) ||
            owner != attacker.owner || map != swing.map || server.MapOf(owner) != map || seated || !string.IsNullOrEmpty(vehicle) ||
            now() < attacker.stunnedUntil || HealthOf(attacker.account) <= 0f || Vector3.Distance(position, swing.position) > 1.8f)
        { Result(attacker, swing.sequence, false, swing.position, 0f); return; }
        Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        if (!swing.shot && Vector3.Dot(forward, Vector3.ProjectOnPlane(swing.direction, Vector3.up).normalized) < AimDot)
        { Result(attacker, swing.sequence, false, position, 0f); return; } // wind-up lets the body turn toward camera aim first.
        var stack = ledger.Bag(attacker.account).GetSlot(swing.slot);
        string itemId = stack != null && !stack.IsEmpty ? stack.Item.itemId : "";
        if (itemId != swing.itemId) { Result(attacker, swing.sequence, false, position, 0f); return; }
        Vector3 origin = position + Vector3.up * 1.3f;
        Fighter target = null; Vector3 point = origin + swing.direction * swing.range; float nearest = float.PositiveInfinity;
        foreach (var candidate in fighters.Values)
        {
            if (candidate == attacker || candidate.map != map || HealthOf(candidate.account) <= 0f ||
                !ReadAvatar(candidate.entity, out _, out _, out Vector3 targetPosition, out _, out _, out string targetVehicle)) continue;
            if (!string.IsNullOrEmpty(targetVehicle) && HasClaimedVehicle(candidate.owner, map, targetPosition, targetVehicle)) continue;
            if ((!swing.shot && Mathf.Abs(targetPosition.y - position.y) > 1.75f) ||
                !ContactPoint(origin, swing.direction, swing.range, swing.shot ? 0f : punchRadius, targetPosition, out Vector3 center, out float distance)) continue;
            Vector3 delta = targetPosition + Vector3.up * Mathf.Clamp(center.y - targetPosition.y, BodyRadius, 1.8f - BodyRadius) - origin;
            if (!swing.shot && delta.sqrMagnitude > .0001f && Vector3.Dot(delta.normalized, swing.direction) < meleeDot) continue;
            if (distance >= nearest) continue;
            if (!collision.TryCheck(map, origin, center, swing.shot ? .025f : .12f, out bool clear))
            { GeometryUnavailable(attacker, swing.sequence, origin); return; }
            if (!clear) continue;
            target = candidate; point = center; nearest = distance;
        }
        if (target == null) { Result(attacker, swing.sequence, false, point, 0f); return; }
        float before = HealthOf(target.account), damage = Mathf.Min(before, swing.damage);
        float after = Mathf.Max(0f, before - damage);
        health[target.account] = after;
        bool heavy = !swing.shot && swing.step == 2;
        if (heavy)
        {
            target.heavyHits = now() - target.lastHeavy <= HeavyWindow ? target.heavyHits + 1 : 1;
            target.lastHeavy = now();
        }
        bool down = after <= 0f || (heavy && target.heavyHits >= 2);
        if (down) target.heavyHits = 0;
        ReadAvatar(target.entity, out _, out _, out Vector3 victimPosition, out _, out _, out _);
        bool face = point.y - victimPosition.y >= 1.4f;
        float stun = down ? DownDuration : heavy ? .5f : face ? .26f : .32f;
        target.stunnedUntil = Math.Max(target.stunnedUntil, now() + stun + (heavy ? .07f : .04f));
        target.lastStep = -1;
        Cancel(target);
        ledger.Store(target.account);
        Hits++;
        Vector3 direction = Vector3.ProjectOnPlane(victimPosition - position, Vector3.up).normalized;
        if (direction.sqrMagnitude < .001f) direction = Vector3.ProjectOnPlane(swing.direction, Vector3.up).normalized;
        server.SendServerEvent(target.entity, AvatarComponent, AvatarState.ReactionEvent, 0, ServerWorld.Bytes(w =>
        {
            w.Write(attacker.owner); w.Write(swing.sequence); w.Write((byte)swing.step); w.Write(after); w.Write(damage);
            w.Write(direction.x); w.Write(direction.y); w.Write(direction.z);
            w.Write(point.x); w.Write(point.y); w.Write(point.z); w.Write(face);
            w.Write(heavy); w.Write(down); w.Write(stun);
        }));
        Result(attacker, swing.sequence, true, point, damage);
    }

    void Cancel(Fighter fighter)
    {
        if (fighter.swing == null) return;
        var swing = fighter.swing; fighter.swing = null;
        Result(fighter, swing.sequence, false, swing.position, 0f);
    }

    void Reject(Fighter fighter, uint sequence, Vector3 point)
    { Rejected++; Result(fighter, sequence, false, point, 0f); }

    void GeometryUnavailable(Fighter fighter, uint sequence, Vector3 point)
    {
        GeometryRefused++;
        Reject(fighter, sequence, point);
        if (warnedMaps.Add(fighter.map))
        {
            Debug.LogWarning("[ServerCombat] attacks refused: collision data unavailable for " + fighter.map);
            ServerWorld.Instance?.Notice(fighter.owner, "이 맵의 서버 충돌 정보를 준비하지 못해 공격을 처리할 수 없습니다.");
        }
    }

    void Result(Fighter fighter, uint sequence, bool hit, Vector3 point, float damage) =>
        server.SendServerEvent(fighter.entity, AvatarComponent, AvatarState.ResultEvent, 0, ServerWorld.Bytes(w =>
        { w.Write(sequence); w.Write(hit); w.Write(point.x); w.Write(point.y); w.Write(point.z); w.Write(damage); }));

    void SendHealth(Fighter fighter)
    {
        if (fighter.entity == 0) return;
        fighter.nextHealth = now() + 2f;
        server.SendServerEvent(fighter.entity, AvatarComponent, AvatarState.HealthEvent, 0,
            ServerWorld.Bytes(w => w.Write(HealthOf(fighter.account))));
    }

    bool ReadAvatar(ulong id, out uint owner, out string map, out Vector3 position, out float yaw, out bool seated, out string vehicle)
    {
        owner = 0; map = null; position = default; yaw = 0; seated = false; vehicle = "";
        return server.TryGetEntity(id, out string key, out map, out owner, out _, out var blobs) && key == AvatarSync.PrefabKey &&
            owner != 0 && blobs != null && blobs.Length >= 2 && TryPosition(blobs[0], out position, out yaw) && TryLook(blobs[1], out seated, out vehicle);
    }

    bool HasClaimedVehicle(uint connection, string map, Vector3 position, string itemId)
    {
        foreach (ulong id in server.EntityIds)
        {
            if (!server.TryGetEntity(id, out string key, out string vehicleMap, out uint owner, out _, out var blobs) ||
                key != DropBlob.PrefabKey || owner != connection || vehicleMap != map || blobs == null || blobs.Length == 0 ||
                !DropBlob.TryDecode(blobs[0], out byte kind, out Vector3 place, out _, out byte[] contents) ||
                kind != DropBlob.VehicleKind || (place - position).sqrMagnitude > 9f) continue;
            string json = DropBlob.UnpackJson(contents);
            var items = new InventoryState();
            if (json == null || !InventorySnapshot.Import(items, json)) continue;
            foreach (var stack in InventoryLedgerRules.Stacks(items))
                if (stack.Item.IsVehicle && stack.Item.itemId == itemId) return true;
        }
        return false;
    }

    static bool TryPosition(byte[] blob, out Vector3 position, out float yaw)
    {
        position = default; yaw = 0;
        if (blob == null || blob.Length != 14) return false;
        using var r = SyncProtocol.Reader(blob);
        position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); yaw = NetTransform.DecodeYaw(r.ReadUInt16());
        return Finite(position) && position.sqrMagnitude <= 10000000000f;
    }

    static bool TryLook(byte[] blob, out bool seated, out string vehicle)
    {
        seated = false; vehicle = "";
        if (blob == null || blob.Length < 4 || blob.Length > 1024) return false;
        try
        {
            using var r = SyncProtocol.Reader(blob);
            byte bits = r.ReadByte(); r.ReadByte(); seated = (bits & 2) != 0;
            vehicle = r.ReadString(); string held = r.ReadString(); SyncProtocol.End(r);
            return bits <= 3 && vehicle.Length <= 128 && held.Length <= 128;
        }
        catch (Exception) { return false; }
    }

    static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) &&
        !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);

    // Closest points between the finite attack segment and the victim's vertical capsule axis. This keeps a wide
    // cone from turning into auto-aim: a victim must intersect the actual ray/sphere sweep as well as its range/cone.
    static bool ContactPoint(Vector3 origin, Vector3 direction, float range, float sweepRadius, Vector3 feet,
        out Vector3 point, out float distance)
    {
        Vector3 d1 = direction * range, p2 = feet + Vector3.up * BodyRadius;
        Vector3 d2 = Vector3.up * (1.8f - 2f * BodyRadius), r = origin - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), b = Vector3.Dot(d1, d2);
        float c = Vector3.Dot(d1, r), f = Vector3.Dot(d2, r), denominator = a * e - b * b;
        float s = denominator > .000001f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
        float t = (b * s + f) / e;
        if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
        else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
        Vector3 onRay = origin + d1 * s, onAxis = p2 + d2 * t;
        float radialSquared = (onRay - onAxis).sqrMagnitude, radius = BodyRadius + sweepRadius;
        Vector3 outward = onRay - onAxis;
        if (outward.sqrMagnitude < .000001f) outward = -direction;
        point = onAxis + outward.normalized * BodyRadius;
        distance = Vector3.Distance(origin, point);
        return Vector3.Dot(onAxis - origin, direction) > 0f && radialSquared <= radius * radius;
    }
}
