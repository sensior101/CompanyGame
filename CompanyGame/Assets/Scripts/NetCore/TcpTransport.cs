using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Direct TCP server (dedicated server, and "my PC server" reached at 127.0.0.1). Frame = u32 little-endian length + body.
/// Accept/receive/send loops run on thread-pool tasks and only move bytes; the server reads events in its Tick.
/// </summary>
public sealed class TcpServerTransport : IServerTransport
{
    /// <summary>Open sockets, Hello or not. More are closed on accept.</summary>
    public const int MaxConnections = 256;

    readonly TcpListener listener;
    readonly ConcurrentQueue<TransportEvent> inbox = new ConcurrentQueue<TransportEvent>();
    readonly ConcurrentDictionary<int, TcpLink> links = new ConcurrentDictionary<int, TcpLink>();
    volatile bool stopped;
    int nextId;

    /// <summary>The bound port (the OS picks one when constructed with 0).</summary>
    public int Port { get; }

    public TcpServerTransport(IPAddress address, int port)
    {
        listener = new TcpListener(address, port);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task.Run(AcceptLoop);
    }

    async Task AcceptLoop()
    {
        while (!stopped)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { return; }
            catch (Exception) { if (stopped) return; continue; } // one failed handshake, keep listening
            if (links.Count >= MaxConnections) { client.Close(); continue; }
            int id = Interlocked.Increment(ref nextId);
            var link = new TcpLink(client, SyncProtocol.MaxFrame,
                frame => inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Data, Connection = id, Data = frame }),
                () => { links.TryRemove(id, out _); inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Disconnected, Connection = id }); });
            links[id] = link;
            inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, Connection = id });
            link.Start();
        }
    }

    public bool TryReceive(out TransportEvent e) => inbox.TryDequeue(out e);

    public void Send(int connection, byte[] frame)
    {
        if (links.TryGetValue(connection, out var link)) link.Send(frame);
    }

    public void Disconnect(int connection)
    {
        if (links.TryGetValue(connection, out var link)) link.CloseAfterSend();
    }

    public void Stop()
    {
        stopped = true;
        try { listener.Stop(); } catch { /* already stopped */ }
        foreach (var link in links.Values) link.Close();
    }
}

/// <summary>Client side of <see cref="TcpServerTransport"/>.</summary>
public sealed class TcpClientTransport : IClientTransport
{
    readonly ConcurrentQueue<byte[]> inbox = new ConcurrentQueue<byte[]>();
    TcpLink link;
    volatile bool open;

    public bool IsOpen => open;

    public async Task Connect(string host, int port)
    {
        var client = new TcpClient { NoDelay = true };
        try { await client.ConnectAsync(host, port).ConfigureAwait(false); } // no context capture: QA blocks on this
        catch { client.Close(); throw; }
        // A server Spawn is a client Spawn plus the owner, so allow some room over the server's limit.
        link = new TcpLink(client, SyncProtocol.MaxFrame * 2, inbox.Enqueue, () => inbox.Enqueue(null));
        open = true;
        link.Start();
    }

    public void Send(byte[] frame)
    {
        if (open) link.Send(frame);
    }

    public bool TryReceive(out byte[] frame)
    {
        if (!inbox.TryDequeue(out frame)) return false;
        if (frame != null) return true;
        open = false; // every frame before the close has been read
        return false;
    }

    /// <summary>Closes once the frames already sent are out (the last character save), at most 2 seconds later.</summary>
    public void Close()
    {
        open = false;
        var closing = link;
        if (closing == null) return;
        closing.CloseAfterSend();
        Task.Delay(2000).ContinueWith(_ => closing.Close());
    }
}

/// <summary>One socket: a receive loop that cuts frames and a send loop that writes queued frames in batches.</summary>
sealed class TcpLink
{
    const int MaxQueuedFrames = 8192; // a peer that stops reading is dropped instead of growing memory

    static readonly byte[] CloseMarker = new byte[0];

    readonly TcpClient client;
    readonly NetworkStream stream;
    readonly int maxFrame;
    readonly Action<byte[]> onFrame;
    readonly Action onClosed;
    readonly ConcurrentQueue<byte[]> outbox = new ConcurrentQueue<byte[]>();
    readonly SemaphoreSlim outSignal = new SemaphoreSlim(0);
    readonly CancellationTokenSource cts = new CancellationTokenSource();
    int queued, closed;

    public TcpLink(TcpClient client, int maxFrame, Action<byte[]> onFrame, Action onClosed)
    {
        client.NoDelay = true;
        this.client = client;
        stream = client.GetStream();
        this.maxFrame = maxFrame;
        this.onFrame = onFrame;
        this.onClosed = onClosed;
    }

    public void Start()
    {
        Task.Run(ReceiveLoop);
        Task.Run(SendLoop);
    }

    public void Send(byte[] frame)
    {
        if (Volatile.Read(ref closed) != 0) return;
        if (Interlocked.Increment(ref queued) > MaxQueuedFrames) { Close(); return; }
        outbox.Enqueue(frame);
        outSignal.Release();
    }

    public void CloseAfterSend()
    {
        outbox.Enqueue(CloseMarker);
        outSignal.Release();
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref closed, 1) == 1) return;
        cts.Cancel();
        try { client.Close(); } catch { /* already closed */ }
        onClosed();
    }

    async Task ReceiveLoop()
    {
        var header = new byte[4];
        try
        {
            while (await Fill(header, 4))
            {
                int length = header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24;
                if (length < 0 || length > maxFrame)
                {
                    onFrame(Array.Empty<byte>()); // the reader rejects it; stop reading but let a Reject go out first
                    return;
                }
                var body = new byte[length];
                if (!await Fill(body, length)) break;
                onFrame(body);
            }
        }
        catch (Exception) { /* reset or closed */ }
        Close();
    }

    async Task<bool> Fill(byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer, read, count - read, cts.Token).ConfigureAwait(false);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    async Task SendLoop()
    {
        var batch = new MemoryStream();
        try
        {
            while (true)
            {
                await outSignal.WaitAsync(cts.Token).ConfigureAwait(false);
                batch.SetLength(0);
                bool closeAfter = false;
                while (outbox.TryDequeue(out byte[] frame))
                {
                    if (ReferenceEquals(frame, CloseMarker)) { closeAfter = true; break; }
                    Interlocked.Decrement(ref queued);
                    int n = frame.Length;
                    batch.WriteByte((byte)n); batch.WriteByte((byte)(n >> 8)); batch.WriteByte((byte)(n >> 16)); batch.WriteByte((byte)(n >> 24));
                    batch.Write(frame, 0, n);
                }
                if (batch.Length > 0) await stream.WriteAsync(batch.GetBuffer(), 0, (int)batch.Length, cts.Token).ConfigureAwait(false);
                if (closeAfter) break;
            }
        }
        catch (Exception) { /* reset or closed */ }
        Close();
    }
}
