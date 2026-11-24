using System.Globalization;
using System.Text;

namespace FocusTimer.Core;

/// <summary>
/// sessions.csv: solo se AÑADEN filas (nunca se reescribe el archivo) y cada fila se vuelca a disco
/// (Flush(true)) antes de volver, así un cierre inesperado no pierde filas ya escritas.
/// </summary>
public sealed class SessionStore(string path)
{
    public const string Header = "id,start_local,end_local,planned_min,actual_min,category,task,completed";
    public const string DateFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string Path { get; } = path;

    public void Append(SessionRecord r)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        using var fs = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var sb = new StringBuilder();
        if (fs.Length == 0)
        {
            fs.Write(Encoding.UTF8.GetPreamble()); // BOM: Excel muestra bien las tildes
            sb.Append(Header).Append("\r\n");
        }
        else
        {
            // Si un cierre dejó una línea a medias, empezamos en una línea nueva.
            fs.Seek(-1, SeekOrigin.End);
            if (fs.ReadByte() != '\n') sb.Append("\r\n");
        }
        fs.Seek(0, SeekOrigin.End);
        sb.Append(FormatRow(r)).Append("\r\n");
        fs.Write(Utf8NoBom.GetBytes(sb.ToString()));
        fs.Flush(flushToDisk: true);
    }

    public static string FormatRow(SessionRecord r) => Csv.FormatRow([
        r.Id,
        r.Start.ToString(DateFormat, Inv),
        r.End.ToString(DateFormat, Inv),
        r.PlannedMin.ToString(Inv),
        r.ActualMin.ToString("0.##", Inv),
        r.Category,
        r.Task,
        r.Completed ? "true" : "false",
    ]);

    /// <summary>Lee todas las sesiones. Las filas dañadas se saltan (y se cuentan), nunca se borran.</summary>
    public List<SessionRecord> ReadAll() => ReadAll(out _);

    public List<SessionRecord> ReadAll(out int skippedRows)
    {
        skippedRows = 0;
        var result = new List<SessionRecord>();
        if (!File.Exists(Path)) return result;

        using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        Dictionary<string, int>? cols = null;
        foreach (var row in Csv.Parse(reader))
        {
            if (row.Count == 1 && row[0].Length == 0) continue; // línea vacía
            if (cols is null)
            {
                cols = new(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < row.Count; i++) cols[row[i].Trim()] = i;
                continue;
            }
            if (TryParse(row, cols, out var rec)) result.Add(rec);
            else skippedRows++;
        }
        return result;
    }

    private static bool TryParse(List<string> row, Dictionary<string, int> cols, out SessionRecord rec)
    {
        rec = null!;
        string? Get(string name) => cols.TryGetValue(name, out var i) && i < row.Count ? row[i] : null;
        try
        {
            var id = Get("id");
            if (string.IsNullOrEmpty(id)) return false;
            if (!DateTime.TryParseExact(Get("start_local"), DateFormat, Inv, DateTimeStyles.None, out var start)) return false;
            if (!DateTime.TryParseExact(Get("end_local"), DateFormat, Inv, DateTimeStyles.None, out var end)) return false;
            if (!int.TryParse(Get("planned_min"), NumberStyles.Integer, Inv, out var planned)) return false;
            if (!double.TryParse(Get("actual_min"), NumberStyles.Float, Inv, out var actual)) return false;
            if (!bool.TryParse(Get("completed"), out var completed)) return false;
            rec = new SessionRecord(id, start, end, planned, actual, Get("category") ?? "", Get("task") ?? "", completed);
            return true;
        }
        catch (FormatException) { return false; }
    }
}
