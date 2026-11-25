using FocusTimer.App.Native;
using FocusTimer.Core;

namespace FocusTimer.App.UI;

/// <summary>Ventana única con tres pestañas. Conecta el motor del temporizador con la UI, el CSV y Windows.</summary>
internal sealed class MainForm : Form
{
    private static readonly uint TaskbarButtonCreated = Win32.RegisterWindowMessage("TaskbarButtonCreated");
    private static readonly TimeSpan CheckpointEvery = TimeSpan.FromSeconds(30);

    private readonly DataPaths _paths;
    private readonly SessionLog _log;
    private readonly CheckpointStore _checkpoint;
    private readonly AppSettings _settings;
    private readonly PomodoroEngine _engine = new(SystemClock.Instance);
    private readonly CountdownBoard _board = new(SystemClock.Instance);
    private readonly List<SessionRecord> _records;
    private readonly TaskbarProgress _taskbar = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    /// <summary>Agrupa cambios seguidos de ajustes (p. ej. flechas de un número) en una sola escritura.</summary>
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 500 };

    private readonly TimerView _timerView = new();
    private readonly TimersView _timersView;
    private readonly DashboardView _dashboard = new();
    private readonly Panel _dashboardHost;
    private readonly SettingsView _settingsView;
    private readonly NavTab[] _tabs;
    private readonly Control[] _pages;

    private DateTime _lastCheckpoint = DateTime.MinValue;
    private DateTime _lastPendingTry = DateTime.MinValue;
    private DateOnly _statsDay;

    public MainForm(DataPaths paths)
    {
        _paths = paths;
        _log = new SessionLog(paths);
        _checkpoint = new CheckpointStore(paths.CheckpointJson);
        _settings = SettingsStore.Load(paths.SettingsJson);

        string startupNote = RecoverPreviousRun();
        _records = _log.ReadAll(out int skipped);
        if (skipped > 0) startupNote += $" {skipped} damaged row(s) in sessions.csv were ignored (not deleted).";

        _engine.Durations = _settings.ToDurations();
        // Cerrar y reabrir el mismo día no reinicia la cuenta hacia el descanso largo.
        _engine.SeedCompletedCount(Stats.Day(_records, DateOnly.FromDateTime(DateTime.Now), _settings).CompletedPomodoros);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSettings(); };
        _engine.SessionFinished += OnSessionFinished;
        _engine.PhaseCompleted += OnPhaseCompleted;
        _board.Finished += OnCountdownFinished;

        // --- Ventana ---
        Text = "FocusTimer";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(380, 620);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Theme.Normal;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); } catch (Exception) { }

        // --- Pestañas ---
        _dashboardHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Back };
        _dashboard.Width = 364;
        _dashboard.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _dashboardHost.Controls.Add(_dashboard);
        _settingsView = new SettingsView(_settings, paths.Root);
        _timersView = new TimersView(_board, _settings);
        _timersView.PresetsChanged += (_, _) => SaveSettings();
        _timersView.BoardChanged += (_, _) => RefreshTimer();
        _pages = [_timerView, _timersView, _dashboardHost, _settingsView];
        foreach (var p in _pages) { p.Dock = DockStyle.Fill; p.Visible = false; }

        _tabs = [new NavTab("Pomodoro"), new NavTab("Timers"), new NavTab("Dashboard"), new NavTab("Settings")];
        var nav = new TableLayoutPanel { Dock = DockStyle.Top, Height = 40, ColumnCount = _tabs.Length, BackColor = Theme.Back, Margin = new Padding(0) };
        for (int i = 0; i < _tabs.Length; i++)
        {
            int index = i;
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / _tabs.Length));
            _tabs[i].Dock = DockStyle.Fill;
            _tabs[i].Margin = new Padding(0);
            _tabs[i].Click += (_, _) => ShowPage(index);
            nav.Controls.Add(_tabs[i], i, 0);
        }
        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
        content.Controls.AddRange(_pages);
        Controls.Add(content);
        Controls.Add(nav);

        // --- Temporizador ---
        _timerView.SetCategories(_settings, _settings.LastCategoryId);
        _timerView.SetRecentTasks(_settings.RecentTasks, _settings.LastTask);
        _timerView.SetStatus(startupNote.Trim());
        _timerView.StartClicked += (_, _) => StartFocus();
        _timerView.PauseClicked += (_, _) => { if (_engine.IsPaused) _engine.Resume(); else _engine.Pause(); SaveCheckpoint(); RefreshTimer(); };
        _timerView.StopClicked += (_, _) => { _engine.Stop(); RefreshTimer(); };
        _timerView.SkipClicked += (_, _) => { _engine.SkipBreak(); RefreshTimer(); };
        _settingsView.SettingsChanged += OnSettingsChanged;

        _timer.Tick += (_, _) => OnTick();
        RestorePosition();
        ShowPage(0);
        RefreshStats();
        RefreshTimer();
    }

    // ------------------------------------------------------------------ inicio / cierre

    private string RecoverPreviousRun()
    {
        string note = "";
        try
        {
            if (_checkpoint.Recover(_log) is { } r)
                note = $"Recovered an interrupted pomodoro ({Theme.Minutes(r.ActualMin)}), saved as incomplete.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note = "Couldn't recover the interrupted session yet (data folder not writable?). Will retry next time.";
        }
        _log.TryFlushPending();
        // Icono para los toasts (se copia del recurso embebido a la carpeta de datos, local).
        string? icon = Path.Combine(_paths.Root, "icon.png");
        try
        {
            if (!File.Exists(icon))
            {
                using var src = typeof(MainForm).Assembly.GetManifestResourceStream("FocusTimer.icon.png");
                if (src is not null) { using var dst = File.Create(icon); src.CopyTo(dst); } else icon = null;
            }
        }
        catch (IOException) { icon = null; }
        Toast.Register("FocusTimer", icon);
        StartupRegistration.RefreshPathIfEnabled();
        return note;
    }

    private void RestorePosition()
    {
        if (_settings.WindowX is int x && _settings.WindowY is int y &&
            Screen.AllScreens.Any(s => s.WorkingArea.Contains(x + 60, y + 20)))
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(x, y);
        }
        else StartPosition = FormStartPosition.CenterScreen;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win32.UseDarkTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _taskbar.Init();
        RefreshTimer();
        RefreshStats(); // alturas de los paneles pintados ya con el DPI real
        AcceptButton = _timerView.StartButton;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(RefreshStats);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Win32.StopFlash(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        bool focus = _engine.Phase == Phase.Focus, timers = _board.AnyActive;
        if ((focus || timers) && e.CloseReason == CloseReason.UserClosing)
        {
            string question = (focus, timers) switch
            {
                (true, true) => "A pomodoro and timers are running. Close anyway? The pomodoro is saved as incomplete and the timers are lost.",
                (true, false) => "A pomodoro is running. Close and save it as incomplete?",
                _ => "Timers are running and will be lost. Close anyway?",
            };
            var answer = MessageBox.Show(this, question,
                "FocusTimer", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) { e.Cancel = true; return; }
        }
        _engine.Stop(); // guarda el enfoque en curso como incompleto
        if (WindowState == FormWindowState.Normal)
        {
            _settings.WindowX = Left;
            _settings.WindowY = Top;
        }
        _saveTimer.Stop();
        SaveSettings();
        _timer.Stop();
        _taskbar.Dispose();
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)TaskbarButtonCreated && TaskbarButtonCreated != 0)
        {
            _taskbar.Reset();
            UpdateTaskbar();
        }
        else if (m.Msg == (int)Win32.ShowWindowMessage && Win32.ShowWindowMessage != 0)
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Win32.SetForegroundWindow(Handle);
        }
        base.WndProc(ref m);
    }

    // ------------------------------------------------------------------ acciones

    private void StartFocus()
    {
        if (_engine.Phase == Phase.Focus) return;
        var cat = Categories.Find(_settings, _timerView.SelectedCategoryId ?? "") ?? _settings.Categories[0];
        var task = _timerView.TaskText.Trim();
        _settings.LastCategoryId = cat.Id;
        _settings.RememberTask(task);
        SaveSettings();
        _engine.StartFocus(cat.Name, task);
        _timerView.SetRecentTasks(_settings.RecentTasks, task);
        _timerView.SetStatus("");
        Win32.StopFlash(Handle);
        SaveCheckpoint();
        RefreshTimer();
    }

    private void OnTick()
    {
        _engine.Tick();
        _board.Tick();
        if (_timersView.Visible) _timersView.UpdateRows();
        RefreshTimer();
        var now = DateTime.Now;
        // En pausa no se escribe nada (el checkpoint ya se guardó al pausar).
        if (_engine.Phase == Phase.Focus && !_engine.IsPaused && now - _lastCheckpoint >= CheckpointEvery) SaveCheckpoint();
        if (DateOnly.FromDateTime(now) != _statsDay) RefreshStats(); // pasó la medianoche
        if (_log.HasPending && now - _lastPendingTry > TimeSpan.FromMinutes(1))
        {
            _lastPendingTry = now;
            if (_log.TryFlushPending()) _timerView.SetStatus("Pending sessions were saved to sessions.csv.");
        }
    }

    private void OnSessionFinished(SessionRecord r)
    {
        try
        {
            bool inMain = _log.Append(r);
            _checkpoint.Clear();
            _timerView.SetStatus(inMain ? "" :
                "sessions.csv is locked (open in Excel?). The session went to sessions.pending.csv and will be merged automatically.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // El checkpoint se conserva: al reabrir la app la sesión se recupera.
            _timerView.SetStatus("Couldn't save the session: " + e.Message);
        }
        _records.Add(r);
        RefreshStats();
    }

    private void OnPhaseCompleted(PhaseEnded p)
    {
        var today = Stats.Day(_records, DateOnly.FromDateTime(DateTime.Now), _settings);
        string progress = $"{today.CompletedPomodoros} / {_settings.DailyGoalPomodoros} pomodoros today";
        var (title, body) = p.Ended == Phase.Focus
            ? ($"Pomodoro done – take {(int)p.NextDuration.TotalMinutes} min",
               (p.Next == Phase.LongBreak ? "Long break. " : "Short break. ") + progress)
            : ("Break over", "Press Start for the next pomodoro. " + progress);
        Notify(title, body);
    }

    private void OnCountdownFinished(Countdown c)
    {
        _timersView.RebuildRows();
        Notify($"⏰ {c.Label} – done", $"{DurationText.Format(c.Duration)} timer finished.");
    }

    private void Notify(string title, string body)
    {
        if (Form.ActiveForm != this) Win32.FlashUntilFocused(Handle);
        Toast.ShowAsync(title, body, onFailure: () => Win32.MessageBeep(0x40 /* MB_ICONASTERISK */));
    }

    private void OnSettingsChanged(bool categoriesChanged)
    {
        _settings.Normalize();
        _engine.Durations = _settings.ToDurations();
        // Cerrar y reabrir el mismo día no reinicia la cuenta hacia el descanso largo.
        _engine.SeedCompletedCount(Stats.Day(_records, DateOnly.FromDateTime(DateTime.Now), _settings).CompletedPomodoros);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSettings(); }; // se aplica desde la próxima fase
        if (categoriesChanged) _timerView.SetCategories(_settings, _timerView.SelectedCategoryId);
        _saveTimer.Stop();
        _saveTimer.Start();
        RefreshStats();
        RefreshTimer();
    }

    // ------------------------------------------------------------------ refresco

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
            _tabs[i].SetSelected(i == index);
        }
        AcceptButton = index == 0 ? _timerView.StartButton : null;
        if (_pages[index] == _dashboardHost) RefreshStats();
        if (_pages[index] == _timersView) _timersView.RebuildRows();
    }

    private void RefreshStats()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        _statsDay = today;
        var day = Stats.Day(_records, today, _settings);
        var week = Stats.Week(_records, today, _settings);
        int streak = Stats.Streak(_records, today, _settings.DailyGoalPomodoros);
        _timerView.Goals.SetData(day, _settings.DailyGoalPomodoros, streak, week, _settings);
        if (_dashboardHost.Visible)
        {
            _dashboard.Width = _dashboardHost.ClientSize.Width - (_dashboardHost.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth);
            _dashboard.SetData(day, week, streak, _settings); // mismos cálculos, sin repetirlos
        }
    }

    private void RefreshTimer()
    {
        _timerView.UpdateState(_engine, TimeSpan.FromMinutes(_settings.FocusMinutes));
        int interval = _engine.Phase == Phase.Idle && !_board.AnyRunning ? 30_000 : 500;
        if (_timer.Interval != interval) _timer.Interval = interval;
        if (!_timer.Enabled) _timer.Start();

        // Título (se ve al pasar el ratón por el botón de la barra de tareas).
        var parts = new List<string>();
        if (_engine.Phase != Phase.Idle)
        {
            int min = (int)Math.Ceiling(_engine.Remaining.TotalMinutes);
            string what = _engine.IsPaused ? "paused" : _engine.Phase == Phase.Focus ? "focus" : "break";
            parts.Add($"{min} min · {what}");
        }
        var next = _board.NextDue();
        if (next is not null)
        {
            int others = _board.Items.Count(c => c.State == CountdownState.Running) - 1;
            parts.Add($"{next.Label} {DurationText.Clock(_board.Remaining(next))}" + (others > 0 ? $" (+{others})" : ""));
        }
        string title = parts.Count == 0 ? "FocusTimer" : string.Join("  |  ", parts) + " – FocusTimer";
        if (Text != title) Text = title;
        UpdateTaskbar(next);
    }

    private void UpdateTaskbar() => UpdateTaskbar(_board.NextDue());

    private void UpdateTaskbar(Countdown? next)
    {
        if (!IsHandleCreated) return;
        // El Pomodoro tiene prioridad; si no hay, se muestra el timer que termina antes.
        if (_engine.Phase == Phase.Idle && next is not null)
        {
            _taskbar.Set(Handle, TaskbarState.Focus, _board.Fraction(next));
            return;
        }
        var state = _engine.Phase == Phase.Idle ? TaskbarState.None
            : _engine.IsPaused ? TaskbarState.Paused
            : _engine.IsBreak ? TaskbarState.Break
            : TaskbarState.Focus;
        _taskbar.Set(Handle, state, _engine.RemainingFraction);
    }

    private void SaveCheckpoint()
    {
        _lastCheckpoint = DateTime.Now;
        if (_engine.Snapshot() is not { } s) return;
        try
        {
            _checkpoint.Save(new ActiveCheckpoint(s.Id, s.Start, DateTime.Now, s.PlannedMin, s.ActualMin, s.Category, s.Task));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void SaveSettings()
    {
        try { SettingsStore.Save(_paths.SettingsJson, _settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _timerView.SetStatus("Couldn't save settings: " + e.Message);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _saveTimer.Dispose(); _taskbar.Dispose(); }
        base.Dispose(disposing);
    }
}
