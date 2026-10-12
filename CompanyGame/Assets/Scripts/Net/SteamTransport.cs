#if STEAMWORKS_NET
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Steamworks;
using UnityEngine;

/// <summary>
/// NetSession's connection in Steam mode. A Steam lobby is the room; the lobby owner's PC runs the <see cref="HostRoom"/>
/// (through <see cref="RoomNode"/>) and everybody talks to it with SteamNetworkingMessages (P2P over Steam's relay).
/// This class only translates: create/list/join become lobby calls, everything else goes to the RoomNode, and the node's
/// output comes back as the server's messages. Frames are chunked below Steam's 512KB message limit.
/// Main thread only (all Steam calls and callbacks run from SteamPump / NetSession.Update).
/// </summary>
sealed class SteamTransport : INetTransport, IRoomLink
{
    const int Channel = 0, MaxRecv = 32, ChunkBytes = 256 * 1024 - 1, MaxAssembledBytes = 4 * 1024 * 1024;
    const byte Whole = 0, More = 1, Last = 2;
    const float PollSeconds = 0.5f;

    readonly Queue<NetMsg> inbox = new Queue<NetMsg>();
    readonly Dictionary<ulong, MemoryStream> partial = new Dictionary<ulong, MemoryStream>();
    readonly HashSet<ulong> sessions = new HashSet<ulong>();
    readonly IntPtr[] recv = new IntPtr[MaxRecv];
    readonly RoomNode node;
    readonly string myId, myName;
    readonly ulong myNum;
    readonly CallResult<LobbyCreated_t> created = CallResult<LobbyCreated_t>.Create();
    readonly CallResult<LobbyMatchList_t> found = CallResult<LobbyMatchList_t>.Create();
    readonly CallResult<LobbyEnter_t> entered = CallResult<LobbyEnter_t>.Create();
    readonly Callback<SteamNetworkingMessagesSessionRequest_t> sessionRequest;

    CSteamID lobby;
    bool open = true, creating, listing, joinedOk;
    string createName = "", wantCode, queuedCode;
    int createMax, findTries;
    float nextPoll, retryAt;

    public SteamTransport()
    {
        myId = SteamBackend.SteamIdText; myNum = ulong.Parse(myId);
        myName = Clean(SteamBackend.PersonaName);
        node = new RoomNode(myId, myName, this, Deliver);
        node.HostMoved += OnHostMoved;
        sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
        inbox.Enqueue(new NetMsg { t = "hello", id = myId, name = myName });
    }

    public bool IsOpen => open && SteamBackend.Initialized;
    public ulong LobbyId => lobby.m_SteamID;

    static string Clean(string s) => (string.IsNullOrWhiteSpace(s) ? "Steam" : s).Replace("<", "").Replace(">", "").Trim();

    // ---- INetTransport ----

    public void Send(NetMsg m)
    {
        switch (m.t)
        {
            case "create": StartCreate(m.room, m.max); break;
            case "list": RequestLobbies(null); break;
            case "join": StartJoin(m.rid); break;
            case "leave": node.FromLocal(m); LeaveLobby(); break;
            default: node.FromLocal(m); break;
        }
    }

    public bool TryDequeue(out NetMsg m)
    {
        if (inbox.Count > 0) { m = inbox.Dequeue(); return true; }
        m = null; return false;
    }

    public bool InviteFriends()
    {
        if (!lobby.IsValid()) return false;
        SteamFriends.ActivateGameOverlayInviteDialog(lobby);
        return true;
    }

    public void Close()
    {
        if (!open) return;
        if (node.InRoom) node.FromLocal(new NetMsg { t = "leave" });
        LeaveLobby();
        open = false;
        sessionRequest.Dispose();
        created.Dispose(); found.Dispose(); entered.Dispose();
    }

