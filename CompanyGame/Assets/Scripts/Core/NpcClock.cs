using System;
using UnityEngine;

/// <summary>
/// The seconds clock and the seed citizens, traffic and signals are computed from (Docs/Multiplayer_Redesign.md 12.14).
/// On a server (a server process, or the owner of an in-app server) ServerWorld sets it to the world clock every frame.
/// A client follows the clock section (once per world minute, 5 s) plus half a round trip: a difference over
/// <see cref="SnapSeconds"/> (joining, a skipped night) is taken at once, a smaller one at most 0.1 s per second.
/// Offline (no session) it is this PC's game time and seed 0, so the pause menu stops the traffic as before.
/// </summary>
public static class NpcClock
{
    public const double SnapSeconds = 1, SlewPerSecond = .1;

    static double offset, target, lastSlew;

    /// <summary>True while a server's clock is followed (a server, or a client that got a clock section).</summary>
    public static bool Synced { get; private set; }
    /// <summary>The world's seed (0 offline). The spawners build their plans again when it changes.</summary>
    public static uint Seed { get; private set; }
    /// <summary>NPC cars per map from the server's settings (12.15), -1 = the map's own count. TrafficSpawner builds again when it changes.</summary>
    public static int Cars { get; set; } = -1;

    /// <summary>World seconds now: the same on every PC of one server, give or take the clock estimate.</summary>
    public static double Now
    {
        get
        {
            if (!Synced) return Time.timeAsDouble;
            double now = Time.unscaledTimeAsDouble, dt = now - lastSlew;
            lastSlew = now;
            if (dt > 0) offset += Math.Clamp(target - offset, -dt * SlewPerSecond, dt * SlewPerSecond);
            return now + offset;
        }
    }

    /// <summary>The server's world seconds right now. <paramref name="exact"/>: this is the server itself (no slewing).</summary>
    public static void Set(double worldSeconds, uint seed, bool exact)
    {
        double next = worldSeconds - Time.unscaledTimeAsDouble;
        if (!Synced) lastSlew = Time.unscaledTimeAsDouble;
        if (exact || !Synced || Math.Abs(next - offset) > SnapSeconds) offset = next;
        target = next;
        Seed = seed;
        Synced = true;
    }

    /// <summary>Back to this PC's own time and seed 0 (left the server).</summary>
    public static void Reset() { Synced = false; offset = target = 0; Seed = 0; Cars = -1; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Reset();
}
