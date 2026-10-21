using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using OricOffice.Core.Model;
using OricOffice.Core.Storage;

namespace OricOffice.Core.Export;

/// <summary>
/// Reads OpenDocument packages: .odt into Author's text/image/table sections and
/// .ods into Calc's sheet. Tolerant of real-world LibreOffice output: repeated
/// empty rows/columns, covered cells, inline (base64) images, and OpenFormula
/// references are all handled. Text-level fidelity only — character styles,
/// lists' bullet glyphs and page layout are not preserved.
/// </summary>
public static class OdfImporter
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";

    private const string TextMime = "application/vnd.oasis.opendocument.text";
    private const string SheetMime = "application/vnd.oasis.opendocument.spreadsheet";

    /// <summary>Imports an .odt (Writer) or .ods (Calc) file into the unified document.</summary>
    public static OricFile Import(string path)
    {
        using var zip = OpenPackage(path);
        return MimeType(zip) switch
        {
            TextMime => ImportOdt(path),
            SheetMime => ImportOds(path),
            var mime => throw new OricFileException(
                mime is null
                    ? "That file is not an OpenDocument (.odt/.ods) package."
                    : $"Unsupported OpenDocument type '{mime}' — expected text or spreadsheet."),
        };
    }

    public static OricFile ImportOdt(string path)
    {
        using var zip = OpenPackage(path);
        RequireMime(zip, TextMime);

        var file = OricFile.CreateNew(OricFileKind.Author, ReadTitle(zip, path));
        var content = ReadXml(zip, "content.xml");
        var body = content.Root?.Element(Office + "body")?.Element(Office + "text");
        if (body is null)
            throw new OricFileException("That document has no text body.");

        // Paragraphs (including list items), in document order, skipping tables
        // and floating shapes. Headings count as paragraphs for our flat model.
        var paragraphs = body.Descendants()
            .Where(e => e.Name == Text + "p" || e.Name == Text + "h")
            .Where(e => !e.Ancestors(Table + "table").Any())
            .Where(e => !e.Ancestors(Draw + "text-box").Any())
            .Where(e => !e.Ancestors(Text + "note").Any())
            // Paragraphs that exist only to carry an image are not blank lines.
            .Where(e => e.Descendants(Draw + "image").Any() == false
                        || ParagraphText(e).Length > 0);
        file.Text.Body = string.Join("\n", paragraphs.Select(ParagraphText));

        // Tables.
        foreach (var table in body.Descendants(Table + "table"))
            file.Text.Tables.Add(ReadTableBlock(table));

        // Images (href entry or inline base64), document order, outside tables.
        foreach (var image in body.Descendants(Draw + "image"))
        {
            if (image.Ancestors(Table + "table").Any()) continue;
            var bytes = ReadImageData(zip, image, out var name);
            if (bytes is not null && ImageDetector.IsSupported(bytes))
                file.Text.Images.Add(new DocumentImage(name, bytes));
        }

        return file;
    }

    public static OricFile ImportOds(string path)
    {
        using var zip = OpenPackage(path);
        RequireMime(zip, SheetMime);

        var file = OricFile.CreateNew(OricFileKind.Calc, ReadTitle(zip, path));
        var content = ReadXml(zip, "content.xml");
        var sheetBody = content.Root?.Element(Office + "body")?.Element(Office + "spreadsheet");
        if (sheetBody is null)
            throw new OricFileException("That file has no spreadsheet body.");

        var table = sheetBody.Descendants(Table + "table").FirstOrDefault();
        if (table is null)
            throw new OricFileException("That spreadsheet contains no sheet.");

        var name = table.Attribute(Table + "name")?.Value;
        if (!string.IsNullOrWhiteSpace(name)) file.Sheet.Name = name;

        // Rows, with repeated empty rows/columns collapsed by trailing-trim.
        var rows = new List<List<(string Raw, string Display, string? Formula)>>();
        foreach (var row in table.Elements(Table + "table-row"))
        {
            var repeat = Repeat(row, Table + "number-rows-repeated");
            var cells = ReadSheetRow(row);
            // Trailing empty cells never matter; interior positions do.
            while (cells.Count > 0)
            {
                var last = cells[^1];
                if (last.Raw.Length != 0 || last.Display.Length != 0 || last.Formula is not null) break;
                cells.RemoveAt(cells.Count - 1);
            }

            if (cells.Count == 0)
            {
                if (repeat <= 4) // keep small interior blank rows
                    for (int i = 0; i < repeat; i++) rows.Add(new List<(string, string, string?)>());
                continue;
            }
            for (int i = 0; i < repeat && rows.Count < 4096; i++) rows.Add(cells);
            if (rows.Count >= 4096) break;
        }
        while (rows.Count > 0 && rows[^1].Count == 0) rows.RemoveAt(rows.Count - 1);

        var maxColumns = rows.Count == 0 ? 0 : Math.Max(rows.Max(r => r.Count), SheetDocument.MinColumns);
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < rows[r].Count; c++)
            {
                var (raw, display, formula) = rows[r][c];
                if (formula is not null)
                    raw = "=" + formula;
                if (raw.Length == 0 && display.Length == 0) continue;
                file.Sheet.Set(r, c, raw, display);
            }
        }
        file.Sheet.EnsureCapacity(Math.Max(rows.Count, SheetDocument.MinRows), maxColumns);

        // Floating images, anchored to their cell.
        foreach (var image in sheetBody.Descendants(Draw + "image"))
        {
            var cell = image.Ancestors(Table + "table-cell").FirstOrDefault();
            if (cell is null) continue;
            var bytes = ReadImageData(zip, image, out var fileName);
            if (bytes is null || !ImageDetector.IsSupported(bytes)) continue;

            var position = LocateCell(table, cell);
            if (position is null) continue;
            var (row, column) = position.Value;

            var (w, h) = ReadLengthPair(image);
            file.Sheet.Images.Add(new SheetImage(fileName, bytes, row, column)
            {
                Width = w,
                Height = h,
            });
        }

        return file;
    }

    /// <summary>Reverse of <see cref="OdfExporter.TransformFormula"/>: "of:=SUM([.B2:.B3])" → "SUM(B2:B3)".</summary>
    public static string ReverseTransformFormula(string ofBody)
    {
        var s = ofBody;
        if (s.StartsWith("of:=", StringComparison.Ordinal)) s = s[4..];
        if (s.StartsWith('=')) return s[1..]; // already our syntax, without '='

        // OpenFormula references: [.A1], [.$A$1:.$A$5], [:.[…] variants.
        return s.Replace("[.", "")
                .Replace(":.", ":")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("$", "");
    }

    // ---------------------------------------------------------------- paragraphs

    private static string ParagraphText(XElement paragraph)
    {
        var sb = new StringBuilder();
        CollectText(paragraph, sb);
        return sb.ToString().Trim();
    }

    private static void CollectText(XElement element, StringBuilder sb)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;
                case XElement child when child.Name == Text + "line-break":
                    sb.Append('\n');
                    break;
                case XElement child when child.Name == Text + "tab":
                    sb.Append('\t');
                    break;
                case XElement child when child.Name == Text + "s":
                    var count = int.TryParse(child.Attribute(Text + "c")?.Value, out var c) ? c : 1;
                    sb.Append(' ', Math.Min(Math.Max(count, 1), 40));
                    break;
                case XElement child when child.Name == Text + "a" || child.Name == Text + "span"
                        || child.Name == Text + "em" || child.Name == Text + "strong":
                    CollectText(child, sb);
                    break;
                case XElement child when child.Name == Text + "p" || child.Name == Text + "h":
                    // Nested paragraph (rare) — treat as line break + content.
                    if (sb.Length > 0) sb.Append('\n');
                    CollectText(child, sb);
                    break;
                default:
                    break; // frames, bookmarks, fields: no text contribution
            }
        }
    }

    // ---------------------------------------------------------------- tables

    private static TableBlock ReadTableBlock(XElement table)
    {
        var name = table.Attribute(Table + "name")?.Value;
        var rows = new List<(string[] Text, string?[] Fill)>();

        foreach (var row in table.Elements(Table + "table-row"))
        {
            var repeat = Repeat(row, Table + "number-rows-repeated");
            var text = new List<string>();
            var fill = new List<string?>();

            foreach (var cell in row.Elements().Where(e =>
                         e.Name == Table + "table-cell" || e.Name == Table + "covered-table-cell"))
            {
                var colRepeat = Math.Min(Repeat(cell, Table + "number-columns-repeated"), 64);
                var content = string.Join("\n", cell.Elements(Text + "p").Select(ParagraphText));
                var background = cell.Attribute(Fo + "background-color")?.Value;
                if (string.IsNullOrWhiteSpace(background)) background = null;

                for (int i = 0; i < colRepeat; i++)
                {
                    text.Add(content);
                    fill.Add(background);
                    if (content.Length == 0 && background is null && text.Count >= 256) break;
                }
            }

            while (text.Count > 0 && text[^1].Length == 0 && fill[^1] is null)
            {
                text.RemoveAt(text.Count - 1);
                fill.RemoveAt(fill.Count - 1);
            }

            if (text.Count == 0)
            {
                if (repeat <= 4)
                    for (int i = 0; i < repeat; i++) rows.Add((Array.Empty<string>(), Array.Empty<string?>()));
                continue;
            }

            for (int i = 0; i < repeat && rows.Count < 512; i++)
                rows.Add((text.ToArray(), fill.ToArray()));
            if (rows.Count >= 512) break;
        }
        while (rows.Count > 0 && rows[^1].Text.Length == 0) rows.RemoveAt(rows.Count - 1);

        var columns = Math.Max(rows.Count == 0 ? 0 : rows.Max(r => r.Text.Length), TableBlock.MinColumns);
        var grid = new string[rows.Count][];
        var fills = new string?[rows.Count][];
        for (int r = 0; r < rows.Count; r++)
        {
            grid[r] = new string[columns];
            fills[r] = new string?[columns];
            for (int c = 0; c < columns; c++)
            {
                grid[r][c] = c < rows[r].Text.Length ? rows[r].Text[c] : string.Empty;
                fills[r][c] = c < rows[r].Fill.Length ? rows[r].Fill[c] : null;
            }
        }

        var block = new TableBlock(name ?? "Table", Math.Max(rows.Count, 1), columns);
        if (rows.Count > 0) block.Restore(grid, fills, name);
        return block;
    }

    private static List<(string Raw, string Display, string? Formula)> ReadSheetRow(XElement row)
    {
        var cells = new List<(string Raw, string Display, string? Formula)>();
        foreach (var cell in row.Elements(Table + "table-cell"))
        {
            var repeat = Math.Min(Repeat(cell, Table + "number-columns-repeated"), 256);

            var type = cell.Attribute(Office + "value-type")?.Value;
            var text = string.Join("\n", cell.Elements(Text + "p").Select(ParagraphText));
            string raw, display, formula = string.Empty;

            var ofFormula = cell.Attribute(Table + "formula")?.Value;
            if (ofFormula is not null) formula = ReverseTransformFormula(ofFormula);

            switch (type)
            {
                case "float" or "percentage" or "currency":
                    raw = cell.Attribute(Office + "value")?.Value ?? text;
                    display = raw;
                    break;
                case "date":
                    raw = cell.Attribute(Office + "date-value")?.Value ?? text;
                    display = raw;
                    break;
                case "time":
                    raw = cell.Attribute(Office + "time-value")?.Value ?? text;
                    display = text.Length > 0 ? text : raw;
                    break;
                case "boolean":
                    raw = cell.Attribute(Office + "boolean-value")?.Value ?? text;
                    display = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE";
                    break;
                default: // string or unknown
                    raw = text;
                    display = text;
                    break;
            }

            // A formula cell: store the reversed formula as raw input and the
            // document's computed value as the display (the VM recalculates anyway).
            if (formula.Length > 0)
            {
                raw = formula;
                display = cell.Attribute(Office + "value")?.Value ?? text;
            }

            var entry = (raw, display, formula.Length > 0 ? formula : null);
            for (int i = 0; i < repeat; i++) cells.Add(entry);
        }
        return cells;
    }

    private static (int Row, int Column)? LocateCell(XElement table, XElement target)
    {
        int rowIndex = 0;
        foreach (var row in table.Elements(Table + "table-row"))
        {
            int columnIndex = 0;
            foreach (var cell in row.Elements().Where(e =>
                         e.Name == Table + "table-cell" || e.Name == Table + "covered-table-cell"))
            {
                var repeat = Math.Min(Repeat(cell, Table + "number-columns-repeated"), 64);
                if (ReferenceEquals(cell, target))
                    return (rowIndex, columnIndex);
                columnIndex += repeat;
                if (columnIndex > 1024) break;
            }
            rowIndex++;
            if (rowIndex > 4096) break;
        }
        return null;
    }

    // ---------------------------------------------------------------- images

    private static byte[]? ReadImageData(ZipArchive zip, XElement image, out string name)
    {
        name = "image.png";
        var href = image.Attribute(Xlink + "href")?.Value;
        if (!string.IsNullOrWhiteSpace(href))
        {
            var entryName = Uri.UnescapeDataString(href.TrimStart('/'))
                .Replace("./", string.Empty);
            name = Path.GetFileName(entryName);
            var entry = FindEntry(zip, entryName);
            if (entry is null) return null;
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        var inline = image.Element(Office + "binary-data")?.Value;
        if (string.IsNullOrWhiteSpace(inline)) return null;
        try
        {
            var bytes = Convert.FromBase64String(inline.Replace("\n", "").Replace("\r", ""));
            name = "image.png";
            return bytes;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static (int Width, int Height) ReadLengthPair(XElement frame)
    {
        var w = LengthPx(frame.Attribute(Svg + "width")?.Value
            ?? frame.Attributes().FirstOrDefault(a => a.Name.LocalName == "width")?.Value);
        var h = LengthPx(frame.Attribute(Svg + "height")?.Value
            ?? frame.Attributes().FirstOrDefault(a => a.Name.LocalName == "height")?.Value);
        return (w > 0 ? w : 160, h > 0 ? h : 120);
    }

    private static int LengthPx(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var number = value.Trim();
        var unit = "px";
        foreach (var suffix in new[] { "cm", "mm", "in", "pt", "pc", "px" })
        {
            if (number.EndsWith(suffix, StringComparison.Ordinal))
            {
                unit = suffix;
                number = number[..^suffix.Length];
                break;
            }
        }
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return 0;
        return (int)Math.Round(unit switch
        {
            "cm" => n * 96.0 / 2.54,
            "mm" => n * 96.0 / 25.4,
            "in" => n * 96.0,
            "pt" => n * 96.0 / 72.0,
            "pc" => n * 96.0 / 6.0,
            _ => n,
        });
    }

    // ---------------------------------------------------------------- package helpers

    private static ZipArchive OpenPackage(string path)
    {
        try
        {
            return new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new OricFileException($"{Path.GetFileName(path)} is not a valid OpenDocument package.", ex);
        }
        catch (IOException ex)
        {
            throw new OricFileException($"Cannot read {Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    private static string? MimeType(ZipArchive zip)
    {
        var entry = FindEntry(zip, "mimetype");
        if (entry is null) return null;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return reader.ReadToEnd().Trim();
    }

    private static void RequireMime(ZipArchive zip, string expected)
    {
        var actual = MimeType(zip);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new OricFileException(
                $"Expected an {expected[(expected.LastIndexOf('.') + 1)..]} file, but the package says '{actual}'.");
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string name)
        => zip.GetEntry(name)
           ?? zip.Entries.FirstOrDefault(e =>
               string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));

    private static XDocument ReadXml(ZipArchive zip, string name)
    {
        var entry = FindEntry(zip, name) ?? throw new OricFileException($"The package has no {name}.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ReadTitle(ZipArchive zip, string path)
    {
        try
        {
            var entry = FindEntry(zip, "meta.xml");
            if (entry is not null)
            {
                using var stream = entry.Open();
                var title = XDocument.Load(stream).Root?
                    .Element(Office + "meta")?.Element(Dc + "title")?.Value;
                if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
            }
        }
        catch
        {
            // Unreadable metadata never blocks an import.
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static int Repeat(XElement element, XName attribute)
        => int.TryParse(element.Attribute(attribute)?.Value, out var n) && n > 0 ? n : 1;
}
