using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns the current dialogue/trade speaker toward the player (yaw only) and returns it
/// to its original facing shortly after the conversation ends. Installs itself at runtime.
/// </summary>
public sealed class NpcFacing : MonoBehaviour
{
    public float turnSpeed = 270f;
    public float returnDelay = .4f;
    public float tolerance = 5f;

    sealed class Entry
    {
        public Quaternion home;
        public float releaseAt = -1f;
    }

    // Keyed by the speaker transform; the first home rotation is kept until the NPC is back.
    readonly Dictionary<Transform, Entry> entries = new Dictionary<Transform, Entry>();
    readonly List<Transform> scratch = new List<Transform>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (FindAnyObjectByType<NpcFacing>()) return;
        var go = new GameObject("NpcFacing");
        DontDestroyOnLoad(go);
        go.AddComponent<NpcFacing>();
    }

    public static float StepYaw(float current, float target, float speed, float dt)
        => Mathf.MoveTowardsAngle(current, target, speed * dt);

    /// <summary>World yaw (degrees) from one point toward another; NaN when horizontally too close.</summary>
    public static float YawToward(Vector3 from, Vector3 to)
    {
        var d = to - from;
        d.y = 0f;
        if (d.sqrMagnitude < .05f * .05f) return float.NaN;
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    static Transform Speaker()
    {
        var dialogue = DialogueManager.Instance;
        if (dialogue && dialogue.Current) return dialogue.Current.transform;
        var trader = PlayerInteraction.Local ? PlayerInteraction.Local.TradeSpeaker : null;
        return trader ? trader.transform : null;
    }

    void Update()
    {
        var speaker = Speaker();
        if (speaker && !entries.ContainsKey(speaker))
            entries[speaker] = new Entry { home = speaker.rotation };
        if (entries.Count == 0) return;

        var player = SceneLoadManager.Traveller;
        float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
        scratch.AddRange(entries.Keys);
        foreach (var t in scratch)
        {
            if (!t) { entries.Remove(t); continue; }
            var entry = entries[t];
            if (t == speaker)
            {
                entry.releaseAt = -1f;
                if (player) Face(t, YawToward(t.position, player.transform.position), true, dt);
                continue;
            }
            if (entry.releaseAt < 0f) entry.releaseAt = now + returnDelay;
            if (now >= entry.releaseAt && Face(t, entry.home.eulerAngles.y, false, dt))
            {
                t.rotation = entry.home;
                entries.Remove(t);
            }
        }
        scratch.Clear();
    }

    // Returns true when the transform already faces the target yaw (within tolerance when requested).
    bool Face(Transform t, float targetYaw, bool useTolerance, float dt)
    {
        if (float.IsNaN(targetYaw)) return true;
        var e = t.eulerAngles;
        float diff = Mathf.Abs(Mathf.DeltaAngle(e.y, targetYaw));
        if (diff <= (useTolerance ? tolerance : .1f)) return true;
        t.rotation = Quaternion.Euler(e.x, StepYaw(e.y, targetYaw, turnSpeed, dt), e.z);
        return false;
    }
}
