using System.Globalization;

namespace FocusTimer.Core;

public enum CountdownState { Running, Paused, Finished }

/// <summary>Un timer simple (cocina, recordatorios...). No se registra en sessions.csv ni cuenta para metas.</summary>
public sealed class Countdown
{
    private DateTimeOffset _deadline;
    private TimeSpan _pausedRemaining;

    internal Countdown(int id, string label, TimeSpan duration, DateTimeOffset now)
    {
        Id = id;
        Label = label;
        Duration = duration;
        _deadline = now + duration;
    }

    public int Id { get; }
    public string Label { get; }
    public TimeSpan Duration { get; }
    public CountdownState State { get; private set; } = CountdownState.Running;

    public TimeSpan Remaining(DateTimeOffset now) => State switch
    {
        CountdownState.Paused => _pausedRemaining,
        CountdownState.Finished => TimeSpan.Zero,
        _ => _deadline > now ? _deadline - now : TimeSpan.Zero,
    };

    /// <summary>Fracción restante 0..1.</summary>
    public double Fraction(DateTimeOffset now) => Math.Clamp(Remaining(now) / Duration, 0, 1);

    internal bool Pause(DateTimeOffset now)
    {
        if (State != CountdownState.Running) return false;
        _pausedRemaining = Remaining(now);
        State = CountdownState.Paused;
        return true;
    }

    internal bool Resume(DateTimeOffset now)
    {
        if (State != CountdownState.Paused) return false;
        _deadline = now + _pausedRemaining;
        State = CountdownState.Running;
        return true;
    }

    internal bool CheckDue(DateTimeOffset now)
    {
        if (State != CountdownState.Running || now < _deadline) return false;
        State = CountdownState.Finished;
        return true;
    }
}

/// <summary>
/// Varios timers a la vez, con hora de pared (igual que el Pomodoro: no derivan si el PC se suspende).
/// La UI llama a <see cref="Tick"/> periódicamente.
/// </summary>
public sealed class CountdownBoard(IClock clock)
{
    public const int MaxTimers = 10;
    private readonly List<Countdown> _items = [];
    private int _nextId = 1;

    public IReadOnlyList<Countdown> Items => _items;
    public bool AnyRunning => _items.Any(c => c.State == CountdownState.Running);
    public bool AnyActive => _items.Any(c => c.State != CountdownState.Finished);

    /// <summary>Un timer llegó a cero.</summary>
    public event Action<Countdown>? Finished;

    /// <summary>Inicia un timer. Devuelve null si ya hay <see cref="MaxTimers"/> o la duración no es válida.</summary>
    public Countdown? Start(string? label, TimeSpan duration)
    {
        if (duration < DurationText.Min || duration > DurationText.Max || _items.Count >= MaxTimers) return null;
        label = string.IsNullOrWhiteSpace(label) ? "Timer " + DurationText.Format(duration) : label.Trim();
        var c = new Countdown(_nextId++, label, duration, clock.Now);
        _items.Add(c);
        return c;
    }

    public TimeSpan Remaining(Countdown c) => c.Remaining(clock.Now);
    public double Fraction(Countdown c) => c.Fraction(clock.Now);
    public bool Pause(int id) => Find(id)?.Pause(clock.Now) ?? false;
    public bool Resume(int id) => Find(id)?.Resume(clock.Now) ?? false;
    public bool Remove(int id) => _items.RemoveAll(c => c.Id == id) > 0;

    /// <summary>El timer en marcha que termina antes (para el título y la barra de tareas).</summary>
    public Countdown? NextDue() => _items.Where(c => c.State == CountdownState.Running)
        .OrderBy(c => c.Remaining(clock.Now)).FirstOrDefault();

    public void Tick()
    {
        var now = clock.Now;
        foreach (var c in _items.ToList())
            if (c.CheckDue(now)) Finished?.Invoke(c);
    }

