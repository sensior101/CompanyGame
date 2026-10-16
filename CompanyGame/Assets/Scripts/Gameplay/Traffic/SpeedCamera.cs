using System;
using UnityEngine;

/// <summary>
/// Speed camera over a road stretch. It measures the player's horizontal speed from position changes (so it works
/// on foot, by bicycle or by car) and fines the bank account. A fine the account cannot cover becomes a penalty
/// point instead. Wallet cash is never touched.
/// </summary>
public sealed class SpeedCamera : MonoBehaviour
{
    [Min(10f)] public float limitKmh = 50f;
    [Min(0f)] public float toleranceKmh = 5f;
    [Min(0)] public long baseFine = 30000;
    [Min(0)] public long finePerKmh = 2000;
    [Min(0f)] public float cooldownSeconds = 10f;
    [Tooltip("The watched stretch. Trigger box, used only as a shape.")]
    public BoxCollider zone;
    [Tooltip("Optional light that blinks when someone is fined.")]
    public Light flash;

    /// <summary>The player to watch. World code (which Gameplay cannot see) provides it; see TrafficBridge.</summary>
    public static Func<Transform> PlayerProvider;

    /// <summary>(speed km/h, limit km/h, fine, paid from the bank). paid=false means a penalty point was given.</summary>
    public static event Action<float, float, long, bool> Fined;

    /// <summary>Replaces the bank payment (tests). Returns true when the fine was paid.</summary>
    public Func<long, bool> payFine;

    // A per-frame move faster than this is a teleport (map change, respawn), not driving.
    const float TeleportMps = 100f;
    /// <summary>
    /// The highest fine (the server's p.fine limit, 12.10.1): the default base and per-km/h fine at the fastest measurable
    /// speed (TeleportMps, 360 km/h) over the lowest limit (10 km/h). FineFor never goes over it.
    /// </summary>
    public const long MaxFine = 30000 + (360 - 10) * 2000;
    const float FlashSeconds = .15f;

    Vector3 last;
    bool hasLast;
    float speedMps, nextAllowed, flashUntil;

    public float SpeedKmh => speedMps * 3.6f;

    void Reset() => zone = GetComponent<BoxCollider>();

    void Update()
    {
        if (flash) flash.enabled = Time.time < flashUntil;
        Transform player = PlayerProvider?.Invoke();
        float dt = Time.deltaTime;
        if (!player || dt <= 0f) { hasLast = false; return; }
        Vector3 p = player.position;
        MeasureSpeed(p, dt);
        last = p;
        hasLast = true;
        if (Inside(p) && Process(SpeedKmh, Time.time, out _, out _)) flashUntil = Time.time + FlashSeconds;
    }

    void MeasureSpeed(Vector3 p, float dt)
    {
        if (!hasLast) { speedMps = 0f; return; }
        Vector3 step = p - last;
        step.y = 0f;
        float instant = step.magnitude / dt;
        if (instant > TeleportMps) { speedMps = 0f; return; }
        speedMps = Mathf.Lerp(speedMps, instant, 1f - Mathf.Exp(-10f * dt));
    }

    bool Inside(Vector3 world)
    {
        if (!zone) return false;
        Vector3 local = zone.transform.InverseTransformPoint(world) - zone.center;
        Vector3 half = zone.size * .5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    /// <summary>The fine for a speed above the limit; 0 when within limit + tolerance.</summary>
    public long FineFor(float kmh) =>
        kmh <= limitKmh + toleranceKmh ? 0 : Math.Min(MaxFine, baseFine + (long)Mathf.Round((kmh - limitKmh) * finePerKmh));

    /// <summary>Decides and applies one measurement. Returns true when someone was fined (cooldown starts).</summary>
    public bool Process(float kmh, float now, out long fine, out bool paid)
    {
        fine = FineFor(kmh);
        paid = false;
        if (fine <= 0 || now < nextAllowed) return false;
        nextAllowed = now + cooldownSeconds;
        paid = Pay(fine);
        if (!paid) GiveOnePenaltyPoint();
        Fined?.Invoke(kmh, limitKmh, fine, paid);
        return true;
    }

    bool Pay(long fine)
    {
        if (payFine != null) return payFine(fine);
        BankManager bank = BankManager.EnsureInstance();
        if (!bank || !bank.TrySpend(fine, MoneyChangeReason.Tax)) return false;
        SharedWriteGate.SendPersonal("p.fine", new SharedOpArgs { n = fine }); // the server takes it from its ledger too (12.10.1)
        return true;
    }

    // The bank could not cover the fine: record it on the library account's penalty points, like other violations.
    void GiveOnePenaltyPoint() => LibraryCatalog.GetAccount(GameSession.LocalPlayerId).penaltyPoints++;

    void OnDrawGizmos()
    {
        if (!zone) return;
        Gizmos.color = new Color(1f, .3f, .1f, .8f);
        Gizmos.matrix = zone.transform.localToWorldMatrix;
        Gizmos.DrawWireCube(zone.center, zone.size);
    }
}
