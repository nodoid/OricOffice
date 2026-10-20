using System.Globalization;
using System.Text;
using OricOffice.Core.Model;

namespace OricOffice.Core.Seeders;

/// <summary>
/// Lets each app open files it did not create:
/// when the section an app edits is empty but the file carries content in another
/// section, that content is converted into the app's own section. Existing sections are
/// never touched, so saving again preserves everything (lossless round-trips).
///
/// Text ↔ sheet conversions use CSV conventions (quoted fields, comma separated) so
/// ordinary text imports cleanly into Calc and exports back out unchanged.
/// </summary>
public static class ContentSeeder
{
    // ------------------------------------------------------------- entry points

    public static void EnsureText(TextDocument target, SheetDocument sheet, TableDocument table)
    {
        if (!target.IsEmpty) return;
        if (!sheet.IsEmpty) target.Body = TextFromSheet(sheet);
        else if (table.Fields.Count > 0 && table.Records.Count > 0) target.Body = TextFromTable(table);
    }

    public static void EnsureSheet(SheetDocument target, TextDocument text, TableDocument table)
    {
        if (!target.IsEmpty) return;
        if (table.Fields.Count > 0 && table.Records.Count > 0) SheetFromTable(target, table);
        else if (!text.IsEmpty) SheetFromText(target, text.Body);
    }

    public static void EnsureTable(TableDocument target, TextDocument text, SheetDocument sheet)
    {
        if (target.Fields.Count > 0) return;
        if (sheet.IsEmpty && text.IsEmpty)
        {
            target.EnsureUsable();
            return;
        }
        if (!sheet.IsEmpty) TableFromSheet(target, sheet);
        else TableFromText(target, text.Body);
        target.EnsureUsable();
    }

    // ---------------------------------------------------------- sheet → text

    public static string TextFromSheet(SheetDocument sheet)
    {
        var sb = new StringBuilder();
        int rows = Math.Max(sheet.UsedRowCount, 1);
        int cols = Math.Max(sheet.UsedColumnCount, 1);

        for (int r = 0; r < rows; r++)
        {
            var values = new string[cols];
            for (int c = 0; c < cols; c++)
                values[c] = sheet.ValueAt(r, c);

            // Trim trailing empty columns; keep interior positions (CSV round-trips).
            int last = values.Length - 1;
            while (last >= 0 && values[last].Length == 0) last--;

            sb.AppendLine(string.Join(", ", values.Take(last + 1).Select(CsvEscape)));
        }
        return sb.ToString().TrimEnd('\r', '\n');
    }

    // ---------------------------------------------------------- table → text

