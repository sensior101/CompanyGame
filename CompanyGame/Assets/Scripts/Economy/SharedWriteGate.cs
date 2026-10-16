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
/// Shared world changes made while in a room are sent as ops and applied from the server echo, so every
/// peer applies them in the same order. Outside a room (or while an op is being applied) the gate is open
/// and managers write locally. Two ways out (12.9), one gate: the old room (NetWorldBridge installs
/// Sender/InRoomCheck/NameResolver) and a sync-layer server where I play a character (WorldStateSync installs the
/// Server* ones). On a server the op goes to the server, which applies it and sends the world back.
/// </summary>
public static class SharedWriteGate
{
    /// <summary>Old room: (kind, json) -> true when the op was sent. Null or false means "apply locally".</summary>
    public static Func<string, string, bool> Sender;
    /// <summary>Old room: in a room right now.</summary>
    public static Func<bool> InRoomCheck;
    /// <summary>Sync-layer server: (kind, json) -> true when the request went to the server.</summary>
    public static Func<string, string, bool> ServerSender;
    /// <summary>Sync-layer server: connected with a character right now.</summary>
    public static Func<bool> ServerCheck;
    /// <summary>True while WorldOps applies a received op; writes then go straight to the managers.</summary>
    public static bool Applying;

    /// <summary>Connected to a sync-layer server with my character (not while applying).</summary>
    public static bool OnServer => !Applying && ServerCheck != null && ServerCheck();

    /// <summary>In the old room (not while applying). Only the old room carries veh.* ops.</summary>
    public static bool InOldRoom => !Applying && InRoomCheck != null && InRoomCheck();

    /// <summary>Changes go out as requests: in the old room or on a sync-layer server.</summary>
    public static bool InRoom => InOldRoom || OnServer;

    /// <summary>Per-player id inside a room or on a server, the single-player fallback otherwise.</summary>
    public static string ActorId(string fallback) => InRoom ? GameSession.LocalPlayerId : fallback;

    /// <summary>Old room: a member's account id to a display name (installed by the net layer).</summary>
    public static Func<string, string> NameResolver;
    /// <summary>Sync-layer server: an account id to its character name in that world.</summary>
    public static Func<string, string> ServerNameResolver;
    public static string NameOf(string id)
    {
        var resolve = ServerCheck != null && ServerCheck() ? ServerNameResolver : NameResolver;
        return (resolve != null ? resolve(id) : null) ?? id;
    }

    /// <summary>True when the last TrySend happened in a room but the op could not be sent. Callers must not apply locally then.</summary>
    public static bool SendFailed { get; private set; }

    /// <summary>For senders that build their own payload (WorldDroppedItem vehicles).</summary>
    public static void SetSendFailed(bool failed) => SendFailed = failed;

    public static bool TrySend(string kind, SharedOpArgs args)
    {
        SendFailed = false;
        if (!InRoom) return false;
        var send = OnServer ? ServerSender : Sender;
        bool sent = send != null && send(kind, JsonUtility.ToJson(args));
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
        Sender = null; InRoomCheck = null; NameResolver = null;
        ServerSender = null; ServerCheck = null; ServerNameResolver = null; PersonalSender = null;
        Applying = false; SendFailed = false;
    }
}
