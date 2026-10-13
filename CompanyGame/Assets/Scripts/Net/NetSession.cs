using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Room client singleton. Knows rooms, peers and the host, and relays opaque JSON (snapshot, world, op).
/// It knows nothing about the clock or the economy; other systems subscribe to the events below.
/// </summary>
public sealed class NetSession : MonoBehaviour
{
    const float SnapshotInterval = 10f;
    const int MaxMessagesPerFrame = 64;

    public static NetSession Instance { get; private set; }

    public bool Connecting { get; private set; }
    /// <summary>True while the connection is the Steam one (the host PC plays the server).</summary>
    public bool IsSteam { get; private set; }
    public ulong SteamLobbyId => client?.LobbyId ?? 0;
    public bool Connected => client != null && client.IsOpen;
    public bool InRoom => !string.IsNullOrEmpty(RoomId);
    public bool IsHost => InRoom && !string.IsNullOrEmpty(MyId) && HostId == MyId;
    public string MyId { get; private set; } = "";
    public string MyName { get; private set; } = "";
    public string RoomId { get; private set; } = "";
    public string HostId { get; private set; } = "";
    public string LastError { get; private set; } = "";
    public IReadOnlyList<PeerInfo> Peers => peers;
    public RoomInfo[] Rooms { get; private set; } = new RoomInfo[0];
    /// <summary>True once this room's world snapshot arrived (with `joined`, or the host's first `state` about 1s after it created the room).</summary>
    public bool HasRoomSnapshot { get; private set; }
    /// <summary>Highest op seq already applied; set by the owner after applying a snapshot's opSeq.</summary>
    public long LastOpSeq { get; set; }

    /// <summary>True while HostChanged(true) comes from leaving/losing the connection (not from a server `host` message).</summary>
    public bool HostFromLeave { get; private set; }
    /// <summary>Raised when a room is joined or created (before the role is set) and when it is left. Per-room state (op numbering) resets here.</summary>
    public event Action RoomReset;

    public event Action StateChanged;
    /// <summary>Argument = whether this machine owns the simulation now. Fires after GameSession.Begin.</summary>
    public event Action<bool> HostChanged;
    public Func<string> SnapshotProvider;
    /// <summary>joined.snap / state / host.snap (never empty). Fires before HostChanged.</summary>
    public event Action<string> SnapshotReceived;
    public event Action<string, string> WorldReceived;
    /// <summary>(seq, fromId, kind, json) for every op including my own echo.</summary>
    public event Action<long, string, string, string> OpReceived;
    /// <summary>Every server message after internal handling (pos, msg, peer, ...).</summary>
    public event Action<NetMsg> Received;
    /// <summary>(fromId, data) for relayed `msg` messages.</summary>
    public event Action<string, string> MessageReceived;
    /// <summary>Free text other systems (handover flow) set; the pause menu shows it.</summary>
    public string StatusText { get; set; } = "";

    readonly List<PeerInfo> peers = new List<PeerInfo>();
    INetTransport client;
    float nextSnapshotAt;
    string lastSnapshotSent;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoCreate()
    {
        if (Instance) return;
        var go = new GameObject("NetSession");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<NetSession>();
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; client?.Close(); }
    void OnApplicationQuit() { client?.Close(); }

    // serverUrl: http(s):// or ws(s):// base of the server. token: JWT from PlayerData.
    public async void Connect(string serverUrl, string token)
    {
        if (Connecting || Connected) return;
        if (ServerBudget.Blocked) { LastError = ServerBudget.Code; StateChanged?.Invoke(); return; } // no new socket until the next UTC day
        Connecting = true; LastError = ""; IsSteam = false;
        StateChanged?.Invoke();
        var next = new NetClient();
        try
        {
            await next.Connect(ToSocketUri(serverUrl, token));
            client = next;
        }
        catch (Exception e)
        {
            next.Close();
            LastError = e is UriFormatException ? "bad_url" : "connect_failed";
        }
        Connecting = false;
        StateChanged?.Invoke();
    }

