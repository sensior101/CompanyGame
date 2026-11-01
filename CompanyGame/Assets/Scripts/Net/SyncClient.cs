using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Client of the sync layer (Docs/Multiplayer_Redesign.md 12.1). Created at startup, idle until Connect.
/// Says Hello, tells the server the current map (SyncMapBinder), makes and removes the remote entities, applies their
/// states and events, and sends the dirty states of my entities every 0.1s. The socket threads only move bytes;
/// everything here runs in Update on the main thread. Right after Welcome the server sends this account's character of
/// its world (12.5): <see cref="CharacterKnown"/>, then <see cref="HasCharacter"/> and its fields; without one the title
/// makes it with <see cref="CreateCharacter"/>. CharacterSync sends the content with <see cref="SaveCharacter"/>.
/// Blobs from the server may be up to SyncProtocol.MaxServerBlob (the world sections, 12.9); entities on map
/// SyncProtocol.AllMaps are kept through map changes.
/// EOS (12.13): <see cref="ConnectEos"/> joins a server by its PUID over EOS P2P; every Hello carries my EOS ID token
/// ("" without EOS). While connected a Ping goes out every 2 seconds: <see cref="RoundTripMs"/>.
/// </summary>
public sealed class SyncClient : MonoBehaviour
{
    const float SendInterval = .1f, PingInterval = 2f;

    public static SyncClient Instance { get; private set; }

    /// <summary>Prefab key → prefab to instantiate for remote entities. QA replaces it to test without assets.</summary>
    public static Func<string, GameObject> PrefabFactory = DefaultFactory;

    public bool Connecting { get; private set; }
    /// <summary>True between Welcome and the disconnect.</summary>
    public bool Connected { get; private set; }
    public uint MyConnection { get; private set; }
    public int MaxPlayers { get; private set; }
    public string Map { get; private set; } = "";
    /// <summary>The server's name from its Welcome (no &lt;&gt;, at most 30 characters: it comes from any server).</summary>
    public string ServerName { get; private set; } = "";
    /// <summary>"connect_failed", "disconnected", or the Reject reason (Version, Full, Malformed, Rate, Password, AlreadyConnected, Identity).</summary>
    public string LastError { get; private set; } = "";
    /// <summary>The last Ping's round trip in milliseconds, -1 before the first Pong.</summary>
    public int RoundTripMs { get; private set; } = -1;
    /// <summary>"tcp", "p2p" (EOS direct) or "relay" (EOS relay), "" when not connected.</summary>
    public string Link => transport == null ? "" : transport is EosClientTransport eos ? (eos.Relayed ? "relay" : "p2p") : "tcp";

    /// <summary>True once the server's Character message arrived (right after Welcome).</summary>
    public bool CharacterKnown { get; private set; }
    /// <summary>The server has a character for my account in its world (from Character, or a successful CreateCharacter).</summary>
    public bool HasCharacter { get; private set; }
    public string CharacterName { get; private set; } = "";
    /// <summary>"male" or "female".</summary>
    public string CharacterGender { get; private set; } = "";
    /// <summary>The content the server kept (empty for a new character): CharacterSync reads it.</summary>
    public byte[] CharacterContent { get; private set; } = Array.Empty<byte>();
    /// <summary>The server's clocks when I joined, in game minutes (6.2). Kept for later use; nothing applies them yet.</summary>
    public uint CharacterMinutes { get; private set; }
    public uint WorldMinutes { get; private set; }
    /// <summary>The answer to the last CreateCharacter, null while waiting (or none sent).</summary>
    public CharacterResult? LastCharacterResult { get; private set; }

    /// <summary>
    /// (joined, connection number, account id, character name). Sent for everyone already there right after Welcome. The
    /// name is "" until that player has a character; a second joined=true of the same number carries the new name.
    /// </summary>
    public event Action<bool, uint, string, string> PeerChanged;

