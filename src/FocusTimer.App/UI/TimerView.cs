using FocusTimer.Core;

namespace FocusTimer.App.UI;

/// <summary>Pestaña principal: reloj, categoría (un clic), tarea opcional, botones y progreso de metas.</summary>
internal sealed class TimerView : UserControl
{
    public event EventHandler? StartClicked, PauseClicked, StopClicked, SkipClicked;

    private readonly Label _phase, _time, _status;
    private readonly FlowLayoutPanel _chips;
    private readonly ComboBox _task;
    private readonly Button _start, _pause, _stop, _skip;
    private readonly ToolTip _tips = new();

    public GoalsPanel Goals { get; } = new();
    public Button StartButton => _start;
    public string? SelectedCategoryId { get; private set; }
    public string TaskText => _task.Text;

    public TimerView()
    {
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Padding = new Padding(16, 4, 16, 8);

        _phase = Theme.Label("Listo", Theme.Bold, Theme.Muted);
        _phase.Anchor = AnchorStyles.None;
        _time = new Label
        {
            Text = "25:00", Font = Theme.Clock, ForeColor = Theme.Text, AutoSize = false, Height = 84,
            TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0),
        };
        _chips = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0), BackColor = Theme.Back,
        };
        _task = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 200, MaxDropDownItems = AppSettings.MaxRecentTasks,
            Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 0, 6),
        };
        Theme.StyleInput(_task);

        _start = Theme.Button("Iniciar", primary: true);
        _start.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _pause = Theme.Button("Pausar");
        _stop = Theme.Button("Detener");
        _skip = Theme.Button("Saltar descanso");
        _tips.SetToolTip(_stop, "Termina el pomodoro y lo guarda como incompleto");
        _start.Click += (_, _) => StartClicked?.Invoke(this, EventArgs.Empty);
        _pause.Click += (_, _) => PauseClicked?.Invoke(this, EventArgs.Empty);
        _stop.Click += (_, _) => StopClicked?.Invoke(this, EventArgs.Empty);
        _skip.Click += (_, _) => SkipClicked?.Invoke(this, EventArgs.Empty);

        var secondary = new TableLayoutPanel
        {
            ColumnCount = 3, RowCount = 1, Height = 40, Margin = new Padding(0),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
        };
        for (int i = 0; i < 3; i++) secondary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        foreach (var (b, i) in new[] { (_pause, 0), (_stop, 1), (_skip, 2) })
        {
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(i == 0 ? 0 : 3, 4, i == 2 ? 0 : 3, 4);
            secondary.Controls.Add(b, i, 0);
        }

        Goals.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Goals.Margin = new Padding(0, 14, 0, 0);
        _status = Theme.Label("", Theme.Small, Theme.Muted);
        _status.MaximumSize = new Size(348, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Back };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Control[] rows =
        [
            _phase, _time, Theme.Label("Categoría"), _chips, Theme.Label("Tarea (opcional)"), _task,
            _start, secondary, Goals, _status,
        ];
        foreach (var c in rows)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(c);
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);
    }

    public void SetCategories(AppSettings s, string? selectedId)
    {
        _chips.SuspendLayout();
        foreach (Control c in _chips.Controls) c.Dispose();
        _chips.Controls.Clear();
        if (s.Categories.All(c => c.Id != selectedId)) selectedId = s.Categories[0].Id;
        SelectedCategoryId = selectedId;
        foreach (var cat in s.Categories)
        {
            var rb = new RadioButton
            {
                Text = cat.Name, Tag = cat.Id, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat,
                AutoSize = true, Font = Theme.Normal, BackColor = Theme.Surface, ForeColor = Theme.Text,
                Margin = new Padding(0, 2, 6, 2), Padding = new Padding(6, 2, 6, 2), Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false, TextAlign = ContentAlignment.MiddleCenter,
            };
            rb.FlatAppearance.BorderColor = Theme.Border;
            rb.FlatAppearance.CheckedBackColor = Theme.Accent;
            rb.FlatAppearance.MouseOverBackColor = Theme.Surface2;
            rb.CheckedChanged += (_, _) =>
            {
                rb.ForeColor = rb.Checked ? Theme.OnAccent : Theme.Text;
                rb.FlatAppearance.BorderColor = rb.Checked ? Theme.Accent : Theme.Border;
                if (rb.Checked) SelectedCategoryId = (string)rb.Tag!;
            };
            rb.Checked = cat.Id == selectedId;
            _chips.Controls.Add(rb);
        }
        _chips.ResumeLayout();
    }

    public void SetRecentTasks(IEnumerable<string> recent, string current)
    {
        _task.BeginUpdate();
        _task.Items.Clear();
        foreach (var t in recent) _task.Items.Add(t);
        _task.EndUpdate();
        _task.Text = current;
    }

    public void SetStatus(string text) => _status.Text = text;

    public void UpdateState(PomodoroEngine e, TimeSpan idleDuration)
    {
        string phase = e.Phase switch
        {
            Phase.Focus => "Enfoque · " + e.Category,
            Phase.ShortBreak => "Descanso corto",
            Phase.LongBreak => "Descanso largo",
            _ => "Listo",
        };
        if (e.IsPaused) phase += " · en pausa";
        SetText(_phase, phase);
        _phase.ForeColor = e.IsPaused ? Theme.Muted : e.Phase == Phase.Focus ? Theme.Accent : e.IsBreak ? Theme.BreakText : Theme.Muted;

        var t = e.Phase == Phase.Idle ? idleDuration : e.Remaining;
        int secs = (int)Math.Ceiling(t.TotalSeconds);
        SetText(_time, $"{secs / 60:00}:{secs % 60:00}");
        _time.ForeColor = e.IsPaused ? Theme.Muted : Theme.Text;

        bool focus = e.Phase == Phase.Focus;
        _start.Enabled = !focus;
        _pause.Enabled = e.Phase != Phase.Idle;
        SetText(_pause, e.IsPaused ? "Reanudar" : "Pausar");
        _stop.Enabled = e.Phase != Phase.Idle;
        _skip.Enabled = e.IsBreak;
        _chips.Enabled = !focus;
        _task.Enabled = !focus;
    }

    private static void SetText(Control c, string text)
    {
        if (c.Text != text) c.Text = text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
