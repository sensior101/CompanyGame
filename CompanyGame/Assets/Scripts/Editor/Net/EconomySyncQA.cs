using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using QaPeer = SyncLayerQA.QaPeer;
using QaMsg = SyncLayerQA.QaMsg;

/// <summary>
/// QA of the shared economy on the server (Docs/Multiplayer_Redesign.md 12.9).
///   -executeMethod EconomySyncQA.Run         in memory (with -quit): the server module only — map "*" entities, server
///                                            states and events, blob limits, Spawn then States under 64KB, the world state
///                                            in world.dat version 3 (version 2 still reads), the night skip and -nightskip
///   -executeMethod EconomySyncQA.RunPlay     play mode (WITHOUT -quit): in-app server + the Unity owner + a fake TCP player
///   -executeMethod EconomySyncQA.RunProcess  play mode (WITHOUT -quit): a built server process + the Unity client + a fake
/// Env QA_SHOTS = folder for E_*.png (needs graphics). Each run prints the passed checks per item.
/// </summary>
[InitializeOnLoad]
public static partial class EconomySyncQA
{
    const string WorldKey = ServerWorld.PrefabKey;
    const string AllMaps = SyncProtocol.AllMaps;
    const double Day = GameServer.DayMinutes;

    static int checks;
    static string tag = "EconomySyncQA"; // LedgerQA (12.10.1) runs on the same helpers under its own name
    static string section = "";
    static readonly List<KeyValuePair<string, int>> sections = new List<KeyValuePair<string, int>>();
    static readonly List<string> notes = new List<string>();

    static EconomySyncQA() => EditorApplication.playModeStateChanged += OnPlayState;

    // ================= in memory =================

    static LoopbackTransport loopback;
    static GameServer server;
    static string root;

