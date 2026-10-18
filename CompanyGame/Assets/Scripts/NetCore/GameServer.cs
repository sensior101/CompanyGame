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
/// Placed cars and dropped items (12.8): an owner can Release a PersistOnLeave + Claimable entity back to the server.
/// PersistOnLeave entities (at most <see cref="MaxPersistent"/> per world) and the next connection number are saved in the
/// world and come back server-owned after a restart. A refused Spawn (no character, a limit) answers Despawn(id) to its
/// sender. A server-owned Expiring entity is moved to the world's lost-and-found list at the first 06:00 of the world
/// clock after it was made; lost items are deleted 7 game days later. The server never reads the entity blobs.
/// Server entities (12.9): the game on the server makes its own entities (<see cref="SpawnServerEntity"/>, owner 0, id =
/// connection 0 + serial, never saved) and changes them with <see cref="SetServerState"/> / <see cref="SendServerEvent"/>.
/// Their blobs may be up to SyncProtocol.MaxServerBlob; their Spawn carries empty blobs and the states follow one State
/// each, so no frame passes 64KB. Entities on map SyncProtocol.AllMaps ("*") go to every connection whatever its map and
/// stay through map changes; clients cannot use that map. <see cref="WorldStateProvider"/> is read before every save
/// (world.dat version 3). The world clock skips the night hours [NightSkipStart, NightSkipEnd) (default 0-6, 9장-6).
/// World entities (12.10.5): the game makes and removes saved, server-owned entities itself (<see cref="SpawnWorldEntity"/>,
/// ids = a connection number the server keeps for itself at start + serial, so they never repeat across restarts) and
/// judges clients through <see cref="SpawnFilter"/>, <see cref="ClaimFilter"/> and <see cref="StateFilter"/>. An entity a
/// connection claimed (made by another) is not that connection's to despawn: it goes back with Release.
/// </summary>
public sealed class GameServer
{
    const long HelloTimeoutMs = 10000, SaveIntervalMs = 60000;
    /// <summary>PersistOnLeave entities one world may hold (12.8); more Spawns are refused.</summary>
    public const int MaxPersistent = 4096;
    /// <summary>World-clock minutes: a day, the 06:00 clearing time, and how long the lost-and-found keeps an item (7 game days).</summary>
    public const double DayMinutes = 1440, ClearAtMinute = 360, LostKeepMinutes = 7 * DayMinutes;
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
        public double created; // world clock minutes
        public bool byServer;  // made by SpawnServerEntity: spawned with empty blobs, the states follow
        public bool Persists => (flags & EntityFlags.PersistOnLeave) != 0;
    }

    readonly IServerTransport transport;
    readonly Dictionary<int, Conn> conns = new Dictionary<int, Conn>();
    readonly Dictionary<ulong, Entity> entities = new Dictionary<ulong, Entity>();
    readonly List<Conn> scratch = new List<Conn>();
    readonly List<Entity> expired = new List<Entity>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly MemoryStream outStream = new MemoryStream();
    readonly BinaryWriter w;
    readonly byte[] passwordHash; // null = no password
    uint nextNumber = 1, serverSerial, worldNumber, worldSerial;
    long nextTimeoutCheck, clockOffset, lastClockAt, nextSaveAt = SaveIntervalMs, lastClearMinute = -1;

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
    /// <summary>Read right before each save: the world state blob kept in world.dat (12.9). Null keeps the one already there.</summary>
    public Func<byte[]> WorldStateProvider;
    /// <summary>
    /// (account, received content) → the content to keep (12.10.1): the game puts its own part in (the server's ledger).
    /// Null from it keeps the content already there; no filter keeps the content as it comes.
    /// </summary>
    public Func<string, byte[], byte[]> CharacterSaveFilter;
    /// <summary>(connection, prefab) → true when that client's Spawn is refused (12.10.5). A refused Spawn is answered with Despawn.</summary>
    public Func<uint, string, bool> SpawnFilter;
    /// <summary>(connection, entity id) → true when its Claim may go on (12.10.5). Refused: answered with Owner (unchanged).</summary>
    public Func<uint, ulong, bool> ClaimFilter;
    /// <summary>(connection, entity id, component, blob) → the blob to keep and send, or null to drop that State (12.10.5).</summary>
    public Func<uint, ulong, byte, byte[], byte[]> StateFilter;
    /// <summary>
    /// (connection, account) of a character entering the world, before its Character goes out (12.10.5): the game may
    /// change its content then (what it is given on entering).
    /// </summary>
    public Action<uint, string> CharacterEntering;

    /// <summary><paramref name="world"/> null: a world in memory only.</summary>
    public GameServer(IServerTransport transport, int maxPlayers, string name = "", string password = "", WorldStore world = null)
    {
        this.transport = transport;
        World = world ?? WorldStore.InMemory();
        MaxPlayers = maxPlayers;
        Name = name ?? "";
        if (!string.IsNullOrEmpty(password)) passwordHash = Hash(password);
        w = new BinaryWriter(outStream, Encoding.UTF8);
        // The saved entities come back server-owned; numbers go on past the file's and every saved id's, so ids never repeat.
        nextNumber = Math.Max(1u, World.NextConnection);
        foreach (var s in World.Entities)
        {
            if (entities.ContainsKey(s.id)) continue;
            entities[s.id] = new Entity { id = s.id, prefab = s.prefab, map = s.map, flags = s.flags, blobs = s.blobs, created = s.created };
            uint number = SyncProtocol.ConnectionOf(s.id);
            if (number != uint.MaxValue && number >= nextNumber) nextNumber = number + 1;
        }
        if (!ValidNightSkip(World.NightSkipStart, World.NightSkipEnd))
        {
            World.NightSkipStart = WorldStore.DefaultNightSkipStart; World.NightSkipEnd = WorldStore.DefaultNightSkipEnd;
        }
        World.WorldMinutes = SkipNight(World.WorldMinutes, World.NightSkipStart, World.NightSkipEnd); // a new world starts at 06:00
    }

    // ---- night skip (9장-6, 12.9) ----

    /// <summary>0 ≤ start ≤ end ≤ 24, not the whole day; start = end skips nothing. A range over midnight (22-2) is not allowed.</summary>
    public static bool ValidNightSkip(int start, int end) => start >= 0 && start <= end && end <= 24 && end - start < 24;

    /// <summary>"start-end" in hours, e.g. "0-6" (the -nightskip argument). False for anything else or an invalid range.</summary>
    public static bool TryParseNightSkip(string text, out int start, out int end)
    {
        start = end = 0;
        string[] parts = (text ?? "").Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out start) && int.TryParse(parts[1], out end) && ValidNightSkip(start, end);
    }

    /// <summary>The -nightskip argument: sets the world's range before a server opens it. False (world unchanged) when invalid.</summary>
    public static bool ApplyNightSkipArg(WorldStore world, string arg)
    {
        if (!TryParseNightSkip(arg, out int start, out int end)) return false;
        world.NightSkipStart = start; world.NightSkipEnd = end;
        return true;
    }

    /// <summary>A time inside the skipped hours of its day moves to their end (the rule of GameTime.NextMinute).</summary>
    public static double SkipNight(double minutes, int start, int end)
    {
        if (start >= end) return minutes;
        double day = Math.Floor(minutes / DayMinutes), inDay = minutes - day * DayMinutes;
        return inDay >= start * 60 && inDay < end * 60 ? day * DayMinutes + end * 60 : minutes;
    }

    /// <summary>The world clock skips [start, end) hours from now on (saved in world.dat). False, and logged, for an invalid range.</summary>
    public bool SetNightSkip(int start, int end)
    {
        if (!ValidNightSkip(start, end)) { Log?.Invoke("night skip " + start + "-" + end + " refused (0 <= start <= end <= 24, not the whole day)"); return false; }
        World.NightSkipStart = start; World.NightSkipEnd = end;
        World.WorldMinutes = SkipNight(World.WorldMinutes, start, end);
        return true;
    }

    /// <summary>The lost-and-found list of this world (12.8), for QA.</summary>
    public IReadOnlyList<WorldStore.LostItem> Lost => World.Lost;

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
        ClearExpired();
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
        if (minutes > 0)
        {
            World.WorldMinutes += minutes;
            foreach (var c in World.Characters) if (c.online) c.minutes += minutes;
        }
        World.WorldMinutes = SkipNight(World.WorldMinutes, World.NightSkipStart, World.NightSkipEnd);
    }

    // Once per world-clock minute: server-owned Expiring entities past their first 06:00 go to the lost-and-found list,
    // and lost items older than 7 game days are deleted.
    // ponytail: scans every entity once a game minute (every 5 real seconds); index the Expiring ones if worlds grow past a few thousand.
    void ClearExpired()
    {
        long minute = (long)Math.Floor(World.WorldMinutes);
        if (minute == lastClearMinute) return;
        lastClearMinute = minute;
        double now = World.WorldMinutes;
        expired.Clear();
        foreach (var e in entities.Values)
            if ((e.flags & EntityFlags.Expiring) != 0 && e.owner == 0 && now >= ClearTime(e.created)) expired.Add(e);
        foreach (var e in expired)
        {
            Remove(e, null);
            World.Lost.Add(new WorldStore.LostItem { prefab = e.prefab, blobs = e.blobs, movedAt = now });
        }
        World.Lost.RemoveAll(l => now - l.movedAt >= LostKeepMinutes);
    }

    /// <summary>The first 06:00 of the world clock after <paramref name="created"/> (made at 06:00 itself: the next day's).</summary>
    public static double ClearTime(double created) => ClearAtMinute + DayMinutes * (Math.Floor((created - ClearAtMinute) / DayMinutes) + 1);

    // ponytail: the whole file is written on the server thread every minute (about 50 x 48KB at most); move to a writer thread if it shows in a tick.
    void SaveWorld()
    {
        World.NextConnection = nextNumber;
        World.Entities.Clear();
        foreach (var e in entities.Values)
            if (e.Persists && !e.byServer) // the game makes its own entities again on start
                World.Entities.Add(new WorldStore.SavedEntity { id = e.id, prefab = e.prefab, map = e.map, flags = e.flags, created = e.created, blobs = e.blobs });
        if (WorldStateProvider != null)
        {
            try { World.WorldState = WorldStateProvider() ?? World.WorldState; }
            catch (Exception e) { Log?.Invoke("world state provider failed (the last state is kept): " + e.Message); }
        }
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
                case ToServer.Release: OnRelease(c, r); break;
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
        c.account = account; c.map = map == SyncProtocol.AllMaps ? "" : map;
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
        foreach (var e in entities.Values) if (Sees(c, e.map)) SendSpawn(c, e);
    }

    void OnSpawn(Conn c, BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        string prefab = SyncProtocol.ReadStr(r), map = SyncProtocol.ReadStr(r);
        var flags = (EntityFlags)r.ReadByte();
        var blobs = new byte[r.ReadByte()][];
        for (int i = 0; i < blobs.Length; i++) blobs[i] = SyncProtocol.ReadBlob(r);
        SyncProtocol.End(r);
        // A bad id gets no answer: a Despawn of it could remove another entity the sender has.
        if (SyncProtocol.ConnectionOf(id) != c.number) { Warn(c, "spawn with another connection's id"); return; }
        if (entities.ContainsKey(id)) { Warn(c, "duplicate spawn id"); return; }
        // The other refusals answer Despawn(id): the sender takes its object back (a dropped item returns to the bag).
        string refused = c.character == null ? "spawn without a character"
            : map == SyncProtocol.AllMaps ? "spawn on the all-maps map"
            : c.owned.Count >= SyncProtocol.MaxEntitiesPerConnection ? "entity limit"
            : (flags & EntityFlags.PersistOnLeave) != 0 && PersistentCount() >= MaxPersistent ? "world entity limit"
            : Refuses(c, prefab) ? "spawn of " + prefab + " refused by the game" : null;
        if (refused != null) { Warn(c, refused); Send(c, DespawnMsg(id)); return; }
        var e = new Entity { id = id, owner = c.number, prefab = prefab, map = map, flags = flags, blobs = blobs, created = World.WorldMinutes };
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
        if (StateFilter != null)
        {
            try { blob = StateFilter(c.number, id, component, blob); }
            catch (Exception ex) { Log?.Invoke("state filter failed (the State is dropped): " + ex.Message); blob = null; }
            if (blob == null || blob.Length > SyncProtocol.MaxBlob) return;
        }
        e.blobs[component] = blob;
        SendToMap(e.map, StateMsg(id, component, blob), c);
    }

    void OnDespawn(Conn c, BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        SyncProtocol.End(r);
        if (!entities.TryGetValue(id, out var e) || e.owner != c.number) return;
        // A claimed entity (a car someone drives) is not the claimer's to remove: it goes back with Release (12.10.5).
        if (SyncProtocol.ConnectionOf(id) != c.number) { Warn(c, "despawn of a claimed entity"); return; }
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
        if (!entities.TryGetValue(id, out var e) || !Sees(c, e.map) || component >= e.blobs.Length) return;
        if (e.byServer && target != EventTarget.Server) return; // a server entity talks only with the server (no relay to everyone)
        if (target == EventTarget.Server)
        {
            try { ServerEvent?.Invoke(c.number, id, component, eventId, blob); }
            catch (Exception ex) { Log?.Invoke("ServerEvent handler failed: " + ex); }
            return;
        }
        byte[] msg = EventMsg(id, component, eventId, c.number, blob);
        if (target == EventTarget.Map) SendToMap(e.map, msg, null);
        else if (FindByNumber(e.owner) is Conn owner) Send(owner, msg);
    }

    void OnMap(Conn c, BinaryReader r)
    {
        string map = SyncProtocol.ReadStr(r);
        SyncProtocol.End(r);
        string old = c.map;
        if (map == SyncProtocol.AllMaps) { Warn(c, "map change to the all-maps map"); return; }
        if (map == old) return;
        c.map = map;
        foreach (var e in entities.Values)
        {
            if (e.map == SyncProtocol.AllMaps) continue; // seen on every map: nothing to despawn or spawn
            if (e.owner == c.number)
            {
                if ((e.flags & EntityFlags.FollowsOwnerMap) == 0 || e.map != old) continue;
                e.map = map;
                SendToMap(old, DespawnMsg(e.id), c);
                SendToMap(map, SpawnMsg(e), c);
            }
            else if (e.map == old) Send(c, DespawnMsg(e.id));
            else if (e.map == map) SendSpawn(c, e);
        }
    }

    void OnClaim(Conn c, BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        SyncProtocol.End(r);
        if (!entities.TryGetValue(id, out var e)) return;
        bool ok = c.character != null && e.owner == 0 && (e.flags & EntityFlags.Claimable) != 0 && e.map == c.map
                  && c.owned.Count < SyncProtocol.MaxEntitiesPerConnection && Allows(c, id);
        if (!ok) { Send(c, OwnerMsg(e)); return; }
        e.owner = c.number;
        c.owned.Add(id);
        SendToMap(e.map, OwnerMsg(e), null);
    }

    // The owner gives a placed car or a dropped item back to the server (12.8), on any map (getting out on the way to another one).
    void OnRelease(Conn c, BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        SyncProtocol.End(r);
        const EntityFlags both = EntityFlags.PersistOnLeave | EntityFlags.Claimable;
        if (!entities.TryGetValue(id, out var e) || e.owner != c.number || (e.flags & both) != both) return;
        e.owner = 0;
        c.owned.Remove(id);
        byte[] msg = OwnerMsg(e);
        SendToMap(e.map, msg, null);
        if (c.map != e.map) Send(c, msg);
    }

    bool Refuses(Conn c, string prefab)
    {
        if (SpawnFilter == null) return false;
        try { return SpawnFilter(c.number, prefab); }
        catch (Exception ex) { Log?.Invoke("spawn filter failed (the Spawn is refused): " + ex.Message); return true; }
    }

    bool Allows(Conn c, ulong id)
    {
        if (ClaimFilter == null) return true;
        try { return ClaimFilter(c.number, id); }
        catch (Exception ex) { Log?.Invoke("claim filter failed (the Claim is refused): " + ex.Message); return false; }
    }

    int PersistentCount()
    {
        int n = 0;
        foreach (var e in entities.Values) if (e.Persists) n++;
        return n;
    }

    // ---- server entities (12.9) ----

    /// <summary>
    /// Makes a server-owned entity (owner 0, id = connection 0 + serial; never saved, gone with the server). Map
    /// SyncProtocol.AllMaps reaches everyone. Blobs may be up to SyncProtocol.MaxServerBlob. Its id, or 0 when refused.
    /// </summary>
    public ulong SpawnServerEntity(string prefab, string map, EntityFlags flags, byte[][] blobs)
    {
        blobs ??= Array.Empty<byte[]>();
        if (blobs.Length > byte.MaxValue || Array.Exists(blobs, b => b != null && b.Length > SyncProtocol.MaxServerBlob))
        { Log?.Invoke("server entity " + prefab + " refused: too many components or a blob over " + SyncProtocol.MaxServerBlob + " bytes"); return 0; }
        var e = new Entity { id = SyncProtocol.MakeId(0, ++serverSerial), prefab = prefab ?? "", map = map ?? "", flags = flags,
            blobs = Array.ConvertAll(blobs, b => b ?? Array.Empty<byte>()), created = World.WorldMinutes, byServer = true };
        entities[e.id] = e;
        foreach (var c in conns.Values) if (c.Welcomed && Sees(c, e.map)) SendSpawn(c, e);
        return e.id;
    }

    /// <summary>The server changes a component of its own entity (sent to everyone who sees it). False when refused (not the server's, too big).</summary>
    public bool SetServerState(ulong id, byte component, byte[] blob)
    {
        blob ??= Array.Empty<byte>();
        if (!entities.TryGetValue(id, out var e) || !e.byServer || e.owner != 0 || component >= e.blobs.Length) return false;
        if (blob.Length > SyncProtocol.MaxServerBlob) { Log?.Invoke("state " + id + "/" + component + " not sent: " + blob.Length + " bytes, over " + SyncProtocol.MaxServerBlob); return false; }
        e.blobs[component] = blob;
        SendToMap(e.map, StateMsg(id, component, blob), null);
        return true;
    }

    /// <summary>An event from the server (sender 0) to connection <paramref name="toConnection"/>, or with 0 to everyone who sees the entity.</summary>
    public bool SendServerEvent(ulong id, byte component, byte eventId, uint toConnection, byte[] blob)
    {
        blob ??= Array.Empty<byte>();
        if (!entities.TryGetValue(id, out var e) || component >= e.blobs.Length || blob.Length > SyncProtocol.MaxServerBlob) return false;
        byte[] msg = EventMsg(id, component, eventId, 0, blob);
        if (toConnection == 0) { SendToMap(e.map, msg, null); return true; }
        var to = FindByNumber(toConnection);
        if (to == null || !Sees(to, e.map)) return false;
        Send(to, msg);
        return true;
    }

    /// <summary>The account id connection <paramref name="connection"/> said Hello with (null when not connected).</summary>
    public string AccountOf(uint connection) => FindByNumber(connection)?.account;

    /// <summary>The map connection <paramref name="connection"/> is on (null when not connected).</summary>
    public string MapOf(uint connection) => FindByNumber(connection)?.map;

    // ---- world entities (12.10.5) ----

    /// <summary>
    /// Makes a saved, server-owned entity as a client's Spawn + Release would (owner 0; world.dat keeps it when
    /// PersistOnLeave), its id under this run's own connection number. Blobs up to SyncProtocol.MaxBlob. Its id, or 0
    /// when refused (no map, a blob too big, the PersistOnLeave limit).
    /// </summary>
    public ulong SpawnWorldEntity(string prefab, string map, EntityFlags flags, byte[][] blobs)
    {
        blobs ??= Array.Empty<byte[]>();
        string refused = string.IsNullOrEmpty(map) || map == SyncProtocol.AllMaps ? "map"
            : blobs.Length > byte.MaxValue || Array.Exists(blobs, b => b != null && b.Length > SyncProtocol.MaxBlob) ? "blob"
            : (flags & EntityFlags.PersistOnLeave) != 0 && PersistentCount() >= MaxPersistent ? "world entity limit" : null;
        if (refused != null) { Log?.Invoke("world entity " + prefab + " refused: " + refused); return 0; }
        if (worldNumber == 0) worldNumber = nextNumber++; // this run's own connection number (saved as used: never again after a restart)
        var e = new Entity { id = SyncProtocol.MakeId(worldNumber, ++worldSerial), prefab = prefab ?? "", map = map, flags = flags,
            blobs = Array.ConvertAll(blobs, b => b ?? Array.Empty<byte>()), created = World.WorldMinutes };
        entities[e.id] = e;
        SendToMap(map, SpawnMsg(e), null);
        return e.id;
    }

    /// <summary>Removes a server-owned entity (not a SpawnServerEntity one); everyone on its map gets Despawn. False when it is not there or someone owns it.</summary>
    public bool RemoveWorldEntity(ulong id)
    {
        if (!entities.TryGetValue(id, out var e) || e.owner != 0 || e.byServer) return false;
        Remove(e, null);
        return true;
    }

    /// <summary>What the server holds of entity <paramref name="id"/>. The blobs are the server's own array: read only.</summary>
    public bool TryGetEntity(ulong id, out string prefab, out string map, out uint owner, out EntityFlags flags, out byte[][] blobs)
    {
        bool found = entities.TryGetValue(id, out var e);
        prefab = e?.prefab; map = e?.map; owner = e?.owner ?? 0; flags = e?.flags ?? EntityFlags.None; blobs = e?.blobs;
        return found;
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

    // The client's content (position, inventory) is kept as it comes, after CharacterSaveFilter put the game's own part in.
    void OnCharacterSave(Conn c, BinaryReader r)
    {
        byte[] content = SyncProtocol.ReadBlob32(r); // over 48KB throws: Reject(Malformed)
        SyncProtocol.End(r);
        if (c.character == null) { Warn(c, "save without a character"); return; }
        if (CharacterSaveFilter != null)
        {
            try { content = CharacterSaveFilter(c.account, content); }
            catch (Exception ex) { Log?.Invoke("character save filter failed (the saved content is kept): " + ex.Message); content = null; }
            if (content == null) return;
        }
        c.character.content = content;
    }

    void Play(Conn c, WorldStore.Character character)
    {
        if (character == null) return;
        RunClocks(); // the time before this belongs to nobody
        c.character = character;
        c.name = character.name;
        character.online = true;
        try { CharacterEntering?.Invoke(c.number, c.account); }
        catch (Exception ex) { Log?.Invoke("character entering failed: " + ex.Message); }
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
            if (c.Welcomed && c != except && Sees(c, map)) Send(c, msg);
    }

    static bool Sees(Conn c, string map) => map == SyncProtocol.AllMaps || c.map == map;

    // A server entity's blobs may each be near 60KB: its Spawn goes out empty and each state as its own State frame.
    void SendSpawn(Conn c, Entity e)
    {
        Send(c, SpawnMsg(e));
        if (!e.byServer) return;
        for (int i = 0; i < e.blobs.Length; i++)
            if (e.blobs[i].Length > 0) Send(c, StateMsg(e.id, (byte)i, e.blobs[i]));
    }

    byte[] SpawnMsg(Entity e) => Msg(ToClient.Spawn, () =>
    {
        w.Write(e.id); w.Write(e.owner);
        SyncProtocol.WriteStr(w, e.prefab); SyncProtocol.WriteStr(w, e.map);
        w.Write((byte)e.flags); w.Write((byte)e.blobs.Length);
        foreach (byte[] blob in e.blobs) SyncProtocol.WriteBlob(w, e.byServer ? null : blob);
    });

    byte[] StateMsg(ulong id, byte component, byte[] blob) => Msg(ToClient.State, () => { w.Write(id); w.Write(component); SyncProtocol.WriteServerBlob(w, blob); });

    byte[] EventMsg(ulong id, byte component, byte eventId, uint sender, byte[] blob) =>
        Msg(ToClient.Event, () => { w.Write(id); w.Write(component); w.Write(eventId); w.Write(sender); SyncProtocol.WriteServerBlob(w, blob); });

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
