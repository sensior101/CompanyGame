using System;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    public const float Max = 100f;

    [Header("상태")]
    [SerializeField, Range(0f, Max)] private float health = Max;
    [SerializeField, Range(0f, Max)] private float energy = Max;
    [SerializeField, Range(0f, Max)] private float stress = 0f;

    [Header("쓰러짐 기준 (체력·기력은 이하, 스트레스는 이상)")]
    [SerializeField] private float collapseHealth = 0f;
    [SerializeField] private float collapseEnergy = 0f;
    [SerializeField] private float collapseStress = Max;

    [Header("스탯")]
    public int intelligence = 0;
    public int charm = 0;
    public int physical = 0;
    public int farming = 0;
    public int cooking = 0;

    public float Health => health;
    public float Energy => energy;
    public float Stress => stress;

    public bool IsCollapsed =>
        health <= collapseHealth || energy <= collapseEnergy || stress >= collapseStress;

    /// <summary>Fires after any state value changes (for HUD updates).</summary>
    public event Action Changed;

    /// <summary>Fires once when the player crosses into the collapsed state. Hospital admission listens to this.</summary>
    public event Action Collapsed;

    public void Add(float healthDelta = 0f, float energyDelta = 0f, float stressDelta = 0f)
    {
        bool wasCollapsed = IsCollapsed;

        health = Mathf.Clamp(health + healthDelta, 0f, Max);
        energy = Mathf.Clamp(energy + energyDelta, 0f, Max);
        stress = Mathf.Clamp(stress + stressDelta, 0f, Max);

        Changed?.Invoke();
        if (!wasCollapsed && IsCollapsed)
            Collapsed?.Invoke();
    }
}
