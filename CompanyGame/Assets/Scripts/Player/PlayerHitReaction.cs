using UnityEngine;

/// <summary>Per-character reaction clock and collision-limited displacement. PlayerMovement owns locomotion;
/// DaldongneAvatarMotion owns the pose. Neither global time nor the player component is disabled.</summary>
[DefaultExecutionOrder(-150)]
[DisallowMultipleComponent]
public sealed class PlayerHitReaction : MonoBehaviour, CompanyGame.Daldongne.DaldongneAvatarMotion.ICombatPoseSource
{
    [Header("Reaction timing")]
    [Min(.05f)] public float faceStun = .26f;
    [Min(.05f)] public float bodyStun = .32f;
    [Min(.05f)] public float heavyStun = .5f;
    [Min(.2f)] public float knockdownSeconds = .85f;
    [Min(.2f)] public float getUpSeconds = .85f;
    [Min(1)] public int heavyHitsToKnockdown = 2;
    [Min(.1f)] public float heavyChainWindow = 2f;
    [Header("Knockback (metres)")]
    [Range(.15f, .3f)] public float normalKnockback = .22f;
    [Range(.3f, 1.5f)] public float heavyKnockback = .65f;
    [Min(.05f)] public float knockbackSeconds = .18f;
    public LayerMask collisionLayers = ~0;

    public string PoseState { get; private set; } = "";
    public float NormalizedTime => Mathf.Lerp(initialPoseProgress, 1f, Mathf.Clamp01(elapsed / Mathf.Max(.01f, duration)));
    public Vector3 LocalHitDirection { get; private set; }
    public bool IsReacting => PoseState.Length > 0;
    public bool IsFrozen => Time.unscaledTime < frozenUntil;
    public float FrameDelta => IsFrozen ? 0f : Time.deltaTime;
    public bool BlocksActions => IsFrozen || IsReacting || (stats && stats.Health <= 0f);
    public bool BlocksMovement => BlocksActions;

