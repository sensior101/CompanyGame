using System;
using UnityEngine;

/// <summary>
/// The Cloudflare server stops serving once its daily budget is used up: HTTP 503 {"error":"daily_budget"} (with Retry-After)
/// and WebSocket close code 4503. From then on this PC sends no more requests (login, autosave, reconnect) until the
/// next UTC day, which is 09:00 Korean time. The block is remembered across restarts (PlayerPrefs).
/// </summary>
public static class ServerBudget
{
    public const string Code = "daily_budget";
    public const string Message = "오늘 서버 사용 한도에 도달했어요. 내일 오전 9시 이후 다시 접속할 수 있어요. 오프라인으로 하거나 Steam 모드로 바꿀 수 있어요.";
    const string Key = "net.budget_until";

    /// <summary>Tests replace the clock.</summary>
    public static Func<DateTime> Now = () => DateTime.UtcNow;

    public static DateTime BlockedUntilUtc
    {
        get { long t = long.Parse(PlayerPrefs.GetString(Key, "0")); return t == 0 ? DateTime.MinValue : new DateTime(t, DateTimeKind.Utc); }
    }

    public static bool Blocked => Now() < BlockedUntilUtc;

    /// <summary>The server said "budget used up". retryAfterSeconds from the Retry-After header; 0 or less = until the next UTC midnight.</summary>
    public static void Trip(int retryAfterSeconds)
    {
        var now = Now();
        var until = retryAfterSeconds > 0 ? now.AddSeconds(retryAfterSeconds) : now.Date.AddDays(1);
        if (until <= BlockedUntilUtc) return;
        PlayerPrefs.SetString(Key, until.Ticks.ToString());
        PlayerPrefs.Save();
    }

    public static void Clear() { PlayerPrefs.DeleteKey(Key); }

    public static bool IsBudgetResponse(long status, string body) => status == 503 && body != null && body.Contains(Code);
}
