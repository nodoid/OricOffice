using System.IO.Compression;
using System.Text;
using OricOffice.Core.Model;

namespace OricOffice.Core.Export;

/// <summary>
/// Writes OpenDocument packages: .odt for Author documents and .ods for Calc
/// sheets. The mimetype entry is stored first and uncompressed as the ODF
/// specification requires; LibreOffice and every other ODF reader accepts the
/// output.
/// </summary>
public static class OdfExporter
{
    public static void ExportOdt(OricFile file, string path)
    {
        var pictures = new List<Picture>();
        var content = new StringBuilder();
        content.Append(Header("application/vnd.oasis.opendocument.text"));
        content.Append("<office:body><office:text>");

        foreach (var paragraph in (file.Text.Body ?? string.Empty).Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(paragraph))
                content.Append("<text:p/>");
            else
                content.Append("<text:p>").Append(Escape(paragraph)).Append("</text:p>");
        }

        foreach (var image in file.Text.Images)
        {
            if (image.IsEmpty || !ImageDetector.IsSupported(image.Data)) continue;
            var (w, h) = ImageSize.Of(image.Data, image.Extension);
            if (w <= 0 || h <= 0) continue;
            var entry = $"Pictures/image{pictures.Count + 1}{image.Extension}";
            pictures.Add(new Picture(entry, image.Data, image.Extension));
            content.Append("<text:p>")
                .Append("<draw:frame draw:name=\"Image").Append(pictures.Count)
                .Append("\" text:anchor-type=\"as-char\"")
                .Append(" svg:width=\"").Append(Cm(w)).Append("cm\"")
                .Append(" svg:height=\"").Append(Cm(h)).Append("cm\">")
                .Append("<draw:image xlink:href=\"").Append(entry)
                .Append("\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>")
                .Append("</draw:frame>")
                .Append("</text:p>");
        }

        foreach (var table in file.Text.Tables)
        {
            content.Append("<table:table table:name=\"").Append(Escape(table.Name)).Append("\">");
            content.Append("<table:table-column table:number-columns-repeated=\"")
                .Append(table.Columns).Append("\"/>");
            for (int r = 0; r < table.Rows; r++)
            {
                content.Append("<table:table-row>");
                for (int c = 0; c < table.Columns; c++)
                {
                    content.Append("<table:table-cell");
                    var fill = table.FillAt(r, c);
                    if (!string.IsNullOrWhiteSpace(fill))
                        content.Append(" fo:background-color=\"").Append(Escape(fill)).Append('"');
                    content.Append(" office:value-type=\"string\"><text:p>")
                        .Append(Escape(table.TextAt(r, c)))
                        .Append("</text:p></table:table-cell>");
                }
                content.Append("</table:table-row>");
            }
            content.Append("</table:table>");
        }

        content.Append("</office:text></office:body></office:document-content>");

