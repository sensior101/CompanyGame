using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using CompanyGame.Daldongne;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace CompanyGame.Editor.Characters
{
    /// <summary>Imports character sources into the existing player visuals and motion owner.</summary>
    public static class ReferenceGirlImporter
    {
                public const string VisualPath = "Assets/Art/Daldongne/Players/FemaleVisual.prefab";
        public const int ArticulatedTriangleBudget = 18000;
        static readonly string[] PlayerPrefabs = {
            "Assets/Resources/Player.prefab", "Assets/Art/Daldongne/Players/PlayerFemale.prefab",
            "Assets/Art/Daldongne/Players/PlayerMale.prefab"
        };
        // Direct MaleVisual/FemaleVisual GUID users found by the character replacement audit.
        // Prefabs run before scenes so inherited anchors are repaired at their source.
        static readonly string[] NpcVisualUsers = {
            "Assets/Art/Daldongne/Players/PlayerFemale.prefab", "Assets/Art/Daldongne/Players/PlayerMale.prefab",
            "Assets/Art/Goshiwon/Prefabs/GoshiwonOwner.prefab",
            "Assets/Art/WorldDistricts/HandaeHQ/Lobby/Prefabs/15_NPCs.prefab",
            "Assets/Resources/Net/RemotePlayer.prefab", "Assets/Resources/Player.prefab",
            "Assets/Scenes/Interiors/CivicLibraryInterior.unity", "Assets/Scenes/Interiors/ConvenienceStoreInterior.unity"
        };
        static readonly Dictionary<HumanBodyBones, string> HumanoidBones = CreateHumanoidMap();
        static readonly string[] Scenes = {
            "Assets/Scenes/daldongnaemap.unity",
            "Assets/Scenes/Maps/DaldongnePocketGarden.unity",
            "Assets/Scenes/Templates/SmallMapTemplate.unity"
        };

        [MenuItem("Tools/Company Game/Characters/Rebuild Female Character")]
        public static void RebuildMenu() => Debug.Log(Rebuild());

        static Vector3 V(JToken a) => new Vector3((float)a[0], (float)a[1], (float)a[2]);
        static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before importing a character.");
            if (EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Resolve script compilation errors first.");
            for (int i=0; i<SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save scene edits before migrating character instances.");
        }

        public static string Rebuild() => Rebuild(false);

        public static string Rebuild(bool male, bool migrateScenes = true)
        {
            if (File.Exists(ArticulatedPath(male))) return RebuildArticulated(male, migrateScenes);
            if(!male && File.Exists(MeshyFemaleImporter.Folder+"/FemaleBody.fbx")) return MeshyFemaleImporter.Rebuild();
            if(male && File.Exists(MeshyMaleImporter.Folder+"/MaleBody.fbx")) return MeshyMaleImporter.Rebuild();
            // The old rigid-part ReferenceGirl/ReferenceBoy source was removed (2026-11-11); only the Meshy bodies remain.
            throw new FileNotFoundException("Character source missing: " + ArticulatedPath(male));
        }

        public static string ArticulatedPath(bool male) => (male ? MeshyMaleImporter.Folder : MeshyFemaleImporter.Folder)
            + (male ? "/MaleArticulated.fbx" : "/FemaleArticulated.fbx");

        static Dictionary<HumanBodyBones, string> CreateHumanoidMap()
        {
            var map = new Dictionary<HumanBodyBones, string> {
                { HumanBodyBones.Hips, "Hips" }, { HumanBodyBones.Spine, "Spine02" },
                { HumanBodyBones.Chest, "Spine01" }, { HumanBodyBones.UpperChest, "Spine" },
                { HumanBodyBones.Neck, "neck" }, { HumanBodyBones.Head, "Head" }
            };
            foreach (string side in new[] { "Left", "Right" })
            {
                string[] human = { "Shoulder", "UpperArm", "LowerArm", "Hand", "UpperLeg", "LowerLeg", "Foot", "Toes" };
                string[] source = { "Shoulder", "Arm", "ForeArm", "Hand", "UpLeg", "Leg", "Foot", "ToeBase" };
                for (int i = 0; i < human.Length; i++)
                    map.Add((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + human[i]), side + source[i]);
                string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
                string[] phalanges = { "Proximal", "Intermediate", "Distal" };
                foreach (string finger in fingers)
                    for (int segment = 0; segment < phalanges.Length; segment++)
                        map.Add((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + finger + phalanges[segment]),
                            side + "Hand" + (finger == "Little" ? "Pinky" : finger) + (segment + 1));
            }
            return map;
        }

        static HumanDescription DescribeHumanoid(GameObject source, bool requireFingers)
        {
            var transforms = source.GetComponentsInChildren<Transform>(true);
            var mapped = new Dictionary<HumanBodyBones, string>();
            foreach (var pair in HumanoidBones)
            {
                int count = transforms.Count(t => t.name == pair.Value);
                if (count > 1 || (count == 0 && (requireFingers || HumanTrait.RequiredBone((int)pair.Key))))
                    throw new InvalidDataException("Expected exactly one character bone named " + pair.Value);
                if (count == 1) mapped.Add(pair.Key, pair.Value);
            }
            return new HumanDescription {
                human = mapped.Select(pair => new HumanBone {
                    boneName = pair.Value, humanName = HumanTrait.BoneName[(int)pair.Key],
                    limit = new HumanLimit { useDefaultValues = true }
                }).ToArray(),
                skeleton = transforms.Select(t => new SkeletonBone {
                    name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale
                }).ToArray(),
                upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
                armStretch = .05f, legStretch = .05f, feetSpacing = 0f, hasTranslationDoF = false
            };
        }

        /// <summary>Imports Meshy body animation takes as reusable Humanoid clips for DaldongneAvatarMotion.PlayClip.</summary>
        public static string ImportArticulatedAnimations(bool male)
        {
            RequireEditMode();
            string sex = male ? "Male" : "Female";
            string folder = (male ? MeshyMaleImporter.Folder : MeshyFemaleImporter.Folder) + "/Animations";
            foreach (string action in new[] { "Walk", "Run", "Dance" })
            {
                string clipName = sex + action, sourcePath = folder + "/" + clipName + ".fbx";
                if (!File.Exists(sourcePath)) throw new FileNotFoundException("Articulated animation source missing.", sourcePath);
                AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (!source) throw new InvalidOperationException("Cannot load animation source: " + sourcePath);
                var importer = (ModelImporter)AssetImporter.GetAtPath(sourcePath);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                // Meshy motion sources have the original body rig. Finger mappings are optional here;
                // the target visual retains its independent, weighted finger rig.
                importer.humanDescription = DescribeHumanoid(source, false);
                importer.optimizeGameObjects = false;
                importer.importAnimation = true;
                importer.resampleCurves = true;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
                var takes = importer.defaultClipAnimations;
                if (takes.Length != 1) throw new InvalidDataException(sourcePath + " must contain exactly one animation take.");
                var take = takes[0];
                take.name = clipName;
                take.loopTime = true; take.loopPose = true;
                take.keepOriginalOrientation = true; take.keepOriginalPositionXZ = true; take.keepOriginalPositionY = true;
                take.lockRootRotation = true; take.lockRootPositionXZ = true; take.lockRootHeightY = true;
                importer.clipAnimations = new[] { take };
                importer.SaveAndReimport();
                var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath).GetComponentInChildren<Animator>(true);
                if (!avatar || !avatar.avatar || !avatar.avatar.isValid || !avatar.avatar.isHuman)
                    throw new InvalidOperationException(sourcePath + " has no valid Humanoid animation Avatar.");
                var imported = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>()
                    .SingleOrDefault(clip => clip.name == clipName && !clip.name.StartsWith("__preview__"));
                if (!imported || !imported.humanMotion || imported.length <= 0f)
                    throw new InvalidOperationException(sourcePath + " did not produce a usable Humanoid clip.");
                string assetPath = folder + "/" + clipName + ".anim";
                var clipAsset = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (clipAsset) EditorUtility.CopySerialized(imported, clipAsset);
                else
                {
                    clipAsset = UnityEngine.Object.Instantiate(imported);
                    AssetDatabase.CreateAsset(clipAsset, assetPath);
                }
                clipAsset.name = clipName; clipAsset.legacy = false; clipAsset.hideFlags = HideFlags.None;
                EditorUtility.SetDirty(clipAsset);
            }
            AssetDatabase.SaveAssets();
            return ValidateArticulatedAnimations(male);
        }

        public static string ValidateArticulatedAnimations(bool male)
        {
            string sex = male ? "Male" : "Female";
            string folder = (male ? MeshyMaleImporter.Folder : MeshyFemaleImporter.Folder) + "/Animations";
            var reports = new List<string>();
            foreach (string action in new[] { "Walk", "Run", "Dance" })
            {
                string path = folder + "/" + sex + action + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (!clip || clip.legacy || !clip.humanMotion || !clip.isLooping || !float.IsFinite(clip.length) || clip.length <= 0f)
                    throw new InvalidOperationException("Invalid looping Humanoid animation: " + path);
                var bindings = AnimationUtility.GetCurveBindings(clip);
                if (bindings.Length == 0) throw new InvalidOperationException("Animation has no curves: " + path);
                foreach (var binding in bindings)
                    if (AnimationUtility.GetEditorCurve(clip, binding).keys.Any(key => !float.IsFinite(key.time) || !float.IsFinite(key.value)))
                        throw new InvalidOperationException("Animation has a non-finite curve: " + path);
                reports.Add(clip.name + " " + clip.length.ToString("F2") + "s");
            }
            return "Validated three looping Humanoid clips: " + string.Join(", ", reports) + ".";
        }

        /// <summary>Retains the authored skeleton, skin weights and FBX Avatar; no runtime rig is reconstructed.</summary>
        public static string RebuildArticulated(bool male, bool migrateScenes = true)
        {
            RequireEditMode();
            string sourcePath = ArticulatedPath(male);
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Articulated character source missing.", sourcePath);
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source) throw new InvalidOperationException("Cannot load character source: " + sourcePath);
            var importer = (ModelImporter)AssetImporter.GetAtPath(sourcePath);
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.optimizeGameObjects = false;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 32;
            importer.minBoneWeight = 0f;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription = DescribeHumanoid(source, true);
            importer.SaveAndReimport();
            source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            var sourceAnimator = source.GetComponentInChildren<Animator>(true);
            if (!sourceAnimator || !sourceAnimator.avatar || !sourceAnimator.avatar.isValid || !sourceAnimator.avatar.isHuman)
                throw new InvalidOperationException("The imported model did not produce a valid Humanoid Avatar: " + sourcePath);

            string folder = male ? MeshyMaleImporter.Folder : MeshyFemaleImporter.Folder;
            var skinMaterial = SolidMaterial(folder + "/Skin.mat", "#E7B89D");
            var clothing = SolidMaterial(folder + (male ? "/Male.mat" : "/Female.mat"), "#858585");
            string visualPath = male ? ReferenceBoyImporter.VisualPath : VisualPath;
            var root = PrefabUtility.LoadPrefabContents(visualPath);
            try
            {
                // Keep the root and motion fileIDs already referenced by players and scenes.
                foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
                var animator = model.GetComponentInChildren<Animator>(true);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.sharedMesh || skin.sharedMesh.subMeshCount != 2)
                        throw new InvalidDataException("Articulated source must contain Skin and Clothing material slots.");
                    skin.sharedMaterials = new[] { skinMaterial, clothing };
                    skin.updateWhenOffscreen = false;
                    // Pose bounds cover raised hands, sitting and dance clips without changing mesh bounds used by QA.
                    var poseBounds = new Bounds(skin.transform.InverseTransformPoint(root.transform.TransformPoint(new Vector3(0f, .9f, 0f))), Vector3.zero);
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var point = new Vector3((corner & 1) == 0 ? -1.6f : 1.6f,
                            (corner & 2) == 0 ? -.7f : 2.5f, (corner & 4) == 0 ? -1.6f : 1.6f);
                        poseBounds.Encapsulate(skin.transform.InverseTransformPoint(root.transform.TransformPoint(point)));
                    }
                    skin.localBounds = poseBounds;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
                }
                var motion = root.GetComponent<DaldongneAvatarMotion>();
                if (!motion) throw new InvalidOperationException("Existing character visual has no motion component.");
                motion.ConfigureHumanoid(animator);
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
                motion.footSpacing = motion.strideScale = motion.liftScale = motion.swayScale = 1f;
                EditorUtility.SetDirty(motion);
                ValidateArticulated(root);
                PrefabUtility.SaveAsPrefabAsset(root, visualPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            if (male && File.Exists("Assets/Art/WorldDistricts/HandaeHQ/Lobby/Prefabs/15_NPCs.prefab"))
                global::HandaeLobbyNPCBuilder.RebindStaffPrefab();
            int migrated = migrateScenes ? ReconnectUnpackedSceneVisuals(male, visualPath) : 0;
            return ValidateArticulated(AssetDatabase.LoadAssetAtPath<GameObject>(visualPath), true, male)
                + " Migrated " + migrated + " unpacked scene visuals.";
        }

        static Material SolidMaterial(string path, string htmlColor)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP Lit shader is required.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            ColorUtility.TryParseHtmlString(htmlColor, out var color);
            material.SetTexture("_BaseMap", null); material.SetTexture("_MainTex", null);
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .18f); material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static bool IsArticulated(GameObject visual)
        {
            var motion = visual ? visual.GetComponent<DaldongneAvatarMotion>() : null;
            return motion && motion.rigAnimator;
        }

        public static string ValidateArticulated(GameObject visual, bool checkPlayerLinks = false, bool male = false)
        {
            if (!visual) throw new InvalidOperationException("Character visual missing.");
            var motion = visual.GetComponent<DaldongneAvatarMotion>();
            var animator = motion ? motion.rigAnimator : null;
            if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException(visual.name + ": valid Humanoid Avatar missing.");
            if (!motion.hips || !motion.head || !motion.leftArm || !motion.rightArm || !motion.leftForearm || !motion.rightForearm
                || !motion.leftHand || !motion.rightHand || !motion.leftLeg || !motion.rightLeg || !motion.leftKnee
                || !motion.rightKnee || !motion.leftFoot || !motion.rightFoot)
                throw new InvalidOperationException(visual.name + ": motion bone assignments missing.");
            var assignments = new Dictionary<HumanBodyBones, Transform> {
                { HumanBodyBones.Hips, motion.hips }, { HumanBodyBones.Head, motion.head },
                { HumanBodyBones.LeftUpperArm, motion.leftArm }, { HumanBodyBones.RightUpperArm, motion.rightArm },
                { HumanBodyBones.LeftLowerArm, motion.leftForearm }, { HumanBodyBones.RightLowerArm, motion.rightForearm },
                { HumanBodyBones.LeftHand, motion.leftHand }, { HumanBodyBones.RightHand, motion.rightHand },
                { HumanBodyBones.LeftUpperLeg, motion.leftLeg }, { HumanBodyBones.RightUpperLeg, motion.rightLeg },
                { HumanBodyBones.LeftLowerLeg, motion.leftKnee }, { HumanBodyBones.RightLowerLeg, motion.rightKnee },
                { HumanBodyBones.LeftFoot, motion.leftFoot }, { HumanBodyBones.RightFoot, motion.rightFoot }
            };
            if (assignments.Any(pair => pair.Value != animator.GetBoneTransform(pair.Key)))
                throw new InvalidOperationException(visual.name + ": motion assignments disagree with the Humanoid Avatar.");
            foreach (var pair in HumanoidBones)
            {
                var bone = animator.GetBoneTransform(pair.Key);
                if (!bone || bone.name != pair.Value)
                    throw new InvalidOperationException(visual.name + ": Humanoid mapping missing for " + pair.Key);
            }
            var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length != 1) throw new InvalidOperationException(visual.name + ": expected one continuous skinned renderer.");
            var skin = skins[0];
            var mesh = skin.sharedMesh;
            if (!mesh || !mesh.isReadable || !EditorUtility.IsPersistent(mesh))
                throw new InvalidOperationException(visual.name + ": persistent readable mesh missing.");
            int triangles = mesh.triangles.Length / 3;
            if (triangles <= 0 || triangles > ArticulatedTriangleBudget)
                throw new InvalidOperationException(visual.name + ": triangle budget exceeded: " + triangles);
            if (skin.bones.Any(b => !b) || mesh.bindposes.Length != skin.bones.Length)
                throw new InvalidOperationException(visual.name + ": skin bone/bindpose mismatch.");
            if (mesh.subMeshCount != 2 || skin.sharedMaterials.Length != 2
                || skin.sharedMaterials.Any(m => !m || !EditorUtility.IsPersistent(m) || m.GetTexture("_BaseMap")))
                throw new InvalidOperationException(visual.name + ": separate solid Skin/Clothing materials missing.");
            if (mesh.normals.Length != mesh.vertexCount || mesh.vertices.Any(v => !Finite(v)) || mesh.normals.Any(v => !Finite(v)))
                throw new InvalidOperationException(visual.name + ": invalid vertex or normal data.");
            foreach (var matrix in mesh.bindposes)
                for (int i = 0; i < 16; i++)
                    if (!float.IsFinite(matrix[i])) throw new InvalidOperationException(visual.name + ": non-finite bindpose.");
            var totals = new float[skin.bones.Length];
            {
                // Mesh owns these NativeArray views; disposing them would free borrowed storage.
                var counts = mesh.GetBonesPerVertex();
                var weights = mesh.GetAllBoneWeights();
                if (counts.Length != mesh.vertexCount) throw new InvalidOperationException(visual.name + ": skin weights missing.");
                int offset = 0;
                foreach (byte count in counts)
                {
                    float sum = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        if (offset >= weights.Length) throw new InvalidOperationException(visual.name + ": truncated skin weights.");
                        var weight = weights[offset++];
                        if (!float.IsFinite(weight.weight) || weight.weight < 0f || weight.boneIndex < 0 || weight.boneIndex >= totals.Length)
                            throw new InvalidOperationException(visual.name + ": invalid skin weight.");
                        sum += weight.weight; totals[weight.boneIndex] += weight.weight;
                    }
                    if (Mathf.Abs(sum - 1f) > .001f) throw new InvalidOperationException(visual.name + ": weights are not normalized.");
                }
                if (offset != weights.Length) throw new InvalidOperationException(visual.name + ": extra skin weights.");
            }
            int weightedFingers = 0;
            foreach (var pair in HumanoidBones.Where(p => p.Value.Contains("Hand") && p.Value.Last() >= '1' && p.Value.Last() <= '3'))
            {
                int index = Array.IndexOf(skin.bones, animator.GetBoneTransform(pair.Key));
                if (index < 0 || totals[index] <= 0f)
                    throw new InvalidOperationException(visual.name + ": finger has no mesh weights: " + pair.Value);
                weightedFingers++;
            }
            var vertices = mesh.vertices.Select(v => visual.transform.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
            float floor = vertices.Min(v => v.y), height = vertices.Max(v => v.y) - floor;
            if (Mathf.Abs(floor) > .025f || Mathf.Abs(height - BodySize.Height) > .025f)
                throw new InvalidOperationException(visual.name + ": expected feet at 0 and height " + BodySize.Height + "; floor=" + floor + ", height=" + height);
            if (checkPlayerLinks)
            {
                foreach (string path in PlayerPrefabs)
                {
                    var player = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var look = player ? player.GetComponent<DaldongnePlayerAppearance>() : null;
                    if (!look || !look.female || !look.male || !player.GetComponent<CharacterController>() || !player.GetComponent<PlayerMovement>())
                        throw new InvalidOperationException("Player components/appearance missing: " + path);
                    var linked = male ? look.male : look.female;
                    if (PrefabUtility.GetCorrespondingObjectFromSource(linked) != visual || !IsArticulated(linked))
                        throw new InvalidOperationException("Player visual connection missing: " + path);
                }
            }
            return visual.name + " validated: " + triangles + " triangles, " + skin.bones.Length + " skin bones, "
                + weightedFingers + " weighted finger joints, valid Humanoid Avatar, height " + height.ToString("F3") + " m.";
        }

        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        /// <summary>Repairs only NPC dialogue head references affected by replacing the shared visual skeleton.</summary>
        public static string ReconnectArticulatedNpcAnchors()
        {
            RequireEditMode();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            int anchors = 0;
            var changedAssets = new List<string>();
            try
            {
                foreach (string path in NpcVisualUsers)
                {
                    if (!File.Exists(path)) continue;
                    var dependencies = AssetDatabase.GetDependencies(path, false);
                    if (!dependencies.Contains(VisualPath) && !dependencies.Contains(ReferenceBoyImporter.VisualPath)) continue;
                    if (path.EndsWith(".prefab", StringComparison.Ordinal))
                    {
                        var root = PrefabUtility.LoadPrefabContents(path);
                        try
                        {
                            int count = ReconnectNpcHeads(root);
                            if (count == 0) continue;
                            PrefabUtility.SaveAsPrefabAsset(root, path);
                            anchors += count; changedAssets.Add(path);
                        }
                        finally { PrefabUtility.UnloadPrefabContents(root); }
                    }
                    else
                    {
                        var scene = SceneManager.GetSceneByPath(path);
                        bool opened = !scene.IsValid() || !scene.isLoaded;
                        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                        try
                        {
                            int count = scene.GetRootGameObjects().Sum(ReconnectNpcHeads);
                            if (count == 0) continue;
                            EditorSceneManager.MarkSceneDirty(scene);
                            EditorSceneManager.SaveScene(scene);
                            anchors += count; changedAssets.Add(path);
                        }
                        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
                    }
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
            return new JObject { ["updatedAnchors"] = anchors, ["changedAssets"] = new JArray(changedAssets) }.ToString();
        }

        static int ReconnectNpcHeads(GameObject root)
        {
            int count = 0;
            foreach (var data in root.GetComponentsInChildren<DialogueData>(true))
            {
                // Authored standalone anchors have their own placement/offset and are not rig bones.
                if (data.head && data.head.name == "SpeechAnchor") continue;
                var motions = data.GetComponentsInChildren<DaldongneAvatarMotion>(true).Where(motion => {
                    if (!motion.HasHumanoidRig || motion.GetComponentInParent<DialogueData>(true) != data) return false;
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(motion.gameObject);
                    string path = source ? AssetDatabase.GetAssetPath(source) : null;
                    return path == VisualPath || path == ReferenceBoyImporter.VisualPath;
                }).ToArray();
                if (motions.Length == 0) continue;
                // Any valid head outside the replaced visual remains an authored attachment.
                if (data.head && !motions.Any(motion => data.head.IsChildOf(motion.transform))) continue;
                if (motions.Length != 1)
                    throw new InvalidOperationException("NPC head repair requires one shared character visual: " + data.npcId);
                var anchor = data.transform.Find("SpeechAnchor");
                var target = !data.head && anchor ? anchor : motions[0].head;
                if (!target) target = motions[0].rigAnimator.GetBoneTransform(HumanBodyBones.Head);
                if (!target) throw new InvalidOperationException("New Humanoid head missing for NPC: " + data.npcId);
                if (data.head == target) continue;
                data.head = target;
                EditorUtility.SetDirty(data);
                if (PrefabUtility.IsPartOfPrefabInstance(data)) PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                count++;
            }
            return count;
        }

        static int ReconnectUnpackedSceneVisuals(bool male,string visualPath)
        {
            int count=0;var setup=EditorSceneManager.GetSceneManagerSetup();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
            try
            {
                foreach(string path in Scenes)
                {
                    var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid() || !scene.isLoaded;
                    if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                    bool dirty=false;
                    foreach(var look in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DaldongnePlayerAppearance>(true)))
                    {
                        var old=male?look.male:look.female;
                        if(!old)throw new InvalidOperationException("Character reference missing in "+path);
                        if(PrefabUtility.GetCorrespondingObjectFromSource(old)==prefab)continue;
                        var replacement=(GameObject)PrefabUtility.InstantiatePrefab(prefab,old.transform.parent);
                        replacement.transform.localPosition=old.transform.localPosition;
                        replacement.transform.localRotation=old.transform.localRotation;
                        replacement.transform.localScale=old.transform.localScale;
                        replacement.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
                        if(male)look.male=replacement;else look.female=replacement;
                        replacement.SetActive(look.selected==(male?DaldongnePlayerAppearance.Variant.Male:DaldongnePlayerAppearance.Variant.Female));
                        UnityEngine.Object.DestroyImmediate(old);EditorUtility.SetDirty(look);dirty=true;count++;
                    }
                    if(dirty){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
                    if(opened)EditorSceneManager.CloseScene(scene,true);
                }
            }
            finally {EditorSceneManager.RestoreSceneManagerSetup(setup);}
            return count;
        }

        [MenuItem("Tools/Company Game/Characters/Validate Female Character")]
        public static void ValidateMenu() => Debug.Log(Validate());
        public static string Validate() => Validate(false);

        public static string Validate(bool male)
        {
            var articulated = AssetDatabase.LoadAssetAtPath<GameObject>(male ? ReferenceBoyImporter.VisualPath : VisualPath);
            if (IsArticulated(articulated)) return ValidateArticulated(articulated, true, male);
            if(male && AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceBoyImporter.VisualPath).GetComponentInChildren<SkinnedMeshRenderer>(true))
                return MeshyMaleImporter.Validate();
            if(!male && AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath).GetComponentInChildren<SkinnedMeshRenderer>(true))
                return MeshyFemaleImporter.Validate();
            throw new InvalidOperationException("Character visual is not a Meshy body.");
        }
    }
}
