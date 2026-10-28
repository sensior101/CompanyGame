using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

public enum TransportEventKind : byte { Connected, Data, Disconnected }

public struct TransportEvent
{
    public TransportEventKind Kind;
    public int Connection;
    /// <summary>The frame (Data only). An empty frame means the peer sent something unreadable (an oversized length).</summary>
    public byte[] Data;
}

/// <summary>
/// The server side of a frame connection. Events are read on the thread that calls <see cref="TryReceive"/>;
/// transports only move bytes on their own threads. Sent frames are not copied, so callers never reuse them.
/// </summary>
public interface IServerTransport
{
    bool TryReceive(out TransportEvent e);
    void Send(int connection, byte[] frame);
    /// <summary>Closes after the frames already queued for it (a Reject) are sent. No Disconnected event is needed for it.</summary>
    void Disconnect(int connection);
    void Stop();
}

/// <summary>The client side of a frame connection. IsOpen turns false in TryReceive once every frame before the close was read.</summary>
public interface IClientTransport
{
    bool IsOpen { get; }
    void Send(byte[] frame);
    bool TryReceive(out byte[] frame);
    void Close();
}

/// <summary>In-memory transport for QA: clients and server in one process, no sockets.</summary>
public sealed class LoopbackTransport : IServerTransport
{
    readonly ConcurrentQueue<TransportEvent> inbox = new ConcurrentQueue<TransportEvent>();
    readonly ConcurrentDictionary<int, Client> clients = new ConcurrentDictionary<int, Client>();
    int nextId;

    public IClientTransport Connect()
    {
        var client = new Client(this, ++nextId);
        clients[client.id] = client;
        inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, Connection = client.id });
        return client;
    }

    public bool TryReceive(out TransportEvent e) => inbox.TryDequeue(out e);

    public void Send(int connection, byte[] frame)
    {
        if (clients.TryGetValue(connection, out var client)) client.inbox.Enqueue(frame);
    }

    public void Disconnect(int connection)
    {
        if (clients.TryRemove(connection, out var client)) client.inbox.Enqueue(null);
    }

    public void Stop()
    {
        foreach (int id in clients.Keys) Disconnect(id);
    }

    sealed class Client : IClientTransport
    {
        public readonly ConcurrentQueue<byte[]> inbox = new ConcurrentQueue<byte[]>();
        public readonly int id;
        readonly LoopbackTransport server;
        volatile bool open = true;

        public Client(LoopbackTransport server, int id) { this.server = server; this.id = id; }

        public bool IsOpen => open;

        public void Send(byte[] frame)
        {
            if (open) server.inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Data, Connection = id, Data = frame });
        }

        public bool TryReceive(out byte[] frame)
        {
            if (!inbox.TryDequeue(out frame)) return false;
            if (frame != null) return true;
            open = false; // the server closed us
            return false;
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            if (server.clients.TryRemove(id, out _))
                server.inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Disconnected, Connection = id });
        }
    }
}

/// <summary>
/// Several server transports as one (12.13: TCP and EOS P2P into one GameServer). Connection numbers are given anew so
/// two transports' numbers never meet; <see cref="Resolve"/> maps one back. Used only from the server's Tick thread.
/// </summary>
public sealed class MultiTransport : IServerTransport
{
    readonly List<IServerTransport> parts = new List<IServerTransport>();
    readonly Dictionary<int, (int part, int inner)> outer = new Dictionary<int, (int, int)>();
    readonly Dictionary<(int part, int inner), int> inner = new Dictionary<(int, int), int>();
    int nextId, cursor;

    public MultiTransport(params IServerTransport[] parts) { this.parts.AddRange(parts); }

    /// <summary>One more transport, from now on (EOS joins once its login is done).</summary>
    public void Add(IServerTransport part) => parts.Add(part);

    /// <summary>The transport and its own number behind <paramref name="connection"/>. False when it is gone.</summary>
    public bool Resolve(int connection, out IServerTransport part, out int innerConnection)
    {
        bool found = outer.TryGetValue(connection, out var at);
        part = found ? parts[at.part] : null;
        innerConnection = found ? at.inner : 0;
        return found;
    }

