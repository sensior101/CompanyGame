using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// QA of the sync layer (Docs/Multiplayer_Redesign.md 12.1, password and server name 12.3).
///   -executeMethod SyncLayerQA.Run         in-memory server, checklist 1-12, the position State size and the password (with -quit)
///   -executeMethod SyncLayerQA.RunLoad     49 and 9 in-memory fake clients at 10Hz for 10s, numbers only (with -quit)
///   -executeMethod SyncLayerQA.RunProcess  builds a Windows player to Builds/SyncLayerQA, runs it as -gameserver,
///                                          two fake TCP clients see each other (with -quit)
///   -executeMethod SyncLayerQA.RunPlay     play mode: in-app TCP server + the real SyncClient + a fake TCP client
///                                          (WITHOUT -quit; the QA exits the editor). Env QA_SHOTS = folder for screenshots.
/// </summary>
[InitializeOnLoad]
public static class SyncLayerQA
{
    const string PlayFlag = "SyncLayerQA.play";
    const string ProbeKey = "QA/SyncProbe";
    static int checks;
    static LoopbackTransport loopback;
    static GameServer server;

    static SyncLayerQA() => EditorApplication.playModeStateChanged += OnPlayState;

    // ================= in-memory checklist =================

    [MenuItem("CompanyGame/Net/Run Sync Layer QA")]
    public static void Run()
    {
        checks = 0;
        Handshake();
        Password();
        Interest();
        BadInput();
        Codec();
        Debug.Log("[SyncLayerQA] RESULT passed, checks=" + checks);
    }

    static void Handshake()
    {
        NewServer(2);
        var a = Join("A", "m1"); var b = Join("B", "m1");
        Check(a.number != 0 && b.number != 0 && a.number != b.number, "1 Hello → Welcome with different connection numbers (" + a.number + ", " + b.number + ")");
        Step(a, b);
        Check(a.Has(m => m.kind == ToClient.Peer && m.joined && m.number == b.number && m.name == "B"), "1 the first player hears Peer(in) for the second");
        Check(b.Has(m => m.kind == ToClient.Peer && m.joined && m.number == a.number), "1 the newcomer hears Peer(in) for who is already there");

        var old = Connect();
        old.Send(SyncProtocol.Hello("x", "Old", "m1", version: 99));
        Step(old);
        Check(Rejected(old, RejectReason.Version), "1 another protocol version → Reject(Version) and closed");

        var third = Connect();
        third.Send(SyncProtocol.Hello("c", "C", "m1"));
        Step(third);
        Check(Rejected(third, RejectReason.Full), "2 over the maximum → Reject(Full) and closed");
        Check(server.PlayerCount == 2, "2 rejected sockets are not counted");
    }

    // 12.3: password and server name (protocol version 2)
    static void Password()
    {
        NewServer(4, name: "QA서버", password: "비밀1234");
        var wrong = Connect();
        wrong.Send(SyncProtocol.Hello("w", "W", "m1", "비밀123"));
        var longer = Connect();
        longer.Send(SyncProtocol.Hello("l", "L", "m1", "비밀12345"));
        var empty = Connect();
        empty.Send(SyncProtocol.Hello("e", "E", "m1"));
        Step(wrong, longer, empty);
        Check(Rejected(wrong, RejectReason.Password) && Rejected(longer, RejectReason.Password) && Rejected(empty, RejectReason.Password),
            "pw a wrong, longer or empty password → Reject(Password) and closed");
        Check(server.PlayerCount == 0, "pw rejected passwords are not counted");
        var right = Connect();
        right.Send(SyncProtocol.Hello("r", "R", "m1", "비밀1234"));
        Step(right);
        var welcome = right.Take(m => m.kind == ToClient.Welcome);
        Check(welcome != null && right.client.IsOpen, "pw the right password → Welcome");
        Check(welcome.name == "QA서버", "pw the Welcome carries the server name (" + welcome.name + ")");

        NewServer(4, name: "열린 서버");
        var any = Connect();
        any.Send(SyncProtocol.Hello("a", "A", "m1", "아무거나"));
        var none = Connect();
        none.Send(SyncProtocol.Hello("n", "N", "m1"));
        Step(any, none);
        Check(any.Has(m => m.kind == ToClient.Welcome && m.name == "열린 서버") && none.Has(m => m.kind == ToClient.Welcome),
            "pw a server without a password lets any password (and none) in");

        var v1 = Connect();
        v1.Send(SyncProtocol.Build((byte)ToServer.Hello, w => { w.Write((ushort)1); SyncProtocol.WriteStr(w, "v1"); SyncProtocol.WriteStr(w, "V1"); SyncProtocol.WriteStr(w, "m1"); }));
        Step(v1);
        Check(Rejected(v1, RejectReason.Version), "pw a version 1 Hello (no password field) → Reject(Version), not Malformed");
    }

