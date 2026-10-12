/// <summary>
/// What NetSession needs from a connection: send the server's flat messages, read them back one per call.
/// <see cref="NetClient"/> is the Cloudflare/Node WebSocket; SteamTransport makes the host PC the server.
/// </summary>
public interface INetTransport
{
    bool IsOpen { get; }
    void Send(NetMsg message);
    bool TryDequeue(out NetMsg message);
    void Close();
    /// <summary>Per-frame work for transports that poll (the WebSocket has none).</summary>
    void Update();
    /// <summary>Steam only: opens the friend invite overlay for the current room. False when not applicable.</summary>
    bool InviteFriends();
    /// <summary>Steam only: the lobby of the current room (0 = none).</summary>
    ulong LobbyId { get; }
}
