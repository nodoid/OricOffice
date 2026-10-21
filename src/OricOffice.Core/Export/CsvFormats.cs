using OricOffice.Core.IO;
using OricOffice.Core.Model;

namespace OricOffice.Core.Export;

/// <summary>CSV import/export for the Calc section (one row per line, values shown).</summary>
public static class CalcCsv
{
    /// <summary>Writes the used range as CSV: the computed value where present,
    /// otherwise the raw input (so numbers and text export as typed).</summary>
    public static string Export(SheetDocument sheet)
    {
        var rows = new List<IReadOnlyList<string>>();
        int rowCount = Math.Max(sheet.UsedRowCount, 1);
        int columnCount = Math.Max(sheet.UsedColumnCount, 1);

        for (int r = 0; r < rowCount; r++)
        {
            var row = new List<string>(columnCount);
            for (int c = 0; c < columnCount; c++)
            {
                var cell = sheet.Find(r, c);
                row.Add(cell is null ? string.Empty
                    : cell.Value.Length > 0 ? cell.Value
                    : cell.Raw);
            }
            rows.Add(row);
        }
        return Csv.Write(rows);
    }

    /// <summary>Builds a Calc document from a CSV file (used by File → Import).</summary>
    public static OricFile ImportFile(string path)
    {
        var file = OricFile.CreateNew(OricFileKind.Calc, Path.GetFileNameWithoutExtension(path));
        Import(file.Sheet, File.ReadAllText(path));
        return file;
    }

    /// <summary>Fills a sheet from CSV text; the grid grows to fit and the
    /// formula engine recomputes values on the next apply.</summary>
    public static void Import(SheetDocument sheet, string text)
    {
        sheet.ClearCells();
        var rows = Csv.Parse(text);

        for (int r = 0; r < rows.Count; r++)
        for (int c = 0; c < rows[r].Count; c++)
        {
            var field = rows[r][c];
            if (field.Length == 0) continue;
            sheet.Set(r, c, field, field);
        }

        sheet.EnsureCapacityForCells();
        sheet.EnsureCapacity(SheetDocument.MinRows, SheetDocument.MinColumns);
    }
}

/// <summary>CSV import/export for the Base section (header row + one row per record).</summary>
public static class BaseCsv
{
    /// <summary>Writes the table as CSV: the first line carries the field names.</summary>
    public static string Export(OricFile file)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            file.Table.Fields.Select(f => f.Name).ToList(),
        };

        foreach (var record in file.Table.Records)
            rows.Add(file.Table.Fields.Select(f => record.Get(f.Name)).ToList());

        return Csv.Write(rows);
    }

    /// <summary>Builds a Base document from a CSV file (used by File → Import).</summary>
    public static OricFile ImportFile(string path)
    {
        var file = OricFile.CreateNew(OricFileKind.Base, Path.GetFileNameWithoutExtension(path));
        Import(file, File.ReadAllText(path));
        return file;
    }

    /// <summary>Replaces the table with the CSV content: the first row names the
    /// fields; a field is typed Number when every non-empty value in its column
    /// parses as a decimal number.</summary>
    public static void Import(OricFile file, string text)
    {
        var rows = Csv.Parse(text);
        var table = file.Table;
        table.Fields.Clear();
        table.Records.Clear();
        if (rows.Count == 0) return;

        var header = rows[0];
        int columns = header.Count;
        for (int c = 0; c < columns; c++)
        {
            var name = string.IsNullOrWhiteSpace(header[c]) ? $"Field{c + 1}" : header[c].Trim();
            table.Fields.Add(new FieldDef(name));
        }

        var body = rows.Skip(1).Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
        for (int r = 0; r < body.Count; r++)
        {
            var record = table.AddRecord();
            record.Ordinal = r;
            for (int c = 0; c < columns && c < body[r].Count; c++)
                record.Set(table.Fields[c].Name, body[r][c]);
        }

        // Number inference: a column whose non-empty values are all numeric.
        foreach (var field in table.Fields)
        {
            bool allNumbers = true;
            bool sawValue = false;
            foreach (var record in table.Records)
            {
                var value = record.Get(field.Name);
                if (value.Length == 0) continue;
                sawValue = true;
                if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    allNumbers = false;
                    break;
                }
            }
            if (sawValue && allNumbers)
                field.Type = FieldType.Number;
        }
    }
}
