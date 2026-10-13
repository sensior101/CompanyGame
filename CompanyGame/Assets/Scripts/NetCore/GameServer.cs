using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

/// <summary>
/// The sync server (Docs/Multiplayer_Redesign.md 12.1): connections, the entity registry, ownership checks,
/// map interest, late-join snapshots, leave handling, claims, size and rate limits. No Unity; driven by <see cref="Tick"/>
/// on one thread. Clients are not trusted: a bad frame rejects and drops that connection only, never throws out of Tick.
/// Owners never get their own entities echoed back (they made them).
/// </summary>
public sealed class GameServer
{
    const long HelloTimeoutMs = 10000;

    sealed class Conn
    {
        public int handle;
        public uint number; // 0 until Welcome
        public string account = "", name = "", map = "";
        public long openedAt, windowStart;
        public int windowCount, warnings;
        public readonly HashSet<ulong> owned = new HashSet<ulong>();
        public bool Welcomed => number != 0;
    }

    sealed class Entity
    {
        public ulong id;
        public uint owner;
        public string prefab, map;
        public EntityFlags flags;
        public byte[][] blobs;
    }

    readonly IServerTransport transport;
    readonly Dictionary<int, Conn> conns = new Dictionary<int, Conn>();
    readonly Dictionary<ulong, Entity> entities = new Dictionary<ulong, Entity>();
    readonly List<Conn> scratch = new List<Conn>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly MemoryStream outStream = new MemoryStream();
    readonly BinaryWriter w;
    uint nextNumber = 1;
    long nextTimeoutCheck;

    public int MaxPlayers { get; }
    public int PlayerCount { get; private set; }
    public int EntityCount => entities.Count;
    /// <summary>Message bytes sent (frame headers not included) and messages sent, for load measurement.</summary>
    public long BytesSent { get; private set; }
    public long FramesSent { get; private set; }

    /// <summary>(sender, entity id, component, event id, payload) of events sent to the server (target 2). Server game logic hooks here.</summary>
    public event Action<uint, ulong, byte, byte, byte[]> ServerEvent;
    /// <summary>Warnings (rejects, ignored requests). The Unity host forwards them to the console.</summary>
    public event Action<string> Log;

    public GameServer(IServerTransport transport, int maxPlayers)
    {
        this.transport = transport;
        MaxPlayers = maxPlayers;
        w = new BinaryWriter(outStream, Encoding.UTF8);
    }

    public void Tick()
    {
        while (transport.TryReceive(out var e))
        {
            if (e.Kind == TransportEventKind.Connected)
            {
                long now = clock.ElapsedMilliseconds;
                conns[e.Connection] = new Conn { handle = e.Connection, openedAt = now, windowStart = now };
            }
            else if (conns.TryGetValue(e.Connection, out var c))
            {
                if (e.Kind == TransportEventKind.Data) Receive(c, e.Data);
                else Drop(c);
            }
        }
        DropSilentConnections();
    }

    public void Stop() => transport.Stop();

    // A socket that never says Hello holds a slot; give it 10 seconds.
    void DropSilentConnections()
    {
        long now = clock.ElapsedMilliseconds;
        if (now < nextTimeoutCheck) return;
        nextTimeoutCheck = now + 1000;
        scratch.Clear();
        foreach (var c in conns.Values) if (!c.Welcomed && now - c.openedAt > HelloTimeoutMs) scratch.Add(c);
        foreach (var c in scratch) Kick(c, RejectReason.Malformed, "no hello");
    }

    void Receive(Conn c, byte[] frame)
    {
        long now = clock.ElapsedMilliseconds;
        if (now - c.windowStart >= 1000) { c.windowStart = now; c.windowCount = 0; }
        if (++c.windowCount > SyncProtocol.MaxFramesPerSecond) { Kick(c, RejectReason.Rate, "rate"); return; }
        if (frame == null || frame.Length == 0 || frame.Length > SyncProtocol.MaxFrame) { Kick(c, RejectReason.Malformed, "frame size"); return; }
        try
        {
            using var r = SyncProtocol.Reader(frame);
            var kind = (ToServer)r.ReadByte();
            if (!c.Welcomed)
            {
                if (kind != ToServer.Hello) throw new InvalidDataException("expected hello");
                OnHello(c, r);
                return;
            }
            switch (kind)
            {
                case ToServer.Spawn: OnSpawn(c, r); break;
                case ToServer.State: OnState(c, r); break;
                case ToServer.Despawn: OnDespawn(c, r); break;
                case ToServer.Event: OnEvent(c, r); break;
                case ToServer.Map: OnMap(c, r); break;
                case ToServer.Claim: OnClaim(c, r); break;
                default: throw new InvalidDataException("kind " + (byte)kind);
            }
        }
        catch (Exception ex) { Kick(c, RejectReason.Malformed, ex.Message); }
    }

