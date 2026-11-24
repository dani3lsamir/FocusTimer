using System.Diagnostics;
using FocusTimer.App.Native;
using FocusTimer.Core;

namespace FocusTimer.App.UI;

/// <summary>Ajustes: duraciones, metas, categorías (añadir, renombrar, ordenar), inicio con Windows y carpeta de datos.</summary>
internal sealed class SettingsView : UserControl
{
    /// <summary>Se cambió algo; el argumento indica si cambiaron las categorías.</summary>
    public event Action<bool>? SettingsChanged;

    private readonly AppSettings _s;
    private readonly string _dataFolder;
    private readonly NumericUpDown _focus, _short, _long, _every, _daily, _weekly;
    private readonly ListBox _cats;
    private readonly TextBox _name;
    private readonly Label _catError;
    private readonly CheckBox _startup;
    private bool _loading;

    public SettingsView(AppSettings settings, string dataFolder)
    {
        _s = settings;
        _dataFolder = dataFolder;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        AutoScroll = true;
        Padding = new Padding(16, 4, 16, 12);

        _focus = Num(1, 180, 1);
        _short = Num(1, 60, 1);
        _long = Num(1, 120, 1);
        _every = Num(1, 12, 1);
        _daily = Num(1, 50, 1);
        _weekly = Num(0, 10080, 30);

        _cats = new ListBox { Height = 104, IntegralHeight = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        Theme.StyleInput(_cats);
        _cats.SelectedIndexChanged += (_, _) => OnCategorySelected();

        _name = new TextBox { MaxLength = 40, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 6, 4) };
        Theme.StyleInput(_name);
        _catError = Theme.Label("", Theme.Small, Theme.Accent);
        _catError.Margin = new Padding(0, 2, 0, 0);

        _startup = new CheckBox
        {
            Text = "Iniciar con Windows", AutoSize = true, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 6, 0, 4), Cursor = Cursors.Hand,
        };
        _startup.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            if (!StartupRegistration.SetEnabled(_startup.Checked))
            {
                _loading = true; _startup.Checked = StartupRegistration.IsEnabled(); _loading = false;
            }
        };

        var openFolder = Theme.Button("Abrir carpeta de datos");
        openFolder.AutoSize = true;
        openFolder.Click += (_, _) => OpenDataFolder();

        // --- Disposición ---
        var root = new TableLayoutPanel
        {
            ColumnCount = 2, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Back,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));

        void Section(string title)
        {
            var l = Theme.Label(title, Theme.Heading, Theme.Text);
            l.Margin = new Padding(0, 12, 0, 4);
            root.Controls.Add(l);
            root.SetColumnSpan(l, 2);
        }
        void Field(string label, Control c)
        {
            var l = Theme.Label(label, Theme.Normal, Theme.Muted);
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, 4, 0, 4);
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            c.Margin = new Padding(0, 3, 0, 3);
            root.Controls.Add(l);
            root.Controls.Add(c);
        }
        void Full(Control c)
        {
            root.Controls.Add(c);
            root.SetColumnSpan(c, 2);
        }

        Section("Duraciones (minutos)");
        Field("Enfoque", _focus);
        Field("Descanso corto", _short);
        Field("Descanso largo", _long);
        Field("Descanso largo cada (pomodoros)", _every);

        Section("Metas");
        Field("Meta diaria (pomodoros completos)", _daily);

        Section("Categorías");
        var catRow = new TableLayoutPanel { ColumnCount = 2, Height = 104, Dock = DockStyle.Top, Margin = new Padding(0), BackColor = Theme.Back };
        catRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        catRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        var arrows = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0), BackColor = Theme.Back };
        var up = Theme.Button("▲"); up.Width = 38; up.Click += (_, _) => MoveSelected(-1);
        var down = Theme.Button("▼"); down.Width = 38; down.Click += (_, _) => MoveSelected(+1);
        arrows.Controls.AddRange([up, down]);
        catRow.Controls.Add(_cats, 0, 0);
        catRow.Controls.Add(arrows, 1, 0);
        Full(catRow);

        var nameRow = new TableLayoutPanel { ColumnCount = 3, Height = 40, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 0), BackColor = Theme.Back };
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var add = Theme.Button("Añadir"); add.AutoSize = true; add.Margin = new Padding(0, 2, 6, 2); add.Click += (_, _) => AddCategory();
        var rename = Theme.Button("Renombrar"); rename.AutoSize = true; rename.Margin = new Padding(0, 2, 0, 2); rename.Click += (_, _) => RenameSelected();
        nameRow.Controls.Add(_name, 0, 0);
        nameRow.Controls.Add(add, 1, 0);
        nameRow.Controls.Add(rename, 2, 0);
        Full(nameRow);
        Full(_catError);
        Field("Meta semanal de la categoría (min, 0 = sin meta)", _weekly);
        var hint = Theme.Label("Renombrar no cambia el CSV: las sesiones antiguas siguen contando para la categoría.", Theme.Small);
        hint.MaximumSize = new Size(340, 0);
        Full(hint);

        Section("Sistema");
        Full(_startup);
        Full(openFolder);
        var path = Theme.Label(dataFolder, Theme.Small);
        path.MaximumSize = new Size(340, 0);
        Full(path);
        var privacy = Theme.Label("100 % sin conexión: ningún dato sale de este PC. sessions.csv es la exportación.", Theme.Small);
        privacy.MaximumSize = new Size(340, 0);
        Full(privacy);

        Controls.Add(root);

        foreach (var n in new[] { _focus, _short, _long, _every, _daily }) n.ValueChanged += (_, _) => OnNumbersChanged();
        _weekly.ValueChanged += (_, _) => OnWeeklyChanged();
        _name.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddCategory(); e.SuppressKeyPress = true; } };

        LoadValues();
    }

    private static NumericUpDown Num(int min, int max, int step)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = step, Width = 80, TextAlign = HorizontalAlignment.Right };
        Theme.StyleInput(n);
        return n;
    }

    private void LoadValues()
    {
        _loading = true;
        _focus.Value = _s.FocusMinutes;
        _short.Value = _s.ShortBreakMinutes;
        _long.Value = _s.LongBreakMinutes;
        _every.Value = _s.LongBreakEvery;
        _daily.Value = _s.DailyGoalPomodoros;
        _startup.Checked = StartupRegistration.IsEnabled();
        RefreshCategoryList(_s.Categories.FirstOrDefault()?.Id);
        _loading = false;
    }

    private void RefreshCategoryList(string? selectId)
    {
        bool wasLoading = _loading;
        _loading = true;
        _cats.BeginUpdate();
        _cats.Items.Clear();
        foreach (var c in _s.Categories)
            _cats.Items.Add(c.WeeklyGoalMinutes > 0 ? $"{c.Name}   ·   meta {Theme.Minutes(c.WeeklyGoalMinutes)}/sem" : c.Name);
        _cats.EndUpdate();
        int idx = _s.Categories.FindIndex(c => c.Id == selectId);
        _cats.SelectedIndex = idx >= 0 ? idx : (_s.Categories.Count > 0 ? 0 : -1);
        _loading = wasLoading;
        OnCategorySelected();
    }

    private CategoryDef? Selected =>
        _cats.SelectedIndex >= 0 && _cats.SelectedIndex < _s.Categories.Count ? _s.Categories[_cats.SelectedIndex] : null;

    private void OnCategorySelected()
    {
        var c = Selected;
        bool wasLoading = _loading;
        _loading = true;
        _weekly.Enabled = c is not null;
        _weekly.Value = c?.WeeklyGoalMinutes ?? 0;
        if (c is not null) _name.Text = c.Name;
        _loading = wasLoading;
        _catError.Text = "";
    }

    private void OnNumbersChanged()
    {
        if (_loading) return;
        _s.FocusMinutes = (int)_focus.Value;
        _s.ShortBreakMinutes = (int)_short.Value;
        _s.LongBreakMinutes = (int)_long.Value;
        _s.LongBreakEvery = (int)_every.Value;
        _s.DailyGoalPomodoros = (int)_daily.Value;
        SettingsChanged?.Invoke(false);
    }

    private void OnWeeklyChanged()
    {
        if (_loading || Selected is not { } c) return;
        c.WeeklyGoalMinutes = (int)_weekly.Value;
        RefreshCategoryList(c.Id);
        SettingsChanged?.Invoke(true);
    }

    private void AddCategory()
    {
        var err = Categories.Add(_s, _name.Text, out var added);
        if (err is not null) { _catError.Text = err; return; }
        RefreshCategoryList(added!.Id);
        SettingsChanged?.Invoke(true);
    }

    private void RenameSelected()
    {
        if (Selected is not { } c) return;
        var err = Categories.Rename(_s, c.Id, _name.Text);
        if (err is not null) { _catError.Text = err; return; }
        RefreshCategoryList(c.Id);
        SettingsChanged?.Invoke(true);
    }

    private void MoveSelected(int delta)
    {
        if (Selected is not { } c || !Categories.Move(_s, c.Id, delta)) return;
        RefreshCategoryList(c.Id);
        SettingsChanged?.Invoke(true);
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(_dataFolder);
        using var _ = Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _dataFolder + "\"") { UseShellExecute = false });
    }
}
