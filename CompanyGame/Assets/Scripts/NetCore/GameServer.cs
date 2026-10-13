using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The sync server (Docs/Multiplayer_Redesign.md 12.1): connections, the entity registry, ownership checks,
/// map interest, late-join snapshots, leave handling, claims, size and rate limits. No Unity; driven by <see cref="Tick"/>
/// on one thread. Clients are not trusted: a bad frame rejects and drops that connection only, never throws out of Tick.
/// Owners never get their own entities echoed back (they made them). With a password set, a Hello must carry it (12.3).
/// Characters (12.5): every account has one character in the <see cref="World"/>, sent right after Welcome; a connection
/// without one can only make it (no Spawn or Claim). The server runs the world clock and the character clocks itself
/// and saves the world every 60 seconds and on <see cref="Stop"/>.
/// Peer names (12.7) are character names, never the Hello's: "" until the character exists; making it sends that
/// connection's Peer(in) again with the name (clients read a second Peer(in) of one number as a rename).
/// </summary>
public sealed class GameServer
{
    const long HelloTimeoutMs = 10000, SaveIntervalMs = 60000;
    /// <summary>
    /// Real seconds per game hour, the same as SimpleGameClock.realSecondsPerGameHour (Core/SimpleGameClock.cs, 300).
    /// NetCore cannot read Unity components, so the value is copied here; change both together.
    /// </summary>
    public const double RealSecondsPerGameHour = 300;

    sealed class Conn
    {
        public int handle;
        public uint number; // 0 until Welcome
        public string account = "", name = "", map = "";
        public long openedAt, windowStart;
        public int windowCount, warnings;
        public readonly HashSet<ulong> owned = new HashSet<ulong>();
        public WorldStore.Character character;
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
    readonly byte[] passwordHash; // null = no password
    uint nextNumber = 1;
    long nextTimeoutCheck, clockOffset, lastClockAt, nextSaveAt = SaveIntervalMs;

    public int MaxPlayers { get; }
    /// <summary>Sent in every Welcome.</summary>
    public string Name { get; }
    public int PlayerCount { get; private set; }
    public int EntityCount => entities.Count;
    /// <summary>Message bytes sent (frame headers not included) and messages sent, for load measurement.</summary>
    public long BytesSent { get; private set; }
    public long FramesSent { get; private set; }
    /// <summary>The world file: clocks and characters.</summary>
    public WorldStore World { get; }
    /// <summary>World saves done (the periodic ones and the last one on Stop), for QA.</summary>
    public int Saves { get; private set; }

    /// <summary>(sender, entity id, component, event id, payload) of events sent to the server (target 2). Server game logic hooks here.</summary>
    public event Action<uint, ulong, byte, byte, byte[]> ServerEvent;
    /// <summary>Warnings (rejects, ignored requests). The Unity host forwards them to the console.</summary>
    public event Action<string> Log;

    /// <summary><paramref name="world"/> null: a world in memory only.</summary>
    public GameServer(IServerTransport transport, int maxPlayers, string name = "", string password = "", WorldStore world = null)
    {
        this.transport = transport;
        World = world ?? WorldStore.InMemory();
        MaxPlayers = maxPlayers;
        Name = name ?? "";
        if (!string.IsNullOrEmpty(password)) passwordHash = Hash(password);
        w = new BinaryWriter(outStream, Encoding.UTF8);
    }

    static byte[] Hash(string text)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
    }

    // Compares the hashes (always 32 bytes) without stopping early, so the time says nothing about the password or its length.
    bool PasswordMatches(string given)
    {
        if (passwordHash == null) return true;
        byte[] h = Hash(given);
        int diff = 0;
        for (int i = 0; i < h.Length; i++) diff |= h[i] ^ passwordHash[i];
        return diff == 0;
    }

    long Now => clock.ElapsedMilliseconds + clockOffset;

    /// <summary>QA: pretend <paramref name="ms"/> more real time has passed (clocks, save timer, rate windows).</summary>
    public void AdvanceClock(long ms) => clockOffset += ms;

    public void Tick()
    {
        while (transport.TryReceive(out var e))
        {
            if (e.Kind == TransportEventKind.Connected)
            {
                long now = Now;
                conns[e.Connection] = new Conn { handle = e.Connection, openedAt = now, windowStart = now };
            }
            else if (conns.TryGetValue(e.Connection, out var c))
            {
                if (e.Kind == TransportEventKind.Data) Receive(c, e.Data);
                else Drop(c);
            }
        }
        DropSilentConnections();
        RunClocks();
        if (Now >= nextSaveAt) { nextSaveAt = Now + SaveIntervalMs; SaveWorld(); }
    }

    /// <summary>Stops listening and saves the world (the players still here are marked as having left now).</summary>
    public void Stop()
    {
        RunClocks();
        foreach (var c in World.Characters) if (c.online) c.lastSeenWorldMinutes = World.WorldMinutes;
        SaveWorld();
        transport.Stop();
    }

    // The world clock runs while the server runs; a character clock only while that account is connected (6.2).
    void RunClocks()
    {
        long now = Now;
        double minutes = (now - lastClockAt) / 1000.0 * 60.0 / RealSecondsPerGameHour;
        lastClockAt = now;
        if (minutes <= 0) return;
        World.WorldMinutes += minutes;
        foreach (var c in World.Characters) if (c.online) c.minutes += minutes;
    }

