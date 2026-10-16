using System;

/// <summary>In-game timestamp. Calendar follows README 1-1: 4 seasons x 20 days, 24h days.</summary>
[Serializable]
public struct GameTime : IEquatable<GameTime>, IComparable<GameTime>
{
    public const int MinutesPerHour = 60;
    public const int HoursPerDay = 24;
    public const int DayStartHour = 6;
    public const int DaysPerSeason = 20;
    public const int SeasonsPerYear = 4;
    public const int DaysPerYear = DaysPerSeason * SeasonsPerYear;

    private static readonly string[] SeasonNames = { "봄", "여름", "가을", "겨울" };

    public int absoluteDay;
    public int hour;
    public int minute;

    public GameTime(int absoluteDay, int hour, int minute = 0)
    {
        this.absoluteDay = Math.Max(0, absoluteDay);
        this.hour = hour;
        this.minute = minute;
    }

    public int Year => absoluteDay / DaysPerYear + 1;
    public int SeasonIndex => absoluteDay % DaysPerYear / DaysPerSeason;
    public string SeasonName => SeasonNames[SeasonIndex];
    public int DayOfSeason => absoluteDay % DaysPerSeason + 1;

    public long TotalMinutes =>
        (long)absoluteDay * HoursPerDay * MinutesPerHour + hour * MinutesPerHour + minute;

    public static GameTime FromTotalMinutes(long totalMinutes)
    {
        if (totalMinutes < 0) totalMinutes = 0;
        const long minutesPerDay = HoursPerDay * MinutesPerHour;
        int day = (int)(totalMinutes / minutesPerDay);
        int rest = (int)(totalMinutes % minutesPerDay);
        return new GameTime(day, rest / MinutesPerHour, rest % MinutesPerHour);
    }

    public GameTime AddMinutes(long minutes) => FromTotalMinutes(TotalMinutes + minutes);

    // The night the clock skips, hours [start, end) of every day (9장-6). Default 0-6: 23:59 is followed by 06:00.
    // The world's server sets it (12.9); start = end skips nothing.
    static int skipStart = 0, skipEnd = DayStartHour;
    public static int NightSkipStart => skipStart;
    public static int NightSkipEnd => skipEnd;

    /// <summary>
    /// Sets the skipped night hours [start, end). 0 ≤ start ≤ end ≤ 24 and not the whole day; a range over midnight is not
    /// allowed. False (nothing changes) for anything else.
    /// </summary>
    public static bool SetNightSkip(int start, int end)
    {
        if (start < 0 || start > end || end > HoursPerDay || end - start >= HoursPerDay) return false;
        skipStart = start; skipEnd = end;
        return true;
    }

    /// <summary>Back to the default night 00-06.</summary>
    public static void ResetNightSkip() { skipStart = 0; skipEnd = DayStartHour; }

    /// <summary>True when this time is inside the skipped night.</summary>
    public bool InNightSkip => skipStart < skipEnd && hour >= skipStart && hour < skipEnd;

    /// <summary>This time, or the end of the skipped night when it is inside it (a time that cannot be on the clock).</summary>
    public GameTime SkipNight() => InNightSkip ? FromTotalMinutes((long)absoluteDay * HoursPerDay * MinutesPerHour + skipEnd * MinutesPerHour) : this;

    /// <summary>One minute forward; a minute inside the skipped night jumps to its end (default: 23:59 → 06:00 of the next day).</summary>
    public GameTime NextMinute() => AddMinutes(1).SkipNight();

    public GameTime AddHours(int hours) => AddMinutes((long)hours * MinutesPerHour);

    public bool Equals(GameTime other) => TotalMinutes == other.TotalMinutes;

    public override bool Equals(object obj) => obj is GameTime other && Equals(other);

    public override int GetHashCode() => TotalMinutes.GetHashCode();

    public int CompareTo(GameTime other) => TotalMinutes.CompareTo(other.TotalMinutes);

    public static bool operator ==(GameTime a, GameTime b) => a.Equals(b);
    public static bool operator !=(GameTime a, GameTime b) => !a.Equals(b);
    public static bool operator <(GameTime a, GameTime b) => a.CompareTo(b) < 0;
    public static bool operator >(GameTime a, GameTime b) => a.CompareTo(b) > 0;
    public static bool operator <=(GameTime a, GameTime b) => a.CompareTo(b) <= 0;
    public static bool operator >=(GameTime a, GameTime b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Year}년 {SeasonName} {DayOfSeason}일차 {hour:00}:{minute:00}";
}

/// <summary>Read-only game clock. TimeManager can implement this to replace SimpleGameClock.</summary>
public interface IGameClock
{
    GameTime Now { get; }
    event Action<GameTime> HourChanged;
    event Action<GameTime> DayChanged;
    /// <summary>Jumps to a time received from the host. Raises no events.</summary>
    void SetNow(GameTime time);
}

/// <summary>Whichever clock is active registers itself here so systems need no scene references.</summary>
public static class GameClock
{
    public static IGameClock Current { get; set; }

    /// <summary>
    /// The running clock jumped over the skipped night (9장-6), once per night, with the time after it. Only a clock that
    /// advances itself raises it; a time received from a server (SetNow) does not (WorldStateSync handles that, 12.9).
    /// </summary>
    public static event Action<GameTime> NightSkipped;

    public static void RaiseNightSkipped(GameTime after) => NightSkipped?.Invoke(after);
}
