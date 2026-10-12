using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Steam mode without Steam: HostRoom + RoomNode (the server's room logic on the host PC) over an in-memory ordered link.
/// Every message goes through the same JSON as the wire. Run with -batchmode -nographics -quit -executeMethod SteamHostRoomQA.Run.
/// Covers: create, join (case-insensitive, full, wrong code), op seq echo, host-only rules, msg rules, snapshot on join,
/// 256-op log, transfer, host drop -> migration (incl. requests that beat the host message), leave, host leaving.
/// </summary>
public static class SteamHostRoomQA
{
    sealed class Machine : IRoomLink
    {
        public readonly string id;
        public readonly RoomNode node;
        public readonly List<NetMsg> got = new List<NetMsg>();
        public readonly FakeNet net;
        public string movedTo;

        public Machine(FakeNet net, string id)
        {
            this.net = net; this.id = id;
            node = new RoomNode(id, "이름" + id, this, m => got.Add(m));
            node.HostMoved += to => movedTo = to;
            net.machines[id] = this;
        }

        public void Send(string peerId, NetMsg m) => net.Enqueue(id, peerId, m);
        public List<NetMsg> Of(string type) => got.FindAll(m => m.t == type);
        public NetMsg Last(string type) { var l = Of(type); return l.Count > 0 ? l[l.Count - 1] : null; }
    }

    sealed class FakeNet
    {
        public readonly Dictionary<string, Machine> machines = new Dictionary<string, Machine>();
        readonly Queue<(string from, string to, string json)> queue = new Queue<(string, string, string)>();

        public void Enqueue(string from, string to, NetMsg m) { queue.Enqueue((from, to, JsonUtility.ToJson(m))); }

        public void Pump()
        {
            while (queue.Count > 0)
            {
                var (from, to, json) = queue.Dequeue();
                if (machines.TryGetValue(to, out var target)) target.node.FromRemote(from, JsonUtility.FromJson<NetMsg>(json));
            }
        }
    }

    static int checks;

    public static void Run()
    {
        CreateJoinAndRules();
        SnapshotAndLog();
        TransferAndMigration();
        LeaveAndHostLeave();
        Debug.Log("[SteamHostRoomQA] RESULT passed, checks=" + checks);
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("[SteamHostRoomQA] FAIL: " + what);
        checks++;
        Debug.Log("[SteamHostRoomQA] ok: " + what);
    }

    static NetMsg Op(string ev, string data) => new NetMsg { t = "op", ev = ev, data = data };

    static (FakeNet, Machine, Machine, Machine) Room(int max = 4)
    {
        var net = new FakeNet();
        var a = new Machine(net, "A"); var b = new Machine(net, "B"); var c = new Machine(net, "C");
        a.node.StartHosting("K7M2QX", "방", max);
        b.node.JoinAsMember("K7M2QX", "A", "방", max); net.Pump();
        c.node.JoinAsMember("K7M2QX", "A", "방", max); net.Pump();
        return (net, a, b, c);
    }

    static void CreateJoinAndRules()
    {
        var (net, a, b, c) = Room(3);
        var ja = a.Last("joined");
        Check(ja != null && ja.rid == "K7M2QX" && ja.host == "A" && ja.players.Length == 1, "host gets joined for its own room (host = me)");
        var jb = b.Last("joined");
        Check(jb != null && jb.host == "A" && jb.players.Length == 2 && jb.players[0].id == "A" && jb.players[1].id == "B", "member gets joined with players in join order");
        Check(a.Of("peer").Count == 2 && a.Last("peer").ev == "join" && a.Last("peer").id == "C", "host sees peer join for B and C");
        Check(b.Last("peer").id == "C" && c.Of("peer").Count == 0, "B sees C join, C does not see itself");

        var d = new Machine(net, "D");
        d.node.JoinAsMember("K7M2QX", "A", "방", 3); net.Pump();
        Check(d.Last("err")?.code == "room_full" && d.Last("joined") == null, "fourth player in a 3-player room: room_full");
        var e = new Machine(net, "E");
        e.node.JoinAsMember("ZZZZZZ", "A", "방", 3); net.Pump();
        Check(e.Last("err")?.code == "room_not_found", "wrong room code: room_not_found");

        // op: server order, echo to everyone including the sender
        b.node.FromLocal(Op("land", "{\"x\":1}")); c.node.FromLocal(Op("land", "{\"x\":2}")); net.Pump();
        foreach (var m in new[] { a, b, c })
        {
            var ops = m.Of("op");
            Check(ops.Count == 2 && ops[0].seq == 1 && ops[0].from == "B" && ops[0].data == "{\"x\":1}" && ops[1].seq == 2 && ops[1].from == "C", "op seq 1,2 echoed to " + m.id + " in host order");
        }

        // host-only rules
        b.node.FromLocal(new NetMsg { t = "state", snap = "{}" });
        b.node.FromLocal(new NetMsg { t = "world", ev = "clock", data = "{}" });
        b.node.FromLocal(new NetMsg { t = "transfer", to = "C" });
        net.Pump();
        var errs = b.Of("err");
        Check(errs.Count == 3 && errs.TrueForAll(m => m.code == "not_host"), "non-host state/world/transfer: not_host x3");
        b.node.FromLocal(new NetMsg { t = "msg", to = "C", data = "hi" }); net.Pump();
        Check(b.Of("err").Count == 4 && b.Last("err").code == "not_host" && c.Of("msg").Count == 0, "non-host msg to another member: not_host");
        b.node.FromLocal(new NetMsg { t = "msg", to = "A", data = "to host" }); net.Pump();
        Check(a.Last("msg")?.from == "B" && a.Last("msg").data == "to host", "non-host msg to the host is relayed with from");
        a.node.FromLocal(new NetMsg { t = "msg", to = "", data = "all" }); net.Pump();
        Check(b.Last("msg").data == "all" && c.Last("msg").data == "all" && a.Of("msg").Count == 1, "host msg to everyone excludes the host");
        a.node.FromLocal(new NetMsg { t = "msg", to = "ZZ", data = "x" }); net.Pump();
        Check(a.Last("err")?.code == "peer_not_found", "host msg to an unknown member: peer_not_found");
        a.node.FromLocal(new NetMsg { t = "world", ev = "clock", data = "{\"min\":5}" }); net.Pump();
        Check(b.Last("world")?.ev == "clock" && c.Last("world") != null && a.Of("world").Count == 0, "host world reaches members only");
        a.node.FromLocal(new NetMsg { t = "state", snap = "SNAP1" }); net.Pump();
        Check(b.Last("state")?.snap == "SNAP1" && a.Of("state").Count == 0, "host state reaches members only");
        a.node.FromLocal(new NetMsg { t = "transfer", to = "A" }); a.node.FromLocal(new NetMsg { t = "transfer", to = "ZZ" });
        Check(a.Of("err").FindAll(m => m.code == "peer_not_found").Count == 3, "transfer to self or a stranger: peer_not_found");
        b.node.FromLocal(new NetMsg { t = "pos", map = "m", x = 1, y = 2, z = 3, ry = 4, sp = 5 }); net.Pump();
        Check(a.Last("pos")?.from == "B" && c.Last("pos").x == 1 && b.Of("pos").Count == 0, "pos relayed to the others with from");
        a.node.FromLocal(Op("e".PadRight(33, 'e'), "{}"));
        Check(a.Last("err").code == "bad_request", "op kind longer than 32 bytes: bad_request");
        a.node.FromLocal(Op("big", new string('x', HostRoom.MaxMsgBytes + 1)));
        Check(a.Last("err").code == "bad_request", "op larger than 8KB: bad_request");
    }

    static void SnapshotAndLog()
    {
        var (net, a, b, c) = Room(8);
        b.node.FromLocal(Op("k", "1")); c.node.FromLocal(Op("k", "2")); net.Pump();
        a.node.FromLocal(new NetMsg { t = "state", snap = "{\"opSeq\":2}" }); net.Pump();
        var e = new Machine(net, "E");
        e.node.JoinAsMember("k7m2qx", "A", "방", 8); net.Pump(); // lower case
        var j = e.Last("joined");
        Check(j != null && j.snap == "{\"opSeq\":2}" && j.ops.Length == 2 && j.ops[1].seq == 2, "join (lower-case code) gets the latest snapshot and recent ops");
        for (int i = 0; i < 300; i++) b.node.FromLocal(Op("k", i.ToString()));
        net.Pump();
        var g = new Machine(net, "G");
        g.node.JoinAsMember("K7M2QX", "A", "방", 8); net.Pump();
        var ops = g.Last("joined").ops;
        Check(ops.Length == 256 && ops[0].seq == 47 && ops[255].seq == 302, "op log keeps the last 256 (seq 47..302 of 302)");
        Check(c.Last("op").seq == 302 && c.Of("op").Count == 302, "members saw every op");
        Check(b.node.Mirror.Ops.Count == 256 && b.node.Mirror.Seq == 302, "member mirror keeps the last 256 ops and the seq");
    }

    static void TransferAndMigration()
    {
        var (net, a, b, c) = Room(8);
        b.node.FromLocal(Op("k", "1")); net.Pump();
        a.node.FromLocal(new NetMsg { t = "state", snap = "SNAP" });
        a.node.FromLocal(new NetMsg { t = "transfer", to = "B" }); net.Pump();
        Check(a.node.Room == null && b.node.Room != null && c.node.Room == null, "transfer: the HostRoom moves from A to B");
        Check(a.movedTo == "B" && a.Last("host").host == "B" && a.Last("host").reason == "transfer", "old host is told (HostMoved) and gets its own host message");
        Check(c.Last("host").host == "B" && c.Last("host").snap == "SNAP" && b.Last("host").host == "B", "everyone gets host=B with the snapshot");
        Check(b.node.Room.Host == "B" && b.node.Room.Members.Count == 3 && b.node.Room.Seq == 1, "new host restored members, seq and ops from its mirror");
        c.node.FromLocal(Op("k", "2")); a.node.FromLocal(Op("k", "3")); net.Pump();
        Check(a.Last("op").seq == 3 && b.Last("op").seq == 3 && c.Last("op").seq == 3, "after the transfer ops are numbered by the new host, seq continues");
        a.node.FromLocal(new NetMsg { t = "msg", to = "C", data = "x" }); net.Pump();
        Check(a.Last("err")?.code == "not_host", "old host is a plain member now: msg to another member is not_host");
        a.node.FromLocal(new NetMsg { t = "state", snap = "x" }); net.Pump();
        Check(a.Last("err").code == "not_host", "old host state: not_host");

        // host PC vanishes (B). Lobby owner becomes C. A notices first and sends an op before C noticed.
        net.machines.Remove("B");
        a.node.HostLost("C");
        a.node.FromLocal(Op("k", "after-drop")); net.Pump();
        Check(c.Of("op").FindAll(m => m.data == "after-drop").Count == 0, "request that reaches C before C noticed is held, not answered yet");
        c.node.HostLost("C"); net.Pump();
        Check(c.node.Room != null && c.node.Room.Host == "C" && c.node.Room.Members.Count == 2, "owner C promoted itself, B no longer a member");
        Check(c.Last("host").host == "C" && c.Last("host").reason == "disconnect" && c.Last("host").snap == "SNAP", "C gets host message (disconnect) with the snapshot");
        Check(a.Last("host").host == "C" && a.Of("peer").FindAll(m => m.ev == "leave" && m.id == "B").Count == 1, "A got peer leave B then host C");
        Check(a.Last("op").data == "after-drop" && a.Last("op").seq == 4 && c.Last("op").seq == 4, "held op was answered after promotion, seq continued (4)");
        a.node.FromLocal(Op("k", "5")); net.Pump();
        Check(c.Last("op").seq == 5 && a.Last("op").seq == 5, "new host keeps ordering");

        // a member that notices late talks to the new host afterwards
        var (net2, a2, b2, c2) = Room(8);
        net2.machines.Remove("A");
        b2.node.HostLost("B"); // B is the owner, promoted at once
        c2.node.FromLocal(Op("k", "x"));       // C still thinks A is the host: the message goes to the dead PC
        c2.node.HostLost("B"); c2.node.FromLocal(Op("k", "y")); net2.Pump();
        Check(b2.Last("op")?.data == "y" && c2.Last("op")?.data == "y" && b2.Of("op").Count == 1, "member that noticed late talks to the new host (op sent to the dead host is lost)");
    }

    static void LeaveAndHostLeave()
    {
        var (net, a, b, c) = Room(8);
        c.node.FromLocal(new NetMsg { t = "leave" }); net.Pump();
        Check(!c.node.InRoom && a.Last("peer").ev == "leave" && a.Last("peer").id == "C" && b.Last("peer").id == "C", "member leave: others get peer leave, room state cleared");
        a.node.FromLocal(new NetMsg { t = "leave" }); net.Pump();
        Check(a.movedTo == "B" && !a.node.InRoom, "host leaves: HostMoved(B) so the lobby owner can be handed over");
        Check(b.Last("host").host == "B" && b.Last("host").reason == "disconnect" && b.node.Room != null, "host leaves: B becomes host (earliest member)");

        var (net2, a2, b2, c2) = Room(8);
        a2.node.PeerLost("C"); net2.Pump();
        Check(b2.Last("peer").id == "C" && b2.Last("peer").ev == "leave" && a2.node.Room.Members.Count == 2, "host sees a lobby member vanish: peer leave broadcast");
    }
}
