namespace FocusTimer.Core;

/// <summary>
/// Registro de sesiones con respaldo: si sessions.csv no se puede abrir (bloqueado por otro programa),
/// la fila va a sessions.pending.csv y se pasa al archivo principal en cuanto vuelve a estar libre.
/// </summary>
public sealed class SessionLog(SessionStore main, SessionStore pending)
{
    public SessionLog(DataPaths paths) : this(new SessionStore(paths.SessionsCsv), new SessionStore(paths.PendingCsv)) { }

    public SessionStore Main { get; } = main;
    public SessionStore Pending { get; } = pending;
    public bool HasPending => File.Exists(Pending.Path);

    /// <summary>Devuelve true si se guardó en sessions.csv; false si quedó en el respaldo.</summary>
    public bool Append(SessionRecord r)
    {
        try
        {
            FlushPending();
            Main.Append(r);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Pending.Append(r);
            return false;
        }
    }

    /// <summary>Pasa las filas pendientes al archivo principal (sin duplicar por id). Lanza IOException si sigue bloqueado.</summary>
    public int FlushPending()
    {
        if (!HasPending) return 0;
        var ids = Main.ReadAll().Select(r => r.Id).ToHashSet();
        int moved = 0;
        foreach (var r in Pending.ReadAll())
            if (ids.Add(r.Id)) { Main.Append(r); moved++; }
        File.Delete(Pending.Path);
        return moved;
    }

    public bool TryFlushPending()
    {
        try { FlushPending(); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Todas las sesiones: principal + pendientes que aún no están en el principal.</summary>
    public List<SessionRecord> ReadAll() => ReadAll(out _);

    public List<SessionRecord> ReadAll(out int skippedRows)
    {
        var all = Main.ReadAll(out skippedRows);
        if (HasPending)
        {
            var ids = all.Select(r => r.Id).ToHashSet();
            all.AddRange(Pending.ReadAll().Where(r => ids.Add(r.Id)));
        }
        return all;
    }
}
