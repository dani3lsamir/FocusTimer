using FocusTimer.Core;

namespace FocusTimer.Tests;

public class CheckpointTests
{
    private static ActiveCheckpoint Cp(string id = "abc") => new(id,
        new DateTime(2026, 11, 24, 9, 0, 0), new DateTime(2026, 11, 24, 9, 12, 30), 25, 12.5, "Trabajo", "tarea, \"x\"");

    [Fact]
    public void Recover_appends_incomplete_session_and_clears()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.File("sessions.csv"));
        var cps = new CheckpointStore(dir.File("current.json"));
        cps.Save(Cp());
        var rec = cps.Recover(store);
        Assert.NotNull(rec);
        Assert.False(File.Exists(cps.Path));
        var r = Assert.Single(store.ReadAll());
        Assert.False(r.Completed);
        Assert.Equal(12.5, r.ActualMin);
        Assert.Equal(new DateTime(2026, 11, 24, 9, 12, 30), r.End);
        Assert.Equal("tarea, \"x\"", r.Task);
    }

    [Fact]
    public void Recover_does_not_duplicate_a_row_already_written()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.File("sessions.csv"));
        var cps = new CheckpointStore(dir.File("current.json"));
        store.Append(new SessionRecord("abc", DateTime.Today, DateTime.Today, 25, 25, "Trabajo", "", true));
        cps.Save(Cp("abc"));
        Assert.Null(cps.Recover(store));
        Assert.Single(store.ReadAll());
        Assert.False(File.Exists(cps.Path));
    }

    [Fact]
    public void Recover_without_checkpoint_does_nothing()
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.File("sessions.csv"));
        Assert.Null(new CheckpointStore(dir.File("current.json")).Recover(store));
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public void Corrupt_checkpoint_is_discarded()
    {
        using var dir = new TempDir();
        var cps = new CheckpointStore(dir.File("current.json"));
        File.WriteAllText(cps.Path, "{roto");
        Assert.Null(cps.Recover(new SessionStore(dir.File("sessions.csv"))));
        Assert.False(File.Exists(cps.Path));
    }
}
