namespace FocusTimer.App.UI;

/// <summary>Tema AMOLED: fondo negro puro, un color de acento y texto con buen contraste.</summary>
internal static class Theme
{
    public static readonly Color Back = Color.Black;
    public static readonly Color Surface = Color.FromArgb(0x14, 0x14, 0x14);
    public static readonly Color Surface2 = Color.FromArgb(0x24, 0x24, 0x24);
    public static readonly Color Border = Color.FromArgb(0x3A, 0x3A, 0x3A);
    public static readonly Color Text = Color.FromArgb(0xEC, 0xEC, 0xEC);      // ~17:1 sobre negro
    public static readonly Color Muted = Color.FromArgb(0xA6, 0xA6, 0xA6);     // ~8.5:1
    public static readonly Color Accent = Color.FromArgb(0xFF, 0x6B, 0x4A);    // ~7:1, el único color de acento
    public static readonly Color OnAccent = Color.Black;
    public static readonly Color BreakText = Color.FromArgb(0xFF, 0xD5, 0x4F); // fase de descanso (igual que la barra amarilla)
    public static readonly Color Other = Color.FromArgb(0x78, 0x78, 0x78);

    /// <summary>Colores de categorías para los gráficos (la primera usa el acento).</summary>
    public static readonly Color[] Categories =
    [
        Accent,
        Color.FromArgb(0x4F, 0xC3, 0xF7),
        Color.FromArgb(0x81, 0xC7, 0x84),
        Color.FromArgb(0xFF, 0xD5, 0x4F),
        Color.FromArgb(0xBA, 0x68, 0xC8),
        Color.FromArgb(0xF0, 0x62, 0x92),
        Color.FromArgb(0x4D, 0xB6, 0xAC),
        Color.FromArgb(0xA1, 0x88, 0x7F),
    ];

    public static readonly Font Normal = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.25f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11.25f);
    public static readonly Font Stat = new("Segoe UI Semilight", 18f);
    public static readonly Font Clock = new("Segoe UI Light", 44f);

    public static Color CategoryColor(int index) => index < 0 ? Other : Categories[index % Categories.Length];

    public static Button Button(string text, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Surface,
            ForeColor = primary ? OnAccent : Text,
            Font = primary ? Bold : Normal,
            Height = primary ? 40 : 32,
            Margin = new Padding(0, 4, 0, 4),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0xFF, 0x8A, 0x70) : Surface2;
        b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(0xE0, 0x55, 0x38) : Border;
        return b;
    }

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = font ?? Normal,
        ForeColor = color ?? Muted,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 8, 0, 2),
    };

    public static void StyleInput(Control c)
    {
        c.BackColor = Surface;
        c.ForeColor = Text;
        c.Font = Normal;
        switch (c)
        {
            case ComboBox cb: cb.FlatStyle = FlatStyle.Flat; break;
            case NumericUpDown n: n.BorderStyle = BorderStyle.FixedSingle; break;
            case TextBox t: t.BorderStyle = BorderStyle.FixedSingle; break;
            case ListBox l: l.BorderStyle = BorderStyle.FixedSingle; break;
        }
    }

    public static string Minutes(double minutes)
    {
        int m = (int)Math.Round(minutes);
        return m < 60 ? $"{m} min" : $"{m / 60} h {m % 60:00} min";
    }

    public static float Scale(Control c) => c.DeviceDpi / 96f;
}

/// <summary>Control pintado a mano con doble búfer (base de los paneles con gráficos).</summary>
internal abstract class PaintedControl : Control
{
    protected PaintedControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
    }

    protected int S(float v) => (int)Math.Round(v * Theme.Scale(this));

    protected static void Draw(Graphics g, string text, Font font, Rectangle r, Color color,
        TextFormatFlags align = TextFormatFlags.Left) =>
        TextRenderer.DrawText(g, text, font, r, color,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

    protected void Bar(Graphics g, Rectangle r, double fraction, Color color)
    {
        using (var bg = new SolidBrush(Theme.Surface2)) g.FillRectangle(bg, r);
        int w = (int)Math.Round(r.Width * Math.Clamp(fraction, 0, 1));
        if (w > 0) using (var fg = new SolidBrush(color)) g.FillRectangle(fg, r with { Width = w });
    }
}

/// <summary>Pestaña de la barra superior (texto + subrayado de acento si está activa).</summary>
internal sealed class NavTab : PaintedControl
{
    private bool _selected;

    public NavTab(string text)
    {
        Text = text;
        Cursor = Cursors.Hand;
        Font = Theme.Bold;
        TabStop = false;
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Draw(e.Graphics, base.Text, Font, ClientRectangle, _selected ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter);
        if (_selected)
            using (var b = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(b, new Rectangle(S(16), Height - S(3), Width - S(32), S(3)));
    }
}
