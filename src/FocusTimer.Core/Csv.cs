using System.Text;

namespace FocusTimer.Core;

/// <summary>CSV mínimo según RFC 4180 (comillas, comas y saltos de línea dentro de campos).</summary>
public static class Csv
{
    public static string Escape(string value)
    {
        value ??= "";
        bool needsQuotes = value.Length > 0 &&
            (value.IndexOfAny([',', '"', '\r', '\n']) >= 0 || char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string FormatRow(IEnumerable<string> fields) => string.Join(",", fields.Select(Escape));

    /// <summary>Lee todas las filas. Tolera CRLF/LF y una última línea sin salto.</summary>
    public static IEnumerable<List<string>> Parse(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false, any = false;
        int c;
        while ((c = reader.Read()) != -1)
        {
            char ch = (char)c;
            any = true;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    yield return row;
                    row = []; any = false;
                    break;
                default: field.Append(ch); break;
            }
        }
        if (any)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