    public static string TextFromTable(TableDocument table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(", ", table.Fields.Select(f => CsvEscape(f.Name))));
        foreach (var record in table.Records)
            sb.AppendLine(string.Join(", ", table.Fields.Select(f => CsvEscape(record.Get(f.Name)))));
        return sb.ToString().TrimEnd('\r', '\n');
    }

    // ---------------------------------------------------------- text → sheet

    public static void SheetFromText(SheetDocument target, string body)
    {
        target.ClearCells();
        var lines = body.Split('\n');
        int row = 0;
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) { row++; continue; }

            var fields = CsvSplit(trimmed);
            for (int c = 0; c < fields.Count; c++)
            {
                if (fields[c].Length > 0)
                    target.Set(row, c, fields[c], fields[c]);
            }
            row++;
        }
        target.EnsureCapacity(Math.Max(row, SheetDocument.MinRows), SheetDocument.MinColumns);
    }

    // ---------------------------------------------------------- table → sheet

    public static void SheetFromTable(SheetDocument target, TableDocument table)
    {
        target.ClearCells();
        for (int c = 0; c < table.Fields.Count; c++)
            target.Set(0, c, table.Fields[c].Name, table.Fields[c].Name);

        for (int r = 0; r < table.Records.Count; r++)
        {
            var record = table.Records[r];
            for (int c = 0; c < table.Fields.Count; c++)
            {
                var value = record.Get(table.Fields[c].Name);
                if (value.Length > 0)
                    target.Set(r + 1, c, value, value);
            }
        }
        target.EnsureCapacity(
            Math.Max(table.Records.Count + 1, SheetDocument.MinRows),
            Math.Max(Math.Max(table.Fields.Count, 1), SheetDocument.MinColumns));
    }

    // ---------------------------------------------------------- sheet → table

    public static void TableFromSheet(TableDocument target, SheetDocument sheet)
    {
        int cols = Math.Max(sheet.UsedColumnCount, 1);
        int rows = sheet.UsedRowCount;

        // Header = row 0; missing/duplicate/empty names get generated unique names.
        var header = new string[cols];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int c = 0; c < cols; c++)
        {
            var name = sheet.ValueAt(0, c).Trim();
            if (name.Length == 0) name = $"Column{c + 1}";
            var unique = name;
            int suffix = 1;
            while (!used.Add(unique))
            {
                suffix++;
                unique = $"{name}{suffix}";
            }
            header[c] = unique;
        }

        for (int c = 0; c < cols; c++)
            target.Fields.Add(new FieldDef(header[c]));

        // Infer types: a column is Number when every non-empty value parses as a number.
        for (int c = 0; c < cols; c++)
        {
            bool sawValue = false;
            bool allNumeric = true;
            for (int r = 1; r < rows; r++)
            {
                var v = sheet.ValueAt(r, c);
                if (v.Length == 0) continue;
                sawValue = true;
                if (!double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    allNumeric = false;
                    break;
                }
            }
            if (sawValue && allNumeric) target.Fields[c].Type = FieldType.Number;
        }

        for (int r = 1; r < rows; r++)
        {
            bool any = false;
            for (int c = 0; c < cols; c++)
                if (sheet.ValueAt(r, c).Length > 0) { any = true; break; }
            if (!any) continue;

            var record = target.AddRecord();
            for (int c = 0; c < cols; c++)
                record.Set(header[c], sheet.ValueAt(r, c));
        }
    }

    // ---------------------------------------------------------- text → table

    public static void TableFromText(TableDocument target, string body)
    {
        var lines = body.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Trim().Length > 0)
            .ToList();

        if (lines.Count == 0)
        {
            target.EnsureUsable();
            return;
        }

        if (lines[0].Contains(','))
        {
            // CSV with a header line.
            var header = CsvSplit(lines[0]);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fieldNames = new List<string>();
            foreach (var h in header)
            {
                var name = h.Trim();
                if (name.Length == 0) name = $"Field{fieldNames.Count + 1}";
                var unique = name;
                int suffix = 1;
                while (!names.Add(unique))
                {
                    suffix++;
                    unique = $"{name}{suffix}";
                }
                fieldNames.Add(unique);
            }
            foreach (var name in fieldNames)
                target.Fields.Add(new FieldDef(name));

            for (int i = 1; i < lines.Count; i++)
            {
                var values = CsvSplit(lines[i]);
                var record = target.AddRecord();
                for (int c = 0; c < fieldNames.Count; c++)
                    record.Set(fieldNames[c], c < values.Count ? values[c] : string.Empty);
            }
        }
        else
        {
            // Free text: one record per line.
            target.Fields.Add(new FieldDef("Line"));
            foreach (var line in lines)
            {
                var record = target.AddRecord();
                record.Set("Line", line.TrimEnd('\r'));
            }
        }
    }

    // ------------------------------------------------------------- CSV helpers

    public static string CsvEscape(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    public static List<string> CsvSplit(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool fieldQuoted = false;
        int i = 0;

        void EndField()
        {
            var s = current.ToString();
            result.Add(fieldQuoted ? s : s.Trim());
            current.Clear();
            fieldQuoted = false;
        }

        while (i < line.Length)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i += 2; continue; }
                    inQuotes = false;
                    i++;
                    continue;
                }
                current.Append(c);
                i++;
                continue;
            }
            if (c == '"' && current.Length == 0 && !fieldQuoted)
            {
                inQuotes = true;
                fieldQuoted = true;
                i++;
                continue;
            }
            if (c == ' ' && current.Length == 0 && !fieldQuoted)
            {
                // Leading padding before an (optional) quoted value or content.
                i++;
                continue;
            }
            if (c == ',')
            {
                EndField();
                i++;
                continue;
            }
            current.Append(c);
            i++;
        }
        EndField();
        return result;
    }
}
