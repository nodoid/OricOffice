using OricOffice.Core.Calc;
using OricOffice.Core.Model;

namespace OricOffice.Core.Export;

/// <summary>Draws ruled tables (with optional coloured cells) across page breaks.</summary>
internal static class PdfTables
{
    public const double LineHeight = 13;
    public const double MinRowHeight = 16;
    public const double CellPadding = 4;

    /// <summary>
    /// Draws an optional header row plus data rows. Returns the Y after the table.
    /// Fills are #RRGGBB strings (or null). Row heights adapt to wrapped text.
    /// </summary>
    public static double Draw(
        PdfWriter pdf,
        string[]? headers,
        IReadOnlyList<string[]> rows,
        IReadOnlyList<string?[]>? fills,
        double x,
        double y,
        double width,
        double top,
        double bottom,
        double fontSize = 10)
    {
        var columns = headers?.Length ?? (rows.Count > 0 ? rows[0].Length : 0);
        if (columns == 0) return y;
        var colWidth = width / columns;
        Func<string, int> lineCount =
            s => PdfWriter.Wrap(s, fontSize, colWidth - 2 * CellPadding).Count;

        double RowHeight(string[] cells)
        {
            var lines = 1;
            foreach (var cell in cells) lines = Math.Max(lines, lineCount(cell ?? string.Empty));
            return Math.Max(MinRowHeight, lines * LineHeight + 4);
        }

        void EnsureSpace(double h)
        {
            if (y + h <= bottom) return;
            pdf.AddPage();
            y = top;
            if (headers is not null)
            {
                y = DrawHeader();
            }
        }

        double DrawHeader()
        {
            var h = RowHeight(headers!);
            pdf.FillRect(x, y, width, h, (0.93, 0.93, 0.93));
            for (int c = 0; c < columns; c++)
                DrawCell(headers![c], x + c * colWidth, y, colWidth, h, bold: true, fill: null);
            pdf.StrokeRect(x, y, width, h, 0.6, (0.55, 0.55, 0.55));
            y += h;
            return y;
        }

        void DrawCell(string text, double cx, double cy, double cw, double ch, bool bold,
            string? fill)
        {
            if (!string.IsNullOrEmpty(fill) && TryRgb(fill, out var rgb))
                pdf.FillRect(cx, cy, cw, ch, rgb);
            pdf.StrokeRect(cx, cy, cw, ch, 0.4, (0.72, 0.72, 0.72));
            var lines = PdfWriter.Wrap(text ?? string.Empty, fontSize, cw - 2 * CellPadding, bold);
            var ty = cy + 3;
            foreach (var line in lines)
            {
                if (ty + LineHeight > cy + ch + 1) break;
                pdf.DrawText(cx + CellPadding, ty, line, fontSize, bold);
                ty += LineHeight;
            }
        }

        if (headers is not null) EnsureSpace(MinRowHeight);
        if (headers is not null) DrawHeader();

        for (int r = 0; r < rows.Count; r++)
        {
            var cells = rows[r];
            var h = RowHeight(cells);
            EnsureSpace(h);
            for (int c = 0; c < columns; c++)
            {
                var fill = fills is not null && r < fills.Count && c < (fills[r]?.Length ?? 0)
                    ? fills[r]?[c]
                    : null;
                var text = c < cells.Length ? cells[c] : string.Empty;
                DrawCell(text, x + c * colWidth, y, colWidth, h, bold: false, fill);
            }
            y += h;
        }

        return y;
    }

