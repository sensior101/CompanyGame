using System;
using System.Collections.Generic;

/// <summary>Reliable, ordered delivery to one room member (the Steam transport, or an in-memory fake in the QA).</summary>
public interface IRoomLink
{
    void Send(string peerId, NetMsg message);
}

/// <summary>
/// The room logic of one PC in Steam mode, free of Steam and Unity so the QA can drive it in memory.
/// Hosting: owns a <see cref="HostRoom"/> and answers members. Member: forwards requests to the host and keeps a
/// <see cref="RoomMirror"/>. Everything NetSession should see arrives through <c>deliver</c> as the server's messages.
/// </summary>
public sealed class RoomNode
{
    const long PendingMs = 5000;
    const int PendingMax = 200;

    public string MyId { get; }
    public string MyName { get; }
    public HostRoom Room { get; private set; }
    public RoomMirror Mirror { get; private set; }
    public string HostId { get; private set; } = "";
    public bool InRoom => Room != null || Mirror != null;
    public bool IsHost => Room != null;

    /// <summary>This PC handed the host role to someone else (transfer or leaving as host): the new host id.</summary>
    public event Action<string> HostMoved;

    readonly IRoomLink link;
    readonly Action<NetMsg> deliver;
    readonly List<Pending> pending = new List<Pending>();

    sealed class Pending { public string from; public NetMsg msg; public long at; }

    public RoomNode(string myId, string myName, IRoomLink link, Action<NetMsg> deliver)
    {
        MyId = myId; MyName = myName; this.link = link; this.deliver = deliver;
    }

    RoomPeer Self() => new RoomPeer { id = MyId, name = MyName, send = deliver };
    RoomPeer PeerFor(string id, string name) => new RoomPeer { id = id, name = name, send = m => link.Send(id, m) };

    public void StartHosting(string rid, string name, int max)
    {
        Reset();
        HostId = MyId;
        Room = new HostRoom(rid, name, max, Self());
        Room.HostChanged += OnRoomHostChanged;
    }

    /// <summary>The lobby was entered: ask the host to add me. The host answers with `joined`.</summary>
    public void JoinAsMember(string rid, string hostId, string name, int max)
    {
        Reset();
        HostId = hostId;
        Mirror = new RoomMirror { Rid = rid, Name = name, Max = max, Host = hostId };
        link.Send(hostId, new NetMsg { t = "join", rid = rid, name = MyName });
    }

    public void Reset() { Room = null; Mirror = null; HostId = ""; pending.Clear(); }

    // ---- from this PC's NetSession ----

    public void FromLocal(NetMsg m)
    {
        if (Room != null) { LocalAsHost(m); return; }
        if (Mirror == null) return;
        link.Send(HostId, m);
        if (m.t == "leave") Reset();
    }

    void LocalAsHost(NetMsg m)
    {
        if (m.t == "leave") { LeaveAsHost(); return; }
        string err = Room.Handle(Room.Find(MyId), m);
        if (err != null) deliver(Err(err));
    }

    void LeaveAsHost()
    {
        var self = Room.Find(MyId);
        Room.Leave(self); // with others present the host migrates to the first member (HostMoved)
        Reset();
    }

    // ---- from the network ----

    public void FromRemote(string fromId, NetMsg m)
    {
        if (Room != null) { RemoteAsHost(fromId, m); return; }
        if (Mirror == null) return;
        if (fromId != HostId) { Hold(fromId, m); return; }
        bool promote = m.t == "host" && m.host == MyId;
        Mirror.Observe(m);
        if (m.t == "host") HostId = m.host ?? HostId;
        if (promote) Promote();
        deliver(m);
        if (promote) FlushPending();
    }

    void RemoteAsHost(string fromId, NetMsg m)
    {
        var peer = Room.Find(fromId);
        if (m.t == "join") { Join(fromId, m); return; }
        if (m.t == "leave") { if (peer != null) Room.Leave(peer); return; }
        if (peer == null) { link.Send(fromId, Err("not_in_room")); return; }
        string err = Room.Handle(peer, m);
        if (err != null) peer.send(Err(err));
    }

    void Join(string fromId, NetMsg m)
    {
        string err = string.Equals(m.rid, Room.Rid, StringComparison.OrdinalIgnoreCase) ? Room.Join(PeerFor(fromId, m.name ?? "")) : "room_not_found";
        if (err != null) link.Send(fromId, Err(err));
    }

    /// <summary>Host side: a member is gone from the Steam lobby.</summary>
    public void PeerLost(string id)
    {
        var peer = Room?.Find(id);
        if (peer != null) Room.Leave(peer);
    }

    /// <summary>Member side: the host is gone from the Steam lobby and <paramref name="newOwner"/> owns it now. Same events as the server's disconnect migration.</summary>
    public void HostLost(string newOwner)
    {
        if (Mirror == null) return;
        string old = HostId;
        Mirror.RemoveMember(old);
        deliver(new NetMsg { t = "peer", ev = "leave", id = old });
        string next = Mirror.Members.Exists(p => p.id == newOwner) ? newOwner : (Mirror.Members.Count > 0 ? Mirror.Members[0].id : "");
        if (next == "") { Reset(); return; }
        Mirror.Host = next; HostId = next;
        if (next == MyId) Promote();
        deliver(new NetMsg { t = "host", host = next, reason = "disconnect", snap = Mirror?.Snap ?? Room.Snap });
        FlushPending(); // the new host's requests/echoes may have beaten my own detection of the loss
    }

    public void Tick()
    {
        long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        pending.RemoveAll(p => now - p.at > PendingMs);
    }

    // ---- role changes ----

    void Promote()
    {
        var members = new List<RoomPeer>();
        foreach (var p in Mirror.Members) members.Add(p.id == MyId ? Self() : PeerFor(p.id, p.name));
        Room = HostRoom.Restore(Mirror, members);
        Room.HostChanged += OnRoomHostChanged;
        Mirror = null;
        HostId = MyId;
    }

    void OnRoomHostChanged(string newHost, string reason)
    {
        if (newHost == MyId) return;
        Mirror = RoomMirror.FromRoom(Room);
        Room = null;
        HostId = newHost;
        HostMoved?.Invoke(newHost);
    }

    // Requests that reach the future host before its own `host` message do.
    void Hold(string from, NetMsg m)
    {
        if (m.t == "pos" || pending.Count >= PendingMax) return;
        pending.Add(new Pending { from = from, msg = m, at = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond });
    }

    void FlushPending()
    {
        var list = pending.ToArray();
        pending.Clear();
        foreach (var p in list) FromRemote(p.from, p.msg);
    }

    static NetMsg Err(string code) => new NetMsg { t = "err", code = code };
}
