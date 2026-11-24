using FocusTimer.Core;

namespace FocusTimer.Tests;

public class StatsTests
{
    private static readonly AppSettings S = MakeSettings();

    private static AppSettings MakeSettings()
    {
        var s = new AppSettings();
        s.Normalize();
        s.Categories[0].WeeklyGoalMinutes = 100; // Trabajo
        return s;
    }

    private static SessionRecord R(DateTime start, double min = 25, string cat = "Trabajo", bool done = true) =>
        new(SessionRecord.NewId(), start, start.AddMinutes(min), 25, min, cat, "", done);

    private static List<SessionRecord> Pomodoros(DateOnly day, int n) =>
        Enumerable.Range(0, n).Select(i => R(day.ToDateTime(new TimeOnly(8, 0)).AddMinutes(30 * i))).ToList();

    [Fact]
    public void Session_crossing_midnight_counts_for_start_day()
    {
        var r = R(new DateTime(2026, 11, 24, 23, 50, 0));
        Assert.Equal(new DateTime(2026, 11, 25, 0, 15, 0), r.End);
        Assert.Equal(new DateOnly(2026, 11, 24), Stats.DayOf(r));
        Assert.Equal(1, Stats.Day([r], new DateOnly(2026, 11, 24), S).CompletedPomodoros);
        Assert.Equal(0, Stats.Day([r], new DateOnly(2026, 11, 25), S).CompletedPomodoros);
    }

    [Fact]
    public void Session_starting_just_after_midnight_counts_for_new_day()
    {
        var r = R(new DateTime(2026, 11, 25, 0, 0, 0));
        Assert.Equal(new DateOnly(2026, 11, 25), Stats.DayOf(r));
        var late = R(new DateTime(2026, 11, 24, 23, 59, 59));
        Assert.Equal(new DateOnly(2026, 11, 24), Stats.DayOf(late));
    }

    [Fact]
    public void Week_starts_on_monday()
    {
        Assert.Equal(new DateOnly(2026, 11, 23), Stats.WeekStart(new DateOnly(2026, 11, 24))); // martes
        Assert.Equal(new DateOnly(2026, 11, 23), Stats.WeekStart(new DateOnly(2026, 11, 23))); // lunes
        Assert.Equal(new DateOnly(2026, 11, 23), Stats.WeekStart(new DateOnly(2026, 11, 29))); // domingo
    }

    [Fact]
    public void Sunday_23_59_belongs_to_that_week_and_monday_00_00_to_next()
    {
        var sun = R(new DateTime(2026, 11, 29, 23, 59, 0), 30);
        var mon = R(new DateTime(2026, 11, 30, 0, 0, 0), 20);
        var w = Stats.Week([sun, mon], new DateOnly(2026, 11, 24), S);
        Assert.Equal(30, w.TotalMinutes);
        Assert.Equal(30, w.Days[6].FocusMinutes);
        var next = Stats.Week([sun, mon], new DateOnly(2026, 11, 30), S);
        Assert.Equal(20, next.TotalMinutes);
    }

    [Fact]
    public void Day_summary_counts_minutes_incl_incomplete_but_only_completed_pomodoros()
    {
        var d = new DateTime(2026, 11, 24, 9, 0, 0);
        var list = new[] { R(d), R(d.AddHours(1), 10, "Estudio", done: false), R(d.AddHours(2), 25, "Personal") };
        var day = Stats.Day(list, new DateOnly(2026, 11, 24), S);
        Assert.Equal(60, day.FocusMinutes);
        Assert.Equal(2, day.CompletedPomodoros);
        Assert.Equal(25, day.MinutesByCategory["Trabajo"]);
        Assert.Equal(10, day.MinutesByCategory["Estudio"]);
        Assert.Equal(["Trabajo", "Estudio", "Personal"], day.MinutesByCategory.Keys);
    }

    [Fact]
    public void Weekly_goal_per_category_uses_renamed_categories()
    {
        var s = MakeSettings();
        var mon = new DateTime(2026, 11, 23, 9, 0, 0);
        var list = new[] { R(mon, 40, "Trabajo"), R(mon.AddDays(2), 35, "Trabajo") };
        Categories.Rename(s, "trabajo", "Empleo");
        var w = Stats.Week(list, new DateOnly(2026, 11, 26), s);
        var g = Assert.Single(w.Goals);
        Assert.Equal("Empleo", g.Category);
        Assert.Equal(100, g.GoalMinutes);
        Assert.Equal(75, g.ActualMinutes);
        Assert.Equal(0.75, g.Fraction, 6);
        Assert.Equal(40, w.Days[0].MinutesByCategory["Empleo"]);
    }

    [Fact]
    public void Streak_counts_consecutive_days_meeting_goal()
    {
        var today = new DateOnly(2026, 11, 24);
        var list = new List<SessionRecord>();
        list.AddRange(Pomodoros(today.AddDays(-1), 8));
        list.AddRange(Pomodoros(today.AddDays(-2), 9));
        list.AddRange(Pomodoros(today.AddDays(-3), 7)); // no llega
        list.AddRange(Pomodoros(today.AddDays(-4), 8));
        Assert.Equal(2, Stats.Streak(list, today, 8)); // hoy aún no cuenta pero no rompe
        list.AddRange(Pomodoros(today, 8));
        Assert.Equal(3, Stats.Streak(list, today, 8));
    }

    [Fact]
    public void Streak_is_zero_when_yesterday_missed_and_today_not_met()
    {
        var today = new DateOnly(2026, 11, 24);
        var list = Pomodoros(today.AddDays(-2), 8);
        list.AddRange(Pomodoros(today, 3));
        Assert.Equal(0, Stats.Streak(list, today, 8));
    }

    [Fact]
    public void Streak_ignores_incomplete_sessions()
    {
        var today = new DateOnly(2026, 11, 24);
        var list = Pomodoros(today, 7);
        list.Add(R(today.ToDateTime(new TimeOnly(20, 0)), 5, done: false));
        Assert.Equal(0, Stats.Streak(list, today, 8));
        Assert.Equal(1, Stats.Streak(list, today, 7));
    }

    [Fact]
    public void Streak_bridges_midnight_by_start_day()
    {
        var today = new DateOnly(2026, 11, 25);
        // 1 pomodoro empezado ayer a las 23:50 cuenta para ayer
        var list = new List<SessionRecord> { R(new DateTime(2026, 11, 24, 23, 50, 0)) };
        Assert.Equal(1, Stats.Streak(list, today, 1));
    }
}
