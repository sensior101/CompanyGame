using UnityEngine;

public class PlayerStats : MonoBehaviour, IDamageable
{
    [Header("상태")]
    public float health = 100f;
    public float stress = 0f;

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
