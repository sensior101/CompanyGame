using UnityEngine;

using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

/// <summary>Persistent conversation owner shared by all maps.</summary>
[DefaultExecutionOrder(-350)]
public sealed class DialogueManager : GameSystem<DialogueManager>
{
    public DialogueData Nearby { get; private set; }
    public DialogueData Current { get; private set; }
    public IReadOnlyList<DialogueOption> VisibleOptions => visibleOptions;
    public static bool IsDialogueOpen => Instance && Instance.Current;
    public static bool HasNearbyNpc => Instance && Instance.Nearby;
    public static bool OwnsInput => Instance && ((Instance.Current && Instance.Current.HasChoices) || Instance.consumedFrame == Time.frameCount);
    public Func<bool> CanStart = () => false;
    // Future systems supply state queries; dialogue never owns construction or property state.
    public Func<bool> HasActiveConstruction = () => false;
    public Func<int> OwnedBuildingCount = () => 0;
    public event Action Changed;
    public event Action<DialogueData, string> OptionSelected;
    readonly List<DialogueOption> visibleOptions = new List<DialogueOption>();
    float dismissAt;
    int consumedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void EnsureInstance()
    {
        if (!Instance && !FindAnyObjectByType<DialogueManager>())
            new GameObject("DialogueManager").AddComponent<DialogueManager>();
    }

    protected override void OnSystemAwake()
    {
        SceneManager.activeSceneChanged += MapChanged;
        SceneManager.sceneUnloaded += MapUnloaded;
    }

    void Update()
    {
        var player = SceneLoadManager.Traveller;
        if (!ReferenceEquals(Current, null))
        {
            if (!Current || SceneLoadManager.IsLoading || !Current.InRange(player ? player.transform : null, .6f) ||
                GameInput.CancelPressed || (!Current.HasChoices && Time.unscaledTime >= dismissAt)) Close();
            else return;
        }
        if (SceneLoadManager.IsLoading || !player || !CanStart() || consumedFrame == Time.frameCount)
        { Nearby = null; return; }
        Nearby = FindNearest(player.transform);
        if (Nearby && GameInput.InteractPressed) Begin(Nearby);
    }

    public bool Begin(DialogueData npc)
    {
        var player = SceneLoadManager.Traveller;
        if (Current || SceneLoadManager.IsLoading || !player || !CanStart() || !npc || !npc.InRange(player.transform)) return false;
        Current = npc;
        Nearby = null;
        consumedFrame = Time.frameCount;
        visibleOptions.Clear();
        foreach (var option in npc.options ?? Array.Empty<DialogueOption>())
            if (option != null && IsAvailable(option.requirement)) visibleOptions.Add(option);
        dismissAt = Time.unscaledTime + 3f;
        Changed?.Invoke();
        return true;
    }

    public bool Select(string optionId)
    {
        if (!Current) return false;
        var option = visibleOptions.Find(candidate => candidate.id == optionId);
        if (option == null || !IsAvailable(option.requirement)) return false;
        var npc = Current;
        Close();
        npc.onSelected?.Invoke(option.id);
        OptionSelected?.Invoke(npc, option.id);
        return true;
    }

    public void Close()
    {
        bool hadConversation = !ReferenceEquals(Current, null);
        Current = null; Nearby = null;
        visibleOptions.Clear();
        consumedFrame = Time.frameCount;
        if (hadConversation) Changed?.Invoke();
    }

    bool IsAvailable(DialogueRequirement requirement) => requirement == DialogueRequirement.Always ||
        (requirement == DialogueRequirement.ActiveConstruction && HasActiveConstruction()) ||
        (requirement == DialogueRequirement.OwnedBuilding && OwnedBuildingCount() > 0);

    static DialogueData FindNearest(Transform player)
    {
        DialogueData result = null;
        float best = float.PositiveInfinity;
        var camera = Camera.main;
        foreach (var npc in DialogueData.Active)
        {
            if (!npc || !npc.InRange(player)) continue;
            float distance = (npc.transform.position - player.position).sqrMagnitude;
            if (distance >= best) continue;
            if (camera)
            {
                Vector3 ray = npc.BubblePosition - camera.transform.position;
                bool blocked = false;
                foreach (var hit in Physics.RaycastAll(camera.transform.position, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(player) || hit.transform.IsChildOf(npc.transform)) continue;
                    blocked = true; break;
                }
                if (blocked) continue;
            }
            result = npc; best = distance;
        }
        return result;
    }

    void MapChanged(Scene oldScene, Scene newScene) => Close();
    void MapUnloaded(Scene scene) { if (Current && Current.gameObject.scene == scene) Close(); }
    protected override void OnDestroy()
    {
        SceneManager.activeSceneChanged -= MapChanged;
        SceneManager.sceneUnloaded -= MapUnloaded;
        if (Instance == this) Close();
        base.OnDestroy();
    }
}
