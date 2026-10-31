using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CompanyGame.Daldongne
{
    // One pose owner for legacy rigs, articulated humanoids and explicitly played clips.
    public sealed class DaldongneAvatarMotion : MonoBehaviour
    {
        public Transform hips, leftArm, rightArm, leftLeg, rightLeg, leftKnee, rightKnee;
        public Transform leftForearm, rightForearm;
        public Animator rigAnimator;
        public Transform head, leftHand, rightHand, leftFoot, rightFoot;
        [Range(.5f, 1f)] public float footSpacing = 1f;
        [Range(.5f, 1f)] public float strideScale = 1f, liftScale = 1f, swayScale = 1f;
        [Header("Optional combat clips (empty uses procedural pose)")]
        public AnimationClip Punch_Jab, Punch_Straight, Punch_Hook;
        public AnimationClip Hit_Face, Hit_Body, Hit_Heavy, Knockdown, GetUp;

        sealed class RestJoint
        {
            public Transform joint;
            public Vector3 position;
            public Quaternion rotation;
            public Quaternion rootRotation;
            public RestJoint(Transform value, Transform root = null)
            {
                joint = value;
                if (!value) return;
                position = value.localPosition;
                rotation = value.localRotation;
                rootRotation = root ? Quaternion.Inverse(root.rotation) * value.rotation : Quaternion.identity;
            }
            public void Restore()
            {
                if (!joint) return;
                joint.localPosition = position;
                joint.localRotation = rotation;
            }
        }

        sealed class Leg
        {
            public RestJoint thigh, knee;
            public Vector3 ankle;
            public float upperLength, lowerLength, floor, restSideAngle, walkingX;
            public Vector3[] sole;
            public Transform foot;
        }

        sealed class FingerJoint
        {
            public RestJoint rest;
            public Quaternion closedDelta;
            public Quaternion combatClosedDelta;
            public int finger;
        }

        RestJoint pelvis, armL, armR, forearmL, forearmR, headRest, chestRest;
        readonly List<RestJoint> humanoidRest = new List<RestJoint>();
        Vector3[] recoveryPositions;
        Quaternion[] recoveryRotations;
        Vector3[] layerScales;
        bool[] upperBodyMask, bodyMask;
        readonly List<FingerJoint> fingersL = new List<FingerJoint>(), fingersR = new List<FingerJoint>();
        Animator capturedAnimator;
        Vector3 pelvisInRoot;
        const float RelaxedFingerCurl = .3f;
        // Negative means the relaxed default; an explicit SetFingerPose(0) still opens the hand.
        float leftCurl = -1f, rightCurl = -1f;
        bool leftPoint, rightPoint;
        PlayableGraph clipGraph;
        AnimationClipPlayable clipPlayable;
        AnimationClip activeClip;
        PlayableGraph combatClipGraph;
        AnimationClipPlayable combatClipPlayable;
        AnimationClip sampledCombatClip;
        RuntimeAnimatorController combatSavedController;
        RuntimeAnimatorController savedController;
        bool clipLoops;
        public bool HasHumanoidRig => rigAnimator && rigAnimator.avatar && rigAnimator.avatar.isValid && rigAnimator.avatar.isHuman;
        public bool IsClipPlaying => clipGraph.IsValid() && clipPlayable.IsValid() && activeClip;
        public bool IsCombatClipPlaying => combatClipGraph.IsValid() && sampledCombatClip;
        public bool HasRightHandGesture => HasHumanoidRig && (rightCurl > .001f || rightPoint);
        public Vector3 RightGripPosition => GripPosition(true);
        public Vector3 GripPosition(bool right)
        {
            var hand = right ? rightHand : leftHand;
            if (HasHumanoidRig && hand)
            {
                var finger = rigAnimator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                return finger ? Vector3.Lerp(hand.position, finger.position, .65f) : hand.position;
            }
            var forearm = right ? rightForearm : leftForearm;
            var arm = right ? rightArm : leftArm;
            return forearm ? forearm.TransformPoint(forearm.localPosition)
                : arm ? arm.TransformPoint(Vector3.down * .5f) : transform.position + transform.up;
        }
        Leg legL, legR;
        Vector3 previousPosition;
        float phase, blend, filteredSpeed;
        bool seated;
        bool cycling;
        float cyclePhase;
        // Implemented by the player assembly; World must not reference Player (which already references World).
        public interface ICombatPoseSource { void PresentCombat(DaldongneAvatarMotion motion); }
        ICombatPoseSource combatSource;
        MonoBehaviour combatSourceComponent;
        float nextCombatSourceCheck;
        bool externalCombat;
        int combatStep;
        float combatTime, combatContact = .36f, reactionTime, combatDelta;
        string reactionState = "";
        Vector3 reactionDirection;
        bool combatWasFrozen;
        string frozenPoseState = "";

        public string CombatPoseState { get; private set; } = "";
        public bool HasCombatPose => !string.IsNullOrEmpty(CombatPoseState);
        public float CombatFrameDelta => externalCombat ? combatDelta : Time.deltaTime;

        /// <summary>Network presentation uses the same pose layer and normalized clocks as the local player.</summary>
        public void SetCombatPresentation(int step, float swingTime, float contact, string hitState,
            float hitTime, Vector3 localHitDirection, float frameDelta)
        {
            externalCombat = true;
            combatStep = Mathf.Clamp(step, 0, 2);
            combatTime = swingTime;
            combatContact = Mathf.Clamp(contact, .15f, .7f);
            reactionState = hitState ?? "";
            reactionTime = Mathf.Clamp01(hitTime);
            reactionDirection = localHitDirection;
            combatDelta = Mathf.Max(0f, frameDelta);
        }

        void ReadCombatPresentation()
        {
            if (!combatSourceComponent && Time.unscaledTime >= nextCombatSourceCheck)
            {
                nextCombatSourceCheck = Time.unscaledTime + .25f;
                foreach (var component in GetComponentsInParent<MonoBehaviour>(true))
                    if (component is ICombatPoseSource source)
                    { combatSource = source; combatSourceComponent = component; break; }
            }
            if (combatSourceComponent) combatSource.PresentCombat(this);
            else if (!externalCombat) combatTime = -1f;
            CombatPoseState = !string.IsNullOrEmpty(reactionState) ? reactionState
                : combatTime >= 0f && combatTime < 1f ? combatStep == 0 ? "Punch_Jab" : combatStep == 1 ? "Punch_Straight" : "Punch_Hook" : "";
        }

        /// <summary>Bind the imported Avatar without rebuilding its skeleton or skin weights.</summary>
        public void ConfigureHumanoid(Animator animator)
        {
            if (animator && (!animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman))
                throw new System.ArgumentException("A valid Humanoid Avatar is required.", nameof(animator));
            StopClip();
            if (capturedAnimator == animator)
                foreach (var joint in humanoidRest) joint.Restore();
            rigAnimator = animator;
            CaptureRestPose();
            previousPosition = transform.position;
            ResetMotion();
        }

        void BindHumanoid()
        {
            rigAnimator.enabled = false;
            rigAnimator.applyRootMotion = false;
            hips = rigAnimator.GetBoneTransform(HumanBodyBones.Hips);
            head = rigAnimator.GetBoneTransform(HumanBodyBones.Head);
            leftArm = rigAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm = rigAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftForearm = rigAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightForearm = rigAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            leftHand = rigAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = rigAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            leftLeg = rigAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightLeg = rigAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            leftKnee = rigAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            rightKnee = rigAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            leftFoot = rigAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFoot = rigAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            // Imported T/A poses need a relaxed stance for the existing gait.
            // FromToRotation uses actual joint directions and is independent of bone roll.
            LowerArm(leftArm, leftForearm);
            LowerArm(rightArm, rightForearm);
        }

        void LowerArm(Transform upper, Transform lower)
        {
            if (!upper || !lower) return;
            var direction = lower.position - upper.position;
            if (direction.sqrMagnitude > .000001f)
                upper.rotation = Quaternion.FromToRotation(direction, -transform.up) * upper.rotation;
        }

        /// <summary>The clip owns the complete rig until StopClip, completion, sitting or cycling.</summary>
        public void PlayClip(AnimationClip clip, bool loop = false)
        {
            if (!clip) throw new System.ArgumentNullException(nameof(clip));
            if (clip.legacy) throw new System.ArgumentException("Playable clips must use Mecanim import settings.", nameof(clip));
            if (!HasHumanoidRig) throw new System.InvalidOperationException("Configure a Humanoid Avatar before playing a clip.");
            if (!SameRig()) CaptureRestPose();
            StopClip();
            seated = cycling = false;
            Restore();
            savedController = rigAnimator.runtimeAnimatorController;
            rigAnimator.runtimeAnimatorController = null;
            rigAnimator.applyRootMotion = false;
            rigAnimator.enabled = true;
            clipGraph = PlayableGraph.Create(name + " Avatar Clip");
            clipGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            clipPlayable = AnimationClipPlayable.Create(clipGraph, clip);
            clipPlayable.SetApplyFootIK(false);
            clipPlayable.SetApplyPlayableIK(false);
            var output = AnimationPlayableOutput.Create(clipGraph, "Avatar", rigAnimator);
            output.SetSourcePlayable(clipPlayable);
            activeClip = clip; clipLoops = loop;
            clipGraph.Play();
            clipGraph.Evaluate(0);
            previousPosition = transform.position;
        }

        public void StopClip()
        {
            StopCombatClip();
            bool wasPlaying = clipGraph.IsValid();
            if (wasPlaying) clipGraph.Destroy();
            activeClip = null;
            if (wasPlaying && rigAnimator)
            {
                rigAnimator.enabled = false;
                rigAnimator.runtimeAnimatorController = savedController;
                savedController = null;
                ResetMotion();
            }
            previousPosition = transform.position;
        }

        void Update()
        {
            ReadCombatPresentation();
            if (!IsClipPlaying) return;
            if (CombatFrameDelta <= 0f) return;
            double duration = System.Math.Max(.001, activeClip.length);
            double time = clipPlayable.GetTime() + CombatFrameDelta;
            if (!clipLoops && time >= duration) { StopClip(); return; }
            clipPlayable.SetTime(clipLoops ? time % duration : time);
            clipGraph.Evaluate(0);
        }

        /// <summary>Curl both joints and fingertips; point keeps the index finger extended.</summary>
        public void SetFingerPose(bool right, float curl, bool point = false)
        {
            if (!HasHumanoidRig || IsClipPlaying) return;
            if (!SameRig()) CaptureRestPose();
            curl = float.IsFinite(curl) ? Mathf.Clamp01(curl) : 0f;
            if (right) { rightCurl = curl; rightPoint = point; }
            else { leftCurl = curl; leftPoint = point; }
            ApplyFingers(right ? fingersR : fingersL, curl, point);
        }

        static void ApplyFingers(List<FingerJoint> fingers, float curl, bool point)
        {
            if (curl < 0f) curl = RelaxedFingerCurl;
            foreach (var finger in fingers)
            {
                float amount = point && finger.finger == 1 ? 0f : curl;
                Quaternion closed = point ? finger.closedDelta : Quaternion.Slerp(finger.closedDelta,
                    finger.combatClosedDelta, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.75f, 1f, curl)));
                finger.rest.joint.localRotation = finger.rest.rotation * Quaternion.Slerp(Quaternion.identity, closed, amount);
            }
        }

        static void ApplyCombatFist(List<FingerJoint> fingers, float weight)
        {
            foreach (var finger in fingers)
                finger.rest.joint.localRotation = Quaternion.Slerp(finger.rest.joint.localRotation,
                    finger.rest.rotation * finger.combatClosedDelta, weight);
        }

        void CaptureFingers(bool right, List<FingerJoint> output)
        {
            output.Clear();
            var hand = right ? rightHand : leftHand;
            if (!hand) return;
            // HumanBodyBones orders the five fingers as contiguous groups of three.
            int first = (int)(right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
            Transform Bone(int index) => rigAnimator.GetBoneTransform((HumanBodyBones)(first + index));
            var indexBase = Bone(3); var middleBase = Bone(6); var littleBase = Bone(12);
            Vector3 along = indexBase && Bone(4) ? Bone(4).position - indexBase.position : -transform.up;
            Vector3 across = indexBase && littleBase ? littleBase.position - indexBase.position : -transform.forward;
            Vector3 inward = (hips ? hips.position : transform.position) - hand.position;
            Vector3 palm = Vector3.Cross(across, along).normalized;
            // Select the inside of the palm geometrically. Hand labels and cross-product
            // signs alone are unreliable across mirrored FBX coordinate conversions.
            // This selects down in a T pose and towards the thigh with lowered arms.
            if (palm.sqrMagnitude < .5f) palm = Vector3.ProjectOnPlane(inward, along).normalized;
            if (Vector3.Dot(palm, inward) < 0f) palm = -palm;
            float palmWidth = Mathf.Max(.025f, across.magnitude);
            for (int finger = 0; finger < 5; finger++)
            for (int segment = 0; segment < 3; segment++)
            {
                var bone = Bone(finger * 3 + segment);
                if (!bone) continue;
                var next = segment < 2 ? Bone(finger * 3 + segment + 1) : null;
                var previous = segment > 0 ? Bone(finger * 3 + segment - 1) : hand;
                Vector3 direction = next ? next.position - bone.position : previous ? bone.position - previous.position : along;
                Quaternion closed, combatClosed;
                if (finger == 0 && segment == 0 && middleBase)
                {
                    // The thumb crosses the palm at its base instead of curling all
                    // three segments into a long hook. The small palmar offset supplies
                    // opposition as well as the sideways sweep towards the middle finger.
                    Vector3 target = middleBase.position + palm * (palmWidth * .25f);
                    var worldDelta = Quaternion.FromToRotation(direction, target - bone.position);
                    closed = Quaternion.RotateTowards(Quaternion.identity,
                        Quaternion.Inverse(bone.rotation) * worldDelta * bone.rotation, 65f);
                    combatClosed = Quaternion.RotateTowards(Quaternion.identity,
                        Quaternion.Inverse(bone.rotation) * worldDelta * bone.rotation, 75f);
                }
                else
                {
                    Vector3 axis = Vector3.Cross(direction, palm).normalized;
                    if (axis.sqrMagnitude < .5f) axis = Vector3.ProjectOnPlane(across, direction).normalized;
                    float angle = finger == 0 ? (segment == 1 ? 35f : 25f)
                        : segment == 1 ? 65f : segment == 0 ? 50f : 45f;
                    closed = Quaternion.AngleAxis(angle, bone.InverseTransformDirection(axis));
                    // Holding uses the original relaxed curl. A striking fist folds the
                    // middle joint far enough to tuck the fingertips back toward the palm.
                    float combatAngle = finger == 0 ? (segment == 1 ? 30f : 20f)
                        : segment == 1 ? 100f : segment == 0 ? 78f + (finger - 1) * 2f : 55f;
                    combatClosed = Quaternion.AngleAxis(combatAngle, bone.InverseTransformDirection(axis));
                }
                output.Add(new FingerJoint { rest = new RestJoint(bone, transform), closedDelta = closed,
                    combatClosedDelta = combatClosed, finger = finger });
            }
            OpposeCombatThumb(output, palm, across);
        }

        static void OpposeCombatThumb(List<FingerJoint> joints, Vector3 palm, Vector3 across)
        {
            if (joints.Count != 15) return;
            // Aim at the closed index/middle pads; the open-hand knuckle position
            // makes the thumb point away from the fist. Keep each thumb joint bounded.
            for (int i = 3; i < joints.Count; i++)
                joints[i].rest.joint.localRotation = joints[i].rest.rotation * joints[i].combatClosedDelta;
            var thumb0 = joints[0].rest.joint;
            var thumb1 = joints[1].rest.joint;
            var thumb2 = joints[2].rest.joint;
            // The source rig ends at the distal joint. Its pad continues by the
            // authored distal/intermediate length ratio (.28 / .32).
            Vector3 tipLocal = thumb2.InverseTransformVector(thumb2.position - thumb1.position) * .875f;
            Vector3 indexPad = Vector3.Lerp(joints[4].rest.joint.position, joints[5].rest.joint.position, .55f);
            Vector3 middlePad = Vector3.Lerp(joints[7].rest.joint.position, joints[8].rest.joint.position, .55f);
            Vector3 tipTarget = Vector3.Lerp(indexPad, middlePad, .65f) + palm * across.magnitude * .20f;
            Vector3 target = tipTarget - thumb0.position;
            Vector3 axis1 = thumb1.InverseTransformDirection(palm);
            Vector3 axis2 = thumb2.InverseTransformDirection(palm);
            float best = float.PositiveInfinity;
            Quaternion best0 = Quaternion.identity, best1 = Quaternion.identity, best2 = Quaternion.identity;
            // Both short joints bend in the same plane. Unconstrained endpoint
            // iteration can alternate their bend directions and leave a long S
            // curve; an outward virtual elbow instead makes a large C shape.
            // This small rest-pose search fits the measured pad once per rig.
            for (int sign = -1; sign <= 1; sign += 2)
                for (int middleAngle = 10; middleAngle <= 80; middleAngle += 5)
                    for (int tipAngle = 10; tipAngle <= 70; tipAngle += 5)
                    {
                        thumb0.localRotation = joints[0].rest.rotation;
                        Quaternion mid = Quaternion.AngleAxis(sign * middleAngle, axis1);
                        Quaternion end = Quaternion.AngleAxis(sign * tipAngle, axis2);
                        thumb1.localRotation = joints[1].rest.rotation * mid;
                        thumb2.localRotation = joints[2].rest.rotation * end;
                        Vector3 endpoint = thumb2.TransformPoint(tipLocal) - thumb0.position;
                        thumb0.rotation = Quaternion.FromToRotation(endpoint, target) * thumb0.rotation;
                        Quaternion root = Quaternion.RotateTowards(Quaternion.identity,
                            Quaternion.Inverse(joints[0].rest.rotation) * thumb0.localRotation, 100f);
                        thumb0.localRotation = joints[0].rest.rotation * root;
                        float error = (thumb2.TransformPoint(tipLocal) - tipTarget).sqrMagnitude;
                        float outside = Mathf.Max(0f, Vector3.Dot(thumb1.position - indexPad, palm) - across.magnitude * .2f);
                        float score = error * 100f + outside * outside * 2f
                            + (thumb1.position - indexPad).sqrMagnitude * .005f;
                        if (score >= best) continue;
                        best = score; best0 = root; best1 = mid; best2 = end;
                    }
            joints[0].combatClosedDelta = best0;
            joints[1].combatClosedDelta = best1;
            joints[2].combatClosedDelta = best2;
            foreach (var joint in joints) joint.rest.Restore();
        }

        Quaternion Delta(RestJoint joint, Quaternion delta)
        {
            if (!HasHumanoidRig) return joint.rotation * delta;
            return joint.rotation * Quaternion.Inverse(joint.rootRotation) * delta * joint.rootRotation;
        }

        void Rotate(RestJoint joint, Vector3 angles)
        {
            if (joint != null && joint.joint) joint.joint.localRotation = Delta(joint, Quaternion.Euler(angles));
        }

        /// <summary>Shared local/remote hold and punch pose; never overwrites a playing clip.</summary>
        public void ApplyRightArmInteraction(float hold, float punch, float recoil)
        {
            if (IsClipPlaying || HasCombatPose) return;
            if (!SameRig()) CaptureRestPose();
            hold = Mathf.Clamp01(hold); punch = Mathf.Clamp01(punch); recoil = Mathf.Clamp01(recoil);
            if (rightArm)
            {
                var held = Delta(armR, Quaternion.Euler(rightForearm ? -12f : -25f, 0, 0));
                rightArm.localRotation = Quaternion.Slerp(RightArmPose, held, hold);
                rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation, Delta(armR, Quaternion.Euler(-76f, 0, -3f)), punch);
            }
            if (rightForearm)
            {
                var held = HasHumanoidRig ? Delta(forearmR, Quaternion.Euler(-58f, 0, 0)) : Quaternion.Euler(-58f, 0, 0);
                var hit = HasHumanoidRig ? Delta(forearmR, Quaternion.Euler(-12f + 8f * recoil, 0, 0)) : Quaternion.Euler(-12f + 8f * recoil, 0, 0);
                rightForearm.localRotation = Quaternion.Slerp(RightForearmPose, held, hold);
                rightForearm.localRotation = Quaternion.Slerp(rightForearm.localRotation, hit, punch);
            }
            if (HasHumanoidRig)
            {
                float restingCurl = rightCurl < 0f ? RelaxedFingerCurl : rightCurl;
                float interactionCurl = Mathf.Max(Mathf.Lerp(restingCurl, .75f, hold), punch);
                ApplyFingers(fingersR, interactionCurl, hold <= .001f && punch <= .001f && rightPoint);
            }
        }

        public void SetCycling(bool value, float pedalPhase)
        {
            if (IsClipPlaying && !value) { cycling = false; return; }
            if (value) { StopClip(); seated = false; }
            if (!SameRig()) CaptureRestPose();
            bool changed = cycling != value;
            cycling = value; cyclePhase = pedalPhase;
            if (cycling) ApplyCyclingPose();
            else if (changed) ResetMotion();
        }
        void ApplyCyclingPose()
        {
            Restore();
            if (!hips || !leftLeg || !rightLeg || !leftKnee || !rightKnee) return;
            var p = HasHumanoidRig ? pelvisInRoot : hips.localPosition; p.y = .98f; p.z -= .35f;
            if (HasHumanoidRig) hips.position = transform.TransformPoint(p); else hips.localPosition = p;
            Rotate(pelvis, new Vector3(8f, 0, 0));
            float pedal = Mathf.Sin(cyclePhase);
            Rotate(legL.thigh, new Vector3(-48f + 24f * pedal, 0, -5f));
            Rotate(legR.thigh, new Vector3(-48f - 24f * pedal, 0, 5f));
            Rotate(legL.knee, new Vector3(70f - 28f * pedal, 0, 0));
            Rotate(legR.knee, new Vector3(70f + 28f * pedal, 0, 0));
            Rotate(armL, new Vector3(-62f, 0, -8f));
            Rotate(armR, new Vector3(-62f, 0, 8f));
            Rotate(forearmL, new Vector3(-23f, 0, 0));
            Rotate(forearmR, new Vector3(-23f, 0, 0));
            CacheArmPose();
        }
        public void SetSeated(bool value)
        {
            if (IsClipPlaying && !value) { seated = false; return; }
            if (value) { StopClip(); cycling = false; }
            if (!SameRig()) CaptureRestPose();
            seated = value;
            ResetMotion();
            if (seated) ApplySeatedPose();
        }
        void ApplySeatedPose()
        {
            Restore();
            if (!hips || !leftLeg || !rightLeg || !leftKnee || !rightKnee) return;
            var p = HasHumanoidRig ? pelvisInRoot : hips.localPosition; p.y = .54f;
            if (HasHumanoidRig) hips.position = transform.TransformPoint(p); else hips.localPosition = p;
            Rotate(legL.thigh, new Vector3(-90f, 0, 0));
            Rotate(legR.thigh, new Vector3(-90f, 0, 0));
            Rotate(legL.knee, new Vector3(90f, 0, 0));
            Rotate(legR.knee, new Vector3(90f, 0, 0));
            // Hands rest on the thighs. A forearm level with the hips read as gripping a steering wheel.
            // ponytail: angles tuned by eye, adjust here if a rig's hands sink into the legs.
            float upper = leftForearm ? -10f : -28f;
            Rotate(armL, new Vector3(upper, 0, -6f));
            Rotate(armR, new Vector3(upper, 0, 6f));
            Rotate(forearmL, new Vector3(-38f, 0, 0));
            Rotate(forearmR, new Vector3(-38f, 0, 0));
            CacheArmPose();
        }
        public Quaternion RightArmPose { get; private set; } = Quaternion.identity;
        public Quaternion RightForearmPose { get; private set; } = Quaternion.identity;
        public Quaternion RightArmRest => armR != null ? armR.rotation : rightArm ? rightArm.localRotation : Quaternion.identity;

        void OnEnable()
        {
            nextCombatSourceCheck = 0f;
            previousPosition = transform.position;
            if (HasHumanoidRig && !SameRig()) CaptureRestPose();
            ResetMotion();
        }
        void OnDisable() { StopClip(); ResetMotion(); }
        void OnDestroy()
        {
            if (clipGraph.IsValid()) clipGraph.Destroy();
            if (combatClipGraph.IsValid()) combatClipGraph.Destroy();
        }

        void LateUpdate()
        {
            ReadCombatPresentation();
            float dt = CombatFrameDelta;
            // Freeze this character's last complete pose, including clip time and gait.
            // Other avatars, the camera, input and UI retain their own normal clocks.
            if (dt <= 0)
            {
                if (!combatWasFrozen || frozenPoseState != CombatPoseState)
                {
                    // The impact is accepted during Update. Sample that contact/recoil once,
                    // then freeze it; retaining the previous frame freezes the wind-up instead.
                    if (IsClipPlaying) clipGraph.Evaluate(0f);
                    else if (cycling) ApplyCyclingPose();
                    else if (seated) ApplySeatedPose();
                    else Pose(filteredSpeed, .000001f);
                    ApplyCombatPose();
                    CacheArmPose();
                }
                combatWasFrozen = true;
                frozenPoseState = CombatPoseState;
                previousPosition = transform.position;
                return;
            }
            combatWasFrozen = false;
            if (IsClipPlaying) { previousPosition = transform.position; ApplyCombatPose(); CacheArmPose(); return; }
            if (cycling) { ApplyCyclingPose(); previousPosition = transform.position; return; }
            if (seated) { ApplySeatedPose(); previousPosition = transform.position; return; }
            Vector3 delta = transform.position - previousPosition;
            previousPosition = transform.position;
            float speed = Vector3.ProjectOnPlane(delta, Vector3.up).magnitude / dt;
            // Reset and scene teleports must never become a very fast stride.
            if (speed > 8 || delta.sqrMagnitude > 1)
            {
                ResetMotion();
                ApplyCombatPose();
                return;
            }
            Pose(speed, dt);
            ApplyCombatPose();
            CacheArmPose();
        }

        void BlendJoint(RestJoint joint, Vector3 angles, float weight)
        {
            if (joint == null || !joint.joint) return;
            joint.joint.localRotation = Quaternion.Slerp(joint.joint.localRotation, Delta(joint, Quaternion.Euler(angles)), Mathf.Clamp01(weight));
        }

        void AddJoint(RestJoint joint, Vector3 angles, float weight)
        {
            if (joint == null || !joint.joint) return;
            Quaternion delta = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(angles), Mathf.Clamp01(weight));
            // Apply root-space motion to the evaluated clip/gait, respecting imported bone roll.
            joint.joint.rotation = transform.rotation * delta * Quaternion.Inverse(transform.rotation) * joint.joint.rotation;
        }

        /// <summary>Shared anticipation/contact/recovery curve for world and first-person fists.</summary>
        public static float PunchExtension(float normalizedTime, float contact)
        {
            if (normalizedTime < 0f || normalizedTime >= 1f) return 0f;
            contact = Mathf.Clamp(contact, .15f, .7f);
            return normalizedTime <= contact
                ? Ease(Mathf.InverseLerp(contact * .42f, contact, normalizedTime))
                : 1f - Ease(Mathf.InverseLerp(contact, 1f, normalizedTime));
        }

        void ApplyCombatPose()
        {
            if (!HasCombatPose) { StopCombatClip(); return; }
            if (!SameRig()) CaptureRestPose();
            if (TryApplyCombatClip()) return;
            if (!string.IsNullOrEmpty(reactionState)) { ApplyReactionPose(); return; }
            float t = Mathf.Clamp01(combatTime);
            float weight = Ease(Mathf.Clamp01(t / .12f)) * (1f - Ease(Mathf.InverseLerp(.72f, 1f, t)));
            float extension = PunchExtension(t, combatContact);
            bool right = combatStep == 1;
            bool hook = combatStep == 2;
            RestJoint striking = right ? armR : armL, elbow = right ? forearmR : forearmL;
            RestJoint guarding = right ? armL : armR, guardElbow = right ? forearmL : forearmR;
            float side = right ? 1f : -1f;
            // Only chest/head/arms change: the lower-body gait or playing dance clip continues.
            AddJoint(chestRest, new Vector3(3f * extension, (hook ? 25f : right ? -15f : 8f) * extension, -side * 3f * extension), weight);
            AddJoint(headRest, new Vector3(-2f, side * 5f * extension, 0), weight);
            if (HasHumanoidRig && leftHand && rightHand)
            {
                PoseCombatArm(!right, false, 0f, weight, true);
                PoseCombatArm(right, hook, extension, weight, false);
                ApplyCombatFist(fingersL, weight);
                ApplyCombatFist(fingersR, weight);
                return;
            }
            BlendJoint(guarding, new Vector3(-25f, 0, -side * 8f), weight);
            BlendJoint(guardElbow, new Vector3(-103f, 0, 0), weight);
            Vector3 upper = hook
                ? new Vector3(Mathf.Lerp(-47f, -78f, extension), Mathf.Lerp(-37f, 48f, extension), Mathf.Lerp(-56f, -14f, extension))
                : new Vector3(Mathf.Lerp(-21f, right ? -88f : -82f, extension), side * Mathf.Lerp(-8f, 7f, extension), side * 6f);
            Vector3 lower = new Vector3(hook ? Mathf.Lerp(-92f, -78f, extension) : Mathf.Lerp(-108f, -8f, extension), 0, 0);
            BlendJoint(striking, upper, weight);
            BlendJoint(elbow, lower, weight);
            ApplyCombatFist(fingersL, weight);
            ApplyCombatFist(fingersR, weight);
        }

        void PoseCombatArm(bool right, bool hook, float extension, float weight, bool guard)
        {
            RestJoint upper = right ? armR : armL, lower = right ? forearmR : forearmL;
            Transform hand = right ? rightHand : leftHand;
            if (upper == null || lower == null || !upper.joint || !lower.joint || !hand) return;
            float side = right ? 1f : -1f;
            Vector3 shoulder = transform.InverseTransformPoint(upper.joint.position);
            float scale = Mathf.Max(.001f, transform.lossyScale.y);
            float reach = (Vector3.Distance(upper.joint.position, lower.joint.position)
                + Vector3.Distance(lower.joint.position, hand.position)) / scale;
            Vector3 ready = shoulder + new Vector3(-side * .045f, .025f, reach * .34f);
            Vector3 contact = shoulder + new Vector3(-side * .13f, right ? -.07f : .025f, reach * .97f);
            Vector3 bend = new Vector3(side * .5f, -1f, 0f);
            if (hook)
            {
                ready = shoulder + new Vector3(-.17f, .035f, reach * .3f);
                contact = shoulder + new Vector3(.13f, .035f, reach * .82f);
                // Elbow and fist travel around a horizontal arc at shoulder height.
                bend = Vector3.left;
            }
            Vector3 target = transform.TransformPoint(guard ? ready : Vector3.Lerp(ready, contact, extension));
            Vector3 origin = upper.joint.position;
            float a = Vector3.Distance(origin, lower.joint.position);
            float b = Vector3.Distance(lower.joint.position, hand.position);
            Vector3 direction = target - origin;
            if (a < .0001f || b < .0001f || direction.sqrMagnitude < .000001f) return;
            float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            direction.Normalize();
            Vector3 bendDirection = Vector3.ProjectOnPlane(transform.TransformDirection(bend), direction).normalized;
            if (bendDirection.sqrMagnitude < .5f) bendDirection = Vector3.ProjectOnPlane(-transform.up, direction).normalized;
            float along = (a * a + distance * distance - b * b) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 elbowPosition = origin + direction * along + bendDirection * height;
            Quaternion originalUpper = upper.joint.localRotation, originalLower = lower.joint.localRotation;
            upper.joint.rotation = Quaternion.FromToRotation(lower.joint.position - origin, elbowPosition - origin) * upper.joint.rotation;
            lower.joint.rotation = Quaternion.FromToRotation(hand.position - lower.joint.position,
                origin + direction * distance - lower.joint.position) * lower.joint.rotation;
            Quaternion solvedUpper = upper.joint.localRotation, solvedLower = lower.joint.localRotation;
            upper.joint.localRotation = Quaternion.Slerp(originalUpper, solvedUpper, weight);
            lower.joint.localRotation = Quaternion.Slerp(originalLower, solvedLower, weight);
        }

        AnimationClip SelectedCombatClip()
        {
            switch (CombatPoseState)
            {
                case "Punch_Jab": return Punch_Jab;
                case "Punch_Straight": return Punch_Straight;
                case "Punch_Hook": return Punch_Hook;
                case "Hit_Face": return Hit_Face;
                case "Hit_Body": return Hit_Body;
                case "Hit_Heavy": return Hit_Heavy;
                case "Knockdown": return Knockdown;
                case "GetUp": return GetUp;
                default: return null;
            }
        }

        void StopCombatClip()
        {
            bool wasPlaying = combatClipGraph.IsValid();
            if (wasPlaying) combatClipGraph.Destroy();
            sampledCombatClip = null;
            if (wasPlaying && rigAnimator && !IsClipPlaying)
            {
                rigAnimator.enabled = false;
                rigAnimator.runtimeAnimatorController = combatSavedController;
            }
            combatSavedController = null;
        }

        bool TryApplyCombatClip()
        {
            AnimationClip clip = SelectedCombatClip();
            if (!clip || clip.legacy || !HasHumanoidRig) { StopCombatClip(); return false; }
            // Preserve the evaluated gait/dance and sample an optional clip under this same owner.
            Vector3 rootPosition = transform.localPosition, rootScale = transform.localScale;
            Quaternion rootRotation = transform.localRotation;
            for (int i = 0; i < humanoidRest.Count; i++)
            {
                var joint = humanoidRest[i].joint;
                recoveryPositions[i] = joint.localPosition;
                recoveryRotations[i] = joint.localRotation;
                layerScales[i] = joint.localScale;
            }
            if (!combatClipGraph.IsValid() || sampledCombatClip != clip)
            {
                StopCombatClip();
                combatSavedController = rigAnimator.runtimeAnimatorController;
                rigAnimator.runtimeAnimatorController = null;
                rigAnimator.applyRootMotion = false;
                rigAnimator.enabled = true;
                combatClipGraph = PlayableGraph.Create(name + " Combat Overlay");
                combatClipGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                combatClipPlayable = AnimationClipPlayable.Create(combatClipGraph, clip);
                combatClipPlayable.SetApplyFootIK(false);
                combatClipPlayable.SetApplyPlayableIK(false);
                var output = AnimationPlayableOutput.Create(combatClipGraph, "Combat", rigAnimator);
                output.SetSourcePlayable(combatClipPlayable);
                sampledCombatClip = clip;
                combatClipGraph.Play();
            }
            bool reacting = !string.IsNullOrEmpty(reactionState);
            bool fullBody = reactionState == "Knockdown" || reactionState == "GetUp";
            float t = Mathf.Clamp01(reacting ? reactionTime : combatTime);
            float weight = fullBody ? reactionState == "GetUp" ? 1f - Ease(Mathf.InverseLerp(.8f, 1f, t))
                : Ease(Mathf.Clamp01(t / .1f))
                : Ease(Mathf.Clamp01(t / .1f)) * (1f - Ease(Mathf.InverseLerp(.75f, 1f, t)));
            combatClipPlayable.SetTime(t * System.Math.Max(.001, clip.length));
            combatClipGraph.Evaluate(0f);
            transform.localPosition = rootPosition;
            transform.localRotation = rootRotation;
            transform.localScale = rootScale;
            for (int i = 0; i < humanoidRest.Count; i++)
            {
                var joint = humanoidRest[i].joint;
                float amount = (fullBody ? bodyMask[i] : upperBodyMask[i]) ? weight : 0f;
                joint.localPosition = Vector3.Lerp(recoveryPositions[i], joint.localPosition, amount);
                joint.localRotation = Quaternion.Slerp(recoveryRotations[i], joint.localRotation, amount);
                joint.localScale = layerScales[i];
            }
            return true;
        }

        void ApplyReactionPose()
        {
            float t = Mathf.Clamp01(reactionTime);
            Vector3 direction = reactionDirection.sqrMagnitude > .0001f ? reactionDirection.normalized : Vector3.back;
            bool down = reactionState == "Knockdown", gettingUp = reactionState == "GetUp";
            if (down || gettingUp)
            {
                float returnToBase = gettingUp ? Ease(Mathf.InverseLerp(.75f, 1f, t)) : 0f;
                if (HasHumanoidRig)
                {
                    if (returnToBase > 0f)
                        for (int i = 0; i < humanoidRest.Count; i++)
                        {
                            recoveryPositions[i] = humanoidRest[i].joint.localPosition;
                            recoveryRotations[i] = humanoidRest[i].joint.localRotation;
                        }
                    // A dance remains scheduled, but its torso must not keep dancing on the ground.
                    Restore();
                }
                // Visual-only collapse. CharacterController/server retain movement ownership.
                float fall = down ? Ease(Mathf.Clamp01(t / .35f)) : 1f - Ease(t);
                float kneel = gettingUp ? Mathf.Sin(t * Mathf.PI) : 0f;
                float pitch = -82f * direction.z;
                if (hips)
                {
                    hips.position = transform.TransformPoint(pelvisInRoot + new Vector3(-direction.x * .11f * fall,
                        -(pelvisInRoot.y - .2f) * fall - .16f * kneel, -direction.z * .13f * fall));
                    BlendJoint(pelvis, new Vector3(pitch * fall, 0, direction.x * 82f * fall), 1f);
                }
                BlendJoint(legL.thigh, new Vector3(-28f * fall - 48f * kneel, 0, -12f * fall), 1f);
                BlendJoint(legR.thigh, new Vector3(-17f * fall - 65f * kneel, 0, 9f * fall), 1f);
                BlendJoint(legL.knee, new Vector3(46f * fall + 58f * kneel, 0, 0), 1f);
                BlendJoint(legR.knee, new Vector3(33f * fall + 67f * kneel, 0, 0), 1f);
                BlendJoint(armL, new Vector3(-25f * fall - 45f * kneel, 0, -36f * fall), 1f);
                BlendJoint(armR, new Vector3(-15f * fall - 62f * kneel, 0, 32f * fall), 1f);
                BlendJoint(forearmL, new Vector3(-38f * fall - 35f * kneel, 0, 0), 1f);
                BlendJoint(forearmR, new Vector3(-29f * fall - 30f * kneel, 0, 0), 1f);
                if (HasHumanoidRig && returnToBase > 0f)
                    for (int i = 0; i < humanoidRest.Count; i++)
                    {
                        var joint = humanoidRest[i].joint;
                        joint.localPosition = Vector3.Lerp(joint.localPosition, recoveryPositions[i], returnToBase);
                        joint.localRotation = Quaternion.Slerp(joint.localRotation, recoveryRotations[i], returnToBase);
                    }
                return;
            }
            float impact = Ease(Mathf.Clamp01(t / .13f)) * (1f - Ease(Mathf.InverseLerp(.2f, 1f, t)));
            bool face = reactionState == "Hit_Face", heavy = reactionState == "Hit_Heavy";
            float strength = heavy ? 1.55f : 1f;
            AddJoint(chestRest, new Vector3((heavy ? -25f : face ? -9f : 27f) * direction.z * strength,
                -direction.x * 19f * strength, direction.x * 13f * strength), impact);
            AddJoint(headRest, new Vector3(face ? -direction.z * 23f : 13f,
                -direction.x * (face ? 32f : 13f), direction.x * 19f), impact);
            BlendJoint(armL, new Vector3(face ? -38f : -16f, 0, -14f), impact);
            BlendJoint(armR, new Vector3(face ? -36f : -20f, 0, 14f), impact);
            BlendJoint(forearmL, new Vector3(face ? -112f : -71f, 0, 0), impact);
            BlendJoint(forearmR, new Vector3(face ? -108f : -67f, 0, 0), impact);
            if (heavy && hips) hips.position += transform.TransformVector(new Vector3(-direction.x * .035f, -.055f, -direction.z * .03f) * impact);
        }

        void ResetMotion()
        {
            phase = blend = filteredSpeed = 0;
            Restore();
        }
        void Restore()
        {
            if (HasHumanoidRig && humanoidRest.Count > 0)
            {
                foreach (var joint in humanoidRest) joint.Restore();
                ApplyFingers(fingersL, leftCurl, leftPoint);
                ApplyFingers(fingersR, rightCurl, rightPoint);
                CacheArmPose();
                return;
            }
            pelvis?.Restore(); armL?.Restore(); armR?.Restore(); headRest?.Restore();
            forearmL?.Restore(); forearmR?.Restore();
            legL?.thigh.Restore(); legL?.knee.Restore();
            legR?.thigh.Restore(); legR?.knee.Restore();
            CacheArmPose();
        }
        void CacheArmPose()
        {
            if (rightArm) RightArmPose = rightArm.localRotation;
            if (rightForearm) RightForearmPose = rightForearm.localRotation;
        }
        bool SameRig() => capturedAnimator == rigAnimator && pelvis != null && pelvis.joint == hips && armL.joint == leftArm
            && armR.joint == rightArm && legL.thigh.joint == leftLeg && legR.thigh.joint == rightLeg
            && legL.knee.joint == leftKnee && legR.knee.joint == rightKnee
            && forearmL.joint == leftForearm && forearmR.joint == rightForearm;

        void CaptureRestPose()
        {
            // The importer can replace the hierarchy on the same component.
            // Retain each avatar's own hip height and authored arm rotations.
            humanoidRest.Clear(); fingersL.Clear(); fingersR.Clear();
            if (HasHumanoidRig)
            {
                BindHumanoid();
                foreach (var joint in rigAnimator.GetComponentsInChildren<Transform>(true))
                    if (joint != transform) humanoidRest.Add(new RestJoint(joint, transform));
                CaptureFingers(false, fingersL); CaptureFingers(true, fingersR);
            }
            recoveryPositions = new Vector3[humanoidRest.Count];
            recoveryRotations = new Quaternion[humanoidRest.Count];
            layerScales = new Vector3[humanoidRest.Count];
            upperBodyMask = new bool[humanoidRest.Count];
            bodyMask = new bool[humanoidRest.Count];
            Transform spine = HasHumanoidRig ? rigAnimator.GetBoneTransform(HumanBodyBones.Spine) : null;
            for (int i = 0; i < humanoidRest.Count; i++)
            {
                Transform joint = humanoidRest[i].joint;
                bodyMask[i] = hips && (joint == hips || joint.IsChildOf(hips));
                upperBodyMask[i] = (spine && (joint == spine || joint.IsChildOf(spine))) ||
                    (leftArm && (joint == leftArm || joint.IsChildOf(leftArm))) ||
                    (rightArm && (joint == rightArm || joint.IsChildOf(rightArm))) ||
                    (head && (joint == head || joint.IsChildOf(head)));
            }
            capturedAnimator = rigAnimator;
            pelvis = new RestJoint(hips, transform);
            pelvisInRoot = hips ? transform.InverseTransformPoint(hips.position) : Vector3.zero;
            armL = new RestJoint(leftArm, transform); armR = new RestJoint(rightArm, transform);
            forearmL = new RestJoint(leftForearm, transform); forearmR = new RestJoint(rightForearm, transform);
            headRest = new RestJoint(head ? head : hips ? hips.Find("Head") : null, transform);
            Transform chest = HasHumanoidRig ? rigAnimator.GetBoneTransform(HumanBodyBones.UpperChest) : null;
            if (!chest && HasHumanoidRig) chest = rigAnimator.GetBoneTransform(HumanBodyBones.Chest);
            if (!chest && HasHumanoidRig) chest = rigAnimator.GetBoneTransform(HumanBodyBones.Spine);
            chestRest = new RestJoint(chest, transform);
            legL = CaptureLeg(leftLeg, leftKnee);
            legR = CaptureLeg(rightLeg, rightKnee);
            phase = blend = filteredSpeed = 0;
        }

        Leg CaptureLeg(Transform thigh, Transform knee)
        {
            var leg = new Leg { thigh = new RestJoint(thigh, transform), knee = new RestJoint(knee, transform),
                foot = HasHumanoidRig ? (knee == leftKnee ? leftFoot : rightFoot) : null };
            if (!thigh || !knee) return leg;
            leg.upperLength = Vector3.Distance(thigh.position, knee.position) / Mathf.Max(.001f, transform.lossyScale.y);
            var samples = new List<Vector3>();
            foreach (var filter in knee.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (!mesh) continue;
                if (mesh.isReadable)
                {
                    foreach (var v in mesh.vertices)
                        samples.Add(knee.InverseTransformPoint(filter.transform.TransformPoint(v)));
                }
                else
                {
                    var b = mesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = b.center + Vector3.Scale(b.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        samples.Add(knee.InverseTransformPoint(filter.transform.TransformPoint(corner)));
                    }
                }
            }
            // A continuous skin has no MeshFilter beneath the knee. Sample its
            // rest-pose shin/foot vertices once, retaining the same sole solver.
            foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skin.sharedMesh;
                int bone = System.Array.IndexOf(skin.bones, knee);
                if (!mesh || !mesh.isReadable || bone < 0) continue;
                var included = new HashSet<int> { bone };
                if (HasHumanoidRig)
                    for (int i = 0; i < skin.bones.Length; i++)
                        if (skin.bones[i] && skin.bones[i].IsChildOf(knee)) included.Add(i);
                var vertices = mesh.vertices;
                var weights = mesh.boneWeights;
                for (int i = 0; i < vertices.Length && i < weights.Length; i++)
                {
                    var w = weights[i];
                    float influence = (included.Contains(w.boneIndex0) ? w.weight0 : 0)
                        + (included.Contains(w.boneIndex1) ? w.weight1 : 0)
                        + (included.Contains(w.boneIndex2) ? w.weight2 : 0)
                        + (included.Contains(w.boneIndex3) ? w.weight3 : 0);
                    if (influence > .5f)
                        samples.Add(knee.InverseTransformPoint(skin.transform.TransformPoint(vertices[i])));
                }
            }
            if (samples.Count == 0) samples.Add(Vector3.down * leg.upperLength);
            // Cache a lower silhouette, including each rig's actual heel and toe.
            // No per-frame allocations or per-frame mesh reads are required.
            var outline = new List<Vector3>();
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2 / 24;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 direction = new Vector3(side * .15f, Mathf.Cos(a), Mathf.Sin(a));
                    if (HasHumanoidRig) direction = knee.InverseTransformDirection(transform.TransformDirection(direction));
                    Vector3 best = samples[0]; float score = Vector3.Dot(best, direction);
                    foreach (var p in samples)
                    {
                        float candidate = Vector3.Dot(p, direction);
                        if (candidate > score) { best = p; score = candidate; }
                    }
                    if (!outline.Contains(best)) outline.Add(best);
                }
            }
            leg.sole = outline.ToArray();
            leg.floor = SoleHeight(leg);
            Vector3 kneeInRoot = transform.InverseTransformPoint(knee.position);
            // The boot is rigid with the shin; the virtual ankle is inside it.
            float ankleHeight = Mathf.Clamp((kneeInRoot.y - leg.floor) * .23f, .065f, .11f);
            leg.ankle = new Vector3(kneeInRoot.x, leg.floor + ankleHeight, kneeInRoot.z);
            leg.lowerLength = Mathf.Max(.08f, kneeInRoot.y - leg.ankle.y);
            if (leg.foot)
            {
                leg.ankle = transform.InverseTransformPoint(leg.foot.position);
                leg.lowerLength = Mathf.Max(.08f, Vector3.Distance(kneeInRoot, leg.ankle));
            }
            var thighInRoot = transform.InverseTransformPoint(thigh.position);
            leg.restSideAngle = Mathf.Atan2(leg.ankle.x - thighInRoot.x, thighInRoot.y - leg.ankle.y) * Mathf.Rad2Deg;
            // Imported A-pose feet are wider than a walking stance. Keep each foot
            // near its own hip, with a small outward margin, without changing the rig.
            leg.walkingX = Mathf.Lerp(thighInRoot.x, leg.ankle.x, .25f);
            return leg;
        }

        float SoleHeight(Leg leg)
        {
            if (leg.sole == null || !leg.knee.joint) return float.PositiveInfinity;
            float minimum = float.PositiveInfinity;
            foreach (var p in leg.sole)
                minimum = Mathf.Min(minimum, transform.InverseTransformPoint(leg.knee.joint.TransformPoint(p)).y);
            return minimum;
        }
        static float Ease(float t) => t * t * (3 - 2 * t);

        static Vector2 FootPath(float cycle, float contact, float stride, float lift)
        {
            if (cycle < contact)
                return new Vector2(Mathf.Lerp(stride, -stride, cycle / contact), 0);
            float u = (cycle - contact) / (1 - contact);
            // Match backward velocity at toe-off and landing, avoiding a sharp
            // stop/reversal. A smooth arch clears the ground during swing.
            float tangent = -2 * stride * (1 - contact) / contact;
            float z = Mathf.Lerp(-stride, stride, Ease(u)) + tangent * (2 * u * u * u - 3 * u * u + u);
            float arc = Mathf.Sin(Mathf.PI * u);
            return new Vector2(z, arc * arc * lift);
        }

        static Vector2 RunningFootPath(float cycle, float contact, float stride, float lift)
        {
            Vector2 foot = FootPath(cycle, contact, stride, lift);
            if (cycle < contact) return foot;
            float u = (cycle - contact) / (1 - contact);
            // Recover the heel behind the body before bringing the knee forwards.
            // A symmetric lift at mid-swing reads as marching at running speed.
            float recovery = u + .16f * Mathf.Sin(Mathf.PI * u);
            float arc = Mathf.Sin(Mathf.PI * recovery);
            foot.y = arc * arc * lift;
            return foot;
        }

        static float FlightArc(float cycle, float contact)
        {
            if (contact >= .5f) return 0;
            float step = Mathf.Repeat(cycle, .5f);
            if (step <= contact) return 0;
            float t = (step - contact) / (.5f - contact);
            float arc = Mathf.Sin(Mathf.PI * t);
            return arc * arc;
        }

        void SolveLeg(Leg leg, Vector2 foot, float side, float weight)
        {
            if (!leg.thigh.joint || !leg.knee.joint) return;
            if (HasHumanoidRig && leg.foot) { SolveHumanoidLeg(leg, foot, weight); return; }
            var thigh = leg.thigh.joint;
            Vector3 target = new Vector3(leg.ankle.x * footSpacing, leg.ankle.y + foot.y, leg.ankle.z + foot.x);
            Vector3 local = thigh.parent.InverseTransformPoint(transform.TransformPoint(target)) - leg.thigh.position;
            float down = Mathf.Max(.08f, -local.y);
            float sagittalDown = Mathf.Sqrt(down * down + local.x * local.x);
            float distance = Mathf.Clamp(Mathf.Sqrt(sagittalDown * sagittalDown + local.z * local.z),
                Mathf.Abs(leg.upperLength - leg.lowerLength) + .001f, leg.upperLength + leg.lowerLength - .001f);
            float hipTriangle = Mathf.Acos(Mathf.Clamp((leg.upperLength * leg.upperLength + distance * distance - leg.lowerLength * leg.lowerLength)
                / (2 * leg.upperLength * distance), -1, 1));
            float kneeAngle = Mathf.PI - Mathf.Acos(Mathf.Clamp((leg.upperLength * leg.upperLength + leg.lowerLength * leg.lowerLength - distance * distance)
                / (2 * leg.upperLength * leg.lowerLength), -1, 1));
            float hipAngle = -(Mathf.Atan2(local.z, sagittalDown) + hipTriangle) * Mathf.Rad2Deg;
            // The bind pose already includes the thigh-to-knee lateral offset.
            // Subtract it instead of adding a second outward rotation each step.
            float sideAngle = Mathf.Atan2(local.x, down) * Mathf.Rad2Deg - leg.restSideAngle;
            // Positive X at the knee folds the heel back, like a human knee.
            var thighPose = Quaternion.Euler(0, 0, sideAngle) * Quaternion.Euler(hipAngle, 0, 0);
            thigh.localRotation = leg.thigh.rotation * Quaternion.Slerp(Quaternion.identity, thighPose, weight);
            leg.knee.joint.localRotation = leg.knee.rotation * Quaternion.Euler(kneeAngle * Mathf.Rad2Deg * weight, 0, 0);
        }

        // Geometric two-bone solve: imported bone roll and intermediate parents do not define bend axes.
        void SolveHumanoidLeg(Leg leg, Vector2 foot, float weight)
        {
            var thigh = leg.thigh.joint; var knee = leg.knee.joint;
            leg.thigh.Restore(); leg.knee.Restore();
            Vector3 target = transform.TransformPoint(new Vector3(leg.walkingX * footSpacing,
                leg.ankle.y + foot.y, leg.ankle.z + foot.x));
            Vector3 from = thigh.position;
            Vector3 direction = target - from;
            float upper = Vector3.Distance(from, knee.position);
            float lower = Vector3.Distance(knee.position, leg.foot.position);
            if (upper < .0001f || lower < .0001f || direction.sqrMagnitude < .000001f) return;
            float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(upper - lower) + .0001f, upper + lower - .0001f);
            direction.Normalize();
            Vector3 bend = Vector3.ProjectOnPlane(transform.forward, direction).normalized;
            if (bend.sqrMagnitude < .5f) bend = Vector3.ProjectOnPlane(transform.up, direction).normalized;
            float along = (upper * upper + distance * distance - lower * lower) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 desiredKnee = from + direction * along + bend * height;
            thigh.rotation = Quaternion.FromToRotation(knee.position - from, desiredKnee - from) * thigh.rotation;
            thigh.localRotation = Quaternion.Slerp(leg.thigh.rotation, thigh.localRotation, weight);
            knee.rotation = Quaternion.FromToRotation(leg.foot.position - knee.position, target - knee.position) * knee.rotation;
            knee.localRotation = Quaternion.Slerp(leg.knee.rotation, knee.localRotation, weight);
        }

        public void Pose(float speed, float dt)
        {
            if (IsClipPlaying) return;
            if (!SameRig()) CaptureRestPose();
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed > 8)
            {
                ResetMotion();
                return;
            }
            dt = Mathf.Min(dt, .1f);
            if (HasHumanoidRig) Restore();
            speed = Mathf.Max(0, speed);
            float response = 1 - Mathf.Exp(-12 * dt);
            filteredSpeed = Mathf.Lerp(filteredSpeed, speed, response);
            blend = Mathf.Lerp(blend, Mathf.Clamp01(filteredSpeed / .55f), response);
            if (speed < .01f && filteredSpeed < .005f && blend < .001f)
            {
                ResetMotion();
                return;
            }

            float run = Ease(Mathf.InverseLerp(2.9f, 4.5f, filteredSpeed));
            // Keep the accepted walking poses, including their transition near
            // 3 m/s. Only faster movement blends into the new running cycle.
            float running = Ease(Mathf.InverseLerp(3.1f, 4.5f, filteredSpeed));
            float pace = Mathf.Clamp01(filteredSpeed / 3);
            float cadence = Mathf.Lerp(.85f, 1.7f, pace) + run * .35f;
            cadence = Mathf.Lerp(cadence, 2.15f, running);
            phase = Mathf.Repeat(phase + cadence * dt * blend, 1);
            float contact = Mathf.Lerp(.62f, .55f, run);
            contact = Mathf.Lerp(contact, .415f, running);
            float stride = Mathf.Lerp(.095f, .26f, pace) + run * .035f;
            stride = Mathf.Lerp(stride, .28f, running);
            stride *= strideScale;
            float lift = Mathf.Lerp(.065f, .11f, pace) + run * .045f;
            lift = Mathf.Lerp(lift, .185f, running);
            lift *= liftScale;
            float rightPhase = Mathf.Repeat(phase + .5f, 1);
            Vector2 leftFoot = Vector2.Lerp(FootPath(phase, contact, stride, lift),
                RunningFootPath(phase, contact, stride, lift), running);
            Vector2 rightFoot = Vector2.Lerp(FootPath(rightPhase, contact, stride, lift),
                RunningFootPath(rightPhase, contact, stride, lift), running);
            float wave = Mathf.Sin(phase * Mathf.PI * 2);
            float flight = FlightArc(phase, contact) * .018f * running * blend;

            if (hips)
            {
                // Fade out the lifted foot's influence continuously. Dropping a
                // foot from a boolean support set at toe-off would pop the hips.
                float supportReach = Mathf.Max(Mathf.Abs(leftFoot.x) - leftFoot.y * 1.5f,
                    Mathf.Abs(rightFoot.x) - rightFoot.y * 1.5f);
                supportReach = Mathf.Max(0, supportReach);
                float length = Mathf.Max(.25f, Mathf.Min(legL.upperLength + legL.lowerLength, legR.upperLength + legR.lowerLength));
                float compression = length - Mathf.Sqrt(Mathf.Max(.01f, length * length - supportReach * supportReach)) + .006f;
                float stance = Mathf.Repeat(phase, .5f) / contact;
                float absorption = stance < 1 ? Mathf.Sin(stance * Mathf.PI) : 0;
                compression += .025f * absorption * absorption * running;
                // Keep walking hips level with only a small lateral weight shift.
                // Foot-contact compression and the running cycle retain their existing motion.
                var offset = new Vector3(-wave * Mathf.Lerp(.002f, .009f, running) * swayScale,
                    -compression, -.008f * run + .012f * running) * blend;
                if (HasHumanoidRig) hips.position = transform.TransformPoint(pelvisInRoot + offset);
                else hips.localPosition = pelvis.position + offset;
                Rotate(pelvis, new Vector3((1.5f + run * 3 + running * 4) * blend,
                    wave * Mathf.Lerp(.6f * swayScale, 4.5f, running) * blend, wave * Mathf.Lerp(0f, 1.1f, running) * blend));
            }
            SolveLeg(legL, leftFoot, -1, blend);
            SolveLeg(legR, rightFoot, 1, blend);

            // Boots have no ankle bone. Use their real sole silhouette to correct
            // heel/toe penetration. Walking always has a support foot; running
            // alone allows a small flight arc between toe-off and the next landing.
            float clearance = Mathf.Min(SoleHeight(legL) - legL.floor, SoleHeight(legR) - legR.floor);
            if (hips && !float.IsInfinity(clearance))
                hips.position -= transform.up * (clearance - flight) * transform.lossyScale.y;

            float armSwing = Mathf.Lerp(13, 23, pace) + run * 9;
            armSwing = Mathf.Lerp(armSwing, 30, running);
            float armWave = Mathf.Cos(phase * Mathf.PI * 2 - .24f);
            armWave = Mathf.Lerp(armWave, Mathf.Cos((phase - contact * .5f) * Mathf.PI * 2 + Mathf.PI * .5f), running);
            Rotate(armL, new Vector3((armWave * armSwing - 7 * running) * blend, wave * 2 * blend, -1.5f * blend));
            Rotate(armR, new Vector3((-armWave * armSwing - 7 * running) * blend, wave * 2 * blend, 1.5f * blend));
            Rotate(forearmL, new Vector3(-Mathf.Lerp(10, 25, running) * blend, 0, 0));
            Rotate(forearmR, new Vector3(-Mathf.Lerp(10, 25, running) * blend, 0, 0));
            if (headRest.joint)
            {
                var angles = new Vector3((-.8f - 4 * running) * blend, -wave * 2.7f * blend, -wave * 1.3f * blend);
                if (HasHumanoidRig) Rotate(headRest, angles);
                else headRest.joint.localRotation = Quaternion.Euler(angles) * headRest.rotation;
            }
            CacheArmPose();
        }
    }
}
