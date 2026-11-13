using UnityEngine;

namespace CompanyGame.Pets
{
    /// <summary>
    /// Drives the dog's Animator from plain gameplay values. The clips are in place, so whatever
    /// moves the pet (follow logic, network state) only reports its ground speed here.
    /// Clip data comes from ArtSource/Pets/Dog_20261110/build_dog.py.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetDogAnimator : MonoBehaviour
    {
        /// <summary>Ground speed (m/s) the Walk clip was authored for; feet do not slide at this speed.</summary>
        public const float WalkClipSpeed = .7f;
        /// <summary>Ground speed (m/s) the Run (gallop) clip was authored for.</summary>
        public const float RunClipSpeed = 3.5f;
        /// <summary>Seconds from the start of Attack to the jaw snapping shut (frame 12 at 30 fps).</summary>
        public const float AttackHitTime = .4f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MotionScaleId = Animator.StringToHash("MotionScale");
        static readonly int SitId = Animator.StringToHash("Sit");
        static readonly int AttackId = Animator.StringToHash("Attack");

        [SerializeField] Animator animator;
        [Tooltip("Seconds to smooth speed changes so the blend does not snap.")]
        [Min(0f)] public float speedSmoothing = .12f;

        float speed, speedVelocity;

        public Animator Animator => animator;
        public bool IsSitting => animator && animator.GetBool(SitId);

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>(true);
        }

        /// <summary>Report the pet's current ground speed in m/s. Call once per frame (it smooths with Time.deltaTime).</summary>
        public void SetMoveSpeed(float metresPerSecond)
        {
            if (!animator) return;
            float target = Mathf.Max(0f, metresPerSecond);
            speed = speedSmoothing > 0f ? Mathf.SmoothDamp(speed, target, ref speedVelocity, speedSmoothing) : target;
            animator.SetFloat(SpeedId, Mathf.Min(speed, RunClipSpeed));
            // Above the gallop's authored speed, play it faster instead of letting the feet slide.
            animator.SetFloat(MotionScaleId, speed > RunClipSpeed ? speed / RunClipSpeed : 1f);
        }

        /// <summary>Sit down (plays SitDown then loops Sit) or stand back up (plays StandUp).</summary>
        public void SetSitting(bool sitting)
        {
            if (animator) animator.SetBool(SitId, sitting);
        }

        /// <summary>Start a bite. Damage should be applied <see cref="AttackHitTime"/> seconds later.</summary>
        public void Attack()
        {
            if (animator) animator.SetTrigger(AttackId);
        }
    }
}