    /// <summary>True at Welcome, false when that connection ends (after the remote copies are gone; my own objects stay).</summary>
    public static event Action<bool> ConnectionChanged;

    readonly Dictionary<ulong, NetEntity> entities = new Dictionary<ulong, NetEntity>();
    readonly List<NetEntity> scratch = new List<NetEntity>();
    readonly MemoryStream blobStream = new MemoryStream();
    BinaryWriter blobWriter;
    IClientTransport transport;
    uint serial;
    string pendingName, pendingGender;
    int attempt; // a Disconnect during the TCP connect abandons that attempt
    float nextSendAt, nextPingAt;

    static GameObject DefaultFactory(string key) => Resources.Load<GameObject>(key);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; PrefabFactory = DefaultFactory; ConnectionChanged = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoCreate()
    {
        if (Instance) return;
        var go = new GameObject("SyncClient");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<SyncClient>();
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;
        blobWriter = new BinaryWriter(blobStream);
    }

    void OnDestroy() { if (Instance == this) Instance = null; transport?.Close(); }
    void OnApplicationQuit() { transport?.Close(); }

    /// <summary>Connects over TCP and says Hello with GameSession's account id and name, the server password and my EOS ID token.</summary>
    public async void Connect(string host, int port, string password = "")
    {
        if (Connecting || transport != null) return;
        Connecting = true; LastError = ""; ServerName = "";
        ForgetCharacter();
        int mine = ++attempt;
        var next = new TcpClientTransport();
        try
        {
            await next.Connect(host, port);
            if (mine != attempt) { next.Close(); return; }
            Open(next, password);
        }
        catch (Exception)
        {
            next.Close();
            if (mine != attempt) return;
            LastError = "connect_failed";
        }
        Connecting = false;
    }

    /// <summary>Connects to the server of PUID <paramref name="serverPuid"/> over EOS P2P (12.13). The Hello opens the connection.</summary>
    public void ConnectEos(string serverPuid, string password = "")
    {
        if (Connecting || transport != null) return;
        LastError = ""; ServerName = "";
        ForgetCharacter();
        attempt++;
        try { Open(new EosClientTransport(serverPuid), password); }
        catch (Exception e) { Debug.LogWarning("[Sync] EOS connect failed: " + e.Message); LastError = "connect_failed"; }
    }

    void Open(IClientTransport next, string password)
    {
        transport = next;
        RoundTripMs = -1;
        transport.Send(SyncProtocol.Hello(GameSession.LocalPlayerId, GameSession.LocalPlayerName, Map, password, token: EosAccount.IdToken()));
    }

    public void Disconnect()
    {
        attempt++;
        transport?.Close();
        Closed("");
    }

    /// <summary>Asks the server to make my character (name rule as TitleFlow.IsValidName). The answer comes in <see cref="LastCharacterResult"/>.</summary>
    public void CreateCharacter(string name, string gender)
    {
        if (!Connected || HasCharacter) return;
        LastCharacterResult = null;
        pendingName = name; pendingGender = gender;
        transport.Send(SyncProtocol.CharacterCreate(name ?? "", gender ?? ""));
    }

    /// <summary>Hands the server my character's content (at most 48KB). False when not sent.</summary>
    public bool SaveCharacter(byte[] content)
    {
        if (!Connected || !HasCharacter) return false;
        if (content.Length > SyncProtocol.MaxCharacter) { Debug.LogError("[Sync] character content is " + content.Length + " bytes, over " + SyncProtocol.MaxCharacter + "; not sent"); return false; }
        CharacterContent = content;
        transport.Send(SyncProtocol.CharacterSave(content));
        return true;
    }

    void ForgetCharacter()
    {
        CharacterKnown = HasCharacter = false;
        CharacterName = CharacterGender = "";
        CharacterContent = Array.Empty<byte>();
        CharacterMinutes = WorldMinutes = 0;
        LastCharacterResult = null;
    }

