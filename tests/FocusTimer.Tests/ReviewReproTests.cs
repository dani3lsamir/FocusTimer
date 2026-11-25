using FocusTimer.Core;

namespace FocusTimer.Tests;

public class ReviewReproTests
{
    [Theory]
    [InlineData("999999999h")]
    [InlineData("99999999999:00")]
    [InlineData("99999999:0:0")]
    public void Huge_durations_do_not_throw(string text)
    {
        var ex = Record.Exception(() => DurationText.TryParse(text, out _));
        Assert.Null(ex);
    }

    [Fact]
    public void Edit_below_text_for_1h30_is_parseable()
    {
        var text = DurationText.Format(TimeSpan.FromMinutes(90)).Replace(" min", ""); // lo que hace "Edit below" hoy
        Assert.True(DurationText.TryParse(text, out _), $"'{text}' no se puede leer");
    }

    [Fact]
    public void Stray_quote_inside_unquoted_field_does_not_swallow_rows()
    {
        var rows = Csv.Parse(new StringReader("a,b\"c,d\r\ne,f\r\n")).ToList();
        Assert.Equal(2, rows.Count);
    }
}

public class ReviewFixTests
{
    [Theory]
    [InlineData("1h30m", 5400)]
    [InlineData("1 h 30 min", 5400)]
    [InlineData("1h30", 5400)]
    [InlineData("2h05m", 7500)]
    public void Parses_compound_durations(string text, int seconds)
    {
        Assert.True(DurationText.TryParse(text, out var d));
        Assert.Equal(TimeSpan.FromSeconds(seconds), d);
    }

    [Theory]
    [InlineData("1h75m")]
    [InlineData("1h-5")]
    [InlineData("h30")]
    [InlineData("1e9")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("24:00:01")]
    public void Rejects_invalid_compound_and_special_values(string text) =>
        Assert.False(DurationText.TryParse(text, out _));

    [Theory]
    [InlineData(1)]
    [InlineData(45)]
    [InlineData(390)]
    [InlineData(600)]
    [InlineData(3600)]
    [InlineData(3930)]
    [InlineData(5400)]
    [InlineData(86400)]
    public void ToInput_round_trips(int seconds)
    {
        var d = TimeSpan.FromSeconds(seconds);
        Assert.True(DurationText.TryParse(DurationText.ToInput(d), out var back));
        Assert.Equal(d, back);
    }

    [Fact]
    public void Stray_quote_is_kept_as_text()
    {
        var rows = Csv.Parse(new StringReader("a,b\"c,d\r\n")).ToList();
        Assert.Equal(["a", "b\"c", "d"], Assert.Single(rows));
    }

    private static ActiveCheckpoint Cp(string id) => new(id,
        new DateTime(2026, 11, 24, 9, 0, 0), new DateTime(2026, 11, 24, 9, 10, 0), 25, 10, "Work", "t");

    [Fact]
    public void Recover_with_log_goes_to_pending_when_main_is_write_locked()
    {
        // "Leer sí, escribir no" (como Excel) solo existe con los bloqueos de Windows.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Bloqueo de escritura compartido: solo Windows");
        using var dir = new TempDir();
        var paths = new DataPaths(dir.Path);
        var log = new SessionLog(paths);
        log.Main.Append(new SessionRecord("old", DateTime.Today, DateTime.Today, 25, 25, "Work", "", true));
        var cps = new CheckpointStore(paths.CheckpointJson);
        cps.Save(Cp("interrupted"));

        // Como Excel: otros pueden leer, nadie puede escribir.
        using (new FileStream(paths.SessionsCsv, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.NotNull(cps.Recover(log));
            Assert.False(File.Exists(cps.Path)); // el siguiente pomodoro ya no puede pisarlo
            Assert.True(log.HasPending);
        }
        Assert.Equal(["old", "interrupted"], log.ReadAll().Select(r => r.Id));
        log.FlushPending();
        Assert.Equal(["old", "interrupted"], log.Main.ReadAll().Select(r => r.Id));
    }

    [Fact]
    public void Recover_with_log_works_when_main_is_fully_locked()
    {
        using var dir = new TempDir();
        var paths = new DataPaths(dir.Path);
        var log = new SessionLog(paths);
        log.Main.Append(new SessionRecord("old", DateTime.Today, DateTime.Today, 25, 25, "Work", "", true));
        var cps = new CheckpointStore(paths.CheckpointJson);
        cps.Save(Cp("interrupted"));
        using (new FileStream(paths.SessionsCsv, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.NotNull(cps.Recover(log));
            Assert.False(File.Exists(cps.Path));
        }
        Assert.Equal(2, log.ReadAll().Count);
    }

    [Fact]
    public void Recover_with_log_does_not_duplicate_pending_row()
    {
        using var dir = new TempDir();
        var paths = new DataPaths(dir.Path);
        var log = new SessionLog(paths);
        log.Pending.Append(new SessionRecord("x", DateTime.Today, DateTime.Today, 25, 10, "Work", "", false));
        var cps = new CheckpointStore(paths.CheckpointJson);
        cps.Save(Cp("x"));
        Assert.Null(cps.Recover(log));
        Assert.Single(log.ReadAll());
    }

    [Fact]
    public void Seeded_count_keeps_long_break_cycle_across_restarts()
    {
        var c = new FakeClock(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero));
        var e = new PomodoroEngine(c);
        e.SeedCompletedCount(3); // 3 completados hoy antes de reabrir
        e.StartFocus("Work", "");
        c.AdvanceMin(25); e.Tick();
        Assert.Equal(Phase.LongBreak, e.Phase);
    }

    [Fact]
    public void Stats_resolver_matches_display_name_rules()
    {
        var s = new AppSettings();
        Categories.Rename(s, "work", "Job");
        Categories.Add(s, "Work", out _); // imposible: alias de Job
        var recs = new[]
        {
            new SessionRecord("1", new DateTime(2026, 11, 25, 9, 0, 0), new DateTime(2026, 11, 25, 9, 25, 0), 25, 25, " work ", "", true),
            new SessionRecord("2", new DateTime(2026, 11, 25, 10, 0, 0), new DateTime(2026, 11, 25, 10, 25, 0), 25, 10, "Gone", "", true),
            new SessionRecord("3", new DateTime(2026, 11, 25, 11, 0, 0), new DateTime(2026, 11, 25, 11, 25, 0), 25, 5, "", "", true),
        };
        var day = Stats.Day(recs, new DateOnly(2026, 11, 25), s);
        Assert.Equal(25, day.MinutesByCategory["Job"]);
        Assert.Equal(10, day.MinutesByCategory["Gone"]);
        Assert.Equal(5, day.MinutesByCategory["(no category)"]);
    }
}
