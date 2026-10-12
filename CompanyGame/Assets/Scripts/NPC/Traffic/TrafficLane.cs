using UnityEngine;

/// <summary>
/// One driving lane: an ordered list of points, the lanes a car may continue onto, and the signal that
/// controls the lane's end (its stop line). Connector lanes are short corner paths inside an intersection.
/// </summary>
public sealed class TrafficLane : MonoBehaviour
{
    public Transform[] points = System.Array.Empty<Transform>();
    [Min(5f)] public float speedLimitKmh = 40f;
    public TrafficLane[] next = System.Array.Empty<TrafficLane>();
    [Tooltip("Controls this lane's last point (the stop line). Empty means the lane never stops.")]
    public TrafficSignal signal;
    [Tooltip("Short path through an intersection corner. Cars are not spawned on it.")]
    public bool connector;

    float[] cumulative;

    public int Count => points == null ? 0 : points.Length;

    /// <summary>Length of the lane polyline in metres.</summary>
    public float Length
    {
        get { Cache(); return cumulative.Length == 0 ? 0f : cumulative[cumulative.Length - 1]; }
    }

    public Vector3 Point(int index) => points[index].position;

    /// <summary>Distance from segment start of point index to the end of the lane, measured along the polyline.</summary>
    public float DistanceFromPointToEnd(int index)
    {
        Cache();
        return Length - cumulative[Mathf.Clamp(index, 0, cumulative.Length - 1)];
    }

    /// <summary>World position and forward direction at distance s from the start; nextIndex is the point the car is heading for.</summary>
    public Vector3 Sample(float s, out Vector3 forward, out int nextIndex)
    {
        Cache();
        forward = Vector3.forward;
        nextIndex = 0;
        if (Count == 0) return transform.position;
        if (Count == 1) return Point(0);
        s = Mathf.Clamp(s, 0f, Length);
        int i = 1;
        while (i < Count - 1 && cumulative[i] < s) i++;
        float span = Mathf.Max(.0001f, cumulative[i] - cumulative[i - 1]);
        Vector3 a = Point(i - 1), b = Point(i);
        forward = (b - a).normalized;
        nextIndex = i;
        return Vector3.Lerp(a, b, (s - cumulative[i - 1]) / span);
    }

    /// <summary>Clears the cached lengths after the points were moved or replaced.</summary>
    public void Invalidate() => cumulative = null;

    void Cache()
    {
        if (cumulative != null && cumulative.Length == Count) return;
        cumulative = new float[Count];
        for (int i = 1; i < Count; i++)
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(Point(i - 1), Point(i));
    }

    void OnDrawGizmos()
    {
        if (Count == 0) return;
        Gizmos.color = connector ? new Color(1f, .6f, .1f) : signal ? new Color(1f, .3f, .3f) : new Color(.2f, .8f, 1f);
        for (int i = 1; i < Count; i++)
        {
            if (!points[i - 1] || !points[i]) continue;
            Gizmos.DrawLine(Point(i - 1), Point(i));
            Vector3 mid = (Point(i - 1) + Point(i)) * .5f;
            Vector3 dir = (Point(i) - Point(i - 1)).normalized;
            Gizmos.DrawLine(mid, mid - dir * .8f + Vector3.Cross(Vector3.up, dir) * .4f);
            Gizmos.DrawLine(mid, mid - dir * .8f - Vector3.Cross(Vector3.up, dir) * .4f);
        }
        if (next == null) return;
        Gizmos.color = Color.yellow;
        foreach (TrafficLane lane in next)
            if (lane && lane.Count > 0 && points[Count - 1]) Gizmos.DrawLine(Point(Count - 1), lane.Point(0));
    }
}
