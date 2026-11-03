using System;
using UnityEngine;

public class PlayerStats : MonoBehaviour, IDamageable
{
    public const float Max = 100f;
    /// <summary>The HUD shows every vital as 20 points (health as 10 hearts in halves); one point is 5 of Max.</summary>
    public const float Points = 20f, Point = Max / Points;
    /// <summary>Sprinting needs more than 5 points of stamina.</summary>
    public const float SprintFloor = 5f * Point;
    /// <summary>든든함: a restaurant meal halves stamina use for 4 game hours.</summary>
    public const int WellFedMinutes = 4 * 60;
    public const float WellFedDrain = .5f;

    [Header("상태")]
    public float health = Max;
    [Range(0f, Max)] public float stamina = Max;
    public float stress = 0f;
    /// <summary>Server-owned (ServerCombat); the client only shows it and checks for room before eating.</summary>
    public float fullness = Max;
    /// <summary>Game minute (GameTime.TotalMinutes) the 든든함 ends; 0 = none.</summary>
    public long wellFedUntil;
    public bool IsWellFed => wellFedUntil > 0 && FoodShelfLife.Now.TotalMinutes < wellFedUntil;
    public const int FoodPoisonMinutes = 60;
    public long foodPoisonUntil;
    public bool IsFoodPoisoned => foodPoisonUntil > FoodShelfLife.Now.TotalMinutes;
    public void ApplyAuthoritativeFoodPoisoning(long until)
    {
        foodPoisonUntil = Math.Max(0, until);
        if (IsFoodPoisoned) { fullness = 0f; wellFedUntil = 0; }
        Changed?.Invoke();
    }
    void Update()
    {
        if (IsFoodPoisoned && fullness != 0f) { fullness = 0f; Changed?.Invoke(); }
    }
    public bool CanSprint => stamina > SprintFloor;

    [Header("쓰러짐 기준 (체력·기력은 이하, 스트레스는 이상)")]
    [SerializeField] private float collapseHealth = 0f;
    [SerializeField] private float collapseEnergy = 0f;
    [SerializeField] private float collapseStress = Max;

    [Header("Movement stamina")]
    // Real seconds per 1 of Max. A game hour is 300 s, so running 18 game hours (06-24) uses all 100: 5400 s / 100 = 54.
    [Min(.1f)] public float walkingSecondsPerPoint = 108f;
    [Min(.1f)] public float sprintingSecondsPerPoint = 54f;
    public float PendingStaminaCost { get; private set; }

    public bool TryConsume(InventoryState inventory, int slot)
        => TryConsume(inventory, slot, out _);

    public bool TryConsume(InventoryState inventory, int slot, out string error)
    {
        error = null;
        if (!isActiveAndEnabled || health <= 0f || inventory == null)
        { error = "지금은 음식을 먹을 수 없습니다."; return false; }
        var stack = inventory.GetSlot(slot);
        if (stack == null || !stack.CanConsume)
        { error = "먹을 음식을 선택해 주세요."; return false; }
        bool spoiled = stack.IsSpoiled;
        bool poisoned = spoiled || IsFoodPoisoned;
        long expires = stack.ExpiresAt;
        var food = stack.Item;
        // Anything that fills you up needs room: a full stomach refuses food, drinks without fullness still go down.
        if (!poisoned && food.fullnessRestore > 0f && fullness >= Max) { error = "배가 불러서 더 못 먹겠어요."; return false; }
        float nextFullness = poisoned ? 0f : Mathf.Clamp(fullness + food.fullnessRestore * Point, 0f, Max);
        float nextStamina = spoiled ? stamina : Mathf.Clamp(stamina + food.staminaRestore * Point, 0f, Max);
        float nextStress = spoiled ? stress : Mathf.Clamp(stress - food.stressRelief * Point, 0f, Max);
        if (!poisoned && nextFullness == fullness && nextStamina == stamina && nextStress == stress && !food.restaurantMeal)
        {
            error = food.staminaRestore > 0f && food.stressRelief == 0f ? "기력이 이미 가득 차 있습니다."
                : food.stressRelief > 0f && food.staminaRestore == 0f ? "스트레스가 없어 지금은 먹을 필요가 없습니다."
                : "이미 충분히 회복되어 지금은 먹을 필요가 없습니다.";
            return false;
        }
        if (!inventory.TryRemove(slot, 1, out error)) return false;
        InventoryLedgerRules.Send(inventory, "p.eat", new SharedOpArgs { a = food.itemId, b = "expiry", n = expires }); // the vitals stay this client's
        // Predicted here; the server's next health message carries the real fullness.
        fullness = nextFullness; stamina = nextStamina; stress = nextStress;
        if (spoiled) ApplyAuthoritativeFoodPoisoning(FoodShelfLife.Now.TotalMinutes + FoodPoisonMinutes);
        if (food.restaurantMeal && !poisoned) wellFedUntil = FoodShelfLife.Now.TotalMinutes + WellFedMinutes;
        Changed?.Invoke();
        return true;
    }

