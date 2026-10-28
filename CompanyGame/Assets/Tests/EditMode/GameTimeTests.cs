using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class GameTimeTests
{
    SimpleGameClock clock;
    IGameClock previous;

    [SetUp]
    public void SetUp()
    {
        previous = GameClock.Current;
        GameClock.Current = null;
        clock = new GameObject("TestClock").AddComponent<SimpleGameClock>();
        // EditMode does not run Awake (which would also read PlayerPrefs), so set the start time directly.
        typeof(SimpleGameClock).GetField("now", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(clock, new GameTime(0, GameTime.DayStartHour));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(clock.gameObject);
        GameClock.Current = previous;
        GameTime.ResetNightSkip();
    }

    [Test]
    public void NightSkip_ChangedRange_SkipsThoseHours()
    {
        Assert.IsTrue(GameTime.SetNightSkip(2, 5));
        Assert.AreEqual(new GameTime(3, 5, 0), new GameTime(3, 1, 59).NextMinute());
        Assert.AreEqual(new GameTime(4, 0, 0), new GameTime(3, 23, 59).NextMinute(), "midnight is no longer skipped");
        Assert.AreEqual(new GameTime(3, 5, 1), new GameTime(3, 5, 0).NextMinute());
        Assert.AreEqual(new GameTime(3, 5, 0), new GameTime(3, 3, 30).SkipNight());
    }

    [Test]
    public void NightSkip_StartEqualsEnd_SkipsNothing()
    {
        Assert.IsTrue(GameTime.SetNightSkip(4, 4));
        Assert.AreEqual(new GameTime(1, 0, 0), new GameTime(0, 23, 59).NextMinute());
        Assert.AreEqual(new GameTime(1, 4, 1), new GameTime(1, 4, 0).NextMinute());
        Assert.IsFalse(new GameTime(1, 4, 0).InNightSkip);
    }

    [Test]
    public void NightSkip_InvalidRanges_AreRefused()
    {
        Assert.IsFalse(GameTime.SetNightSkip(5, 2), "start after end (over midnight)");
        Assert.IsFalse(GameTime.SetNightSkip(0, 25), "past 24");
        Assert.IsFalse(GameTime.SetNightSkip(-1, 3), "negative");
        Assert.IsFalse(GameTime.SetNightSkip(0, 24), "the whole day");
        Assert.AreEqual(0, GameTime.NightSkipStart);
        Assert.AreEqual(6, GameTime.NightSkipEnd, "a refused range changes nothing");
        Assert.IsTrue(GameTime.SetNightSkip(22, 24));
        Assert.AreEqual(new GameTime(1, 0, 0), new GameTime(0, 21, 59).NextMinute(), "22-24 skips to the next day's 00:00");
    }

    [Test]
    public void NightSkipped_FiresOncePerNight()
    {
        var after = new List<GameTime>();
        System.Action<GameTime> listen = t => after.Add(t);
        GameClock.NightSkipped += listen;
        try
        {
            clock.AdvanceMinutes(18 * 60 * 2); // two whole 18-hour days from 06:00
            Assert.AreEqual(2, after.Count);
            Assert.AreEqual(new GameTime(1, 6, 0), after[0]);
            Assert.AreEqual(new GameTime(2, 6, 0), after[1]);
        }
        finally { GameClock.NightSkipped -= listen; }
    }

    [Test]
    public void NextMinute_JumpsFrom2359ToNextDay0600()
    {
        Assert.AreEqual(new GameTime(5, 6, 0), new GameTime(4, 23, 59).NextMinute());
        Assert.AreEqual(new GameTime(0, 22, 31), new GameTime(0, 22, 30).NextMinute());
        Assert.AreEqual(new GameTime(0, 6, 1), new GameTime(0, 6, 0).NextMinute());
    }

    [Test]
    public void DayOfEighteenHours_FiresHourChanged18Times_DayChangedOnce_NoNightHours()
    {
        Assert.AreEqual(new GameTime(0, 6, 0), clock.Now);
        var hours = new List<int>();
        int days = 0;
        clock.HourChanged += t => hours.Add(t.hour);
        clock.DayChanged += t => days++;

        clock.AdvanceMinutes(18 * 60);

        Assert.AreEqual(18, hours.Count);
        Assert.AreEqual(1, days);
        Assert.IsTrue(hours.TrueForAll(h => h >= 6), "hours 0-5 must be skipped");
        Assert.AreEqual(new GameTime(1, 6, 0), clock.Now);
    }

    [Test]
    public void Daylight_FollowsDuskCurve()
    {
        Assert.AreEqual(1f, DayNightManager.Daylight(6f));
        Assert.AreEqual(1f, DayNightManager.Daylight(12f));
        Assert.AreEqual(1f, DayNightManager.Daylight(18f));
        Assert.AreEqual(.5f, DayNightManager.Daylight(19f), .01f);
        Assert.AreEqual(0f, DayNightManager.Daylight(20f));
        Assert.AreEqual(0f, DayNightManager.Daylight(23f));
        Assert.AreEqual(1f, DayNightManager.SunsetTint(19f), .001f);
        Assert.AreEqual(0f, DayNightManager.SunsetTint(18f));
    }

    [Test]
    public void Calendar_RollsSeasonAndYear()
    {
        Assert.AreEqual("봄", new GameTime(19, 6).SeasonName);
        Assert.AreEqual("여름", new GameTime(20, 6).SeasonName);
        Assert.AreEqual(1, new GameTime(20, 6).DayOfSeason);
        Assert.AreEqual(1, new GameTime(79, 6).Year);
        Assert.AreEqual(2, new GameTime(80, 6).Year);
        Assert.AreEqual("봄", new GameTime(80, 6).SeasonName);
    }
}
