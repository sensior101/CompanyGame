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
    ManualAdjustment = 9,
    Withdrawal = 10,
    Deposit = 11,
    BankDeposit = 12,
    BankWithdrawal = 13,
    BookPublicationFee = 14,
    BookRoyalty = 15,
    LibraryLateFee = 16
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
        tenThousandWon * BankManager.TenThousandWon +
        fiveThousandWon * BankManager.FiveThousandWon +
        oneThousandWon * BankManager.OneThousandWon +
        fiveHundredWon * BankManager.FiveHundredWon +
        oneHundredWon * BankManager.OneHundredWon + remainder;

    public long GetCount(int denomination)
    {
        switch (denomination)
        {
            case BankManager.TenThousandWon: return tenThousandWon;
            case BankManager.FiveThousandWon: return fiveThousandWon;
            case BankManager.OneThousandWon: return oneThousandWon;
            case BankManager.FiveHundredWon: return fiveHundredWon;
            case BankManager.OneHundredWon: return oneHundredWon;
            default: return 0;
        }
    }

    public static CurrencyBreakdown FromAmount(long amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var result = new CurrencyBreakdown();
        long remaining = amount;
        result.tenThousandWon = remaining / BankManager.TenThousandWon;
        remaining %= BankManager.TenThousandWon;
        result.fiveThousandWon = remaining / BankManager.FiveThousandWon;
        remaining %= BankManager.FiveThousandWon;
        result.oneThousandWon = remaining / BankManager.OneThousandWon;
        remaining %= BankManager.OneThousandWon;
        result.fiveHundredWon = remaining / BankManager.FiveHundredWon;
        remaining %= BankManager.FiveHundredWon;
        result.oneHundredWon = remaining / BankManager.OneHundredWon;
        result.remainder = remaining % BankManager.OneHundredWon;
        return result;
    }
}

/// <summary>Owns the player's bank balance. Carried coins and notes live in InventoryState.</summary>
[DefaultExecutionOrder(-600)]
public class BankManager : MonoBehaviour
{
    public const int OneHundredWon = 100;
    public const int FiveHundredWon = 500;
    public const int OneThousandWon = 1000;
    public const int FiveThousandWon = 5000;
    public const int TenThousandWon = 10000;

    public static BankManager Instance { get; private set; }

    [SerializeField, Min(0)]
    private long startingMoney = 100000L;

    private long currentMoney;

    /// <summary>Invoked as (new balance, signed delta, reason).</summary>
    public event Action<long, long, MoneyChangeReason> MoneyChanged;

    public long Money => currentMoney;
    public long BankBalance => currentMoney;

    /// <summary>Lowercase alias for existing economy/UI code conventions.</summary>
    public long money
    {
        get => currentMoney;
        set => SetMoney(value, MoneyChangeReason.ManualAdjustment);
    }

    public CurrencyBreakdown CurrentCurrency => CurrencyBreakdown.FromAmount(currentMoney);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    public static BankManager EnsureInstance()
    {
        if (!Instance && Application.isPlaying)
            new GameObject("Bank Session").AddComponent<BankManager>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // The legacy component can share its host with an entire map or systems root.
            Destroy(this);
            return;
        }
        Instance = this;
        currentMoney = Math.Max(0L, startingMoney);
        if (Application.isPlaying && (transform.parent || transform.childCount > 0 || GetComponents<Component>().Length > 2))
        {
            long initialBalance = currentMoney;
            Instance = null;
            BankManager host = EnsureInstance();
            host.currentMoney = initialBalance;
            Destroy(this);
            return;
        }
        if (Application.isPlaying) DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool CanAfford(long amount) => amount > 0 && currentMoney >= amount;

    public bool AddMoney(long amount) => AddMoney(amount, MoneyChangeReason.Earned);

    public bool AddMoney(long amount, MoneyChangeReason reason)
    {
        if (!GameSession.IsAuthority || amount <= 0 || currentMoney > long.MaxValue - amount) return false;
        ApplyBalance(currentMoney + amount, amount, reason);
        return true;
    }

    public bool TrySpend(long amount) => TrySpend(amount, MoneyChangeReason.Spent);

    public bool TrySpend(long amount, MoneyChangeReason reason)
    {
        if (!GameSession.IsAuthority || amount <= 0 || currentMoney < amount) return false;
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

    // Used by CashService to debit/credit and publish in one transaction.
    public bool TryDebitSilently(long amount)
    {
        if (!GameSession.IsAuthority || !CanAfford(amount)) return false;
        currentMoney -= amount;
        return true;
    }

    public void NotifyWithdrawal(long amount) { NotifyMoneyChanged(-amount, MoneyChangeReason.Withdrawal); }

    public bool TryCreditSilently(long amount)
    {
        if (!GameSession.IsAuthority || amount <= 0 || currentMoney > long.MaxValue - amount) return false;
        currentMoney += amount;
        return true;
    }

    public void NotifyDeposit(long amount) { NotifyMoneyChanged(amount, MoneyChangeReason.Deposit); }

    private void ApplyBalance(long newBalance, long delta, MoneyChangeReason reason)
    {
        currentMoney = newBalance;
        NotifyMoneyChanged(delta, reason);
    }

    void NotifyMoneyChanged(long delta, MoneyChangeReason reason)
    {
        if (MoneyChanged == null) return;
        foreach (Action<long, long, MoneyChangeReason> listener in MoneyChanged.GetInvocationList())
        {
            try { listener(currentMoney, delta, reason); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
