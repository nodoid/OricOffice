using System.Globalization;
using System.IO.Compression;
using System.Text;
using OricOffice.Core.Model;
using OricOffice.Core.Storage;

namespace OricOffice.Core.Export;

/// <summary>
/// Reads LibreOffice Base .odb packages by parsing the embedded HSQLDB script
/// (<c>database/script</c>) — the SQL replay text that holds schema and, for
/// MEMORY tables (LibreOffice's default), all rows. The first table in the
/// script becomes the document's table. Rows stored only in binary cached-data
/// files are outside the script and therefore not recoverable here.
/// </summary>
public static class OdbImporter
{
    public static OricFile Import(string path)
    {
        var file = OricFile.CreateNew(OricFileKind.Base, Path.GetFileNameWithoutExtension(path));
        var script = ReadScript(path);
        ParseScript(script, file.Table);
        if (file.Table.Fields.Count == 0)
            throw new OricFileException(
                $"{Path.GetFileName(path)} contains no table definitions in its embedded database script.");
        return file;
    }

    /// <summary>Exposed for tests: parses an HSQLDB 1.8 script into the table.</summary>
    internal static void ParseScript(string script, TableDocument table)
    {
        foreach (var statement in SplitStatements(script))
        {
            if (table.Fields.Count > 0 && !statement.StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase))
                continue; // first table only; ignore everything after it starts

            if (statement.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase) &&
                statement.Contains(" TABLE ", StringComparison.OrdinalIgnoreCase))
            {
                ReadCreateTable(statement, table);
            }
            else if (statement.StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase))
            {
                ReadInsert(statement, table);
            }
        }
    }

    // ---------------------------------------------------------------- statements

    /// <summary>
    /// Splits the replay script into statements: one per line, except that
    /// newlines inside single-quoted literals are part of the value.
    /// </summary>
    internal static IEnumerable<string> SplitStatements(string script)
    {
        var lines = script.Replace("\r\n", "\n").Split('\n');
        var buffer = new StringBuilder();

        foreach (var line in lines)
        {
            if (buffer.Length == 0)
            {
                buffer.Append(line);
            }
            else
            {
                buffer.Append('\n').Append(line);
            }

            if (!EndsOutsideQuotes(buffer)) continue;
            var statement = buffer.ToString().Trim();
            buffer.Clear();
            if (statement.Length > 0) yield return statement;
        }

        if (buffer.Length > 0)
        {
            var statement = buffer.ToString().Trim();
            if (statement.Length > 0) yield return statement;
        }
    }

    private static bool EndsOutsideQuotes(StringBuilder statement)
    {
        bool inQuote = false;
        for (int i = 0; i < statement.Length; i++)
        {
            var c = statement[i];
            if (c == '\'')
            {
                if (inQuote && i + 1 < statement.Length && statement[i + 1] == '\'')
                {
                    i++; // escaped '' stays inside the literal
                    continue;
                }
                inQuote = !inQuote;
            }
        }
        return !inQuote;
    }

    // ---------------------------------------------------------------- CREATE TABLE

    private static void ReadCreateTable(string statement, TableDocument table)
    {
        // CREATE MEMORY TABLE PUBLIC."Name" ("Col" VARCHAR(64), …)
        var open = statement.IndexOf('(');
        if (open < 0) return;
        var head = statement[..open];
        var body = statement[(open + 1)..].TrimEnd();
        if (body.EndsWith(')')) body = body[..^1];

        var name = ExtractIdentifier(head[(head.LastIndexOf(" TABLE ", StringComparison.OrdinalIgnoreCase) + 7)..]);
        if (table.Fields.Count > 0) return; // first table only
        table.Name = string.IsNullOrWhiteSpace(name) ? "Table1" : name;

        foreach (var rawColumn in SplitTopLevel(body))
        {
            var column = rawColumn.Trim();
            if (column.Length == 0) continue;

            var (columnName, sqlType) = SplitColumnDefinition(column);
            if (string.Equals(columnName, "ID", StringComparison.OrdinalIgnoreCase) &&
                sqlType.Contains("IDENTITY", StringComparison.OrdinalIgnoreCase))
                continue; // the engine's row identifier, not a user field

            table.Fields.Add(new FieldDef(columnName, MapType(sqlType)));
        }
    }

    private static (string Name, string Type) SplitColumnDefinition(string column)
    {
        var depth = 0;
        bool inQuote = false;
        int split = -1;
        for (int i = 0; i < column.Length; i++)
        {
            var c = column[i];
            if (c == '"')
            {
                if (inQuote && i + 1 < column.Length && column[i + 1] == '"') { i++; continue; }
                inQuote = !inQuote;
                continue;
            }
            if (inQuote) continue;
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (depth == 0 && char.IsWhiteSpace(c)) { split = i; break; }
        }
        if (split < 0) return (Unquote(column.Trim()), string.Empty);
        return (Unquote(column[..split].Trim()), column[(split + 1)..].Trim());
    }

    private static FieldType MapType(string sqlType)
    {
        var t = sqlType.ToUpperInvariant();
        if (t.StartsWith("DECIMAL") || t.StartsWith("NUMERIC") || t.StartsWith("DOUBLE") ||
            t.StartsWith("FLOAT") || t.StartsWith("REAL") || t.StartsWith("INT"))
            return FieldType.Number;
        if (t.StartsWith("DATE") || t.StartsWith("TIMESTAMP"))
            return FieldType.Date;
        return FieldType.Text;
    }

    // ---------------------------------------------------------------- INSERT

    private static void ReadInsert(string statement, TableDocument table)
    {
        if (table.Fields.Count == 0) return;

        var marker = statement.IndexOf("VALUES(", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return;
        var open = marker + "VALUES(".Length - 1; // index of the '('
        var body = statement[(open + 1)..].Trim();
        if (body.EndsWith(')')) body = body[..^1];

        var values = SplitTopLevel(body).Select(UnquoteLiteral).ToList();
        if (values.Count == 0) return;

        // Scripted inserts carry the ID first, then user columns in order.
        long id = 0;
        long.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
        var record = new RecordRow { Id = id, Ordinal = table.Records.Count };

        for (int i = 0; i < table.Fields.Count; i++)
        {
            var sourceIndex = i + 1;
            var value = sourceIndex < values.Count ? values[sourceIndex] : string.Empty;
            record.Set(table.Fields[i].Name, value);
        }
        table.Records.Add(record);
    }

    // ---------------------------------------------------------------- token helpers

    /// <summary>Comma-split respecting parentheses and both quote styles.</summary>
    internal static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        int depth = 0;
        bool inSingle = false, inDouble = false;

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'' && !inDouble)
            {
                if (inSingle && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    current.Append("''");
                    i++;
                    continue;
                }
                inSingle = !inSingle;
                current.Append(c);
                continue;
            }
            if (c == '"' && !inSingle)
            {
                if (inDouble && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append("\"\"");
                    i++;
                    continue;
                }
                inDouble = !inDouble;
                current.Append(c);
                continue;
            }
            if (!inSingle && !inDouble)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    private static string UnquoteLiteral(string value)
    {
        var v = value.Trim();
        if (v.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (v.Length >= 2 && v[0] == '\'' && v[^1] == '\'')
            return v[1..^1].Replace("''", "'");
        return v;
    }

    private static string Unquote(string identifier)
    {
        var id = identifier.Trim();
        if (id.Length >= 2 && id[0] == '"' && id[^1] == '"')
            return id[1..^1].Replace("\"\"", "\"");
        // Unquoted identifiers are uppercased by HSQLDB — keep as written for display.
        return id;
    }

    private static string ExtractIdentifier(string head)
    {
        var text = head.Trim();
        // Strip optional schema qualifier: PUBLIC."Name" / PUBLIC.Name
        var dot = text.LastIndexOf('.');
        if (dot >= 0) text = text[(dot + 1)..];
        return Unquote(text.Trim());
    }

    // ---------------------------------------------------------------- package

    private static string ReadScript(string path)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new OricFileException($"{Path.GetFileName(path)} is not an .odb package.", ex);
        }

        using (zip)
        {
            var entry = zip.Entries.FirstOrDefault(e =>
                            e.FullName.EndsWith(".script", StringComparison.OrdinalIgnoreCase) ||
                            e.FullName.EndsWith("/script", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.FullName, "script", StringComparison.OrdinalIgnoreCase))
                        ?? throw new OricFileException(
                            $"{Path.GetFileName(path)} has no embedded database script — it may use an external database.");

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
