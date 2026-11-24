namespace FocusTimer.Core;

/// <summary>Rutas de los datos del usuario. Por defecto %LOCALAPPDATA%\FocusTimer (nunca dentro del repo).</summary>
public sealed class DataPaths(string root)
{
    public string Root { get; } = root;
    public string SessionsCsv => Path.Combine(Root, "sessions.csv");
    public string SettingsJson => Path.Combine(Root, "settings.json");
    public string CheckpointJson => Path.Combine(Root, "current.json");
    /// <summary>Respaldo si sessions.csv está bloqueado (p. ej. abierto en Excel). Se fusiona después.</summary>
    public string PendingCsv => Path.Combine(Root, "sessions.pending.csv");

    public static DataPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusTimer"));

    public void EnsureExists() => Directory.CreateDirectory(Root);
}
