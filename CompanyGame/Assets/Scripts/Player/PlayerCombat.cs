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
    [Min(.1f)] public float punchRange = 1.65f;
    [Min(.01f)] public float punchRadius = .24f;
    [Min(.1f)] public float punchesPerSecond = 2f;
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
    public bool CanAttack => Application.isPlaying && isActiveAndEnabled && movement && movement.isActiveAndEnabled &&
        Time.timeScale > 0f && Time.time >= nextAttackTime && !SceneLoadManager.IsLoading &&
        !InputFocus.GameplayBlocked() && !UIEventSystem.IsEditingText() && !IsPointerOverUI();

    static InventoryState Inventory => InventoryManager.Instance ? InventoryManager.Instance.State : null;

    PlayerMovement movement;
    float nextAttackTime;
    readonly List<RaycastResult> pointerHits = new List<RaycastResult>();

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        if (GetComponent<IDamageable>() == null) gameObject.AddComponent<PlayerStats>();
    }

    void Update()
    {
        if (LeftPressed()) TryAttack();
    }

    /// <summary>Returns true for one permitted swing/shot, including misses; damage is applied only to the first obstruction.</summary>
    public bool TryAttack()
    {
        if (!CanAttack) return false;
        if (EmployeeGate.TryHandleClick(movement.viewCamera ? movement.viewCamera : Camera.main, transform)) return true;
        var inventory = Inventory;
        var selected = inventory?.GetSlot(inventory.SelectedHotbarIndex);
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

        Vector3 origin = attackOrigin ? attackOrigin.position : transform.position + Vector3.up * 1.3f;
        Vector3 direction = AimDirection(origin, range);
        LastAttackOrigin = origin;
        Ray ray = new Ray(origin, direction);
        RaycastHit[] hits = shot
            ? Physics.RaycastAll(ray, range, hitLayers, QueryTriggerInteraction.Ignore)
            : Physics.SphereCastAll(ray, Mathf.Max(.01f, punchRadius), range, hitLayers, QueryTriggerInteraction.Ignore);
        HasLastHit = FirstExternalHit(hits, out RaycastHit hit);
        LastHit = hit;
        LastAttackEnd = HasLastHit ? hit.point : origin + direction * range;
        if (HasLastHit)
        {
            foreach (var receiver in hit.collider.GetComponentsInParent<MonoBehaviour>())
            {
                if (!receiver.isActiveAndEnabled || !(receiver is IDamageable damageable)) continue;
                LastAttackDealtDamage = damageable.TakeDamage(damage, gameObject, hit.point);
                break;
            }
        }
        AttackPerformed?.Invoke(shot);
        return true;
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
