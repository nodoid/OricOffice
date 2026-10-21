using System.Text;

namespace OricOffice.Core.IO;

/// <summary>
/// RFC-4180-style CSV: comma separators, quoted fields with doubled quotes,
/// embedded newlines inside quotes, tolerant of CRLF/LF/CR line endings.
/// </summary>
public static class Csv
{
    /// <summary>Parses a whole CSV document into rows of fields.</summary>
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text)) return rows;

        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool fieldWasTouched = false;
        int i = 0;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
            fieldWasTouched = false;
        }

        void EndRow()
        {
            EndField();
            rows.Add(row);
            row = new List<string>();
        }

        while (i < text.Length)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            if (c == '"' && !fieldWasTouched && field.Length == 0)
            {
                inQuotes = true;
                fieldWasTouched = true;
                i++;
                continue;
            }

            if (c == ',')
            {
                EndField();
                i++;
                continue;
            }

            if (c is '\r' or '\n')
            {
                EndRow();
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i += 2;
                else i++;
                continue;
            }

            field.Append(c);
            fieldWasTouched = true;
            i++;
        }

        // Flush the tail unless it is a single empty line produced by a trailing newline.
        if (field.Length > 0 || fieldWasTouched || row.Count > 0 || rows.Count == 0)
            EndRow();

        return rows;
    }

    /// <summary>Writes rows as CSV text (CRLF line endings, trailing newline).</summary>
    public static string Write(IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            for (int c = 0; c < row.Count; c++)
            {
                if (c > 0) builder.Append(',');
                builder.Append(Escape(row[c]));
            }
            builder.Append("\r\n");
        }
        return builder.ToString();
    }

    /// <summary>Quotes a field when it contains a comma, quote, CR or LF.</summary>
    public static string Escape(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