    float elapsed, duration = 1f, frozenUntil, lastHeavy = -100f, knockElapsed;
    float initialPoseProgress;
    int heavyCount;
    Vector3 knockDirection;
    float knockDistance;
    PlayerStats stats;
    Damageable damageable;
    PlayerMovement movement;
    bool pendingDamage;
    float pendingAmount;
    GameObject pendingAttacker;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        damageable = GetComponent<Damageable>();
        movement = GetComponent<PlayerMovement>();
    }

    void OnEnable()
    {
        if (stats) stats.Damaged += OnDamaged;
        if (damageable) damageable.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (stats) stats.Damaged -= OnDamaged;
        if (damageable) damageable.Damaged -= OnDamaged;
        PoseState = ""; knockDistance = 0f; frozenUntil = 0f; pendingDamage = false;
    }

    void OnDamaged(float amount, GameObject attacker)
    {
        pendingDamage = true; pendingAmount = amount; pendingAttacker = attacker;
    }

    void LateUpdate()
    {
        // Other existing IDamageable callers still receive a basic reaction. Melee supplies exact contact data first.
        if (pendingDamage)
        {
            Vector3 direction = pendingAttacker ? transform.position - pendingAttacker.transform.position : -transform.forward;
            ReceiveHit(direction, transform.position + Vector3.up * 1.2f, pendingAmount >= 20f, pendingAmount, false);
        }
    }

    void Update()
    {
        float delta = FrameDelta;
        if (delta <= 0f) return;
        if (IsReacting)
        {
            elapsed += delta;
            if (elapsed >= duration)
            {
                if (PoseState == "Knockdown")
                {
                    bool defeated = (stats && stats.Health <= 0f) || (damageable && damageable.IsDefeated);
                    if (!defeated) SetPose("GetUp", getUpSeconds);
                }
                else PoseState = "";
            }
        }
        if (!movement)
        {
            Vector3 step = ConsumeKnockback(delta);
            if (step.sqrMagnitude > 0f) transform.position += SafeDisplacement(step);
        }
    }

    public void HitStop(float seconds) => frozenUntil = Mathf.Max(frozenUntil, Time.unscaledTime + Mathf.Clamp(seconds, 0f, .2f));

    public void SynchronizeHealth(float health)
    {
        if (health > 0f || PoseState == "Knockdown") return;
        SetPose("Knockdown", knockdownSeconds);
        LocalHitDirection = Vector3.forward;
        elapsed = duration;
        knockDistance = 0f;
        var combat = GetComponent<PlayerCombat>();
        if (combat) combat.CancelSwing();
    }

    /// <param name="direction">World-space direction in which the strike travels, from attacker to victim.</param>
    public void ReceiveHit(Vector3 direction, Vector3 point, bool heavy, float strength, bool forceKnockdown,
        bool authoritative = false, float authoritativeDuration = 0f)
    {
        if (strength <= 0f || !Finite(direction)) return;
        pendingDamage = false;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (direction.sqrMagnitude < .001f) direction = -transform.forward;
        LocalHitDirection = transform.InverseTransformDirection(-direction);
        bool defeated = (stats && stats.Health <= 0f) || (damageable && damageable.IsDefeated);
        if (heavy)
        {
            heavyCount = Time.time - lastHeavy <= heavyChainWindow ? heavyCount + 1 : 1;
            lastHeavy = Time.time;
        }
        bool down = forceKnockdown || defeated || (!authoritative && heavy && heavyCount >= heavyHitsToKnockdown);
        if (down) LocalHitDirection = transform.InverseTransformDirection(-ClearFallDirection(transform, direction, collisionLayers));
        if (down) { heavyCount = 0; SetPose("Knockdown", knockdownSeconds); }
        else if (heavy) SetPose("Hit_Heavy", heavyStun);
        else
        {
            bool face = point.y - transform.position.y >= 1.4f;
            SetPose(face ? "Hit_Face" : "Hit_Body", face ? faceStun : bodyStun);
        }
        if (authoritative && authoritativeDuration > 0f) duration = Mathf.Clamp(authoritativeDuration, .05f, 10f);
        var combat = GetComponent<PlayerCombat>();
        if (combat) combat.CancelSwing();
        knockDirection = direction;
        knockDistance = heavy ? heavyKnockback : normalKnockback;
        knockElapsed = 0f;
        HitStop(heavy ? .07f : .04f);
        var feedback = GetComponent<CombatFeedback>();
        if (!feedback) feedback = gameObject.AddComponent<CombatFeedback>();
        feedback.PlayImpact(heavy, true);
    }

    void SetPose(string state, float seconds)
    {
        PoseState = state; elapsed = 0f; duration = Mathf.Max(.05f, seconds);
        initialPoseProgress = state == "GetUp" ? 0f : state == "Knockdown" ? .04f : .13f;
    }

    /// <summary>Only the visual fall changes near a wall; root displacement keeps the authoritative impact direction.</summary>
    public static Vector3 ClearFallDirection(Transform root, Vector3 desired, int layers = ~0)
    {
        desired = Vector3.ProjectOnPlane(desired, Vector3.up).normalized;
        if (desired.sqrMagnitude < .001f) desired = -root.forward;
        Vector3 best = desired;
        float bestRoom = FallRoom(root, desired, layers);
        if (bestRoom >= .8f) return best;
        var side = Vector3.Cross(Vector3.up, desired);
        foreach (var direction in new[] { side, -side, -desired })
        {
            float room = FallRoom(root, direction, layers);
            if (room > bestRoom) { bestRoom = room; best = direction; }
            if (bestRoom >= .8f) break;
        }
        return best;
    }

    static float FallRoom(Transform root, Vector3 direction, int layers)
    {
        float room = .9f;
        foreach (var hit in Physics.SphereCastAll(root.position + Vector3.up * .55f, .22f, direction, .9f, layers, QueryTriggerInteraction.Ignore))
            if (hit.collider && hit.transform != root && !hit.transform.IsChildOf(root)) room = Mathf.Min(room, hit.distance);
        return room;
    }

    public void PresentCombat(CompanyGame.Daldongne.DaldongneAvatarMotion motion)
    {
        var combat = GetComponent<PlayerCombat>();
        motion.SetCombatPresentation(combat ? combat.ComboStep : 0, combat && combat.IsSwinging ? combat.SwingNormalizedTime : -1f,
            combat ? combat.ContactNormalized : .36f, PoseState, NormalizedTime, LocalHitDirection, FrameDelta);
    }

    public Vector3 ConsumeKnockback(float delta)
    {
        if (IsFrozen || knockDistance <= 0f || delta <= 0f) return Vector3.zero;
        float previous = Mathf.Clamp01(knockElapsed / knockbackSeconds);
        knockElapsed += delta;
        float current = Mathf.Clamp01(knockElapsed / knockbackSeconds);
        float achieved = (1f - previous) * (1f - previous) - (1f - current) * (1f - current);
        Vector3 step = knockDirection * (knockDistance * achieved);
        if (current >= 1f) knockDistance = 0f;
        return step;
    }

    public Vector3 SafeDisplacement(Vector3 step)
    {
        float distance = step.magnitude;
        if (distance < .00001f) return Vector3.zero;
        var controller = GetComponent<CharacterController>();
        float radius = controller ? controller.radius : .3f;
        float height = controller ? controller.height : 1.8f;
        Vector3 center = controller ? transform.TransformPoint(controller.center) : transform.position + Vector3.up * height * .5f;
        float offset = Mathf.Max(0f, height * .5f - radius);
        float permitted = distance;
        foreach (var hit in Physics.CapsuleCastAll(center + Vector3.up * offset, center - Vector3.up * offset,
                     Mathf.Max(.02f, radius - .02f), step / distance, distance + .025f, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider || hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            permitted = Mathf.Min(permitted, Mathf.Max(0f, hit.distance - .025f));
        }
        return step * (permitted / distance);
    }

    static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) ||
        float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
}
