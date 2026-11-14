using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Makes an ItemData for every library food that is not a store item yet (FoodMenu), with its icon, held model,
/// price and effects. Items go to Resources/Inventory/Food so saves find them; models stay in Resources/FoodModels and
/// load only when held (ItemData.heldResource). Unity does not pick up the textures embedded in the library FBX, so their
/// materials are remapped to shared URP materials made from the library's BaseColor/Normal maps.
/// Safe to run again: existing food items are updated in place.
/// Batch: Unity -batchmode -quit -projectPath ... -executeMethod FoodItemBuilder.Run [-foodPreview out.png]
/// </summary>
public static class FoodItemBuilder
{
    [Serializable] public class RecipeSource { public RecipeRow[] items; }
    [Serializable] public class RecipeRow { public string id,name,recipe;public bool recipeUnlockRequired; }
    static readonly Dictionary<string,string> IngredientIcons=new()
    {
        ["감자"]="Potato",["고구마"]="SweetPotato",["광어"]="Fish",["녹차잎"]="GreenTea",["달걀"]="Egg",["닭고기"]="ChickenLeg",
        ["당근"]="Carrot",["돼지고기"]="Bacon",["두부"]="Tofu",["레몬"]="Lemon",["망고"]="Mango",["물"]="Water",["밀가루"]="Flour",
        ["바나나"]="Banana",["바닐라"]="Vanilla",["바질"]="Basil",["배추"]="Cabbage",["버섯"]="Mushroom",["블루베리"]="Blueberry",
        ["새우"]="Shrimp",["설탕"]="Sugar",["소고기"]="Steak",["쌀"]="Rice",["연어"]="Salmon",["오렌지"]="Orange",["오이"]="Cucumber",
        ["옥수수"]="Corn",["우유"]="Milk",["자몽"]="Grapefruit",["장류"]="SoySauce",["장어"]="Eel",["참치"]="Tuna",["초콜릿"]="Chocolate",
        ["치즈"]="Cheese",["커피"]="Coffee",["향신료"]="Spice",["홍차잎"]="BlackTea"
    };
    [MenuItem("CompanyGame/Setup/Build Cooking Recipes")]
    public static string BuildCooking()
    {
        const string folder="Assets/Resources/Inventory/Ingredients";const string catalogFolder="Assets/Resources/Cooking";
        Directory.CreateDirectory(folder);Directory.CreateDirectory(catalogFolder);AssetDatabase.Refresh();
        string source=Path.GetFullPath(Path.Combine(Application.dataPath,"../../ArtSource/Items/Food/Crafted_20261012/menu.json"));
        var rows=JsonUtility.FromJson<RecipeSource>("{\"items\":"+File.ReadAllText(source)+"}").items;
        var foods=Resources.LoadAll<ItemData>("Inventory").Concat(TradeCatalog.Items()).Distinct().ToArray();
        var ingredients=new Dictionary<string,ItemData>();var recipes=new List<CookingCatalog.Recipe>();var missing=new List<string>();
        foreach(var row in rows)
        {
            var output=foods.FirstOrDefault(i=>FoodMenu.TryGet(i,out var entry)&&entry.id==row.id);
            if(!output){missing.Add(row.id);continue;}
            var parts=new List<CookingCatalog.Ingredient>();
            foreach(string part in row.recipe.Split('+'))
            {
                var match=Regex.Match(part.Trim(),@"^(.*?)(?:\s*×\s*(\d+))?$");string label=match.Groups[1].Value.Trim();int count=match.Groups[2].Success?int.Parse(match.Groups[2].Value):1;
                if(!ingredients.TryGetValue(label,out var item))
                {
                    string key=IngredientIcons[label];
                    item=label=="물"?InventorySnapshot.Find("food:water"):label=="초콜릿"?InventorySnapshot.Find("food:chocolate"):null;
                    if(!item)
                    {
                        string path=folder+"/"+key+".asset";item=AssetDatabase.LoadAssetAtPath<ItemData>(path);
                        if(!item){item=ScriptableObject.CreateInstance<ItemData>();AssetDatabase.CreateAsset(item,path);}
                        item.category=ItemCategory.Food;item.itemId="ingredient:"+Kebab(key);item.displayName=label;item.maxStack=20;item.consumableType=label=="우유"?ConsumableType.Drink:ConsumableType.Food;item.shelfLifeHours=72;
                        string iconPath="Assets/Art/Items/Food/Food_"+key+".png";
                        if(File.Exists(iconPath))item.icon=ImportIcon(iconPath);
                        EditorUtility.SetDirty(item);
                    }
                    ingredients[label]=item;
                }
                parts.Add(new CookingCatalog.Ingredient{item=item,count=count});
            }
            recipes.Add(new CookingCatalog.Recipe{id=row.id,output=output,ingredients=parts.ToArray(),unlockRequired=row.recipeUnlockRequired});
        }
        if(missing.Count>0)throw new InvalidOperationException("Missing cooked food items: "+string.Join(",",missing));
        const string catalogPath=catalogFolder+"/Catalog.asset";var catalog=AssetDatabase.LoadAssetAtPath<CookingCatalog>(catalogPath);
        if(!catalog){catalog=ScriptableObject.CreateInstance<CookingCatalog>();AssetDatabase.CreateAsset(catalog,catalogPath);}
        catalog.recipes=recipes.ToArray();catalog.allowUnlockRecipes=true;EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        return $"Recipes: {recipes.Count}; ingredients: {ingredients.Count}; all recipes enabled.";
    }
    const string IconFolder = "Assets/Art/Items/FoodLibrary/Icons";
    const string TextureFolder = "Assets/Art/Items/FoodLibrary/Textures";
    const string MaterialFolder = "Assets/Art/Items/FoodLibrary/Materials";
    const string ModelFolder = "Assets/Resources/FoodModels";
    const string ItemFolder = "Assets/Resources/Inventory/Food";
    // Largest side of a held food, metres: real size when it is in this range.
    const float MinHeld = .12f, MaxHeld = .26f;

