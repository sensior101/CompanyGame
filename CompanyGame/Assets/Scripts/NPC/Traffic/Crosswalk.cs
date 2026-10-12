using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A zebra crossing between the sidewalks on both sides of a road. Pedestrian code (citizens) asks
/// <see cref="CanCross"/>; with no signal a crossing is always open.
/// </summary>
public sealed class Crosswalk : MonoBehaviour
{
    [Tooltip("Sidewalk ends of the crossing, one on each side of the road.")]
    public Transform endA;
    public Transform endB;
    [Tooltip("The signal for the cars that cross this walk. Pedestrians go while it is red. Empty means always open.")]
    public TrafficSignal signal;

    // Needs this long in red before people step out, and enough red left to walk across at a slow pace.
    const float ClearAfterRedSeconds = .5f;
    const float SlowWalkMps = 1.2f;

    static readonly List<Crosswalk> all = new List<Crosswalk>();

    public bool CanCross
    {
        get
        {
            if (!signal) return true;
            return signal.Current == TrafficSignal.Phase.Red
                && signal.TimeInPhase >= ClearAfterRedSeconds
                && signal.TimeLeft >= CrossSeconds;
        }
    }

    /// <summary>Red time a walker needs to be across (capped so a short red still leaves a window to start).</summary>
    public float CrossSeconds
    {
        get
        {
            float length = endA && endB ? Vector3.Distance(endA.position, endB.position) : 0f;
            float redCap = signal ? Mathf.Max(3f, signal.red - 2f) : 3f;
            return Mathf.Clamp(length / SlowWalkMps, 3f, redCap);
        }
    }

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    /// <summary>The closest crossing whose line passes within r (metres, horizontal) of p, or null.</summary>
    public static Crosswalk FindNear(Vector3 p, float r = 4f)
    {
        Crosswalk best = null;
        float bestSqr = r * r;
        foreach (Crosswalk walk in all)
        {
            if (!walk || !walk.endA || !walk.endB) continue;
            float sqr = walk.SqrDistance(p);
            if (sqr > bestSqr) continue;
            best = walk;
            bestSqr = sqr;
        }
        return best;
    }

    float SqrDistance(Vector3 p)
    {
        Vector3 a = endA.position, b = endB.position;
        Vector2 ab = new Vector2(b.x - a.x, b.z - a.z);
        Vector2 ap = new Vector2(p.x - a.x, p.z - a.z);
        float t = ab.sqrMagnitude < .0001f ? 0f : Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude);
        Vector3 closest = Vector3.Lerp(a, b, t);
        if (Mathf.Abs(p.y - closest.y) > 3f) return float.MaxValue;
        return new Vector2(p.x - closest.x, p.z - closest.z).sqrMagnitude;
    }

    void OnDrawGizmos()
    {
        if (!endA || !endB) return;
        Gizmos.color = CanCross ? Color.green : Color.red;
        Gizmos.DrawLine(endA.position + Vector3.up * .1f, endB.position + Vector3.up * .1f);
        Gizmos.DrawWireSphere(endA.position, .3f);
        Gizmos.DrawWireSphere(endB.position, .3f);
    }
}
