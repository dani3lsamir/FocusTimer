namespace FocusTimer.Core;

/// <summary>Una sesión de enfoque registrada (una fila de sessions.csv). Los descansos no se registran.</summary>
/// <param name="Start">Hora local de inicio.</param>
/// <param name="End">Hora local de fin.</param>
/// <param name="ActualMin">Minutos reales de enfoque (sin contar pausas).</param>
/// <param name="Category">Nombre de la categoría en el momento de la sesión.</param>
public sealed record SessionRecord(
    string Id,
    DateTime Start,
    DateTime End,
    int PlannedMin,
    double ActualMin,
    string Category,
    string Task,
    bool Completed)
{
    public static string NewId() => Guid.NewGuid().ToString("N");
}
