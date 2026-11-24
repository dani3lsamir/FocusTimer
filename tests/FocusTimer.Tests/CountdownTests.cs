using FocusTimer.Core;

namespace FocusTimer.Tests;

public class CountdownTests
{
    private static (CountdownBoard b, FakeClock c, List<Countdown> done) Make()
    {
        var c = new FakeClock(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
        var b = new CountdownBoard(c);
        var done = new List<Countdown>();
        b.Finished += done.Add;
        return (b, c, done);
    }

    [Fact]
    public void Several_timers_run_at_once_and_finish_in_order()
    {
        var (b, c, done) = Make();
        b.Start("Huevo", TimeSpan.FromMinutes(10));
        b.Start("Pasta", TimeSpan.FromMinutes(3));
        b.Start(null, TimeSpan.FromMinutes(5));
        Assert.Equal("Pasta", b.NextDue()!.Label);
        c.AdvanceMin(3); b.Tick();
        Assert.Equal(["Pasta"], done.Select(d => d.Label));
        c.AdvanceMin(2); b.Tick();
        Assert.Equal("Timer 5 min", done[1].Label);
        Assert.Equal(TimeSpan.FromMinutes(5), b.Remaining(b.Items[0]));
        c.AdvanceMin(5); b.Tick();
        Assert.Equal(3, done.Count);
        Assert.All(b.Items, i => Assert.Equal(CountdownState.Finished, i.State));
        Assert.False(b.AnyActive);
        b.Tick();
        Assert.Equal(3, done.Count); // no se repite
    }

    [Fact]
    public void Pause_only_affects_that_timer()
    {
        var (b, c, done) = Make();
        var a = b.Start("A", TimeSpan.FromMinutes(5))!;
        var z = b.Start("Z", TimeSpan.FromMinutes(5))!;
        c.AdvanceMin(2);
        Assert.True(b.Pause(a.Id));
        Assert.False(b.Pause(a.Id));
        c.AdvanceMin(10); b.Tick();
        Assert.Equal(["Z"], done.Select(d => d.Label));
        Assert.Equal(TimeSpan.FromMinutes(3), b.Remaining(a));
        Assert.True(b.Resume(a.Id));
        c.AdvanceMin(3); b.Tick();
        Assert.Equal(2, done.Count);
        Assert.Equal(CountdownState.Finished, z.State);
    }

    [Fact]
    public void Remove_cancels_without_finishing()
    {
        var (b, c, done) = Make();
        var a = b.Start("A", TimeSpan.FromMinutes(1))!;
        Assert.True(b.Remove(a.Id));
        c.AdvanceMin(5); b.Tick();
        Assert.Empty(done);
        Assert.Empty(b.Items);
        Assert.False(b.Remove(a.Id));
    }

    [Fact]
    public void Sleep_through_deadline_finishes_on_next_tick()
    {
        var (b, c, done) = Make();
        b.Start("Huevo", TimeSpan.FromMinutes(10));
        c.AdvanceMin(120); // PC suspendido
        b.Tick();
        Assert.Single(done);
    }

    [Fact]
    public void Limits_number_and_duration()
    {
        var (b, _, _) = Make();
        Assert.Null(b.Start("x", TimeSpan.Zero));
        Assert.Null(b.Start("x", TimeSpan.FromHours(25)));
        for (int i = 0; i < CountdownBoard.MaxTimers; i++) Assert.NotNull(b.Start("t" + i, TimeSpan.FromMinutes(1)));
        Assert.Null(b.Start("extra", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Fraction_counts_down()
    {
        var (b, c, _) = Make();
        var a = b.Start("A", TimeSpan.FromMinutes(10))!;
        c.AdvanceMin(2.5);
        Assert.Equal(0.75, b.Fraction(a), 6);
    }

    [Theory]
    [InlineData("10", 600)]
    [InlineData(" 3 ", 180)]
    [InlineData("6:30", 390)]
    [InlineData("1:15:00", 4500)]
    [InlineData("6.5", 390)]
    [InlineData("6,5", 390)]
    [InlineData("90s", 90)]
    [InlineData("45m", 2700)]
    [InlineData("10 min", 600)]
    [InlineData("2h", 7200)]
    [InlineData("0:05", 5)]
    public void Parses_durations(string text, int seconds)
    {
        Assert.True(DurationText.TryParse(text, out var d));
        Assert.Equal(TimeSpan.FromSeconds(seconds), d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("5:75")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    [InlineData("25h")]
    [InlineData(":30")]
    public void Rejects_bad_durations(string text) => Assert.False(DurationText.TryParse(text, out _));

    [Theory]
    [InlineData(600, "10 min")]
    [InlineData(390, "6:30")]
    [InlineData(3600, "1 h")]
    [InlineData(5400, "1 h 30 min")]
    [InlineData(3930, "1:05:30")]
    public void Formats_durations(int seconds, string expected) =>
        Assert.Equal(expected, DurationText.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Clock_format()
    {
        Assert.Equal("9:58", DurationText.Clock(TimeSpan.FromSeconds(597.2)));
        Assert.Equal("1:02:03", DurationText.Clock(TimeSpan.FromSeconds(3723)));
        Assert.Equal("0:00", DurationText.Clock(TimeSpan.Zero));
    }

    [Fact]
    public void Presets_save_update_limit_and_round_trip()
    {
        using var dir = new TempDir();
        var s = new AppSettings();
        Assert.Empty(s.TimerPresets);
        Assert.Null(s.SaveTimerPreset("Huevo", TimeSpan.FromMinutes(10)));
        Assert.Null(s.SaveTimerPreset("huevo ", TimeSpan.FromSeconds(390))); // actualiza
        Assert.Single(s.TimerPresets);
        Assert.Equal(390, s.TimerPresets[0].Seconds);
        Assert.NotNull(s.SaveTimerPreset("  ", TimeSpan.FromMinutes(1)));
        Assert.NotNull(s.SaveTimerPreset("x", TimeSpan.Zero));
        Assert.Equal("Huevo · 6:30", s.TimerPresets[0].ToString());

        SettingsStore.Save(dir.File("settings.json"), s);
        var back = SettingsStore.Load(dir.File("settings.json"));
        Assert.Equal("Huevo", Assert.Single(back.TimerPresets).Name);

        for (int i = 0; i < 30; i++) s.SaveTimerPreset("p" + i, TimeSpan.FromMinutes(1));
        Assert.Equal(AppSettings.MaxTimerPresets, s.TimerPresets.Count);
    }
}
