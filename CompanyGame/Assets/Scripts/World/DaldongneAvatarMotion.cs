using System.Collections.Generic;
using UnityEngine;

namespace CompanyGame.Daldongne
{
    // Stance and swing trajectories drive this small rigid rig without an Animator.
    public sealed class DaldongneAvatarMotion : MonoBehaviour
    {
        public Transform hips, leftArm, rightArm, leftLeg, rightLeg, leftKnee, rightKnee;

        sealed class RestJoint
        {
            public Transform joint;
            public Vector3 position;
            public Quaternion rotation;
            public RestJoint(Transform value)
            {
                joint = value;
                if (!value) return;
                position = value.localPosition;
                rotation = value.localRotation;
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
            public float upperLength, lowerLength, floor;
            public Vector3[] sole;
        }

        RestJoint pelvis, armL, armR, head;
        Leg legL, legR;
        Vector3 previousPosition;
        float phase, blend, filteredSpeed;

        void OnEnable()
        {
            previousPosition = transform.position;
            ResetMotion();
        }
        void OnDisable() { ResetMotion(); }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            Vector3 delta = transform.position - previousPosition;
            previousPosition = transform.position;
            float speed = Vector3.ProjectOnPlane(delta, Vector3.up).magnitude / dt;
            // Reset and scene teleports must never become a very fast stride.
            if (speed > 8 || delta.sqrMagnitude > 1)
            {
                ResetMotion();
                return;
            }
            Pose(speed, dt);
        }

        void ResetMotion()
        {
            phase = blend = filteredSpeed = 0;
            Restore();
        }
        void Restore()
        {
            pelvis?.Restore(); armL?.Restore(); armR?.Restore(); head?.Restore();
            legL?.thigh.Restore(); legL?.knee.Restore();
            legR?.thigh.Restore(); legR?.knee.Restore();
        }
        bool SameRig() => pelvis != null && pelvis.joint == hips && armL.joint == leftArm
            && armR.joint == rightArm && legL.thigh.joint == leftLeg && legR.thigh.joint == rightLeg
            && legL.knee.joint == leftKnee && legR.knee.joint == rightKnee;

        void CaptureRestPose()
        {
            // The importer can replace the hierarchy on the same component.
            // Retain each avatar's own hip height and authored arm rotations.
            pelvis = new RestJoint(hips);
            armL = new RestJoint(leftArm); armR = new RestJoint(rightArm);
            head = new RestJoint(hips ? hips.Find("Head") : null);
            legL = CaptureLeg(leftLeg, leftKnee);
            legR = CaptureLeg(rightLeg, rightKnee);
            phase = blend = filteredSpeed = 0;
        }

        Leg CaptureLeg(Transform thigh, Transform knee)
        {
            var leg = new Leg { thigh = new RestJoint(thigh), knee = new RestJoint(knee) };
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
            var thigh = leg.thigh.joint;
            Vector3 target = leg.ankle + new Vector3(side * foot.y * .08f, foot.y, foot.x);
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
            float sideAngle = Mathf.Atan2(local.x, down) * Mathf.Rad2Deg;
            // Positive X at the knee folds the heel back, like a human knee.
            var thighPose = Quaternion.Euler(0, 0, sideAngle) * Quaternion.Euler(hipAngle, 0, 0);
            thigh.localRotation = leg.thigh.rotation * Quaternion.Slerp(Quaternion.identity, thighPose, weight);
            leg.knee.joint.localRotation = leg.knee.rotation * Quaternion.Euler(kneeAngle * Mathf.Rad2Deg * weight, 0, 0);
        }

        public void Pose(float speed, float dt)
        {
            if (!SameRig()) CaptureRestPose();
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed > 8)
            {
                ResetMotion();
                return;
            }
            dt = Mathf.Min(dt, .1f);
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
            float lift = Mathf.Lerp(.065f, .11f, pace) + run * .045f;
            lift = Mathf.Lerp(lift, .185f, running);
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
                hips.localPosition = pelvis.position + new Vector3(-wave * Mathf.Lerp(.017f, .009f, running),
                    -compression, -.008f * run + .012f * running) * blend;
                hips.localRotation = pelvis.rotation * Quaternion.Euler((1.5f + run * 3 + running * 4) * blend,
                    wave * Mathf.Lerp(3.5f, 4.5f, running) * blend, wave * Mathf.Lerp(1.8f, 1.1f, running) * blend);
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
            if (leftArm) leftArm.localRotation = armL.rotation * Quaternion.Euler((armWave * armSwing - 7 * running) * blend, wave * 2 * blend, -1.5f * blend);
            if (rightArm) rightArm.localRotation = armR.rotation * Quaternion.Euler((-armWave * armSwing - 7 * running) * blend, wave * 2 * blend, 1.5f * blend);
            if (head.joint)
                head.joint.localRotation = Quaternion.Euler((-.8f - 4 * running) * blend, -wave * 2.7f * blend, -wave * 1.3f * blend) * head.rotation;
        }
    }
}
