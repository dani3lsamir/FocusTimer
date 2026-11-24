using FocusTimer.Core;

namespace FocusTimer.Tests;

public class PomodoroEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 24, 9, 0, 0, TimeSpan.FromHours(-4));

    private static (PomodoroEngine e, FakeClock c, List<SessionRecord> log, List<PhaseEnded> ends) Make()
    {
        var c = new FakeClock(T0);
        var e = new PomodoroEngine(c);
        var log = new List<SessionRecord>();
        var ends = new List<PhaseEnded>();
        e.SessionFinished += log.Add;
        e.PhaseCompleted += ends.Add;
        return (e, c, log, ends);
    }

    [Fact]
    public void Starts_idle()
    {
        var (e, _, _, _) = Make();
        Assert.Equal(Phase.Idle, e.Phase);
        Assert.Equal(TimeSpan.Zero, e.Remaining);
    }

    [Fact]
    public void Focus_completes_into_short_break_and_logs_completed()
    {
        var (e, c, log, ends) = Make();
        e.StartFocus("Work", " informe ");
        Assert.Equal(Phase.Focus, e.Phase);
        Assert.Equal(TimeSpan.FromMinutes(25), e.Remaining);

        c.AdvanceMin(10);
        e.Tick();
        Assert.Equal(TimeSpan.FromMinutes(15), e.Remaining);
        Assert.Empty(log);

        c.AdvanceMin(15);
        e.Tick();
        Assert.Equal(Phase.ShortBreak, e.Phase);
        var r = Assert.Single(log);
        Assert.True(r.Completed);
        Assert.Equal(25, r.PlannedMin);
        Assert.Equal(25, r.ActualMin);
        Assert.Equal("informe", r.Task);
        Assert.Equal(T0.DateTime, r.Start);
        Assert.Equal(T0.AddMinutes(25).DateTime, r.End);
        Assert.Equal(new PhaseEnded(Phase.Focus, Phase.ShortBreak, TimeSpan.FromMinutes(5)), Assert.Single(ends));
    }

    [Fact]
    public void Break_completes_into_idle_without_logging()
    {
        var (e, c, log, ends) = Make();
        e.StartFocus("Work", "");
        c.AdvanceMin(25); e.Tick();
        c.AdvanceMin(5); e.Tick();
        Assert.Equal(Phase.Idle, e.Phase);
        Assert.Single(log);
        Assert.Equal(Phase.ShortBreak, ends[1].Ended);
        Assert.Equal(Phase.Idle, ends[1].Next);
    }

    [Fact]
    public void Every_fourth_focus_gets_long_break()
    {
        var (e, c, _, _) = Make();
        var breaks = new List<Phase>();
        for (int i = 0; i < 8; i++)
        {
            e.StartFocus("Work", "");
            c.AdvanceMin(25); e.Tick();
            breaks.Add(e.Phase);
            e.SkipBreak();
        }
        Assert.Equal(
            [Phase.ShortBreak, Phase.ShortBreak, Phase.ShortBreak, Phase.LongBreak,
             Phase.ShortBreak, Phase.ShortBreak, Phase.ShortBreak, Phase.LongBreak], breaks);
    }

    [Fact]
    public void Long_break_uses_configured_duration_and_interval()
    {
        var (e, c, _, _) = Make();
        e.Durations = new TimerDurations(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), 2);
        e.StartFocus("Study", ""); c.AdvanceMin(50); e.Tick();
        Assert.Equal(Phase.ShortBreak, e.Phase);
        Assert.Equal(TimeSpan.FromMinutes(10), e.Remaining);
        e.SkipBreak();
        e.StartFocus("Study", ""); c.AdvanceMin(50); e.Tick();
        Assert.Equal(Phase.LongBreak, e.Phase);
        Assert.Equal(TimeSpan.FromMinutes(30), e.Remaining);
    }

    [Fact]
    public void Pause_freezes_remaining_and_excludes_paused_time()
    {
        var (e, c, log, _) = Make();
        e.StartFocus("Work", "");
        c.AdvanceMin(10);
        Assert.True(e.Pause());
        Assert.True(e.IsPaused);
        c.AdvanceMin(60); // una hora en pausa
        e.Tick();
        Assert.Equal(Phase.Focus, e.Phase);
        Assert.Equal(TimeSpan.FromMinutes(15), e.Remaining);
        Assert.True(e.Resume());
        c.AdvanceMin(15); e.Tick();
        var r = Assert.Single(log);
        Assert.True(r.Completed);
        Assert.Equal(25, r.ActualMin);
        Assert.Equal(T0.AddMinutes(85).DateTime, r.End);
    }

    [Fact]
    public void Pause_and_resume_are_idempotent_and_invalid_when_idle()
    {
        var (e, _, _, _) = Make();
        Assert.False(e.Pause());
        Assert.False(e.Resume());
        e.StartFocus("Work", "");
        Assert.False(e.Resume());
        Assert.True(e.Pause());
        Assert.False(e.Pause());
    }

    [Fact]
    public void Stop_during_focus_logs_incomplete_with_actual_minutes()
    {
        var (e, c, log, ends) = Make();
        e.StartFocus("Work", "x");
        c.AdvanceMin(7.5);
        Assert.True(e.Stop());
        Assert.Equal(Phase.Idle, e.Phase);
        var r = Assert.Single(log);
        Assert.False(r.Completed);
        Assert.Equal(7.5, r.ActualMin);
        Assert.Equal(25, r.PlannedMin);
        Assert.Empty(ends); // detener no es "terminar"
        Assert.Equal(0, e.CompletedFocusCount);
    }

    [Fact]
    public void Stop_while_paused_counts_only_active_time()
    {
        var (e, c, log, _) = Make();
        e.StartFocus("Work", "");
        c.AdvanceMin(5); e.Pause();
        c.AdvanceMin(30);
        e.Stop();
        Assert.Equal(5, Assert.Single(log).ActualMin);
    }

    [Fact]
    public void Stop_during_break_does_not_log()
    {
        var (e, c, log, _) = Make();
        e.StartFocus("Work", ""); c.AdvanceMin(25); e.Tick();
        Assert.True(e.Stop());
        Assert.Equal(Phase.Idle, e.Phase);
        Assert.Single(log);
    }

    [Fact]
    public void Skip_only_works_during_break()
    {
        var (e, c, _, _) = Make();
        Assert.False(e.SkipBreak());
        e.StartFocus("Work", "");
        Assert.False(e.SkipBreak());
        c.AdvanceMin(25); e.Tick();
        Assert.True(e.SkipBreak());
        Assert.Equal(Phase.Idle, e.Phase);
    }

    [Fact]
    public void Start_during_break_ends_break_and_starts_focus()
    {
        var (e, c, _, _) = Make();
        e.StartFocus("Work", ""); c.AdvanceMin(25); e.Tick();
        Assert.True(e.StartFocus("Study", "b"));
        Assert.Equal(Phase.Focus, e.Phase);
        Assert.False(e.StartFocus("Study", "c")); // ya hay un enfoque
    }

    [Fact]
    public void Sleep_through_focus_and_break_closes_both_at_their_real_deadlines()
    {
        var (e, c, log, ends) = Make();
        e.StartFocus("Work", "");
        c.AdvanceMin(3 * 60); // el PC estuvo suspendido 3 horas
        e.Tick();
        Assert.Equal(Phase.Idle, e.Phase);
        var r = Assert.Single(log);
        Assert.True(r.Completed);
        Assert.Equal(T0.AddMinutes(25).DateTime, r.End); // no 3 horas después
        Assert.Equal(2, ends.Count);
    }

    [Fact]
    public void Remaining_follows_wall_clock_not_ticks()
    {
        var (e, c, _, _) = Make();
        e.StartFocus("Work", "");
        c.AdvanceMin(12); // sin llamar a Tick
        Assert.Equal(TimeSpan.FromMinutes(13), e.Remaining);
        Assert.Equal(13.0 / 25, e.RemainingFraction, 6);
    }

    [Fact]
    public void Duration_change_applies_from_next_phase()
    {
        var (e, c, log, _) = Make();
        e.StartFocus("Work", "");
        e.Durations = e.Durations with { Focus = TimeSpan.FromMinutes(50) };
        Assert.Equal(TimeSpan.FromMinutes(25), e.Remaining);
        c.AdvanceMin(25); e.Tick();
        Assert.Equal(25, log[0].PlannedMin);
    }

    [Fact]
    public void Snapshot_reports_active_focus_only()
    {
        var (e, c, _, _) = Make();
        Assert.Null(e.Snapshot());
        e.StartFocus("Work", "t");
        c.AdvanceMin(4);
        var s = e.Snapshot()!;
        Assert.Equal(4, s.ActualMin);
        Assert.Equal("t", s.Task);
        c.AdvanceMin(21); e.Tick();
        Assert.Null(e.Snapshot());
    }
}
