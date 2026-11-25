using FocusTimer.Core;

namespace FocusTimer.Tests;

public class NewFeatureTests
{
    [Fact]
    public void Restart_runs_a_finished_timer_again_with_same_name_and_duration()
    {
        var c = new FakeClock(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero));
        var b = new CountdownBoard(c);
        int finished = 0;
        b.Finished += _ => finished++;
        var egg = b.Start("Eggs", TimeSpan.FromMinutes(6))!;
        c.AdvanceMin(6); b.Tick();
        Assert.Equal(CountdownState.Finished, egg.State);

        Assert.True(b.Restart(egg.Id));
        Assert.Equal(CountdownState.Running, egg.State);
        Assert.Equal(TimeSpan.FromMinutes(6), b.Remaining(egg));
        Assert.Equal("Eggs", Assert.Single(b.Items).Label);
        c.AdvanceMin(6); b.Tick();
        Assert.Equal(2, finished);
        Assert.False(b.Restart(999));
    }

    [Fact]
    public void Restart_also_works_on_paused_timer()
    {
        var c = new FakeClock(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero));
        var b = new CountdownBoard(c);
        var t = b.Start("Tea", TimeSpan.FromMinutes(3))!;
        c.AdvanceMin(1); b.Pause(t.Id);
        b.Restart(t.Id);
        Assert.Equal(CountdownState.Running, t.State);
        Assert.Equal(TimeSpan.FromMinutes(3), b.Remaining(t));
    }

    [Fact]
    public void Remove_category_keeps_one_and_fixes_last_selected()
    {
        var s = new AppSettings { LastCategoryId = "study" };
        Assert.Null(Categories.Remove(s, "study"));
        Assert.Equal("work", s.LastCategoryId);
        Assert.Null(Categories.Remove(s, "personal"));
        Assert.NotNull(Categories.Remove(s, "work"));
        Assert.NotNull(Categories.Remove(s, "nope"));
        Assert.Equal("Work", Assert.Single(s.Categories).Name);
    }

    [Fact]
    public void Deleted_category_sessions_keep_their_name_in_stats()
    {
        var s = new AppSettings();
        Categories.Remove(s, "study");
        var r = new SessionRecord("1", new DateTime(2026, 11, 25, 9, 0, 0), new DateTime(2026, 11, 25, 9, 25, 0), 25, 25, "Study", "", true);
        var day = Stats.Day([r], new DateOnly(2026, 11, 25), s);
        Assert.Equal(25, day.MinutesByCategory["Study"]);
        Assert.Equal(1, day.CompletedPomodoros);
    }
}
