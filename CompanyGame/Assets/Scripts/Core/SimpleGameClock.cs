using System;
using UnityEngine;

/// <summary>Stand-in game clock until TimeManager implements IGameClock. README 1-1: 5 real minutes = 1 game hour.</summary>
public class SimpleGameClock : MonoBehaviour, IGameClock
{
    [SerializeField, Min(1f)]
    private float realSecondsPerGameHour = 300f;

    [SerializeField, Min(0)]
    private int startDay;

    [SerializeField, Range(GameTime.DayStartHour, 23)]
    private int startHour = GameTime.DayStartHour;

    [SerializeField]
    private bool paused;

    private GameTime now;
    private float carrySeconds;

    public event Action<GameTime> HourChanged;
    public event Action<GameTime> DayChanged;

    public GameTime Now => now;

    public bool Paused
    {
        get => paused;
        set => paused = value;
    }

    private void Awake()
    {
        if (GameClock.Current != null && !ReferenceEquals(GameClock.Current, this))
        {
            enabled = false;
            Destroy(this);
            return;
        }

        now = new GameTime(startDay, startHour);
        if (long.TryParse(PlayerPrefs.GetString("GameClock.TotalMinutes", ""), out long saved)) now = GameTime.FromTotalMinutes(saved);
        // Saves from the old 24h rule can land in the skipped night; those hours do not exist, so start at its end (06:00).
        now = now.SkipNight();
        GameClock.Current = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(GameClock.Current, this)) GameClock.Current = null;
    }

    private void Update()
    {
        // Only single player and the host advance time; clients get it from the host.
        if (paused || !GameSession.IsAuthority) return;

        carrySeconds += Time.deltaTime;
        float secondsPerMinute = realSecondsPerGameHour / GameTime.MinutesPerHour;
        while (carrySeconds >= secondsPerMinute)
        {
            carrySeconds -= secondsPerMinute;
            AdvanceMinutes(1);
        }
    }

    public void SetNow(GameTime time)
    {
        now = time;
        carrySeconds = 0f;
    }

    public void AdvanceMinutes(int minutes)
    {
        for (int i = 0; i < minutes; i++)
        {
            GameTime before = now, plain = now.AddMinutes(1);
            now = plain.SkipNight();
            if (now != plain) GameClock.RaiseNightSkipped(now);
            if (now.hour == before.hour) continue;

            SaveTime();
            HourChanged?.Invoke(now);
            if (now.absoluteDay != before.absoluteDay) DayChanged?.Invoke(now);
        }
    }

    // Paused = driven from outside (a server's world clock, 12.9): that time is not this PC's to keep.
    void SaveTime()
    {
        if (GameSession.IsAuthority && !paused && ReferenceEquals(GameClock.Current, this))
            PlayerPrefs.SetString("GameClock.TotalMinutes", now.TotalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    void OnApplicationQuit() { SaveTime(); PlayerPrefs.Save(); }

    [ContextMenu("Advance 1 hour")]
    private void AdvanceOneHour() => AdvanceMinutes(GameTime.MinutesPerHour);

    [ContextMenu("Advance 1 day")]
    private void AdvanceOneDay() => AdvanceMinutes((GameTime.HoursPerDay - GameTime.DayStartHour) * GameTime.MinutesPerHour);
}
