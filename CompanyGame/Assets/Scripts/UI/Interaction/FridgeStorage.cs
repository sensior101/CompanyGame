using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A fridge that holds food. Any object named Fridge / Kitchen_Fridge (the furniture prefabs) gets one at scene load.
/// Food inside keeps for its FoodShelfLife days. Contents are saved on this device per scene and object path.
/// </summary>
[DisallowMultipleComponent]
public sealed class FridgeStorage : MonoBehaviour
{
    public const int Slots = InventoryState.BaseCapacity;
    [Min(.5f)] public float interactionRadius = 2f;

    static readonly HashSet<FridgeStorage> active = new HashSet<FridgeStorage>();
    InventoryState contents;
    string saveKey;

    public InventoryState Contents
    {
        get
        {
            if (contents != null) return contents;
            contents = new InventoryState();
            saveKey = "fridge:" + gameObject.scene.name + ":" + PathOf(transform);
            InventorySnapshot.Import(contents, PlayerPrefs.GetString(saveKey, ""));
            contents.Changed += Save;
            return contents;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => active.Clear();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) Attach(SceneManager.GetSceneAt(i));
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

    static void Attach(Scene scene)
    {
        if (!scene.isLoaded) return;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (IsFridgeName(t.name) && !t.GetComponentInParent<FridgeStorage>(true)) t.gameObject.AddComponent<FridgeStorage>();
    }

    // The furniture prefabs' roots; parts like Fridge_Door sit under one and are skipped by the parent check.
    static bool IsFridgeName(string name) =>
        name.StartsWith("Fridge", System.StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Kitchen_Fridge", System.StringComparison.OrdinalIgnoreCase);

    void OnEnable() => active.Add(this);
    void OnDisable() => active.Remove(this);

    void Save()
    {
        if (contents == null) return;
        PlayerPrefs.SetString(saveKey, InventorySnapshot.Export(contents));
        PlayerPrefs.Save();
    }

    public static FridgeStorage FindNearest(Transform player)
    {
        FridgeStorage best = null; float bestDistance = float.MaxValue;
        foreach (var fridge in active)
        {
            if (!fridge) continue;
            Vector3 offset = fridge.transform.position - player.position; offset.y = 0f;
            float distance = offset.magnitude;
            if (distance <= fridge.interactionRadius && distance < bestDistance) { best = fridge; bestDistance = distance; }
        }
        return best;
    }

    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name + t.GetSiblingIndex() : t.name;
}