    static void Interest()
    {
        NewServer(8);
        var a = Join("A", "m1"); var b = Join("B", "m1"); var c = Join("C", "m2");
        Step(a, b, c); Clear(a, b, c);

        // 3 interest
        ulong box = SyncProtocol.MakeId(a.number, 1);
        a.Send(SyncProtocol.Spawn(box, "Net/Box", "m1", EntityFlags.None, new[] { Pos(new Vector3(1, 2, 3), 10), new byte[] { 7 } }));
        Step(a, b, c);
        var spawn = b.Take(ToClient.Spawn, box);
        Check(spawn != null && spawn.owner == a.number && spawn.key == "Net/Box" && spawn.map == "m1" && spawn.blobs.Length == 2
              && Same(spawn.blobs[0], Pos(new Vector3(1, 2, 3), 10)) && Same(spawn.blobs[1], new byte[] { 7 }), "3 B on the same map gets A's Spawn with owner, key and state");
        Check(!c.Has(ToClient.Spawn), "3 C on another map gets nothing");
        Check(!a.Has(ToClient.Spawn), "3 the owner gets no echo");
        Clear(a, b, c);

        // 4 owner-only state / despawn
        a.Send(SyncProtocol.State(box, 0, Pos(new Vector3(4, 5, 6), 20)));
        Step(a, b, c);
        var state = b.Take(ToClient.State, box);
        Check(state != null && state.component == 0 && Same(state.blob, Pos(new Vector3(4, 5, 6), 20)), "4 A's State reaches B");
        Check(state.size + 4 <= 30, "4 a position State is " + state.size + " bytes (+4 TCP length = " + (state.size + 4) + ")");
        b.Send(SyncProtocol.State(box, 0, Pos(new Vector3(9, 9, 9), 0)));
        b.Send(SyncProtocol.Despawn(box));
        Step(a, b, c);
        Check(a.inbox.Count == 0 && b.inbox.Count == 0 && c.inbox.Count == 0, "4 B's State and Despawn on A's entity are ignored");
        a.Send(SyncProtocol.State(box, 1, new byte[] { 8 }));
        Step(a, b, c);
        Check(b.Take(ToClient.State, box)?.component == 1, "4 the entity is still there and still A's");

        // 5 bad spawns
        b.Send(SyncProtocol.Spawn(SyncProtocol.MakeId(a.number, 2), "Net/Box", "m1", EntityFlags.None, new byte[0][]));
        a.Send(SyncProtocol.Spawn(box, "Net/Other", "m1", EntityFlags.None, new byte[0][]));
        Step(a, b, c);
        Check(!a.Has(ToClient.Spawn) && !b.Has(ToClient.Spawn) && server.EntityCount == 1, "5 a Spawn with another connection's number and a duplicate id are ignored");

        // 6 events
        uint serverSender = 0; ulong serverId = 0; byte serverEvent = 0; byte[] serverPayload = null;
        server.ServerEvent += (sender, id, component, eventId, payload) => { serverSender = sender; serverId = id; serverEvent = eventId; serverPayload = payload; };
        b.Send(SyncProtocol.Event(box, 0, 5, EventTarget.Owner, new byte[] { 1 }));
        Step(a, b, c);
        var toOwner = a.Take(ToClient.Event, box);
        Check(toOwner != null && toOwner.sender == b.number && toOwner.eventId == 5 && Same(toOwner.blob, new byte[] { 1 }) && !b.Has(ToClient.Event) && !c.Has(ToClient.Event),
            "6 target 0 goes to the owner only, with sender B");
        b.Send(SyncProtocol.Event(box, 0, 6, EventTarget.Map, new byte[] { 2 }));
        Step(a, b, c);
        var toA = a.Take(ToClient.Event, box); var toB = b.Take(ToClient.Event, box);
        Check(toA != null && toB != null && toA.sender == b.number && toB.sender == b.number && toA.eventId == 6 && !c.Has(ToClient.Event),
            "6 target 1 goes to everyone on the map (sender included), not to other maps");
        b.Send(SyncProtocol.Event(box, 0, 7, EventTarget.Server, new byte[] { 3 }));
        Step(a, b, c);
        Check(serverSender == b.number && serverId == box && serverEvent == 7 && Same(serverPayload, new byte[] { 3 }) && !a.Has(ToClient.Event) && !b.Has(ToClient.Event),
            "6 target 2 goes to the ServerEvent callback with sender B");
        c.Send(SyncProtocol.Event(box, 0, 8, EventTarget.Map, new byte[] { 4 }));
        Step(a, b, c);
        Check(!a.Has(ToClient.Event) && !b.Has(ToClient.Event), "6 an event from another map is ignored");

        // 7 map change and late join
        c.Send(SyncProtocol.Map("m1"));
        Step(a, b, c);
        var seen = c.Take(ToClient.Spawn, box);
        Check(seen != null && Same(seen.blobs[0], Pos(new Vector3(4, 5, 6), 20)) && Same(seen.blobs[1], new byte[] { 8 }), "7 C moving to A's map gets the entity with its latest full state");
        c.Send(SyncProtocol.Map("m2"));
        Step(a, b, c);
        Check(c.Take(ToClient.Despawn, box) != null, "7 C leaving that map gets a Despawn");
        var d = Join("D", "m1");
        var late = d.Take(ToClient.Spawn, box);
        Check(late != null && late.owner == a.number && Same(late.blobs[0], Pos(new Vector3(4, 5, 6), 20)), "7 late D gets the entity with its latest full state");
        Step(a, b, c, d); Clear(a, b, c, d);

        // 8 FollowsOwnerMap
        ulong avatar = SyncProtocol.MakeId(a.number, 3);
        a.Send(SyncProtocol.Spawn(avatar, "Net/Avatar", "m1", EntityFlags.FollowsOwnerMap, new[] { Pos(Vector3.zero, 0) }));
        Step(a, b, c, d); Clear(a, b, c, d);
        a.Send(SyncProtocol.Map("m2"));
        Step(a, b, c, d);
        Check(b.Take(ToClient.Despawn, avatar) != null && d.Take(ToClient.Despawn, avatar) != null, "8 the old map gets a Despawn of the following avatar");
        Check(!b.Has(ToClient.Despawn) && !a.Has(ToClient.Despawn), "8 A's other entity stays on m1 (no Despawn for it)");
        var moved = c.Take(ToClient.Spawn, avatar);
        Check(moved != null && moved.map == "m2" && moved.owner == a.number, "8 the new map gets a Spawn of the avatar on m2");
        Clear(a, b, c, d);

        // 9 leave
        ulong car = SyncProtocol.MakeId(a.number, 4);
        a.Send(SyncProtocol.Spawn(car, "Net/Car", "m1", EntityFlags.PersistOnLeave | EntityFlags.Claimable, new[] { Pos(new Vector3(10, 0, 0), 90) }));
        Step(a, b, c, d); Clear(a, b, c, d);
        a.client.Close();
        Step(b, c, d);
        Check(b.Take(ToClient.Despawn, box) != null && d.Take(ToClient.Despawn, box) != null, "9 A's plain entity is despawned when A leaves");
        Check(c.Take(ToClient.Despawn, avatar) != null, "9 A's avatar is despawned on m2");
        var keptB = b.Take(ToClient.Owner, car);
        Check(keptB != null && keptB.owner == 0 && !b.Has(ToClient.Despawn) && server.EntityCount == 1, "9 the PersistOnLeave entity stays, now Owner 0");
        Check(b.Has(m => m.kind == ToClient.Peer && !m.joined && m.number == a.number) && c.Has(m => m.kind == ToClient.Peer && !m.joined)
              && d.Has(m => m.kind == ToClient.Peer && !m.joined), "9 everyone gets Peer(out)");
        Clear(b, c, d);

        // 10 claim
        c.Send(SyncProtocol.Map("m1"));
        Step(b, c, d);
        Check(c.Take(ToClient.Spawn, car)?.owner == 0, "10 C sees the server-owned car");
        b.Send(SyncProtocol.Claim(car));
        c.Send(SyncProtocol.Claim(car));
        Step(b, c, d);
        Check(b.Last(ToClient.Owner, car)?.owner == b.number && c.Last(ToClient.Owner, car)?.owner == b.number && d.Last(ToClient.Owner, car)?.owner == b.number,
            "10 two Claims in one Tick: B (first) owns it, everyone is told");
        Clear(b, c, d);
        c.Send(SyncProtocol.State(car, 0, Pos(new Vector3(1, 1, 1), 0)));
        b.Send(SyncProtocol.State(car, 0, Pos(new Vector3(2, 2, 2), 0)));
        Step(b, c, d);
        var bState = c.Take(ToClient.State, car);
        Check(bState != null && Same(bState.blob, Pos(new Vector3(2, 2, 2), 0)) && !b.Has(ToClient.State) && d.inbox.FindAll(m => m.kind == ToClient.State).Count == 1,
            "10 B's State passes, C's is ignored");
    }

