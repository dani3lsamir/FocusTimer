using System.Text;
using FocusTimer.Core;

namespace FocusTimer.Tests;

public class SessionStoreTests
{
    private static SessionRecord Rec(string task, string cat = "Work", bool done = true, double actual = 25) =>
        new(SessionRecord.NewId(), new DateTime(2026, 11, 24, 9, 0, 0), new DateTime(2026, 11, 24, 9, 25, 0),
            25, actual, cat, task, done);

    [Theory]
    [InlineData("simple")]
    [InlineData("con, coma")]
    [InlineData("con \"comillas\" dobles")]
    [InlineData("acentos: áéíóú ñ Ü ¿¡ 中文")]
    [InlineData("multi\r\nlínea\ny más")]
    [InlineData("  espacios  ")]
    [InlineData("")]
    [InlineData("\"")]
    [InlineData(",,,")]
    public void Round_trip_preserves_task_text(string task)
    {
        using var dir = new TempDir();
        var store = new SessionStore(dir.File("sessions.csv"));
        var r = Rec(task, cat: "Ñandú, \"cat\"");
        store.Append(r);
        var back = Assert.Single(store.ReadAll());
        Assert.Equal(r, back);
    }

    [Fact]
    public void Appends_never_rewrite_existing_rows()
    {
        using var dir = new TempDir();
        var path = dir.File("sessions.csv");
        var store = new SessionStore(path);
        store.Append(Rec("uno"));
        var before = File.ReadAllBytes(path);
        store.Append(Rec("dos", done: false, actual: 7.25));
        var after = File.ReadAllBytes(path);
        Assert.Equal(before, after.Take(before.Length));
        var all = store.ReadAll();
        Assert.Equal(["uno", "dos"], all.Select(x => x.Task));
        Assert.False(all[1].Completed);
        Assert.Equal(7.25, all[1].ActualMin);
    }

    [Fact]
    public void Writes_header_once_with_bom_and_crlf()
    {
        using var dir = new TempDir();
        var path = dir.File("sessions.csv");
        var store = new SessionStore(path);
        store.Append(Rec("a"));
        store.Append(Rec("b"));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith(SessionStore.Header + "\r\n", text);
        Assert.Equal(1, CountOf(text, "id,start_local"));
        Assert.EndsWith("\r\n", text);
    }

    [Fact]
    public void Survives_truncated_last_line_from_a_crash()
    {
        using var dir = new TempDir();
        var path = dir.File("sessions.csv");
        var store = new SessionStore(path);
        store.Append(Rec("ok"));
        File.AppendAllText(path, "abc,2026-11-24 10:0"); // fila a medias
        store.Append(Rec("después"));
        var all = store.ReadAll(out int skipped);
        Assert.Equal(["ok", "después"], all.Select(x => x.Task));
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void Missing_file_reads_empty()
    {
        using var dir = new TempDir();
        Assert.Empty(new SessionStore(dir.File("nope.csv")).ReadAll());
    }

    [Fact]
    public void Reads_by_header_name_so_future_columns_are_ok()
    {
        using var dir = new TempDir();
        var path = dir.File("sessions.csv");
        File.WriteAllText(path,
            "id,start_local,end_local,planned_min,actual_min,category,task,completed,notes\n" +
            "x1,2026-11-24 09:00:00,2026-11-24 09:25:00,25,25,Trabajo,t,true,\"nota, larga\"\n");
        var r = Assert.Single(new SessionStore(path).ReadAll());
        Assert.Equal("x1", r.Id);
        Assert.True(r.Completed);
    }

    [Fact]
    public void Number_format_is_invariant()
    {
        Assert.Contains(",12.5,", SessionStore.FormatRow(Rec("t", actual: 12.5)));
    }

    private static int CountOf(string s, string sub)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
        return n;
    }
}
