using System;
using UnityEngine;

/// <summary>
/// Fixed-cycle traffic light. The phase is a pure function of <see cref="Clock"/>, the shared NpcClock (12.14), so every
/// PC of one server shows the same lights. Every signal runs the same 70 s cycle (12.14, 2026-10-20), the two groups of a
/// junction half a cycle apart: this group green 12 s, yellow 3 s, all red 2 s, pedestrians 16 s on every crossing of the
/// junction, all red 2 s, then the other group the same way. Cars and pedestrians are never let go at the same time.
/// </summary>
public sealed class TrafficSignal : MonoBehaviour
{
    public enum Phase { Green, Yellow, Red }

    /// <summary>One lamp housing; a signal can have several (one per approach pole).</summary>
    [Serializable]
    public sealed class Head
    {
        public Renderer red, yellow, green;
    }

    /// <summary>Seconds of each part of a half cycle (one group's turn): green, yellow, all red, pedestrians, all red.</summary>
    public const float Green = 12f, Yellow = 3f, AllRed = 2f, Walk = 16f;
    public const float CycleSeconds = 2f * (Green + Yellow + AllRed + Walk + AllRed);
    /// <summary>Citizens and traffic repeat after this many seconds: 8 signal cycles.</summary>
    public const int CyclesPerLoop = 8;
    public const double LoopSeconds = CyclesPerLoop * (double)CycleSeconds;

    [Tooltip("Shifts this signal inside the shared cycle. The crossing group of a junction is half a cycle later (TrafficMapBuilder).")]
    public float offset;
    public Head[] heads = Array.Empty<Head>();

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly Color RedOn = new Color(1f, .12f, .1f);
    static readonly Color YellowOn = new Color(1f, .75f, .05f);
    static readonly Color GreenOn = new Color(.1f, 1f, .35f);
    static readonly Color Off = new Color(.06f, .06f, .06f);
    static MaterialPropertyBlock block;

    Phase shown = (Phase)(-1);

    public static double Clock => NpcClock.Now;
    public float Cycle => CycleSeconds;
    public Phase Current => PhaseAt(Clock, out _, out _);
    public float TimeInPhase { get { PhaseAt(Clock, out float inPhase, out _); return inPhase; } }
    public float TimeLeft { get { PhaseAt(Clock, out _, out float left); return left; } }

    float CycleTime(double clock) => (float)(((clock + offset) % CycleSeconds + CycleSeconds) % CycleSeconds);

    /// <summary>Car phase at a given shared clock value, with the seconds spent in it and the seconds remaining.</summary>
    public Phase PhaseAt(double clock, out float timeInPhase, out float timeLeft)
    {
        float t = CycleTime(clock);
        if (t < Green) { timeInPhase = t; timeLeft = Green - t; return Phase.Green; }
        t -= Green;
        if (t < Yellow) { timeInPhase = t; timeLeft = Yellow - t; return Phase.Yellow; }
        t -= Yellow;
        timeInPhase = t;
        timeLeft = CycleSeconds - Green - Yellow - t;
        return Phase.Red;
    }

    /// <summary>
    /// Whether the junction's pedestrians have green at that clock value (twice a cycle, after each group's yellow and
    /// all red), with the seconds of it gone and left. Every signal of one junction gives the same answer.
    /// </summary>
    public bool WalkAt(double clock, out float inWalk, out float left)
    {
        const float half = CycleSeconds / 2f;
        float t = ((CycleTime(clock) - (Green + Yellow + AllRed)) % half + half) % half;
        inWalk = t;
        left = Walk - t;
        return t < Walk;
    }

    void Update()
    {
        Phase now = Current;
        if (now == shown) return;
        shown = now;
        ApplyLamps(now);
    }

    public void ApplyLamps(Phase phase)
    {
        block ??= new MaterialPropertyBlock();
        foreach (Head head in heads)
        {
            if (head == null) continue;
            Light(head.red, phase == Phase.Red, RedOn);
            Light(head.yellow, phase == Phase.Yellow, YellowOn);
            Light(head.green, phase == Phase.Green, GreenOn);
        }
    }

    static void Light(Renderer lamp, bool on, Color color)
    {
        if (!lamp) return;
        lamp.GetPropertyBlock(block);
        block.SetColor(BaseColorId, on ? color : Off);
        block.SetColor(EmissionId, on ? color * 2.5f : Color.black);
        lamp.SetPropertyBlock(block);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Application.isPlaying ? PhaseColor(Current) : Color.white;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 3f, .5f);
    }

    static Color PhaseColor(Phase phase) => phase == Phase.Green ? Color.green : phase == Phase.Yellow ? Color.yellow : Color.red;
}