    /// <summary>My interest moves to this map: remote entities here are dropped now (the server sends the new map's ones).</summary>
    public void SetMap(string map)
    {
        map ??= "";
        if (map == Map) return;
        Map = map;
        if (transport == null) return; // the Hello will carry it
        transport.Send(SyncProtocol.Map(map)); // also between Hello and Welcome: TCP keeps the order, the server reads it after the Hello
        scratch.Clear();
        foreach (var e in entities.Values)
        {
            if (e.Map == SyncProtocol.AllMaps) continue; // the world entity (12.9) stays on every map
            if (!e.IsMine) scratch.Add(e);
            else if ((e.Flags & EntityFlags.FollowsOwnerMap) != 0) e.Map = map;
        }
        foreach (var e in scratch) RemoveRemote(e);
    }

    /// <summary>Shares <paramref name="instance"/> (made from <paramref name="prefabKey"/>) on my current map. Null when not connected.</summary>
    public NetEntity Spawn(string prefabKey, GameObject instance, EntityFlags flags = EntityFlags.None)
    {
        if (!Connected || !instance) return null;
        var entity = instance.GetComponent<NetEntity>();
        if (!entity) entity = instance.AddComponent<NetEntity>();
        if (entity.IsSpawned) return entity;
        entity.Init(SyncProtocol.MakeId(MyConnection, ++serial), MyConnection, prefabKey, flags, Map);
        if (entity.Behaviours.Length > byte.MaxValue) { Debug.LogError("[Sync] too many NetBehaviours on " + instance.name); return null; }
        var blobs = new byte[entity.Behaviours.Length][];
        for (int i = 0; i < blobs.Length; i++) blobs[i] = Capture(w => entity.Behaviours[i].Write(w)) ?? Array.Empty<byte>();
        foreach (var b in entity.Behaviours) b.Dirty = false;
        entities[entity.Id] = entity;
        transport.Send(SyncProtocol.Spawn(entity.Id, prefabKey, Map, flags, blobs));
        return entity;
    }

    /// <summary>Ask the server for a Claimable, server-owned entity. The answer comes as <see cref="NetEntity.OwnerChanged"/>.</summary>
    public void Claim(NetEntity entity)
    {
        if (Connected && entity && entity.IsSpawned) transport.Send(SyncProtocol.Claim(entity.Id));
    }

    /// <summary>
    /// Gives my PersistOnLeave + Claimable entity back to the server (12.8). Its changed states go out first, so a parked
    /// car's place arrives before the new owner. The answer comes as <see cref="NetEntity.OwnerChanged"/> (owner 0).
    /// </summary>
    public void Release(NetEntity entity)
    {
        if (!Connected || !entity || !entity.IsMine) return;
        SendDirty(entity);
        transport.Send(SyncProtocol.Release(entity.Id));
    }

    public NetEntity Find(ulong id) => entities.TryGetValue(id, out var e) && e ? e : null;

    internal void SendEvent(NetBehaviour behaviour, byte id, EventTarget target, Action<BinaryWriter> write)
    {
        var entity = behaviour.Entity;
        if (!Connected || !entity.IsSpawned) return;
        if (entity.IsMine) SendDirty(entity); // an action observes the owner's newest pose/place on the same ordered stream.
        int index = Array.IndexOf(entity.Behaviours, behaviour);
        byte[] blob = Capture(w => write?.Invoke(w));
        if (index >= 0 && blob != null) transport.Send(SyncProtocol.Event(entity.Id, (byte)index, id, target, blob));
    }

    // Called by NetEntity.OnDestroy: my own entity despawns everywhere.
    internal void Forget(NetEntity entity)
    {
        if (!entity.IsSpawned || !entities.TryGetValue(entity.Id, out var known) || known != entity) return;
        entities.Remove(entity.Id);
        if (!entity.RemovedByServer && entity.IsMine) transport.Send(SyncProtocol.Despawn(entity.Id));
    }