    /// <summary>Steam mode: no server and no login. False with LastError set (steam_*) when Steam cannot be used.</summary>
    public bool ConnectSteam()
    {
        if (Connecting || Connected) return Connected;
        LastError = "";
        var next = SteamBackend.CreateTransport(out string error);
        if (next == null) { LastError = error; StateChanged?.Invoke(); return false; }
        client = next; IsSteam = true;
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>Steam mode: opens the overlay's friend invite dialog for this room.</summary>
    public bool InviteFriends() => InRoom && client != null && client.InviteFriends();

    public void Disconnect()
    {
        client?.Close();
        client = null; IsSteam = false;
        LeaveLocally();
        MyId = "";
        StateChanged?.Invoke();
    }

    /// <summary>Forget the last server error code (the title screen clears it before each create/join).</summary>
    public void ClearError() { LastError = ""; }

    public void RefreshRooms() => Send(NetMsg.Of("list"));
    public void CreateRoom(string name, int max) { var m = NetMsg.Of("create"); m.room = name; m.max = max; Send(m); }
    public void Join(string rid) { var m = NetMsg.Of("join"); m.rid = rid; Send(m); }

    /// <summary>Host only: sends a fresh snapshot right away (no 10s / unchanged check), then the transfer, on the same socket.</summary>
    public void TransferHost(string id)
    {
        if (IsHost) SendSnapshot(true);
        var m = NetMsg.Of("transfer"); m.to = id; Send(m);
    }

    /// <summary>Generic relay to one member ("" = everyone); the server accepts it from the host only.</summary>
    public void SendMessageTo(string id, string data)
    {
        if (!InRoom) return;
        var m = NetMsg.Of("msg"); m.to = id ?? ""; m.data = data; Send(m);
    }

    public void Leave()
    {
        if (!InRoom) return;
        Send(NetMsg.Of("leave"));
        LeaveLocally();
        StateChanged?.Invoke();
    }

    /// <summary>name = character name, female = look, vehicle = ridden itemId ("" on foot), attacks = swing counter.</summary>
    public void SendPos(string map, Vector3 pos, float ry, float speed, string name = "", bool female = false, string vehicle = "", int attacks = 0)
    {
        if (!InRoom) return;
        var m = NetMsg.Of("pos");
        m.map = map; m.x = pos.x; m.y = pos.y; m.z = pos.z; m.ry = ry; m.sp = speed;
        m.nm = name ?? ""; m.g = female ? 1 : 0; m.veh = vehicle ?? ""; m.act = attacks;
        Send(m);
    }

    /// <summary>Host only; others are ignored. Delivered to everyone else as WorldReceived(kind, json).</summary>
    public void SendWorld(string kind, string json)
    {
        if (!IsHost || string.IsNullOrEmpty(kind)) return;
        var m = NetMsg.Of("world"); m.ev = kind; m.data = json; Send(m);
    }

    /// <summary>Any room member. Applied by everyone (sender included) when the echo arrives as OpReceived.</summary>
    public bool SendOp(string kind, string json)
    {
        if (!InRoom || string.IsNullOrEmpty(kind)) return false;
        var m = NetMsg.Of("op"); m.ev = kind; m.data = json; return Send(m);
    }

    bool Send(NetMsg message)
    {
        if (!Connected) return false;
        client.Send(message);
        return true;
    }

    void Update()
    {
        if (client == null) return;
        client.Update();
        for (int i = 0; i < MaxMessagesPerFrame && client != null && client.TryDequeue(out var m); i++) Handle(m);
        if (IsHost) TickSnapshot();
    }

    void TickSnapshot()
    {
        if (SnapshotProvider == null || Time.unscaledTime < nextSnapshotAt) return;
        SendSnapshot(false);
    }

    void SendSnapshot(bool force)
    {
        nextSnapshotAt = Time.unscaledTime + SnapshotInterval;
        string snap = SnapshotProvider?.Invoke();
        if (string.IsNullOrEmpty(snap) || (!force && snap == lastSnapshotSent)) return;
        lastSnapshotSent = snap;
        var m = NetMsg.Of("state"); m.snap = snap; Send(m);
    }

    void Handle(NetMsg m)
    {
        switch (m.t)
        {
            case "hello": OnHello(m); break;
            case "rooms": Rooms = m.rooms ?? new RoomInfo[0]; break;
            case "joined": OnJoined(m); break;
            case "peer": OnPeer(m); break;
            case "host": OnHost(m); break;
            case "state": if (!string.IsNullOrEmpty(m.snap)) HasRoomSnapshot = true; Raise(SnapshotReceived, m.snap); break;
            case "world": if (!IsHost) WorldReceived?.Invoke(m.ev, m.data); break;
            case "op": ApplyOp(m.seq, m.from, m.ev, m.data); break;
            case "msg": MessageReceived?.Invoke(m.from, m.data); break;
            case "err": LastError = m.code ?? ""; break;
            case "closed": OnClosed(m); break;
        }
        Received?.Invoke(m);
        StateChanged?.Invoke();
    }

    // The server account is the identity: ownership, op actors and personal files all use it.
    void OnHello(NetMsg m)
    {
        MyId = m.id ?? ""; MyName = m.name ?? "";
        GameSession.SetAccount(MyId, MyName);
        RefreshRooms();
    }

    void OnJoined(NetMsg m)
    {
        RoomReset?.Invoke(); // before Begin(Client): the owner may still save its own world
        RoomId = m.rid ?? ""; HostId = m.host ?? ""; LastError = ""; HasRoomSnapshot = false;
        peers.Clear();
        if (m.players != null) peers.AddRange(m.players);
        LastOpSeq = 0; // the snapshot's owner sets it again after applying
        BecomeRole(m.snap);
        if (m.ops != null)
            foreach (var op in m.ops) ApplyOp(op.seq, op.from, op.ev, op.data);
    }

    void OnPeer(NetMsg m)
    {
        peers.RemoveAll(p => p.id == m.id);
        if (m.ev == "join") peers.Add(new PeerInfo { id = m.id, name = m.name });
    }

    void OnHost(NetMsg m)
    {
        HostId = m.host ?? "";
        BecomeRole(m.snap);
    }

    // Order is part of the contract: Begin, then SnapshotReceived, then HostChanged.
    void BecomeRole(string snap)
    {
        HostFromLeave = false;
        HasRoomSnapshot |= !string.IsNullOrEmpty(snap) || IsHost;
        GameSession.Begin(IsHost ? SessionMode.Host : SessionMode.Client);
        lastSnapshotSent = null;
        nextSnapshotAt = Time.unscaledTime + 1f;
        Raise(SnapshotReceived, snap);
        HostChanged?.Invoke(IsHost);
    }

    void ApplyOp(long seq, string from, string kind, string json)
    {
        if (seq <= LastOpSeq) return;
        LastOpSeq = seq;
        OpReceived?.Invoke(seq, from, kind, json);
    }

    void OnClosed(NetMsg m)
    {
        client = null; IsSteam = false;
        if (m.code == "4503") ServerBudget.Trip(0);
        LastError = m.code == "4503" ? ServerBudget.Code : m.code == "4401" ? "unauthorized" : m.code == "4409" ? "replaced" : m.code == "4429" ? "too_many_connections" : "disconnected";
        LeaveLocally();
        MyId = "";
    }

    // Authority returns to this machine: the visitor world it mirrored becomes its own.
    void LeaveLocally()
    {
        bool wasClient = GameSession.Mode == SessionMode.Client;
        bool wasInRoom = InRoom;
        RoomId = ""; HostId = ""; LastOpSeq = 0; lastSnapshotSent = null;
        peers.Clear();
        GameSession.ClearCharacter(); // the character belongs to the room's world
        GameSession.Begin(SessionMode.Host);
        HostFromLeave = true;
        if (wasInRoom) RoomReset?.Invoke();
        if (wasClient) HostChanged?.Invoke(true);
    }

    static void Raise(Action<string> handler, string value)
    {
        if (!string.IsNullOrEmpty(value)) handler?.Invoke(value);
    }

    static Uri ToSocketUri(string serverUrl, string token)
    {
        string url = (serverUrl ?? "").Trim().TrimEnd('/');
        if (url.StartsWith("https://")) url = "wss://" + url.Substring(8);
        else if (url.StartsWith("http://")) url = "ws://" + url.Substring(7);
        else if (!url.StartsWith("ws://") && !url.StartsWith("wss://")) url = "ws://" + url;
        if (!url.EndsWith("/ws")) url += "/ws";
        return new Uri(url + "?token=" + Uri.EscapeDataString(token ?? ""));
    }
}