    public static void Run()
    {
        Begin();
        root = Path.Combine(Path.GetTempPath(), "EconomySyncQA-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
        var b = Join("acc-b", "QA나", "m2");
        Clear(a, b);

        Section("map * entity");
        ulong world = server.SpawnServerEntity(WorldKey, AllMaps, EntityFlags.None, new byte[3][]);
        Step(a, b);
        var seenA = a.Take(ToClient.Spawn, world);
        var seenB = b.Take(ToClient.Spawn, world);
        Check(world != 0 && SyncProtocol.ConnectionOf(world) == 0, "the server's entity id has connection 0 (" + world + ")");
        Check(seenA != null && seenB != null && seenA.owner == 0 && seenA.map == AllMaps && seenA.key == WorldKey, "connections on m1 and m2 both get it, owned by the server, map *");
        a.Send(SyncProtocol.Map("m3"));
        Step(a, b);
        Check(!a.Has(m => m.id == world), "moving to another map despawns nothing of it and spawns nothing again");
        var c = Join("acc-c", "QA다", "m9");
        Check(c.Take(ToClient.Spawn, world) != null, "a late joiner on yet another map gets it");
        Clear(a, b, c);

        Section("server states and events");
        Check(server.SetServerState(world, 1, Blob(7)), "SetServerState");
        Step(a, b, c);
        Check(a.Take(ToClient.State, world)?.blob[0] == 7 && b.Take(ToClient.State, world)?.blob[0] == 7 && c.Take(ToClient.State, world)?.blob[0] == 7, "its State reaches everyone, whatever their map");
        a.Send(SyncProtocol.State(world, 1, Blob(9)));
        a.Send(SyncProtocol.Despawn(world));
        Step(a, b, c);
        Check(!b.Has(ToClient.State) && !b.Has(ToClient.Despawn) && server.EntityCount >= 1, "a client's State or Despawn of the server's entity is ignored");
        var heard = new List<(uint sender, ulong id, byte component, byte eventId, byte value)>();
        server.ServerEvent += (sender, id, component, eventId, blob) => heard.Add((sender, id, component, eventId, blob.Length > 0 ? blob[0] : (byte)0));
        a.Send(SyncProtocol.Event(world, 0, 1, EventTarget.Server, Blob(5)));
        Step(a, b, c);
        Check(heard.Count == 1 && heard[0].sender == a.number && heard[0].id == world && heard[0].eventId == 1 && heard[0].value == 5,
            "an event to the server from another map arrives at ServerEvent with its sender");
        Check(server.AccountOf(a.number) == "acc-a" && server.AccountOf(9999) == null, "AccountOf gives the connection's account id");
        Check(server.SendServerEvent(world, 0, 2, b.number, Blob(3)), "SendServerEvent to one connection");
        Step(a, b, c);
        var toB = b.Take(ToClient.Event, world);
        Check(toB != null && toB.sender == 0 && toB.eventId == 2 && toB.blob[0] == 3 && !a.Has(ToClient.Event) && !c.Has(ToClient.Event), "only that connection gets it, from sender 0");
        a.Send(SyncProtocol.Event(world, 0, 9, EventTarget.Map, Blob(6)));
        Step(a, b, c);
        Check(!a.Has(ToClient.Event) && !b.Has(ToClient.Event) && heard.Count == 1, "a client's map-wide event on the server's entity is not relayed (it only talks with the server)");
        server.SendServerEvent(world, 0, 4, 0, Blob(4));
        Step(a, b, c);
        Check(a.Take(ToClient.Event, world) != null && b.Take(ToClient.Event, world) != null && c.Take(ToClient.Event, world) != null, "to connection 0: everyone who sees it");
        ulong mine = SyncProtocol.MakeId(a.number, 1);
        a.Send(SyncProtocol.Spawn(mine, "Net/Box", AllMaps, EntityFlags.None, new[] { Blob(1) }));
        Step(a, b, c);
        Check(a.Take(ToClient.Despawn, mine) != null && !b.Has(m => m.kind == ToClient.Spawn && m.id == mine), "a client cannot spawn on map * (refused with Despawn)");
        a.Send(SyncProtocol.Map(AllMaps));
        ulong onM3 = SyncProtocol.MakeId(c.number, 1);
        c.Send(SyncProtocol.Map("m3"));
        c.Send(SyncProtocol.Spawn(onM3, "Net/Box", "m3", EntityFlags.None, new[] { Blob(1) }));
        Step(a, b, c);
        Check(a.client.IsOpen && a.Take(ToClient.Spawn, onM3) != null, "a client's Map(*) is ignored: it stays on m3 and still sees m3");
        Check(!server.SetServerState(onM3, 0, Blob(1)), "SetServerState refuses an entity a client made");
        Clear(a, b, c);

        Section("blob limits");
        byte[] big = Filled(30 * 1024, 30);
        Check(server.SetServerState(world, 2, big), "a 30KB server blob is accepted");
        Step(a, b, c);
        var gotBig = b.Take(ToClient.State, world);
        Check(gotBig != null && gotBig.blob.Length == big.Length && gotBig.size <= SyncProtocol.MaxFrame, "and arrives whole (" + gotBig?.size + " byte frame)");
        Check(!server.SetServerState(world, 2, new byte[SyncProtocol.MaxServerBlob + 1]), "over 60KB is refused");
        c.Send(SyncProtocol.Build((byte)ToServer.State, w => { w.Write(onM3); w.Write((byte)0); w.Write((ushort)5000); w.Write(new byte[5000]); }));
        Step(a, b, c);
        Check(Rejected(c, RejectReason.Malformed) && a.client.IsOpen && b.client.IsOpen, "a client blob over 4KB is still rejected (that connection only)");
        Clear(a, b);

        Section("spawn then states");
        ulong full = server.SpawnServerEntity(WorldKey, AllMaps, EntityFlags.None, new[] { Filled(SyncProtocol.MaxServerBlob, 1), Filled(SyncProtocol.MaxServerBlob, 2), Blob(3) });
        Step(a, b);
        var d = Join("acc-d", "QA라", "m1");
        var spawn = d.Take(ToClient.Spawn, full);
        Check(spawn != null && spawn.blobs.Length == 3 && Array.TrueForAll(spawn.blobs, x => x.Length == 0), "a server entity's Spawn carries empty blobs");
        var states = d.inbox.FindAll(m => m.kind == ToClient.State && m.id == full);
        Check(states.Count == 3 && states[0].blob.Length == SyncProtocol.MaxServerBlob && states[1].blob[0] == 2 && states[2].blob[0] == 3, "then one State per component follows");
        int largest = 0;
        foreach (var m in d.inbox) largest = Math.Max(largest, m.size);
        foreach (var m in a.inbox) largest = Math.Max(largest, m.size);
        Check(largest <= SyncProtocol.MaxFrame && spawn.size < 200, "no frame over 64KB (largest " + largest + ", Spawn " + spawn.size + " bytes)");
        Note("largest frame with two 60KB sections: " + largest + " bytes");
        Clear(a, b, d);

        Section("world state in world.dat");
        byte[] state = Filled(100 * 1024, 9);
        server.WorldStateProvider = () => state;
        server.Stop();
        Check(FileVersion(w1) == 3 && Same(WorldStore.Open(root, w1).WorldState, state), "the provider's blob is in world.dat version 3 (" + state.Length + " bytes)");
        StartServer(w1);
        Check(Same(server.World.WorldState, state), "and comes back when the server starts again");
        var e = Join("acc-e", "QA마", "m1");
        Check(!e.Has(m => m.kind == ToClient.Spawn && m.key == WorldKey), "server entities are not saved (the game makes its own again)");
        server.Stop();
        string v2 = WorldStore.NewId();
        WriteVersion2(v2);
        var old = WorldStore.Open(root, v2);
        Check(old.CharacterCount == 1 && old.Entities.Count == 1 && old.NextConnection == 5 && old.NightSkipStart == 0 && old.NightSkipEnd == 6 && old.WorldState.Length == 0,
            "a version 2 world.dat reads: its character and entity, the default night 0-6, no world state");
        old.Save();
        Check(FileVersion(v2) == 3 && WorldStore.Open(root, v2).Entities.Count == 1, "and is saved again as version 3");

        Section("night skip");
        loopback = new LoopbackTransport();
        server = new GameServer(loopback, 8);
        Check(Near(server.World.WorldMinutes, 6 * 60), "a new world's clock starts at 06:00 (" + Clock(server.World.WorldMinutes) + ")");
        SetClock(23 * 60 + 59);
        Minute();
        Check(Near(server.World.WorldMinutes, Day + 6 * 60), "23:59 + 1 minute = 06:00 of the next day (" + Clock(server.World.WorldMinutes) + ")");
        Check(server.SetNightSkip(2, 5), "SetNightSkip(2, 5)");
        SetClock(Day + 1 * 60 + 59);
        Minute();
        Check(Near(server.World.WorldMinutes, Day + 5 * 60), "with 2-5: 01:59 + 1 minute = 05:00 (" + Clock(server.World.WorldMinutes) + ")");
        SetClock(Day + 23 * 60 + 59);
        Minute();
        Check(Near(server.World.WorldMinutes, 2 * Day), "and midnight is not skipped any more (" + Clock(server.World.WorldMinutes) + ")");
        Check(server.SetNightSkip(4, 4), "SetNightSkip(4, 4)");
        SetClock(2 * Day + 3 * 60 + 59);
        Minute();
        Minute();
        Check(Near(server.World.WorldMinutes, 2 * Day + 4 * 60 + 1), "start = end skips nothing (03:59 → 04:01 in two minutes: " + Clock(server.World.WorldMinutes) + ")");
        Check(!server.SetNightSkip(5, 2) && !server.SetNightSkip(0, 25) && !server.SetNightSkip(-1, 3) && !server.SetNightSkip(0, 24), "start > end, past 24, negative and the whole day are refused");
        Check(server.World.NightSkipStart == 4 && server.World.NightSkipEnd == 4, "and change nothing");

        Section("night skip saved, -nightskip");
        string w3 = WorldStore.NewId();
        StartServer(w3);
        Check(server.World.NightSkipStart == 0 && server.World.NightSkipEnd == 6, "a new world skips 0-6");
        server.SetNightSkip(2, 5);
        server.Stop();
        StartServer(w3);
        Check(server.World.NightSkipStart == 2 && server.World.NightSkipEnd == 5, "the range is saved in world.dat and comes back");
        server.Stop();
        var arg = WorldStore.Open(root, w3);
        Check(GameServer.ApplyNightSkipArg(arg, "1-4") && arg.NightSkipStart == 1 && arg.NightSkipEnd == 4, "-nightskip 1-4 sets the world's range (what SyncHost does before opening it)");
        Check(!GameServer.ApplyNightSkipArg(arg, "5-2") && !GameServer.ApplyNightSkipArg(arg, "0-25") && !GameServer.ApplyNightSkipArg(arg, "x") && arg.NightSkipStart == 1,
            "a bad -nightskip (5-2, 0-25, x) is refused and changes nothing");
        arg.WorldMinutes = Day + 2 * 60;
        arg.Save();
        StartServer(w3);
        Check(Near(server.World.WorldMinutes, Day + 4 * 60), "a server opening a world at 02:00 with 1-4 moves its clock to 04:00");
        server.Stop();
    }

    static void StartServer(string world)
    {
        loopback = new LoopbackTransport();
        server = new GameServer(loopback, 16, world: WorldStore.Open(root, world));
        server.Log += m => Debug.Log("[" + tag + "] server: " + m);
    }

    static QaPeer Join(string account, string name, string map)
    {
        var p = new QaPeer(loopback.Connect());
        p.Send(SyncProtocol.Hello(account, account, map));
        server.Tick();
        p.number = p.Take(m => m.kind == ToClient.Welcome).number;
        if (p.Take(m => m.kind == ToClient.Character)?.has != true)
        {
            p.Send(SyncProtocol.CharacterCreate(name, "male"));
            server.Tick();
            if (p.Take(m => m.kind == ToClient.CharacterResult)?.result != CharacterResult.Ok) throw new Exception("[EconomySyncQA] FAIL: character " + name);
        }
        return p;
    }

    static void SetClock(double minutes) => server.World.WorldMinutes = minutes;
    static void Minute() { server.AdvanceClock((long)(GameServer.RealSecondsPerGameHour * 1000 / 60)); server.Tick(); }
    static bool Near(double a, double b) => Math.Abs(a - b) < .1; // a tick also adds the real milliseconds between ticks (0.2 game minutes a second)
    static string Clock(double minutes) => "day " + Math.Floor(minutes / Day) + " " + TimeSpan.FromMinutes(minutes % Day).ToString(@"hh\:mm");
    static void Step(params QaPeer[] peers) { server.Tick(); foreach (var p in peers) p.Pump(); }
    static void Clear(params QaPeer[] peers) { foreach (var p in peers) { p.Pump(); p.inbox.Clear(); } }
    static byte[] Blob(params byte[] bytes) => bytes;
    static bool Same(byte[] a, byte[] b) => a != null && b != null && ((ReadOnlySpan<byte>)a).SequenceEqual(b);

    static byte[] Filled(int length, byte value)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)(value + i % 7);
        return bytes;
    }

    static bool Rejected(QaPeer p, RejectReason reason)
    {
        p.Pump();
        return p.Has(m => m.kind == ToClient.Reject && m.reason == reason) && !p.client.IsOpen;
    }

    static int FileVersion(string id)
    {
        using var r = new BinaryReader(File.OpenRead(Path.Combine(root, id, WorldStore.FileName)));
        return r.ReadUInt16();
    }

    // The 12.8 layout, written by hand: version 2 with one character and one saved entity.
    static void WriteVersion2(string id)
    {
        string folder = Path.Combine(root, id);
        Directory.CreateDirectory(folder);
        using var stream = new FileStream(Path.Combine(folder, WorldStore.FileName), FileMode.Create);
        using var w = new BinaryWriter(stream, Encoding.UTF8);
        w.Write((ushort)2);
        SyncProtocol.WriteStr(w, id);
        w.Write(DateTime.UtcNow.Ticks);
        w.Write(500.0);
        w.Write(1);
        SyncProtocol.WriteStr(w, "acc-old"); SyncProtocol.WriteStr(w, "옛사람"); SyncProtocol.WriteStr(w, "female");
        w.Write(10.0); w.Write(90.0);
        SyncProtocol.WriteBlob32(w, new byte[] { 1, 2, 3 });
        w.Write(5u);
        w.Write(1);
        w.Write(SyncProtocol.MakeId(4, 1)); SyncProtocol.WriteStr(w, "Net/WorldDrop"); SyncProtocol.WriteStr(w, "m1");
        w.Write((byte)(EntityFlags.PersistOnLeave | EntityFlags.Claimable)); w.Write(400.0);
        w.Write((byte)1); SyncProtocol.WriteBlob(w, new byte[] { 7 });
        w.Write(0);
    }

    // ---- counting ----

    static void Begin() { checks = 0; section = ""; sections.Clear(); notes.Clear(); }

    static void Section(string name)
    {
        section = name;
        sections.Add(new KeyValuePair<string, int>(name, 0));
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("[" + tag + "] FAIL (" + section + "): " + what);
        checks++;
        if (sections.Count > 0) sections[sections.Count - 1] = new KeyValuePair<string, int>(section, sections[sections.Count - 1].Value + 1);
        Debug.Log("[" + tag + "] ok: " + what);
    }

    static void Note(string what)
    {
        notes.Add(what);
        Debug.Log("[" + tag + "] measured: " + what);
    }

    static void Report(string result, bool failed = false)
    {
        var text = new StringBuilder("[" + tag + "] " + result + ", checks=" + checks);
        foreach (var s in sections) text.Append("\n  ").Append(s.Key).Append(": ").Append(s.Value);
        foreach (var n in notes) text.Append("\n  measured: ").Append(n);
        if (failed) Debug.LogError(text.ToString()); else Debug.Log(text.ToString());
    }
}
