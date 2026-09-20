using System;
using UnityEngine;

/// <summary>Why a balance changed. The reason is useful to UI, logs and save data.</summary>
public enum MoneyChangeReason
{
    Unknown = 0,
    InitialBalance = 1,
    Earned = 2,
    Spent = 3,
    Reward = 4,
    Sale = 5,
    Purchase = 6,
    Tax = 7,
    PropertyUpgrade = 8,
    ManualAdjustment = 9
}

[Serializable]
public struct CurrencyBreakdown
{
    public long tenThousandWon;
    public long fiveThousandWon;
    public long oneThousandWon;
    public long fiveHundredWon;
    public long oneHundredWon;
    public long remainder;

    public long Total =>
        tenThousandWon * PropertyManager.TenThousandWon +
        fiveThousandWon * PropertyManager.FiveThousandWon +
        oneThousandWon * PropertyManager.OneThousandWon +
        fiveHundredWon * PropertyManager.FiveHundredWon +
        oneHundredWon * PropertyManager.OneHundredWon + remainder;

    public long GetCount(int denomination)
    {
        switch (denomination)
        {
            case PropertyManager.TenThousandWon: return tenThousandWon;
            case PropertyManager.FiveThousandWon: return fiveThousandWon;
            case PropertyManager.OneThousandWon: return oneThousandWon;
            case PropertyManager.FiveHundredWon: return fiveHundredWon;
            case PropertyManager.OneHundredWon: return oneHundredWon;
            default: return 0;
        }
    }

    public static CurrencyBreakdown FromAmount(long amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var result = new CurrencyBreakdown();
        long remaining = amount;
        result.tenThousandWon = remaining / PropertyManager.TenThousandWon;
        remaining %= PropertyManager.TenThousandWon;
        result.fiveThousandWon = remaining / PropertyManager.FiveThousandWon;
        remaining %= PropertyManager.FiveThousandWon;
        result.oneThousandWon = remaining / PropertyManager.OneThousandWon;
        remaining %= PropertyManager.OneThousandWon;
        result.fiveHundredWon = remaining / PropertyManager.FiveHundredWon;
        remaining %= PropertyManager.FiveHundredWon;
        result.oneHundredWon = remaining / PropertyManager.OneHundredWon;
        result.remainder = remaining % PropertyManager.OneHundredWon;
        return result;
    }
}

/// <summary>Owns the player's cash balance and applies every money change.</summary>
public class PropertyManager : MonoBehaviour
{
    public const int OneHundredWon = 100;
    public const int FiveHundredWon = 500;
    public const int OneThousandWon = 1000;
    public const int FiveThousandWon = 5000;
    public const int TenThousandWon = 10000;

    public static PropertyManager Instance { get; private set; }

    [SerializeField, Min(0)]
    private long startingMoney;

    private long currentMoney;

    /// <summary>Invoked as (new balance, signed delta, reason).</summary>
    public event Action<long, long, MoneyChangeReason> MoneyChanged;

    public long Money => currentMoney;

    /// <summary>Lowercase alias for existing economy/UI code conventions.</summary>
    public long money
    {
        get => currentMoney;
        set => SetMoney(value, MoneyChangeReason.ManualAdjustment);
    }

    public CurrencyBreakdown CurrentCurrency => CurrencyBreakdown.FromAmount(currentMoney);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        currentMoney = Math.Max(0L, startingMoney);
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool CanAfford(long amount) => amount > 0 && currentMoney >= amount;

    public bool AddMoney(long amount) => AddMoney(amount, MoneyChangeReason.Earned);

    public bool AddMoney(long amount, MoneyChangeReason reason)
    {
        if (amount <= 0 || currentMoney > long.MaxValue - amount) return false;
        ApplyBalance(currentMoney + amount, amount, reason);
        return true;
    }

    public bool TrySpend(long amount) => TrySpend(amount, MoneyChangeReason.Spent);

    public bool TrySpend(long amount, MoneyChangeReason reason)
    {
        if (amount <= 0 || currentMoney < amount) return false;
        ApplyBalance(currentMoney - amount, -amount, reason);
        return true;
    }

    public bool SetMoney(long amount, MoneyChangeReason reason = MoneyChangeReason.ManualAdjustment)
    {
        if (amount < 0 || amount == currentMoney) return amount == currentMoney;
        ApplyBalance(amount, amount - currentMoney, reason);
        return true;
    }

    public CurrencyBreakdown GetCurrencyBreakdown() => CurrentCurrency;

    private void ApplyBalance(long newBalance, long delta, MoneyChangeReason reason)
    {
        currentMoney = newBalance;
        MoneyChanged?.Invoke(currentMoney, delta, reason);
    }
}