    public void Update()
    {
        if (!open || !SteamBackend.Initialized) return;
        Receive();
        node.Tick();
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + PollSeconds;
        PollLobby();
        if (queuedCode != null && !listing) { string c = queuedCode; queuedCode = null; StartJoin(c); }
        if (wantCode != null && !listing && retryAt > 0 && Time.unscaledTime >= retryAt) { retryAt = 0; RequestLobbies(wantCode); }
    }

    void Deliver(NetMsg m)
    {
        if (m.t == "joined") joinedOk = true;
        inbox.Enqueue(m);
        if (m.t == "err" && !joinedOk && node.Mirror != null) { node.Reset(); LeaveLobby(); } // the host refused me (room_full ...)
    }

    void Err(string code) { inbox.Enqueue(new NetMsg { t = "err", code = code }); }

    // ---- lobby: create ----

    void StartCreate(string name, int max)
    {
        if (node.InRoom || lobby.IsValid()) { Err("already_in_room"); return; }
        string bad = HostRoom.ValidateCreate(name, max);
        if (bad != null) { Err(bad); return; }
        if (creating) return;
        creating = true; createName = name; createMax = max;
        created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, max), OnCreated);
    }

    void OnCreated(LobbyCreated_t r, bool ioFailure)
    {
        creating = false;
        if (!open) { if (r.m_ulSteamIDLobby != 0) SteamMatchmaking.LeaveLobby(new CSteamID(r.m_ulSteamIDLobby)); return; }
        if (ioFailure || r.m_eResult != EResult.k_EResultOK) { Err("steam_lobby_failed"); return; }
        lobby = new CSteamID(r.m_ulSteamIDLobby);
        string code = NewCode();
        SteamMatchmaking.SetLobbyData(lobby, "game", SteamBackend.GameKey);
        SteamMatchmaking.SetLobbyData(lobby, "code", code);
        SteamMatchmaking.SetLobbyData(lobby, "name", createName);
        SteamMatchmaking.SetLobbyData(lobby, "ver", SteamBackend.VersionTag);
        SteamMatchmaking.SetLobbyData(lobby, "hostName", myName);
        joinedOk = true;
        node.StartHosting(code, createName, createMax);
    }

    static string NewCode()
    {
        var bytes = new byte[RoomCode.Length];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        var sb = new StringBuilder(RoomCode.Length);
        foreach (byte b in bytes) sb.Append(RoomCode.Alphabet[b & 31]);
        return sb.ToString();
    }

    // ---- lobby: list and join ----

    // code == null: fill the room list; otherwise look for that code and join the first match.
    void RequestLobbies(string code)
    {
        if (listing) { if (code != null) queuedCode = code; return; }
        listing = true;
        SteamMatchmaking.AddRequestLobbyListStringFilter("game", SteamBackend.GameKey, ELobbyComparison.k_ELobbyComparisonEqual);
        if (code != null) SteamMatchmaking.AddRequestLobbyListStringFilter("code", code, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
        found.Set(SteamMatchmaking.RequestLobbyList(), (r, io) => OnFound(code, r, io));
    }

    void OnFound(string code, LobbyMatchList_t r, bool ioFailure)
    {
        listing = false;
        if (!open) return;
        int n = ioFailure ? 0 : (int)r.m_nLobbiesMatching;
        if (code == null) { DeliverRooms(n); return; }
        if (n == 0)
        {   // a lobby created a moment ago is not searchable for a second or two: retry a few times
            if (++findTries < 4) { retryAt = Time.unscaledTime + 1f; return; }
            wantCode = null; Err(ioFailure ? "steam_lobby_failed" : "room_not_found"); return;
        }
        wantCode = null;
        entered.Set(SteamMatchmaking.JoinLobby(SteamMatchmaking.GetLobbyByIndex(0)), OnEntered);
    }

    void DeliverRooms(int n)
    {
        var rooms = new List<RoomInfo>();
        for (int i = 0; i < n; i++)
        {
            var id = SteamMatchmaking.GetLobbyByIndex(i);
            if (id == lobby) continue;
            rooms.Add(new RoomInfo
            {
                rid = SteamMatchmaking.GetLobbyData(id, "code"), room = SteamMatchmaking.GetLobbyData(id, "name"),
                host = SteamMatchmaking.GetLobbyOwner(id).m_SteamID.ToString(), hostName = SteamMatchmaking.GetLobbyData(id, "hostName"),
                count = SteamMatchmaking.GetNumLobbyMembers(id), max = SteamMatchmaking.GetLobbyMemberLimit(id)
            });
        }
        inbox.Enqueue(new NetMsg { t = "rooms", rooms = rooms.ToArray() });
    }

    // rid: a room code, or "#<lobby id>" (friend invite / launch argument).
    void StartJoin(string rid)
    {
        if (node.InRoom || lobby.IsValid()) { Err("already_in_room"); return; }
        if (!string.IsNullOrEmpty(rid) && rid[0] == '#')
        {
            if (!ulong.TryParse(rid.Substring(1), out ulong id)) { Err("room_not_found"); return; }
            entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)), OnEntered);
            return;
        }
        string code = RoomCode.Normalize(rid);
        if (code.Length != RoomCode.Length) { Err("room_not_found"); return; }
        if (listing) { queuedCode = code; return; }
        findTries = 0; wantCode = code; retryAt = 0;
        RequestLobbies(code);
    }

    void OnEntered(LobbyEnter_t r, bool ioFailure)
    {
        if (!open) { if (r.m_ulSteamIDLobby != 0) SteamMatchmaking.LeaveLobby(new CSteamID(r.m_ulSteamIDLobby)); return; }
        var id = new CSteamID(r.m_ulSteamIDLobby);
        if (ioFailure || r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) { Err(EnterError(r.m_EChatRoomEnterResponse, ioFailure)); return; }
        string problem = Check(id);
        if (problem != null) { SteamMatchmaking.LeaveLobby(id); Err(problem); return; }
        lobby = id; joinedOk = false;
        var owner = SteamMatchmaking.GetLobbyOwner(id);
        node.JoinAsMember(SteamMatchmaking.GetLobbyData(id, "code"), owner.m_SteamID.ToString(),
            SteamMatchmaking.GetLobbyData(id, "name"), Mathf.Max(1, SteamMatchmaking.GetLobbyMemberLimit(id)));
    }

    static string Check(CSteamID id)
    {
        if (SteamMatchmaking.GetLobbyData(id, "game") != SteamBackend.GameKey) return "room_not_found";
        return SteamMatchmaking.GetLobbyData(id, "ver") == SteamBackend.VersionTag ? null : "version_mismatch";
    }

    static string EnterError(uint response, bool io)
    {
        if (io) return "steam_lobby_failed";
        switch ((EChatRoomEnterResponse)response)
        {
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull: return "room_full";
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist: return "room_not_found";
            default: return "steam_lobby_failed";
        }
    }

    // ---- lobby: leave and watch ----

    void LeaveLobby()
    {
        if (lobby.IsValid()) SteamMatchmaking.LeaveLobby(lobby);
        lobby = default; joinedOk = false; wantCode = null;
        foreach (ulong s in sessions) { var identity = Identity(s); SteamNetworkingMessages.CloseSessionWithUser(ref identity); }
        sessions.Clear(); partial.Clear();
        node.Reset();
    }

    void OnHostMoved(string newHost)
    {
        if (lobby.IsValid() && ulong.TryParse(newHost, out ulong id)) SteamMatchmaking.SetLobbyOwner(lobby, new CSteamID(id));
    }

    bool InLobby(string id)
    {
        if (!ulong.TryParse(id, out ulong num)) return false;
        return InLobby(num);
    }

    bool InLobby(ulong num)
    {
        int n = SteamMatchmaking.GetNumLobbyMembers(lobby);
        for (int i = 0; i < n; i++) if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID == num) return true;
        return false;
    }

    // Who is in the lobby is Steam's truth: a vanished member leaves the room, a vanished host migrates it.
    void PollLobby()
    {
        if (!lobby.IsValid()) return;
        if (node.Room != null)
        {
            foreach (var p in node.Room.Members.ToArray()) if (p.id != myId && !InLobby(p.id)) node.PeerLost(p.id);
        }
        else if (node.Mirror != null && joinedOk && !InLobby(node.HostId))
        {
            string owner = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID.ToString();
            if (owner != node.HostId) node.HostLost(owner); // owner still the old host: Steam has not moved it yet, ask again next poll
        }
    }

    // ---- P2P ----

    static SteamNetworkingIdentity Identity(ulong steamId)
    {
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID64(steamId);
        return identity;
    }

    void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t r)
    {
        var identity = r.m_identityRemote;
        ulong from = identity.GetSteamID64();
        if (!lobby.IsValid() && !creating) return; // not in any room: nobody has business with us
        if (SteamNetworkingMessages.AcceptSessionWithUser(ref identity)) sessions.Add(from);
    }

    public void Send(string peerId, NetMsg m)
    {
        if (!ulong.TryParse(peerId, out ulong to) || to == myNum) return;
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(m));
        bool reliable = m.t != "pos";
        sessions.Add(to);
        if (bytes.Length <= ChunkBytes) { SendFrame(to, Whole, bytes, 0, bytes.Length, reliable); return; }
        for (int at = 0; at < bytes.Length; at += ChunkBytes)
        {
            int len = Math.Min(ChunkBytes, bytes.Length - at);
            SendFrame(to, at + len >= bytes.Length ? Last : More, bytes, at, len, true);
        }
    }

    static void SendFrame(ulong to, byte kind, byte[] bytes, int offset, int length, bool reliable)
    {
        IntPtr buffer = Marshal.AllocHGlobal(length + 1);
        try
        {
            Marshal.WriteByte(buffer, kind);
            Marshal.Copy(bytes, offset, buffer + 1, length);
            var identity = Identity(to);
            int flags = (reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable)
                | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
            var result = SteamNetworkingMessages.SendMessageToUser(ref identity, buffer, (uint)(length + 1), flags, Channel);
            if (result != EResult.k_EResultOK && result != EResult.k_EResultNoConnection) Debug.LogWarning("[SteamTransport] send failed: " + result);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    void Receive()
    {
        int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, recv, MaxRecv);
        for (int i = 0; i < n; i++)
        {
            var msg = SteamNetworkingMessage_t.FromIntPtr(recv[i]);
            ulong from = msg.m_identityPeer.GetSteamID64();
            var bytes = new byte[Math.Max(0, msg.m_cbSize)];
            if (bytes.Length > 0) Marshal.Copy(msg.m_pData, bytes, 0, bytes.Length);
            SteamNetworkingMessage_t.Release(recv[i]);
            OnFrame(from, bytes);
        }
    }

    void OnFrame(ulong from, byte[] frame)
    {
        if (frame.Length < 1) return;
        byte kind = frame[0];
        if (kind == Whole) { OnJson(from, Encoding.UTF8.GetString(frame, 1, frame.Length - 1)); return; }
        if (!partial.TryGetValue(from, out var stream)) partial[from] = stream = new MemoryStream();
        stream.Write(frame, 1, frame.Length - 1);
        if (stream.Length > MaxAssembledBytes) { partial.Remove(from); return; }
        if (kind != Last) return;
        partial.Remove(from);
        OnJson(from, Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length));
    }

    void OnJson(ulong from, string json)
    {
        NetMsg m;
        try { m = JsonUtility.FromJson<NetMsg>(json); }
        catch (Exception e) { Debug.LogWarning("[SteamTransport] bad message: " + e.Message); return; }
        if (m == null || string.IsNullOrEmpty(m.t)) return;
        // Only lobby members may talk to us; a `join` can beat the lobby's member list by a moment, the room validates it anyway.
        if (!lobby.IsValid() || (!InLobby(from) && !(m.t == "join" && node.IsHost))) return;
        node.FromRemote(from.ToString(), m);
    }
}
#endif
