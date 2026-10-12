using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Host-synced citizens over NetSession world kind "citizens" (5 Hz, one small JSON).
/// The room host (or a single player) simulates and sends; everyone else follows the received
/// transforms. A client that has received nothing for its map for a few seconds (the host is in
/// another map) simulates locally instead of freezing the crowd.
/// Payload: {"map":"Assets/Scenes/Maps/X.unity","c":[{"i":0,"x":1,"y":0,"z":2,"ry":90,"s":0}]}  (s = sitting)
/// </summary>
public sealed class CitizenSync : MonoBehaviour
{
    public const string Kind = "citizens";
    const float SendInterval = .2f, StaleSeconds = 3f, Follow = 10f, SnapDistance = 6f;

    [Serializable] public sealed class Entry { public int i, s; public float x, y, z, ry; }
    [Serializable] public sealed class Packet { public string map; public Entry[] c; }

    CitizenSpawner spawner;
    NetSession hooked;
    float nextSend, lastReceived = float.NegativeInfinity;
    bool? simulating;
    readonly Dictionary<int, Entry> targets = new Dictionary<int, Entry>();

    void Awake() { spawner = GetComponent<CitizenSpawner>(); }

    void OnDestroy() { if (hooked) hooked.WorldReceived -= OnWorld; }

    void Update()
    {
        Hook(NetSession.Instance);
        bool simulate = GameSession.IsAuthority || Time.unscaledTime - lastReceived > StaleSeconds;
        if (simulating != simulate)
        {
            simulating = simulate;
            foreach (var walker in spawner.Citizens) if (walker) walker.SetProxy(!simulate);
        }
        if (!simulate) { FollowTargets(Time.deltaTime); return; }
        if (hooked && hooked.InRoom && hooked.IsHost && Time.unscaledTime >= nextSend)
        {
            nextSend = Time.unscaledTime + SendInterval;
            hooked.SendWorld(Kind, Serialize());
        }
    }

    void Hook(NetSession net)
    {
        if (net == hooked) return;
        if (hooked) hooked.WorldReceived -= OnWorld;
        hooked = net;
        if (hooked) hooked.WorldReceived += OnWorld;
    }

    string Serialize()
    {
        var list = new List<Entry>(spawner.Citizens.Count);
        foreach (var walker in spawner.Citizens)
        {
            if (!walker) continue;
            Vector3 p = walker.transform.position;
            list.Add(new Entry
            {
                i = walker.Index, s = walker.Seated ? 1 : 0,
                x = Round(p.x), y = Round(p.y), z = Round(p.z), ry = Mathf.Round(walker.transform.eulerAngles.y),
            });
        }
        return JsonUtility.ToJson(new Packet { map = spawner.MapPath, c = list.ToArray() });
    }

    static float Round(float v) => Mathf.Round(v * 100f) / 100f;

    void OnWorld(string kind, string json)
    {
        if (kind != Kind || string.IsNullOrEmpty(json)) return;
        Packet packet;
        try { packet = JsonUtility.FromJson<Packet>(json); }
        catch (ArgumentException) { return; }
        if (packet == null || packet.c == null || packet.map != spawner.MapPath) return; // host is in another map
        int known = spawner.Citizens.Count;   // unknown indices (beyond this map's crowd) are ignored, so targets stays bounded
        foreach (var entry in packet.c) if (entry != null && entry.i >= 0 && entry.i < known) targets[entry.i] = entry;
        lastReceived = Time.unscaledTime;
    }

    void FollowTargets(float dt)
    {
        float blend = 1f - Mathf.Exp(-Follow * dt);
        foreach (var walker in spawner.Citizens)
        {
            if (!walker || !targets.TryGetValue(walker.Index, out var t)) continue;
            var target = new Vector3(t.x, t.y, t.z);
            var body = walker.transform;
            bool snap = (body.position - target).sqrMagnitude > SnapDistance * SnapDistance;
            body.SetPositionAndRotation(
                snap ? target : Vector3.Lerp(body.position, target, blend),
                snap ? Quaternion.Euler(0f, t.ry, 0f) : Quaternion.Slerp(body.rotation, Quaternion.Euler(0f, t.ry, 0f), blend));
            walker.SetSeatedVisual(t.s == 1);
        }
    }
}
