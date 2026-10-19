using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using UnityEngine;

/// <summary>
/// Sync frames over EOS P2P (Docs/Multiplayer_Redesign.md 12.13): socket "companygame", reliable and ordered, frames cut
/// into packets by <see cref="PacketFraming"/>. Clients send on channel 0 and servers on channel 1, so the two sides never
/// read each other's packets in one process. A connection that EOS reports Interrupted and does not get back within
/// <see cref="InterruptedSeconds"/> counts as closed. Main thread only (EOS rule): the server's <see cref="EosServerTransport.Pump"/>
/// runs in the same Update as its Tick; the client receives inside TryReceive (SyncClient.Update).
/// </summary>
static class EosP2P
{
    public const string SocketName = "companygame";
    public const byte ToServer = 0, ToClient = 1;
    public const float InterruptedSeconds = 15f;

    public static SocketId Socket => new SocketId { SocketName = SocketName };

    /// <summary>Sends every packet of <paramref name="frame"/>. False when EOS refused one (the connection is dropped then).</summary>
    public static bool Send(ProductUserId peer, byte channel, byte[] frame)
    {
        if (EosAccount.P2P == null || frame.Length > PacketFraming.MaxFrame) return false;
        foreach (var packet in PacketFraming.Split(frame))
        {
            var options = new SendPacketOptions
            {
                LocalUserId = EosAccount.Puid, RemoteUserId = peer, SocketId = Socket, Channel = channel, Data = packet,
                AllowDelayedDelivery = true, Reliability = PacketReliability.ReliableOrdered,
            };
            if (EosAccount.P2P.SendPacket(ref options) != Result.Success) return false;
        }
        return true;
    }

    /// <summary>The next packet on <paramref name="channel"/> (any peer), or false when there is none.</summary>
    public static bool Receive(byte channel, out ProductUserId peer, out ArraySegment<byte> data)
    {
        peer = null; data = default;
        var p2p = EosAccount.P2P;
        if (p2p == null) return false;
        var sizeOptions = new GetNextReceivedPacketSizeOptions { LocalUserId = EosAccount.Puid, RequestedChannel = channel };
        if (p2p.GetNextReceivedPacketSize(ref sizeOptions, out uint size) != Result.Success) return false;
        var options = new ReceivePacketOptions { LocalUserId = EosAccount.Puid, MaxDataSizeBytes = size, RequestedChannel = channel };
        var socket = new SocketId();
        var buffer = new byte[size];
        if (p2p.ReceivePacket(ref options, ref peer, ref socket, out _, new ArraySegment<byte>(buffer), out uint length) != Result.Success) return false;
        data = new ArraySegment<byte>(buffer, 0, (int)length);
        if (socket.SocketName != SocketName) peer = null; // another socket's (the 12.4 probe): read and dropped
        return true;
    }

    public static void RemoveNotifies(List<Action> removers)
    {
        foreach (var remove in removers) remove();
        removers.Clear();
    }
}

/// <summary>The server side: accepts every connection request on the socket and gives each peer a connection number.</summary>
public sealed class EosServerTransport : IServerTransport
{
    sealed class Peer
    {
        public int id;
        public ProductUserId user;
        public readonly PacketFraming.Joiner joiner = new PacketFraming.Joiner();
        public float interruptedAt = -1;
        public bool relayed;
    }

    readonly Queue<TransportEvent> inbox = new Queue<TransportEvent>();
    readonly Dictionary<string, Peer> byUser = new Dictionary<string, Peer>();
    readonly Dictionary<int, Peer> byId = new Dictionary<int, Peer>();
    readonly List<Action> notifies = new List<Action>();
    readonly List<byte[]> frames = new List<byte[]>();
    readonly List<Peer> scratch = new List<Peer>();
    int nextId;
    bool stopped;