    // ---- handlers: read the whole message (End) before changing anything ----

    void OnHello(Conn c, BinaryReader r)
    {
        ushort version = r.ReadUInt16();
        string account = SyncProtocol.ReadStr(r), name = SyncProtocol.ReadStr(r), map = SyncProtocol.ReadStr(r);
        SyncProtocol.End(r);
        if (version != SyncProtocol.Version) { Kick(c, RejectReason.Version, "version " + version); return; }
        if (PlayerCount >= MaxPlayers) { Kick(c, RejectReason.Full, "full"); return; }
        c.number = nextNumber++;
        c.account = account; c.name = name; c.map = map;
        PlayerCount++;
        Send(c, Msg(ToClient.Welcome, () => { w.Write(c.number); w.Write((ushort)Math.Min(MaxPlayers, ushort.MaxValue)); }));
        byte[] joined = PeerMsg(true, c);
        foreach (var other in conns.Values)
        {
            if (!other.Welcomed || other == c) continue;
            Send(other, joined);
            Send(c, PeerMsg(true, other));
        }
        foreach (var e in entities.Values) if (e.map == map) Send(c, SpawnMsg(e));
    }

    void OnSpawn(Conn c, BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        string prefab = SyncProtocol.ReadStr(r), map = SyncProtocol.ReadStr(r);
        var flags = (EntityFlags)r.ReadByte();
        var blobs = new byte[r.ReadByte()][];
        for (int i = 0; i < blobs.Length; i++) blobs[i] = SyncProtocol.ReadBlob(r);
        SyncProtocol.End(r);
        if (SyncProtocol.ConnectionOf(id) != c.number) { Warn(c, "spawn with another connection's id"); return; }
        if (entities.ContainsKey(id)) { Warn(c, "duplicate spawn id"); return; }
        if (c.owned.Count >= SyncProtocol.MaxEntitiesPerConnection) { Warn(c, "entity limit"); return; }
        var e = new Entity { id = id, owner = c.number, prefab = prefab, map = map, flags = flags, blobs = blobs };
        entities[id] = e;
        c.owned.Add(id);
        SendToMap(map, SpawnMsg(e), c);
    }

    void OnState(Conn c, BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        byte component = r.ReadByte();
        byte[] blob = SyncProtocol.ReadBlob(r);
        SyncProtocol.End(r);
        if (!entities.TryGetValue(id, out var e) || e.owner != c.number || component >= e.blobs.Length) return;
        e.blobs[component] = blob;
        SendToMap(e.map, Msg(ToClient.State, () => { w.Write(id); w.Write(component); SyncProtocol.WriteBlob(w, blob); }), c);
    }

    void OnDespawn(Conn c, BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        SyncProtocol.End(r);
        if (!entities.TryGetValue(id, out var e) || e.owner != c.number) return;
        Remove(e, c);
    }

    void OnEvent(Conn c, BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        byte component = r.ReadByte(), eventId = r.ReadByte();
        var target = (EventTarget)r.ReadByte();
        byte[] blob = SyncProtocol.ReadBlob(r);
        SyncProtocol.End(r);
        if (target > EventTarget.Server) throw new InvalidDataException("event target " + (byte)target);
        if (!entities.TryGetValue(id, out var e) || e.map != c.map || component >= e.blobs.Length) return;
        if (target == EventTarget.Server)
        {
            try { ServerEvent?.Invoke(c.number, id, component, eventId, blob); }
            catch (Exception ex) { Log?.Invoke("ServerEvent handler failed: " + ex); }
            return;
        }
        byte[] msg = Msg(ToClient.Event, () => { w.Write(id); w.Write(component); w.Write(eventId); w.Write(c.number); SyncProtocol.WriteBlob(w, blob); });
        if (target == EventTarget.Map) SendToMap(e.map, msg, null);
        else if (FindByNumber(e.owner) is Conn owner) Send(owner, msg);
    }

