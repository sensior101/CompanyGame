using System;
using System.Collections.Generic;
using System.Text;

/// <summary>One member as the room sees it. <c>send</c> delivers a message to that member (local loopback or the network).</summary>
public sealed class RoomPeer
{
    public string id, name;
    public Action<NetMsg> send;
}

/// <summary>
/// The room logic of Server/src/rooms.ts, run on the host PC in Steam mode (no transport, no Steam, no rate limits).
/// Same rules: join order members, op seq echo to everyone (last 256 kept), host-only state/world/transfer,
/// non-host `msg` only to the host, snapshot + recent ops in `joined`. Error codes are the server's.
/// Host migration after the host PC vanished is <see cref="Restore"/> on the next host, from a <see cref="RoomMirror"/>.
/// </summary>
public sealed class HostRoom
{
    public const int MaxOps = 256, MaxPlayers = 8, MaxMsgBytes = 8192, MaxStateBytes = 512 * 1024;

    public string Rid { get; }
    public string Name { get; }
    public int Max { get; }
    public string Host { get; private set; }
    public string Snap { get; private set; } = "";
    public long Seq { get; private set; }
    public readonly List<RoomPeer> Members = new List<RoomPeer>(); // join order
    public readonly List<OpInfo> Ops = new List<OpInfo>();

    /// <summary>(newHostId, reason "transfer"|"disconnect"). Raised after the `host` broadcast.</summary>
    public event Action<string, string> HostChanged;

    public HostRoom(string rid, string name, int max, RoomPeer creator)
    {
        Rid = rid; Name = name; Max = max; Host = creator.id;
        Enter(creator);
    }

    HostRoom(RoomMirror m, IList<RoomPeer> members)
    {
        Rid = m.Rid; Name = m.Name; Max = m.Max; Snap = m.Snap; Seq = m.Seq;
        Members.AddRange(members);
        Ops.AddRange(m.Ops);
        Host = Find(m.Host) != null ? m.Host : Members[0].id;
    }

    /// <summary>Rebuild after the old host PC was lost. <paramref name="members"/> are the still-present peers in join order (never empty).</summary>
    public static HostRoom Restore(RoomMirror mirror, IList<RoomPeer> members) => new HostRoom(mirror, members);

    public RoomPeer Find(string id) { foreach (var p in Members) if (p.id == id) return p; return null; }

