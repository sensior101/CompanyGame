using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using QaPeer = SyncLayerQA.QaPeer;

/// <summary>Deterministic real-protocol combat checks. Uses throwaway peers, ledgers and an unsaved temporary wall.</summary>
public static class ServerCombatQA
{
    sealed class FoodClock : IGameClock
    {
        public GameTime Now { get; private set; }
        public event Action<GameTime> HourChanged { add { } remove { } }
        public event Action<GameTime> DayChanged { add { } remove { } }
        public void SetNow(GameTime value) => Now = value;
    }

    public static string RunFoodPoisoning()
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FoodPoisonQA: " + label); checks++; }
        var previousClock = GameClock.Current;
        var foodClock = new FoodClock(); foodClock.SetNow(new GameTime(0, 10)); GameClock.Current = foodClock;
        var loop = new LoopbackTransport();
        var server = new GameServer(loop, 2);
        var books = new ServerBooks(server, 0, null);
        var ledger = new ServerLedger(server, 0, books);
        double realtime = 1000;
        var combat = new ServerCombat(server, ledger, () => realtime);
        GameObject host = null;
        try
        {
            string map = SceneManager.GetActiveScene().path;
            var peer = new QaPeer(loop.Connect());
            peer.Send(SyncProtocol.Hello("poison-qa", "음식검증", map)); server.Tick();
            peer.number = peer.Take(m => m.kind == ToClient.Welcome).number;
            peer.Send(SyncProtocol.CharacterCreate("음식검증", "male")); server.Tick(); peer.Pump();
            ulong id = SyncProtocol.MakeId(peer.number, 1);
            peer.Send(SyncProtocol.Spawn(id, AvatarSync.PrefabKey, map, EntityFlags.FollowsOwnerMap,
                new[] { SyncLayerQA.Pos(new Vector3(10000, 1000, 10000), 0), Bytes(w => { w.Write((byte)4); w.Write((byte)0); w.Write(""); w.Write("food:gimbap"); }) }));
            void Step(int minute)
            {
                server.Tick(); server.World.WorldMinutes = minute; foodClock.SetNow(GameTime.FromTotalMinutes(minute));
                realtime = 1000 + (minute - 600) * 5; combat.Tick(); peer.Pump();
            }
            Step(600);
            var food = InventorySnapshot.Find("food:gimbap");
            var bag = ledger.Bag("poison-qa");
            for (int i = 0; i < bag.Capacity; i++) if (!bag.GetSlot(i).IsEmpty) bag.TryRemove(i, bag.GetSlot(i).Count, out _);
            bag.TryAdd(food, 4, out _, expiresAt: 2000);
            bag.TryAdd(food, 3, out _, expiresAt: 590);
            string Eat(long expiry) => ledger.Apply(peer.number, "poison-qa", "p.eat",
                JsonUtility.ToJson(new SharedOpArgs { a = food.itemId, b = "expiry", n = expiry }));
            var forged = new InventoryState();
            forged.TryAdd(food, 4, out _, expiresAt: 2000); forged.TryAdd(food, 3, out _, expiresAt: 2001);
            Check(!InventoryLedgerRules.SameContents(bag, forged), "layout cannot turn spoiled food fresh");
            string before = InventorySnapshot.Export(bag);
            Check(Eat(591) != null && before == InventorySnapshot.Export(bag) && combat.FoodPoisonUntil("poison-qa") == 0, "forged expiry refuses consumption and illness");
            Check(Eat(590) == null, "spoiled food is edible even when full");
            Check(bag.GetSlot(0).Count == 4 && bag.GetSlot(1).Count == 2, "exact expired stack consumed, fresh stack preserved");
            Check(combat.FullnessOf("poison-qa") == 0 && combat.FoodPoisonUntil("poison-qa") == 660, "server applies zero fullness and 60 game minutes");
            float hp = combat.HealthOf("poison-qa");
            Step(630);
            Check(Mathf.Abs(combat.HealthOf("poison-qa") - (hp - PlayerStats.Point)) < .01f, "existing starvation loses one HUD health per 30 minutes");
            Check(Eat(2000) == null && combat.FullnessOf("poison-qa") == 0, "fresh food is consumed but cannot refill fullness");
            Step(659);
            Check(combat.FoodPoisonUntil("poison-qa") == 660 && Eat(2000) == null && combat.FullnessOf("poison-qa") == 0, "59 minute boundary still poisoned");
            Step(660);
            Check(combat.FoodPoisonUntil("poison-qa") == 0 && Mathf.Abs(combat.HealthOf("poison-qa") - (hp - 2 * PlayerStats.Point)) < .01f, "60 minute expiry and two HUD health lost");
            Check(Eat(2000) == null && combat.FullnessOf("poison-qa") == food.fullnessRestore * PlayerStats.Point, "normal eating resumes on expiry");
            Check(Eat(590) == null && combat.FoodPoisonUntil("poison-qa") == 720, "new spoiled meal re-applies illness");
            Step(670); Check(Eat(590) == null && combat.FoodPoisonUntil("poison-qa") == 730, "repeat spoiled meal refreshes timer");
            combat.Reconcile("poison-qa"); peer.Pump();
            Check(peer.inbox.Exists(m => m.kind == ToClient.Event && m.eventId == AvatarState.HealthEvent && m.blob.Length == 16), "health payload includes illness timer");

            var coffee = InventorySnapshot.Find("item_coffee");
            bag.TryAdd(coffee, 1, out _, expiresAt: 590);
            Check(ledger.Apply(peer.number, "poison-qa", "p.eat", JsonUtility.ToJson(new SharedOpArgs { a = coffee.itemId, b = "expiry", n = 590 })) == null,
                "spoiled zero-effect food is edible and causes illness");
            host = new GameObject("FoodPoisonLocalQA"); var stats = host.AddComponent<PlayerStats>();
            var local = new InventoryState(); local.TryAdd(food, 2, out _, expiresAt: 590); local.TryAdd(food, 2, out _, expiresAt: 2000);
            Check(stats.TryConsume(local, 0, out _) && stats.IsFoodPoisoned && stats.fullness == 0, "local spoiled consumption predicts poison");
            Check(stats.TryConsume(local, 1, out _) && stats.fullness == 0, "local fresh food cannot refill poisoned player");
            stats.ApplyAuthoritativeFullness(100); Check(stats.fullness == 0, "stale fullness cannot override poison");
            stats.ApplyAuthoritativeFoodPoisoning(0); stats.ApplyAuthoritativeFullness(100);
            Check(!stats.IsFoodPoisoned && stats.fullness == 100, "rejected prediction is reconciled");
            stats.ApplyAuthoritativeFoodPoisoning(730); foodClock.SetNow(GameTime.FromTotalMinutes(730));
            Check(!stats.IsFoodPoisoned && stats.TryConsume(local, 1, out _) && stats.fullness > 0, "local timer ends at exact minute");
            var cafe = new InventoryState(); cafe.TryAdd(coffee, 1, out _, expiresAt: 590);
            Check(cafe.GetSlot(0).CanConsume && stats.TryConsume(cafe, 0, out _) && stats.IsFoodPoisoned, "local zero-effect spoiled drink is edible");
            return "FoodPoisonQA passed " + checks + " checks";
        }
        finally
        {
            if (host) UnityEngine.Object.DestroyImmediate(host);
            combat.End(); ledger.End(); server.Stop(); GameClock.Current = previousClock;
        }
    }

    [MenuItem("CompanyGame/QA/Server Combat")]
    public static void Run()
    {
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new Exception("[ServerCombatQA] FAIL " + label);
            checks++; Debug.Log("[ServerCombatQA] OK " + label);
        }
        string map = SceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(map))
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (!string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)) { map = SceneManager.GetSceneAt(i).path; break; }
        if (string.IsNullOrEmpty(map)) throw new Exception("ServerCombatQA needs one loaded saved map (no scene is saved by the test).");
        var loop = new LoopbackTransport();
        var server = new GameServer(loop, 4);
        var books = new ServerBooks(server, 0, null);
        var ledger = new ServerLedger(server, 0, books);
        double clock = 1000;
        var combat = new ServerCombat(server, ledger, () => clock);
        GameObject wall = null;
        try
        {
            QaPeer Join(string account, string name)
            {
                var peer = new QaPeer(loop.Connect());
                peer.Send(SyncProtocol.Hello(account, name, map)); server.Tick();
                peer.number = peer.Take(m => m.kind == ToClient.Welcome).number;
                peer.Send(SyncProtocol.CharacterCreate(name, "male")); server.Tick(); peer.Pump(); peer.inbox.Clear();
                var bag = ledger.Bag(account);
                var slot = bag.GetSlot(InventoryState.HotbarSize - 1);
                if (slot != null && !slot.IsEmpty) bag.TryRemove(InventoryState.HotbarSize - 1, slot.Count, out _);
                return peer;
            }
            var a = Join("combat-qa-a", "격투검증가");
            var b = Join("combat-qa-b", "격투검증나");
            ulong aid = SyncProtocol.MakeId(a.number, 1), bid = SyncProtocol.MakeId(b.number, 1);
            Vector3 origin = new Vector3(10000, 1000, 10000);
            void Step() { server.Tick(); combat.Tick(); a.Pump(); b.Pump(); }
            void Place(QaPeer peer, ulong id, Vector3 where, float yaw = 0)
            { peer.Send(SyncProtocol.State(id, 0, SyncLayerQA.Pos(where, yaw))); Step(); }
            void Attack(QaPeer peer, ulong id, uint sequence, byte step = 0, bool shot = false, Vector3? direction = null)
            {
                Vector3 d = direction ?? Vector3.forward;
                peer.Send(SyncProtocol.Event(id, 1, AvatarState.AttackIntentEvent, EventTarget.Server,
                    Bytes(w => { w.Write(sequence); w.Write(step); w.Write(shot); w.Write((byte)(InventoryState.HotbarSize - 1)); w.Write(d.x); w.Write(d.y); w.Write(d.z); })));
                Step();
            }
            byte[] look = Bytes(w => { w.Write((byte)0); w.Write((byte)0); w.Write(""); w.Write(""); });
            a.Send(SyncProtocol.Spawn(aid, AvatarSync.PrefabKey, map, EntityFlags.FollowsOwnerMap,
                new[] { SyncLayerQA.Pos(origin, 0), look }));
            b.Send(SyncProtocol.Spawn(bid, AvatarSync.PrefabKey, map, EntityFlags.FollowsOwnerMap,
                new[] { SyncLayerQA.Pos(origin + Vector3.forward * .8f, 180), look }));
            Step();
            Check(combat.HealthOf("combat-qa-b") == 100f, "new server character starts with full health");
            a.inbox.Clear(); b.inbox.Clear();
            a.Send(SyncProtocol.Event(bid, 1, AvatarState.HitEvent, EventTarget.Owner, Bytes(w => w.Write(9999f))));
            a.Send(SyncProtocol.Event(bid, 1, AvatarState.PunchEvent, EventTarget.Map, Array.Empty<byte>()));
            Step();
            Check(!b.Has(m => m.kind == ToClient.Event && (m.eventId == AvatarState.HitEvent || m.eventId == AvatarState.PunchEvent)), "forged legacy damage and punch never relay");
            Attack(b, aid, 50);
            Check(combat.Accepted == 0, "another owner's avatar cannot be used to attack");
            Attack(a, aid, 1);
            Check(combat.Accepted == 1 && combat.Hits == 0, $"intent starts a swing without immediate damage (accepted={combat.Accepted}, rejected={combat.Rejected}, geometry={combat.GeometryRefused}, hits={combat.Hits})");
            var start = a.Take(m => m.kind == ToClient.Event && m.id == aid && m.eventId == AvatarState.SwingEvent);
            Check(start != null && start.sender == 0, "only sender zero confirms an accepted swing");
            float duration, contact;
            using (var r = SyncProtocol.Reader(start.blob)) { r.ReadUInt32(); r.ReadByte(); r.ReadBoolean(); duration = r.ReadSingle(); contact = r.ReadSingle(); }
            clock += duration * contact - .01; Step();
            Check(combat.Hits == 0, "no damage before authoritative contact time");
            clock += .02; Step();
            Check(combat.Hits == 1 && combat.HealthOf("combat-qa-b") < 100f, "contact applies exactly one server-computed hit");
            var hit = a.Take(m => m.kind == ToClient.Event && m.id == aid && m.eventId == AvatarState.ResultEvent);
            Vector3 hitPoint;
            using (var r = SyncProtocol.Reader(hit.blob))
            { r.ReadUInt32(); r.ReadBoolean(); hitPoint = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
            Check(hitPoint.z >= origin.z + .8f - .35f - .01f && hitPoint.z <= origin.z + .8f,
                "finite reach reports a contact on the victim capsule surface for occlusion and reaction");
            float hitHealth = combat.HealthOf("combat-qa-b");
            Attack(a, aid, 1);
            Check(combat.Hits == 1 && combat.HealthOf("combat-qa-b") == hitHealth, "replayed sequence cannot duplicate damage");
            Attack(a, aid, 2);
            Check(combat.Accepted == 1, "another attack during recovery is rejected");
            a.Send(SyncProtocol.Spawn(SyncProtocol.MakeId(a.number, 2), AvatarSync.PrefabKey, map, EntityFlags.FollowsOwnerMap,
                new[] { SyncLayerQA.Pos(origin, 0), look })); Step();
            Check(!server.TryGetEntity(SyncProtocol.MakeId(a.number, 2), out _, out _, out _, out _, out _), "a second avatar cannot bypass cooldown");
            Check(CharacterSync.Read(server.World.Find("combat-qa-b").content, out var saved) && saved.hasCombatHealth && saved.combatHealth == hitHealth,
                "server damage persists through the existing character content");
            saved.combatHealth = 100; saved.hasCombatHealth = true;
            b.Send(SyncProtocol.CharacterSave(CharacterSync.Encode(saved))); Step();
            Check(CharacterSync.Read(server.World.Find("combat-qa-b").content, out saved) && saved.combatHealth == hitHealth,
                "client save cannot overwrite server health");

            clock = 1000 + duration + .1;
            Attack(a, aid, 3, 2);
            Check(combat.Accepted == 1, "hook cannot skip the straight in a combo");
            Attack(a, aid, 4, 1);
            Check(combat.Accepted == 2, "the expected straight is accepted");
            clock += 1; Step();
            Check(combat.Hits == 2 && combat.HealthOf("combat-qa-b") < hitHealth, "straight resolves after its own contact time");

            clock += 1;
            Place(b, bid, origin + Vector3.forward * 10, 180);
            Attack(a, aid, 5); clock += 1; Step();
            Check(combat.Hits == 2, "distant target is outside melee reach");
            clock += 1;
            Place(b, bid, origin + Vector3.right * .6f + Vector3.forward * .45f, 180);
            Attack(a, aid, 6); clock += 1; Step();
            Check(combat.Hits == 2, "wide cone cannot hit a target outside the actual sphere sweep");
            clock += 1;
            Place(b, bid, origin - Vector3.forward, 180);
            Attack(a, aid, 7); clock += 1; Step();
            Check(combat.Hits == 2, "target behind the attacker is excluded");

            clock += 1;
            Place(b, bid, origin + Vector3.forward * .85f, 180);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "ServerCombatQA temporary wall";
            SceneManager.MoveGameObjectToScene(wall, SceneManager.GetSceneByPath(map));
            wall.transform.position = origin + new Vector3(0, 1.2f, .35f); wall.transform.localScale = new Vector3(3, 3, .15f);
            Physics.SyncTransforms();
            Attack(a, aid, 8); clock += 1; Step();
            Check(combat.Hits == 2, "solid wall blocks a reachable target");
            UnityEngine.Object.DestroyImmediate(wall); wall = null;

            clock += 1;
            Place(b, bid, origin + Vector3.forward * .8f, 180);
            Attack(a, aid, 9);
            clock += .04;
            Attack(b, bid, 1, 0, false, Vector3.back);
            int acceptedBeforeStun = combat.Accepted;
            clock += duration * contact; Step();
            Attack(b, bid, 2, 0, false, Vector3.back);
            Check(combat.Accepted == acceptedBeforeStun, "confirmed stun rejects a victim's new intent");
            clock += 1; Step();
            Check(combat.HealthOf("combat-qa-a") == 100, "hit interrupts the victim's pending contact");
            clock += 1;
            Attack(a, aid, 10, 0, true);
            Check(combat.Accepted == acceptedBeforeStun, "firearm request needs a firearm in the server ledger");
            Attack(a, aid, 11, 0, false, new Vector3(float.NaN, 0, 1));
            Check(combat.Accepted == acceptedBeforeStun, "non-finite aim is rejected");

            ItemData food = null;
            foreach (var item in TradeCatalog.Items())
                if (item && item.IsConsumable && item.healthRestore > 0) { food = item; break; }
            Check(food, "an existing food definition is available");
            var bagB = ledger.Bag("combat-qa-b"); bagB.TryAdd(food, 1, out _);
            float beforeFood = combat.HealthOf("combat-qa-b");
            string error = ledger.Apply(b.number, "combat-qa-b", "p.eat", JsonUtility.ToJson(new SharedOpArgs { a = food.itemId }));
            combat.Reconcile("combat-qa-b"); Step();
            Check(error == null && combat.HealthOf("combat-qa-b") == Mathf.Min(100, beforeFood + food.healthRestore), "only successful ledger eating heals server health");
            float afterFood = combat.HealthOf("combat-qa-b");
            error = ledger.Apply(b.number, "combat-qa-b", "p.eat", JsonUtility.ToJson(new SharedOpArgs { a = "missing_food" }));
            Check(error != null && combat.HealthOf("combat-qa-b") == afterFood, "failed eating cannot heal");

            b.Send(SyncProtocol.State(bid, 1, Bytes(w => { w.Write((byte)0); w.Write((byte)0); w.Write("forged_vehicle"); w.Write(""); })));
            Step(); int beforeForgedVehicle = combat.Hits;
            Attack(a, aid, 12); clock += 1; Step();
            Check(combat.Hits == beforeForgedVehicle + 1, "a forged vehicle pose without an owned world vehicle cannot grant immunity");

            a.Send(SyncProtocol.Map("QA/MissingCombatGeometry.unity")); Step();
            int beforeMissing = combat.GeometryRefused;
            Attack(a, aid, 13); Step();
            Check(combat.GeometryRefused == beforeMissing + 1, "missing map collision data explicitly fails closed");
            Debug.Log("[ServerCombatQA] RESULT passed, checks=" + checks);
        }
        finally
        {
            if (wall) UnityEngine.Object.DestroyImmediate(wall);
            combat.End(); ledger.End(); server.Stop();
        }
    }

    static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer); writer.Flush(); return stream.ToArray();
    }
}
