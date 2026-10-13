using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Client of the sync layer (Docs/Multiplayer_Redesign.md 12.1). Created at startup like NetSession, idle until Connect.
/// Says Hello, tells the server the current map (SyncMapBinder), makes and removes the remote entities, applies their
/// states and events, and sends the dirty states of my entities every 0.1s. The socket threads only move bytes;
/// everything here runs in Update on the main thread.
/// </summary>
public sealed class SyncClient : MonoBehaviour
{
    const float SendInterval = .1f;

    public static SyncClient Instance { get; private set; }

    /// <summary>Prefab key → prefab to instantiate for remote entities. QA replaces it to test without assets.</summary>
    public static Func<string, GameObject> PrefabFactory = DefaultFactory;

    public bool Connecting { get; private set; }
    /// <summary>True between Welcome and the disconnect.</summary>
    public bool Connected { get; private set; }
    public uint MyConnection { get; private set; }
    public int MaxPlayers { get; private set; }
    public string Map { get; private set; } = "";
    /// <summary>"connect_failed", "disconnected", or the Reject reason (Version, Full, Malformed, Rate).</summary>
    public string LastError { get; private set; } = "";

    /// <summary>(joined, connection number, account id, name). Sent for everyone already there right after Welcome.</summary>
    public event Action<bool, uint, string, string> PeerChanged;

    readonly Dictionary<ulong, NetEntity> entities = new Dictionary<ulong, NetEntity>();
    readonly List<NetEntity> scratch = new List<NetEntity>();
    readonly MemoryStream blobStream = new MemoryStream();
    BinaryWriter blobWriter;
    IClientTransport transport;
    uint serial;
    float nextSendAt;

    static GameObject DefaultFactory(string key) => Resources.Load<GameObject>(key);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; PrefabFactory = DefaultFactory; }

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

    /// <summary>Connects over TCP and says Hello with GameSession's account id and name.</summary>
    public async void Connect(string host, int port)
    {
        if (Connecting || transport != null) return;
        Connecting = true; LastError = "";
        var next = new TcpClientTransport();
        try
        {
            await next.Connect(host, port);
            transport = next;
            transport.Send(SyncProtocol.Hello(GameSession.LocalPlayerId, GameSession.LocalPlayerName, Map));
        }
        catch (Exception)
        {
            next.Close();
            LastError = "connect_failed";
        }
        Connecting = false;
    }

    public void Disconnect()
    {
        transport?.Close();
        Closed("");
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

    public NetEntity Find(ulong id) => entities.TryGetValue(id, out var e) && e ? e : null;

    internal void SendEvent(NetBehaviour behaviour, byte id, EventTarget target, Action<BinaryWriter> write)
    {
        var entity = behaviour.Entity;
        if (!Connected || !entity.IsSpawned) return;
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
    }

    void SendDirty()
    {
        foreach (var entity in entities.Values)
        {
            if (!entity || !entity.IsMine) continue;
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
                case ToClient.Welcome: MyConnection = r.ReadUInt32(); MaxPlayers = r.ReadUInt16(); Connected = true; break;
                case ToClient.Reject: LastError = ((RejectReason)r.ReadByte()).ToString(); break; // the server closes right after
                case ToClient.Spawn: OnSpawn(r); break;
                case ToClient.State: OnState(r); break;
                case ToClient.Despawn: if (entities.TryGetValue(r.ReadUInt64(), out var gone) && !gone.IsMine) RemoveRemote(gone); break;
                case ToClient.Event: OnEvent(r); break;
                case ToClient.Owner: if (entities.TryGetValue(r.ReadUInt64(), out var owned)) owned.SetOwner(r.ReadUInt32()); break;
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

    void OnSpawn(BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        uint owner = r.ReadUInt32();
        string key = SyncProtocol.ReadStr(r), map = SyncProtocol.ReadStr(r);
        var flags = (EntityFlags)r.ReadByte();
        var blobs = new byte[r.ReadByte()][];
        for (int i = 0; i < blobs.Length; i++) blobs[i] = SyncProtocol.ReadBlob(r);
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
        byte[] blob = SyncProtocol.ReadBlob(r);
        if (entities.TryGetValue(id, out var entity) && entity && !entity.IsMine && component < entity.Behaviours.Length)
            Apply(entity.Behaviours[component], blob);
    }

    void OnEvent(BinaryReader r)
    {
        ulong id = r.ReadUInt64();
        byte component = r.ReadByte(), eventId = r.ReadByte();
        uint sender = r.ReadUInt32();
        byte[] blob = SyncProtocol.ReadBlob(r);
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
    }
}
