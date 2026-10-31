using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using CompanyGame.World.Maps;

/// <summary>Left-click eats selected food, attacks with a weapon, or punches with an empty hand.</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public sealed class PlayerCombat : MonoBehaviour
{
    [Header("Bare hands")]
    [Min(.1f)] public float punchDamage = 10f;
    [Min(.1f)] public float punchRange = .52f;
    [Min(.01f)] public float punchRadius = .1f;
    [Min(.1f)] public float punchesPerSecond = 2f;
    [Header("Melee combo (jab, straight, hook)")]
    [Min(.05f)] public float comboWindow = .65f;
    [Range(.03f, .4f)] public float inputBufferSeconds = .18f;
    [Range(.15f, .7f)] public float contactNormalized = .36f;
    [Range(10f, 85f)] public float attackHalfAngle = 65f;
    [Range(.25f, 1f)] public float attackingMoveMultiplier = .8f;
    [Min(90f)] public float attackTurnSpeed = 720f;
    [Header("Hit detection")]
    public LayerMask hitLayers = ~0;
    public Transform attackOrigin;

    public event Action<bool> AttackPerformed;
    public event Action<string> ItemUseFeedback;
    public bool HasLastHit { get; private set; }
    public RaycastHit LastHit { get; private set; }
    public bool LastAttackWasShot { get; private set; }
    public bool LastAttackDealtDamage { get; private set; }
    public float LastAttackTime { get; private set; } = float.NegativeInfinity;
    public Vector3 LastAttackOrigin { get; private set; }
    public Vector3 LastAttackEnd { get; private set; }
    public ItemData SelectedWeapon
    {
        get
        {
            var state = Inventory;
            var stack = state?.GetSlot(state.SelectedHotbarIndex);
            return stack != null && !stack.IsEmpty && stack.Item.IsWeapon ? stack.Item : null;
        }
    }
    public int ComboStep { get; private set; } = -1;
    public uint SwingSequence { get; private set; }
    public float SwingDuration { get; private set; } = .5f;
    public float ContactNormalized { get; private set; } = .36f;
    public float SwingNormalizedTime => IsSwinging ? Mathf.Clamp01(swingElapsed / SwingDuration) : 1f;
    public bool IsSwinging { get; private set; }
    public Vector3 AttackDirection { get; private set; }
    public string PoseState => IsSwinging ? (ComboStep == 0 ? "Punch_Jab" : ComboStep == 1 ? "Punch_Straight" : "Punch_Hook") : "";
    public bool CanAttack => GameplayInputAllowed && !IsSwinging && Time.time >= nextAttackTime;
    bool GameplayInputAllowed => Application.isPlaying && isActiveAndEnabled && movement && movement.isActiveAndEnabled &&
        Time.timeScale > 0f && !SceneLoadManager.IsLoading && (!reaction || !reaction.BlocksActions) &&
        (!netEntity || netEntity.IsMine) &&
        !InputFocus.GameplayBlocked() && !UIEventSystem.IsEditingText() && !IsPointerOverUI();

    static InventoryState Inventory => InventoryManager.Instance ? InventoryManager.Instance.State : null;

    PlayerMovement movement;
    PlayerHitReaction reaction;
    CombatFeedback feedback;
    NetEntity netEntity;
    float nextAttackTime;
    float swingElapsed, comboExpires = float.NegativeInfinity, bufferedUntil;
    float swingRange, swingDamage;
    bool contactResolved, buffered;
    ItemData swingWeapon;
    readonly Dictionary<uint, int> pendingSwings = new Dictionary<uint, int>();
    readonly Queue<uint> recentSwings = new Queue<uint>();
    readonly List<RaycastResult> pointerHits = new List<RaycastResult>();

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        if (GetComponent<IDamageable>() == null) gameObject.AddComponent<PlayerStats>();
        reaction = GetComponent<PlayerHitReaction>();
        if (!reaction) reaction = gameObject.AddComponent<PlayerHitReaction>();
        feedback = GetComponent<CombatFeedback>();
        if (!feedback) feedback = gameObject.AddComponent<CombatFeedback>();
        netEntity = GetComponent<NetEntity>();
    }

    void Update()
    {
        TickSwing(reaction ? reaction.FrameDelta : Time.deltaTime);
        if (LeftPressed()) TryAttack();
    }

    void OnDisable() => CancelSwing();

    public void CancelSwing()
    {
        IsSwinging = buffered = false;
        ComboStep = -1;
        comboExpires = float.NegativeInfinity;
    }

    public static float DurationForStep(int step, float rate)
        => (step == 1 ? 1.08f : step == 2 ? 1.24f : 1f) / Mathf.Clamp(rate, .1f, 8f);
    public static float DamageForStep(int step, float damage)
        => damage * (step == 1 ? 1.15f : step == 2 ? 1.5f : 1f);
    public static float RangeForStep(int step, float range) => range * (step == 2 ? .84f : 1f);

    void TickSwing(float delta)
    {
        if (!IsSwinging) return;
        if (reaction && reaction.IsFrozen) return;
        if (!GameplayInputAllowed || SelectedWeapon != swingWeapon) { CancelSwing(); return; }
        swingElapsed += Mathf.Max(0f, delta);
        if (!contactResolved && SwingNormalizedTime >= ContactNormalized)
        {
            contactResolved = true;
            // Networked combat is resolved by ServerCombat at its accepted contact frame.
            if (!SyncClient.Instance || !SyncClient.Instance.Connected) ResolveHit(false, swingRange, swingDamage);
        }
        if (swingElapsed < SwingDuration) return;
        IsSwinging = false;
        comboExpires = Time.time + Mathf.Max(.05f, comboWindow);
        nextAttackTime = Time.time;
        bool continueCombo = buffered && Time.time <= bufferedUntil && ComboStep < 2;
        buffered = false;
        if (continueCombo) TryAttack();
    }

    /// <summary>Returns true for one permitted swing/shot, including misses; damage is applied only to the first obstruction.</summary>
    public bool TryAttack()
    {
        if (!GameplayInputAllowed) return false;
        if (IsSwinging)
        {
            // Exactly one late press is retained. Holding or early spam never restarts a strike.
            if (buffered || ComboStep >= 2 || SwingDuration - swingElapsed > inputBufferSeconds) return false;
            buffered = true;
            bufferedUntil = Time.time + inputBufferSeconds + .08f;
            return true;
        }
        if (Time.time < nextAttackTime) return false;
        if (EmployeeGate.TryHandleClick(movement.viewCamera ? movement.viewCamera : Camera.main, transform)) return true;
        var inventory = Inventory;
        var selected = inventory?.GetSlot(inventory.SelectedHotbarIndex);
        var vehicle = GetComponent<PlayerVehicle>();
        if (selected != null && !selected.IsEmpty && selected.Item.IsVehicle)
        {
            bool used = vehicle && vehicle.TryPlaceSelected();
            if (used) nextAttackTime = Time.time + .3f;
            return used;
        }
        if (vehicle && vehicle.IsRiding) return false;
        if (selected != null && !selected.IsEmpty && selected.Item.IsConsumable)
        {
            var stats = GetComponent<PlayerStats>();
            string foodName = selected.Item.DisplayName;
            string error = "플레이어 상태를 확인할 수 없습니다.";
            bool consumed = stats && stats.TryConsume(inventory, inventory.SelectedHotbarIndex, out error);
            if (consumed) nextAttackTime = Time.time + .25f;
            ItemUseFeedback?.Invoke(consumed ? foodName + " 1개를 먹었습니다." : error);
            return consumed;
        }
        ItemData weapon = SelectedWeapon;
        bool shot = weapon && weapon.weaponKind == WeaponKind.Firearm;
        float range = weapon ? Mathf.Max(.1f, weapon.weaponRange) : Mathf.Max(.1f, punchRange);
        // A melee definition cannot accidentally inherit a gun's long default range.
        if (weapon && !shot) range = Mathf.Min(range, 3f);
        float damage = weapon ? Mathf.Max(.1f, weapon.weaponDamage) : Mathf.Max(.1f, punchDamage);
        float rate = weapon ? Mathf.Max(.1f, weapon.attacksPerSecond) : Mathf.Max(.1f, punchesPerSecond);
        nextAttackTime = Time.time + 1f / rate;
        LastAttackTime = Time.time;
        LastAttackWasShot = shot;
        LastAttackDealtDamage = false;

        LastAttackOrigin = attackOrigin ? attackOrigin.position : transform.position + Vector3.up * 1.3f;
        AttackDirection = AimDirection(LastAttackOrigin, range);
        LastAttackEnd = LastAttackOrigin + AttackDirection * range;
        HasLastHit = false; LastHit = default;
        unchecked { SwingSequence++; if (SwingSequence == 0) SwingSequence++; }
        if (shot)
        {
            ComboStep = -1;
            if (!SyncClient.Instance || !SyncClient.Instance.Connected) ResolveHit(true, range, damage);
        }
        else
        {
            ComboStep = Time.time <= comboExpires && ComboStep >= 0 && ComboStep < 2 ? ComboStep + 1 : 0;
            SwingDuration = DurationForStep(ComboStep, rate);
            ContactNormalized = Mathf.Clamp(contactNormalized, .15f, .7f);
            swingRange = weapon ? range : RangeForStep(ComboStep, range);
            swingDamage = DamageForStep(ComboStep, damage); swingWeapon = weapon;
            swingElapsed = 0f; contactResolved = buffered = false; IsSwinging = true;
            if (feedback) feedback.PlaySwing();
        }
        pendingSwings[SwingSequence] = shot ? -1 : ComboStep;
        recentSwings.Enqueue(SwingSequence);
        while (recentSwings.Count > 16) pendingSwings.Remove(recentSwings.Dequeue());
        AttackPerformed?.Invoke(shot);
        return true;
    }

    void ResolveHit(bool shot, float range, float damage)
    {
        Vector3 origin = attackOrigin ? attackOrigin.position : transform.position + Vector3.up * 1.3f;
        LastAttackOrigin = origin;
        Ray ray = new Ray(origin, AttackDirection);
        RaycastHit[] hits = shot
            ? Physics.RaycastAll(ray, range, hitLayers, QueryTriggerInteraction.Ignore)
            : Physics.SphereCastAll(ray, Mathf.Max(.01f, punchRadius), range, hitLayers, QueryTriggerInteraction.Ignore);
        HasLastHit = FirstExternalHit(hits, out RaycastHit hit);
        LastHit = hit;
        LastAttackEnd = HasLastHit ? hit.point : origin + AttackDirection * range;
        if (HasLastHit)
        {
            Vector3 toHit = hit.point - origin;
            if (!shot && (toHit.magnitude > range + punchRadius ||
                (toHit.sqrMagnitude > .0001f && Vector3.Angle(AttackDirection, toHit) > attackHalfAngle))) return;
            foreach (var receiver in hit.collider.GetComponentsInParent<MonoBehaviour>())
            {
                if (!receiver.isActiveAndEnabled || !(receiver is IDamageable damageable)) continue;
                LastAttackDealtDamage = damageable.TakeDamage(damage, gameObject, hit.point);
                if (LastAttackDealtDamage && !shot)
                {
                    bool heavy = ComboStep == 2;
                    var targetReaction = receiver.GetComponent<PlayerHitReaction>();
                    if (!targetReaction) targetReaction = receiver.gameObject.AddComponent<PlayerHitReaction>();
                    Vector3 incoming = Vector3.ProjectOnPlane(receiver.transform.position - transform.position, Vector3.up);
                    targetReaction.ReceiveHit(incoming.sqrMagnitude > .0001f ? incoming.normalized : AttackDirection, hit.point, heavy, damage, false);
                    OnConfirmedImpact(heavy);
                }
                break;
            }
        }
    }

    void OnConfirmedImpact(bool heavy)
    {
        if (reaction) reaction.HitStop(heavy ? .07f : .04f);
        if (feedback) feedback.PlayImpact(heavy, false);
    }

    public void ReceiveServerSwing(uint sequence, int step, float duration, float contact)
    {
        if (sequence != SwingSequence || LastAttackWasShot) return;
        ComboStep = Mathf.Clamp(step, 0, 2);
        SwingDuration = Mathf.Clamp(duration, .125f, 15f);
        ContactNormalized = Mathf.Clamp01(contact);
    }

    public void ReceiveServerResult(uint sequence, bool hit, Vector3 point, float damage)
    {
        if (!pendingSwings.TryGetValue(sequence, out int step)) return;
        pendingSwings.Remove(sequence); // a delayed/duplicate result never repeats feedback.
        if (sequence == SwingSequence)
        {
            LastAttackDealtDamage = hit && damage > 0f;
            if (hit) { HasLastHit = true; LastAttackEnd = point; }
        }
        if (hit && damage > 0f && step >= 0) OnConfirmedImpact(step == 2);
    }

    public void ReceiveServerHealth(float authoritativeHealth)
    {
        var stats = GetComponent<PlayerStats>();
        if (stats) stats.ApplyAuthoritativeHealth(authoritativeHealth);
        if (reaction) reaction.SynchronizeHealth(authoritativeHealth);
    }

    public void ReceiveServerHit(float authoritativeHealth, Vector3 direction, float strength, bool heavy,
        int step = 0, bool knockedDown = false, float stun = 0f)
        => ReceiveServerHit(authoritativeHealth, direction, strength, heavy, step, knockedDown, stun,
            transform.position + Vector3.up * (step == 1 ? 1.2f : 1.55f));

    public void ReceiveServerHit(float authoritativeHealth, Vector3 direction, float strength, bool heavy,
        int step, bool knockedDown, float stun, Vector3 hitPoint)
    {
        var stats = GetComponent<PlayerStats>();
        if (stats) stats.ApplyAuthoritativeDamage(authoritativeHealth, strength);
        if (reaction) reaction.ReceiveHit(direction, hitPoint, heavy, strength, knockedDown, true,
            knockedDown ? Mathf.Max(.1f, stun - reaction.getUpSeconds) : stun);
    }

    Vector3 AimDirection(Vector3 origin, float range)
    {
        Camera camera = movement.viewCamera ? movement.viewCamera : Camera.main;
        if (!camera) return transform.forward;
        Ray aim = camera.ViewportPointToRay(new Vector3(.5f, .5f));
        float aimRange = range + Vector3.Distance(aim.origin, origin);
        Vector3 target = aim.GetPoint(aimRange);
        if (FirstExternalHit(Physics.RaycastAll(aim, aimRange, hitLayers, QueryTriggerInteraction.Ignore), out RaycastHit hit))
            target = hit.point;
        Vector3 delta = target - origin;
        // A third-person camera can be behind cover. Do not shoot backwards at it.
        return delta.sqrMagnitude > .0001f && Vector3.Dot(delta, aim.direction) > 0f ? delta.normalized : aim.direction;
    }

    bool FirstExternalHit(RaycastHit[] hits, out RaycastHit result)
    {
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (!hit.collider || hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            result = hit;
            return true;
        }
        result = default;
        return false;
    }

    bool IsPointerOverUI()
    {
        if (!EventSystem.current) return false;
        pointerHits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = PointerPosition() }, pointerHits);
        foreach (var hit in pointerHits)
            if (hit.module is UnityEngine.UI.GraphicRaycaster) return true;
        return false;
    }

    static Vector2 PointerPosition()
    {
        return GameInput.PointerPosition;
    }

    static bool LeftPressed()
    {
        return GameInput.AttackPressed;
    }
}
