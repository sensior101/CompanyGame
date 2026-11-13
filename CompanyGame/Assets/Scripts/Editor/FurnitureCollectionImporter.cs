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
    const string Household = "Assets/Art/Items/Furniture/Household";
    const string HouseholdItems = "Assets/Resources/Inventory/Furniture/Household";
    static string Integration => Path.GetFullPath(Path.Combine(Application.dataPath, "../../ArtSource/Items/Furniture/HouseholdLowPoly/UnityIntegration"));
    [Serializable] public class HouseholdCatalog { public HouseholdEntry[] items; public Surface[] materials; }
    [Serializable] public class HouseholdEntry { public string key, style, label, functions, placement; public int triangles; public Part[] parts; }
    [Serializable] public class Part { public string name; public string[] materials; }
    [Serializable] public class Surface { public string name, texture; public float[] color, emission; public float metallic, roughness; public bool transparent; }

    [MenuItem("CompanyGame/Furniture/Import household 60 x 4")]
    public static string ImportHousehold()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var catalog = JsonUtility.FromJson<HouseholdCatalog>(File.ReadAllText(Path.Combine(Integration, "ImportManifest.json")));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EnsureFolder(Household + "/Materials");
        var surfaces = new Dictionary<string, Material>();
        foreach (var row in catalog.materials)
        {
            string path = Household + "/Materials/" + row.name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", new Color(row.color[0], row.color[1], row.color[2], row.color[3]).gamma);
            mat.SetFloat("_Metallic", row.metallic); mat.SetFloat("_Smoothness", 1f - row.roughness);
            mat.SetFloat("_Cull", 0);
            mat.SetTexture("_BaseMap", string.IsNullOrEmpty(row.texture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(Household + "/Textures/" + row.texture + ".png"));
            if (row.emission != null && row.emission.Any(x => x > 0)) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(row.emission[0],row.emission[1],row.emission[2])); }
            if (row.transparent)
            {
                mat.SetFloat("_Surface", 1); mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); mat.SetFloat("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000;
            }
            EditorUtility.SetDirty(mat); surfaces[row.name] = mat;
        }
        var preview = EditorSceneManager.NewPreviewScene(); int count = 0;
        try
        {
            foreach (var row in catalog.items)
            {
                string modelPath = Household + "/Models/" + row.style + "/" + row.key + "_" + row.style + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
                importer.importAnimation = false; importer.addCollider = false; importer.isReadable = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None; importer.SaveAndReimport();
                string icons = Household + "/Icons/" + row.style + "/" + row.key + ".png";
                var iconImport = (TextureImporter)AssetImporter.GetAtPath(icons);
                iconImport.textureType = TextureImporterType.Sprite; iconImport.spriteImportMode = SpriteImportMode.Single;
                iconImport.maxTextureSize = 512; iconImport.mipmapEnabled = false; iconImport.SaveAndReimport();
                string itemFolder = HouseholdItems + "/" + row.style, prefabFolder = Household + "/Prefabs/" + row.style;
                EnsureFolder(itemFolder); EnsureFolder(prefabFolder);
                string itemPath = itemFolder + "/" + row.key + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<ItemData>(itemPath);
                if (!item) { item = ScriptableObject.CreateInstance<ItemData>(); AssetDatabase.CreateAsset(item, itemPath); }
                item.category = ItemCategory.Furniture; item.itemId = "furniture:household:" + row.style.ToLowerInvariant() + ":" + row.key.ToLowerInvariant();
                item.displayName = row.label + " · " + StyleLabel(row.style); item.maxStack = 1;
                item.furnitureFunctions = Enum.Parse<FurnitureFunction>(row.functions); item.furniturePlacement = Enum.Parse<FurniturePlacement>(row.placement);
                item.furnitureTheme = row.style == "WhiteWood" ? FurnitureTheme.WhiteWood : row.style == "ChicBlack" ? FurnitureTheme.AllBlack : row.style == "MarbleGold" ? FurnitureTheme.Luxury : FurnitureTheme.Cute;
                item.storageType = (item.furnitureFunctions & FurnitureFunction.Storage) == 0 ? StorageType.None :
                    row.key == "Wardrobe" || row.key == "LaundryBasket" || row.key == "ShoeCabinet" ? StorageType.Clothing :
                    row.key == "Fridge" || row.key == "MiniFridge" ? StorageType.Food : row.key == "Bookcase" ? StorageType.Books : StorageType.General;
                item.icon = AssetDatabase.LoadAssetAtPath<Sprite>(icons);
                var root = new GameObject(row.key + "_" + row.style); SceneManager.MoveGameObjectToScene(root, preview);
                try
                {
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), root.transform);
                    var renderers = visual.GetComponentsInChildren<MeshRenderer>();
                    foreach (var r in renderers)
                    {
                        var part = row.parts.FirstOrDefault(p => p.name == r.name);
                        if (part == null) throw new InvalidOperationException("Unmapped part " + row.key + "/" + r.name);
                        r.sharedMaterials = part.materials.Select(n => surfaces[n]).ToArray();
                        // The actual low-poly surfaces preserve openings in chairs, cabinets and sanitary fixtures.
                        var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                        r.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                    }
                    var world = root.AddComponent<WorldObject>(); world.objectType = WorldObjectType.PlaceableFurniture;
                    world.functions = item.furnitureFunctions; world.furnitureItem = item;
                    if (item.storageType != StorageType.None)
                    {
                        var storage = root.AddComponent<FridgeStorage>(); storage.storageType = item.storageType; storage.refrigerated = item.storageType == StorageType.Food;
                    }
                    if (world.HasFunction(FurnitureFunction.Seating)) AddHouseholdSeats(root, world, row.key, renderers);
                    item.furniturePrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabFolder + "/" + row.key + ".prefab");
                    EditorUtility.SetDirty(item); AssetDatabase.SetLabels(item, new[] { "Household", row.style, row.key }); count++;
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); AssetDatabase.SaveAssets(); }
        string result = "Household items/prefabs imported: " + count;
        File.WriteAllText(Path.Combine(Integration, "ImportResult.txt"), result); return result;
    }
    static string StyleLabel(string style) => style == "WhiteWood" ? "화이트 앤 우드" : style == "Eclectic" ? "컬러 믹스" : style == "MarbleGold" ? "대리석 · 골드" : "시크 블랙";
    static void AddHouseholdSeats(GameObject root, WorldObject world, string key, MeshRenderer[] renderers)
    {
        var seats = renderers.Where(r => r.name == "Seat" || r.name == "MoldedSeat" || r.name == "RoundSeat" || r.name == "SeatPad" || r.name == "BucketSeatPad" ||
            r.name == "FittedSeatCushion" || r.name.StartsWith("SeatCushion",StringComparison.Ordinal) || r.name == "ChaiseCushion").ToArray();
        int count = key == "Sofa2" ? 2 : key == "Sofa3" ? 3 : key == "CornerSofa" ? 4 : 1;
        Bounds bounds = seats.Length > 0 ? seats[0].bounds : new Bounds(new Vector3(0,.45f,0),new Vector3(.5f,.05f,.5f));
        foreach (var r in seats) bounds.Encapsulate(r.bounds);
        for (int i = 0; i < count; i++)
        {
            var anchor = new GameObject("Seat_" + (i + 1)); anchor.transform.SetParent(root.transform, false);
            Vector3 position;
            if (seats.Length == count) position = new Vector3(seats[i].bounds.center.x,seats[i].bounds.max.y,seats[i].bounds.center.z);
            else position = new Vector3(count == 1 ? bounds.center.x : bounds.min.x + bounds.size.x * (i + .5f) / count, bounds.max.y, bounds.center.z);
            anchor.transform.position = position; var seat = anchor.AddComponent<Seat>(); seat.owner = world;
        }
    }
    public static string RefreshHouseholdSeats()
    {
        int count=0;
        foreach(string guid in AssetDatabase.FindAssets("t:ItemData",new[]{HouseholdItems}))
        {
            var item=AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if((item.furnitureFunctions&FurnitureFunction.Seating)==0)continue;
            string path=AssetDatabase.GetAssetPath(item.furniturePrefab);var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(var seat in root.GetComponentsInChildren<Seat>()) UnityEngine.Object.DestroyImmediate(seat.gameObject);
                AddHouseholdSeats(root,root.GetComponent<WorldObject>(),item.name,root.GetComponentsInChildren<MeshRenderer>());
                PrefabUtility.SaveAsPrefabAsset(root,path);count++;
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        return "Seating prefabs updated: "+count;
    }

    [MenuItem("CompanyGame/Furniture/Bake storage locations")]
    public static string BakeStorageLocations()
    {
        var locations = new List<FurnitureStorageRules.Location>();
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid); var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.gameObject.activeInHierarchy) continue;
                    var storage = t.GetComponent<FridgeStorage>(); var world = t.GetComponent<WorldObject>();
                    if (storage && !storage.enabled) continue;
                    bool legacy = t.name.StartsWith("Fridge",StringComparison.OrdinalIgnoreCase) || t.name.StartsWith("Kitchen_Fridge",StringComparison.OrdinalIgnoreCase);
                    if (world && world.HasFunction(FurnitureFunction.Cooking)) locations.Add(new FurnitureStorageRules.Location { id = world.InteractionId, map = path, label = world.name, position = t.position, functions = FurnitureFunction.Cooking, radius = 2f });
                    if (!storage && !legacy && !(world && world.HasFunction(FurnitureFunction.Storage))) continue;
                    if (t.parent && t.parent.GetComponentInParent<FridgeStorage>(true)) continue;
                    if (!storage)
                    {
                        storage = t.gameObject.AddComponent<FridgeStorage>();
                        if (world && world.furnitureItem) { storage.storageType = world.furnitureItem.storageType == StorageType.None ? StorageType.General : world.furnitureItem.storageType; storage.refrigerated = storage.storageType == StorageType.Food; }
                    }
                    locations.Add(new FurnitureStorageRules.Location { id = storage.Id, map = path, label = storage.Label, position = t.position, type = storage.storageType, refrigerated = storage.refrigerated, radius = storage.interactionRadius });
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        if (locations.Select(x => x.id).Distinct().Count() != locations.Count) throw new InvalidOperationException("Duplicate storage IDs");
        EnsureFolder("Assets/Resources/Furniture");
        File.WriteAllText("Assets/Resources/Furniture/StorageLocations.json",JsonUtility.ToJson(new FurnitureStorageRules.Locations { items = locations.ToArray() },true));
        AssetDatabase.ImportAsset("Assets/Resources/Furniture/StorageLocations.json"); return "Storage locations: " + locations.Count;
    }
    [InitializeOnLoadMethod]
    static void WatchStorageScenes()
    {
        EditorSceneManager.sceneSaving -= AssignStorageIds; EditorSceneManager.sceneSaving += AssignStorageIds;
        EditorSceneManager.sceneSaved -= StorageSceneSaved; EditorSceneManager.sceneSaved += StorageSceneSaved;
    }
    static void AssignStorageIds(Scene scene, string path)
    {
        if (Application.isPlaying || EditorSceneManager.IsPreviewScene(scene)) return;
        var seen = new HashSet<string>();
        foreach (var root in scene.GetRootGameObjects()) foreach (var storage in root.GetComponentsInChildren<FridgeStorage>(true))
        {
            if (!string.IsNullOrEmpty(storage.storageId) && seen.Add(storage.storageId)) continue;
            storage.storageId = "storage:" + Guid.NewGuid().ToString("N"); seen.Add(storage.storageId);
            EditorUtility.SetDirty(storage); PrefabUtility.RecordPrefabInstancePropertyModifications(storage);
        }
    }
    static void StorageSceneSaved(Scene scene)
    {
        if (Application.isPlaying || EditorSceneManager.IsPreviewScene(scene) || !scene.path.StartsWith("Assets/Scenes/")) return;
        // Defer until scene serialization ends. Server reads the same authoritative map/position/type catalog.
        EditorApplication.delayCall -= RefreshStorageCatalog;
        EditorApplication.delayCall += RefreshStorageCatalog;
    }
    static void RefreshStorageCatalog() { if (!EditorApplication.isPlayingOrWillChangePlaymode) BakeStorageLocations(); }
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
