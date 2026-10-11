using UnityEngine;

using System;
using System.Collections.Generic;
using UnityEngine.Events;

public enum DialogueRequirement { Always, ActiveConstruction, OwnedBuilding }

[Serializable]
public sealed class DialogueOption
{
    public string id;
    public string label;
    public DialogueRequirement requirement;
    // Content can supply a live state query without putting its rules in the dialogue manager.
    [NonSerialized] public Func<bool> isAvailable;
    public DialogueOption(string id, string label, DialogueRequirement requirement = DialogueRequirement.Always)
    { this.id = id; this.label = label; this.requirement = requirement; }
}

/// <summary>Scene NPC data. Empty options means a spoken line with content-dependent reading time.</summary>
[DisallowMultipleComponent]
public sealed class DialogueData : MonoBehaviour
{
    public string npcId;
    [Tooltip("Shown above the head. Empty falls back to NpcTrader.displayName, then \"NPC\".")]
    public string displayName;
    [TextArea] public string line;
    public Transform head;
    public Vector3 headOffset = new Vector3(0, .32f, 0);
    [Min(.5f)] public float radius = 2.2f;
    public DialogueOption[] options = Array.Empty<DialogueOption>();
    public UnityEvent<string> onSelected = new UnityEvent<string>();
    // Bubble sits above the name tag so they never overlap.
    public const float NameTagClearance = .35f;
    public Vector3 BubblePosition => (head ? head.position + headOffset : transform.position + Vector3.up * 1.9f) + Vector3.up * NameTagClearance;
    public bool HasChoices => options != null && options.Length > 0;
    internal static readonly HashSet<DialogueData> Active = new HashSet<DialogueData>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active.Clear();
    void OnEnable() { Active.Add(this); NameTag.Ensure(gameObject, ResolveName(gameObject), head); }
    void OnDisable() => Active.Remove(this);

    /// <summary>Tag text for an NPC object: DialogueData, then NpcTrader, then "NPC".</summary>
    internal static string ResolveName(GameObject npc)
    {
        var data = npc.GetComponent<DialogueData>();
        if (data && !string.IsNullOrWhiteSpace(data.displayName)) return data.displayName;
        var trader = npc.GetComponent<NpcTrader>();
        return trader && !string.IsNullOrWhiteSpace(trader.displayName) ? trader.displayName : "NPC";
    }

    public bool InRange(Transform player, float extra = 0f)
    {
        if (!player || !isActiveAndEnabled || gameObject.scene != SceneLoadManager.CurrentMap) return false;
        Vector3 delta = player.position - transform.position;
        return Mathf.Abs(delta.y) < 2f && new Vector2(delta.x, delta.z).sqrMagnitude <= (radius + extra) * (radius + extra);
    }
}