    // The library's materials as build_crafted.py material() makes them: FoodV2_<surface>_<hex>, roughness .88,
    // normal strength .2, V2_<surface>_<hex>_BaseColor / V2_<surface>_Normal maps except on the flat surfaces.
    static readonly Regex LibraryMaterialName = new Regex(@"^FoodV2_(\w+)_([0-9a-fA-F]{6})$");
    const float Smoothness = .12f, NormalStrength = .2f;
    static readonly HashSet<string> FlatSurfaces = new HashSet<string>
    {
        "glass", "glassrim", "ricepaper", "ice", "studio", "silver", "label", "blue", "packpink",
        "whiteplate", "ceramic", "rim", "metal", "cream", "rice", "egg",
    };
    static readonly Dictionary<string, float> SurfaceAlpha = new Dictionary<string, float>
    {
        ["glass"] = .10f, ["teagold"] = .78f, ["teaamber"] = .78f, ["teagreen"] = .78f,
        ["waterglass"] = .32f, ["waterliquid"] = .25f, ["ice"] = .65f, ["ricepaper"] = .27f,
    };
    static string TextureSource => Path.GetFullPath(Path.Combine(Application.dataPath, "../../ArtSource/Items/Food/Crafted_20261012/Textures"));

    [MenuItem("CompanyGame/Setup/Build Food Items")]
    public static void Run()
    {
        // Made on disk before the refresh, so the folders are imported when assets are created in them.
        foreach (string folder in new[] { ItemFolder, TextureFolder, MaterialFolder }) Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        var taken = new Dictionary<string, string>();
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var existing = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (existing && !string.IsNullOrEmpty(existing.itemId)) taken[existing.itemId] = path;
        }
        int made = 0, updated = 0;
        var missing = new List<string>();
        var built = new List<ItemData>();
        var materials = new Dictionary<string, Material>();
        foreach (var food in FoodMenu.All)
        {
            if (food.category == FoodMenu.Category.Convenience) continue; // the store's ten already exist
            var icon = ImportIcon($"{IconFolder}/{food.id}.png");
            var model = ImportModel($"{ModelFolder}/Food_{food.id}.fbx", materials);
            if (!icon || !model) { missing.Add(food.id + (icon ? "" : " (icon)") + (model ? "" : " (model)")); continue; }

            string path = $"{ItemFolder}/{food.id}.asset";
            string itemId = food.itemId ?? "food:" + Kebab(food.id);
            if (taken.TryGetValue(itemId, out string owner) && owner != path)
                throw new InvalidOperationException($"{itemId} is already used by {owner}");
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            bool isNew = !item;
            if (isNew) { item = ScriptableObject.CreateInstance<ItemData>(); AssetDatabase.CreateAsset(item, path); }
            item.category = ItemCategory.Food;
            item.itemId = itemId;
            item.displayName = food.name;
            item.icon = icon;
            item.maxStack = 20;
            item.heldPrefab = null;
            item.heldResource = "FoodModels/Food_" + food.id;
            // The hand replaces the model root's own transform, which holds the FBX's centimetre scale and Z-up turn,
            // so the held pose carries both, scaled to fit the hand.
            Vector3 size = ModelSize(model);
            float side = Mathf.Max(size.x, size.y, size.z, .001f);
            item.heldLocalScale = model.transform.localScale * (Mathf.Clamp(side, MinHeld, MaxHeld) / side);
            item.heldLocalPosition = new Vector3(0f, .09f, 0f); // the store foods' grip height
            item.heldLocalEulerAngles = model.transform.localEulerAngles;
            FoodDataSync.Apply(item, food);
            EditorUtility.SetDirty(item);
            built.Add(item);
            if (isNew) made++; else updated++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[FoodItemBuilder] {made} food items made, {updated} updated, {materials.Values.Count(m => m)} materials" +
            (missing.Count > 0 ? $"; skipped (no file): {string.Join(", ", missing)}" : "."));
        string preview = Environment.GetCommandLineArgs().SkipWhile(a => a != "-foodPreview").Skip(1).FirstOrDefault();
        if (!string.IsNullOrEmpty(preview)) Preview(built, preview);
    }

    static Sprite ImportIcon(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) return null;
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single ||
            importer.mipmapEnabled || importer.maxTextureSize != 256)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // No animation, cameras or lights, and every library material remapped to its shared one: at most one reimport.
    static GameObject ImportModel(string path, Dictionary<string, Material> materials)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!importer || !model) return null;
        bool changed = importer.importAnimation || importer.animationType != ModelImporterAnimationType.None ||
            importer.importCameras || importer.importLights;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        var remapped = importer.GetExternalObjectMap();
        var names = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name).Distinct().ToArray();
        foreach (string name in names)
        {
            if (!materials.TryGetValue(name, out var material)) materials[name] = material = LibraryMaterial(name);
            var source = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
            if (!material || remapped.TryGetValue(source, out var current) && current == material) continue;
            importer.AddRemap(source, material);
            changed = true;
        }
        if (!changed) return model;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    static Material LibraryMaterial(string name)
    {
        var match = LibraryMaterialName.Match(name);
        if (!match.Success) { Debug.LogWarning($"[FoodItemBuilder] {name} is not a library material; left as imported."); return null; }
        string surface = match.Groups[1].Value, hex = match.Groups[2].Value;
        string path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        bool flat = FlatSurfaces.Contains(surface);
        var albedo = flat ? null : LibraryTexture($"V2_{surface}_{hex}_BaseColor.png", false);
        var normal = flat ? null : LibraryTexture($"V2_{surface}_Normal.png", true);
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        if (albedo) color = Color.white; // the map carries the colour
        color.a = SurfaceAlpha.TryGetValue(surface, out float alpha) ? alpha : 1f;
        material.SetColor("_BaseColor", color);
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", NormalStrength);
        if (normal) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
        material.SetFloat("_Smoothness", Smoothness);
        material.SetFloat("_Metallic", 0f);
        if (color.a < 1f)
        {
            material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = (int)RenderQueue.Transparent;
        }
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    // Copied in from the art source the first time a material needs it.
    static Texture2D LibraryTexture(string file, bool normalMap)
    {
        string path = $"{TextureFolder}/{file}";
        if (!File.Exists(path))
        {
            string source = Path.Combine(TextureSource, file);
            if (!File.Exists(source)) return null;
            File.Copy(source, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (normalMap && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Size as imported, root transform included: a one-mesh FBX keeps its unit scale and axis turn on the root.
    static Vector3 ModelSize(GameObject model)
    {
        var bounds = new Bounds(); bool any = false;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.sharedMesh) continue;
            var b = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var p = filter.transform.TransformPoint(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
            }
        }
        return bounds.size;
    }

    static string Kebab(string id)
    {
        var s = new System.Text.StringBuilder();
        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            bool boundary = i > 0 && char.IsUpper(c) && (char.IsLower(id[i - 1]) || i + 1 < id.Length && char.IsLower(id[i + 1]));
            if (boundary) s.Append('-');
            s.Append(char.ToLowerInvariant(c));
        }
        return s.ToString();
    }

    // Batch check: every new item posed as the hand poses it (held transform in place of the model root's), in rows of 13, written to a PNG.
    static void Preview(List<ItemData> items, string path)
    {
        var root = new GameObject("FoodPreview");
        var cameraObject = new GameObject("FoodPreviewCamera"); var cam = cameraObject.AddComponent<Camera>();
        var light = new GameObject("FoodPreviewLight").AddComponent<Light>();
        // In batch mode no frame runs to upload per-material data, so the SRP Batcher draws every material with one
        // material's colour and maps; render the preview without it.
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        bool batcher = pipeline && pipeline.useSRPBatcher;
        if (pipeline) pipeline.useSRPBatcher = false;
        try
        {
            light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f, .55f, .55f);
            const int Columns = 13; const float Step = .32f;
            for (int i = 0; i < items.Count; i++)
            {
                var model = items[i].HeldPrefab;
                if (!model) continue;
                var copy = (GameObject)UnityEngine.Object.Instantiate(model, root.transform);
                copy.transform.localPosition = new Vector3(i % Columns * Step, 0f, -(i / Columns) * Step);
                copy.transform.localRotation = Quaternion.Euler(items[i].heldLocalEulerAngles);
                copy.transform.localScale = items[i].heldLocalScale;
            }
            int rows = (items.Count + Columns - 1) / Columns;
            var center = new Vector3((Columns - 1) * Step * .5f, 0f, -(rows - 1) * Step * .5f);
            cam.orthographic = true; cam.orthographicSize = Mathf.Max(rows, Columns * .56f) * Step * .5f + .1f;
            cam.transform.position = center + new Vector3(0f, 6f, -6f); cam.transform.LookAt(center);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.86f, .8f, .71f);
            var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
        finally
        {
            if (pipeline) pipeline.useSRPBatcher = batcher;
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(light.gameObject);
        }
    }
}
