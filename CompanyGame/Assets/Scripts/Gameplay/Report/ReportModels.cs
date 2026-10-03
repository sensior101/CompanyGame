using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Crime types and default penalties follow README 8-2.</summary>
public enum CrimeType
{
    Theft,
    Trespass,
    Assault,
    Murder
}

public enum ReportStatus
{
    Pending,
    Convicted,
    Dismissed
}

public enum ReportSubmitResult
{
    Submitted,
    InvalidSuspect,
    MissingEvidence,
    UnknownCrimeType,
    OnCooldown
}

[Serializable]
public class CrimeRule
{
    public CrimeType type;
    public string displayName;
    [Min(0)] public int penaltyPoints;
    [Min(0f)] public float fineMultiplierOfStolenValue;
    [Min(0)] public long fixedFine;
    [Min(0)] public int jailHours;
    public bool allowSettlement;
    [Min(0)] public int creditPenalty;
}

[Serializable]
public class ReportVerdict
{
    public bool guilty;
    public int penaltyPoints;
    public long fine;
    public int jailHours;
    public bool settlementAllowed;
    public int creditDelta;
    public string reason;
}

[Serializable]
public class CrimeReport
{
    public string id;
    public string reporterId;
    public string suspectId;
    public CrimeType type;
    public string evidenceId;
    public long stolenValue;
    public GameTime submittedAt;
    public GameTime resolvedAt;
    public ReportStatus status;
    public ReportVerdict verdict;
}

[Serializable]
public class ReportSettings
{
    public string localPlayerId = "player_local";
    [Min(0)] public int reportCooldownGameMinutes = 60;
    [Min(1)] public int maxStoredReports = 200;

    [Header("Penalty point thresholds (README 8-2)")]
    [Min(1)] public int businessBlockPenaltyPoints = 50;
    [Min(1)] public int assetSeizurePenaltyPoints = 100;

    public List<CrimeRule> rules = CreateDefaultRules();

    public static List<CrimeRule> CreateDefaultRules()
    {
        return new List<CrimeRule>
        {
            new CrimeRule { type = CrimeType.Theft, displayName = "절도", penaltyPoints = 5,
                fineMultiplierOfStolenValue = 2f, creditPenalty = 5 },
            new CrimeRule { type = CrimeType.Trespass, displayName = "주거침입", penaltyPoints = 5,
                creditPenalty = 3 },
            new CrimeRule { type = CrimeType.Assault, displayName = "폭행", penaltyPoints = 5,
                jailHours = 2, allowSettlement = true, creditPenalty = 5 },
            new CrimeRule { type = CrimeType.Murder, displayName = "살인", penaltyPoints = 30,
                jailHours = 24, allowSettlement = true, creditPenalty = 20 }
        };
    }
}