    public void RecordTravel(float distance, float speed, bool sprinting)
    {
        if (health <= 0f || stamina <= 0f || distance <= 0f || speed <= 0f ||
            float.IsNaN(distance) || float.IsInfinity(distance)) return;
        float secondsPerPoint = Mathf.Max(.1f, sprinting ? sprintingSecondsPerPoint : walkingSecondsPerPoint);
        PendingStaminaCost += distance / speed / secondsPerPoint * (IsWellFed ? WellFedDrain : 1f);
        int points = Mathf.FloorToInt(PendingStaminaCost + .00001f);
        if (points == 0) return;
        PendingStaminaCost = Mathf.Max(0f, PendingStaminaCost - points);
        bool wasCollapsed = IsCollapsed;
        stamina = Mathf.Clamp(stamina - points, 0f, 100f);
        if (stamina <= 0f) PendingStaminaCost = 0f;
        Changed?.Invoke();
        if (!wasCollapsed && IsCollapsed) Collapsed?.Invoke();
    }

    /// <summary>Server fullness from the health message (ServerCombat).</summary>
    public void ApplyAuthoritativeFullness(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        float next = IsFoodPoisoned ? 0f : Mathf.Clamp(value, 0f, Max);
        if (Mathf.Approximately(next, fullness)) return;
        fullness = next;
        Changed?.Invoke();
    }

    // Waking up (the clock jumping the night) fills stamina; the night activity's own cost applies on top.
    void OnEnable() => GameClock.NightSkipped += OnNightSkipped;
    void OnDisable() => GameClock.NightSkipped -= OnNightSkipped;
    void OnNightSkipped(GameTime after)
    {
        stamina = Max; PendingStaminaCost = 0f;
        Changed?.Invoke();
    }

    public void RestoreVitals(Vector3 values, float pendingStaminaCost)
    {
        health = Mathf.Clamp(values.x, 0f, 100f);
        stamina = Mathf.Clamp(values.y, 0f, 100f);
        stress = Mathf.Clamp(values.z, 0f, 100f);
        PendingStaminaCost = Mathf.Clamp(pendingStaminaCost, 0f, .99999f);
    }

    [Header("스탯")]
    public int intelligence = 0;
    public int charm = 0;
    public int physical = 0;
    public int farming = 0;
    public int cooking = 0;

    public event System.Action<float, GameObject> Damaged;

    public bool TakeDamage(float amount, GameObject attacker, Vector3 hitPoint)
    {
        if (!isActiveAndEnabled || health <= 0f || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return false;
        float received = Mathf.Min(health, amount);
        ApplyAuthoritativeHealth(health - received);
        var aggressor = attacker ? attacker.GetComponentInParent<PlayerStats>() : null;
        if (aggressor && aggressor != this && ReportManager.Instance)
        {
            var local = PlayerSpawner.Player;
            string actorName = local && aggressor.gameObject == local.gameObject ? GameSession.LocalPlayerName : aggressor.name;
            string targetName = local && gameObject == local.gameObject ? GameSession.LocalPlayerName : name;
            ReportManager.Instance.NotifyCrime(health <= 0f ? CrimeType.Murder : CrimeType.Assault, actorName, targetName);
        }
        Damaged?.Invoke(received, attacker);
        return true;
    }

    /// <summary>Reconciles server health through the existing HUD/collapse events without fabricating a second hit.</summary>
    public void ApplyAuthoritativeHealth(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        bool wasCollapsed = IsCollapsed;
        float next = Mathf.Clamp(value, 0f, Max);
        if (Mathf.Approximately(next, health)) return;
        health = next;
        Changed?.Invoke();
        if (!wasCollapsed && IsCollapsed) Collapsed?.Invoke();
    }

    public void ApplyAuthoritativeDamage(float value, float received, GameObject attacker = null)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || float.IsNaN(received) || float.IsInfinity(received) || received <= 0f) return;
        ApplyAuthoritativeHealth(value);
        Damaged?.Invoke(Mathf.Clamp(received, 0f, Max), attacker);
    }

    public float Health => health;
    public float Energy => stamina;
    public float Stress => stress;

    public bool IsCollapsed =>
        health <= collapseHealth || stamina <= collapseEnergy || stress >= collapseStress;

    /// <summary>Fires after Add() changes a state value (for HUD updates).</summary>
    public event Action Changed;

    /// <summary>Fires once when Add() pushes the player into the collapsed state. Hospital admission listens to this.</summary>
    public event Action Collapsed;

    public void Add(float healthDelta = 0f, float energyDelta = 0f, float stressDelta = 0f)
    {
        bool wasCollapsed = IsCollapsed;

        health = Mathf.Clamp(health + healthDelta, 0f, Max);
        stamina = Mathf.Clamp(stamina + energyDelta, 0f, Max);
        stress = Mathf.Clamp(stress + stressDelta, 0f, Max);

        Changed?.Invoke();
        if (!wasCollapsed && IsCollapsed)
            Collapsed?.Invoke();
    }
}