    private Countdown? Find(int id) => _items.FirstOrDefault(c => c.Id == id);
}

/// <summary>Timer guardado: un clic lo inicia.</summary>
public sealed class TimerPreset
{
    public string Name { get; set; } = "";
    public int Seconds { get; set; }

    public override string ToString() => $"{Name} · {DurationText.Format(TimeSpan.FromSeconds(Seconds))}";
}

/// <summary>Lee y muestra duraciones: "10" = 10 min, "6:30", "1:15:00", "6.5", "90s", "2h", "45m", "1h30m".</summary>
public static class DurationText
{
    public static readonly TimeSpan Min = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Max = TimeSpan.FromHours(24);

    /// <summary>Nunca lanza excepciones: cualquier texto fuera de rango devuelve false.</summary>
    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var t = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace(',', '.');
        if (t.Length == 0 || t.Length > 20) return false;

        double seconds;
        if (t.Contains(':'))
        {
            // m:ss o h:mm:ss
            var parts = t.Split(':');
            if (parts.Length > 3) return false;
            var n = new long[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!TryDigits(parts[i], out n[i])) return false;
            if (n.Skip(1).Any(v => v >= 60)) return false;
            seconds = n.Length == 2 ? n[0] * 60 + n[1] : n[0] * 3600 + n[1] * 60 + n[2];
        }
        else if (t.IndexOf('h') is int h and > 0 && h < t.Length - 1)
        {
            // compuesto: 1h30, 1h30m, 1h30min
            var rest = t[(h + 1)..];
            if (rest.EndsWith("min")) rest = rest[..^3];
            else if (rest.EndsWith('m')) rest = rest[..^1];
            if (!TryDigits(t[..h], out var hours) || !TryDigits(rest, out var mins) || mins >= 60) return false;
            seconds = hours * 3600 + mins * 60;
        }
        else
        {
            double factor = 60; // sin unidad = minutos
            if (t.EndsWith("min")) t = t[..^3];
            else if (t.EndsWith('m')) t = t[..^1];
            else if (t.EndsWith('s')) { factor = 1; t = t[..^1]; }
            else if (t.EndsWith('h')) { factor = 3600; t = t[..^1]; }
            if (!double.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
                return false;
            seconds = Math.Round(v * factor);
        }
        if (seconds < Min.TotalSeconds || seconds > Max.TotalSeconds) return false;
        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }

    /// <summary>Solo dígitos ASCII, como máximo 9 (sin desbordes).</summary>
    private static bool TryDigits(string s, out long value)
    {
        value = 0;
        if (s.Length is 0 or > 9 || !s.All(char.IsAsciiDigit)) return false;
        value = long.Parse(s, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>"10 min", "6:30", "1 h 30 min", "1:05:30".</summary>
    public static string Format(TimeSpan d)
    {
        int s = Math.Max(0, (int)Math.Round(d.TotalSeconds));
        if (s % 60 == 0)
        {
            int m = s / 60;
            return m < 60 ? $"{m} min" : m % 60 == 0 ? $"{m / 60} h" : $"{m / 60} h {m % 60} min";
        }
        return s < 3600 ? $"{s / 60}:{s % 60:00}" : $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}";
    }

    /// <summary>Texto para editar: siempre se puede volver a leer con <see cref="TryParse"/> ("10", "6:30", "1:30:00", "45s").</summary>
    public static string ToInput(TimeSpan d)
    {
        int s = Math.Max(1, (int)Math.Round(d.TotalSeconds));
        if (s < 60) return $"{s}s";
        if (s < 3600) return s % 60 == 0 ? $"{s / 60}" : $"{s / 60}:{s % 60:00}";
        return $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}";
    }

    /// <summary>Cuenta atrás para mostrar: "9:58" o "1:02:03".</summary>
    public static string Clock(TimeSpan d)
    {
        int s = Math.Max(0, (int)Math.Ceiling(d.TotalSeconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }
}