    public EosServerTransport()
    {
        var p2p = EosAccount.P2P;
        var me = EosAccount.Puid;
        var socket = EosP2P.Socket;
        var request = new AddNotifyPeerConnectionRequestOptions { LocalUserId = me, SocketId = socket };
        ulong idRequest = p2p.AddNotifyPeerConnectionRequest(ref request, null, (ref OnIncomingConnectionRequestInfo info) =>
        {
            var accept = new AcceptConnectionOptions { LocalUserId = me, RemoteUserId = info.RemoteUserId, SocketId = EosP2P.Socket };
            if (stopped) return;
            EosAccount.KeepOpen(info.RemoteUserId);
            if (p2p.AcceptConnection(ref accept) != Result.Success) return;
            string key = info.RemoteUserId.ToString();
            if (byUser.TryGetValue(key, out var old)) Forget(old, true); // it came again before its old connection closed
            var peer = new Peer { id = ++nextId, user = info.RemoteUserId };
            byUser[key] = peer; byId[peer.id] = peer;
            inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, Connection = peer.id });
        });
        notifies.Add(() => p2p.RemoveNotifyPeerConnectionRequest(idRequest));
        var established = new AddNotifyPeerConnectionEstablishedOptions { LocalUserId = me, SocketId = socket };
        ulong idEstablished = p2p.AddNotifyPeerConnectionEstablished(ref established, null, (ref OnPeerConnectionEstablishedInfo info) =>
        {
            if (!byUser.TryGetValue(info.RemoteUserId.ToString(), out var peer)) return;
            peer.interruptedAt = -1;
            peer.relayed = info.NetworkType == NetworkConnectionType.RelayedConnection;
        });
        notifies.Add(() => p2p.RemoveNotifyPeerConnectionEstablished(idEstablished));
        var interrupted = new AddNotifyPeerConnectionInterruptedOptions { LocalUserId = me, SocketId = socket };
        ulong idInterrupted = p2p.AddNotifyPeerConnectionInterrupted(ref interrupted, null, (ref OnPeerConnectionInterruptedInfo info) =>
        {
            if (byUser.TryGetValue(info.RemoteUserId.ToString(), out var peer)) peer.interruptedAt = Time.realtimeSinceStartup;
        });
        notifies.Add(() => p2p.RemoveNotifyPeerConnectionInterrupted(idInterrupted));
        var closed = new AddNotifyPeerConnectionClosedOptions { LocalUserId = me, SocketId = socket };
        ulong idClosed = p2p.AddNotifyPeerConnectionClosed(ref closed, null, (ref OnRemoteConnectionClosedInfo info) =>
        {
            if (byUser.TryGetValue(info.RemoteUserId.ToString(), out var peer)) Forget(peer, true);
        });
        notifies.Add(() => p2p.RemoveNotifyPeerConnectionClosed(idClosed));
    }

    /// <summary>The PUID EOS says connection <paramref name="connection"/> is (null when gone): EOS checked it, the Hello's account must match.</summary>
    public string PeerOf(int connection) => byId.TryGetValue(connection, out var peer) ? peer.user.ToString() : null;

    /// <summary>True when connection <paramref name="connection"/> goes through an EOS relay.</summary>
    public bool Relayed(int connection) => byId.TryGetValue(connection, out var peer) && peer.relayed;

    /// <summary>Reads the packets EOS has for the server channel and ends interrupted connections that did not come back. Once a frame.</summary>
    public void Pump()
    {
        if (stopped) return;
        while (EosP2P.Receive(EosP2P.ToServer, out var user, out var data))
        {
            if (user == null || !byUser.TryGetValue(user.ToString(), out var peer) || peer.joiner.Broken) continue; // not accepted (late packets of a closed one)
            frames.Clear();
            bool ok = peer.joiner.Add(data, frames);
            foreach (byte[] frame in frames) inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Data, Connection = peer.id, Data = frame });
            // An unreadable length: an empty frame makes the server reject it, like TcpLink's oversized length.
            if (!ok) inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Data, Connection = peer.id, Data = Array.Empty<byte>() });
        }
        float now = Time.realtimeSinceStartup;
        scratch.Clear();
        foreach (var peer in byId.Values) if (peer.interruptedAt >= 0 && now - peer.interruptedAt > EosP2P.InterruptedSeconds) scratch.Add(peer);
        foreach (var peer in scratch) { Forget(peer, true); EosAccount.CloseLater(peer.user, EosP2P.Socket); }
    }

    public bool TryReceive(out TransportEvent e) => inbox.TryDequeue(out e);

    public void Send(int connection, byte[] frame)
    {
        if (!byId.TryGetValue(connection, out var peer)) return;
        if (EosP2P.Send(peer.user, EosP2P.ToClient, frame)) return;
        Debug.LogWarning("[EosServer] send to connection " + connection + " failed; dropping it");
        Forget(peer, true);
        EosAccount.CloseLater(peer.user, EosP2P.Socket);
    }

    public void Disconnect(int connection)
    {
        if (!byId.TryGetValue(connection, out var peer)) return;
        Forget(peer, false);
        EosAccount.CloseLater(peer.user, EosP2P.Socket); // the Reject queued before this goes out first
    }

    public void Stop()
    {
        if (stopped) return;
        stopped = true;
        foreach (var peer in byId.Values) EosAccount.CloseLater(peer.user, EosP2P.Socket);
        byId.Clear(); byUser.Clear();
        EosP2P.RemoveNotifies(notifies);
    }

    void Forget(Peer peer, bool tell)
    {
        if (!byId.Remove(peer.id)) return;
        byUser.Remove(peer.user.ToString());
        if (tell) inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Disconnected, Connection = peer.id });
    }
}