    void Update()
    {
        if (transport == null) return;
        while (transport != null && transport.TryReceive(out byte[] frame)) Handle(frame);
        if (transport != null && !transport.IsOpen) { Closed("disconnected"); return; }
        if (Connected && Time.unscaledTime >= nextSendAt) { nextSendAt = Time.unscaledTime + SendInterval; SendDirty(); }
        if (Connected && Time.unscaledTime >= nextPingAt)
        {
            nextPingAt = Time.unscaledTime + PingInterval;
            transport.Send(SyncProtocol.Ping((uint)(Time.realtimeSinceStartupAsDouble * 1000)));
        }
    }

    void SendDirty()
    {
        foreach (var entity in entities.Values) if (entity && entity.IsMine) SendDirty(entity);
    }

    void SendDirty(NetEntity entity)
    {
        var behaviours = entity.Behaviours;
        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (!b || !b.Dirty) continue;
            b.Dirty = false;
            byte[] blob = Capture(b.Write);
            if (blob != null) transport.Send(SyncProtocol.State(entity.Id, (byte)i, blob));
        }
    }

    byte[] Capture(Action<BinaryWriter> write)
    {
        blobStream.SetLength(0);
        write(blobWriter);
        blobWriter.Flush();
        if (blobStream.Length <= SyncProtocol.MaxBlob) return blobStream.ToArray();
        Debug.LogError("[Sync] state or event over " + SyncProtocol.MaxBlob + " bytes, not sent");
        return null;
    }

    void Handle(byte[] frame)
    {
        try
        {
            using var r = SyncProtocol.Reader(frame);
            switch ((ToClient)r.ReadByte())
            {
                case ToClient.Welcome:
                    MyConnection = r.ReadUInt32(); MaxPlayers = r.ReadUInt16();
                    string serverName = SyncProtocol.ReadStr(r).Replace("<", "").Replace(">", "");
                    ServerName = serverName.Length > ServerSettings.NameMax ? serverName.Substring(0, ServerSettings.NameMax) : serverName;
                    Connected = true;
                    ConnectionChanged?.Invoke(true);
                    break;
                case ToClient.Reject: LastError = ((RejectReason)r.ReadByte()).ToString(); break; // the server closes right after
                case ToClient.Pong: RoundTripMs = (int)((uint)(Time.realtimeSinceStartupAsDouble * 1000) - r.ReadUInt32()); break;
                case ToClient.Character: OnCharacter(r); break;
                case ToClient.CharacterResult:
                    var result = (CharacterResult)r.ReadByte();
                    if (result == CharacterResult.Ok)
                    {
                        HasCharacter = true; CharacterName = pendingName; CharacterGender = pendingGender == "female" ? "female" : "male";
                        CharacterContent = Array.Empty<byte>();
                    }
                    LastCharacterResult = result;
                    break;
                case ToClient.Spawn: OnSpawn(r); break;
                case ToClient.State: OnState(r); break;
                // Also my own: the server refused its Spawn (12.8). Removed like a remote one, no Despawn goes back.
                case ToClient.Despawn: if (entities.TryGetValue(r.ReadUInt64(), out var gone)) RemoveRemote(gone); break;
                case ToClient.Event: OnEvent(r); break;
                case ToClient.Owner:
                    if (!entities.TryGetValue(r.ReadUInt64(), out var owned)) break;
                    if (owned) owned.SetOwner(r.ReadUInt32());
                    // Released on the way to another map: no longer mine, and not on my map.
                    if (owned && !owned.IsMine && owned.Map != Map) RemoveRemote(owned);
                    break;
                case ToClient.Peer:
                    bool joined = r.ReadByte() == 1;
                    uint number = r.ReadUInt32();
                    string account = SyncProtocol.ReadStr(r), name = SyncProtocol.ReadStr(r);
                    PeerChanged?.Invoke(joined, number, account, name);
                    break;
            }
        }
        catch (Exception e) { Debug.LogWarning("[Sync] bad message from the server: " + e.Message); }
    }

    void OnCharacter(BinaryReader r)
    {
        bool has = r.ReadByte() == 1;
        string name = SyncProtocol.ReadStr(r), gender = SyncProtocol.ReadStr(r);
        uint characterMinutes = r.ReadUInt32(), worldMinutes = r.ReadUInt32();
        byte[] content = SyncProtocol.ReadBlob32(r);
        HasCharacter = has;
        CharacterName = has ? name.Replace("<", "").Replace(">", "") : ""; CharacterGender = gender == "female" ? "female" : "male";
        CharacterMinutes = characterMinutes; WorldMinutes = worldMinutes;
        CharacterContent = content;
        CharacterKnown = true;
    }

    void OnSpawn(BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        uint owner = r.ReadUInt32();
        string key = SyncProtocol.ReadStr(r), map = SyncProtocol.ReadStr(r);
        var flags = (EntityFlags)r.ReadByte();
        var blobs = new byte[r.ReadByte()][];
        for (int i = 0; i < blobs.Length; i++) blobs[i] = SyncProtocol.ReadServerBlob(r);
        if (entities.ContainsKey(id)) return;
        var prefab = PrefabFactory?.Invoke(key);
        if (!prefab) { Debug.LogWarning("[Sync] no prefab for key '" + key + "'"); return; }
        var go = Instantiate(prefab);
        DontDestroyOnLoad(go); // the server despawns it on map change; scene unload must not destroy it first
        var entity = go.GetComponent<NetEntity>();
        if (!entity) entity = go.AddComponent<NetEntity>();
        entity.Init(id, owner, key, flags, map);
        entities[id] = entity;
        for (int i = 0; i < blobs.Length && i < entity.Behaviours.Length; i++) Apply(entity.Behaviours[i], blobs[i]);
        go.SetActive(true); // QA templates are inactive
    }

    void OnState(BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        byte component = r.ReadByte();
        byte[] blob = SyncProtocol.ReadServerBlob(r);
        if (entities.TryGetValue(id, out var entity) && entity && !entity.IsMine && component < entity.Behaviours.Length)
            Apply(entity.Behaviours[component], blob);
    }

    void OnEvent(BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        byte component = r.ReadByte(), eventId = r.ReadByte();
        uint sender = r.ReadUInt32();
        byte[] blob = SyncProtocol.ReadServerBlob(r);
        if (!entities.TryGetValue(id, out var entity) || !entity || component >= entity.Behaviours.Length) return;
        try
        {
            using var reader = SyncProtocol.Reader(blob);
            entity.Behaviours[component].OnEvent(eventId, sender, reader);
        }
        catch (Exception e) { Debug.LogWarning("[Sync] event " + eventId + " on " + entity.name + " failed: " + e.Message); }
    }

    static void Apply(NetBehaviour behaviour, byte[] blob)
    {
        try
        {
            using var reader = SyncProtocol.Reader(blob);
            behaviour.Read(reader);
        }
        catch (Exception e) { Debug.LogWarning("[Sync] state of " + behaviour.GetType().Name + " unreadable: " + e.Message); }
    }

    void RemoveRemote(NetEntity entity)
    {
        entities.Remove(entity.Id);
        if (!entity) return;
        entity.RemovedByServer = true;
        Destroy(entity.gameObject);
    }

    // Remote copies go away; my own objects stay in the scene but are no longer shared.
    void Closed(string error)
    {
        uint me = MyConnection;
        bool was = Connected;
        transport = null;
        Connected = false; Connecting = false; MyConnection = 0;
        if (string.IsNullOrEmpty(LastError)) LastError = error;
        foreach (var e in entities.Values)
        {
            if (!e) continue;
            e.RemovedByServer = true;
            if (e.Owner != me) Destroy(e.gameObject);
        }
        entities.Clear();
        if (was) ConnectionChanged?.Invoke(false);
    }
}
