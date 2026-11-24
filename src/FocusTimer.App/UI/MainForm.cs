using FocusTimer.App.Native;
using FocusTimer.Core;

namespace FocusTimer.App.UI;

/// <summary>Ventana única con tres pestañas. Conecta el motor del temporizador con la UI, el CSV y Windows.</summary>
internal sealed class MainForm : Form
{
    public static readonly uint ShowMessage = Win32.RegisterWindowMessage("FocusTimer.ShowWindow");
    private static readonly uint TaskbarButtonCreated = Win32.RegisterWindowMessage("TaskbarButtonCreated");
    private static readonly TimeSpan CheckpointEvery = TimeSpan.FromSeconds(30);

    private readonly DataPaths _paths;
    private readonly SessionLog _log;
    private readonly CheckpointStore _checkpoint;
    private readonly AppSettings _settings;
    private readonly PomodoroEngine _engine = new(SystemClock.Instance);
    private readonly List<SessionRecord> _records;
    private readonly TaskbarProgress _taskbar = new();
    private readonly System.Windows.Forms.Timer _timer = new();

    private readonly TimerView _timerView = new();
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
        if (skipped > 0) startupNote += $" {skipped} fila(s) dañada(s) en sessions.csv se ignoraron (no se borraron).";

        _engine.Durations = _settings.ToDurations();
        _engine.SessionFinished += OnSessionFinished;
        _engine.PhaseCompleted += OnPhaseCompleted;

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
        _pages = [_timerView, _dashboardHost, _settingsView];
        foreach (var p in _pages) { p.Dock = DockStyle.Fill; p.Visible = false; }

        _tabs = [new NavTab("Temporizador"), new NavTab("Panel"), new NavTab("Ajustes")];
        var nav = new TableLayoutPanel { Dock = DockStyle.Top, Height = 40, ColumnCount = 3, BackColor = Theme.Back, Margin = new Padding(0) };
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
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
            if (_checkpoint.Recover(_log.Main) is { } r)
                note = $"Se recuperó un pomodoro interrumpido ({Theme.Minutes(r.ActualMin)}), guardado como incompleto.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note = "No se pudo recuperar la sesión interrumpida todavía (¿sessions.csv abierto?). Se intentará otra vez.";
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
        AcceptButton = _timerView.StartButton;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Win32.StopFlash(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_engine.Phase == Phase.Focus && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = MessageBox.Show(this, "Hay un pomodoro en curso. ¿Cerrar y guardarlo como incompleto?",
                "FocusTimer", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) { e.Cancel = true; return; }
        }
        _engine.Stop(); // guarda el enfoque en curso como incompleto
        if (WindowState == FormWindowState.Normal)
        {
            _settings.WindowX = Left;
            _settings.WindowY = Top;
        }
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
        else if (m.Msg == (int)ShowMessage && ShowMessage != 0)
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
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
        RefreshTimer();
        var now = DateTime.Now;
        if (_engine.Phase == Phase.Focus && now - _lastCheckpoint >= CheckpointEvery) SaveCheckpoint();
        if (DateOnly.FromDateTime(now) != _statsDay) RefreshStats(); // pasó la medianoche
        if (_log.HasPending && now - _lastPendingTry > TimeSpan.FromMinutes(1))
        {
            _lastPendingTry = now;
            if (_log.TryFlushPending()) _timerView.SetStatus("Las sesiones pendientes ya se guardaron en sessions.csv.");
        }
    }

    private void OnSessionFinished(SessionRecord r)
    {
        try
        {
            bool inMain = _log.Append(r);
            _checkpoint.Clear();
            _timerView.SetStatus(inMain ? "" :
                "sessions.csv está bloqueado (¿abierto en Excel?). La sesión se guardó en sessions.pending.csv y se pasará sola.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // El checkpoint se conserva: al reabrir la app la sesión se recupera.
            _timerView.SetStatus("No se pudo guardar la sesión: " + e.Message);
        }
        _records.Add(r);
        RefreshStats();
    }

    private void OnPhaseCompleted(PhaseEnded p)
    {
        var today = Stats.Day(_records, DateOnly.FromDateTime(DateTime.Now), _settings);
        string progress = $"{today.CompletedPomodoros} / {_settings.DailyGoalPomodoros} pomodoros hoy";
        var (title, body) = p.Ended == Phase.Focus
            ? ($"Pomodoro terminado – toma {(int)p.NextDuration.TotalMinutes} min",
               (p.Next == Phase.LongBreak ? "Descanso largo. " : "Descanso corto. ") + progress)
            : ("Descanso terminado", "Pulsa Iniciar para el siguiente pomodoro. " + progress);
        Notify(title, body);
    }

    private void Notify(string title, string body)
    {
        if (Form.ActiveForm != this) Win32.FlashUntilFocused(Handle);
        Toast.ShowAsync(title, body, onFailure: () => Win32.MessageBeep(0x40 /* MB_ICONASTERISK */));
    }

    private void OnSettingsChanged(bool categoriesChanged)
    {
        _settings.Normalize();
        _engine.Durations = _settings.ToDurations(); // se aplica desde la próxima fase
        if (categoriesChanged) _timerView.SetCategories(_settings, _timerView.SelectedCategoryId);
        SaveSettings();
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
        if (index == 1) RefreshStats();
    }

    private void RefreshStats()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        _statsDay = today;
        var day = Stats.Day(_records, today, _settings);
        var week = Stats.Week(_records, today, _settings);
        int streak = Stats.Streak(_records, today, _settings.DailyGoalPomodoros);
        _timerView.Goals.Width = _timerView.ClientSize.Width - 32;
        _timerView.Goals.SetData(day, _settings.DailyGoalPomodoros, streak, week, _settings);
        if (_dashboardHost.Visible)
        {
            _dashboard.Width = _dashboardHost.ClientSize.Width;
            _dashboard.SetData(_records, _settings, today);
        }
    }

    private void RefreshTimer()
    {
        _timerView.UpdateState(_engine, TimeSpan.FromMinutes(_settings.FocusMinutes));
        _timer.Interval = _engine.Phase == Phase.Idle ? 30_000 : 500;
        if (!_timer.Enabled) _timer.Start();

        string title = "FocusTimer";
        if (_engine.Phase != Phase.Idle)
        {
            int min = (int)Math.Ceiling(_engine.Remaining.TotalMinutes);
            string what = _engine.IsPaused ? "en pausa" : _engine.Phase == Phase.Focus ? "enfoque" : "descanso";
            title = $"{min} min · {what} – FocusTimer";
        }
        if (Text != title) Text = title;
        UpdateTaskbar();
    }

    private void UpdateTaskbar()
    {
        if (!IsHandleCreated) return;
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
            _timerView.SetStatus("No se pudieron guardar los ajustes: " + e.Message);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _taskbar.Dispose(); }
        base.Dispose(disposing);
    }
}