    internal static bool TryRgb(string hex, out (double R, double G, double B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.TrimStart('#');
        if (s.Length != 6) return false;
        try
        {
            var r = Convert.ToInt32(s[..2], 16);
            var g = Convert.ToInt32(s.Substring(2, 2), 16);
            var b = Convert.ToInt32(s.Substring(4, 2), 16);
            rgb = (r / 255.0, g / 255.0, b / 255.0);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>PDF export for Author documents: title, body text, images, tables.</summary>
public static class AuthorPdfExporter
{
    private const double Margin = 54;

    public static void Export(OricFile file, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, Render(file));
    }

    public static byte[] Render(OricFile file)
    {
        var pdf = new PdfWriter();
        pdf.AddPage();
        double top = Margin;
        double bottom = PdfWriter.A4Height - Margin;
        double width = PdfWriter.A4Width - 2 * Margin;
        double y = Margin;

        if (!string.IsNullOrWhiteSpace(file.Title) && file.Title != "Untitled")
        {
            pdf.DrawText(Margin, y, file.Title, 18, bold: true);
            y += 30;
        }

        foreach (var paragraph in (file.Text.Body ?? string.Empty).Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                y += 8;
                continue;
            }
            foreach (var line in PdfWriter.Wrap(paragraph, 11, width))
            {
                if (y + 15 > bottom) { pdf.AddPage(); y = Margin; }
                pdf.DrawText(Margin, y, line, 11);
                y += 14.5;
            }
            y += 4;
        }

        foreach (var image in file.Text.Images)
        {
            if (image.IsEmpty) continue;
            var (w, h) = ImageSize.Of(image.Data, image.Extension);
            if (w <= 0 || h <= 0) continue;
            var scale = Math.Min(width / w, 1.0);
            var drawW = w * scale;
            var drawH = h * scale;
            if (y + drawH + 8 > bottom)
            {
                pdf.AddPage();
                y = Margin;
                var fit = Math.Min(width / w, (bottom - Margin) / h);
                drawW = w * fit;
                drawH = h * fit;
            }
            y += 6;
            pdf.DrawImage(image.Data, Margin, y, drawW, drawH);
            y += drawH + 8;
        }

        foreach (var table in file.Text.Tables)
        {
            var rows = new List<string[]>();
            var fills = new List<string?[]>();
            for (int r = 0; r < table.Rows; r++)
            {
                var row = new string[table.Columns];
                var fill = new string?[table.Columns];
                for (int c = 0; c < table.Columns; c++)
                {
                    row[c] = table.TextAt(r, c);
                    fill[c] = table.FillAt(r, c);
                }
                rows.Add(row);
                fills.Add(fill);
            }
            y += 8;
            if (y > bottom - MinRow()) { pdf.AddPage(); y = Margin; }
            y = PdfTables.Draw(pdf, null, rows, fills, Margin, y, width, Margin, bottom);
        }

        return ToBytes(pdf);
        // local: single-page documents guarantee at least one page.
    }

    private static double MinRow() => PdfTables.MinRowHeight;

    internal static byte[] ToBytes(PdfWriter pdf)
    {
        using var ms = new MemoryStream();
        pdf.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>PDF export for Calc documents: the grid (landscape) with floating images.</summary>
public static class CalcPdfExporter
{
    // Mirrors CalcPage's on-screen geometry, in CSS pixels (96dpi).
    public const double PxRowHeader = 42;
    public const double PxColumnWidth = 104;
    public const double PxHeaderHeight = 26;
    public const double PxRowHeight = 30;

    public static void Export(OricFile file, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, Render(file));
    }

    public static byte[] Render(OricFile file)
    {
        var pdf = new PdfWriter();
        pdf.AddPage(landscape: true);
        var margin = 24.0;
        var pageW = PdfWriter.A4Height;
        var pageH = PdfWriter.A4Width;

        var sheet = file.Sheet;
        var gridW = PxRowHeader + sheet.ColumnCount * PxColumnWidth;
        var gridH = PxHeaderHeight + sheet.RowCount * PxRowHeight;
        var scale = Math.Min(0.75,
            Math.Min((pageW - 2 * margin) / gridW, (pageH - 2 * margin) / gridH));

        double X(double px) => margin + px * scale;
        double Y(double px) => margin + px * scale;

        var gridColor = (0.78, 0.78, 0.78);
        var headerBg = (0.96, 0.96, 0.96);

        // Column headers.
        pdf.FillRect(X(0), Y(0), gridW * scale, PxHeaderHeight * scale, headerBg);
        pdf.DrawText(X(PxRowHeader + 4), Y(4), "", 8); // keep stream simple
        for (int c = 0; c < sheet.ColumnCount; c++)
            pdf.DrawText(X(PxRowHeader + c * PxColumnWidth + PxColumnWidth / 2 - 4),
                Y(PxHeaderHeight / 2 - 5),
                PdfWriter.Wrap(CellAddress.ColumnName(c), 8, PxColumnWidth * scale - 6, bold: true)[0],
                8, bold: true);

        // Row headers.
        pdf.FillRect(X(0), Y(PxHeaderHeight), PxRowHeader * scale, (gridH - PxHeaderHeight) * scale, headerBg);
        for (int r = 0; r < sheet.RowCount; r++)
            pdf.DrawText(X(6), Y(PxHeaderHeight + r * PxRowHeight + PxRowHeight / 2 - 5),
                (r + 1).ToString(), 8, bold: true);

        // Grid lines.
        for (int c = 0; c <= sheet.ColumnCount; c++)
            pdf.StrokeRect(X(PxRowHeader + c * PxColumnWidth - 1), Y(0), 1, gridH * scale, 0, gridColor);
        for (int r = 0; r <= sheet.RowCount; r++)
            pdf.StrokeRect(X(0), Y(PxHeaderHeight + r * PxRowHeight - 1), gridW * scale, 1, 0, gridColor);
        pdf.StrokeRect(X(0), Y(0), gridW * scale, gridH * scale, 1, (0.55, 0.55, 0.55));

        // Cell values (right-aligned when numeric).
        for (int r = 0; r < sheet.RowCount; r++)
        {
            for (int c = 0; c < sheet.ColumnCount; c++)
            {
                var value = sheet.ValueAt(r, c);
                if (value.Length == 0) continue;
                var cellX = PxRowHeader + c * PxColumnWidth;
                var cellY = PxHeaderHeight + r * PxRowHeight;
                var available = PxColumnWidth * scale - 8;
                var isNumber = double.TryParse(value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out _);
                if (isNumber)
                {
                    var w = PdfWriter.Measure(value, 8);
                    var right = cellX + PxColumnWidth - 4 - w / scale;
                    pdf.DrawText(X(right), Y(cellY + PxRowHeight / 2 - 5), value, 8);
                }
                else
                {
                    var text = value.Replace('\n', ' ');
                    while (PdfWriter.Measure(text, 8) * scale > available && text.Length > 1)
                        text = text[..^1];
                    pdf.DrawText(X(cellX + 4), Y(cellY + PxRowHeight / 2 - 5), text, 8);
                }
            }
        }

        // Floating images.
        foreach (var image in sheet.Images)
        {
            if (image.IsEmpty) continue;
            var px = PxRowHeader + image.AnchorColumn * PxColumnWidth + image.OffsetX;
            var py = PxHeaderHeight + image.AnchorRow * PxRowHeight + image.OffsetY;
            pdf.DrawImage(image.Data, X(px), Y(py), image.Width * scale, image.Height * scale);
        }

        return AuthorPdfExporter.ToBytes(pdf);
    }
}

/// <summary>PDF export for Base documents: the records datasheet.</summary>
public static class BasePdfExporter
{
    public static void Export(OricFile file, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, Render(file));
    }

    public static byte[] Render(OricFile file)
    {
        var pdf = new PdfWriter();
        pdf.AddPage();
        var margin = 48.0;
        var width = PdfWriter.A4Width - 2 * margin;
        var bottom = PdfWriter.A4Height - margin;
        double y = margin;

        if (!string.IsNullOrWhiteSpace(file.Title) && file.Title != "Untitled")
        {
            pdf.DrawText(margin, y, file.Title, 16, bold: true);
            y += 24;
        }
        pdf.DrawText(margin, y, file.Table.Name, 12, bold: true,
            color: (0.35, 0.35, 0.35));
        y += 20;

        var fields = file.Table.Fields;
        var headers = fields.Select(f => f.Name).ToArray();
        var rows = new List<string[]>();
        foreach (var record in file.Table.Records)
        {
            var row = new string[fields.Count];
            for (int i = 0; i < fields.Count; i++)
                row[i] = record.Values.TryGetValue(fields[i].Name, out var v) ? v : string.Empty;
            rows.Add(row);
        }

        if (rows.Count == 0)
        {
            pdf.DrawText(margin, y, "(no records)", 11, color: (0.5, 0.5, 0.5));
        }
        else
        {
            PdfTables.Draw(pdf, headers, rows, null, margin, y, width, margin, bottom);
        }

        return AuthorPdfExporter.ToBytes(pdf);
    }
}

/// <summary>Shared image pixel-size probing (PNG IHDR / JPEG SOF).</summary>
public static class ImageSize
{
    public static (int Width, int Height) Of(byte[] data, string fallbackExtension = ".png")
    {
        var format = ImageDetector.Detect(data);
        if (format == ImageFormat.Png && data.Length > 24)
        {
            var w = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            var h = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return (w, h);
        }
        if (format == ImageFormat.Jpeg)
        {
            int i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF) { i++; continue; }
                var marker = data[i + 1];
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    i += 2;
                    continue;
                }
                if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    var h = (data[i + 5] << 8) | data[i + 6];
                    var w = (data[i + 7] << 8) | data[i + 8];
                    return (w, h);
                }
                if (marker == 0xDA) break;
                var length = (data[i + 2] << 8) | data[i + 3];
                if (length < 2) break;
                i += 2 + length;
            }
        }
        return (0, 0);
    }
}
