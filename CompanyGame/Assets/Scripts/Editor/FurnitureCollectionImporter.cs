using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Imports the separated user collections without modifying any gameplay scene.</summary>
public static class FurnitureCollectionImporter
{
    [Serializable] public class Catalog { public Entry[] items; }
    [Serializable] public class Entry
    {
        public string pack, key, label, functions, storage, placement, modelPath, note;
        public int vertices, triangles;
    }
    const string Root = "Assets/Gameplay/Item/Furniture";
    [MenuItem("CompanyGame/Furniture/Import separated collections")]
    public static void Import()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before importing.");
        var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../ArtSource/FurnitureCollections/catalog.json"))));
        var preview = EditorSceneManager.NewPreviewScene();
        int complete = 0;
        try
        {
            foreach (var row in catalog.items)
            {
                var functions = (FurnitureFunction)Enum.Parse(typeof(FurnitureFunction), row.functions);
                var storage = (StorageType)Enum.Parse(typeof(StorageType), row.storage);
                var placement = (FurniturePlacement)Enum.Parse(typeof(FurniturePlacement), row.placement);
                string category = (functions & FurnitureFunction.Sleep) != 0 ? "Sleep" : row.functions;
                if (category == "Storage") category += "/" + storage;
                string folder = Root + "/" + category + "/" + row.pack;
                EnsureFolder(folder);
                AssetDatabase.ImportAsset(row.modelPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(row.modelPath) as ModelImporter;
                if (!importer) throw new InvalidOperationException("Missing model: " + row.modelPath);
                importer.importAnimation = false; importer.addCollider = false; importer.isReadable = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(row.modelPath);
                var item = AssetDatabase.LoadAssetAtPath<ItemData>(folder + "/" + row.key + ".asset");
                if (!item) { item = ScriptableObject.CreateInstance<ItemData>(); AssetDatabase.CreateAsset(item, folder + "/" + row.key + ".asset"); }
                item.category = ItemCategory.Furniture;
                item.itemId = "furniture:" + row.pack.ToLowerInvariant() + ":" + row.key.ToLowerInvariant();
                item.displayName = row.label; item.furnitureFunctions = functions; item.storageType = storage;
                item.furniturePlacement = placement; item.maxStack = 1;
                var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "/" + row.key + ".mat");
                if (!material)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    material.SetColor("_BaseColor", new Color(.65f,.65f,.65f)); material.SetFloat("_Smoothness", .25f);
                    AssetDatabase.CreateAsset(material, folder + "/" + row.key + ".mat");
                }
                var root = new GameObject(row.label);
                SceneManager.MoveGameObjectToScene(root, preview);
                try
                {
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    var renderers = visual.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0) throw new InvalidOperationException("Empty model: " + row.modelPath);
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers)
                    {
                        bounds.Encapsulate(renderer.bounds);
                        renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    }
                    visual.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                    bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    var collider = root.AddComponent<BoxCollider>(); collider.center = bounds.center; collider.size = bounds.size;
                    var world = root.AddComponent<WorldObject>(); world.objectType = WorldObjectType.PlaceableFurniture;
                    world.functions = functions; world.furnitureItem = item;
                    // Author classification only; no unrequested sleep, storage, cooking or placement gameplay.
                    item.furniturePrefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/" + row.key + ".prefab");
                    if (!item.furniturePrefab) throw new InvalidOperationException("Prefab save failed: " + row.key);
                    EditorUtility.SetDirty(item);
                    string[] labels = new[] { "Furniture", row.pack, row.functions.Replace(", ", "+"), storage.ToString(), placement.ToString() };
                    AssetDatabase.SetLabels(item, labels); AssetDatabase.SetLabels(item.furniturePrefab, labels);
                    complete++;
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); AssetDatabase.SaveAssets(); }
        Debug.Log("Furniture collection import complete: " + complete);
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
    public static string Validate()
    {
        var failures = new List<string>(); int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData", new[] { Root }))
        {
            if (AssetDatabase.GUIDToAssetPath(guid).StartsWith(Root + "/Themes/")) continue;
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid)); count++;
            if (item.itemType != ItemType.Furniture || !item.furniturePrefab) { failures.Add(item.name + ": missing prefab/classification"); continue; }
            var world = item.furniturePrefab.GetComponent<WorldObject>();
            if (!world || world.furnitureItem != item || world.functions != item.furnitureFunctions) failures.Add(item.name + ": inconsistent metadata");
            foreach (var filter in item.furniturePrefab.GetComponentsInChildren<MeshFilter>()) if (!filter.sharedMesh || filter.sharedMesh.vertexCount == 0) failures.Add(item.name + ": empty mesh");
            foreach (var renderer in item.furniturePrefab.GetComponentsInChildren<Renderer>()) if (renderer.sharedMaterials.Any(m => !m || m.shader.name != "Universal Render Pipeline/Lit")) failures.Add(item.name + ": invalid URP material");
        }
        string report = "Items/prefabs: " + count + "; failures: " + failures.Count + "\n" + string.Join("\n", failures);
        File.WriteAllText(Path.Combine(Application.dataPath, "../Docs/FurnitureCollections/UnityValidation.txt"), report);
        if (failures.Count > 0 || count != 61) throw new InvalidOperationException(report);
        return report;
    }
}
