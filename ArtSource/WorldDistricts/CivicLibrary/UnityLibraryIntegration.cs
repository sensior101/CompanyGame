// Offline, reproducible editor integration. Copy this file into Assets/Editor in
// the isolated CivicLibraryValidation project; never install a runtime manager.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.CivicLibrary
{
    public static class UnityLibraryIntegration
    {
        const string ScenePath = "Assets/Scenes/Maps/CivicDistrict.unity";
        const string Art = "Assets/Art/WorldDistricts/CivicLibrary";
        const string Fbx = Art + "/CivicLibrary.fbx";
        const string PrefabPath = Art + "/CivicLibrary.prefab";
        const float Expansion = 1.4f;
        // Unity removes project Temp on a normal exit, so evidence lives outside it.
        const string QaFolder = "CivicLibraryQA";

        [Serializable] public sealed class MaterialSpec
        {
            public string name, baseColorTexture, normalTexture, surface, cull;
            public float[] baseColor, emission;
            public float roughness, metallic, alpha = 1, emissionStrength;
        }
        [Serializable] public sealed class Manifest { public MaterialSpec[] materials; public int triangles, mesh_objects; }
        [Serializable] public sealed class Check { public string name, detail; public bool pass; }
        [Serializable] public sealed class Report
        {
            public string scene = ScenePath, utc, error;
            public bool pass;
            public int passed, failed, renderers, triangles, colliders;
            public Vector3 libraryPosition, librarySize, mapScale;
            public List<Check> checks = new List<Check>();
            public List<string> captures = new List<string>();
        }

        public static void Build()
        {
            RequireClone();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Art + "/material_manifest.json"));
            var materials = CreateMaterials(manifest);
            ImportModel(materials);
            CreatePrefab(materials);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var world = Find(scene, "10_World");
            var library = Find(scene, "CivicDistrict_Library");
            if (!world || !library) throw new InvalidOperationException("Expected existing civic world/library roots are missing.");
            bool originalScale = Approximately(world.localScale, Vector3.one);
            if (!originalScale && !Approximately(world.localScale, new Vector3(Expansion, 1, Expansion)))
                throw new InvalidOperationException("Unexpected world scale; refusing to rescale an unknown map state.");

            TrimOldLibraryDetails(scene);
            if (originalScale)
            {
                var markers = new HashSet<Transform>();
                foreach (var spawn in Components<MapSpawnPoint>(scene)) markers.Add(spawn.transform);
                foreach (var stop in Components<TransitStop>(scene))
                {
                    markers.Add(stop.transform);
                    if (stop.boardingPoint) markers.Add(stop.boardingPoint);
                }
                foreach (var camera in Components<Camera>(scene)) markers.Add(camera.transform);
                // Marker roots must be transformed only once, including future nested boarding points.
                var roots = markers.Where(t => !t.IsChildOf(world) &&
                    !markers.Any(other => other != t && t.IsChildOf(other))).ToArray();
                foreach (var marker in roots)
                {
                    Vector3 p = marker.position;
                    marker.position = new Vector3(p.x * Expansion, p.y, p.z * Expansion);
                }
                world.localScale = new Vector3(Expansion, 1, Expansion);
            }
            // Keep the existing serialized library root identity; replace its old art/collider/sign children.
            foreach (Transform child in library.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            library.localPosition = new Vector3(-20, 0, 55);
            library.localRotation = Quaternion.identity;
            library.localScale = new Vector3(1f / Expansion, 1, 1f / Expansion);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, library);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the integrated civic scene.");
            AssetDatabase.SaveAssets();
            Validate();
        }

        static void RequireClone()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
            if (!Application.dataPath.Replace('\\', '/').EndsWith("/Temp/CivicLibraryValidation/Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This integration is restricted to the isolated Temp/CivicLibraryValidation project.");
        }

        static Dictionary<string, Material> CreateMaterials(Manifest manifest)
        {
            Directory.CreateDirectory(Art + "/Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP Lit is unavailable.");
            var result = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var spec in manifest.materials)
            {
                string path = Art + "/Materials/" + spec.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                material.name = spec.name;
                material.shaderKeywords = Array.Empty<string>();
                material.SetFloat("_WorkflowMode", 1);
                material.SetFloat("_Metallic", spec.metallic);
                material.SetFloat("_Smoothness", 1 - spec.roughness);
                material.SetFloat("_Cull", spec.cull == "Off" ? 0 : 2);
                material.SetFloat("_AlphaClip", 0);
                bool glass = spec.surface == "Transparent";
                Color color = spec.baseColor != null && spec.baseColor.Length >= 3
                    ? new Color(spec.baseColor[0], spec.baseColor[1], spec.baseColor[2], glass ? spec.alpha : 1) : Color.white;
                if (!string.IsNullOrEmpty(spec.baseColorTexture))
                {
                    material.SetTexture("_BaseMap", Texture(spec.baseColorTexture, false));
                    color = Color.white; // The supplied map already contains the albedo; do not multiply it twice.
                }
                material.SetColor("_BaseColor", color);
                if (!string.IsNullOrEmpty(spec.normalTexture))
                {
                    material.SetTexture("_BumpMap", Texture(spec.normalTexture, true));
                    material.SetFloat("_BumpScale", .45f);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (glass)
                {
                    material.SetFloat("_Surface", 1);
                    material.SetFloat("_Blend", 0);
                    material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                    material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0);
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = (int)RenderQueue.Transparent;
                    material.SetShaderPassEnabled("ShadowCaster", false);
                    material.SetShaderPassEnabled("DepthOnly", false);
                    material.doubleSidedGI = true;
                }
                else
                {
                    material.SetFloat("_Surface", 0);
                    material.SetFloat("_SrcBlend", (float)BlendMode.One);
                    material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                    material.SetFloat("_ZWrite", 1);
                    material.SetOverrideTag("RenderType", "Opaque");
                    material.renderQueue = -1;
                    material.SetShaderPassEnabled("ShadowCaster", true);
                }
                if (spec.emission != null && spec.emission.Length >= 3)
                {
                    material.SetColor("_EmissionColor", new Color(spec.emission[0], spec.emission[1], spec.emission[2]) * spec.emissionStrength);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                }
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                result.Add(spec.name, material);
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static Texture2D Texture(string relativePath, bool normal)
        {
            string path = Art + "/" + relativePath;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new FileNotFoundException("Missing texture", path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void ImportModel(Dictionary<string, Material> materials)
        {
            var importer = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            if (!importer) throw new FileNotFoundException("Missing Blender export", Fbx);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var pair in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();
        }

        static void CreatePrefab(Dictionary<string, Material> materials)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var root = new GameObject("CivicLibrary");
            // The imported FBX root can carry Blender's axis conversion. Keep it
            // intact under an identity placement root instead of zeroing it out.
            var visual = Object.Instantiate(source, root.transform, false);
            visual.name = "BlenderModel";
            try
            {
                var firstStep = visual.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "EntryStep_01");
                if (firstStep.bounds.center.z > 0)
                    visual.transform.localRotation = Quaternion.Euler(0, 180, 0) * visual.transform.localRotation;
                Debug.Log("Civic FBX root transform retained: rotation=" + source.transform.localEulerAngles +
                    ", scale=" + source.transform.localScale + "; front-step=" + firstStep.bounds.center);
                foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = mesh.GetComponent<MeshRenderer>();
                    if (!renderer) continue;
                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        string name = slots[i] ? slots[i].name.Replace(" (Instance)", "") : "";
                        if (!materials.TryGetValue(name, out var resolved))
                            throw new InvalidOperationException("Unmapped model material " + name + " at " + mesh.name);
                        slots[i] = resolved;
                    }
                    renderer.sharedMaterials = slots;
                    bool glass = slots.Any(m => m.GetFloat("_Surface") == 1);
                    renderer.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    renderer.receiveShadows = !glass;
                    GameObjectUtility.SetStaticEditorFlags(mesh.gameObject,
                        StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
                    string group = AncestorGroup(mesh.transform, root.transform);
                    bool collision = group != "InteriorHint" && group != "Landscape";
                    if (group == "Landscape") collision = mesh.name.StartsWith("Site_", StringComparison.Ordinal) ||
                        slots.Any(m => m.name == "CL_Pavement");
                    if (collision)
                    {
                        var collider = mesh.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = mesh.sharedMesh;
                        collider.convex = false;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static string AncestorGroup(Transform value, Transform root)
        {
            string[] groups = { "Structure", "Glass", "Frames", "Wood", "Roof", "Steps", "Ramp", "Railings", "Bench", "Landscape", "InteriorHint" };
            for (var t = value; t && t != root; t = t.parent) if (groups.Contains(t.name)) return t.name;
            throw new InvalidOperationException("Mesh is outside a named authoring group: " + value.name);
        }

        static void TrimOldLibraryDetails(Scene scene)
        {
            var details = Find(scene, "CivicDistrict_UrbanDetail");
            if (!details) throw new InvalidOperationException("Original shared facade details are missing.");
            var world = Find(scene, "10_World");
            Directory.CreateDirectory(Art + "/SceneMeshes");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var remove = new HashSet<string> { "blue", "glass", "glassLight", "leaf", "metal", "white", "stone" };
            foreach (var filter in details.GetComponentsInChildren<MeshFilter>(true))
            {
                string name = filter.gameObject.name;
                if (!remove.Contains(name)) continue;
                var source = filter.sharedMesh;
                var verts = source.vertices;
                Matrix4x4 toOriginalDistrict = world.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                bool Inside(int index)
                {
                    // UrbanDetail mesh vertices are already in the original unexpanded district coordinates.
                    Vector3 p = toOriginalDistrict.MultiplyPoint3x4(verts[index]);
                    return name == "stone"
                        ? p.x >= -28 && p.x <= -12 && p.z >= 42.3f && p.z <= 43.3f && p.y >= -.1f && p.y <= .5f
                        : p.x >= -35 && p.x <= -5 && p.z >= 42 && p.z <= 68;
                }
                var clone = Object.Instantiate(source);
                clone.name = "CivicDistrict_UrbanDetail_" + name + "_LibraryReplaced";
                int removed = 0;
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    int[] tris = source.GetTriangles(sub);
                    var keep = new List<int>(tris.Length);
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        if (Inside(tris[i]) && Inside(tris[i + 1]) && Inside(tris[i + 2])) { removed++; continue; }
                        keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
                    }
                    clone.SetTriangles(keep, sub, false);
                }
                clone.RecalculateBounds();
                string path = Art + "/SceneMeshes/" + clone.name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing) { EditorUtility.CopySerialized(clone, existing); Object.DestroyImmediate(clone); clone = existing; }
                else AssetDatabase.CreateAsset(clone, path);
                filter.sharedMesh = clone;
                foreach (var collider in filter.GetComponents<MeshCollider>()) if (collider.sharedMesh == source) collider.sharedMesh = clone;
                Debug.Log("Civic library old detail removal " + name + ": " + removed + " triangles.");
            }
        }

        public static void Validate()
        {
            RequireClone();
            Directory.CreateDirectory(QaFolder);
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var world = Find(scene, "10_World");
                var library = Find(scene, "CivicDistrict_Library");
                var all = library.GetComponentsInChildren<Transform>(true);
                var renderers = library.GetComponentsInChildren<MeshRenderer>(true);
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                report.libraryPosition = library.position;
                report.librarySize = bounds.size;
                report.mapScale = world.localScale;
                report.renderers = renderers.Length;
                report.triangles = library.GetComponentsInChildren<MeshFilter>().Sum(f =>
                    Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(sub => (int)f.sharedMesh.GetIndexCount(sub) / 3));
                report.colliders = library.GetComponentsInChildren<Collider>().Length;
                Add(report, "Expanded district XZ; height unchanged", Approximately(world.localScale, new Vector3(1.4f, 1, 1.4f)), world.localScale.ToString("F3"));
                Add(report, "Library retains original lot center", Approximately(library.position, new Vector3(-28, 0, 77)), library.position.ToString("F3"));
                Add(report, "Library retains metre dimensions", Approximately(library.lossyScale, Vector3.one) && bounds.size.x > 35 && bounds.size.x < 42 && bounds.size.y < 12,
                    "World bounds " + bounds + "; scale " + library.lossyScale);
                Add(report, "Fits enlarged library parcel", bounds.min.x >= -50.4f && bounds.max.x <= -5.6f && bounds.min.z >= 56 && bounds.max.z <= 100.8f, bounds.ToString());
                var steps = all.Where(t => t.name.StartsWith("EntryStep_", StringComparison.Ordinal)).OrderBy(t => t.name).ToArray();
                Add(report, "Exactly five front steps", steps.Length == 5, "Count=" + steps.Length);
                for (int i = 0; i < steps.Length; i++)
                {
                    var b = steps[i].GetComponent<Renderer>().bounds;
                    Add(report, "Step " + (i + 1) + " rise/front orientation", Mathf.Abs(b.max.y - (i + 1) * .15f) < .025f && b.center.z < 67,
                        "Top=" + b.max.y.ToString("F3") + ", center=" + b.center.ToString("F3"));
                }
                Add(report, "Grouped single front-left bench", all.Count(t => t.name == "Bench") == 1 &&
                    all.First(t => t.name == "Bench").GetComponentsInChildren<Renderer>().All(r => r.bounds.center.x < -28 && r.bounds.center.z < 66), "One Bench authoring group with all parts on the front left.");
                Add(report, "No missing materials or unsupported shaders", renderers.All(r => r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m && m.shader && m.shader.isSupported)), "All library renderer material slots checked.");
                Add(report, "Grouped game-ready geometry", renderers.Length <= 120 && report.triangles < 180000,
                    report.renderers + " renderers, " + report.triangles + " triangles.");
                Add(report, "Interior bookshelf and reading-area hints retained", all.Any(t => t.name == "InteriorHint") &&
                    all.First(t => t.name == "InteriorHint").GetComponentsInChildren<Renderer>().Length >= 5,
                    "Decorative interior grouped independently without gameplay or collision.");
                var glass = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/CL_Glass.mat");
                Add(report, "Transparent URP glass", glass && glass.GetFloat("_Surface") == 1 && glass.GetFloat("_ZWrite") == 0 && glass.renderQueue == 3000,
                    glass ? glass.shader.name + "; alpha=" + glass.GetColor("_BaseColor").a : "Missing glass");
                Add(report, "Runtime owners preserved", Components<MonoBehaviour>(scene).All(c => !c || (c.GetType().Name != "PlayerMovement" && c.GetType().Name != "GameSystems")), "No scene-authored replacement player or systems manager.");
                Add(report, "No tiny vegetation collision", all.Where(t => t.name == "Landscape").SelectMany(t => t.GetComponentsInChildren<Collider>()).All(c => c.name.StartsWith("Site_", StringComparison.Ordinal) || c.name.Contains("Pavement")), "Landscape colliders are confined to the site surface.");
                Physics.SyncTransforms();
                ValidateMarkers(scene, report);
                Vector3 origin = library.position;
                CheckRampSlope(report, origin, "Lower ramp 1:20", new Vector3(8, 0, -16.1f), new Vector3(13.5f, 0, -16.1f), .275f);
                CheckRampSlope(report, origin, "Upper ramp 1:20", new Vector3(8, 0, -13.9f), new Vector3(13.5f, 0, -13.9f), -.275f);
                var stairRoute = new[] { new Vector3(0, 0, -18.7f), new Vector3(0, 0, -13.6f), new Vector3(0, .75f, -10), new Vector3(0, .75f, -9.8f) };
                var rampRoute = new[] { new Vector3(5.9f, 0, -18.7f), new Vector3(5.9f, 0, -16.1f), new Vector3(7.2f, .028f, -16.1f),
                    new Vector3(15.5f, .393f, -16.1f), new Vector3(15.5f, .393f, -13.9f), new Vector3(6.1f, .768f, -13.9f),
                    new Vector3(6.1f, .768f, -10), new Vector3(0, .75f, -10) };
                Walk(report, "Five-step approach", origin, stairRoute);
                Walk(report, "Five-step descent", origin, stairRoute.Reverse().ToArray());
                Walk(report, "U ramp ascent and landing to entrance", origin, rampRoute);
                Walk(report, "U ramp descent to public pavement", origin, rampRoute.Reverse().ToArray());
                Capture(report, "CivicDistrict_ExpandedOverview.png", new Vector3(110, 145, -150), new Vector3(0, 0, 10), 60, false);
                Capture(report, "Library_Unity_AccessOverview.png", origin + new Vector3(27, 28, -42), origin + new Vector3(0, 0, -5), 50, false);
                Capture(report, "Library_Unity_EyeLevel.png", origin + new Vector3(18, 2, -31), origin + new Vector3(0, 4, -6), 63, false);
            }
            catch (Exception e) { report.error = e.ToString(); Add(report, "QA execution", false, e.Message); }
            report.passed = report.checks.Count(c => c.pass);
            report.failed = report.checks.Count(c => !c.pass);
            report.pass = report.failed == 0;
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(QaFolder + "/CivicLibraryQA.json", json);
            Debug.Log(json);
            if (!report.pass) throw new InvalidOperationException("Civic library QA failed; inspect CivicLibraryQA/CivicLibraryQA.json.");
        }

        static void ValidateMarkers(Scene scene, Report report)
        {
            var spawns = Components<MapSpawnPoint>(scene).ToArray();
            Add(report, "All original spawn IDs preserved", spawns.Length == 18 && spawns.Select(s => s.spawnId).Distinct().Count() == 18, "Count=" + spawns.Length);
            var standard = spawns.FirstOrDefault(s => s.spawnId == "default");
            Add(report, "Default spawn follows expanded district", standard && Approximately(standard.transform.position, new Vector3(-7, .16f, -53.2f)), standard ? standard.transform.position.ToString("F3") : "Missing");
            foreach (var stop in Components<TransitStop>(scene))
            {
                var spawn = spawns.FirstOrDefault(s => s.spawnId == stop.ArrivalSpawnId);
                Vector3 expected = stop.kind == TransitKind.Subway ? new Vector3(-28, .13f, -23.24f) : new Vector3(78.4f, .15f, 54.81f);
                Add(report, "Transport alignment " + stop.kind, Approximately(stop.BoardingPosition, expected) && spawn &&
                    Vector3.Distance(stop.BoardingPosition, spawn.transform.position) < stop.interactionRadius,
                    "Boarding=" + stop.BoardingPosition.ToString("F3") + "; arrival=" + (spawn ? spawn.transform.position.ToString("F3") : "missing"));
            }
        }

        static void Walk(Report report, string label, Vector3 origin, Vector3[] localPoints)
        {
            var temporary = new GameObject("__CivicLibraryControllerQA");
            var controller = temporary.AddComponent<CharacterController>();
            controller.height = 1.8f; controller.radius = .35f; controller.center = new Vector3(0, .9f, 0);
            controller.stepOffset = .23f; controller.slopeLimit = 45; controller.skinWidth = .02f; controller.minMoveDistance = 0;
            var points = localPoints.Select(p => origin + p).ToArray();
            int samples = 0;
            string error = null;
            try
            {
                controller.enabled = false;
                temporary.transform.position = points[0] + Vector3.up * .04f;
                controller.enabled = true;
                Physics.SyncTransforms();
                for (int settle = 0; settle < 5; settle++) controller.Move(Vector3.down * .02f);
                for (int index = 1; index < points.Length && error == null; index++)
                {
                    var goal = points[index];
                    int max = Mathf.CeilToInt(Vector2.Distance(XZ(temporary.transform.position), XZ(goal)) / .045f) + 60;
                    for (int step = 0; step < max; step++)
                    {
                        Vector3 delta = goal - temporary.transform.position; delta.y = 0;
                        if (delta.magnitude < .06f) break;
                        controller.Move(Vector3.ClampMagnitude(delta, .06f) + Vector3.down * .035f);
                        samples++;
                    }
                    Vector3 actual = temporary.transform.position;
                    float horizontal = Vector2.Distance(XZ(actual), XZ(goal));
                    if (horizontal > .10f || Mathf.Abs(actual.y - goal.y) > .14f)
                        error = "Waypoint " + index + " expected " + goal.ToString("F3") + ", reached " + actual.ToString("F3");
                }
                Add(report, label, error == null, error ?? ("CharacterController radius .35m, height 1.8m, step .23m; " + samples + " movement samples."));
            }
            finally { Object.DestroyImmediate(temporary); Physics.SyncTransforms(); }
        }

        static void CheckRampSlope(Report report, Vector3 origin, string label, Vector3 a, Vector3 b, float rise)
        {
            bool ha = Physics.Raycast(origin + a + Vector3.up * 2, Vector3.down, out var first, 3, ~0, QueryTriggerInteraction.Ignore);
            bool hb = Physics.Raycast(origin + b + Vector3.up * 2, Vector3.down, out var second, 3, ~0, QueryTriggerInteraction.Ignore);
            float actual = second.point.y - first.point.y;
            float angle = ha ? Vector3.Angle(first.normal, Vector3.up) : 90;
            Add(report, label, ha && hb && Mathf.Abs(actual - rise) < .02f && angle > 2.3f && angle < 3.4f,
                "Rise=" + actual.ToString("F3") + "m across 5.5m; slope=" + angle.ToString("F2") + " degrees.");
        }

        static void Capture(Report report, string file, Vector3 position, Vector3 target, float fov, bool orthographic)
        {
            var go = new GameObject("__CivicLibraryCapture");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            camera.fieldOfView = fov; camera.nearClipPlane = .1f; camera.farClipPlane = 600;
            camera.clearFlags = CameraClearFlags.Skybox; camera.orthographic = orthographic;
            var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            var rt = RenderTexture.GetTemporary(1600, 1000, 24, RenderTextureFormat.ARGB32);
            var oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                // Warm the render pipeline before retaining the final capture;
                // this does not change any scene material or lighting settings.
                camera.Render();
                camera.Render();
                camera.Render();
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
                string path = QaFolder + "/" + file;
                File.WriteAllBytes(path, texture.EncodeToPNG()); report.captures.Add(path);
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture); Object.DestroyImmediate(go);
            }
        }

        static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
        static bool Approximately(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .015f;
        static void Add(Report report, string name, bool pass, string detail) => report.checks.Add(new Check { name = name, pass = pass, detail = detail });
        static IEnumerable<T> Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));
        static Transform Find(Scene scene, string name) => Components<Transform>(scene).FirstOrDefault(t => t.name == name);
    }
}