    // ponytail: the whole file is written on the server thread every minute (about 50 x 48KB at most); move to a writer thread if it shows in a tick.
    void SaveWorld()
    {
        try { World.Save(); Saves++; }
        catch (Exception e) { Log?.Invoke("world save failed: " + e.Message); }
    }

    // A socket that never says Hello holds a slot; give it 10 seconds.
    void DropSilentConnections()
    {
        long now = Now;
        if (now < nextTimeoutCheck) return;
        nextTimeoutCheck = now + 1000;
        scratch.Clear();
        foreach (var c in conns.Values) if (!c.Welcomed && now - c.openedAt > HelloTimeoutMs) scratch.Add(c);
        foreach (var c in scratch) Kick(c, RejectReason.Malformed, "no hello");
    }

    void Receive(Conn c, byte[] frame)
    {
        long now = Now;
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
                case ToServer.CharacterCreate: OnCharacterCreate(c, r); break;
                case ToServer.CharacterSave: OnCharacterSave(c, r); break;
                default: throw new InvalidDataException("kind " + (byte)kind);
            }
        }
        catch (Exception ex) { Kick(c, RejectReason.Malformed, ex.Message); }
    }

    // ---- handlers: read the whole message (End) before changing anything ----

    void OnHello(Conn c, BinaryReader r)
    {
        ushort version = r.ReadUInt16();
        // Before the rest: another version's Hello has another layout (version 1 has no password).
        if (version != SyncProtocol.Version) { Kick(c, RejectReason.Version, "version " + version); return; }
        string account = SyncProtocol.ReadStr(r);
        SyncProtocol.ReadStr(r); // the client's own name: shown to nobody, Peer carries the character name
        string map = SyncProtocol.ReadStr(r), password = SyncProtocol.ReadStr(r);
        SyncProtocol.End(r);
        if (!PasswordMatches(password)) { Kick(c, RejectReason.Password, "password"); return; }
        // One connection per account: two copies of one character could each change and save it (duplicated items and money).
        if (HasAccount(account)) { Kick(c, RejectReason.AlreadyConnected, "account already connected"); return; }
        if (PlayerCount >= MaxPlayers) { Kick(c, RejectReason.Full, "full"); return; }
        c.number = nextNumber++;
        c.account = account; c.map = map;
        PlayerCount++;
        Send(c, Msg(ToClient.Welcome, () => { w.Write(c.number); w.Write((ushort)Math.Min(MaxPlayers, ushort.MaxValue)); SyncProtocol.WriteStr(w, Name); }));
        Play(c, World.Find(account));
        Send(c, CharacterMsg(c.character));
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
        if (c.character == null) { Warn(c, "spawn without a character"); return; }
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
        bool ok = c.character != null && e.owner == 0 && (e.flags & EntityFlags.Claimable) != 0 && e.map == c.map
                  && c.owned.Count < SyncProtocol.MaxEntitiesPerConnection;
        if (!ok) { Send(c, OwnerMsg(e)); return; }
        e.owner = c.number;
        c.owned.Add(id);
        SendToMap(e.map, OwnerMsg(e), null);
    }

    // ---- characters (12.5) ----

    void OnCharacterCreate(Conn c, BinaryReader r)
    {
        string name = SyncProtocol.ReadStr(r), gender = SyncProtocol.ReadStr(r);
        SyncProtocol.End(r);
        var result = c.character != null ? CharacterResult.AlreadyExists : World.Create(c.account, name, gender, out _);
        Send(c, Msg(ToClient.CharacterResult, () => w.Write((byte)result)));
        if (result != CharacterResult.Ok) return;
        Play(c, World.Find(c.account));
        byte[] renamed = PeerMsg(true, c);
        foreach (var other in conns.Values) if (other.Welcomed && other != c) Send(other, renamed);
    }

    // The content is the client's own until stage 4 (bank, stocks, inventory, position): kept as it comes.
    void OnCharacterSave(Conn c, BinaryReader r)
    {
        byte[] content = SyncProtocol.ReadBlob32(r); // over 48KB throws: Reject(Malformed)
        SyncProtocol.End(r);
        if (c.character == null) { Warn(c, "save without a character"); return; }
        c.character.content = content;
    }

    void Play(Conn c, WorldStore.Character character)
    {
        if (character == null) return;
        RunClocks(); // the time before this belongs to nobody
        c.character = character;
        c.name = character.name;
        character.online = true;
    }

    byte[] CharacterMsg(WorldStore.Character ch) => Msg(ToClient.Character, () =>
    {
        w.Write((byte)(ch != null ? 1 : 0));
        SyncProtocol.WriteStr(w, ch?.name ?? ""); SyncProtocol.WriteStr(w, ch?.gender ?? "");
        w.Write(ch != null ? (uint)Math.Min(ch.minutes, uint.MaxValue) : 0u);
        w.Write((uint)Math.Min(World.WorldMinutes, uint.MaxValue));
        SyncProtocol.WriteBlob32(w, ch?.content);
    });

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
        if (c.character != null)
        {
            RunClocks();
            c.character.online = false;
            c.character.lastSeenWorldMinutes = World.WorldMinutes;
            c.character = null;
        }
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

    /// <summary>A welcomed connection of <paramref name="account"/> is still here.</summary>
    public bool HasAccount(string account)
    {
        foreach (var c in conns.Values) if (c.Welcomed && c.account == account) return true;
        return false;
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
