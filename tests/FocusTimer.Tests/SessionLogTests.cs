using FocusTimer.Core;

namespace FocusTimer.Tests;

public class SessionLogTests
{
    private static SessionRecord Rec(string task) =>
        new(SessionRecord.NewId(), new DateTime(2026, 11, 24, 9, 0, 0), new DateTime(2026, 11, 24, 9, 25, 0), 25, 25, "Work", task, true);

    [Fact]
    public void Locked_main_file_goes_to_pending_then_merges()
    {
        using var dir = new TempDir();
        var log = new SessionLog(new DataPaths(dir.Path));
        Assert.True(log.Append(Rec("a")));

        using (new FileStream(log.Main.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) // "Excel" lo bloquea
        {
            Assert.False(log.Append(Rec("b")));
            Assert.True(log.HasPending);
            Assert.False(log.TryFlushPending());
        }
        Assert.Equal(["a", "b"], log.ReadAll().Select(r => r.Task));
        Assert.True(log.Append(Rec("c"))); // fusiona primero las pendientes
        Assert.False(log.HasPending);
        Assert.Equal(["a", "b", "c"], log.Main.ReadAll().Select(r => r.Task));
    }

    [Fact]
    public void Flush_does_not_duplicate_rows()
    {
        using var dir = new TempDir();
        var log = new SessionLog(new DataPaths(dir.Path));
        var r = Rec("x");
        log.Main.Append(r);
        log.Pending.Append(r);
        Assert.Equal(0, log.FlushPending());
        Assert.Single(log.Main.ReadAll());
    }
}