    // Takes turns between the parts, so a busy one does not starve the others.
    public bool TryReceive(out TransportEvent e)
    {
        for (int n = 0; n < parts.Count; n++)
        {
            int p = cursor;
            cursor = (cursor + 1) % parts.Count;
            if (!parts[p].TryReceive(out e)) continue;
            var key = (p, e.Connection);
            if (e.Kind == TransportEventKind.Connected)
            {
                int id = ++nextId;
                outer[id] = key; inner[key] = id;
                e.Connection = id;
                return true;
            }
            if (!inner.TryGetValue(key, out int known)) { n--; continue; } // one this side already let go (Disconnect): read the next
            if (e.Kind == TransportEventKind.Disconnected) { inner.Remove(key); outer.Remove(known); }
            e.Connection = known;
            return true;
        }
        e = default;
        return false;
    }

    public void Send(int connection, byte[] frame)
    {
        if (outer.TryGetValue(connection, out var at)) parts[at.part].Send(at.inner, frame);
    }

    public void Disconnect(int connection)
    {
        if (!outer.TryGetValue(connection, out var at)) return;
        parts[at.part].Disconnect(at.inner);
        outer.Remove(connection); inner.Remove(at);
    }

    public void Stop()
    {
        foreach (var part in parts) part.Stop();
    }
}

/// <summary>
/// Frames over a packet transport with a packet size limit (EOS P2P, 12.13). A frame goes out as packets of at most
/// <see cref="MaxPacket"/> bytes; only its first packet starts with the u32 little-endian frame length. The channel must
/// be reliable and ordered. <see cref="Joiner"/> puts them back together and refuses a length outside 0..<see cref="MaxFrame"/>
/// or a packet that runs past its frame.
/// </summary>
public static class PacketFraming
{
    /// <summary>EOS P2P's packet limit (P2PInterface.MAX_PACKET_SIZE).</summary>
    public const int MaxPacket = 1170;
    /// <summary>The largest frame either side accepts (GameServer itself refuses more than SyncProtocol.MaxFrame).</summary>
    public const int MaxFrame = 512 * 1024;

    /// <summary>The packets of one frame, in order.</summary>
    public static List<ArraySegment<byte>> Split(byte[] frame)
    {
        if (frame.Length > MaxFrame) throw new ArgumentException("frame over " + MaxFrame + " bytes");
        var withLength = new byte[frame.Length + 4];
        int n = frame.Length;
        withLength[0] = (byte)n; withLength[1] = (byte)(n >> 8); withLength[2] = (byte)(n >> 16); withLength[3] = (byte)(n >> 24);
        Buffer.BlockCopy(frame, 0, withLength, 4, n);
        var packets = new List<ArraySegment<byte>>();
        for (int at = 0; at < withLength.Length; at += MaxPacket)
            packets.Add(new ArraySegment<byte>(withLength, at, Math.Min(MaxPacket, withLength.Length - at)));
        return packets;
    }

    /// <summary>One peer's incoming packets back into frames. After a bad packet it stays broken (the peer is dropped).</summary>
    public sealed class Joiner
    {
        byte[] frame;
        int filled;
        public bool Broken { get; private set; }

        /// <summary>Adds one packet; a finished frame is added to <paramref name="frames"/>. False (and Broken) when the packet is bad.</summary>
        public bool Add(ArraySegment<byte> packet, List<byte[]> frames)
        {
            if (Broken) return false;
            int at = packet.Offset, end = packet.Offset + packet.Count;
            if (frame == null)
            {
                if (packet.Count < 4) return Fail();
                byte[] b = packet.Array;
                int length = b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24;
                if (length < 0 || length > MaxFrame) return Fail();
                frame = new byte[length]; filled = 0; at += 4;
            }
            int count = end - at;
            if (filled + count > frame.Length) return Fail(); // runs into the next frame: the sender does not cut frames like that
            Buffer.BlockCopy(packet.Array, at, frame, filled, count);
            filled += count;
            if (filled == frame.Length) { frames.Add(frame); frame = null; }
            return true;
        }

        bool Fail() { Broken = true; frame = null; return false; }
    }
}
