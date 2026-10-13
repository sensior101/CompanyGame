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
/// and managers write locally. The net layer installs Sender/InRoomCheck (NetWorldBridge).
/// </summary>
public static class SharedWriteGate
{
    /// <summary>(kind, json) -> true when the op was sent. Null or false means "apply locally".</summary>
    public static Func<string, string, bool> Sender;
    public static Func<bool> InRoomCheck;
    /// <summary>True while WorldOps applies a received op; writes then go straight to the managers.</summary>
    public static bool Applying;

    public static bool InRoom => !Applying && InRoomCheck != null && InRoomCheck();

    /// <summary>Per-player id inside a room, the single-player fallback otherwise.</summary>
    public static string ActorId(string fallback) => InRoom ? GameSession.LocalPlayerId : fallback;

    /// <summary>Resolves a room member's account id to a display name (installed by the net layer).</summary>
    public static Func<string, string> NameResolver;
    public static string NameOf(string id) => (NameResolver != null ? NameResolver(id) : null) ?? id;

    /// <summary>True when the last TrySend happened in a room but the op could not be sent. Callers must not apply locally then.</summary>
    public static bool SendFailed { get; private set; }

    /// <summary>For senders that build their own payload (WorldDroppedItem vehicles).</summary>
    public static void SetSendFailed(bool failed) => SendFailed = failed;

    public static bool TrySend(string kind, SharedOpArgs args)
    {
        SendFailed = false;
        if (!InRoom) return false;
        bool sent = Sender != null && Sender(kind, JsonUtility.ToJson(args));
        SendFailed = !sent;
        return sent;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Sender = null; InRoomCheck = null; NameResolver = null; Applying = false; SendFailed = false; }
}