    public static string ValidateCreate(string name, int max)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 30 || name.IndexOfAny(new[] { '<', '>' }) >= 0 || max < 1 || max > MaxPlayers) return "bad_request";
        return null;
    }

    public string Join(RoomPeer peer)
    {
        if (Find(peer.id) != null) return "already_in_room";
        if (Members.Count >= Max) return "room_full";
        Enter(peer);
        return null;
    }

    public string Leave(RoomPeer peer)
    {
        if (Find(peer.id) == null) return "not_in_room";
        Members.RemoveAll(p => p.id == peer.id);
        if (Members.Count == 0) return null;
        Bcast(new NetMsg { t = "peer", ev = "leave", id = peer.id, name = peer.name });
        if (Host == peer.id) SetHost(Members[0].id, "disconnect");
        return null;
    }

    /// <summary>One message from a member (never `join`/`leave`/`create`/`list`). Returns an error code or null.</summary>
    public string Handle(RoomPeer from, NetMsg m)
    {
        switch (m.t)
        {
            case "pos": Bcast(Pos(from, m), from.id); return null; // not validated (the server drops malformed ones)
            case "transfer": return Transfer(from, m.to);
            case "state": return SetSnap(from, m.snap);
            case "world": return RelayWorld(from, m);
            case "msg": return RelayMsg(from, m);
            case "op": return RelayOp(from, m);
            default: return "bad_request";
        }
    }

    string Transfer(RoomPeer from, string to)
    {
        if (Host != from.id) return "not_host";
        if (to == from.id || string.IsNullOrEmpty(to) || Find(to) == null) return "peer_not_found";
        SetHost(to, "transfer");
        return null;
    }

    string SetSnap(RoomPeer from, string snap)
    {
        if (Host != from.id) return "not_host";
        if (string.IsNullOrEmpty(snap) || Bytes(snap) > MaxStateBytes) return "bad_request";
        Snap = snap;
        Bcast(new NetMsg { t = "state", snap = snap }, from.id);
        return null;
    }

    string RelayWorld(RoomPeer from, NetMsg m)
    {
        if (Host != from.id) return "not_host";
        if (!ValidEv(m.ev) || Bytes(m.data) > MaxMsgBytes) return "bad_request";
        Bcast(new NetMsg { t = "world", ev = m.ev, data = m.data ?? "" }, from.id);
        return null;
    }

    // Host may send to anyone or everyone; other members only to the current host.
    string RelayMsg(RoomPeer from, NetMsg m)
    {
        string to = m.to ?? "";
        if (Bytes(m.data) > MaxMsgBytes) return "bad_request";
        if (Host != from.id && to != Host) return "not_host";
        var msg = new NetMsg { t = "msg", from = from.id, data = m.data ?? "" };
        if (to == "") { Bcast(msg, from.id); return null; }
        var target = Find(to);
        if (to == from.id || target == null) return "peer_not_found";
        target.send(msg);
        return null;
    }

    string RelayOp(RoomPeer from, NetMsg m)
    {
        if (!ValidEv(m.ev) || Bytes(m.data) > MaxMsgBytes) return "bad_request";
        var op = new OpInfo { seq = ++Seq, from = from.id, ev = m.ev, data = m.data ?? "" };
        Ops.Add(op);
        if (Ops.Count > MaxOps) Ops.RemoveAt(0);
        Bcast(new NetMsg { t = "op", seq = op.seq, from = op.from, ev = op.ev, data = op.data });
        return null;
    }

    static NetMsg Pos(RoomPeer from, NetMsg m) =>
        new NetMsg { t = "pos", from = from.id, map = m.map, x = m.x, y = m.y, z = m.z, ry = m.ry, sp = m.sp };

    void Enter(RoomPeer peer)
    {
        Members.Add(peer);
        var players = new PeerInfo[Members.Count];
        for (int i = 0; i < players.Length; i++) players[i] = new PeerInfo { id = Members[i].id, name = Members[i].name };
        peer.send(new NetMsg { t = "joined", rid = Rid, host = Host, players = players, snap = Snap, ops = Ops.ToArray() });
        Bcast(new NetMsg { t = "peer", ev = "join", id = peer.id, name = peer.name }, peer.id);
    }

    void SetHost(string id, string reason)
    {
        Host = id;
        Bcast(new NetMsg { t = "host", host = id, reason = reason, snap = Snap });
        HostChanged?.Invoke(id, reason);
    }

    void Bcast(NetMsg m, string except = null)
    {
        foreach (var p in Members.ToArray()) if (p.id != except) p.send(m);
    }

    static bool ValidEv(string ev) => !string.IsNullOrEmpty(ev) && Bytes(ev) <= 32;
    static int Bytes(string s) => string.IsNullOrEmpty(s) ? 0 : Encoding.UTF8.GetByteCount(s);
}

/// <summary>
/// What every non-host member remembers about the room, from the messages it saw (members, latest snapshot, op log).
/// When the host PC vanishes the next host (Steam lobby owner) rebuilds a <see cref="HostRoom"/> from this.
/// </summary>
public sealed class RoomMirror
{
    public string Rid = "", Name = "", Host = "", Snap = "";
    public int Max = HostRoom.MaxPlayers;
    public long Seq;
    public readonly List<PeerInfo> Members = new List<PeerInfo>();
    public readonly List<OpInfo> Ops = new List<OpInfo>();

    public static RoomMirror FromRoom(HostRoom room)
    {
        var m = new RoomMirror { Rid = room.Rid, Name = room.Name, Max = room.Max, Host = room.Host, Snap = room.Snap, Seq = room.Seq };
        foreach (var p in room.Members) m.Members.Add(new PeerInfo { id = p.id, name = p.name });
        m.Ops.AddRange(room.Ops);
        return m;
    }

    public void Observe(NetMsg m)
    {
        switch (m.t)
        {
            case "joined":
                Rid = m.rid ?? ""; Host = m.host ?? ""; Snap = m.snap ?? "";
                Members.Clear(); if (m.players != null) Members.AddRange(m.players);
                Ops.Clear(); if (m.ops != null) Ops.AddRange(m.ops);
                Seq = Ops.Count > 0 ? Ops[Ops.Count - 1].seq : 0;
                break;
            case "peer":
                RemoveMember(m.id);
                if (m.ev == "join") Members.Add(new PeerInfo { id = m.id, name = m.name });
                break;
            case "state": Snap = m.snap ?? Snap; break;
            case "host": Host = m.host ?? Host; if (!string.IsNullOrEmpty(m.snap)) Snap = m.snap; break;
            case "op":
                Seq = m.seq;
                Ops.Add(new OpInfo { seq = m.seq, from = m.from, ev = m.ev, data = m.data });
                if (Ops.Count > HostRoom.MaxOps) Ops.RemoveAt(0);
                break;
        }
    }

    public void RemoveMember(string id) { Members.RemoveAll(p => p.id == id); }
}
