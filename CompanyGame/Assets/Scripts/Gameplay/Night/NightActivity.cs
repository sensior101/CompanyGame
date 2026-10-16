using System;
using UnityEngine;

/// <summary>What my character does through the skipped night (9장-7): 잠자기, 야간 알바, 놀러 가기, 살인 청부.</summary>
public enum NightActivityKind { Sleep, NightJob, GoOut, Contract }

/// <summary>
/// The night activity of my character on this PC (Docs/Multiplayer_Redesign.md 12.9, structure only). There is no screen to
/// pick one yet, so it is always <see cref="NightActivityKind.Sleep"/> (nothing happens, as before); it is not saved.
/// <see cref="Apply"/> runs once per skipped night: on GameClock.NightSkipped where this PC's clock runs, and from
/// WorldStateSync when a server's clock passed a night. A server process has no character and turns it off (<see cref="Enabled"/>).
/// The effects stay on this PC until 4d moves money and vitals to the server. The numbers are placeholders.
/// </summary>
public static class NightActivity
{
    public const float NightJobEnergy = -40f, GoOutEnergy = -30f, GoOutStress = -40f;
    public const long NightJobPay = 50000, GoOutCost = 30000;

    /// <summary>My pick for the coming nights (default 잠자기).</summary>
    public static NightActivityKind Selected { get; private set; } = NightActivityKind.Sleep;
    /// <summary>살인 청부 can be picked only after an event unlocks it (no such event yet).</summary>
    public static bool ContractUnlocked { get; set; }
    /// <summary>False where no character of mine lives here (a server process).</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>(energy delta, stress delta) on my character. Installed by the Player assembly (PlayerStats is not visible here).</summary>
    public static Action<float, float> ChangeVitals;
    /// <summary>Each night applied: the activity that really happened (GoOut without money becomes Sleep). For QA and later screens.</summary>
    public static event Action<NightActivityKind> Applied;

    public static bool IsAvailable(NightActivityKind kind) => kind != NightActivityKind.Contract || ContractUnlocked;

    /// <summary>Picks the activity; false (nothing changes) when it cannot be picked.</summary>
    public static bool Select(NightActivityKind kind)
    {
        if (!IsAvailable(kind) || !Enum.IsDefined(typeof(NightActivityKind), kind)) return false;
        Selected = kind;
        return true;
    }

    /// <summary>One night of <paramref name="kind"/> on my character. Returns what happened.</summary>
    public static NightActivityKind Apply(NightActivityKind kind)
    {
        if (!Enabled) return NightActivityKind.Sleep;
        var bank = BankManager.Instance;
        switch (kind)
        {
            case NightActivityKind.NightJob:
                ChangeVitals?.Invoke(NightJobEnergy, 0f);
                if (bank) bank.AddMoney(NightJobPay, MoneyChangeReason.Earned);
                break;
            case NightActivityKind.GoOut:
                if (!bank || !bank.TrySpend(GoOutCost, MoneyChangeReason.Spent)) { kind = NightActivityKind.Sleep; break; } // no money: sleeps instead
                ChangeVitals?.Invoke(GoOutEnergy, GoOutStress);
                break;
            case NightActivityKind.Contract:
                if (!ContractUnlocked) kind = NightActivityKind.Sleep; // its effect comes with the event that unlocks it
                break;
        }
        Applied?.Invoke(kind);
        return kind;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        Selected = NightActivityKind.Sleep; ContractUnlocked = false; Enabled = true; Applied = null;
        GameClock.NightSkipped -= OnNightSkipped;
        GameClock.NightSkipped += OnNightSkipped;
    }

    // Where this PC's clock runs itself (offline, the old room's host, an in-app server's owner).
    static void OnNightSkipped(GameTime after)
    {
        if (GameSession.IsAuthority) Apply(Selected);
    }
}
