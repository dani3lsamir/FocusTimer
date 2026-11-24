namespace FocusTimer.Core;

/// <summary>Fuente de hora. Se inyecta para poder probar el temporizador sin esperar.</summary>
public interface IClock
{
    /// <summary>Hora actual con desplazamiento (reloj de pared, no ticks).</summary>
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset Now => DateTimeOffset.Now;
}
