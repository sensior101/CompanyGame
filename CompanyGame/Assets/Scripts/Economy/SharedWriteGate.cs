using System;
using UnityEngine;

/// <summary>Generic argument bag for shared-world ops (JsonUtility cannot serialize polymorphic payloads).</summary>
[Serializable]
public sealed class SharedOpArgs
{
    public string a, b, c, d;
    public long n;
    public bool f;
}

/// <summary>
/// Shared world changes made on a sync-layer server where I play a character are sent to the server as requests
/// (WorldStateSync installs the Server* hooks); the server applies them and sends the world back (12.9). Off a server
/// (or while an op is being applied) the gate is open and managers write locally.
/// </summary>
public static class SharedWriteGate
{
    /// <summary>Sync-layer server: (kind, json) -> true when the request went to the server.</summary>
    public static Func<string, string, bool> ServerSender;
    /// <summary>Sync-layer server: connected with a character right now.</summary>
    public static Func<bool> ServerCheck;
    /// <summary>True while WorldOps applies a received op; writes then go straight to the managers.</summary>
    public static bool Applying;

    /// <summary>Connected to a sync-layer server with my character (not while applying).</summary>
    public static bool OnServer => !Applying && ServerCheck != null && ServerCheck();

    /// <summary>Changes go out as requests: on a sync-layer server (same as <see cref="OnServer"/>).</summary>
    public static bool InRoom => OnServer;

    /// <summary>Per-player id on a server, the single-player fallback otherwise.</summary>
    public static string ActorId(string fallback) => InRoom ? GameSession.LocalPlayerId : fallback;

    /// <summary>Sync-layer server: an account id to its character name in that world.</summary>
    public static Func<string, string> ServerNameResolver;
    public static string NameOf(string id)
    {
        bool server = ServerCheck != null && ServerCheck();
        return (server && ServerNameResolver != null ? ServerNameResolver(id) : null) ?? id;
    }

    /// <summary>True when the last TrySend happened on a server but the op could not be sent. Callers must not apply locally then.</summary>
    public static bool SendFailed { get; private set; }

    public static bool TrySend(string kind, SharedOpArgs args)
    {
        SendFailed = false;
        if (!InRoom) return false;
        bool sent = ServerSender != null && ServerSender(kind, JsonUtility.ToJson(args));
        SendFailed = !sent;
        return sent;
    }

    /// <summary>Sync-layer server: (kind, json) of a personal request (p.*, 12.10.1). Installed by WorldStateSync.</summary>
    public static Action<string, string> PersonalSender;

    /// <summary>
    /// Tells the server about a money change this client already applied (12.10.1): only on a sync-layer server, and it
    /// never stops the local change (unlike TrySend). The server decides on its own ledger and sends it back.
    /// </summary>
    public static void SendPersonal(string kind, SharedOpArgs args)
    {
        if (OnServer) PersonalSender?.Invoke(kind, JsonUtility.ToJson(args));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        ServerSender = null; ServerCheck = null; ServerNameResolver = null; PersonalSender = null;
        Applying = false; SendFailed = false;
    }
}