    static void BadInput()
    {
        NewServer(64);
        var a = Join("A", "m1"); var b = Join("B", "m1");
        ulong box = SyncProtocol.MakeId(a.number, 1);
        a.Send(SyncProtocol.Spawn(box, "Net/Box", "m1", EntityFlags.None, new[] { Pos(Vector3.zero, 0) }));
        Step(a, b); Clear(a, b);

        Bad("truncated State", RejectReason.Malformed, new byte[] { (byte)ToServer.State, 1, 2 });
        Bad("unknown kind", RejectReason.Malformed, new byte[] { 99 });
        Bad("empty frame", RejectReason.Malformed, new byte[0]);
        Bad("trailing bytes", RejectReason.Malformed, Concat(SyncProtocol.Map("m1"), new byte[] { 0 }));
        Bad("blob over 4KB", RejectReason.Malformed, SyncProtocol.Build((byte)ToServer.State, w => { w.Write(box); w.Write((byte)0); w.Write((ushort)5000); w.Write(new byte[5000]); }));
        Bad("event target 3", RejectReason.Malformed, SyncProtocol.Build((byte)ToServer.Event, w => { w.Write(box); w.Write((byte)0); w.Write((byte)0); w.Write((byte)3); w.Write((ushort)0); }));
        Bad("frame over 16KB", RejectReason.Malformed, new byte[SyncProtocol.MaxFrame + 1]);
        var flood = new byte[SyncProtocol.MaxFramesPerSecond][];
        for (int i = 0; i < flood.Length; i++) flood[i] = SyncProtocol.Map(i % 2 == 0 ? "x" : "y");
        Bad("301 frames in a second", RejectReason.Rate, flood);

        var early = Connect();
        early.Send(SyncProtocol.State(box, 0, Pos(Vector3.one, 0)));
        Step(early);
        Check(Rejected(early, RejectReason.Malformed), "11 a message before Hello → Reject(Malformed)");

        // random frames from 300 connections: the server must survive whatever they say
        var random = new System.Random(12345);
        for (int i = 0; i < 300; i++)
        {
            var fuzz = Connect();
            fuzz.Send(SyncProtocol.Hello("f" + i, "F", "m1"));
            var junk = new byte[random.Next(1, 48)];
            random.NextBytes(junk);
            junk[0] = (byte)random.Next(1, 8);
            fuzz.Send(junk);
            Step(fuzz);
            fuzz.client.Close();
        }
        server.Tick();
        Check(true, "11 300 random frames did not throw out of Tick");
        StillServing(a, b, "after the random frames");

        void Bad(string what, RejectReason reason, params byte[][] frames)
        {
            var bad = Connect();
            bad.Send(SyncProtocol.Hello("bad", "Bad", "m1"));
            foreach (var frame in frames) bad.Send(frame);
            Step(bad, a, b);
            Check(Rejected(bad, reason), "11 " + what + " → Reject(" + reason + ") and closed");
            StillServing(a, b, "after " + what);
        }
    }

