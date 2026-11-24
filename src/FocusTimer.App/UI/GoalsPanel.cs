using FocusTimer.Core;

namespace FocusTimer.App.UI;

/// <summary>Progreso hacia las metas, en la pestaña del temporizador.</summary>
internal sealed class GoalsPanel : PaintedControl
{
    private readonly record struct Row(string Label, string Value, double Fraction, Color Color);
    private readonly List<Row> _rows = [];
    private const int RowHeight = 34;

    public void SetData(DaySummary today, int dailyGoal, int streak, WeekSummary week, AppSettings s)
    {
        _rows.Clear();
        _rows.Add(new("Today", $"{today.CompletedPomodoros} / {dailyGoal} pomodoros · {Theme.Minutes(today.FocusMinutes)}",
            (double)today.CompletedPomodoros / dailyGoal, Theme.Accent));
        _rows.Add(new("Streak", streak == 1 ? "1 day" : $"{streak} days", -1, Theme.Accent));
        foreach (var g in week.Goals)
        {
            int idx = s.Categories.FindIndex(c => c.Name == g.Category);
            _rows.Add(new($"{g.Category} (week)", $"{Theme.Minutes(g.ActualMinutes)} / {Theme.Minutes(g.GoalMinutes)}",
                g.Fraction, Theme.CategoryColor(idx)));
        }
        Height = S(_rows.Count * RowHeight + 4);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int y = 0;
        foreach (var r in _rows)
        {
            var line = new Rectangle(0, y, Width, S(20));
            Draw(g, r.Label, Theme.Normal, line, Theme.Muted);
            Draw(g, r.Fraction >= 1 ? "✓ " + r.Value : r.Value, Theme.Normal, line,
                r.Fraction >= 1 ? r.Color : Theme.Text, TextFormatFlags.Right);
            if (r.Fraction >= 0) Bar(g, new Rectangle(0, y + S(22), Width, S(5)), r.Fraction, r.Color);
            y += S(RowHeight);
        }
    }
}
