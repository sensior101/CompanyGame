using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CompanyGame.Daldongne;
using CompanyGame.Editor.Characters;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Disposable Play-mode combat acceptance. Uses real frame clocks, physics, authored avatars and camera.</summary>
[InitializeOnLoad]
public static class CombatRuntimeQA
{
    const string Key = "CompanyGame.CombatRuntimeQA";
    const string Folder = "Library/CombatQA";
    [Serializable] public sealed class Report
    {
        public int stage, frame;
        public bool done, passed;
        public float stageTime;
        public string error = "";
        public List<string> checks = new List<string>();
        public List<string> contactMeasurements = new List<string>();
    }
    static PlayerCombat combat;
    static PlayerMovement player;
    static Damageable target;
    static PlayerHitReaction victim;
    static GameObject dummy, wall, floor;
    static Vector3 originalSpawn;
    static int originalSlot;
    static PlayerCameraController camera;
    static float originalPitch, originalYaw, originalHeight, originalDistance;
    static float targetStartZ, attackPoseTime;
    static bool buffered, checkedContact;
    static bool originalFirstPerson;
    static uint firstSequence;
    static InventoryState temporarilyHeld;
    static bool cameraCaptured;
    static DaldongneAvatarMotion clipMotion;
    static GameObject testLight;
    static string queuedCapture;
    static int captureFrame;
    static string previousPersonalDirectory, previousWorldPath, isolatedDirectory;
    static bool handSmoke, handSmokeMale;
    static string outputFolder = Folder;
    static DaldongnePlayerAppearance appearance;
    static DaldongnePlayerAppearance.Variant originalVariant;
    static CombatRuntimeQA() { EditorApplication.update += Tick; }
    public static string Status() => SessionState.GetString(Key, "Not started");
    /// <summary>Actual authored hand skin in disposable preview scenes; no player, profile, inventory or source asset edits.</summary>
    public static string CaptureHandStudy(string label)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before hand preview.");
        if (string.IsNullOrEmpty(label) || Array.Exists(label.ToCharArray(), c => !char.IsLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Use a simple hand-study label.");
        string folder = Path.GetFullPath(Path.Combine(Folder, "Hands", label));
        Directory.CreateDirectory(folder);
        var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty;
        var coordinates = new System.Text.StringBuilder("sex\tside\tpose\tbone\thandLocalPosition\tlocalRotation\tchildren\n");
        foreach (bool male in new[] { false, true })
        {
            string sex = male ? "Male" : "Female";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(male ? ReferenceBoyImporter.VisualPath : ReferenceGirlImporter.VisualPath);
            var preview = new PreviewRenderUtility();
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview.camera.scene);
                preview.AddSingleGO(visual); visual.SetActive(true);
                var motion = visual.GetComponent<DaldongneAvatarMotion>();
                motion.ConfigureHumanoid(motion.rigAnimator); motion.enabled = false;
                foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.updateWhenOffscreen = true;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.22f, .25f, .29f, 1f);
                preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 30f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35, -35, 0); preview.lights[0].intensity = 1.2f;
                preview.lights[1].transform.rotation = Quaternion.Euler(20, 140, 0); preview.lights[1].intensity = .8f;
                preview.ambientColor = new Color(.55f, .55f, .55f);
                foreach (bool right in new[] { false, true })
                {
                    motion.SetFingerPose(right, 0f);
                    Transform arm = right ? motion.rightArm : motion.leftArm;
                    Transform forearm = right ? motion.rightForearm : motion.leftForearm;
                    Transform hand = right ? motion.rightHand : motion.leftHand;
                    Vector3 side = right ? Vector3.right : Vector3.left;
                    arm.rotation = Quaternion.FromToRotation(forearm.position - arm.position, side) * arm.rotation;
                    forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position, side) * forearm.rotation;
                    int first = (int)(right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
                    Transform Bone(int index) => motion.rigAnimator.GetBoneTransform((HumanBodyBones)(first + index));
                    Vector3 along = (Bone(6).position - hand.position).normalized;
                    Vector3 across = (Bone(12).position - Bone(3).position).normalized;
                    Vector3 palm = Vector3.Cross(across, along).normalized;
                    if (Vector3.Dot(palm, motion.hips.position - hand.position) < 0f) palm = -palm;
                    var directions = new[] { palm, -palm, along, across };
                    var names = new[] { "Palm", "Back", "Front", "Side" };
                    foreach (string pose in new[] { "Open", "Hold", "Point", "Fist", "CombatFist" })
                    {
                        motion.SetFingerPose(right, pose == "Open" || pose == "CombatFist" ? 0f : pose == "Hold" ? .55f : 1f, pose == "Point");
                        if (pose == "CombatFist")
                        {
                            var fingers = typeof(DaldongneAvatarMotion).GetField(right ? "fingersR" : "fingersL", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(motion);
                            typeof(DaldongneAvatarMotion).GetMethod("ApplyCombatFist", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { fingers, (object)1f });
                        }
                        string prefix = sex + (right ? "Right" : "Left") + pose;
                        coordinates.AppendLine(sex + "\t" + (right ? "R" : "L") + "\t" + pose + "\tWrist\t(0,0,0)\t" + hand.localRotation.ToString("F5") + "\t" + hand.childCount);
                        for (int i = 0; i < 15; i++)
                        {
                            var bone = Bone(i);
                            coordinates.AppendLine(sex + "\t" + (right ? "R" : "L") + "\t" + pose + "\t" + ((HumanBodyBones)(first + i))
                                + "\t" + hand.InverseTransformPoint(bone.position).ToString("F5") + "\t" + bone.localRotation.ToString("F5") + "\t" + bone.childCount);
                        }
                        var bounds = (Bounds)HandStudyPreview("PreviewHandBounds", motion, right);
                        for (int angle = 0; angle < directions.Length; angle++)
                        {
                            HandStudyPreview("PreviewFrame", preview.camera, bounds, directions[angle], 1f);
                            HandStudyPreview("PreviewSave", preview, Path.Combine(folder, prefix + names[angle] + ".png"), 800, 800);
                        }
                    }
                }
            }
            finally { preview.Cleanup(); }
        }
        File.WriteAllText(Path.Combine(folder, "hand-joints.tsv"), coordinates.ToString());
        if (SceneManager.GetActiveScene() != scene || scene.isDirty != dirty) throw new InvalidOperationException("Hand study changed active scene state.");
        return folder;
    }

    static object HandStudyPreview(string method, params object[] args) => typeof(CharacterQA)
        .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

    public static string Start()
    {
        handSmoke = false; outputFolder = Folder;
        return StartAcceptance();
    }

    /// <summary>Focused hand acceptance: contact/miss plus both camera views of all three strikes.</summary>
    public static string StartHandSmoke(bool male, string label)
    {
        if (string.IsNullOrEmpty(label) || Array.Exists(label.ToCharArray(), c => !char.IsLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Use a simple hand-smoke label.");
        handSmoke = true; handSmokeMale = male;
        outputFolder = Path.Combine(Folder, "Hands", label, male ? "MaleRuntime" : "FemaleRuntime");
        return StartAcceptance();
    }

    static string StartAcceptance()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        if (SyncClient.Instance && SyncClient.Instance.Connected) throw new InvalidOperationException("Run offline acceptance before network QA.");
        if (isolatedDirectory != null) throw new InvalidOperationException("Combat acceptance is already running.");
        buffered = checkedContact = cameraCaptured = false; queuedCapture = null; appearance = null;
        previousPersonalDirectory = PersonalSave.DirOverride; previousWorldPath = WorldSave.PathOverride;
        isolatedDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CompanyGame-CombatRuntimeQA-" + Guid.NewGuid().ToString("N")));
        PersonalSave.DirOverride = Path.Combine(isolatedDirectory, "player");
        WorldSave.PathOverride = Path.Combine(isolatedDirectory, "world.json");
        Store(new Report());
        return "Combat Play-mode acceptance started";
    }
    static void Store(Report report) => SessionState.SetString(Key, JsonUtility.ToJson(report));
    static void Check(Report report, bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        report.checks.Add(description);
    }
    static void Next(Report report) { report.stage++; report.stageTime = Time.time; Store(report); }
    static void SetPrivate(object value, string field, object data) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, data);
    static void Capture(string name)
    { queuedCapture = name; captureFrame = Time.frameCount; }
    static void RenderCapture(string name)
    {
        if (name.StartsWith("combo-", StringComparison.Ordinal))
        {
            var report = JsonUtility.FromJson<Report>(Status());
            foreach (var animator in player.GetComponentsInChildren<Animator>())
            {
                if (!animator.isHuman || !animator.gameObject.activeInHierarchy) continue;
                bool right = name == "combo-1";
                var hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                var knuckle = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                if (!hand) continue;
                Vector3 fist = knuckle ? knuckle.position : hand.position;
                float gap = Vector3.Distance(fist, dummy.GetComponent<CapsuleCollider>().ClosestPoint(fist));
                report.contactMeasurements.Add(name + " rootDistance=" + Vector3.Distance(player.transform.position, dummy.transform.position).ToString("F3")
                    + " fistToCapsule=" + gap.ToString("F3") + " fistToHitPoint=" + Vector3.Distance(fist, combat.LastAttackEnd).ToString("F3")
                    + " fistLocal=" + player.transform.InverseTransformPoint(fist).ToString("F3"));
                Store(report); break;
            }
        }
        Directory.CreateDirectory(outputFolder);
        var view = player.viewCamera;
        var position = view.transform.position; var rotation = view.transform.rotation;
        var previousTarget = view.targetTexture; var previousActive = RenderTexture.active;
        var texture = RenderTexture.GetTemporary(1280, 720, 24);
        Texture2D image = null;
        try
        {
            if (!camera.firstPerson)
            {
                view.transform.position = player.transform.position + new Vector3(2.3f, 1.65f, -1.25f);
                view.transform.LookAt(player.transform.position + new Vector3(0, 1f, .7f));
            }
            view.targetTexture = texture; view.Render(); RenderTexture.active = texture;
            image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(outputFolder, name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            view.targetTexture = previousTarget; RenderTexture.active = previousActive;
            view.transform.SetPositionAndRotation(position, rotation);
            RenderTexture.ReleaseTemporary(texture); if (image) Object.Destroy(image);
        }
    }
    static void Tick()
    {
        string json = SessionState.GetString(Key, "");
        if (string.IsNullOrEmpty(json) || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
        if (queuedCapture != null && Time.frameCount > captureFrame)
        { string name = queuedCapture; queuedCapture = null; RenderCapture(name); }
        var report = JsonUtility.FromJson<Report>(Status());
        if (report.done || report.frame == Time.frameCount) return;
        report.frame = Time.frameCount;
        try
        {
            if (report.stage > 0 && Time.time - report.stageTime > 12f) throw new TimeoutException("Combat stage " + report.stage);
            if (report.stage == 0)
            {
                player = PlayerSpawner.Player;
                if (!player || !player.viewCamera || SceneLoadManager.IsLoading) return;
                combat = player.GetComponent<PlayerCombat>(); camera = player.viewCamera.GetComponent<PlayerCameraController>();
                Check(report, combat && player.GetComponent<PlayerHitReaction>() && player.GetComponent<CombatFeedback>(), "Shared player owns combat/reaction/feedback components");
                originalSpawn = player.spawn; originalSlot = InventoryManager.Instance.State.SelectedHotbarIndex;
                var inventory = InventoryManager.Instance.State;
                int empty = -1;
                for (int i = 0; i < InventoryState.HotbarSize; i++) if (inventory.GetSlot(i).IsEmpty) { empty = i; break; }
                if (empty < 0)
                {
                    empty = InventoryState.HotbarSize - 1;
                    temporarilyHeld = new InventoryState();
                    Check(report, inventory.TryTransferTo(temporarilyHeld, empty, inventory.GetSlot(empty).Count, out _),
                        "Occupied test slot is preserved in a temporary inventory through existing transfer API");
                }
                inventory.SelectHotbar(empty);
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Disposable combat test floor";
                floor.transform.position = new Vector3(0, 40, 0); floor.transform.localScale = new Vector3(30, .2f, 30);
                testLight = new GameObject("Disposable combat evidence light");
                var light = testLight.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
                testLight.transform.rotation = Quaternion.Euler(45, -35, 0);
                player.spawn = new Vector3(0, 40.11f, 0); player.ResetToSpawn(); player.transform.rotation = Quaternion.identity;
                originalPitch = (float)typeof(PlayerCameraController).GetField("pitch", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(camera);
                originalYaw = (float)typeof(PlayerCameraController).GetField("yaw", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(camera);
                originalHeight = camera.targetHeight; originalDistance = camera.distance;
                cameraCaptured = true;
                originalFirstPerson = camera.firstPerson;
                if (handSmoke)
                {
                    appearance = player.GetComponent<DaldongnePlayerAppearance>();
                    Check(report, appearance, "Existing appearance owner selects the hand-study avatar");
                    originalVariant = appearance.selected;
                    appearance.Select(handSmokeMale ? DaldongnePlayerAppearance.Variant.Male : DaldongnePlayerAppearance.Variant.Female);
                }
                camera.targetHeight = 1.3f; camera.distance = 3f; camera.firstPerson = false;
                typeof(PlayerCameraController).GetMethod("UpdatePlayerVisuals", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(camera, null);
                SetPrivate(camera, "pitch", 0f); SetPrivate(camera, "yaw", 0f);
                dummy = Object.Instantiate(Resources.Load<GameObject>(AvatarSync.PrefabKey)); dummy.name = "Disposable combat avatar";
                dummy.transform.SetPositionAndRotation(new Vector3(0, 40.11f, .82f), Quaternion.Euler(0, 180, 0));
                var male = dummy.transform.Find("MaleVisual"); var female = dummy.transform.Find("FemaleVisual");
                if (female) female.gameObject.SetActive(false); if (male) male.gameObject.SetActive(true);
                var capsule = dummy.AddComponent<CapsuleCollider>(); capsule.center = Vector3.up * .9f; capsule.height = 1.8f; capsule.radius = .3f;
                var duplicate = new GameObject("Duplicate hit collider"); duplicate.transform.SetParent(dummy.transform, false);
                var box = duplicate.AddComponent<BoxCollider>(); box.center = Vector3.up * 1.3f; box.size = new Vector3(.4f, .4f, .3f);
                target = dummy.AddComponent<Damageable>(); victim = dummy.AddComponent<PlayerHitReaction>();
                victim.normalKnockback = .15f;
                targetStartZ = dummy.transform.position.z;
                Physics.SyncTransforms();
                Next(report); return;
            }
            if (report.stage == 1 && Time.time - report.stageTime > .25f)
            {
                Check(report, combat.TryAttack() && combat.ComboStep == 0, "First press starts Punch_Jab");
                firstSequence = combat.SwingSequence;
                Check(report, target.CurrentHealth == 100f && !combat.LastAttackDealtDamage, "Wind-up applies no immediate damage");
                Check(report, !combat.TryAttack() && combat.SwingSequence == firstSequence, "Early repeated press cannot restart swing");
                Next(report); return;
            }
            if (report.stage >= 2 && report.stage <= 4)
            {
                int step = report.stage - 2;
                if (step < 2 && combat.ComboStep == step + 1)
                {
                    Check(report, checkedContact && buffered, "Buffered strike advances combo in order");
                    checkedContact = buffered = false; Next(report); return;
                }
                if (combat.ComboStep != step) throw new InvalidOperationException("Unexpected combo step " + combat.ComboStep + " expected " + step);
                if (combat.LastAttackDealtDamage && !checkedContact)
                {
                    float expected = step == 0 ? 90f : step == 1 ? 78.5f : 63.5f;
                    Check(report, Mathf.Abs(target.CurrentHealth - expected) < .01f, "Step " + step + " hits once despite multiple target colliders");
                    Check(report, player.GetComponent<PlayerHitReaction>().IsFrozen && Time.timeScale == 1f, "Step " + step + " hitstop is character-local");
                    checkedContact = true; Capture("combo-" + step);
                    attackPoseTime = combat.SwingNormalizedTime;
                }
                if (step < 2 && combat.IsSwinging && combat.SwingNormalizedTime > .75f && !buffered)
                {
                    // A player must follow normal knockback to keep the next fist within physical reach.
                    float follow = Mathf.Max(0f, dummy.transform.position.z - player.transform.position.z - .82f);
                    player.GetComponent<CharacterController>().Move(Vector3.forward * follow);
                    Check(report, combat.TryAttack() && !combat.TryAttack(), "Step " + step + " accepts one buffered follow-up only");
                    buffered = true;
                }
                // The next strike can start in Update before this Editor tick; observe it at the top of the next frame.
                if (checkedContact && (step < 2 ? combat.SwingNormalizedTime > .96f : !combat.IsSwinging))
                {
                    if (step < 2) { /* advanced below when the pending next step appears */ }
                    else { Next(report); checkedContact = buffered = false; }
                }
            }
            if (report.stage == 5 && Time.time - report.stageTime > .8f)
            {
                Check(report, dummy.transform.position.z - targetStartZ > .4f && dummy.transform.position.z - targetStartZ < 1.2f,
                    "Normal and heavy knockback moved the victim by bounded distances");
                dummy.transform.position = player.transform.position + new Vector3(0, 0, 1.05f);
                combat.CancelSwing(); SetPrivate(combat, "nextAttackTime", 0f);
                Check(report, combat.TryAttack(), "Aimed miss starts a new jab");
                Next(report); return;
            }
            if (report.stage == 6 && !combat.IsSwinging)
            {
                Check(report, !combat.LastAttackDealtDamage && !player.GetComponent<PlayerHitReaction>().IsFrozen && target.CurrentHealth == 63.5f,
                    "Target beyond physical fist reach is missed: no damage, hitstop or impact reaction");
                if (handSmoke)
                {
                    camera.firstPerson = true;
                    typeof(PlayerCameraController).GetMethod("UpdatePlayerVisuals", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(camera, null);
                    dummy.transform.position = player.transform.position + Vector3.right * 4f;
                    combat.CancelSwing(); SetPrivate(combat, "nextAttackTime", 0f);
                    report.stage = 100; report.stageTime = Time.time; Store(report); return;
                }
                dummy.transform.position = player.transform.position + new Vector3(0, 0, 1.2f);
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Disposable melee wall";
                wall.transform.position = player.transform.position + new Vector3(0, 1, .65f); wall.transform.localScale = new Vector3(2, 2, .12f);
                Physics.SyncTransforms(); combat.CancelSwing(); SetPrivate(combat, "nextAttackTime", 0f); combat.TryAttack();
                Next(report); return;
            }
            if (report.stage == 7 && !combat.IsSwinging)
            {
                Check(report, !combat.LastAttackDealtDamage && target.CurrentHealth == 63.5f, "Wall blocks melee before receiver");
                wall.transform.position = dummy.transform.position + new Vector3(0, 1, .42f);
                Physics.SyncTransforms(); targetStartZ = dummy.transform.position.z;
                victim.ReceiveHit(Vector3.forward, dummy.transform.position + Vector3.up * 1.6f, true, 15, true);
                Next(report); return;
            }
            if (report.stage == 8 && Time.time - report.stageTime > .4f)
            {
                Check(report, victim.PoseState == "Knockdown" && dummy.transform.position.z - targetStartZ < .14f,
                    "Forced knockdown is visible and heavy knockback stops at nearby wall");
                wall.GetComponent<Renderer>().enabled = false;
                Capture("knockdown"); Next(report); return;
            }
            if (report.stage == 9 && victim.PoseState == "GetUp")
            {
                Check(report, victim.BlocksActions, "GetUp blocks attacks until recovery completes"); Capture("get-up"); Next(report); return;
            }
            if (report.stage == 10 && !victim.IsReacting)
            {
                Check(report, !victim.BlocksActions, "Nonlethal knockdown returns to normal control");
                camera.firstPerson = true;
                typeof(PlayerCameraController).GetMethod("UpdatePlayerVisuals", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(camera, null);
                dummy.transform.position = player.transform.position + Vector3.right * 4f;
                if (wall) Object.Destroy(wall);
                combat.CancelSwing(); SetPrivate(combat, "nextAttackTime", 0f); combat.TryAttack();
                Next(report); return;
            }
            if (report.stage == 11 && combat.SwingNormalizedTime > .3f)
            {
                var held = player.GetComponent<PlayerHeldItem>();
                Check(report, held.IsFirstPersonVisible && held.FirstPersonLeftArmRoot && held.FirstPersonLeftArmRoot.gameObject.activeInHierarchy,
                    "First person jab renders an actual left arm");
                Capture("first-person-jab"); Next(report); return;
            }
            if (report.stage == 12 && !combat.IsSwinging)
            { Check(report, combat.TryAttack() && combat.ComboStep == 1, "First person second strike is straight"); Next(report); return; }
            if (report.stage == 13 && combat.SwingNormalizedTime > .3f)
            {
                var held = player.GetComponent<PlayerHeldItem>();
                Check(report, held.IsFirstPersonVisible && held.FirstPersonArmRoot && held.FirstPersonArmRoot.gameObject.activeInHierarchy,
                    "First person straight renders the actual right arm");
                Capture("first-person-straight"); Next(report); return;
            }
            if (report.stage == 14 && !combat.IsSwinging)
            {
                CollisionChecks(report);
                foreach (var motion in player.GetComponentsInChildren<DaldongneAvatarMotion>())
                    if (motion.isActiveAndEnabled && motion.HasHumanoidRig) { clipMotion = motion; break; }
                Check(report, clipMotion, "Active authored humanoid supports optional combat clip smoke test");
                bool female = clipMotion.name.IndexOf("Female", StringComparison.OrdinalIgnoreCase) >= 0;
                string clipPath = "Assets/Art/Daldongne/Players/" + (female ? "MeshyFemale/Animations/FemaleWalk.anim" : "MeshyMale/Animations/MaleWalk.anim");
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                Check(report, clip, "Optional clip smoke test reuses existing authored walk clip without saving it");
                clipMotion.PlayClip(clip, true); clipMotion.Punch_Jab = clip;
                combat.CancelSwing(); SetPrivate(combat, "nextAttackTime", 0f); combat.TryAttack();
                Next(report); return;
            }
            if (report.stage == 15 && combat.SwingNormalizedTime > .3f)
            {
                Check(report, clipMotion.IsClipPlaying && clipMotion.IsCombatClipPlaying, "Optional combat clip evaluates while existing base clip remains active");
                var bones = (System.Collections.IList)typeof(DaldongneAvatarMotion).GetField("humanoidRest", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(clipMotion);
                var mask = (bool[])typeof(DaldongneAvatarMotion).GetField("upperBodyMask", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(clipMotion);
                var rotations = (Quaternion[])typeof(DaldongneAvatarMotion).GetField("recoveryRotations", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(clipMotion);
                bool preserved = true;
                for (int i = 0; i < bones.Count; i++) if (!mask[i])
                {
                    var joint = (Transform)bones[i].GetType().GetField("joint").GetValue(bones[i]);
                    if (Quaternion.Angle(joint.localRotation, rotations[i]) > .01f) preserved = false;
                }
                Check(report, preserved, "Optional upper-body clip preserves evaluated lower-body rotations");
                Next(report); return;
            }
            if (report.stage == 16 && !combat.IsSwinging && !clipMotion.IsCombatClipPlaying)
            {
                Check(report, clipMotion.IsClipPlaying, "Combat graph releases and existing base playback survives");
                clipMotion.Punch_Jab = null; clipMotion.StopClip();
                report.passed = true; Finish(report); return;
            }
            if (handSmoke && report.stage >= 100 && report.stage <= 105)
            {
                int step = (report.stage - 100) / 2;
                if ((report.stage & 1) == 0 && !combat.IsSwinging)
                {
                    Check(report, combat.TryAttack() && combat.ComboStep == step,
                        "First-person hand smoke starts " + new[] { "jab", "straight", "hook" }[step]);
                    Next(report); return;
                }
                if ((report.stage & 1) != 0 && combat.SwingNormalizedTime >= .34f)
                {
                    var held = player.GetComponent<PlayerHeldItem>();
                    Check(report, held.IsFirstPersonVisible && held.FirstPersonArmRoot && held.FirstPersonLeftArmRoot
                        && held.FirstPersonArmRoot.gameObject.activeInHierarchy && held.FirstPersonLeftArmRoot.gameObject.activeInHierarchy,
                        "First-person " + step + " renders both authored hands");
                    Capture("first-person-" + new[] { "jab", "straight", "hook" }[step]);
                    Next(report); return;
                }
            }
            if (handSmoke && report.stage == 106 && !combat.IsSwinging)
            { report.passed = true; Finish(report); return; }
            Store(report);
        }
        catch (Exception exception) { report.error = exception.ToString(); Finish(report); }
    }

    static void CollisionChecks(Report report)
    {
        const string map = "Assets/Scenes/Maps/DaldongnePocketGarden.unity";
        var prefab = Resources.Load<GameObject>(ServerCombatCollision.ResourceKey(map));
        Check(report, prefab && !prefab.activeSelf, "Dedicated collision proxy exists and is inactive");
        foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            if (!(component is Transform || component is Collider)) throw new InvalidOperationException("Proxy contains gameplay component");
        Check(report, !SceneManager.GetSceneByPath(map).isLoaded, "Dedicated geometry test uses an unloaded gameplay map");
        var probe = new Vector3(0, 500, 0);
        int before = Physics.OverlapSphere(probe, 2000).Length;
        using (var collision = new ServerCombatCollision())
        {
            Check(report, collision.TryCheck(map, probe, probe + Vector3.forward, .1f, out bool clear) && clear, "Unloaded map resolves clear segment in isolated physics");
            bool blocked = false;
            foreach (var box in prefab.GetComponentsInChildren<BoxCollider>(true))
            {
                Vector3 center = box.transform.TransformPoint(box.center);
                if (collision.TryCheck(map, center + Vector3.up * 20, center - Vector3.up * 20, .01f, out clear) && !clear) { blocked = true; break; }
            }
            Check(report, blocked, "Unloaded map geometry blocks a segment through exported structure");
            Check(report, Physics.OverlapSphere(probe, 2000).Length == before, "Collision proxy does not enter the gameplay physics scene");
        }
    }
    static void Finish(Report report)
    {
        report.done = true;
        try
        {
            if (combat) combat.CancelSwing();
            if (player) { player.spawn = originalSpawn; player.ResetToSpawn(); }
            if (InventoryManager.Instance) InventoryManager.Instance.State.SelectHotbar(originalSlot);
            if (temporarilyHeld != null && InventoryManager.Instance && !temporarilyHeld.GetSlot(0).IsEmpty)
                temporarilyHeld.TryTransferTo(InventoryManager.Instance.State, 0, temporarilyHeld.GetSlot(0).Count, out _);
            temporarilyHeld = null;
            if (appearance) appearance.Select(originalVariant);
            if (camera && cameraCaptured) { camera.targetHeight = originalHeight; camera.distance = originalDistance; SetPrivate(camera, "pitch", originalPitch); SetPrivate(camera, "yaw", originalYaw);
                camera.firstPerson = originalFirstPerson; typeof(PlayerCameraController).GetMethod("UpdatePlayerVisuals", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(camera, null); }
            if (dummy) Object.Destroy(dummy); if (floor) Object.Destroy(floor); if (wall) Object.Destroy(wall);
            if (testLight) Object.Destroy(testLight);
            if (clipMotion) { clipMotion.Punch_Jab = null; clipMotion.StopClip(); }
        }
        catch (Exception cleanupError) { report.passed = false; report.error += "\nCleanup: " + cleanupError; }
        finally
        {
            PersonalSave.DirOverride = previousPersonalDirectory; WorldSave.PathOverride = previousWorldPath;
            string scratch = isolatedDirectory; isolatedDirectory = null;
            if (!string.IsNullOrEmpty(scratch))
            {
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(Path.GetDirectoryName(scratch), temp, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(scratch).StartsWith("CompanyGame-CombatRuntimeQA-", StringComparison.Ordinal) && Directory.Exists(scratch))
                    Directory.Delete(scratch, true);
            }
        }
        Store(report); Directory.CreateDirectory(outputFolder); File.WriteAllText(Path.Combine(outputFolder, "combat-runtime.json"), JsonUtility.ToJson(report, true));
        Debug.Log("[CombatRuntimeQA] " + (report.passed ? "PASS " : "FAIL ") + report.checks.Count + " checks " + report.error);
    }
}