    static void StillServing(QaPeer a, QaPeer b, string when)
    {
        Clear(a, b);
        ulong box = SyncProtocol.MakeId(a.number, 1);
        var pos = Pos(new Vector3(UnityEngine.Random.Range(-9f, 9f), 0, 0), 0);
        a.Send(SyncProtocol.State(box, 0, pos));
        Step(a, b);
        var got = b.Take(ToClient.State, box);
        Check(got != null && Same(got.blob, pos) && a.client.IsOpen && b.client.IsOpen, "11 the server and other connections still work " + when);
        Clear(a, b);
    }

    static void Codec()
    {
        var source = new GameObject("SyncQA source").AddComponent<NetTransform>();
        float[] yaws = { 0f, 0.004f, 45.3f, 123.456f, 179.997f, 270.01f, 359.996f };
        Vector3[] points = { Vector3.zero, new Vector3(1.5f, -2.25f, 1000.125f), new Vector3(-1234.567f, 89.01f, 0.0001f) };
        float worst = 0f;
        int size = 0;
        bool exact = true;
        foreach (var point in points)
            foreach (float yaw in yaws)
            {
                source.transform.SetPositionAndRotation(point, Quaternion.Euler(0f, yaw, 0f));
                byte[] blob = Blob(source.Write);
                size = blob.Length;
                var target = new GameObject("SyncQA target").AddComponent<NetTransform>();
                using (var r = SyncProtocol.Reader(blob)) target.Read(r);
                var p = target.transform.position;
                exact &= p.x == point.x && p.y == point.y && p.z == point.z;
                worst = Mathf.Max(worst, Mathf.Abs(Mathf.DeltaAngle(target.transform.eulerAngles.y, yaw)));
                Object.DestroyImmediate(target.gameObject);
            }
        Object.DestroyImmediate(source.gameObject);
        Check(exact, "12 NetTransform positions are exact after write → read (" + points.Length * yaws.Length + " samples)");
        Check(worst <= .01f, "12 yaw error after u16 is at most " + worst.ToString("F5") + "° (≤ 0.01°)");
        int frame = SyncProtocol.State(SyncProtocol.MakeId(1, 1), 0, new byte[size]).Length;
        Check(size == 14 && frame + 4 <= 30, "size: NetTransform state = " + size + " bytes, position State message = " + frame + " bytes, on TCP " + (frame + 4) + " bytes (≤ 30)");
    }

    // ================= load =================

    public static void RunLoad()
    {
        Load(49);
        Load(9);
        Debug.Log("[SyncLayerQA] RESULT load measured");
    }

