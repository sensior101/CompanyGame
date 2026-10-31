using System.Collections.Generic;
using CompanyGame.Daldongne;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Renders the selected quick-slot item at the avatar's right hand and in first person.</summary>
[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
public sealed class PlayerHeldItem : MonoBehaviour
{
    const int HandLayer = 29;
    [SerializeField, Range(1f, 1.8f)] float firstPersonArmScale = 1.35f;
    PlayerMovement movement;
    PlayerCombat combat;
    PlayerCameraController cameraController;
    Camera viewCamera, handCamera;
    Transform arm, forearm, visualSource, viewRig, viewArm, worldItem, firstPersonItem;
    Vector3 handPoint, forearmGrip;
    Mesh firstPersonArmMesh;
    Transform viewSkeleton, viewHand;
    Transform viewLeftArm, viewLeftSkeleton, viewLeftHand;
    Vector3 leftHandPoint, leftViewScale;
    Quaternion leftSkinArmFrameOffset;
    readonly Dictionary<Transform, Transform> leftViewBones = new Dictionary<Transform, Transform>();
    Quaternion skinArmFrameOffset;
    readonly Dictionary<Transform, Transform> viewBones = new Dictionary<Transform, Transform>();
    readonly List<Mesh> articulatedArmMeshes = new List<Mesh>();
    readonly List<SkinCopy> viewSkins = new List<SkinCopy>();
    struct SkinCopy
    {
        public SkinnedMeshRenderer source, copy;
    }
    DaldongneAvatarMotion avatarMotion;
    Quaternion armRest;
    ItemData heldItem;
    float attackStarted = -10f;
    float punchAmount, punchVelocity, recoilAmount, recoilVelocity, holdBlend;
    Vector3 viewScale;
    bool attackWasShot, excludedHandLayer;
    bool handsWereFrozen;
    string frozenHandPose = "";
    LineRenderer tracer;
    Material tracerMaterial;
    readonly List<Material> materials = new List<Material>();
    readonly List<Mesh> bakedMeshes = new List<Mesh>();

    public ItemData HeldItem => heldItem;
    public Transform WorldItemRoot => worldItem;
    public Transform FirstPersonItemRoot => firstPersonItem;
    public Transform FirstPersonArmRoot => viewArm;
    public Transform FirstPersonLeftArmRoot => viewLeftArm;
    public bool IsFirstPersonVisible => viewRig && viewRig.gameObject.activeInHierarchy;
    public Vector3 WorldGripPosition => avatarMotion && avatarMotion.HasHumanoidRig && avatarMotion.rightHand
        ? avatarMotion.RightGripPosition
        : forearm ? forearm.TransformPoint(forearmGrip) : arm ? arm.TransformPoint(handPoint) : transform.position + Vector3.up;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }
    void Start()
    {
        combat = GetComponent<PlayerCombat>();
        if (combat) combat.AttackPerformed += AnimateAttack;
        EnsureCamera();
    }
    void AnimateAttack(bool shot)
    {
        attackStarted = Time.time; attackWasShot = shot;
        if (!shot || !combat) return;
        if (!tracer)
        {
            var host = new GameObject("ShotTrail", typeof(LineRenderer));
            host.transform.SetParent(transform, false);
            tracer = host.GetComponent<LineRenderer>();
            tracerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.DontSave };
            tracerMaterial.color = new Color(1f, .78f, .3f);
            tracer.sharedMaterial = tracerMaterial;
            tracer.positionCount = 2; tracer.useWorldSpace = true;
            tracer.startWidth = .014f; tracer.endWidth = .006f;
            tracer.shadowCastingMode = ShadowCastingMode.Off;
        }
        var muzzle = IsFirstPersonVisible ? firstPersonItem : worldItem;
        tracer.SetPosition(0, muzzle ? muzzle.TransformPoint(new Vector3(0, .1f, .28f)) : combat.LastAttackOrigin);
        tracer.SetPosition(1, combat.LastAttackEnd);
        tracer.enabled = true;
    }

    Transform FindRightArm()
    {
        var appearance = GetComponent<DaldongnePlayerAppearance>();
        Transform source = transform;
        if (appearance)
        {
            var visual = appearance.selected == DaldongnePlayerAppearance.Variant.Female ? appearance.female : appearance.male;
            if (visual) source = visual.transform;
        }
        if (source == visualSource && arm) return arm;
        visualSource = source;
        avatarMotion = source.GetComponentInChildren<DaldongneAvatarMotion>(true);
        if (avatarMotion && avatarMotion.rightArm) return avatarMotion.rightArm;
        var animator = source.GetComponentInChildren<Animator>(true);
        if (animator && animator.isHuman && animator.avatar && animator.avatar.isValid)
            return animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        foreach (var child in source.GetComponentsInChildren<Transform>(true))
            if (child.name == "RightArm") return child;
        return null;
    }

    void EnsureCamera()
    {
        if (viewCamera || !movement || !movement.viewCamera) return;
        viewCamera = movement.viewCamera;
        cameraController = viewCamera.GetComponent<PlayerCameraController>();
        viewRig = new GameObject("FirstPersonHands").transform;
        viewRig.SetParent(viewCamera.transform, false);
        var host = new GameObject("FirstPersonHandCamera", typeof(Camera), typeof(UniversalAdditionalCameraData));
        host.transform.SetParent(viewCamera.transform, false);
        handCamera = host.GetComponent<Camera>();
        handCamera.CopyFrom(viewCamera);
        handCamera.cullingMask = 1 << HandLayer;
        handCamera.nearClipPlane = .01f;
        handCamera.farClipPlane = 4f;
        handCamera.useOcclusionCulling = false;
        var overlay = host.GetComponent<UniversalAdditionalCameraData>();
        overlay.renderType = CameraRenderType.Overlay;
        overlay.renderPostProcessing = false;
        overlay.renderShadows = false;
        overlay.volumeLayerMask = 0;
        viewCamera.GetUniversalAdditionalCameraData().cameraStack.Add(handCamera);
        excludedHandLayer = (viewCamera.cullingMask & (1 << HandLayer)) != 0;
        viewCamera.cullingMask &= ~(1 << HandLayer);
        handCamera.enabled = false;
        viewRig.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        EnsureCamera();
        var inventory = InventoryManager.Instance ? InventoryManager.Instance.State : null;
        if (inventory == null || !viewRig) return;
        Transform currentArm = FindRightArm();
        bool newArm = arm != currentArm;
        bool changedArm = newArm || (currentArm && !viewArm);
        if (changedArm)
        {
            arm = currentArm;
            if (arm) { if (newArm) armRest = arm.localRotation; BuildArm(); }
        }
        var stack = inventory.GetSlot(inventory.SelectedHotbarIndex);
        ItemData selected = stack != null && !stack.IsEmpty ? stack.Item : null;
        if (heldItem != selected || changedArm)
        {
            heldItem = selected;
            RebuildItems();
        }

        var vehicle = GetComponent<PlayerVehicle>();
        if (vehicle && vehicle.IsRiding)
        {
            viewRig.gameObject.SetActive(false);
            if (handCamera) handCamera.enabled = false;
            if (worldItem) worldItem.gameObject.SetActive(false);
            return;
        }
        bool firstPerson = cameraController && cameraController.firstPerson;
        bool showHands = firstPerson && !InputFocus.InventoryOpen() && !InputFocus.ChatOpen() &&
            !SceneLoadManager.IsLoading && movement.isActiveAndEnabled && !cameraController.IsDialogueCameraActive;
        // Keep the relaxed right hand visible while empty; the left hand is for attacks.
        bool swinging = combat && combat.IsSwinging;
        float frameDelta = avatarMotion ? avatarMotion.CombatFrameDelta : Time.deltaTime;
        string poseState = avatarMotion ? avatarMotion.CombatPoseState : "";
        bool captureFrozenHands = frameDelta <= 0f && (!handsWereFrozen || frozenHandPose != poseState);
        handsWereFrozen = frameDelta <= 0f;
        frozenHandPose = poseState;
        bool showArm = showHands && arm;
        viewRig.gameObject.SetActive(showArm);
        handCamera.enabled = showArm;
        handCamera.fieldOfView = viewCamera.fieldOfView;
        handCamera.aspect = viewCamera.aspect;
        handCamera.rect = viewCamera.rect;
        if (tracer) tracer.enabled = attackWasShot && Time.time - attackStarted < .07f && !InputFocus.InventoryOpen();
        // Retrigger targets, not transforms. Preserve velocity through rapid clicks
        // and ease all the way back to the current idle/walking pose.
        float elapsed = Time.time - attackStarted;
        float recoilTarget = attackWasShot && elapsed < .045f ? 1f : 0f;
        if (frameDelta > 0f || captureFrozenHands) punchAmount = swinging ? DaldongneAvatarMotion.PunchExtension(combat.SwingNormalizedTime, combat.ContactNormalized) : 0f;
        if (frameDelta > 0f) recoilAmount = Mathf.SmoothDamp(recoilAmount, recoilTarget, ref recoilVelocity,
            recoilTarget > 0 ? .025f : .065f, Mathf.Infinity, frameDelta);
        holdBlend = Mathf.MoveTowards(holdBlend, heldItem ? 1f : 0f, frameDelta * 6f);
        float punch = swinging && combat.ComboStep == 1 ? punchAmount : 0f, recoil = recoilAmount;
        if (arm && avatarMotion && avatarMotion.HasHumanoidRig)
        {
            avatarMotion.ApplyRightArmInteraction(holdBlend, 0f, recoil);
        }
        else if (arm && (!avatarMotion || (!avatarMotion.IsClipPlaying && !avatarMotion.HasCombatPose)))
        {
            Quaternion baseArm = avatarMotion ? avatarMotion.RightArmPose : armRest;
            Quaternion baseForearm = avatarMotion ? avatarMotion.RightForearmPose : Quaternion.identity;
            float holdAngle = forearm ? -12f : -25f;
            arm.localRotation = Quaternion.Slerp(baseArm, armRest * Quaternion.Euler(holdAngle, 0, 0), holdBlend);
            arm.localRotation = Quaternion.Slerp(arm.localRotation, armRest * Quaternion.Euler(-76f, 0, -3f), punch);
            if (forearm)
            {
                forearm.localRotation = Quaternion.Slerp(baseForearm, Quaternion.Euler(-58f, 0, 0), holdBlend);
                forearm.localRotation = Quaternion.Slerp(forearm.localRotation, Quaternion.Euler(-12f + 8f * recoil, 0, 0), punch);
            }
        }
        if (worldItem)
        {
            worldItem.gameObject.SetActive(!firstPerson);
            worldItem.position = WorldGripPosition + transform.up * .055f;
            worldItem.rotation = transform.rotation;
        }
        // A received hit cancels gameplay swing immediately; retain the last rendered fists for local hit stop.
        if (frameDelta <= 0f && !captureFrozenHands) return;
        if (viewArm)
        {
            SynchronizeSkinnedArm();
            viewArm.localScale = viewScale;
            viewArm.localRotation = Quaternion.Euler(-125f + 35f * punch - 8f * recoil, -10f, -18f + 10f * punch);
            // A short forearm enters diagonally from the bottom-right, away from
            // the central HUD. Punches travel towards the aim point and return.
            float depth = .65f + .18f * punch - .035f * recoil;
            float halfHeight = Mathf.Tan(handCamera.fieldOfView * Mathf.Deg2Rad * .5f) * depth;
            Vector3 gripInView = new Vector3(halfHeight * handCamera.aspect * (.68f - .38f * punch),
                halfHeight * (-.62f + .26f * punch), depth);
            viewArm.localPosition = gripInView - viewArm.localRotation * Vector3.Scale(viewArm.localScale, handPoint);
            FrameArticulatedForearm(false, gripInView);
            MagnifyFirstPersonArm(viewArm, handPoint);
            if (firstPersonItem)
            {
                firstPersonItem.position = viewArm.TransformPoint(handPoint) + viewCamera.transform.up * .055f;
                firstPersonItem.rotation = viewCamera.transform.rotation * Quaternion.Euler(-8f * recoil, -10f, -8f);
            }
        }
        if (viewLeftArm)
        {
            bool showLeftArm = swinging;
            viewLeftArm.gameObject.SetActive(showLeftArm);
            if (showLeftArm)
            {
                float leftPunch = swinging && combat.ComboStep != 1 ? punchAmount : 0f;
                bool hook = swinging && combat.ComboStep == 2;
                float depth = .65f + (hook ? .08f : .22f) * leftPunch;
                float halfHeight = Mathf.Tan(handCamera.fieldOfView * Mathf.Deg2Rad * .5f) * depth;
                // The hook follows a lateral arc across the aim point; jab extends from the left.
                float side = hook ? Mathf.Lerp(-.75f, .32f, leftPunch) : -.68f + .38f * leftPunch;
                Vector3 grip = new Vector3(halfHeight * handCamera.aspect * side, halfHeight * (-.62f + .26f * leftPunch), depth);
                viewLeftArm.localScale = leftViewScale;
                viewLeftArm.localRotation = Quaternion.Euler(-125f + (hook ? 16f : 35f) * leftPunch,
                    hook ? Mathf.Lerp(33f, -42f, leftPunch) : 10f, 18f - 10f * leftPunch);
                viewLeftArm.localPosition = grip - viewLeftArm.localRotation * Vector3.Scale(leftViewScale, leftHandPoint);
                FrameArticulatedForearm(true, grip);
                MagnifyFirstPersonArm(viewLeftArm, leftHandPoint);
            }
        }
    }

    void MagnifyFirstPersonArm(Transform output, Vector3 grip)
    {
        // Magnify after framing so its fit calculation cannot cancel the size change.
        // Keep the palm anchor fixed; held items retain their existing size and position.
        Vector3 anchor = output.TransformPoint(grip);
        output.localScale *= Mathf.Clamp(firstPersonArmScale, 1f, 1.8f);
        output.position += anchor - output.TransformPoint(grip);
    }

    void FrameArticulatedForearm(bool left, Vector3 gripInView)
    {
        Transform output = left ? viewLeftArm : viewArm;
        Transform sourceElbow = avatarMotion ? left ? avatarMotion.leftForearm : avatarMotion.rightForearm : null;
        var bones = left ? leftViewBones : viewBones;
        if (!output || !sourceElbow || !bones.TryGetValue(sourceElbow, out var elbow)) return;
        Vector3 grip = left ? leftHandPoint : handPoint;
        Vector3 cut = output.InverseTransformPoint(elbow.position);
        Vector3 cutDirection = output.localRotation * Vector3.Scale(output.localScale, cut - grip);
        if (cutDirection.sqrMagnitude < .00001f) return;
        // The copied elbow bends with the world pose. Orient the actual visible
        // forearm from the fist toward an off-screen cut, independent of that bend.
        float tangent = Mathf.Tan(handCamera.fieldOfView * Mathf.Deg2Rad * .5f);
        float cutDepth = Mathf.Max(.12f, gripInView.z * .62f);
        Vector3 offscreenCut = new Vector3(gripInView.x + (left ? -.12f : .12f),
            -tangent * cutDepth * 1.12f - .06f, cutDepth);
        Vector3 desired = offscreenCut - gripInView;
        float presentationScale = Mathf.Clamp(desired.magnitude / cutDirection.magnitude, 1f, 1.55f);
        output.localScale *= presentationScale;
        output.localRotation = Quaternion.FromToRotation(cutDirection, desired) * output.localRotation;
        Vector3 actualCut = gripInView + desired.normalized * cutDirection.magnitude * presentationScale;
        // The extraction includes a short blend above the elbow; leave enough
        // border margin that its open seam cannot enter the image.
        gripInView.y += Mathf.Min(0f, -tangent * actualCut.z * 1.12f - .06f - actualCut.y);
        output.localPosition = gripInView - output.localRotation * Vector3.Scale(output.localScale, grip);
    }

    void BuildArm()
    {
        Remove(ref viewArm);
        ClearSkinnedArm();
        if (firstPersonArmMesh) Destroy(firstPersonArmMesh);
        firstPersonArmMesh = null;
        forearm = avatarMotion && avatarMotion.rightForearm ? avatarMotion.rightForearm : arm.Find("RightForearm");
        if (avatarMotion) armRest = avatarMotion.RightArmRest;
        else if (forearm) armRest = Quaternion.identity;
        holdBlend = punchAmount = recoilAmount = punchVelocity = recoilVelocity = 0;
        viewScale = Vector3.Scale(arm.lossyScale, new Vector3(1.3f, forearm ? .85f : .6f, 1.3f));
        if (avatarMotion && avatarMotion.HasHumanoidRig && BuildArticulatedArm()) return;
        viewArm = CopyRenderers(arm, viewRig, HandLayer);
        viewArm.name = "CurrentAvatarRightArm";
        viewArm.localScale = arm.lossyScale;
        viewScale = Vector3.Scale(arm.lossyScale, new Vector3(1.3f, forearm ? .85f : .6f, 1.3f));
        // Continuous skins keep the renderer beside the skeleton, not on the arm.
        if (viewArm.GetComponentsInChildren<Renderer>(true).Length == 0 && BuildSkinnedArm())
        { BuildLegacyLeftArm(); return; }
        Transform hand = arm.Find("Hand");
        if (hand) handPoint = arm.InverseTransformPoint(hand.position);
        else
        {
            var mesh = arm.GetComponent<MeshFilter>();
            var bounds = mesh && mesh.sharedMesh ? mesh.sharedMesh.bounds : new Bounds(new Vector3(0f, -.25f, 0f), new Vector3(.18f, .5f, .18f));
            handPoint = new Vector3(bounds.center.x, bounds.min.y + .055f, bounds.center.z);
        }
        if (forearm) forearmGrip = forearm.InverseTransformPoint(arm.TransformPoint(handPoint));
        BuildLegacyLeftArm();
    }

    void BuildLegacyLeftArm()
    {
        if (avatarMotion && avatarMotion.leftArm)
        {
            viewLeftArm = CopyRenderers(avatarMotion.leftArm, viewRig, HandLayer);
            viewLeftArm.name = "CurrentAvatarLeftArm";
            leftViewScale = viewScale;
            var leftHand = avatarMotion.leftArm.Find("Hand");
            leftHandPoint = leftHand ? avatarMotion.leftArm.InverseTransformPoint(leftHand.position) : handPoint;
            if (viewLeftArm.GetComponentsInChildren<Renderer>(true).Length == 0) BuildSkinnedArm(true);
        }
    }

    bool BuildArticulatedArm(bool left = false)
    {
        var sources = visualSource.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Transform sourceArm = left ? avatarMotion.leftArm : arm;
        Transform sourceForearm = left ? avatarMotion.leftForearm : forearm;
        if (!sourceArm) return false;
        var boneMap = left ? leftViewBones : viewBones;
        Transform outputArm = new GameObject(left ? "CurrentAvatarLeftArm" : "CurrentAvatarRightArm").transform;
        outputArm.SetParent(viewRig, false);
        outputArm.gameObject.layer = HandLayer;
        // Preserve the authored hand proportions; forearm framing supplies only
        // uniform magnification rather than flattening the knuckles and fingers.
        Vector3 outputScale = sourceArm.lossyScale * 1.1f;
        outputArm.localScale = outputScale;
        if (left) { viewLeftArm = outputArm; leftViewScale = outputScale; }
        else { viewArm = outputArm; viewScale = outputScale; }
        // Imported bones need not use the legacy rig's local axes. Establish a
        // shoulder frame whose negative Y points towards the elbow, once.
        Vector3 up = sourceForearm ? sourceArm.position - sourceForearm.position : visualSource.up;
        if (up.sqrMagnitude < .000001f) up = visualSource.up;
        up.Normalize();
        Vector3 forward = Vector3.ProjectOnPlane(visualSource.forward, up);
        if (forward.sqrMagnitude < .000001f) forward = Vector3.ProjectOnPlane(visualSource.up, up);
        if (forward.sqrMagnitude < .000001f) forward = Vector3.ProjectOnPlane(visualSource.right, up);
        Quaternion frameOffset = Quaternion.Inverse(sourceArm.rotation) * Quaternion.LookRotation(forward.normalized, up);
        Transform skeleton = CopyArmHierarchy(visualSource, outputArm, boneMap);
        if (left) { leftSkinArmFrameOffset = frameOffset; viewLeftSkeleton = skeleton; }
        else { skinArmFrameOffset = frameOffset; viewSkeleton = skeleton; }
        int originalSkinCount = viewSkins.Count;
        foreach (var skin in sources)
        {
            var source = skin.sharedMesh;
            var bones = skin.bones;
            if (!source || !source.isReadable || !boneMap.TryGetValue(skin.transform, out var destination)) continue;
            var includedBones = new HashSet<int>();
            Transform cut = sourceForearm ? sourceForearm : sourceArm;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] && (bones[i] == cut || bones[i].IsChildOf(cut))) includedBones.Add(i);
            if (includedBones.Count == 0) continue;
            var weights = source.GetAllBoneWeights();
            var counts = source.GetBonesPerVertex();
            var accepted = new bool[source.vertexCount];
            int cursor = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                float amount = 0;
                for (int j = 0; j < counts[i]; j++)
                {
                    var weight = weights[cursor++];
                    if (includedBones.Contains(weight.boneIndex)) amount += weight.weight;
                }
                accepted[i] = amount > .1f;
            }
            // These are borrowed views of the mesh's bone data, not allocations
            // owned by this component.
            var submeshes = new List<int[]>();
            int triangleCount = 0;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var indices = source.GetTriangles(sub);
                var kept = new List<int>();
                for (int i = 0; i < indices.Length; i += 3)
                {
                    if (!accepted[indices[i]] || !accepted[indices[i + 1]] || !accepted[indices[i + 2]]) continue;
                    kept.Add(indices[i]); kept.Add(indices[i + 1]); kept.Add(indices[i + 2]);
                }
                triangleCount += kept.Count / 3;
                submeshes.Add(kept.ToArray());
            }
            if (triangleCount == 0) continue;
            // Retain the source vertex streams, bind poses, all bone influences,
            // and blend shapes. Only triangle visibility changes for this view.
            var mesh = Instantiate(source);
            mesh.name = left ? "CurrentAvatarLeftArmMesh" : "CurrentAvatarRightArmMesh";
            mesh.hideFlags = HideFlags.DontSave;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i, false);
            articulatedArmMeshes.Add(mesh);
            var copy = destination.gameObject.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = mesh;
            copy.sharedMaterials = skin.sharedMaterials;
            copy.updateWhenOffscreen = true;
            copy.quality = skin.quality;
            copy.localBounds = skin.localBounds;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off;
            copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var copiedBones = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] && boneMap.TryGetValue(bones[i], out var bone)) copiedBones[i] = bone;
            copy.bones = copiedBones;
            if (skin.rootBone && boneMap.TryGetValue(skin.rootBone, out var root)) copy.rootBone = root;
            viewSkins.Add(new SkinCopy { source = skin, copy = copy });
        }
        if (viewSkins.Count == originalSkinCount)
        {
            if (left) { Remove(ref viewLeftArm); viewLeftSkeleton = null; leftViewBones.Clear(); }
            else { Remove(ref viewArm); ClearSkinnedArm(); }
            return false;
        }
        if (left)
        {
            if (avatarMotion.leftHand) boneMap.TryGetValue(avatarMotion.leftHand, out viewLeftHand);
        }
        else
        {
            if (avatarMotion.rightHand) boneMap.TryGetValue(avatarMotion.rightHand, out viewHand);
            BuildArticulatedArm(true);
        }
        SynchronizeSkinnedArm();
        return true;
    }

    Transform CopyArmHierarchy(Transform source, Transform parent, Dictionary<Transform, Transform> boneMap)
    {
        // Same transform remapping used by InventoryCharacterPreview; no gameplay
        // components or second Animator are copied into the camera hierarchy.
        var copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false);
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
        copy.gameObject.layer = HandLayer;
        copy.gameObject.SetActive(source.gameObject.activeSelf);
        boneMap.Add(source, copy);
        foreach (Transform child in source) CopyArmHierarchy(child, copy, boneMap);
        return copy;
    }

    void SynchronizeSkinnedArm()
    {
        SynchronizeArm(false);
        SynchronizeArm(true);
        foreach (var pair in viewSkins)
        {
            if (!pair.source || !pair.copy || !pair.source.sharedMesh) continue;
            pair.copy.sharedMaterials = pair.source.sharedMaterials;
            for (int i = 0; i < pair.source.sharedMesh.blendShapeCount; i++)
                pair.copy.SetBlendShapeWeight(i, pair.source.GetBlendShapeWeight(i));
        }
    }

    void SynchronizeArm(bool left)
    {
        Transform skeleton = left ? viewLeftSkeleton : viewSkeleton;
        Transform sourceArm = left && avatarMotion ? avatarMotion.leftArm : arm;
        Transform outputArm = left ? viewLeftArm : viewArm;
        Transform outputHand = left ? viewLeftHand : viewHand;
        Transform sourceHand = left && avatarMotion ? avatarMotion.leftHand : avatarMotion ? avatarMotion.rightHand : null;
        var boneMap = left ? leftViewBones : viewBones;
        if (!skeleton || !sourceArm || !outputArm) return;
        foreach (var pair in boneMap)
        {
            if (!pair.Key || !pair.Value || pair.Key == visualSource) continue;
            pair.Value.localPosition = pair.Key.localPosition;
            pair.Value.localRotation = pair.Key.localRotation;
            pair.Value.localScale = pair.Key.localScale;
            if (pair.Value.gameObject.activeSelf != pair.Key.gameObject.activeSelf)
                pair.Value.gameObject.SetActive(pair.Key.gameObject.activeSelf);
        }
        Quaternion frame = sourceArm.rotation * (left ? leftSkinArmFrameOffset : skinArmFrameOffset);
        Vector3 inverseScale = new Vector3(1f / Mathf.Max(.00001f, sourceArm.lossyScale.x),
            1f / Mathf.Max(.00001f, sourceArm.lossyScale.y), 1f / Mathf.Max(.00001f, sourceArm.lossyScale.z));
        skeleton.localPosition = Vector3.Scale(Quaternion.Inverse(frame) * (visualSource.position - sourceArm.position), inverseScale);
        skeleton.localRotation = Quaternion.Inverse(frame) * visualSource.rotation;
        skeleton.localScale = Vector3.Scale(visualSource.lossyScale, inverseScale);
        skeleton.gameObject.SetActive(true);
        Vector3 grip = avatarMotion.GripPosition(!left);
        Vector3 point = outputHand && sourceHand
            ? outputArm.InverseTransformPoint(outputHand.TransformPoint(sourceHand.InverseTransformPoint(grip)))
            : Vector3.Scale(Quaternion.Inverse(frame) * (grip - sourceArm.position), inverseScale);
        if (left) leftHandPoint = point; else handPoint = point;
    }

    void ClearSkinnedArm()
    {
        viewSkeleton = viewHand = null;
        Remove(ref viewLeftArm);
        viewLeftSkeleton = viewLeftHand = null;
        viewBones.Clear();
        leftViewBones.Clear();
        viewSkins.Clear();
        foreach (var mesh in articulatedArmMeshes) if (mesh) Destroy(mesh);
        articulatedArmMeshes.Clear();
    }

    bool BuildSkinnedArm(bool left = false)
    {
        Transform sourceArm = left ? avatarMotion.leftArm : arm;
        Transform sourceForearm = left ? avatarMotion.leftForearm : forearm;
        Transform outputArm = left ? viewLeftArm : viewArm;
        foreach (var skin in visualSource.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var source = skin.sharedMesh;
            var bones = skin.bones;
            int armIndex = System.Array.IndexOf(bones, sourceArm);
            if (!source || !source.isReadable || armIndex < 0) continue;
            var includedBones = new HashSet<int>();
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] && (sourceForearm ? bones[i] == sourceForearm || bones[i].IsChildOf(sourceForearm)
                    : bones[i] == sourceArm || bones[i].IsChildOf(sourceArm))) includedBones.Add(i);
            var weights = source.boneWeights;
            var accepted = new bool[source.vertexCount];
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                float amount = (includedBones.Contains(w.boneIndex0) ? w.weight0 : 0)
                    + (includedBones.Contains(w.boneIndex1) ? w.weight1 : 0)
                    + (includedBones.Contains(w.boneIndex2) ? w.weight2 : 0)
                    + (includedBones.Contains(w.boneIndex3) ? w.weight3 : 0);
                accepted[i] = amount > .1f;
            }
            var vertices = source.vertices; var normals = source.normals; var uv = source.uv;
            var bind = source.bindposes[armIndex]; var normalMatrix = bind.inverse.transpose;
            var remap = new Dictionary<int, int>();
            var outputVertices = new List<Vector3>(); var outputNormals = new List<Vector3>();
            var outputUV = new List<Vector2>(); var submeshes = new List<int[]>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var indices = source.GetTriangles(sub); var output = new List<int>();
                for (int i = 0; i < indices.Length; i += 3)
                {
                    if (!accepted[indices[i]] || !accepted[indices[i+1]] || !accepted[indices[i+2]]) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        int original = indices[i+j];
                        if (!remap.TryGetValue(original, out int mapped))
                        {
                            mapped = outputVertices.Count; remap.Add(original, mapped);
                            outputVertices.Add(bind.MultiplyPoint3x4(vertices[original]));
                            outputNormals.Add(normalMatrix.MultiplyVector(normals[original]).normalized);
                            outputUV.Add(uv.Length == vertices.Length ? uv[original] : Vector2.zero);
                        }
                        output.Add(mapped);
                    }
                }
                submeshes.Add(output.ToArray());
            }
            if (outputVertices.Count == 0) continue;
            var outputMesh = new Mesh { name = left ? "CurrentAvatarLeftArmMesh" : "CurrentAvatarRightArmMesh", hideFlags = HideFlags.DontSave,
                indexFormat = outputVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            if (left) articulatedArmMeshes.Add(outputMesh); else firstPersonArmMesh = outputMesh;
            outputMesh.SetVertices(outputVertices); outputMesh.SetNormals(outputNormals); outputMesh.SetUVs(0, outputUV);
            outputMesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) outputMesh.SetTriangles(submeshes[i], i);
            outputMesh.RecalculateBounds(); outputMesh.RecalculateTangents();
            outputArm.gameObject.AddComponent<MeshFilter>().sharedMesh = outputMesh;
            var renderer = outputArm.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = skin.sharedMaterials; renderer.shadowCastingMode = ShadowCastingMode.Off;
            var bounds = outputMesh.bounds;
            Vector3 point = new Vector3(bounds.center.x, bounds.min.y + .055f, bounds.center.z);
            if (left) leftHandPoint = point; else handPoint = point;
            int forearmIndex = System.Array.IndexOf(bones, sourceForearm);
            if (!left && forearmIndex >= 0) forearmGrip = source.bindposes[forearmIndex].MultiplyPoint3x4(bind.inverse.MultiplyPoint3x4(handPoint));
            return true;
        }
        return false;
    }

    void RebuildItems()
    {
        Remove(ref worldItem); Remove(ref firstPersonItem);
        foreach (var material in materials) if (material) Destroy(material);
        materials.Clear();
        if (!heldItem || !arm) return;
        worldItem = new GameObject("RightHandHeldItem").transform;
        worldItem.SetParent(transform, false);
        firstPersonItem = new GameObject("FirstPersonHeldItem").transform;
        firstPersonItem.SetParent(viewRig, false);
        BuildItem(worldItem, 0);
        BuildItem(firstPersonItem, HandLayer);
    }

    /// <summary>
    /// Builds <paramref name="item"/>'s third-person model under <paramref name="parent"/>, the same one this hand shows.
    /// RemoteAvatarView keeps a disabled PlayerHeldItem only for this (it never runs Start or LateUpdate); the materials die with it.
    /// </summary>
    internal void BuildWorldModel(ItemData item, Transform parent)
    {
        foreach (var material in materials) if (material) Destroy(material);
        materials.Clear();
        heldItem = item;
        BuildItem(parent, 0);
    }

    void BuildItem(Transform parent, int layer)
    {
        Transform model;
        if (heldItem.heldPrefab)
            model = CopyRenderers(heldItem.heldPrefab.transform, parent, layer);
        else
        {
            model = new GameObject("HeldModel").transform;
            model.SetParent(parent, false);
            if (heldItem.IsBook) BuildBook(model, layer);
            else if (heldItem.IsWeapon && heldItem.weaponKind == WeaponKind.Firearm) BuildPistol(model, layer);
            else if (heldItem.icon) BuildIcon(model, layer);
            else Part(model, "Item", PrimitiveType.Cube, new Vector3(0f, .05f, 0f), new Vector3(.13f, .15f, .1f),
                Material(new Color(.64f, .72f, .67f)), layer);
        }
        model.localPosition = heldItem.heldLocalPosition;
        model.localRotation = Quaternion.Euler(heldItem.heldLocalEulerAngles);
        model.localScale = heldItem.heldLocalScale;
    }

    void BuildIcon(Transform parent, int layer)
    {
        bool coin = heldItem.IsCurrency && heldItem.CurrencyValue < 10000;
        // Silver coins are intentionally smaller than gold coins in the held 3D model.
        float width = coin
            ? (heldItem.CurrencyValue < 1000 ? .14f : .19f)
            : .30f;
        var sprite = heldItem.icon;
        float height = width * sprite.bounds.size.y / Mathf.Max(.001f, sprite.bounds.size.x);
        var edge = Material(coin ? (heldItem.CurrencyValue < 1000 ? new Color(.62f, .66f, .7f) : new Color(.85f, .56f, .12f)) : new Color(.92f, .9f, .79f));
        if (coin)
        {
            var rim = Part(parent, "CoinRim", PrimitiveType.Cylinder, new Vector3(0, .07f, 0), new Vector3(width, .005f, width), edge, layer);
            rim.localRotation = Quaternion.Euler(90f, 0, 0);
        }
        else Part(parent, "PaperEdge", PrimitiveType.Cube, new Vector3(0, .07f, 0), new Vector3(width, height, .004f), edge, layer);
        for (int side = 0; side < 2; side++)
        {
            var host = new GameObject(side == 0 ? "ItemFront" : "ItemBack", typeof(SpriteRenderer));
            host.layer = layer;
            host.transform.SetParent(parent, false);
            host.transform.localPosition = new Vector3(0, .07f, side == 0 ? -.006f : .006f);
            host.transform.localRotation = Quaternion.Euler(0, side == 0 ? 0 : 180, 0);
            host.transform.localScale = Vector3.one * (width / Mathf.Max(.001f, sprite.bounds.size.x));
            var renderer = host.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    void BuildPistol(Transform parent, int layer)
    {
        var metal = Material(new Color(.18f, .2f, .22f), .65f);
        var grip = Material(new Color(.08f, .09f, .095f));
        var sight = Material(new Color(.76f, .8f, .77f), .3f);
        var handle = Part(parent, "Grip", PrimitiveType.Cube, new Vector3(0, .015f, .015f), new Vector3(.067f, .13f, .085f), grip, layer);
        handle.localRotation = Quaternion.Euler(-14f, 0, 0);
        Part(parent, "Slide", PrimitiveType.Cube, new Vector3(0, .10f, .105f), new Vector3(.074f, .074f, .27f), metal, layer);
        var barrel = Part(parent, "Barrel", PrimitiveType.Cylinder, new Vector3(0, .09f, .251f), new Vector3(.04f, .019f, .04f), grip, layer);
        barrel.localRotation = Quaternion.Euler(90f, 0, 0);
        Part(parent, "FrontSight", PrimitiveType.Cube, new Vector3(0, .144f, .211f), new Vector3(.013f, .015f, .022f), sight, layer);
        Part(parent, "RearSight", PrimitiveType.Cube, new Vector3(0, .144f, .003f), new Vector3(.041f, .015f, .018f), grip, layer);
        Part(parent, "TriggerGuard", PrimitiveType.Cube, new Vector3(0, -.008f, .095f), new Vector3(.018f, .014f, .075f), metal, layer);
        Part(parent, "GuardFront", PrimitiveType.Cube, new Vector3(0, .025f, .129f), new Vector3(.018f, .064f, .014f), metal, layer);
        Part(parent, "Trigger", PrimitiveType.Cube, new Vector3(0, .043f, .088f), new Vector3(.014f, .035f, .018f), grip, layer);
    }

    void BuildBook(Transform parent, int layer)
    {
        var leather = Material(new Color(.27f, .105f, .048f), .08f);
        var spine = Material(new Color(.19f, .065f, .032f), .04f);
        var pages = Material(new Color(.91f, .83f, .66f));
        var gold = Material(new Color(.71f, .49f, .17f), .55f);
        Part(parent, "PageBlock", PrimitiveType.Cube, new Vector3(.015f, .09f, 0f), new Vector3(.20f, .26f, .045f), pages, layer);
        Part(parent, "FrontCover", PrimitiveType.Cube, new Vector3(.008f, .09f, -.029f), new Vector3(.225f, .285f, .012f), leather, layer);
        Part(parent, "BackCover", PrimitiveType.Cube, new Vector3(.008f, .09f, .029f), new Vector3(.225f, .285f, .012f), leather, layer);
        Part(parent, "Spine", PrimitiveType.Cube, new Vector3(-.112f, .09f, 0f), new Vector3(.029f, .29f, .074f), spine, layer);
        foreach (float y in new[] { -.025f, .205f })
            Part(parent, "SpineBand", PrimitiveType.Cube, new Vector3(-.13f, y, 0f), new Vector3(.004f, .009f, .077f), gold, layer);
        foreach (float x in new[] { -.084f, .10f })
            Part(parent, "CoverLine", PrimitiveType.Cube, new Vector3(x, .09f, -.037f), new Vector3(.002f, .245f, .002f), gold, layer);
        foreach (float y in new[] { -.033f, .213f })
            Part(parent, "CoverLine", PrimitiveType.Cube, new Vector3(.008f, y, -.037f), new Vector3(.185f, .002f, .002f), gold, layer);
    }

    Material Material(Color color, float metallic = 0f)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "HeldItemMaterial", hideFlags = HideFlags.DontSave };
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", metallic > 0f ? .5f : .2f);
        materials.Add(material);
        return material;
    }
    static Transform Part(Transform parent, string name, PrimitiveType primitive, Vector3 position, Vector3 scale, Material material, int layer)
    {
        var host = GameObject.CreatePrimitive(primitive);
        host.name = name; host.layer = layer;
        host.transform.SetParent(parent, false);
        host.transform.localPosition = position; host.transform.localScale = scale;
        var collider = host.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        var renderer = host.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return host.transform;
    }
    Transform CopyRenderers(Transform source, Transform parent, int layer)
    {
        var copy = new GameObject(source.name).transform;
        copy.gameObject.layer = layer; copy.SetParent(parent, false);
        if (source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh && source.TryGetComponent<MeshRenderer>(out var renderer))
        {
            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var cloned = copy.gameObject.AddComponent<MeshRenderer>();
            cloned.sharedMaterials = renderer.sharedMaterials; cloned.shadowCastingMode = ShadowCastingMode.Off;
        }
        else if (source.TryGetComponent<SkinnedMeshRenderer>(out var skin) && skin.sharedMesh)
        {
            var mesh = new Mesh { name = "HeldItemBakedMesh", hideFlags = HideFlags.DontSave };
            skin.BakeMesh(mesh); bakedMeshes.Add(mesh);
            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var cloned = copy.gameObject.AddComponent<MeshRenderer>();
            cloned.sharedMaterials = skin.sharedMaterials; cloned.shadowCastingMode = ShadowCastingMode.Off;
        }
        foreach (Transform child in source)
        {
            var cloned = CopyRenderers(child, copy, layer);
            cloned.localPosition = child.localPosition;
            cloned.localRotation = child.localRotation;
            cloned.localScale = child.localScale;
            cloned.gameObject.SetActive(child.gameObject.activeSelf);
        }
        return copy;
    }
    static void Remove(ref Transform value)
    {
        if (value) { value.gameObject.SetActive(false); Destroy(value.gameObject); }
        value = null;
    }
    void OnDisable()
    {
        if (tracer) tracer.enabled = false;
        if (viewRig) viewRig.gameObject.SetActive(false);
        if (handCamera) handCamera.enabled = false;
        if (worldItem) worldItem.gameObject.SetActive(false);
    }
    void OnDestroy()
    {
        if (combat) combat.AttackPerformed -= AnimateAttack;
        if (viewCamera)
        {
            viewCamera.GetUniversalAdditionalCameraData().cameraStack.Remove(handCamera);
            if (excludedHandLayer) viewCamera.cullingMask |= 1 << HandLayer;
        }
        if (handCamera) Destroy(handCamera.gameObject);
        if (tracerMaterial) Destroy(tracerMaterial);
        Remove(ref viewRig); Remove(ref worldItem);
        foreach (var material in materials) if (material) Destroy(material);
        foreach (var mesh in bakedMeshes) if (mesh) Destroy(mesh);
        if (firstPersonArmMesh) Destroy(firstPersonArmMesh);
        ClearSkinnedArm();
    }
}
