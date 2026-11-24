using FocusTimer.Core;

namespace FocusTimer.Tests;

public class SettingsAndCategoriesTests
{
    [Fact]
    public void Defaults_are_generic()
    {
        var s = new AppSettings();
        s.Normalize();
        Assert.Equal(["Trabajo", "Estudio", "Personal"], s.Categories.Select(c => c.Name));
        Assert.Equal(25, s.FocusMinutes);
        Assert.Equal(5, s.ShortBreakMinutes);
        Assert.Equal(15, s.LongBreakMinutes);
        Assert.Equal(4, s.LongBreakEvery);
        Assert.Equal(8, s.DailyGoalPomodoros);
        Assert.Equal("trabajo", s.LastCategoryId);
    }

    [Fact]
    public void Save_load_round_trip()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var s = new AppSettings { FocusMinutes = 50, DailyGoalPomodoros = 6, WindowX = 10, WindowY = -20 };
        s.Categories[1].WeeklyGoalMinutes = 300;
        s.RememberTask("Tesis – capítulo 2");
        SettingsStore.Save(path, s);
        var back = SettingsStore.Load(path);
        Assert.Equal(50, back.FocusMinutes);
        Assert.Equal(6, back.DailyGoalPomodoros);
        Assert.Equal(300, back.Categories[1].WeeklyGoalMinutes);
        Assert.Equal(["Tesis – capítulo 2"], back.RecentTasks);
        Assert.Equal(-20, back.WindowY);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults_and_keep_a_copy()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, "{ esto no es json");
        var s = SettingsStore.Load(path);
        Assert.Equal(25, s.FocusMinutes);
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Normalize_clamps_bad_values()
    {
        var s = new AppSettings { FocusMinutes = 0, LongBreakEvery = -3, DailyGoalPomodoros = 999, Categories = [] };
        s.Normalize();
        Assert.Equal(1, s.FocusMinutes);
        Assert.Equal(1, s.LongBreakEvery);
        Assert.Equal(50, s.DailyGoalPomodoros);
        Assert.Equal(3, s.Categories.Count);
    }

    [Fact]
    public void Recent_tasks_keep_10_most_recent_without_duplicates()
    {
        var s = new AppSettings();
        for (int i = 0; i < 15; i++) s.RememberTask("t" + i);
        s.RememberTask("T3");
        Assert.Equal(10, s.RecentTasks.Count);
        Assert.Equal("T3", s.RecentTasks[0]);
        Assert.Single(s.RecentTasks, t => t.Equals("t3", StringComparison.OrdinalIgnoreCase));
        s.RememberTask("   ");
        Assert.Equal("", s.LastTask);
        Assert.Equal(10, s.RecentTasks.Count);
    }

    [Fact]
    public void Rename_keeps_old_rows_in_the_same_category()
    {
        var s = new AppSettings();
        Assert.Null(Categories.Rename(s, "trabajo", "Empleo"));
        Assert.Equal("Empleo", Categories.DisplayName(s, "Trabajo"));
        Assert.Equal("Empleo", Categories.DisplayName(s, "trabajo "));
        Assert.Null(Categories.Rename(s, "trabajo", "Oficina"));
        Assert.Equal("Oficina", Categories.DisplayName(s, "Trabajo"));
        Assert.Equal("Oficina", Categories.DisplayName(s, "Empleo"));
        // volver al nombre original
        Assert.Null(Categories.Rename(s, "trabajo", "Trabajo"));
        var cat = Categories.Find(s, "trabajo")!;
        Assert.Equal("Trabajo", cat.Name);
        Assert.DoesNotContain("Trabajo", cat.Aliases);
        Assert.Equal("Trabajo", Categories.DisplayName(s, "Oficina"));
    }

    [Fact]
    public void Rename_and_add_reject_duplicates_and_aliases()
    {
        var s = new AppSettings();
        Assert.NotNull(Categories.Rename(s, "trabajo", "estudio"));
        Assert.NotNull(Categories.Add(s, "PERSONAL", out _));
        Assert.NotNull(Categories.Add(s, "  ", out _));
        Categories.Rename(s, "trabajo", "Empleo");
        Assert.NotNull(Categories.Add(s, "Trabajo", out _)); // es alias de Empleo
        Assert.Null(Categories.Add(s, "Deporte", out var added));
        Assert.Equal("Deporte", added!.Name);
        Assert.Equal(4, s.Categories.Count);
    }

    [Fact]
    public void Unknown_category_keeps_its_raw_name()
    {
        var s = new AppSettings();
        Assert.Equal("Viejo", Categories.DisplayName(s, "Viejo"));
        Assert.Equal("(sin categoría)", Categories.DisplayName(s, ""));
    }

    [Fact]
    public void Move_reorders()
    {
        var s = new AppSettings();
        Assert.True(Categories.Move(s, "personal", -1));
        Assert.Equal(["Trabajo", "Personal", "Estudio"], s.Categories.Select(c => c.Name));
        Assert.False(Categories.Move(s, "trabajo", -1));
        Assert.False(Categories.Move(s, "estudio", +1));
    }
}
