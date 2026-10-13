using System.Collections.Concurrent;

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