        WritePackage(path,
            "application/vnd.oasis.opendocument.text",
            "text",
            content.ToString(),
            file.Title ?? "Untitled",
            pictures);
    }

    public static void ExportOds(OricFile file, string path)
    {
        var pictures = new List<Picture>();
        var sheet = file.Sheet;
        var content = new StringBuilder();
        content.Append(Header("application/vnd.oasis.opendocument.spreadsheet"));
        content.Append("<office:body><office:spreadsheet>");
        content.Append("<table:table table:name=\"").Append(Escape(sheet.Name)).Append("\">");
        content.Append("<table:table-column table:number-columns-repeated=\"")
            .Append(sheet.ColumnCount).Append("\"/>");

        // A frame per floating image, keyed by the cell it is anchored to.
        var imagesByCell = new Dictionary<(int Row, int Col), List<SheetImage>>();
        foreach (var image in sheet.Images)
        {
            if (image.IsEmpty || !ImageDetector.IsSupported(image.Data)) continue;
            var key = (image.AnchorRow, image.AnchorColumn);
            if (!imagesByCell.TryGetValue(key, out var list))
                imagesByCell[key] = list = new List<SheetImage>();
            list.Add(image);
        }

        for (int r = 0; r < sheet.RowCount; r++)
        {
            content.Append("<table:table-row>");
            for (int c = 0; c < sheet.ColumnCount; c++)
            {
                var raw = sheet.RawAt(r, c);
                var value = sheet.ValueAt(r, c);
                content.Append("<table:table-cell");

                var hasFormula = raw.StartsWith('=');
                var isNumber = !hasFormula && value.Length > 0 && double.TryParse(value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var numeric);

                if (hasFormula)
                {
                    content.Append(" table:formula=\"of:=").Append(Escape(TransformFormula(raw[1..])))
                        .Append('"');
                    if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out _))
                    {
                        content.Append(" office:value-type=\"float\" office:value=\"")
                            .Append(Escape(value)).Append('"');
                    }
                    else
                    {
                        content.Append(" office:value-type=\"string\"");
                    }
                }
                else if (isNumber)
                {
                    content.Append(" office:value-type=\"float\" office:value=\"")
                        .Append(Escape(value)).Append('"');
                }
                else
                {
                    content.Append(" office:value-type=\"string\"");
                }

                content.Append('>');
                content.Append("<text:p>").Append(Escape(hasFormula ? value : value)).Append("</text:p>");

                if (imagesByCell.TryGetValue((r, c), out var cellImages))
                {
                    foreach (var image in cellImages)
                    {
                        var (w, h) = ImageSize.Of(image.Data, image.Extension);
                        if (w <= 0 || h <= 0) continue;
                        var entry = $"Pictures/image{pictures.Count + 1}{image.Extension}";
                        pictures.Add(new Picture(entry, image.Data, image.Extension));
                        content.Append("<draw:frame draw:name=\"Image").Append(pictures.Count)
                            .Append("\" text:anchor-type=\"as-char\"")
                            .Append(" svg:width=\"").Append(Cm(w)).Append("cm\"")
                            .Append(" svg:height=\"").Append(Cm(h)).Append("cm\">")
                            .Append("<draw:image xlink:href=\"").Append(entry)
                            .Append("\" xlink:type=\"simple\" xlink:show=\"embed\" xlink:actuate=\"onLoad\"/>")
                            .Append("</draw:frame>");
                    }
                }

                content.Append("</table:table-cell>");
            }
            content.Append("</table:table-row>");
        }

        content.Append("</table:table></office:spreadsheet></office:body></office:document-content>");

        WritePackage(path,
            "application/vnd.oasis.opendocument.spreadsheet",
            "spreadsheet",
            content.ToString(),
            file.Title ?? "Untitled",
            pictures);
    }

    /// <summary>Converts an A1-style formula body to OpenFormula references ([.A1]).</summary>
    public static string TransformFormula(string formulaBody)
    {
        var sb = new StringBuilder(formulaBody.Length + 8);
        int i = 0;
        while (i < formulaBody.Length)
        {
            var start = i;
            while (i < formulaBody.Length &&
                   (char.IsLetterOrDigit(formulaBody[i]) || formulaBody[i] == '$' || formulaBody[i] == ':'))
                i++;
            var token = formulaBody[start..i];
            if (i == start) { sb.Append(formulaBody[i]); i++; continue; }

            var parts = token.Split(':');
            if (parts.Length >= 1 && IsCellRef(parts[0]))
            {
                sb.Append("[.").Append(parts[0]);
                if (parts.Length > 1 && IsCellRef(parts[1]))
                    sb.Append(":.").Append(parts[1]);
                else if (parts.Length > 1)
                    sb.Append(':').Append(parts[1]);
                sb.Append(']');
                for (int k = 2; k < parts.Length; k++) sb.Append(':').Append(parts[k]);
            }
            else
            {
                sb.Append(token);
            }
        }
        return sb.ToString();
    }

    private static bool IsCellRef(string s)
    {
        int i = 0;
        if (i < s.Length && s[i] == '$') i++;
        int letters = 0;
        while (i < s.Length && char.IsLetter(s[i])) { letters++; i++; }
        if (letters is < 1 or > 3) return false;
        if (i < s.Length && s[i] == '$') i++;
        int digits = 0;
        while (i < s.Length && char.IsDigit(s[i])) { digits++; i++; }
        return digits >= 1 && i == s.Length;
    }

    // ---------------------------------------------------------------- package

    private sealed record Picture(string Entry, byte[] Data, string Extension);

    private static void WritePackage(string path, string mimetype, string flavor,
        string contentXml, string title, List<Picture> pictures)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(path)) File.Delete(path);

        using var stream = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        // 1. mimetype must be first and STORED.
        var mimeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var s = mimeEntry.Open())
        {
            var bytes = Encoding.ASCII.GetBytes(mimetype);
            s.Write(bytes, 0, bytes.Length);
        }

        AddEntry(zip, "META-INF/manifest.xml", Manifest(mimetype, flavor, pictures));
        AddEntry(zip, "content.xml", contentXml);
        AddEntry(zip, "styles.xml", Styles());
        AddEntry(zip, "meta.xml", Meta(title));
        foreach (var picture in pictures)
        {
            var entry = zip.CreateEntry(picture.Entry, CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(picture.Data, 0, picture.Data.Length);
        }
    }

    private static void AddEntry(ZipArchive zip, string name, string xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(xml);
        s.Write(bytes, 0, bytes.Length);
    }

    private static string Header(string mimetype) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<office:document-content\n" +
        " xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"\n" +
        " xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\"\n" +
        " xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\"\n" +
        " xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\"\n" +
        " xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\"\n" +
        " xmlns:xlink=\"http://www.w3.org/1999/xlink\"\n" +
        " xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\"\n" +
        " xmlns:of=\"urn:oasis:names:tc:opendocument:xmlns:of:1.2\"\n" +
        " office:version=\"1.3\">\n";

    private static string Manifest(string mimetype, string flavor, List<Picture> pictures)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<manifest:manifest\n");
        sb.Append(" xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\"\n");
        sb.Append(" manifest:version=\"1.3\">\n");
        sb.Append("<manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.3\" manifest:media-type=\"")
          .Append(mimetype).Append("\"/>\n");
        foreach (var part in new[] { "content.xml", "styles.xml", "meta.xml" })
            sb.Append("<manifest:file-entry manifest:full-path=\"").Append(part)
              .Append("\" manifest:media-type=\"text/xml\"/>\n");
        foreach (var picture in pictures)
            sb.Append("<manifest:file-entry manifest:full-path=\"").Append(picture.Entry)
              .Append("\" manifest:media-type=\"image/")
              .Append(picture.Extension.TrimStart('.').Replace("jpg", "jpeg"))
              .Append("\"/>\n");
        sb.Append("</manifest:manifest>");
        return sb.ToString();
    }

    private static string Styles() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-styles
         xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
         xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
         xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
         xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"
         office:version="1.3">
         <office:styles>
          <style:default-style style:family="paragraph">
           <style:text-properties style:font-name="Liberation Sans" fo:font-size="12pt"/>
          </style:default-style>
          <style:style style:name="Standard" style:family="paragraph" style:class="default"/>
         </office:styles>
        </office:document-styles>
        """;

    private static string Meta(string title) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-meta
         xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
         xmlns:dc="http://purl.org/dc/elements/1.1/"
         xmlns:meta="urn:oasis:names:tc:opendocument:xmlns:meta:1.0"
         office:version="1.3">
         <office:meta>
          <dc:title>{Escape(title)}</dc:title>
          <meta:generator>Oric Office</meta:generator>
         </office:meta>
        </office:document-meta>
        """;

    private static string Cm(int px) =>
        (px / 96.0 * 2.54).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    internal static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
    }
}
