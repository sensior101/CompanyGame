using UnityEngine;

public class PlayerStats : MonoBehaviour, IDamageable
{
    [Header("상태")]
    public float health = 100f;
    [Range(0f, 100f)] public float stamina = 100f;
    public float stress = 0f;

    [Header("Movement stamina")]
    [Min(.1f)] public float walkingSecondsPerPoint = 8f;
    [Min(.1f)] public float sprintingSecondsPerPoint = 4f;
    public float PendingStaminaCost { get; private set; }

    public bool TryConsume(InventoryState inventory, int slot)
    {
        if (!isActiveAndEnabled || health <= 0f || inventory == null) return false;
        var stack = inventory.GetSlot(slot);
        if (stack == null || stack.IsEmpty || !stack.Item.IsConsumable) return false;
        var food = stack.Item;
        float nextHealth = Mathf.Clamp(health + food.healthRestore, 0f, 100f);
        float nextStamina = Mathf.Clamp(stamina + food.staminaRestore, 0f, 100f);
        float nextStress = Mathf.Clamp(stress - food.stressRelief, 0f, 100f);
        if (nextHealth == health && nextStamina == stamina && nextStress == stress) return false;
        if (!inventory.TryRemove(slot, 1, out _)) return false;
        health = nextHealth; stamina = nextStamina; stress = nextStress;
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
}
