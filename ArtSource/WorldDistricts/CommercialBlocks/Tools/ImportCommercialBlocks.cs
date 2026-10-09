// Optional editor tool. Copy this file into Assets/Editor only when importing the pack.
// This file does not install itself, run automatically, or change an existing scene.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CompanyGame.Editor.CommercialBlocks
{
    public static class ImportCommercialBlocks
    {
        const string ShaderName = "Universal Render Pipeline/Lit";
        const int BlockCount = 8;

        [Serializable] public class Catalog { public Block[] items; }
        [Serializable] public class Block
        {
            public string id, fbx;
            public int revision;
            public MaterialSpec[] materials;
        }
        [Serializable] public class MaterialSpec
        {
            public string name, texture, unitySurface;
            public float[] color, emissionColor;
            public float metallic, roughness, emissionStrength;
            public float unityAlpha = 1f;
        }

        [MenuItem("Tools/Company Game/Import Commercial Blocks (URP)")]
        public static void Import()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Commercial Blocks", "Stop Play Mode before importing.", "OK");
                return;
            }
            string manifest = EditorUtility.OpenFilePanel("Select CommercialBlocks/Catalog/catalog.json", "", "json");
            if (string.IsNullOrEmpty(manifest)) return;
            string destination = EditorUtility.OpenFolderPanel("Choose a destination folder inside this project's Assets", Application.dataPath, "");
            if (string.IsNullOrEmpty(destination)) return;

            string output = null;
            try
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader == null) throw new InvalidOperationException("Universal Render Pipeline/Lit was not found. Use a URP project.");
                if (GraphicsSettings.currentRenderPipeline == null ||
                    GraphicsSettings.currentRenderPipeline.GetType().Name != "UniversalRenderPipelineAsset")
                    throw new InvalidOperationException("The active render pipeline must be URP. This tool does not change project settings.");

                string assets = Path.GetFullPath(Application.dataPath);
                string selected = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!IsWithin(selected, assets)) throw new InvalidOperationException("Choose the Assets folder or one of its subfolders.");
                string selectedAsset = "Assets" + selected.Substring(assets.Length).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(selectedAsset)) throw new InvalidOperationException("The selected folder must already be imported in Unity.");

                Catalog catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(manifest));
                string exportRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest), "../Exports"));
                var textures = Preflight(catalog, manifest, exportRoot);
                if (!EditorUtility.DisplayDialog("Import Commercial Blocks",
                    "Import 8 individually designed building models, textures, URP materials and prefabs into a NEW CommercialBlocks folder?\n" +
                    "Existing scenes and assets will not be replaced. No colliders or gameplay components will be added.", "Import", "Cancel")) return;

                output = AssetDatabase.GenerateUniqueAssetPath(selectedAsset + "/CommercialBlocks");
                string absoluteOutput = AbsoluteAssetPath(output);
                Directory.CreateDirectory(Path.Combine(absoluteOutput, "Models"));
                Directory.CreateDirectory(Path.Combine(absoluteOutput, "Materials"));
                Directory.CreateDirectory(Path.Combine(absoluteOutput, "Prefabs"));
                foreach (var texture in textures)
                {
                    string target = Path.Combine(absoluteOutput, texture);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(Path.Combine(exportRoot, texture), target, false);
                }
                foreach (var block in catalog.items)
                    File.Copy(Path.Combine(exportRoot, block.id + ".fbx"), Path.Combine(absoluteOutput, "Models", block.id + ".fbx"), false);
                File.Copy(manifest, Path.Combine(absoluteOutput, "SourceCatalog.json"), false);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureTextures(output, textures);

                for (int i = 0; i < catalog.items.Length; i++)
                {
                    Block block = catalog.items[i];
                    EditorUtility.DisplayProgressBar("Commercial Blocks", block.id + " — materials and prefab", (float)i / BlockCount);
                    ImportBlock(output, block, shader);
                }
                AssetDatabase.SaveAssets();
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(output + "/Prefabs");
                EditorGUIUtility.PingObject(Selection.activeObject);
                Debug.Log("Imported 8 Commercial Blocks into " + output + ". Verify glass, dimensions and sign textures in your scene before gameplay integration.");
                EditorUtility.DisplayDialog("Commercial Blocks", "8 prefabs imported into:\n" + output + "/Prefabs\n\nScene placement and gameplay are not included.", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                string partial = output == null ? "" : "\n\nAny partial output remains in " + output + ". Existing assets were not overwritten.";
                EditorUtility.DisplayDialog("Commercial Blocks import stopped", exception.Message + partial, "OK");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static HashSet<string> Preflight(Catalog catalog, string manifest, string exportRoot)
        {
            if (catalog == null || catalog.items == null || catalog.items.Length != BlockCount)
                throw new InvalidDataException("The finished revision 2 or later catalog must contain exactly 8 individually designed blocks. Wait until generation finishes.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in catalog.items)
            {
                if (block == null || !Regex.IsMatch(block.id ?? "", @"^CB_0[1-8]$") || !ids.Add(block.id) || block.revision < 2)
                    throw new InvalidDataException("Catalog must contain each of CB_01–CB_08 once, with revision 2 or later.");
                string model = Path.GetFullPath(Path.Combine(exportRoot, block.id + ".fbx"));
                string declared = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest), block.fbx ?? ""));
                if (!string.Equals(model, declared, StringComparison.OrdinalIgnoreCase) || !File.Exists(model))
                    throw new FileNotFoundException("Missing or unexpected FBX path for " + block.id, model);
                if (block.materials == null || block.materials.Length == 0) throw new InvalidDataException(block.id + " has no material specifications.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var spec in block.materials)
                {
                    if (spec == null || !Regex.IsMatch(spec.name ?? "", @"^[A-Za-z0-9_.-]+$") || spec.name == "." || spec.name == ".." || !names.Add(spec.name))
                        throw new InvalidDataException("Invalid or repeated material name in " + block.id);
                    if (spec.color == null || spec.color.Length < 3 || spec.color.Any(v => float.IsNaN(v) || float.IsInfinity(v)))
                        throw new InvalidDataException("Invalid material color in " + block.id + ": " + spec.name);
                    if (string.IsNullOrEmpty(spec.texture)) continue;
                    string texture = spec.texture.Replace('\\', '/');
                    string source = Path.GetFullPath(Path.Combine(exportRoot, texture));
                    if (Path.IsPathRooted(texture) || !texture.StartsWith("Textures/", StringComparison.Ordinal) ||
                        texture.Split('/').Any(p => p == ".." || p == ".") || !IsWithin(source, Path.Combine(exportRoot, "Textures")) || !File.Exists(source))
                        throw new FileNotFoundException("Missing or invalid texture: " + texture);
                    textures.Add(texture);
                }
            }
            return textures;
        }

        static void ConfigureTextures(string output, IEnumerable<string> textures)
        {
            foreach (string relative in textures)
            {
                var importer = AssetImporter.GetAtPath(output + "/" + relative) as TextureImporter;
                if (importer == null) throw new InvalidDataException("Texture import failed: " + relative);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
        }

        static void ImportBlock(string output, Block block, Shader shader)
        {
            // Per-block materials prevent changing one sign/palette from recoloring another block.
            AssetDatabase.CreateFolder(output + "/Materials", block.id);
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var spec in block.materials)
            {
                var material = new Material(shader) { name = spec.name, enableInstancing = true };
                bool transparent = string.Equals(spec.unitySurface, "Transparent", StringComparison.OrdinalIgnoreCase);
                Color color = new Color(spec.color[0], spec.color[1], spec.color[2], 1f).gamma;
                color.a = transparent ? Mathf.Clamp01(spec.unityAlpha) : 1f;
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Metallic", Mathf.Clamp01(spec.metallic));
                material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(spec.roughness));
                if (!string.IsNullOrEmpty(spec.texture))
                    material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(output + "/" + spec.texture.Replace('\\', '/')));
                SetSurface(material, transparent);
                if (spec.emissionColor != null && spec.emissionColor.Length >= 3 && spec.emissionStrength > 0f)
                {
                    var emission = new Color(spec.emissionColor[0], spec.emissionColor[1], spec.emissionColor[2], 1f).gamma * spec.emissionStrength;
                    material.SetColor("_EmissionColor", emission);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                }
                AssetDatabase.CreateAsset(material, output + "/Materials/" + block.id + "/" + spec.name + ".mat");
                materials.Add(spec.name, material);
            }

            string modelPath = output + "/Models/" + block.id + ".fbx";
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) throw new InvalidDataException("Model import failed: " + block.id);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.generateSecondaryUV = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var pair in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new InvalidDataException("Imported model is unavailable: " + block.id);
            var nodes = new HashSet<string>(model.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            foreach (string required in new[] { "Sign_Main_01", "Sign_Main_02", "Sign_Corner", "Sign_Side", "Door_Leaf_01", "Door_Leaf_02", "EntryAnchor", "InteriorSpawnAnchor" })
                if (!nodes.Contains(required)) throw new InvalidDataException(block.id + " is missing node " + required);
            var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0 || renderers.Any(r => r.sharedMaterials.Any(m => m == null || !materials.Values.Contains(m))))
                throw new InvalidDataException("Material remapping or mesh import failed for " + block.id);

            // Isolated editor preview scene avoids adding temporary objects to the user's scene.
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                instance.name = block.id;
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, output + "/Prefabs/" + block.id + ".prefab", out bool saved);
                if (!saved || prefab == null) throw new IOException("Could not save prefab " + block.id);
                AssetDatabase.SetLabels(prefab, new[] { "CommercialBlock", "IndividualDesign", block.id });
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        static void SetSurface(Material material, bool transparent)
        {
            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetFloat("_Blend", 0f); // Alpha blending; Blender refraction is not reproduced.
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat("_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat("_ZWrite", transparent ? 0f : 1f);
            material.SetFloat("_Cull", (float)CullMode.Back); // Glass already has front and back geometry.
            material.SetFloat("_QueueOffset", 0f);
            material.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            material.renderQueue = transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            if (transparent) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetShaderPassEnabled("ShadowCaster", !transparent);
            material.SetShaderPassEnabled("DepthOnly", !transparent);
        }

        static bool IsWithin(string candidate, string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        static string AbsoluteAssetPath(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }
}
#endif
