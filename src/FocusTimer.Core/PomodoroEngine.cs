namespace FocusTimer.Core;

public enum Phase { Idle, Focus, ShortBreak, LongBreak }

public sealed record TimerDurations(TimeSpan Focus, TimeSpan ShortBreak, TimeSpan LongBreak, int LongBreakEvery)
{
    public static readonly TimerDurations Default =
        new(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), 4);

    public TimeSpan For(Phase phase) => phase switch
    {
        Phase.Focus => Focus,
        Phase.ShortBreak => ShortBreak,
        Phase.LongBreak => LongBreak,
        _ => TimeSpan.Zero,
    };
}

/// <summary>Fase que terminó sola (no por Detener/Saltar) y la fase que empieza a continuación.</summary>
public readonly record struct PhaseEnded(Phase Ended, Phase Next, TimeSpan NextDuration);

/// <summary>Estado de la sesión de enfoque en curso, para el checkpoint anti-cierres.</summary>
public sealed record ActiveFocus(string Id, DateTime Start, int PlannedMin, double ActualMin, string Category, string Task);

/// <summary>
/// Máquina de estados del Pomodoro. Usa la hora de pared (fecha límite = inicio + duración),
/// así que suspender/hibernar el PC o una CPU ocupada no la hacen derivar: al volver, <see cref="Tick"/>
/// cierra las fases cuya fecha límite ya pasó, con la hora en que realmente terminaban.
/// No usa temporizadores propios: la UI llama a <see cref="Tick"/> periódicamente.
/// </summary>
public sealed class PomodoroEngine(IClock clock)
{
    private DateTimeOffset _deadline;          // válido si corre y no está en pausa
    private TimeSpan _remainingWhenPaused;     // válido en pausa
    private string _sessionId = "";
    private DateTimeOffset _sessionStart;

    /// <summary>Duraciones; un cambio se aplica a partir de la próxima fase.</summary>
    public TimerDurations Durations { get; set; } = TimerDurations.Default;

    public Phase Phase { get; private set; } = Phase.Idle;
    public bool IsPaused { get; private set; }
    public bool IsBreak => Phase is Phase.ShortBreak or Phase.LongBreak;
    public TimeSpan PlannedDuration { get; private set; }
    /// <summary>Pomodoros completados desde que se abrió la app (define cuándo toca descanso largo).</summary>
    public int CompletedFocusCount { get; private set; }
    public string Category { get; private set; } = "";
    public string Task { get; private set; } = "";

    /// <summary>Una fase terminó por tiempo.</summary>
    public event Action<PhaseEnded>? PhaseCompleted;
    /// <summary>Una sesión de enfoque terminó (completa o detenida) y debe guardarse.</summary>
    public event Action<SessionRecord>? SessionFinished;

    public TimeSpan Remaining
    {
        get
        {
            if (Phase == Phase.Idle) return TimeSpan.Zero;
            if (IsPaused) return _remainingWhenPaused;
            var r = _deadline - clock.Now;
            return r < TimeSpan.Zero ? TimeSpan.Zero : r;
        }
    }

    /// <summary>Fracción restante 0..1 (para la barra de la barra de tareas).</summary>
    public double RemainingFraction =>
        PlannedDuration <= TimeSpan.Zero ? 0 : Math.Clamp(Remaining / PlannedDuration, 0, 1);

    /// <summary>Inicia un enfoque. Si hay un descanso en curso, lo termina. Devuelve false si ya hay un enfoque.</summary>
    public bool StartFocus(string category, string task)
    {
        Tick();
        if (Phase == Phase.Focus) return false;
        Category = category;
        Task = task.Trim();
        _sessionId = SessionRecord.NewId();
        _sessionStart = clock.Now;
        BeginPhase(Phase.Focus, _sessionStart);
        return true;
    }

    public bool Pause()
    {
        Tick();
        if (Phase == Phase.Idle || IsPaused) return false;
        _remainingWhenPaused = Remaining;
        IsPaused = true;
        return true;
    }

    public bool Resume()
    {
        if (Phase == Phase.Idle || !IsPaused) return false;
        _deadline = clock.Now + _remainingWhenPaused;
        IsPaused = false;
        return true;
    }

    /// <summary>Detiene. Un enfoque detenido se registra con completed=false. Un descanso detenido no se registra.</summary>
    public bool Stop()
    {
        Tick();
        if (Phase == Phase.Idle) return false;
        if (Phase == Phase.Focus)
            SessionFinished?.Invoke(BuildRecord(clock.Now, completed: false));
        GoIdle();
        return true;
    }

    /// <summary>Salta el descanso en curso y queda listo para iniciar otro enfoque.</summary>
    public bool SkipBreak()
    {
        Tick();
        if (!IsBreak) return false;
        GoIdle();
        return true;
    }

    /// <summary>Cierra las fases cuya fecha límite ya pasó (puede cerrar varias tras una suspensión).</summary>
    public void Tick()
    {
        while (Phase != Phase.Idle && !IsPaused && clock.Now >= _deadline)
            CompletePhase(_deadline);
    }

    /// <summary>Datos del enfoque en curso, o null si no hay.</summary>
    public ActiveFocus? Snapshot()
    {
        if (Phase != Phase.Focus) return null;
        return new ActiveFocus(_sessionId, _sessionStart.DateTime, PlannedMinutes,
            ActualMinutes(), Category, Task);
    }

    private int PlannedMinutes => (int)Math.Round(PlannedDuration.TotalMinutes);

    private double ActualMinutes() =>
        Math.Round(Math.Max(0, (PlannedDuration - Remaining).TotalMinutes), 2);

    private void CompletePhase(DateTimeOffset at)
    {
        var ended = Phase;
        if (ended == Phase.Focus)
        {
            SessionFinished?.Invoke(new SessionRecord(_sessionId, _sessionStart.DateTime, at.DateTime,
                PlannedMinutes, Math.Round(PlannedDuration.TotalMinutes, 2), Category, Task, Completed: true));
            CompletedFocusCount++;
            var every = Math.Max(1, Durations.LongBreakEvery);
            var next = CompletedFocusCount % every == 0 ? Phase.LongBreak : Phase.ShortBreak;
            BeginPhase(next, at);
            PhaseCompleted?.Invoke(new PhaseEnded(ended, next, PlannedDuration));
        }
        else
        {
            GoIdle();
            PhaseCompleted?.Invoke(new PhaseEnded(ended, Phase.Idle, TimeSpan.Zero));
        }
    }

    private SessionRecord BuildRecord(DateTimeOffset end, bool completed) =>
        new(_sessionId, _sessionStart.DateTime, end.DateTime, PlannedMinutes, ActualMinutes(),
            Category, Task, completed);

    private void BeginPhase(Phase phase, DateTimeOffset start)
    {
        Phase = phase;
        IsPaused = false;
        PlannedDuration = Durations.For(phase);
        _deadline = start + PlannedDuration;
    }

    private void GoIdle()
    {
        Phase = Phase.Idle;
        IsPaused = false;
        PlannedDuration = TimeSpan.Zero;
    }
}
