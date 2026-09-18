using UnityEngine;

namespace CompanyGame.Daldongne
{
    // Rigid low-poly joint animation; no external rig or animation package required.
    public sealed class DaldongneAvatarMotion : MonoBehaviour
    {
        public Transform hips, leftArm, rightArm, leftLeg, rightLeg, leftKnee, rightKnee;
        Vector3 previousPosition;
        float phase, blend;
        void OnEnable() { previousPosition = transform.position; blend = 0; }
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            Vector3 delta = transform.position - previousPosition;
            previousPosition = transform.position;
            float speed = Vector3.ProjectOnPlane(delta, Vector3.up).magnitude / dt;
            if (speed > 8) speed = 0; // Reset/teleport is not a step.
            Pose(speed, dt);
        }
        public void Pose(float speed, float dt)
        {
            blend = Mathf.MoveTowards(blend, Mathf.Clamp01(speed / 2.4f), dt * 7);
            phase += dt * Mathf.Lerp(6, 11, Mathf.Clamp01(speed / 4.5f));
            float stride = Mathf.Sin(phase) * 30 * blend;
            if (leftLeg) leftLeg.localRotation = Quaternion.Euler(stride, 0, 0);
            if (rightLeg) rightLeg.localRotation = Quaternion.Euler(-stride, 0, 0);
            if (leftKnee) leftKnee.localRotation = Quaternion.Euler(-Mathf.Max(0, -Mathf.Sin(phase)) * 32 * blend, 0, 0);
            if (rightKnee) rightKnee.localRotation = Quaternion.Euler(-Mathf.Max(0, Mathf.Sin(phase)) * 32 * blend, 0, 0);
            if (leftArm) leftArm.localRotation = Quaternion.Euler(-stride * .75f - 6, 0, -5);
            if (rightArm) rightArm.localRotation = Quaternion.Euler(stride * .75f - 6, 0, 5);
            if (hips) hips.localPosition = new Vector3(0, .767f + Mathf.Abs(Mathf.Sin(phase)) * .022f * blend + Mathf.Sin(Time.time * 2) * .003f * (1 - blend), 0);
        }
    }
}
