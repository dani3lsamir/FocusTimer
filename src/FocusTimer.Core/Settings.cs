using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusTimer.Core;

public sealed class CategoryDef
{
    /// <summary>Id estable: no cambia al renombrar.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Nombres anteriores. Las filas antiguas del CSV con esos nombres cuentan para esta categoría.</summary>
    public List<string> Aliases { get; set; } = [];
    /// <summary>Meta semanal en minutos; 0 = sin meta.</summary>
    public int WeeklyGoalMinutes { get; set; }
}

public sealed class AppSettings
{
    public const int MaxRecentTasks = 10;
    public const int MaxTimerPresets = 20;

    public int SchemaVersion { get; set; } = 1;
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int LongBreakEvery { get; set; } = 4;
    public int DailyGoalPomodoros { get; set; } = 8;
    public List<CategoryDef> Categories { get; set; } = DefaultCategories();
    public string? LastCategoryId { get; set; }
    public string LastTask { get; set; } = "";
    public List<string> RecentTasks { get; set; } = [];
    /// <summary>Timers guardados (nombre + duración). Vacío por defecto.</summary>
    public List<TimerPreset> TimerPresets { get; set; } = [];
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }

    public static List<CategoryDef> DefaultCategories() =>
    [
        new() { Id = "work", Name = "Work" },
        new() { Id = "study", Name = "Study" },
        new() { Id = "personal", Name = "Personal" },
    ];

    public TimerDurations ToDurations() => new(
        TimeSpan.FromMinutes(FocusMinutes), TimeSpan.FromMinutes(ShortBreakMinutes),
        TimeSpan.FromMinutes(LongBreakMinutes), LongBreakEvery);

    /// <summary>Corrige valores fuera de rango o faltantes (p. ej. un settings.json editado a mano).</summary>
    public void Normalize()
    {
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180);
        ShortBreakMinutes = Math.Clamp(ShortBreakMinutes, 1, 60);
        LongBreakMinutes = Math.Clamp(LongBreakMinutes, 1, 120);
        LongBreakEvery = Math.Clamp(LongBreakEvery, 1, 12);
        DailyGoalPomodoros = Math.Clamp(DailyGoalPomodoros, 1, 50);
        Categories ??= [];
        Categories.RemoveAll(c => c is null || string.IsNullOrWhiteSpace(c.Name));
        var ids = new HashSet<string>();
        foreach (var c in Categories)
        {
            c.Aliases ??= [];
            c.Name = c.Name.Trim();
            c.WeeklyGoalMinutes = Math.Clamp(c.WeeklyGoalMinutes, 0, 10080);
            if (string.IsNullOrWhiteSpace(c.Id) || !ids.Add(c.Id)) { c.Id = Categories_NewId(); ids.Add(c.Id); }
        }
        if (Categories.Count == 0) Categories = DefaultCategories();
        if (LastCategoryId is null || Categories.All(c => c.Id != LastCategoryId)) LastCategoryId = Categories[0].Id;
        LastTask ??= "";
        RecentTasks = (RecentTasks ?? []).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRecentTasks).ToList();
        TimerPresets = (TimerPresets ?? []).Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name)).Take(MaxTimerPresets).ToList();
        foreach (var p in TimerPresets)
        {
            p.Name = p.Name.Trim();
            if (p.Name.Length > 30) p.Name = p.Name[..30];
            p.Seconds = Math.Clamp(p.Seconds, (int)DurationText.Min.TotalSeconds, (int)DurationText.Max.TotalSeconds);
        }
    }

    /// <summary>Guarda (o actualiza, si el nombre ya existe) un timer. Devuelve un error en español o null.</summary>
    public string? SaveTimerPreset(string name, TimeSpan duration)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return "Give it a name to save it.";
        if (name.Length > 30) return "Name: 30 characters max.";
        if (duration < DurationText.Min || duration > DurationText.Max) return "Duration must be between 1 second and 24 hours.";
        var existing = TimerPresets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { existing.Seconds = (int)duration.TotalSeconds; return null; }
        if (TimerPresets.Count >= MaxTimerPresets) return $"Up to {MaxTimerPresets} saved timers.";
        TimerPresets.Add(new TimerPreset { Name = name, Seconds = (int)duration.TotalSeconds });
        return null;
    }

    internal static string Categories_NewId() => "c" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>Pone la tarea al principio de las recientes (máx. 10, sin duplicados).</summary>
    public void RememberTask(string task)
    {
        task = (task ?? "").Trim();
        LastTask = task;
        if (task.Length == 0) return;
        RecentTasks.RemoveAll(t => string.Equals(t, task, StringComparison.OrdinalIgnoreCase));
        RecentTasks.Insert(0, task);
        if (RecentTasks.Count > MaxRecentTasks) RecentTasks.RemoveRange(MaxRecentTasks, RecentTasks.Count - MaxRecentTasks);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ActiveCheckpoint))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;

public static class SettingsStore
{
    public static AppSettings Load(string path)
    {
        AppSettings? s = null;
        if (File.Exists(path))
        {
            try { s = JsonSerializer.Deserialize(File.ReadAllText(path), CoreJsonContext.Default.AppSettings); }
            catch (JsonException)
            {
                // No se pierde: se guarda una copia del archivo dañado y se usan valores por defecto.
                try { File.Copy(path, path + ".bad", overwrite: true); } catch (IOException) { }
            }
        }
        s ??= new AppSettings();
        s.Normalize();
        return s;
    }

    /// <summary>Escritura atómica: archivo temporal y luego reemplazo.</summary>
    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(settings, CoreJsonContext.Default.AppSettings));
    }
}

internal static class AtomicFile
{
    public static void WriteAllText(string path, string text)
    {
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
        {
            w.Write(text);
            w.Flush();
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
