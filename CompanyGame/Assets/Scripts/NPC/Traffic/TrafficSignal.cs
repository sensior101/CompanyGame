using System;
using UnityEngine;

/// <summary>
/// Fixed-cycle traffic light (green, yellow, red). The phase is a pure function of <see cref="Clock"/>.
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

    [Min(.5f)] public float green = 12f;
    [Min(.5f)] public float yellow = 3f;
    [Min(.5f)] public float red = 15f;
    [Tooltip("Shifts this signal inside the shared cycle. A crossing road uses green + yellow so it is red while this one is green.")]
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

    public static double Clock => Time.timeAsDouble;
    public float Cycle => green + yellow + red;
    public Phase Current => PhaseAt(Clock, out _, out _);
    public float TimeInPhase { get { PhaseAt(Clock, out float inPhase, out _); return inPhase; } }
    public float TimeLeft { get { PhaseAt(Clock, out _, out float left); return left; } }

    /// <summary>Phase at a given shared clock value, with the seconds spent in it and the seconds remaining.</summary>
    public Phase PhaseAt(double clock, out float timeInPhase, out float timeLeft)
    {
        float cycle = Mathf.Max(.1f, Cycle);
        float t = (float)(((clock + offset) % cycle + cycle) % cycle);
        if (t < green) { timeInPhase = t; timeLeft = green - t; return Phase.Green; }
        t -= green;
        if (t < yellow) { timeInPhase = t; timeLeft = yellow - t; return Phase.Yellow; }
        t -= yellow;
        timeInPhase = t;
        timeLeft = red - t;
        return Phase.Red;
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
