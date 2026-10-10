// Editor recipe only: invoke with the existing Unity CLI eval_file workflow.
// Authoring assets and existing WorldObject are reused; no runtime kiosk controller.
if (EditorApplication.isPlaying || EditorApplication.isCompiling)
    throw new Exception("Integrate in idle Edit Mode.");
const string scenePath = "Assets/Scenes/Interiors/CivicLibraryInterior.unity";
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.path != scenePath) throw new Exception("Open CivicLibraryInterior before integration.");
var source = System.IO.Path.GetFullPath("../ArtSource/Items/Furniture/LibraryKiosk");
const string art = "Assets/Art/Items/Furniture/LibraryKiosk";
void Folder(string path)
{
    if (AssetDatabase.IsValidFolder(path)) return;
    string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
    Folder(parent); AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
}
foreach (var folder in new[] { "Models", "Textures", "Materials", "Prefabs" }) Folder(art + "/" + folder);
System.IO.File.Copy(source + "/Exports/LibraryKiosk.fbx", art + "/Models/LibraryKiosk.fbx", true);
System.IO.File.Copy(source + "/Exports/Textures/Kiosk_OakGrain.png", art + "/Textures/Kiosk_OakGrain.png", true);
AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
var textureImporter = (TextureImporter)AssetImporter.GetAtPath(art + "/Textures/Kiosk_OakGrain.png");
textureImporter.textureType = TextureImporterType.Default;
textureImporter.sRGBTexture = true; textureImporter.mipmapEnabled = true;
textureImporter.maxTextureSize = 1024; textureImporter.anisoLevel = 4;
textureImporter.SaveAndReimport();
var manifest = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(source + "/Exports/LibraryKiosk.json"));
var materials = new Dictionary<string, Material>();
var shader = Shader.Find("Universal Render Pipeline/Lit");
if (!shader) throw new Exception("Existing URP shader unavailable.");
foreach (var specification in manifest["materials"])
{
    string name = (string)specification["name"];
    string path = art + "/Materials/" + name + ".mat";
    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
    if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
    var values = specification["color"];
    var color = new Color((float)values[0], (float)values[1], (float)values[2], 1).gamma;
    bool textured = name == "Kiosk_NaturalOak";
    mat.SetColor("_BaseColor", textured ? Color.white : color);
    mat.SetFloat("_Metallic", (float)specification["metallic"]);
    mat.SetFloat("_Smoothness", 1 - (float)specification["roughness"]);
    mat.SetTexture("_BaseMap", textured ? AssetDatabase.LoadAssetAtPath<Texture2D>(art + "/Textures/Kiosk_OakGrain.png") : null);
    EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); materials.Add(name, mat);
}
var importer = (ModelImporter)AssetImporter.GetAtPath(art + "/Models/LibraryKiosk.fbx");
importer.globalScale = 1; importer.useFileScale = true;
importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
importer.addCollider = false; importer.isReadable = false;
// This prop uses the scene's lighting/probes. Automatic lightmap unwrapping
// discards the small reader-symbol mesh when its UV islands cannot be packed.
importer.generateSecondaryUV = false;
importer.importNormals = ModelImporterNormals.Import;
importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
foreach (var material in materials)
    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.Key), material.Value);
importer.SaveAndReimport();
string prefabPath = art + "/Prefabs/LibraryKiosk.prefab";
var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
Bounds bounds = default;
try
{
    var root = new GameObject("LibraryKiosk");
    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
    var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(art + "/Models/LibraryKiosk.fbx"), preview);
    model.name = "BlenderModel"; model.transform.SetParent(root.transform, false);
    var anchor = model.GetComponentsInChildren<Transform>().Single(x => x.name == "FrontAnchor");
    model.transform.rotation = Quaternion.FromToRotation((anchor.position - model.transform.position).normalized, Vector3.forward) * model.transform.rotation;
    var renderers = model.GetComponentsInChildren<Renderer>();
    bounds = renderers[0].bounds;
    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
    model.transform.localPosition -= Vector3.up * bounds.min.y;
    bounds = renderers[0].bounds;
    foreach (var renderer in renderers)
    {
        bounds.Encapsulate(renderer.bounds);
        if (renderer.sharedMaterials.Any(x => !x || !materials.Values.Contains(x)))
            throw new Exception("Unmapped kiosk material: " + renderer.name);
        var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        if (!mesh || mesh.vertexCount == 0 || mesh.subMeshCount == 0)
            throw new Exception("Empty imported mesh: " + renderer.name);
        GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic);
    }
    if (bounds.size.y < 1.70f || bounds.size.y > 1.80f || bounds.size.x > .80f || bounds.size.z > .70f)
        throw new Exception("Unexpected metre scale: " + bounds.size);
    var world = root.AddComponent<WorldObject>();
    world.objectType = WorldObjectType.StaticProp; world.functions = FurnitureFunction.Display;
    var collider = root.AddComponent<BoxCollider>(); collider.center = bounds.center; collider.size = bounds.size;
    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
    UnityEngine.Object.DestroyImmediate(root);
}
finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
var map = scene.GetRootGameObjects().Single(x => x.name == "Map_CivicLibraryInterior").transform;
var worldRoot = map.Find("10_World");
if (!worldRoot) throw new Exception("Existing map world group missing.");
var placement = worldRoot.Find("LibraryKiosks");
if (!placement) { placement = new GameObject("LibraryKiosks").transform; placement.SetParent(worldRoot, false); }
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
var objects = new List<GameObject>();
for (int i = 0; i < 3; i++)
{
    string name = "LibraryKiosk_" + (i + 1).ToString("00");
    var existing = placement.Find(name);
    var instance = existing ? existing.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
    if (PrefabUtility.GetCorrespondingObjectFromSource(instance) != prefab)
        throw new Exception("Existing placement has a different source prefab: " + name);
    instance.name = name; instance.transform.SetParent(placement, false);
    instance.transform.SetPositionAndRotation(new Vector3(15.1f, 0, -4.6f - i * 1.4f), Quaternion.Euler(0, 270, 0));
    PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
    objects.Add(instance);
}
Physics.SyncTransforms();
var checks = new List<string>();
foreach (var obj in objects)
{
    var collider = obj.GetComponent<BoxCollider>();
    var overlaps = Physics.OverlapBox(collider.bounds.center, collider.bounds.extents - Vector3.one * .015f,
        Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Where(x => !x.transform.IsChildOf(obj.transform) && x.bounds.max.y > .12f).ToArray();
    if (overlaps.Length != 0) throw new Exception(obj.name + " overlaps " + string.Join(",", overlaps.Select(x => x.name)));
    var access = obj.transform.position + obj.transform.forward * 1.05f + Vector3.up;
    if (Physics.OverlapBox(access, new Vector3(.55f, .85f, .5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(x => x.bounds.max.y > .12f))
        throw new Exception(obj.name + " has blocked standing space");
    checks.Add(obj.name + ": shared prefab, floor-level, collision-free, clear front access");
}
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
var report = new { scene = scene.path, prefab = prefabPath, count = objects.Count, bounds = bounds.size.ToString(),
    checks, renderersPerUnit = prefab.GetComponentsInChildren<Renderer>().Length, materials = materials.Count,
    placements = objects.Select(x => new { name = x.name, position = x.transform.position.ToString(), facing = x.transform.forward.ToString() }).ToArray() };
System.IO.File.WriteAllText(source + "/QA/UnityIntegration.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
return report;
