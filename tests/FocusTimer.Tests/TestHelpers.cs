using FocusTimer.Core;

namespace FocusTimer.Tests;

internal sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset Now { get; set; } = start;
    public void Advance(TimeSpan t) => Now += t;
    public void AdvanceMin(double m) => Now += TimeSpan.FromMinutes(m);
}

/// <summary>Carpeta temporal que se borra al terminar el test.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ft-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}
