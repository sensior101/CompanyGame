using System;
using System.Collections.Generic;
using System.IO;
using CompanyGame.Daldongne;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Exports only world collider geometry. Source scenes and authored mesh assets are never saved or modified.
/// Rebuild after changing map geometry, before distributing a server build.
/// </summary>
public static class CombatCollisionBuilder
{
    const string Folder = "Assets/Resources/" + ServerCombatCollision.ResourceFolder;
    [Serializable] public sealed class Entry { public string map, prefab, sourceHash; public int colliders, transforms; }
    [Serializable] public sealed class Report { public List<Entry> maps = new List<Entry>(); public int colliderCount; }
    public static Report LastReport { get; private set; }

    [MenuItem("CompanyGame/Combat/Build Server Map Collisions")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build server map collisions outside Play Mode.");
        EnsureFolder(Folder);
        var report = new Report();
        foreach (var item in EditorBuildSettings.scenes)
        {
            if (!item.enabled || string.IsNullOrEmpty(item.path) || Path.GetFileNameWithoutExtension(item.path) == PlayerSpawner.TitleSceneName) continue;
            var entry = BuildMap(item.path);
            report.maps.Add(entry);
            report.colliderCount += entry.colliders;
        }
        if (report.maps.Count == 0) throw new InvalidOperationException("No enabled gameplay maps to export.");
        string manifest = Folder + "/manifest.json";
        File.WriteAllText(manifest, JsonUtility.ToJson(report, true) + "\n");
        AssetDatabase.ImportAsset(manifest);
        LastReport = report;
        Debug.Log("[CombatCollisionBuilder] exported " + report.maps.Count + " maps, " + report.colliderCount + " colliders; source scenes unchanged.");
    }

    public static Entry BuildMap(string path)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Cannot export a running gameplay scene.");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new FileNotFoundException("Map scene missing.", path);
        EnsureFolder(Folder);
        // Preview scenes do not replace/open/close the user's scene setup and do not invoke runtime scene loaders.
        Scene source = EditorSceneManager.OpenPreviewScene(path);
        Scene output = default;
        try
        {
            output = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("ServerCollision_" + Path.GetFileNameWithoutExtension(path));
            SceneManager.MoveGameObjectToScene(root, output);
            var copied = new Dictionary<Transform, Transform>();
            var copiedMeshes = new Dictionary<Mesh, Mesh>();
            int count = 0;
            foreach (var sourceRoot in source.GetRootGameObjects())
            {
                foreach (var collider in sourceRoot.GetComponentsInChildren<Collider>(true))
                {
                    if (!ServerCombatCollision.IsWorldObstacle(collider)) continue;
                    if (collider.attachedRigidbody && !collider.attachedRigidbody.isKinematic) continue;
                    if (!(collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider || collider is MeshCollider || collider is TerrainCollider))
                        throw new NotSupportedException("Unsupported map collider: " + path + "/" + collider.name + " (" + collider.GetType().Name + ")");
                    var node = CopyTransform(collider.transform, root.transform, copied);
                    CopyCollider(collider, node.gameObject, path, count, copiedMeshes);
                    count++;
                }
            }
            if (count == 0) throw new InvalidOperationException("No static collision geometry in " + path);
            root.SetActive(false); // runtime moves the inactive copy to its own physics scene before activation
            string target = "Assets/Resources/" + ServerCombatCollision.ResourceKey(path) + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, target, out bool success);
            if (!success) throw new IOException("Failed to save " + target);
            return new Entry { map = path, prefab = target, sourceHash = AssetDatabase.GetAssetDependencyHash(path).ToString(), colliders = count, transforms = copied.Count };
        }
        finally
        {
            if (output.IsValid()) EditorSceneManager.ClosePreviewScene(output);
            if (source.IsValid()) EditorSceneManager.ClosePreviewScene(source);
        }
    }

    static Transform CopyTransform(Transform source, Transform root, Dictionary<Transform, Transform> copied)
    {
        if (copied.TryGetValue(source, out var existing)) return existing;
        var parent = source.parent ? CopyTransform(source.parent, root, copied) : root;
        var node = new GameObject(source.name).transform;
        node.SetParent(parent, false);
        node.localPosition = source.localPosition;
        node.localRotation = source.localRotation;
        node.localScale = source.localScale;
        node.gameObject.layer = source.gameObject.layer;
        copied[source] = node;
        return node;
    }

    static void CopyCollider(Collider source, GameObject node, string scenePath, int index, Dictionary<Mesh, Mesh> copiedMeshes)
    {
        Collider copy;
        if (source is BoxCollider box)
        {
            var value = node.AddComponent<BoxCollider>(); value.center = box.center; value.size = box.size; copy = value;
        }
        else if (source is SphereCollider sphere)
        {
            var value = node.AddComponent<SphereCollider>(); value.center = sphere.center; value.radius = sphere.radius; copy = value;
        }
        else if (source is CapsuleCollider capsule)
        {
            var value = node.AddComponent<CapsuleCollider>(); value.center = capsule.center; value.radius = capsule.radius;
            value.height = capsule.height; value.direction = capsule.direction; copy = value;
        }
        else if (source is MeshCollider mesh)
        {
            if (!mesh.sharedMesh) throw new InvalidOperationException("Missing map collision mesh: " + source.name);
            var value = node.AddComponent<MeshCollider>();
            value.cookingOptions = mesh.cookingOptions;
            value.convex = mesh.convex;
            Mesh geometry = mesh.sharedMesh;
            string geometryPath = AssetDatabase.GetAssetPath(geometry);
            if (!EditorUtility.IsPersistent(geometry) || geometryPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                // Some scene-generated meshes are embedded in the scene. Copy into our generated folder, never edit the source.
                if (!copiedMeshes.TryGetValue(geometry, out var persistent))
                {
                    string directory = "Assets/Resources/" + ServerCombatCollision.ResourceKey(scenePath) + "_Meshes";
                    EnsureFolder(directory);
                    string meshPath = directory + "/mesh_" + index + ".asset";
                    persistent = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (persistent) { EditorUtility.CopySerialized(geometry, persistent); EditorUtility.SetDirty(persistent); }
                    else { persistent = UnityEngine.Object.Instantiate(geometry); AssetDatabase.CreateAsset(persistent, meshPath); }
                    AssetDatabase.SaveAssetIfDirty(persistent);
                    copiedMeshes[geometry] = persistent;
                }
                geometry = persistent;
            }
            value.sharedMesh = geometry; copy = value;
        }
        else if (source is TerrainCollider terrain)
        {
            if (!terrain.terrainData || !EditorUtility.IsPersistent(terrain.terrainData))
                throw new InvalidOperationException("Terrain collision needs a saved TerrainData asset: " + source.name);
            var value = node.AddComponent<TerrainCollider>(); value.terrainData = terrain.terrainData; copy = value;
        }
        else throw new NotSupportedException(source.GetType().Name);
        copy.sharedMaterial = source.sharedMaterial;
        copy.contactOffset = source.contactOffset;
        copy.isTrigger = false;
        copy.enabled = true;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
