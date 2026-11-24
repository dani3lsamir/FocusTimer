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
    public SessionRecord? Recover(SessionStore store)
    {
        if (!File.Exists(Path)) return null;
        ActiveCheckpoint? cp = null;
        try { cp = JsonSerializer.Deserialize(File.ReadAllText(Path), CoreJsonContext.Default.ActiveCheckpoint); }
        catch (JsonException) { }
        SessionRecord? rec = null;
        if (cp is not null && !string.IsNullOrEmpty(cp.Id) && store.ReadAll().All(r => r.Id != cp.Id))
        {
            rec = new SessionRecord(cp.Id, cp.Start, cp.LastSeen, cp.PlannedMin, cp.ActualMin,
                cp.Category, cp.Task, Completed: false);
            store.Append(rec);
        }
        Clear();
        return rec;
    }
}
