namespace FocusTimer.Core;

public sealed class DaySummary(DateOnly date)
{
    public DateOnly Date { get; } = date;
    public double FocusMinutes { get; set; }
    public int CompletedPomodoros { get; set; }
    /// <summary>Minutos por categoría (nombre actual), en el orden de los ajustes.</summary>
    public Dictionary<string, double> MinutesByCategory { get; } = [];
}

public sealed record CategoryGoal(string Category, int GoalMinutes, double ActualMinutes)
{
    public double Fraction => GoalMinutes <= 0 ? 0 : ActualMinutes / GoalMinutes;
}

public sealed class WeekSummary(DateOnly monday, DaySummary[] days, IReadOnlyList<CategoryGoal> goals)
{
    public DateOnly Monday { get; } = monday;
    public DaySummary[] Days { get; } = days;
    public IReadOnlyList<CategoryGoal> Goals { get; } = goals;
    public double TotalMinutes => Days.Sum(d => d.FocusMinutes);
}

/// <summary>
/// Cálculos de metas. Regla de días: una sesión cuenta para el día en que EMPEZÓ
/// (un pomodoro de 23:50 a 00:15 es del día anterior). La semana va de lunes a domingo.
/// Los minutos de enfoque incluyen sesiones detenidas; los pomodoros solo cuentan los completados.
/// </summary>
public static class Stats
{
    public static DateOnly DayOf(SessionRecord r) => DateOnly.FromDateTime(r.Start);

    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public static DaySummary Day(IEnumerable<SessionRecord> records, DateOnly date, AppSettings s)
    {
        var day = EmptyDay(date, s);
        var resolve = Resolver(s);
        foreach (var r in records)
            if (DayOf(r) == date) Add(day, r, resolve);
        return day;
    }

    public static WeekSummary Week(IReadOnlyCollection<SessionRecord> records, DateOnly anyDay, AppSettings s)
    {
        var monday = WeekStart(anyDay);
        var days = Enumerable.Range(0, 7).Select(i => EmptyDay(monday.AddDays(i), s)).ToArray();
        var resolve = Resolver(s);
        foreach (var r in records)
        {
            int i = DayOf(r).DayNumber - monday.DayNumber;
            if (i is >= 0 and < 7) Add(days[i], r, resolve);
        }
        var goals = s.Categories.Where(c => c.WeeklyGoalMinutes > 0)
            .Select(c => new CategoryGoal(c.Name, c.WeeklyGoalMinutes,
                days.Sum(d => d.MinutesByCategory.GetValueOrDefault(c.Name))))
            .ToList();
        return new WeekSummary(monday, days, goals);
    }

    private static DaySummary EmptyDay(DateOnly date, AppSettings s)
    {
        var day = new DaySummary(date);
        foreach (var c in s.Categories) day.MinutesByCategory[c.Name] = 0;
        return day;
    }

    /// <summary>Nombre CSV → nombre actual. Mismas reglas que <see cref="Categories.DisplayName"/>.</summary>
    private static Func<string, string> Resolver(AppSettings s)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in s.Categories)
            foreach (var a in c.Aliases) map.TryAdd(a.Trim(), c.Name);
        foreach (var c in s.Categories) map[c.Name.Trim()] = c.Name; // el nombre actual gana al alias
        return name => map.TryGetValue((name ?? "").Trim(), out var n) ? n : Categories.DisplayName(s, name ?? "");
    }

    private static void Add(DaySummary day, SessionRecord r, Func<string, string> resolve)
    {
        day.FocusMinutes += r.ActualMin;
        if (r.Completed) day.CompletedPomodoros++;
        var key = resolve(r.Category);
        day.MinutesByCategory[key] = day.MinutesByCategory.GetValueOrDefault(key) + r.ActualMin;
    }

    public static Dictionary<DateOnly, int> CompletedPerDay(IEnumerable<SessionRecord> records) =>
        records.Where(r => r.Completed).GroupBy(DayOf).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Racha: días seguidos que cumplen la meta diaria. Hoy cuenta solo si ya se cumplió;
    /// si aún no, la racha sigue viva y se cuenta hasta ayer.
    /// </summary>
    public static int Streak(IEnumerable<SessionRecord> records, DateOnly today, int dailyGoal)
    {
        if (dailyGoal <= 0) return 0;
        var perDay = CompletedPerDay(records);
        bool Met(DateOnly d) => perDay.GetValueOrDefault(d) >= dailyGoal;
        var d = Met(today) ? today : today.AddDays(-1);
        int streak = 0;
        while (Met(d)) { streak++; d = d.AddDays(-1); }
        return streak;
    }
}
