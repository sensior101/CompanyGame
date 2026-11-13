using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.Pets;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CompanyGame.Editor.Pets
{
    /// <summary>
    /// Imports the dog built by ArtSource/Pets/Dog_20261110/build_dog.py as a Generic rig,
    /// splits its baked takes into clips, and (re)builds Dog.controller and Dog.prefab.
    /// Rebuilding keeps the GUIDs of the controller, prefab and materials.
    /// </summary>
    public static class PetDogImporter
    {
        public const string Folder = "Assets/Art/Pets/Dog";
        public const string SourcePath = Folder + "/Dog.fbx";
        public const string ControllerPath = Folder + "/Dog.controller";
        public const string PrefabPath = Folder + "/Dog.prefab";

        static readonly (string name, bool loop)[] Clips =
        {
            ("Idle", true), ("Walk", true), ("Run", true), ("SitDown", false), ("Sit", true), ("StandUp", false), ("Attack", false),
        };

        // Same order as the Blender material slots (submesh order in the FBX).
        static readonly (string name, string hex)[] Palette =
        {
            ("Dog_Fur", "#BE8450"), ("Dog_Cream", "#F0E2C8"), ("Dog_Dark", "#2E2724"),
            ("Dog_Collar", "#B4553F"), ("Dog_Tag", "#D8B04A"), ("Dog_InnerEar", "#E2B49A"),
        };

        [MenuItem("Tools/Company Game/Pets/Rebuild Dog")]
        static void Menu() => Debug.Log(Rebuild());

        /// <summary>Entry point for -batchmode -executeMethod.</summary>
        public static void RebuildBatch()
        {
            try
            {
                Debug.Log("PET_DOG_RESULT " + Rebuild());
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static string Rebuild()
        {
            if (!File.Exists(SourcePath)) throw new FileNotFoundException("Dog source missing; run build_dog.py and copy Dog.fbx.", SourcePath);
            AssetDatabase.ImportAsset(SourcePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(SourcePath);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.importAnimation = true;
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            importer.SaveAndReimport();

            var takes = importer.defaultClipAnimations;
            var split = new List<ModelImporterClipAnimation>();
            foreach (var (name, loop) in Clips)
            {
                var take = takes.FirstOrDefault(t => t.takeName.EndsWith("Dog_" + name, StringComparison.Ordinal));
                if (take == null)
                    throw new InvalidDataException($"Take Dog_{name} missing from {SourcePath}. Takes: {string.Join(", ", takes.Select(t => t.takeName))}");
                take.name = name;
                take.loopTime = loop;
                take.loopPose = loop;
                take.lockRootRotation = take.lockRootHeightY = take.lockRootPositionXZ = true;
                take.keepOriginalOrientation = take.keepOriginalPositionY = take.keepOriginalPositionXZ = true;
                split.Add(take);
            }
            importer.clipAnimations = split.ToArray();
            importer.SaveAndReimport();

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToDictionary(c => c.name);
            foreach (var (name, _) in Clips)
                if (!clips.ContainsKey(name)) throw new InvalidDataException("Clip not imported: " + name);

            var materials = Palette.Select(p => SolidMaterial(Folder + "/Materials/" + p.name + ".mat", p.hex)).ToArray();
            var controller = BuildController(clips);
            BuildPrefab(source, controller, materials);
            AssetDatabase.SaveAssets();
            return Validate();
        }

        static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!ctrl) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);
            foreach (var t in sm.anyStateTransitions.ToArray()) sm.RemoveAnyStateTransition(t);
            foreach (var tree in AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().ToArray())
            {
                AssetDatabase.RemoveObjectFromAsset(tree);
                UnityEngine.Object.DestroyImmediate(tree, true);
            }
            ctrl.parameters = new[]
            {
                new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 0f },
                new AnimatorControllerParameter { name = "MotionScale", type = AnimatorControllerParameterType.Float, defaultFloat = 1f },
                new AnimatorControllerParameter { name = "Sit", type = AnimatorControllerParameterType.Bool },
                new AnimatorControllerParameter { name = "Attack", type = AnimatorControllerParameterType.Trigger },
            };

            var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree blend, 0);
            blend.blendType = BlendTreeType.Simple1D;
            blend.blendParameter = "Speed";
            blend.useAutomaticThresholds = false;
            blend.AddChild(clips["Idle"], 0f);
            blend.AddChild(clips["Walk"], PetDogAnimator.WalkClipSpeed);
            blend.AddChild(clips["Run"], PetDogAnimator.RunClipSpeed);
            loco.speedParameter = "MotionScale";
            loco.speedParameterActive = true;
            sm.defaultState = loco;

            var sitDown = State(sm, "SitDown", clips);
            var sit = State(sm, "Sit", clips);
            var standUp = State(sm, "StandUp", clips);
            var attack = State(sm, "Attack", clips);

            Link(loco, sitDown, .15f).AddCondition(AnimatorConditionMode.If, 0f, "Sit");
            Link(sitDown, sit, 0f, 1f);
            Link(sit, standUp, .1f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Sit");
            Link(standUp, loco, .12f, 1f);
            Link(loco, attack, .08f).AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            Link(attack, loco, .12f, .95f);
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static AnimatorState State(AnimatorStateMachine sm, string name, Dictionary<string, AnimationClip> clips)
        {
            var s = sm.AddState(name);
            s.motion = clips[name];
            s.writeDefaultValues = true;
            return s;
        }

        static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration, float exitTime = -1f)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = exitTime >= 0f;
            if (t.hasExitTime) t.exitTime = exitTime;
            t.duration = duration;
            t.hasFixedDuration = true;
            return t;
        }

        static void BuildPrefab(GameObject source, AnimatorController controller, Material[] materials)
        {
            var root = new GameObject("Dog");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                var animator = model.GetComponent<Animator>();
                if (!animator) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.sharedMesh || skin.sharedMesh.subMeshCount != materials.Length)
                        throw new InvalidDataException($"Dog mesh must have {materials.Length} material slots.");
                    skin.sharedMaterials = materials;
                    // Cover sitting, galloping and the attack lunge so the pet is not culled mid-motion.
                    var b = new Bounds(skin.transform.InverseTransformPoint(root.transform.TransformPoint(new Vector3(0f, .35f, 0f))), Vector3.zero);
                    for (int corner = 0; corner < 8; corner++)
                        b.Encapsulate(skin.transform.InverseTransformPoint(root.transform.TransformPoint(new Vector3(
                            (corner & 1) == 0 ? -.45f : .45f, (corner & 2) == 0 ? -.05f : .95f, (corner & 4) == 0 ? -.75f : .75f))));
                    skin.localBounds = b;
                    skin.updateWhenOffscreen = false;
                }
                var driver = root.AddComponent<PetDogAnimator>();
                var so = new SerializedObject(driver);
                so.FindProperty("animator").objectReferenceValue = animator;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static Material SolidMaterial(string path, string htmlColor)
        {
            string dir = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(dir).Replace('\\', '/'), Path.GetFileName(dir));
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP Lit shader is required.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            ColorUtility.TryParseHtmlString(htmlColor, out var color);
            material.SetTexture("_BaseMap", null);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .12f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static string Validate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) throw new InvalidOperationException("Dog prefab missing.");
            var animator = prefab.GetComponentInChildren<Animator>(true);
            if (!animator || !animator.avatar || !animator.avatar.isValid || animator.avatar.isHuman)
                throw new InvalidOperationException("Dog needs a valid Generic avatar.");
            if (!prefab.GetComponent<PetDogAnimator>()) throw new InvalidOperationException("Dog prefab has no PetDogAnimator.");
            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var mesh = skin.sharedMesh;
            int tris = mesh.triangles.Length / 3;
            if (skin.bones.Length < 26) throw new InvalidDataException("Dog skin has " + skin.bones.Length + " bones, expected 26.");
            var reports = new List<string>();
            var clips = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToDictionary(c => c.name);
            foreach (var (name, loop) in Clips)
            {
                var clip = clips[name];
                if (clip.isLooping != loop || clip.length <= 0f || clip.humanMotion)
                    throw new InvalidDataException($"Clip {name}: loop={clip.isLooping} length={clip.length} human={clip.humanMotion}");
                reports.Add($"{name} {clip.length:F2}s{(loop ? " loop" : "")}");
            }
            // Height from the bind pose in prefab space.
            var bounds = new Bounds();
            bool first = true;
            foreach (var v in mesh.vertices)
            {
                var w = prefab.transform.InverseTransformPoint(skin.transform.TransformPoint(v));
                if (first) { bounds = new Bounds(w, Vector3.zero); first = false; } else bounds.Encapsulate(w);
            }
            return $"Dog: {tris} triangles, {skin.bones.Length} bones, {mesh.subMeshCount} materials, " +
                   $"size {bounds.size.x:F2}x{bounds.size.y:F2}x{bounds.size.z:F2} m (min y {bounds.min.y:F3}, front z {bounds.max.z:F2}). " +
                   "Clips: " + string.Join(", ", reports) + ".";
        }
    }
}
