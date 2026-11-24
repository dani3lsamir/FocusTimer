using System.Drawing.Drawing2D;
using FocusTimer.Core;

namespace FocusTimer.App.UI;

/// <summary>Panel: hoy, gráfico semanal apilado por categoría (GDI+, sin librerías), metas semanales y racha.</summary>
internal sealed class DashboardView : PaintedControl
{
    private static readonly string[] DayNames = ["L", "M", "X", "J", "V", "S", "D"];

    private DaySummary? _today;
    private WeekSummary? _week;
    private int _streak, _dailyGoal, _focusMin;
    private List<(string Name, Color Color)> _legend = [];
    private DateOnly _todayDate;

    public void SetData(IReadOnlyCollection<SessionRecord> records, AppSettings s, DateOnly today)
    {
        _todayDate = today;
        _today = Stats.Day(records, today, s);
        _week = Stats.Week(records, today, s);
        _streak = Stats.Streak(records, today, s.DailyGoalPomodoros);
        _dailyGoal = s.DailyGoalPomodoros;
        _focusMin = s.FocusMinutes;
        // Orden y color: categorías de los ajustes y luego nombres antiguos sin categoría actual (gris).
        _legend = s.Categories.Select((c, i) => (c.Name, Theme.CategoryColor(i))).ToList();
        foreach (var name in _week.Days.SelectMany(d => d.MinutesByCategory.Keys).Concat(_today.MinutesByCategory.Keys).Distinct())
            if (_legend.All(l => l.Name != name)) _legend.Add((name, Theme.Other));
        Height = Render(null);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_today is null || _week is null) return;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        Render(e.Graphics);
    }

    /// <summary>Dibuja (si g != null) y devuelve la altura total.</summary>
    private int Render(Graphics? g)
    {
        if (_today is null || _week is null) return 0;
        int pad = S(16), w = Width - 2 * pad, y = S(8);

        // ---- Hoy ----
        Heading(g, "Hoy", pad, ref y, w);
        if (g is not null)
        {
            int col = w / 3;
            StatBlock(g, Theme.Minutes(_today.FocusMinutes), "de enfoque", pad, y, col, Theme.Text);
            StatBlock(g, $"{_today.CompletedPomodoros} / {_dailyGoal}", "pomodoros",
                pad + col, y, col, _today.CompletedPomodoros >= _dailyGoal ? Theme.Accent : Theme.Text);
            StatBlock(g, _streak.ToString(), _streak == 1 ? "día de racha" : "días de racha", pad + 2 * col, y, col, Theme.Text);
        }
        y += S(58);
        var todayCats = _today.MinutesByCategory.Where(kv => kv.Value > 0).ToList();
        if (todayCats.Count == 0)
        {
            if (g is not null) Draw(g, "Aún no hay sesiones hoy.", Theme.Normal, new Rectangle(pad, y, w, S(20)), Theme.Muted);
            y += S(24);
        }
        else
        {
            double max = todayCats.Max(kv => kv.Value);
            foreach (var (name, min) in todayCats)
            {
                if (g is not null)
                {
                    var color = ColorOf(name);
                    Draw(g, name, Theme.Normal, new Rectangle(pad, y, w, S(18)), Theme.Text);
                    Draw(g, Theme.Minutes(min), Theme.Normal, new Rectangle(pad, y, w, S(18)), Theme.Muted, TextFormatFlags.Right);
                    Bar(g, new Rectangle(pad, y + S(20), w, S(5)), min / max, color);
                }
                y += S(32);
            }
        }

        // ---- Semana ----
        y += S(8);
        Heading(g, "Esta semana · " + Theme.Minutes(_week.TotalMinutes), pad, ref y, w);
        int chartH = S(140);
        if (g is not null) Chart(g, new Rectangle(pad, y, w, chartH));
        y += chartH + S(26);
        // Leyenda
        int x = pad;
        foreach (var (name, color) in _legend)
        {
            int tw = TextRenderer.MeasureText(name, Theme.Small).Width + S(22);
            if (x + tw > pad + w) { x = pad; y += S(18); }
            if (g is not null)
            {
                using (var b = new SolidBrush(color)) g.FillRectangle(b, x, y + S(5), S(9), S(9));
                Draw(g, name, Theme.Small, new Rectangle(x + S(13), y, tw, S(18)), Theme.Muted);
            }
            x += tw;
        }
        y += S(28);

        // ---- Metas semanales ----
        Heading(g, "Metas semanales", pad, ref y, w);
        if (_week.Goals.Count == 0)
        {
            if (g is not null)
                Draw(g, "Sin metas por categoría. Agrégalas en Ajustes.", Theme.Normal, new Rectangle(pad, y, w, S(20)), Theme.Muted);
            y += S(24);
        }
        foreach (var goal in _week.Goals)
        {
            if (g is not null)
            {
                var done = goal.Fraction >= 1;
                Draw(g, goal.Category, Theme.Normal, new Rectangle(pad, y, w, S(18)), Theme.Text);
                Draw(g, (done ? "✓ " : "") + $"{Theme.Minutes(goal.ActualMinutes)} / {Theme.Minutes(goal.GoalMinutes)} · {goal.Fraction:P0}",
                    Theme.Normal, new Rectangle(pad, y, w, S(18)), done ? ColorOf(goal.Category) : Theme.Muted, TextFormatFlags.Right);
                Bar(g, new Rectangle(pad, y + S(20), w, S(5)), goal.Fraction, ColorOf(goal.Category));
            }
            y += S(32);
        }
        return y + S(12);
    }

    private void Chart(Graphics g, Rectangle area)
    {
        var days = _week!.Days;
        double goalMinutes = _dailyGoal * _focusMin;
        double max = Math.Max(Math.Max(days.Max(d => d.FocusMinutes), goalMinutes), 30) * 1.1;
        int labelH = S(16);
        var plot = area with { Y = area.Y + labelH, Height = area.Height - labelH };
        int slot = plot.Width / 7, barW = (int)(slot * 0.56);

        using (var axis = new Pen(Theme.Border)) g.DrawLine(axis, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        for (int i = 0; i < 7; i++)
        {
            var day = days[i];
            int x = plot.Left + i * slot + (slot - barW) / 2;
            int bottom = plot.Bottom;
            foreach (var (name, color) in _legend)
            {
                double m = day.MinutesByCategory.GetValueOrDefault(name);
                if (m <= 0) continue;
                int h = Math.Max(1, (int)Math.Round(m / max * plot.Height));
                using var b = new SolidBrush(color);
                g.FillRectangle(b, x, bottom - h, barW, h);
                bottom -= h;
            }
            if (day.FocusMinutes > 0)
                Draw(g, ((int)Math.Round(day.FocusMinutes)).ToString(), Theme.Small,
                    new Rectangle(x - S(10), bottom - labelH, barW + S(20), labelH), Theme.Muted, TextFormatFlags.HorizontalCenter);
            bool isToday = day.Date == _todayDate;
            Draw(g, DayNames[i], isToday ? Theme.Bold : Theme.Small,
                new Rectangle(plot.Left + i * slot, plot.Bottom + S(4), slot, S(18)),
                isToday ? Theme.Accent : Theme.Muted, TextFormatFlags.HorizontalCenter);
        }

        // Línea de la meta diaria (pomodoros × minutos de enfoque)
        int gy = plot.Bottom - (int)Math.Round(goalMinutes / max * plot.Height);
        using (var pen = new Pen(Theme.Muted) { DashStyle = DashStyle.Dash })
            g.DrawLine(pen, plot.Left, gy, plot.Right, gy);
        Draw(g, $"meta {goalMinutes:0} min", Theme.Small, new Rectangle(plot.Left, gy - labelH, plot.Width, labelH),
            Theme.Muted, TextFormatFlags.Right);
    }

    private void Heading(Graphics? g, string text, int x, ref int y, int w)
    {
        if (g is not null) Draw(g, text, Theme.Heading, new Rectangle(x, y, w, S(24)), Theme.Text);
        y += S(30);
    }

    private void StatBlock(Graphics g, string value, string label, int x, int y, int w, Color color)
    {
        Draw(g, value, Theme.Stat, new Rectangle(x, y, w, S(34)), color);
        Draw(g, label, Theme.Small, new Rectangle(x, y + S(34), w, S(18)), Theme.Muted);
    }

    private Color ColorOf(string name) => _legend.FirstOrDefault(l => l.Name == name).Color is { IsEmpty: false } c ? c : Theme.Other;
}
