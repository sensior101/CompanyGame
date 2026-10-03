using System;
using UnityEngine;

/// <summary>Stand-in game clock until TimeManager implements IGameClock. README 1-1: 5 real minutes = 1 game hour.</summary>
public class SimpleGameClock : MonoBehaviour, IGameClock
{
    [SerializeField, Min(1f)]
    private float realSecondsPerGameHour = 300f;

    [SerializeField, Min(0)]
    private int startDay;

    [SerializeField, Range(0, 23)]
    private int startHour = 8;

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
        GameClock.Current = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(GameClock.Current, this)) GameClock.Current = null;
    }

    private void Update()
    {
        if (paused) return;

        carrySeconds += Time.deltaTime;
        float secondsPerMinute = realSecondsPerGameHour / GameTime.MinutesPerHour;
        while (carrySeconds >= secondsPerMinute)
        {
            carrySeconds -= secondsPerMinute;
            AdvanceMinutes(1);
        }
    }

    public void AdvanceMinutes(int minutes)
    {
        for (int i = 0; i < minutes; i++)
        {
            GameTime before = now;
            now = now.AddMinutes(1);
            if (now.hour == before.hour) continue;

            HourChanged?.Invoke(now);
            if (now.absoluteDay != before.absoluteDay) DayChanged?.Invoke(now);
        }
    }

    [ContextMenu("Advance 1 hour")]
    private void AdvanceOneHour() => AdvanceMinutes(GameTime.MinutesPerHour);

    [ContextMenu("Advance 1 day")]
    private void AdvanceOneDay() => AdvanceMinutes(GameTime.HoursPerDay * GameTime.MinutesPerHour);
}
