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
    [TextArea] public string line;
    public Transform head;
    public Vector3 headOffset = new Vector3(0, .32f, 0);
    [Min(.5f)] public float radius = 2.2f;
    public DialogueOption[] options = Array.Empty<DialogueOption>();
    public UnityEvent<string> onSelected = new UnityEvent<string>();
    public Vector3 BubblePosition => head ? head.position + headOffset : transform.position + Vector3.up * 1.9f;
    public bool HasChoices => options != null && options.Length > 0;
    internal static readonly HashSet<DialogueData> Active = new HashSet<DialogueData>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active.Clear();
    void OnEnable() => Active.Add(this);
    void OnDisable() => Active.Remove(this);

    public bool InRange(Transform player, float extra = 0f)
    {
        if (!player || !isActiveAndEnabled || gameObject.scene != SceneLoadManager.CurrentMap) return false;
        Vector3 delta = player.position - transform.position;
        return Mathf.Abs(delta.y) < 2f && new Vector2(delta.x, delta.z).sqrMagnitude <= (radius + extra) * (radius + extra);
    }
}
