using System.Text.Json;

namespace FocusTimer.Core;

/// <summary>Último estado conocido del enfoque en curso.</summary>
public sealed record ActiveCheckpoint(
    string Id, DateTime Start, DateTime LastSeen, int PlannedMin, double ActualMin, string Category, string Task);

/// <summary>
/// current.json: se escribe cada ~30 s durante un enfoque. Si la app o el PC se cierran de golpe,
/// al volver a abrir se registra esa sesión con completed=false hasta la última hora guardada.
/// </summary>
public sealed class CheckpointStore(string path)
{
    public string Path { get; } = path;

    public void Save(ActiveCheckpoint cp)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        AtomicFile.WriteAllText(Path, JsonSerializer.Serialize(cp, CoreJsonContext.Default.ActiveCheckpoint));
    }

    public void Clear()
    {
        try { File.Delete(Path); } catch (IOException) { }
    }

    /// <summary>Si quedó un enfoque sin cerrar, lo añade al CSV (si no estaba ya) y borra el checkpoint.</summary>
    public SessionRecord? Recover(SessionStore store) =>
        RecoverCore(() => store.ReadAll().Select(r => r.Id), store.Append);

    /// <summary>
    /// Igual, pero usando el registro con respaldo: si sessions.csv está bloqueado, la sesión va a
    /// sessions.pending.csv y el checkpoint se borra igualmente (así no lo pisa el siguiente pomodoro).
    /// Si no se puede escribir en ningún archivo, lanza IOException y el checkpoint se conserva.
    /// </summary>
    public SessionRecord? Recover(SessionLog log) =>
        RecoverCore(() => KnownIds(log), r => log.Append(r));

    private static IEnumerable<string> KnownIds(SessionLog log)
    {
        IEnumerable<string> ids = [];
        try { ids = log.Main.ReadAll().Select(r => r.Id); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // ilegible: deduplicar solo con pendientes
        if (log.HasPending) ids = ids.Concat(log.Pending.ReadAll().Select(r => r.Id));
        return ids.ToList();
    }

    private SessionRecord? RecoverCore(Func<IEnumerable<string>> knownIds, Action<SessionRecord> append)
    {
        if (!File.Exists(Path)) return null;
        ActiveCheckpoint? cp = null;
        try { cp = JsonSerializer.Deserialize(File.ReadAllText(Path), CoreJsonContext.Default.ActiveCheckpoint); }
        catch (JsonException) { }
        SessionRecord? rec = null;
        if (cp is not null && !string.IsNullOrEmpty(cp.Id) && !knownIds().Contains(cp.Id))
        {
            rec = new SessionRecord(cp.Id, cp.Start, cp.LastSeen, cp.PlannedMin, cp.ActualMin,
                cp.Category, cp.Task, Completed: false);
            append(rec); // si lanza, el checkpoint NO se borra
        }
        Clear();
        return rec;
    }
}
