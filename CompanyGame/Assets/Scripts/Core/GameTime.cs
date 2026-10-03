using System;

/// <summary>In-game timestamp. Calendar follows README 1-1: 4 seasons x 20 days, 24h days.</summary>
[Serializable]
public struct GameTime : IEquatable<GameTime>, IComparable<GameTime>
{
    public const int MinutesPerHour = 60;
    public const int HoursPerDay = 24;
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
}

/// <summary>Whichever clock is active registers itself here so systems need no scene references.</summary>
public static class GameClock
{
    public static IGameClock Current { get; set; }
}
