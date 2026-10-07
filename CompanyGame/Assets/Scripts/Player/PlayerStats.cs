using System;
using UnityEngine;

public class PlayerStats : MonoBehaviour, IDamageable
{
    public const float Max = 100f;

    [Header("상태")]
    public float health = Max;
    [Range(0f, Max)] public float stamina = Max;
    public float stress = 0f;

    [Header("쓰러짐 기준 (체력·기력은 이하, 스트레스는 이상)")]
    [SerializeField] private float collapseHealth = 0f;
    [SerializeField] private float collapseEnergy = 0f;
    [SerializeField] private float collapseStress = Max;

    [Header("Movement stamina")]
    [Min(.1f)] public float walkingSecondsPerPoint = 8f;
    [Min(.1f)] public float sprintingSecondsPerPoint = 4f;
    public float PendingStaminaCost { get; private set; }

    public bool TryConsume(InventoryState inventory, int slot)
        => TryConsume(inventory, slot, out _);

    public bool TryConsume(InventoryState inventory, int slot, out string error)
    {
        error = null;
        if (!isActiveAndEnabled || health <= 0f || inventory == null)
        { error = "지금은 음식을 먹을 수 없습니다."; return false; }
        var stack = inventory.GetSlot(slot);
        if (stack == null || stack.IsEmpty || !stack.Item.IsConsumable)
        { error = "먹을 음식을 선택해 주세요."; return false; }
        var food = stack.Item;
        float nextHealth = Mathf.Clamp(health + food.healthRestore, 0f, 100f);
        float nextStamina = Mathf.Clamp(stamina + food.staminaRestore, 0f, 100f);
        float nextStress = Mathf.Clamp(stress - food.stressRelief, 0f, 100f);
        if (nextHealth == health && nextStamina == stamina && nextStress == stress)
        {
            error = food.healthRestore > 0f && food.staminaRestore == 0f && food.stressRelief == 0f
                ? "체력이 이미 가득 차 있습니다. 이 음식은 체력을 회복합니다."
                : food.staminaRestore > 0f && food.healthRestore == 0f && food.stressRelief == 0f
                ? "기력이 이미 가득 차 있습니다."
                : food.stressRelief > 0f && food.healthRestore == 0f && food.staminaRestore == 0f
                ? "스트레스가 없어 지금은 먹을 필요가 없습니다."
                : "이미 충분히 회복되어 지금은 먹을 필요가 없습니다.";
            return false;
        }
        if (!inventory.TryRemove(slot, 1, out error)) return false;
        health = nextHealth; stamina = nextStamina; stress = nextStress;
        Changed?.Invoke();
        return true;
    }

    public void RecordTravel(float distance, float speed, bool sprinting)
    {
        if (health <= 0f || stamina <= 0f || distance <= 0f || speed <= 0f ||
            float.IsNaN(distance) || float.IsInfinity(distance)) return;
        float secondsPerPoint = Mathf.Max(.1f, sprinting ? sprintingSecondsPerPoint : walkingSecondsPerPoint);
        PendingStaminaCost += distance / speed / secondsPerPoint;
        int points = Mathf.FloorToInt(PendingStaminaCost + .00001f);
        if (points == 0) return;
        PendingStaminaCost = Mathf.Max(0f, PendingStaminaCost - points);
        stamina = Mathf.Clamp(stamina - points, 0f, 100f);
        if (stamina <= 0f) PendingStaminaCost = 0f;
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
        health -= received;
        Damaged?.Invoke(received, attacker);
        return true;
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
