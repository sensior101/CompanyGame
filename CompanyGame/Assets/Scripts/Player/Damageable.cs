using System;
using UnityEngine;

public interface IDamageable
{
    bool TakeDamage(float amount, GameObject attacker, Vector3 hitPoint);
}

/// <summary>Optional damage receiver for NPCs, players or destructible props. Defeat behavior belongs to subscribers.</summary>
[DisallowMultipleComponent]
public sealed class Damageable : MonoBehaviour, IDamageable
{
    [SerializeField, Min(1f)] float maxHealth = 100f;
    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDefeated => CurrentHealth <= 0f;
    public GameObject LastAttacker { get; private set; }
    public Vector3 LastHitPoint { get; private set; }
    public event Action<float, GameObject> Damaged;
    public event Action<GameObject> Defeated;

    void Awake() { ResetHealth(maxHealth); }

    public void ResetHealth(float maximumHealth = 100f)
    {
        maxHealth = Mathf.Max(1f, maximumHealth);
        CurrentHealth = maxHealth;
        LastAttacker = null;
    }

    public bool TakeDamage(float amount, GameObject attacker, Vector3 hitPoint)
    {
        if (!isActiveAndEnabled || IsDefeated || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return false;
        float received = Mathf.Min(CurrentHealth, amount);
        CurrentHealth -= received;
        LastAttacker = attacker;
        LastHitPoint = hitPoint;
        Damaged?.Invoke(received, attacker);
        if (IsDefeated) Defeated?.Invoke(attacker);
        return true;
    }
}
