using FocusTimer.Core;

namespace FocusTimer.App.UI;

/// <summary>
/// Pestaña de timers simples (cocina, recordatorios): rápidos de 3/5/10 min, timers guardados de un clic,
/// uno nuevo con nombre y duración, y la lista de los que están corriendo. No se registran ni cuentan para metas.
/// </summary>
internal sealed class TimersView : UserControl
{
    /// <summary>Cambió algo que hay que guardar en settings.json (timers guardados).</summary>
    public event EventHandler? PresetsChanged;
    /// <summary>Se inició, pausó o quitó un timer.</summary>
    public event EventHandler? BoardChanged;

    private static readonly int[] QuickMinutes = [3, 5, 10];

    private readonly CountdownBoard _board;
    private readonly AppSettings _s;
    private readonly FlowLayoutPanel _presets, _running;
    private readonly TextBox _name, _duration;
    private readonly Label _error, _presetsHint, _empty;
    private readonly ToolTip _tips = new();
    private readonly Dictionary<int, TimerRow> _rows = [];

    public TimersView(CountdownBoard board, AppSettings settings)
    {
        _board = board;
        _s = settings;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Padding = new Padding(16, 4, 16, 8);

        // Rápidos
        var quick = new TableLayoutPanel { ColumnCount = QuickMinutes.Length, Height = 40, Dock = DockStyle.Top, Margin = new Padding(0), BackColor = Theme.Back };
        for (int i = 0; i < QuickMinutes.Length; i++)
        {
            int m = QuickMinutes[i];
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / QuickMinutes.Length));
            var b = Theme.Button($"{m} min");
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(i == 0 ? 0 : 3, 4, i == QuickMinutes.Length - 1 ? 0 : 3, 4);
            b.Click += (_, _) => StartTimer(null, TimeSpan.FromMinutes(m));
            quick.Controls.Add(b, i, 0);
        }

        // Guardados
        _presets = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            Dock = DockStyle.Top, Margin = new Padding(0), BackColor = Theme.Back,
        };
        _presetsHint = Theme.Label("Aún no hay timers guardados. Crea uno abajo y pulsa Guardar.", Theme.Small);
        _presetsHint.MaximumSize = new Size(348, 0);

        // Nuevo
        _name = new TextBox { MaxLength = 30, PlaceholderText = "Nombre (opcional): Huevo", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 6, 4) };
        _duration = new TextBox { MaxLength = 10, PlaceholderText = "10 o 6:30", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4), TextAlign = HorizontalAlignment.Center };
        Theme.StyleInput(_name);
        Theme.StyleInput(_duration);
        _tips.SetToolTip(_duration, "Minutos (10, 6.5), m:ss (6:30), h:mm:ss, o con unidad: 90s, 2h");
        var inputs = new TableLayoutPanel { ColumnCount = 2, Height = 34, Dock = DockStyle.Top, Margin = new Padding(0), BackColor = Theme.Back };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        inputs.Controls.Add(_name, 0, 0);
        inputs.Controls.Add(_duration, 1, 0);

        var start = Theme.Button("Iniciar", primary: true);
        var save = Theme.Button("Guardar");
        var actions = new TableLayoutPanel { ColumnCount = 2, Height = 48, Dock = DockStyle.Top, Margin = new Padding(0), BackColor = Theme.Back };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        start.Dock = DockStyle.Fill; start.Margin = new Padding(0, 4, 6, 4);
        save.Dock = DockStyle.Fill; save.Margin = new Padding(0, 4, 0, 4);
        _tips.SetToolTip(save, "Guarda este timer arriba para iniciarlo con un clic");
        start.Click += (_, _) => StartFromInputs();
        save.Click += (_, _) => SavePreset();
        actions.Controls.Add(start, 0, 0);
        actions.Controls.Add(save, 1, 0);
        foreach (var t in new[] { _name, _duration })
            t.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { StartFromInputs(); e.SuppressKeyPress = true; } };

        _error = Theme.Label("", Theme.Small, Theme.Accent);
        _error.Margin = new Padding(0);

        // En curso
        _running = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Back,
        };
        _running.SizeChanged += (_, _) => FitRows();
        _empty = Theme.Label("Ningún timer corriendo.", Theme.Normal);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Back };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(Control c, bool fill = false)
        {
            layout.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            if (c is Label) c.Anchor = AnchorStyles.Left;
            else c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            layout.Controls.Add(c);
        }
        Row(Theme.Label("Rápidos"));
        Row(quick);
        Row(Theme.Label("Guardados  ·  clic para iniciar, clic derecho para eliminar"));
        Row(_presets);
        Row(_presetsHint);
        Row(Theme.Label("Nuevo"));
        Row(inputs);
        Row(actions);
        Row(_error);
        Row(Theme.Label("En curso"));
        Row(_empty);
        _running.Dock = DockStyle.Fill;
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_running);
        Controls.Add(layout);

        RebuildPresets();
        RebuildRows();
    }

    // ------------------------------------------------------------------ guardados

    private void RebuildPresets()
    {
        _presets.SuspendLayout();
        foreach (Control c in _presets.Controls) c.Dispose();
        _presets.Controls.Clear();
        foreach (var p in _s.TimerPresets)
        {
            var preset = p;
            var b = Theme.Button(preset.ToString());
            b.AutoSize = true;
            b.Height = 32;
            b.Margin = new Padding(0, 2, 6, 2);
            b.Click += (_, _) => StartTimer(preset.Name, TimeSpan.FromSeconds(preset.Seconds));
            var menu = new ContextMenuStrip { ShowImageMargin = false, BackColor = Theme.Surface, ForeColor = Theme.Text };
            menu.Items.Add("Editar abajo", null, (_, _) =>
            {
                _name.Text = preset.Name;
                _duration.Text = DurationText.Format(TimeSpan.FromSeconds(preset.Seconds)).Replace(" min", "");
            });
            menu.Items.Add($"Eliminar «{preset.Name}»", null, (_, _) =>
            {
                _s.TimerPresets.Remove(preset);
                PresetsChanged?.Invoke(this, EventArgs.Empty);
                BeginInvoke(RebuildPresets);
            });
            b.ContextMenuStrip = menu;
            b.Disposed += (_, _) => menu.Dispose();
            _presets.Controls.Add(b);
        }
        _presets.ResumeLayout();
        _presets.Visible = _s.TimerPresets.Count > 0;
        _presetsHint.Visible = _s.TimerPresets.Count == 0;
    }

    private void SavePreset()
    {
        if (!TryReadDuration(out var d)) return;
        var err = _s.SaveTimerPreset(_name.Text, d);
        _error.Text = err ?? "";
        if (err is not null) return;
        PresetsChanged?.Invoke(this, EventArgs.Empty);
        RebuildPresets();
    }

    // ------------------------------------------------------------------ iniciar

    private void StartFromInputs()
    {
        if (!TryReadDuration(out var d)) return;
        if (StartTimer(_name.Text, d)) { _name.Text = ""; _duration.Text = ""; }
    }

    private bool TryReadDuration(out TimeSpan d)
    {
        if (DurationText.TryParse(_duration.Text, out d)) return true;
        _error.Text = _duration.Text.Trim().Length == 0
            ? "Escribe la duración: 10 (min), 6:30, 90s..."
            : "Duración no válida. Ejemplos: 10, 6:30, 1:15:00, 90s (máx. 24 h).";
        _duration.Focus();
        return false;
    }

    private bool StartTimer(string? name, TimeSpan d)
    {
        if (_board.Start(name, d) is null)
        {
            _error.Text = $"Máximo {CountdownBoard.MaxTimers} timers a la vez. Quita alguno terminado.";
            return false;
        }
        _error.Text = "";
        RebuildRows();
        BoardChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // ------------------------------------------------------------------ en curso

    /// <summary>Reconstruye las filas si cambió la lista; si no, solo actualiza los tiempos.</summary>
    public void RebuildRows()
    {
        var ids = _board.Items.Select(c => c.Id).ToHashSet();
        if (!ids.SetEquals(_rows.Keys))
        {
            _running.SuspendLayout();
            foreach (var id in _rows.Keys.Where(k => !ids.Contains(k)).ToList())
            {
                _rows[id].Dispose();
                _rows.Remove(id);
            }
            // Los más nuevos arriba.
            foreach (var c in _board.Items.Reverse())
            {
                if (_rows.ContainsKey(c.Id)) continue;
                var row = new TimerRow(c);
                row.PauseClicked += (_, _) =>
                {
                    if (c.State == CountdownState.Paused) _board.Resume(c.Id); else _board.Pause(c.Id);
                    UpdateRows();
                    BoardChanged?.Invoke(this, EventArgs.Empty);
                };
                row.RemoveClicked += (_, _) =>
                {
                    _board.Remove(c.Id);
                    BeginInvoke(() => { RebuildRows(); BoardChanged?.Invoke(this, EventArgs.Empty); });
                };
                _rows[c.Id] = row;
                _running.Controls.Add(row);
                _running.Controls.SetChildIndex(row, 0);
            }
            _running.ResumeLayout();
            FitRows();
        }
        _empty.Visible = _rows.Count == 0;
        UpdateRows();
    }

    public void UpdateRows()
    {
        foreach (var c in _board.Items)
            if (_rows.TryGetValue(c.Id, out var row))
                row.UpdateState(_board.Remaining(c), _board.Fraction(c));
    }

    private void FitRows()
    {
        int w = _running.ClientSize.Width - 2;
        foreach (var r in _rows.Values) r.Width = w;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Una fila: nombre, cuenta atrás, barra y botones Pausar/Reanudar y Quitar.</summary>
internal sealed class TimerRow : UserControl
{
    public event EventHandler? PauseClicked, RemoveClicked;

    private readonly Countdown _c;
    private readonly Label _name, _time;
    private readonly Button _pause, _remove;
    private readonly ThinBar _bar = new();

    public TimerRow(Countdown c)
    {
        _c = c;
        BackColor = Theme.Surface;
        Height = 58;
        Margin = new Padding(0, 0, 0, 6);
        Padding = new Padding(10, 6, 6, 6);

        _name = new Label { Text = c.Label, AutoSize = false, AutoEllipsis = true, Font = Theme.Normal, ForeColor = Theme.Text, Location = new Point(10, 6), Height = 20 };
        _time = new Label { AutoSize = false, Font = Theme.Heading, ForeColor = Theme.Text, Location = new Point(10, 26), Size = new Size(110, 24), TextAlign = ContentAlignment.MiddleLeft };
        _pause = Theme.Button("Pausar");
        _remove = Theme.Button("✕");
        _pause.Size = new Size(82, 30);
        _remove.Size = new Size(34, 30);
        _pause.Anchor = _remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _pause.Click += (_, _) => PauseClicked?.Invoke(this, EventArgs.Empty);
        _remove.Click += (_, _) => RemoveClicked?.Invoke(this, EventArgs.Empty);
        _bar.Height = 4;
        _bar.Dock = DockStyle.Bottom;
        Controls.AddRange([_name, _time, _pause, _remove, _bar]);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        int w = ClientSize.Width;
        _remove.Location = new Point(w - _remove.Width - 8, (ClientSize.Height - _remove.Height - _bar.Height) / 2);
        _pause.Location = new Point(_remove.Left - _pause.Width - 6, _remove.Top);
        _name.Width = Math.Max(40, _pause.Left - 16);
    }

    public void UpdateState(TimeSpan remaining, double fraction)
    {
        bool finished = _c.State == CountdownState.Finished;
        bool paused = _c.State == CountdownState.Paused;
        string time = finished ? "¡Terminado!" : DurationText.Clock(remaining) + (paused ? "  en pausa" : "");
        if (_time.Text != time) _time.Text = time;
        _time.Width = Math.Max(110, _pause.Left - 16);
        _time.ForeColor = finished ? Theme.Accent : paused ? Theme.Muted : Theme.Text;
        _pause.Visible = !finished;
        string pauseText = paused ? "Reanudar" : "Pausar";
        if (_pause.Text != pauseText) _pause.Text = pauseText;
        _bar.Set(finished ? 1 : fraction, finished ? Theme.Accent : paused ? Theme.Muted : Theme.Categories[1]);
    }
}

internal sealed class ThinBar : PaintedControl
{
    private double _fraction;
    private Color _color = Theme.Accent;

    public void Set(double fraction, Color color)
    {
        if (Math.Abs(fraction - _fraction) < 0.0005 && color == _color) return;
        _fraction = fraction;
        _color = color;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) => Bar(e.Graphics, ClientRectangle, _fraction, _color);
}
