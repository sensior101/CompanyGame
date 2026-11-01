using System;
using System.Collections.Generic;
using CompanyGame.Daldongne;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Collision queries for ServerCombat. An in-app server uses its loaded map; other maps and a dedicated server use
/// script-free collision prefabs exported by CombatCollisionBuilder, in separate physics scenes. Never loads gameplay.
/// </summary>
public sealed class ServerCombatCollision : IDisposable
{
    public const string ResourceFolder = "CombatCollision";
    readonly Dictionary<string, Scene> maps = new Dictionary<string, Scene>(StringComparer.Ordinal);
    readonly Collider[] overlap = new Collider[1];
    bool disposed;
    static int serial;

    /// <summary>Stable resource name shared by the exporter; accepts full scene asset paths, not scene display names.</summary>
    public static string ResourceKey(string map)
    {
        map = (map ?? "").Replace('\\', '/');
        uint hash = 2166136261;
        foreach (char value in map) { hash ^= value; hash = unchecked(hash * 16777619); }
        string name = System.IO.Path.GetFileNameWithoutExtension(map);
        return ResourceFolder + "/" + name + "_" + hash.ToString("x8");
    }

    /// <summary>False means the map cannot be validated. True returns whether the complete swept segment is clear.</summary>
    public bool TryCheck(string map, Vector3 origin, Vector3 target, float radius, out bool clear)
    {
        clear = false;
        if (disposed || string.IsNullOrEmpty(map) || !Finite(origin) || !Finite(target) || !float.IsFinite(radius)) return false;
        map = map.Replace('\\', '/');
        radius = Mathf.Clamp(radius, .001f, 2f);
        Scene loaded = SceneManager.GetSceneByPath(map);
        if (loaded.IsValid() && loaded.isLoaded)
        {
            // The normal physics scene may hold several additive maps. Filter every hit by its actual scene.
            Physics.SyncTransforms();
            foreach (var collider in Physics.OverlapSphere(origin, radius, ~0, QueryTriggerInteraction.Ignore))
                if (Blocks(collider, loaded)) return true;
            foreach (var collider in Physics.OverlapSphere(target, radius, ~0, QueryTriggerInteraction.Ignore))
                if (Blocks(collider, loaded)) return true;
            Vector3 delta = target - origin;
            if (delta.sqrMagnitude > .000001f)
                foreach (var hit in Physics.SphereCastAll(origin, radius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    if (Blocks(hit.collider, loaded)) return true;
            clear = true;
            return true;
        }
        if (!TryGetPhysics(map, out var physics)) return false;
        if (physics.OverlapSphere(origin, radius, overlap, ~0, QueryTriggerInteraction.Ignore) != 0 ||
            physics.OverlapSphere(target, radius, overlap, ~0, QueryTriggerInteraction.Ignore) != 0) return true;
        Vector3 ray = target - origin;
        clear = ray.sqrMagnitude <= .000001f ||
            !physics.SphereCast(origin, radius, ray.normalized, out _, ray.magnitude, ~0, QueryTriggerInteraction.Ignore);
        return true;
    }

    static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    static bool Blocks(Collider collider, Scene scene) => collider && collider.gameObject.scene == scene && IsWorldObstacle(collider);

    /// <summary>Shared with the exporter. Character bodies are tested by ServerCombat, not treated as map walls.</summary>
    public static bool IsWorldObstacle(Collider collider)
    {
        if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger || collider is CharacterController) return false;
        for (Transform node = collider.transform; node; node = node.parent)
            if (node.GetComponent<PlayerMovement>() || node.GetComponent<AvatarState>() || node.GetComponent<DaldongneAvatarMotion>()) return false;
        return true;
    }

    bool TryGetPhysics(string map, out PhysicsScene physics)
    {
        physics = default;
        if (maps.TryGetValue(map, out var cached) && cached.IsValid() && cached.isLoaded)
        {
            physics = cached.GetPhysicsScene();
            return physics.IsValid();
        }
        var prefab = Resources.Load<GameObject>(ResourceKey(map));
        if (!prefab) return false;
        // Validate before Instantiate: importing a gameplay prefab here must never execute its scripts.
        foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            if (!component || !(component is Transform || component is Collider)) return false;
        if (prefab.activeSelf || prefab.GetComponentsInChildren<Collider>(true).Length == 0) return false;
        Scene scene = SceneManager.CreateScene("CombatCollision_" + ++serial, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        GameObject root = null;
        try
        {
            root = UnityEngine.Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.SetActive(true);
            Physics.SyncTransforms();
            physics = scene.GetPhysicsScene();
            if (!physics.IsValid()) throw new InvalidOperationException("Map collision physics scene unavailable.");
            maps[map] = scene;
            return true;
        }
        catch (Exception exception)
        {
            if (root) UnityEngine.Object.Destroy(root);
            if (scene.IsValid()) SceneManager.UnloadSceneAsync(scene);
            Debug.LogWarning("[CombatCollision] " + map + ": " + exception.Message);
            return false;
        }
    }

    public void End() => Dispose();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var scene in maps.Values)
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        maps.Clear();
    }
}
