using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using QaPeer = SyncLayerQA.QaPeer;

/// <summary>Deterministic real-protocol combat checks. Uses throwaway peers, ledgers and an unsaved temporary wall.</summary>
public static class ServerCombatQA
{
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