    static void Load(int players)
    {
        NewServer(players + 1, quiet: true);
        var peers = new List<QaPeer>();
        var ids = new ulong[players];
        for (int i = 0; i < players; i++)
        {
            peers.Add(Join("L" + i, "load"));
            ids[i] = SyncProtocol.MakeId(peers[i].number, 1);
            peers[i].Send(SyncProtocol.Spawn(ids[i], "Net/Avatar", "load", EntityFlags.FollowsOwnerMap, new[] { Pos(new Vector3(i, 0, 0), 0) }));
        }
        server.Tick();
        foreach (var p in peers) Drain(p);
        long bytes0 = server.BytesSent, frames0 = server.FramesSent, received = 0;
        var clock = Stopwatch.StartNew();
        var tick = new Stopwatch();
        double total = 0, worst = 0;
        int ticks = 0, rounds = 0;
        long nextRound = 0;
        while (clock.ElapsedMilliseconds < 10000)
        {
            if (clock.ElapsedMilliseconds >= nextRound)
            {
                nextRound += 100;
                rounds++;
                for (int i = 0; i < players; i++)
                    peers[i].Send(SyncProtocol.State(ids[i], 0, Pos(new Vector3(i, 0, rounds * .1f), rounds)));
            }
            tick.Restart();
            server.Tick();
            tick.Stop();
            double ms = tick.Elapsed.TotalMilliseconds;
            total += ms; worst = Math.Max(worst, ms); ticks++;
            foreach (var p in peers) received += Drain(p);
            Thread.Sleep(15); // about one game frame between ticks
        }
        double seconds = clock.Elapsed.TotalSeconds;
        long bytes = server.BytesSent - bytes0, frames = server.FramesSent - frames0;
        Debug.Log($"[SyncLayerQA] LOAD players={players} seconds={seconds:F1} rounds={rounds} ticks={ticks} tick_avg_ms={total / ticks:F3} tick_max_ms={worst:F3} "
                  + $"sent_bytes_per_s={bytes / seconds:F0} sent_bytes_per_s_with_tcp_header={(bytes + 4 * frames) / seconds:F0} sent_msgs_per_s={frames / seconds:F0} "
                  + $"received={received} expected={(long)rounds * players * (players - 1)}");
        server.Stop();
    }

    static int Drain(QaPeer p)
    {
        int n = 0;
        while (p.client.TryReceive(out _)) n++;
        return n;
    }

    // ================= separate process =================