/// <summary>The client side: one connection to a server's PUID. EOS sets it up with the first packet (the Hello).</summary>
public sealed class EosClientTransport : IClientTransport
{
    readonly ProductUserId server;
    readonly PacketFraming.Joiner joiner = new PacketFraming.Joiner();
    readonly Queue<byte[]> inbox = new Queue<byte[]>();
    readonly List<byte[]> frames = new List<byte[]>();
    readonly List<Action> notifies = new List<Action>();
    bool open = true, closedByPeer;
    float interruptedAt = -1;

    /// <summary>True once EOS said the connection goes through a relay (false: direct, or not established yet).</summary>
    public bool Relayed { get; private set; }

    /// <summary>Throws when EOS is not logged in or the PUID is not one.</summary>
    public EosClientTransport(string serverPuid)
    {
        if (EosAccount.State != EosAccount.Status.Ready) throw new InvalidOperationException("EOS is not logged in");
        server = ProductUserId.FromString(serverPuid ?? "");
        if (server == null || !server.IsValid()) throw new ArgumentException("bad server PUID");
        EosAccount.KeepOpen(server);
        var p2p = EosAccount.P2P;
        var me = EosAccount.Puid;
        var socket = EosP2P.Socket;
        var established = new AddNotifyPeerConnectionEstablishedOptions { LocalUserId = me, SocketId = socket };
        ulong idEstablished = p2p.AddNotifyPeerConnectionEstablished(ref established, null, (ref OnPeerConnectionEstablishedInfo info) =>
        {
            if (info.RemoteUserId.ToString() != serverPuid) return;
            interruptedAt = -1;
            Relayed = info.NetworkType == NetworkConnectionType.RelayedConnection;
        });
        notifies.Add(() => p2p.RemoveNotifyPeerConnectionEstablished(idEstablished));
        var interrupted = new AddNotifyPeerConnectionInterruptedOptions { LocalUserId = me, SocketId = socket };
        ulong idInterrupted = p2p.AddNotifyPeerConnectionInterrupted(ref interrupted, null, (ref OnPeerConnectionInterruptedInfo info) =>
        {
            if (info.RemoteUserId.ToString() == serverPuid) interruptedAt = Time.realtimeSinceStartup;
        });
        notifies.Add(() => p2p.RemoveNotifyPeerConnectionInterrupted(idInterrupted));
        var closed = new AddNotifyPeerConnectionClosedOptions { LocalUserId = me, SocketId = socket };
        ulong idClosed = p2p.AddNotifyPeerConnectionClosed(ref closed, null, (ref OnRemoteConnectionClosedInfo info) =>
        {
            if (info.RemoteUserId.ToString() == serverPuid) closedByPeer = true; // also when it could not be set up at all
        });
        notifies.Add(() => p2p.RemoveNotifyPeerConnectionClosed(idClosed));
    }

    public bool IsOpen => open;

    public void Send(byte[] frame)
    {
        if (!open || closedByPeer) return;
        if (!EosP2P.Send(server, EosP2P.ToServer, frame)) { Debug.LogWarning("[EosClient] send failed; closing"); closedByPeer = true; }
    }

    public bool TryReceive(out byte[] frame)
    {
        if (inbox.Count == 0 && open) Pump();
        if (inbox.TryDequeue(out frame)) return true;
        if (open && (closedByPeer || joiner.Broken)) { open = false; EosP2P.RemoveNotifies(notifies); } // every frame before the close was read
        return false;
    }

    void Pump()
    {
        while (EosP2P.Receive(EosP2P.ToClient, out var user, out var data))
        {
            if (user == null || user.ToString() != server.ToString() || joiner.Broken) continue;
            frames.Clear();
            if (!joiner.Add(data, frames)) Debug.LogWarning("[EosClient] unreadable packet from the server; closing");
            foreach (byte[] f in frames) inbox.Enqueue(f);
        }
        if (interruptedAt >= 0 && Time.realtimeSinceStartup - interruptedAt > EosP2P.InterruptedSeconds) closedByPeer = true;
    }

    /// <summary>Closes a second from now, so the frames already sent (the last character save) go out first.</summary>
    public void Close()
    {
        if (!open) return;
        open = false;
        EosP2P.RemoveNotifies(notifies);
        EosAccount.CloseLater(server, EosP2P.Socket);
    }
}