    void OnMap(Conn c, BinaryReader r)
    {
        string map = SyncProtocol.ReadStr(r);
        SyncProtocol.End(r);
        string old = c.map;
        if (map == old) return;
        c.map = map;
        foreach (var e in entities.Values)
        {
            if (e.owner == c.number)
            {
                if ((e.flags & EntityFlags.FollowsOwnerMap) == 0 || e.map != old) continue;
                e.map = map;
                SendToMap(old, DespawnMsg(e.id), c);
                SendToMap(map, SpawnMsg(e), c);
            }
            else if (e.map == old) Send(c, DespawnMsg(e.id));
            else if (e.map == map) Send(c, SpawnMsg(e));
        }
    }

    void OnClaim(Conn c, BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        SyncProtocol.End(r);
        if (!entities.TryGetValue(id, out var e)) return;
        bool ok = e.owner == 0 && (e.flags & EntityFlags.Claimable) != 0 && e.map == c.map
                  && c.owned.Count < SyncProtocol.MaxEntitiesPerConnection;
        if (!ok) { Send(c, OwnerMsg(e)); return; }
        e.owner = c.number;
        c.owned.Add(id);
        SendToMap(e.map, OwnerMsg(e), null);
    }

    // ---- leaving ----

    void Kick(Conn c, RejectReason reason, string why)
    {
        Log?.Invoke("reject connection " + (c.Welcomed ? c.number.ToString() : "(no hello)") + ": " + reason + " (" + why + ")");
        Send(c, Msg(ToClient.Reject, () => w.Write((byte)reason)));
        transport.Disconnect(c.handle);
        Drop(c);
    }

    void Drop(Conn c)
    {
        if (!conns.Remove(c.handle) || !c.Welcomed) return;
        PlayerCount--;
        foreach (ulong id in c.owned)
        {
            var e = entities[id];
            if ((e.flags & EntityFlags.PersistOnLeave) == 0) Remove(e, null);
            else { e.owner = 0; SendToMap(e.map, OwnerMsg(e), null); }
        }
        c.owned.Clear();
        byte[] left = PeerMsg(false, c);
        foreach (var other in conns.Values) if (other.Welcomed) Send(other, left);
    }

    void Remove(Entity e, Conn except)
    {
        entities.Remove(e.id);
        FindByNumber(e.owner)?.owned.Remove(e.id);
        SendToMap(e.map, DespawnMsg(e.id), except);
    }

    void Warn(Conn c, string what)
    {
        c.warnings++;
        Log?.Invoke("ignored from " + c.number + ": " + what + " (warnings " + c.warnings + ")");
    }

    Conn FindByNumber(uint number)
    {
        if (number == 0) return null;
        foreach (var c in conns.Values) if (c.number == number) return c;
        return null;
    }

    // ---- sending ----

    void Send(Conn c, byte[] msg)
    {
        transport.Send(c.handle, msg);
        BytesSent += msg.Length;
        FramesSent++;
    }

    // ponytail: linear scan of connections per message; fine for 50 players, index by map if it shows in RunLoad.
    void SendToMap(string map, byte[] msg, Conn except)
    {
        foreach (var c in conns.Values)
            if (c.Welcomed && c != except && c.map == map) Send(c, msg);
    }

    byte[] SpawnMsg(Entity e) => Msg(ToClient.Spawn, () =>
    {
        w.Write(e.id); w.Write(e.owner);
        SyncProtocol.WriteStr(w, e.prefab); SyncProtocol.WriteStr(w, e.map);
        w.Write((byte)e.flags); w.Write((byte)e.blobs.Length);
        foreach (byte[] blob in e.blobs) SyncProtocol.WriteBlob(w, blob);
    });

    byte[] DespawnMsg(ulong id) => Msg(ToClient.Despawn, () => w.Write(id));
    byte[] OwnerMsg(Entity e) => Msg(ToClient.Owner, () => { w.Write(e.id); w.Write(e.owner); });

    byte[] PeerMsg(bool joined, Conn c) => Msg(ToClient.Peer, () =>
    {
        w.Write((byte)(joined ? 1 : 0)); w.Write(c.number);
        SyncProtocol.WriteStr(w, c.account); SyncProtocol.WriteStr(w, c.name);
    });

    // One shared writer: a message body never builds another message.
    byte[] Msg(ToClient kind, Action body)
    {
        outStream.SetLength(0);
        w.Write((byte)kind);
        body();
        w.Flush();
        return outStream.ToArray();
    }
}
