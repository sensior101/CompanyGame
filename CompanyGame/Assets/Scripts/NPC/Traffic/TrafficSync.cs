using System;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public sealed class TrafficCarSnap
{
    public int k;          // spawner key
    public string p;       // car item id (picks the prefab)
    public float x, y, z, ry;
}

/// <summary>World message "traffic": the host's signal clock and every NPC car's pose.</summary>
[Serializable]
public sealed class TrafficSnap
{
    public string map;
    public double sig;     // host TrafficSignal.Clock when sent
    public TrafficCarSnap[] cars = Array.Empty<TrafficCarSnap>();
}

/// <summary>
/// Host side: sends the car list and the signal clock about 5 times a second. Client side: aligns
/// <see cref="TrafficSignal.TimeOffset"/> to the host and hands the cars to <see cref="TrafficSpawner"/> as proxies.
/// Lives next to the spawner (the spawner requires it), so it only exists on maps that have traffic.
/// </summary>
public sealed class TrafficSync : MonoBehaviour
{
    public const string Kind = "traffic";
    const float SendInterval = .2f;
    const double ResyncThreshold = .3;

    TrafficSpawner spawner;
    NetSession net;
    float nextSend;
    bool aligned;

    void Awake() => spawner = GetComponent<TrafficSpawner>();

    void OnDisable()
    {
        if (net) net.WorldReceived -= OnWorld;
        net = null;
        ResetClock();
    }

    // The host's clock offset must not outlive the room or the scene.
    void ResetClock()
    {
        aligned = false;
        TrafficSignal.TimeOffset = 0;
    }

    void Update()
    {
        NetSession current = NetSession.Instance;
        if (current != net)
        {
            if (net) net.WorldReceived -= OnWorld;
            net = current;
            if (net) net.WorldReceived += OnWorld;
        }
        if (aligned && (!net || !net.InRoom)) ResetClock();
        if (!net || !net.IsHost || !GameSession.IsAuthority || Time.time < nextSend) return;
        nextSend = Time.time + SendInterval;
        net.SendWorld(Kind, ToJson());
    }

    string ToJson()
    {
        var snap = new TrafficSnap { map = SceneManager.GetActiveScene().path, sig = TrafficSignal.Clock };
        var list = new System.Collections.Generic.List<TrafficCarSnap>();
        foreach (TrafficCar car in spawner.Cars)
        {
            if (!car || car.Finished || list.Count >= TrafficSpawner.MaxCars) continue;
            Vector3 p = car.Position;
            list.Add(new TrafficCarSnap { k = car.key, p = car.itemId, x = Round(p.x), y = Round(p.y), z = Round(p.z), ry = Round(car.Yaw) });
        }
        snap.cars = list.ToArray();
        return JsonUtility.ToJson(snap);
    }

    static float Round(float v) => Mathf.Round(v * 100f) / 100f;

    void OnWorld(string kind, string json)
    {
        if (kind != Kind || GameSession.IsAuthority || string.IsNullOrEmpty(json)) return;
        TrafficSnap snap;
        try { snap = JsonUtility.FromJson<TrafficSnap>(json); }
        catch (ArgumentException) { return; }
        if (snap == null || snap.map != SceneManager.GetActiveScene().path) return;
        AlignSignals(snap.sig);
        spawner.ApplyRemote(snap);
    }

    // Time.timeAsDouble differs per PC, so the offset makes this PC's signal clock read the host's value.
    void AlignSignals(double hostClock)
    {
        double wanted = hostClock - Time.timeAsDouble;
        if (!aligned || Math.Abs(wanted - TrafficSignal.TimeOffset) > ResyncThreshold) TrafficSignal.TimeOffset = wanted;
        else TrafficSignal.TimeOffset += (wanted - TrafficSignal.TimeOffset) * .1;
        aligned = true;
    }
}
