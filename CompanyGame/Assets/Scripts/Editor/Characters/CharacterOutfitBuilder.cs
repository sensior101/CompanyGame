using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CompanyGame.Editor.Characters
{
    /// <summary>
    /// Turns Players/Outfits/{Sex}BasicOutfit.fbx (ArtSource/Daldongne/Characters/Outfits_20261111/build_outfits.py) into a
    /// CharacterOutfit asset and puts AvatarOutfit on MaleVisual / FemaleVisual, which every player, remote player, citizen
    /// and staff NPC is made from. Garment meshes are moved into the body mesh's space and given the body's own bind poses,
    /// so at runtime they only need the avatar's bones by name.
    /// Run: -executeMethod CompanyGame.Editor.Characters.CharacterOutfitBuilder.Build
    /// </summary>
    public static class CharacterOutfitBuilder
    {
        public const string Folder = "Assets/Art/Daldongne/Players/Outfits";

        static readonly (string sex, string visual, string body)[] Bodies =
        {
            ("Male", ReferenceBoyImporter.VisualPath, "Assets/Art/Daldongne/Players/MeshyMale/MaleArticulated.fbx"),
            ("Female", ReferenceGirlImporter.VisualPath, "Assets/Art/Daldongne/Players/MeshyFemale/FemaleArticulated.fbx"),
        };

        // Default colours; NPCs re-tint them (AvatarOutfit.SetColors).
        static readonly Color TopColor = new Color(.56f, .64f, .72f), BottomColor = new Color(.18f, .22f, .30f);

        [MenuItem("Tools/Company Game/Characters/Build Basic Outfits")]
        public static void Build()
        {
            var report = new StringBuilder();
            foreach (var (sex, visual, bodyPath) in Bodies)
            {
                var outfit = BuildOutfit(sex, bodyPath, report);
                var root = PrefabUtility.LoadPrefabContents(visual);
                try
                {
                    var dress = root.GetComponent<AvatarOutfit>() ?? root.AddComponent<AvatarOutfit>();
                    dress.outfit = outfit;
                    PrefabUtility.SaveAsPrefabAsset(root, visual);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                report.AppendLine(sex + ": AvatarOutfit on " + visual);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Outfits] built\n" + report);
        }

        static CharacterOutfit BuildOutfit(string sex, string bodyPath, StringBuilder report)
        {
            string fbx = Folder + "/" + sex + "BasicOutfit.fbx";
            ConfigureImporter(fbx);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx) ?? throw new FileNotFoundException(fbx);
            var bodyModel = AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath) ?? throw new FileNotFoundException(bodyPath);
            var body = bodyModel.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();

            string assetPath = Folder + "/" + sex + "BasicOutfit.asset";
            var outfit = AssetDatabase.LoadAssetAtPath<CharacterOutfit>(assetPath);
            if (!outfit) { outfit = ScriptableObject.CreateInstance<CharacterOutfit>(); AssetDatabase.CreateAsset(outfit, assetPath); }
            foreach (var old in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>()) UnityEngine.Object.DestroyImmediate(old, true);

            var pieces = new List<CharacterOutfit.Piece>();
            foreach (var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string name = skin.name;
                var mesh = Bake(skin, source.transform, body, bodyModel.transform, out var bones, out float worst);
                mesh.name = name;
                AssetDatabase.AddObjectToAsset(mesh, outfit);
                if (name.EndsWith("BodyMasked"))
                {
                    outfit.maskedBody = mesh; outfit.maskedBodyBones = bones;
                    report.AppendLine(sex + " masked body: " + mesh.triangles.Length / 3 + " triangles (full " + body.sharedMesh.triangles.Length / 3 + ")");
                    continue;
                }
                bool isTop = name.EndsWith("Top");
                var material = Material(Folder + "/" + name + ".mat", isTop ? TopColor : BottomColor);
                pieces.Add(new CharacterOutfit.Piece { name = name, mesh = mesh, material = material, bones = bones, slot = isTop ? 0 : 1 });
                report.AppendLine(sex + " " + name + ": " + mesh.vertexCount + " vertices, " + mesh.triangles.Length / 3 + " triangles, " + bones.Length + " bones, bind pose mismatch " + worst.ToString("F5"));
            }
            outfit.pieces = pieces.OrderBy(p => p.slot).ToArray();
            if (outfit.pieces.Length != 2 || !outfit.maskedBody) throw new InvalidDataException(fbx + " must hold Top, Bottom and BodyMasked.");
            EditorUtility.SetDirty(outfit);
            AssetDatabase.SaveAssets();
            return outfit;
        }

        static void ConfigureImporter(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path) ?? throw new FileNotFoundException(path);
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.optimizeGameObjects = false;
            importer.skinWeights = ModelImporterSkinWeights.Standard;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// A copy of the garment mesh in the body mesh's space, with the body's bind pose for each bone (by name).
        /// worst: the largest difference between the garment file's own bind pose and the body's (0 when the rigs agree).
        /// </summary>
        static Mesh Bake(SkinnedMeshRenderer garment, Transform garmentRoot, SkinnedMeshRenderer body, Transform bodyRoot,
            out string[] boneNames, out float worst)
        {
            var mesh = UnityEngine.Object.Instantiate(garment.sharedMesh);
            Matrix4x4 garmentNode = garmentRoot.worldToLocalMatrix * garment.transform.localToWorldMatrix;
            Matrix4x4 bodyNode = bodyRoot.worldToLocalMatrix * body.transform.localToWorldMatrix;
            Matrix4x4 toBody = bodyNode.inverse * garmentNode;
            if (!IsIdentity(toBody))
            {
                var v = mesh.vertices; var n = mesh.normals; var t = mesh.tangents;
                for (int i = 0; i < v.Length; i++) v[i] = toBody.MultiplyPoint3x4(v[i]);
                for (int i = 0; i < n.Length; i++) n[i] = toBody.MultiplyVector(n[i]).normalized;
                for (int i = 0; i < t.Length; i++) { var d = toBody.MultiplyVector(t[i]).normalized; t[i] = new Vector4(d.x, d.y, d.z, t[i].w); }
                mesh.vertices = v; mesh.normals = n; if (t.Length > 0) mesh.tangents = t;
            }
            var bodyBones = body.bones; var bodyPoses = body.sharedMesh.bindposes;
            var bodyAll = bodyRoot.GetComponentsInChildren<Transform>(true).GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.First());
            var garmentPoses = garment.sharedMesh.bindposes;
            var names = garment.bones.Select(b => b.name).ToArray();
            boneNames = names;
            var poses = new Matrix4x4[names.Length];
            worst = 0f;
            for (int i = 0; i < names.Length; i++)
            {
                string boneName = names[i];
                int j = Array.FindIndex(bodyBones, b => b && b.name == boneName);
                if (j >= 0) poses[i] = bodyPoses[j];
                else if (bodyAll.TryGetValue(boneNames[i], out var bone)) poses[i] = bone.worldToLocalMatrix * body.transform.localToWorldMatrix;
                else throw new InvalidDataException("Body has no bone " + boneNames[i]);
                // The garment file's own bind pose, taken to the body's space, should describe the same bone.
                var own = garmentPoses[i] * toBody.inverse;
                for (int k = 0; k < 16; k++) worst = Mathf.Max(worst, Mathf.Abs(own[k] - poses[i][k]));
            }
            mesh.bindposes = poses;
            mesh.RecalculateBounds();
            return mesh;
        }

        static bool IsIdentity(Matrix4x4 m)
        {
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++)
                if (Mathf.Abs(m[r, c] - (r == c ? 1f : 0f)) > 1e-5f) return false;
            return true;
        }

        static Material Material(string path, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .12f); material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