    public static void RunProcess()
    {
        checks = 0;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/SyncLayerQA"));
        string exe = Path.Combine(dir, "SyncServerQA.exe");
        string scene = Array.Find(EditorBuildSettings.scenes, s => s.enabled).path; // one scene is enough for a server
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scene }, locationPathName = exe, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
        });
        Check(report.summary.result == BuildResult.Succeeded, "Windows player built to " + dir + " (" + report.summary.result + ")");

        int port = FreePort();
        string log = Path.Combine(dir, "server.log");
        var process = Process.Start(new ProcessStartInfo(exe, $"-batchmode -nographics -gameserver {port} -maxplayers 4 -bind 127.0.0.1 -logFile \"{log}\"")
        {
            UseShellExecute = false, CreateNoWindow = true,
        });
        try
        {
            var a = ConnectTcp(port, process, log);
            var b = ConnectTcp(port, process, log);
            a.Send(SyncProtocol.Hello("qa-a", "A", "qa"));
            b.Send(SyncProtocol.Hello("qa-b", "B", "qa"));
            a.number = WaitSync(a, m => m.kind == ToClient.Welcome, "A welcome").number;
            b.number = WaitSync(b, m => m.kind == ToClient.Welcome, "B welcome").number;
            Check(a.number != b.number, "both fake clients welcomed by the server process (" + a.number + ", " + b.number + ")");
            ulong ea = SyncProtocol.MakeId(a.number, 1), eb = SyncProtocol.MakeId(b.number, 1);
            a.Send(SyncProtocol.Spawn(ea, ProbeKey, "qa", EntityFlags.None, new[] { Pos(Vector3.one, 0) }));
            b.Send(SyncProtocol.Spawn(eb, ProbeKey, "qa", EntityFlags.None, new[] { Pos(Vector3.up, 0) }));
            var gotB = WaitSync(a, m => m.kind == ToClient.Spawn && m.id == eb, "B's entity at A");
            var gotA = WaitSync(b, m => m.kind == ToClient.Spawn && m.id == ea, "A's entity at B");
            Check(gotA.owner == a.number && gotB.owner == b.number && Same(gotA.blobs[0], Pos(Vector3.one, 0)), "each fake client got the other's entity through the server process");
            a.Send(SyncProtocol.State(ea, 0, Pos(Vector3.right, 90)));
            Check(Same(WaitSync(b, m => m.kind == ToClient.State && m.id == ea, "A's state at B").blob, Pos(Vector3.right, 90)), "a State goes through the server process");

            process.Kill();
            process.WaitForExit(10000);
            var until = DateTime.UtcNow.AddSeconds(10);
            while ((a.client.IsOpen || b.client.IsOpen) && DateTime.UtcNow < until) { a.Pump(); b.Pump(); Thread.Sleep(20); }
            Check(!a.client.IsOpen && !b.client.IsOpen, "both clients notice the server process is gone");
        }
        finally
        {
            if (!process.HasExited) process.Kill();
        }
        Debug.Log("[SyncLayerQA] RESULT process passed, checks=" + checks + " (server log: " + log + ")");
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static QaPeer ConnectTcp(int port, Process process, string log)
    {
        var until = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var client = new TcpClientTransport();
            try { client.Connect("127.0.0.1", port).GetAwaiter().GetResult(); return new QaPeer(client); }
            catch (Exception)
            {
                if (process.HasExited) throw new Exception("server process exited (" + process.ExitCode + "), see " + log);
                if (DateTime.UtcNow > until) throw new Exception("TIMEOUT connecting to the server process, see " + log);
                Thread.Sleep(500);
            }
        }
    }

    static QaMsg WaitSync(QaPeer p, Func<QaMsg, bool> match, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            var m = p.Take(match);
            if (m != null) return m;
            Thread.Sleep(10);
        }
        throw new Exception("[SyncLayerQA] TIMEOUT waiting for " + what);
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
        int code = 0;
        checks = 0;
        try { await PlayBody(); Debug.Log("[SyncLayerQA] RESULT play passed, checks=" + checks); }
        catch (Exception e) { Debug.LogError("[SyncLayerQA] RESULT play FAILED: " + e.Message + "\n" + e.StackTrace); code = 1; }
        EditorApplication.Exit(code);
    }

    static async Task PlayBody()
    {
        var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        template.SetActive(false);
        template.name = "SyncProbe template";
        template.AddComponent<NetTransform>();
        template.AddComponent<SyncQaProbe>();
        SyncClient.PrefabFactory = key => key == ProbeKey ? template : null;

        var client = SyncClient.Instance;
        string map = SceneLoadManager.CurrentMap.path;
        Check(client.Map == map && map.Length > 0, "SyncMapBinder gave the client the current map (" + map + ")");

        // A map change between Hello and Welcome must still reach the server. The server is not ticked until the Hello
        // is out, so the Welcome cannot arrive before SetMap.
        var host = SyncHost.StartInApp(new ServerSettings { port = 0, maxPlayers = 8 });
        host.enabled = false;
        await WaitFor(() => !client.Connecting, 5, "the SyncClient socket and its Hello");
        Check(!client.Connected && client.LastError == "", "Hello sent, no Welcome yet");
        const string earlyMap = "QA/EarlyMap";
        client.SetMap(earlyMap);
        host.enabled = true;
        await WaitFor(() => client.Connected, 5, "SyncClient welcomed by the in-app server");
        Check(host.Port > 0 && client.MyConnection != 0, "the real SyncClient joined the in-app server on 127.0.0.1:" + host.Port);
        var earlySocket = new TcpClientTransport();
        await earlySocket.Connect("127.0.0.1", host.Port);
        var early = new QaPeer(earlySocket);
        early.Send(SyncProtocol.Hello("qa-early", "early", earlyMap));
        early.number = (await WaitMsg(early, m => m.kind == ToClient.Welcome, "early fake welcome")).number;
        ulong earlyId = SyncProtocol.MakeId(early.number, 1);
        early.Send(SyncProtocol.Spawn(earlyId, ProbeKey, earlyMap, EntityFlags.None, new[] { Pos(Vector3.zero, 0), new byte[0] }));
        await WaitFor(() => client.Find(earlyId), 5, "the entity made on the early map");
        Check(client.Find(earlyId).Map == earlyMap, "SetMap before Welcome moved the server's view of the client to " + earlyMap);
        earlySocket.Close();
        client.SetMap(map);
        await WaitFor(() => !client.Find(earlyId), 5, "the early map entity to go");

        var socket = new TcpClientTransport();
        await socket.Connect("127.0.0.1", host.Port);
        var fake = new QaPeer(socket);
        fake.Send(SyncProtocol.Hello("qa-fake", "가짜", map));
        fake.number = (await WaitMsg(fake, m => m.kind == ToClient.Welcome, "fake welcome")).number;
        await WaitMsg(fake, m => m.kind == ToClient.Peer && m.joined && m.number == client.MyConnection, "Peer(in) for the Unity client");
        Check(true, "a fake TCP client joined and hears the Unity client as a peer");

        var cam = Camera.main.transform;
        Vector3 ahead = cam.position + cam.forward * 6f;
        Vector3 left = ahead - cam.right * 1.3f, right = ahead + cam.right * 1.3f;
        ulong id = SyncProtocol.MakeId(fake.number, 1);
        fake.Send(SyncProtocol.Spawn(id, ProbeKey, map, EntityFlags.None, new[] { Pos(left, 30), new byte[0] }));
        NetEntity remote = null;
        await WaitFor(() => remote = client.Find(id), 5, "the fake client's entity in the scene");
        Check(remote.gameObject.activeInHierarchy && (remote.transform.position - left).sqrMagnitude < 1e-6f && !remote.IsMine && remote.Owner == fake.number,
            "the fake client's entity appeared where it put it, owned by it");
        remote.GetComponent<Renderer>().material.color = new Color(1f, .55f, .1f);

        var mine = Object.Instantiate(template, right, Quaternion.Euler(0f, -20f, 0f));
        mine.name = "SyncProbe (Unity side)";
        mine.SetActive(true);
        mine.GetComponent<Renderer>().material.color = new Color(.2f, .5f, 1f);
        var entity = client.Spawn(ProbeKey, mine, EntityFlags.None);
        Check(entity && entity.IsMine, "the Unity side spawned its own entity");
        var spawned = await WaitMsg(fake, m => m.kind == ToClient.Spawn && m.id == entity.Id, "Unity's entity at the fake client");
        Check(spawned.owner == client.MyConnection && spawned.key == ProbeKey && spawned.blobs.Length == 2 && (PosOf(spawned.blobs[0]) - right).sqrMagnitude < 1e-6f,
            "the fake client received Unity's entity with its owner, key and position (owner " + spawned.owner + "/" + client.MyConnection
            + ", key " + spawned.key + ", blobs " + spawned.blobs.Length + ", at " + PosOf(spawned.blobs[0]).ToString("F3") + " vs " + right.ToString("F3") + ")");
        await Shot("S_entities_spawned.png");

        Vector3 to = left + Vector3.up * 1.2f;
        fake.Send(SyncProtocol.State(id, 0, Pos(to, 120)));
        await WaitFor(() => (remote.transform.position - to).sqrMagnitude < 1e-4f && Mathf.Abs(Mathf.DeltaAngle(remote.transform.eulerAngles.y, 120f)) < .5f, 3, "the remote entity to move and turn");
        Check(true, "the fake client's entity moved and turned to its new State");

        mine.transform.position += Vector3.up * 1.2f;
        var moved = await WaitMsg(fake, m => m.kind == ToClient.State && m.id == entity.Id && (PosOf(m.blob) - mine.transform.position).sqrMagnitude < 1e-6f, "Unity's movement at the fake client");
        Check(moved != null, "the fake client received Unity's entity moving");

        fake.Send(SyncProtocol.Event(id, 1, 9, EventTarget.Map, new byte[] { 42 }));
        var probe = remote.GetComponent<SyncQaProbe>();
        await WaitFor(() => probe.lastEvent == 9, 3, "the event on the remote entity");
        Check(probe.lastSender == fake.number && probe.lastValue == 42, "the remote entity's component got the event with the fake client as sender");
        mine.GetComponent<SyncQaProbe>().SendEvent(4, EventTarget.Map, w => w.Write((byte)77));
        var ev = await WaitMsg(fake, m => m.kind == ToClient.Event && m.id == entity.Id, "Unity's event at the fake client");
        Check(ev.sender == client.MyConnection && ev.eventId == 4 && ev.component == 1 && ev.blob[0] == 77, "the fake client got Unity's event");
        await Shot("S_entities_moved.png");

        socket.Close();
        await WaitFor(() => !remote, 5, "the fake client's entity to disappear");
        Check(client.Find(id) == null && mine, "the fake client's entity is gone after it disconnected; Unity's own stays");

        Object.Destroy(mine);
        client.Disconnect();
        host.Stop();
        await Awaitable.NextFrameAsync();
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

    static async Task Shot(string file)
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (string.IsNullOrEmpty(dir) || !Camera.main) return;
        Directory.CreateDirectory(dir);
        await Awaitable.WaitForSecondsAsync(.5f);
        await TitleShots.Snap(Camera.main, Path.Combine(dir, file));
    }

    // ================= helpers =================

    static void NewServer(int max, bool quiet = false, string name = "", string password = "")
    {
        loopback = new LoopbackTransport();
        server = new GameServer(loopback, max, name, password);
        if (!quiet) server.Log += m => Debug.Log("[SyncLayerQA] server: " + m);
    }

    static QaPeer Connect() => new QaPeer(loopback.Connect());

    static QaPeer Join(string name, string map)
    {
        var p = Connect();
        p.Send(SyncProtocol.Hello("acc-" + name, name, map));
        server.Tick();
        var welcome = p.Take(m => m.kind == ToClient.Welcome);
        if (welcome == null) throw new Exception("[SyncLayerQA] FAIL: " + name + " was not welcomed");
        p.number = welcome.number;
        return p;
    }

    static void Step(params QaPeer[] peers)
    {
        server.Tick();
        foreach (var p in peers) p.Pump();
    }

    static void Clear(params QaPeer[] peers)
    {
        foreach (var p in peers) { p.Pump(); p.inbox.Clear(); }
    }

    static bool Rejected(QaPeer p, RejectReason reason)
    {
        p.Pump();
        return p.Has(m => m.kind == ToClient.Reject && m.reason == reason) && !p.client.IsOpen;
    }

    /// <summary>A NetTransform blob: 3 floats + u16 yaw.</summary>
    static byte[] Pos(Vector3 p, float yaw) => Blob(w => { w.Write(p.x); w.Write(p.y); w.Write(p.z); w.Write(NetTransform.EncodeYaw(yaw)); });

    static Vector3 PosOf(byte[] blob)
    {
        using var r = SyncProtocol.Reader(blob);
        return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    static byte[] Blob(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        write(w);
        w.Flush();
        return stream.ToArray();
    }

    static byte[] Concat(byte[] a, byte[] b)
    {
        var all = new byte[a.Length + b.Length];
        a.CopyTo(all, 0); b.CopyTo(all, a.Length);
        return all;
    }

    static bool Same(byte[] a, byte[] b) => a != null && b != null && ((ReadOnlySpan<byte>)a).SequenceEqual(b);

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("[SyncLayerQA] FAIL: " + what);
        checks++;
        Debug.Log("[SyncLayerQA] ok: " + what);
    }

    /// <summary>A fake client: raw protocol over any client transport.</summary>
    sealed class QaPeer
    {
        public readonly IClientTransport client;
        public readonly List<QaMsg> inbox = new List<QaMsg>();
        public uint number;

        public QaPeer(IClientTransport client) { this.client = client; }

        public void Send(byte[] frame) => client.Send(frame);

        public void Pump()
        {
            while (client.TryReceive(out byte[] frame)) inbox.Add(QaMsg.Parse(frame));
        }

        public bool Has(Func<QaMsg, bool> match) { Pump(); return inbox.Exists(m => match(m)); }
        public bool Has(ToClient kind) => Has(m => m.kind == kind);

        public QaMsg Take(Func<QaMsg, bool> match)
        {
            Pump();
            int i = inbox.FindIndex(m => match(m));
            if (i < 0) return null;
            var found = inbox[i];
            inbox.RemoveAt(i);
            return found;
        }

        public QaMsg Take(ToClient kind, ulong id) => Take(m => m.kind == kind && m.id == id);
        public QaMsg Last(ToClient kind, ulong id) { Pump(); return inbox.FindLast(m => m.kind == kind && m.id == id); }
    }

    /// <summary>Any server → client message, parsed strictly (a malformed one fails the QA).</summary>
    sealed class QaMsg
    {
        public ToClient kind;
        public int size;
        public ulong id;
        public uint owner, sender, number;
        public byte component, eventId;
        public RejectReason reason;
        public string key, map, account, name;
        public EntityFlags flags;
        public bool joined;
        public byte[] blob;
        public byte[][] blobs;

        public static QaMsg Parse(byte[] frame)
        {
            using var r = SyncProtocol.Reader(frame);
            var m = new QaMsg { kind = (ToClient)r.ReadByte(), size = frame.Length };
            switch (m.kind)
            {
                case ToClient.Welcome: m.number = r.ReadUInt32(); r.ReadUInt16(); m.name = SyncProtocol.ReadStr(r); break;
                case ToClient.Reject: m.reason = (RejectReason)r.ReadByte(); break;
                case ToClient.Spawn:
                    m.id = r.ReadUInt64(); m.owner = r.ReadUInt32(); m.key = SyncProtocol.ReadStr(r); m.map = SyncProtocol.ReadStr(r);
                    m.flags = (EntityFlags)r.ReadByte(); m.blobs = new byte[r.ReadByte()][];
                    for (int i = 0; i < m.blobs.Length; i++) m.blobs[i] = SyncProtocol.ReadBlob(r);
                    break;
                case ToClient.State: m.id = r.ReadUInt64(); m.component = r.ReadByte(); m.blob = SyncProtocol.ReadBlob(r); break;
                case ToClient.Despawn: m.id = r.ReadUInt64(); break;
                case ToClient.Event:
                    m.id = r.ReadUInt64(); m.component = r.ReadByte(); m.eventId = r.ReadByte(); m.sender = r.ReadUInt32(); m.blob = SyncProtocol.ReadBlob(r);
                    break;
                case ToClient.Owner: m.id = r.ReadUInt64(); m.owner = r.ReadUInt32(); break;
                case ToClient.Peer:
                    m.joined = r.ReadByte() == 1; m.number = r.ReadUInt32(); m.account = SyncProtocol.ReadStr(r); m.name = SyncProtocol.ReadStr(r);
                    break;
                default: throw new Exception("[SyncLayerQA] FAIL: unknown message kind " + (byte)m.kind);
            }
            SyncProtocol.End(r);
            return m;
        }
    }
}
