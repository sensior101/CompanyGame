using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// WebSocket transport. The receive/send loops run on thread-pool tasks and only move strings;
/// every Unity API call (JsonUtility included) happens on the caller's thread in Send/TryDequeue.
/// A dropped socket surfaces as a synthetic {t:"closed", code} message.
/// </summary>
public sealed class NetClient : INetTransport
{
    const int MaxMessageBytes = 3 * 1024 * 1024; // joined = 512KB snapshot + up to 256 ops of 8KB
    readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
    readonly SemaphoreSlim outSignal = new SemaphoreSlim(0);
    readonly CancellationTokenSource cts = new CancellationTokenSource();
    ClientWebSocket socket;
    int closedRaised;

    public void Update() { }
    public bool InviteFriends() => false;
    public ulong LobbyId => 0;

    public bool IsOpen => socket != null && socket.State == WebSocketState.Open;

    public async Task Connect(Uri uri)
    {
        socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, cts.Token).ConfigureAwait(false); // no context capture: the QA blocks on this
        _ = Task.Run(ReceiveLoop);
        _ = Task.Run(SendLoop);
    }

    public void Send(NetMsg message)
    {
        if (!IsOpen) return;
        outbox.Enqueue(JsonUtility.ToJson(message));
        outSignal.Release();
    }

    public bool TryDequeue(out NetMsg message)
    {
        message = null;
        while (message == null && inbox.TryDequeue(out string json))
        {
            try { message = JsonUtility.FromJson<NetMsg>(json); }
            catch (Exception e) { Debug.LogWarning("[Net] Bad message: " + e.Message); }
            if (message != null && string.IsNullOrEmpty(message.t)) message = null;
        }
        return message != null;
    }

    public void Close()
    {
        if (cts.IsCancellationRequested) return;
        cts.Cancel();
        try { socket?.Abort(); } catch { /* already closed */ }
    }

    async Task ReceiveLoop()
    {
        var buffer = new byte[8192];
        string code = "0";
        try
        {
            while (!cts.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                string text = await ReadMessage(buffer);
                if (text == null) { code = ((int?)socket.CloseStatus ?? 0).ToString(); break; }
                inbox.Enqueue(text);
            }
        }
        catch (Exception) { code = "error"; }
        RaiseClosed(code);
    }

    async Task<string> ReadMessage(byte[] buffer)
    {
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > MaxMessageBytes) throw new InvalidDataException("message too large");
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    async Task SendLoop()
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await outSignal.WaitAsync(cts.Token);
                while (outbox.TryDequeue(out string json))
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
                        WebSocketMessageType.Text, true, cts.Token);
            }
        }
        catch (Exception) { RaiseClosed("error"); }
    }

    void RaiseClosed(string code)
    {
        if (Interlocked.Exchange(ref closedRaised, 1) == 1) return;
        inbox.Enqueue("{\"t\":\"closed\",\"code\":\"" + code + "\"}");
    }
}
