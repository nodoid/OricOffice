namespace OricOffice.Core.Export;

/// <summary>
/// A deliberately small PDF generator — enough of the format to export Author,
/// Calc and Base documents (base-14 Helvetica text, vector fills/strokes, PNG
/// and JPEG images) with no external dependencies, so it runs identically on
/// every MAUI platform.
///
/// Coordinates are given in points with a TOP-LEFT origin (like the views);
/// the writer converts to PDF's bottom-left origin internally.
/// </summary>
public sealed class PdfWriter
{
    public const double A4Width = 595.276;
    public const double A4Height = 841.89;

    private sealed class Page
    {
        public Page(double width, double height) { Width = width; Height = height; }
        public double Width { get; }
        public double Height { get; }
        public System.Text.StringBuilder Content { get; } = new();
    }

    private sealed class ImageRes
    {
        public string Name = "";
        public byte[] Source = Array.Empty<byte>();
        public bool IsJpeg;
        public int Width;
        public int Height;
        public string ColorSpace = "/DeviceRGB";
        public byte[]? Decoded;   // PNG: Flate-ready RGB samples
        public byte[]? Alpha;     // PNG: Flate-ready gray plane for /SMask
        public int ObjectNumber;
        public int? SoftMaskObject;
    }

    private readonly List<Page> _pages = new();
    private readonly List<ImageRes> _images = new();
    private Page _current = null!;

    public int PageCount => _pages.Count;

    public double PageWidth => _current.Width;
    public double PageHeight => _current.Height;

    // ---------------------------------------------------------------- pages

    public void AddPage(bool landscape = false)
    {
        double w = landscape ? A4Height : A4Width;
        double h = landscape ? A4Width : A4Height;
        _current = new Page(w, h);
        _pages.Add(_current);
    }

    // ---------------------------------------------------------------- text

    /// <summary>Width of text in points when drawn with <paramref name="size"/>.</summary>
    public static double Measure(string text, double size, bool bold = false)
    {
        var widths = bold ? BoldWidths : RegularWidths;
        double units = 0;
        foreach (var c in Sanitize(text))
            units += widths.TryGetValue(c, out var w) ? w : (bold ? 600 : 556);
        return units / 1000.0 * size;
    }

    /// <summary>Greedy word wrap; words longer than the line are force-broken.</summary>
    public static List<string> Wrap(string text, double size, double maxWidth, bool bold = false)
    {
        var lines = new List<string>();
        foreach (var hardLine in (text ?? string.Empty).Replace("\r", "").Split('\n'))
        {
            if (hardLine.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var line = string.Empty;
            foreach (var word in hardLine.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Measure(candidate, size, bold) <= maxWidth)
                {
                    line = candidate;
                    continue;
                }
                if (line.Length > 0) lines.Add(line);
                line = word;
                while (Measure(line, size, bold) > maxWidth && line.Length > 1)
                {
                    var cut = line.Length;
                    while (cut > 1 && Measure(line[..cut], size, bold) > maxWidth) cut--;
                    lines.Add(line[..cut]);
                    line = line[cut..];
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Draws one line of text with a TOP-LEFT anchor.</summary>
    public void DrawText(double x, double y, string text, double size, bool bold = false,
        (double R, double G, double B)? color = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var c = color ?? (0, 0, 0);
        var baseline = y + size * 0.8;
        _current.Content.Append("BT ")
            .Append(F(c.R)).Append(' ').Append(F(c.G)).Append(' ').Append(F(c.B)).Append(" rg ")
            .Append(F(x)).Append(' ').Append(F(MapY(baseline))).Append(" Td /")
            .Append(bold ? "F2" : "F1").Append(' ').Append(F(size)).Append(" Tf (")
            .Append(Escape(Sanitize(text))).Append(") Tj ET\n");
    }

    // ---------------------------------------------------------------- shapes

    public void FillRect(double x, double y, double w, double h, (double R, double G, double B) color)
    {
        _current.Content.Append(F(color.R)).Append(' ').Append(F(color.G)).Append(' ')
            .Append(F(color.B)).Append(" rg ")
            .Append(F(x)).Append(' ').Append(F(MapY(y + h))).Append(' ')
            .Append(F(w)).Append(' ').Append(F(h)).Append(" re f\n");
    }

    public void StrokeRect(double x, double y, double w, double h, double thickness,
        (double R, double G, double B) color)
    {
        _current.Content.Append(F(color.R)).Append(' ').Append(F(color.G)).Append(' ')
            .Append(F(color.B)).Append(" RG ").Append(F(thickness)).Append(" w ")
            .Append(F(x + thickness / 2)).Append(' ').Append(F(MapY(y + h) + thickness / 2)).Append(' ')
            .Append(F(Math.Max(w - thickness, 0.1))).Append(' ')
            .Append(F(Math.Max(h - thickness, 0.1))).Append(" re S\n");
    }

    // ---------------------------------------------------------------- images

    /// <summary>Draws a PNG or JPEG (top-left anchor). Returns false when unsupported.</summary>
    public bool DrawImage(byte[] data, double x, double y, double w, double h)
    {
        var res = GetOrAddImage(data);
        if (res is null) return false;
        _current.Content.Append("q ").Append(F(w)).Append(" 0 0 ").Append(F(h)).Append(' ')
            .Append(F(x)).Append(' ').Append(F(MapY(y + h))).Append(" cm /")
            .Append(res.Name).Append(" Do Q\n");
        return true;
    }

    private ImageRes? GetOrAddImage(byte[] data)
    {
        if (data is null || data.Length == 0) return null;
        foreach (var img in _images)
            if (ReferenceEquals(img.Source, data))
                return img;

        ImageRes res;
        var format = Model.ImageDetector.Detect(data);
        if (format == Model.ImageFormat.Jpeg)
        {
            if (!TryReadJpegSize(data, out var jw, out var jh, out var gray, out var progressive))
                return null;
            if (progressive) return null; // DCTDecode is unreliable for progressive JPEGs
            res = new ImageRes
            {
                Source = data,
                IsJpeg = true,
                Width = jw,
                Height = jh,
                ColorSpace = gray ? "/DeviceGray" : "/DeviceRGB",
            };
        }
        else if (format == Model.ImageFormat.Png)
        {
            var png = TryReadPng(data);
            if (png is null) return null;
            res = new ImageRes
            {
                Source = data,
                IsJpeg = false,
                Width = png.Width,
                Height = png.Height,
                ColorSpace = png.ColorSpace,
                Decoded = png.Decoded,
                Alpha = png.Alpha,
            };
            if (png.Alpha is not null) res.SoftMaskObject = 0; // real id assigned in Save
        }
        else
        {
            return null;
        }

        res.Name = "Im" + (_images.Count + 1);
        _images.Add(res);
        return res;
    }

    // ---------------------------------------------------------------- output

    public void Save(Stream stream)
    {
        // Object numbering: 1 catalog, 2 pages, 3 F1, 4 F2,
        // then page + content pairs, then images, then shared resources.
        var pageObjIds = new int[_pages.Count];
        var contentIds = new int[_pages.Count];
        var next = 5;
        for (int i = 0; i < _pages.Count; i++)
        {
            pageObjIds[i] = next++;
            contentIds[i] = next++;
        }
        foreach (var img in _images)
        {
            img.ObjectNumber = next++;
            if (img.Alpha is not null) img.SoftMaskObject = next++;
        }
        var resourcesId = next++;
        var total = next;

        var offsets = new long[total];
        long pos = 0;

        void Raw(byte[] bytes)
        {
            stream.Write(bytes, 0, bytes.Length);
            pos += bytes.Length;
        }

        void Text(string s) => Raw(System.Text.Encoding.Latin1.GetBytes(s));

        void StartObj(int id)
        {
            offsets[id] = pos;
            Text(id + " 0 obj\n");
        }

        void WriteStream(int id, byte[] payload, string dictExtra)
        {
            StartObj(id);
            Text($"<< /Length {payload.Length}{dictExtra} >>\nstream\n");
            Raw(payload);
            Text("\nendstream\nendobj\n");
        }

        Text("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

        StartObj(1);
        Text("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        StartObj(2);
        Text("<< /Type /Pages /Kids [" +
             string.Join(' ', pageObjIds.Select(id => $"{id} 0 R")) +
             $"] /Count {_pages.Count} >>\nendobj\n");

        StartObj(3);
        Text("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
        StartObj(4);
        Text("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

        foreach (var img in _images)
        {
            if (img.IsJpeg)
            {
                WriteStream(img.ObjectNumber, img.Source,
                    $" /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height}" +
                    $" /ColorSpace {img.ColorSpace} /BitsPerComponent 8 /Filter /DCTDecode");
            }
            else
            {
                if (img.SoftMaskObject is { } smask && img.Alpha is not null)
                {
                    WriteStream(smask, img.Alpha,
                        " /Type /XObject /Subtype /Image /ColorSpace /DeviceGray" +
                        $" /BitsPerComponent 8 /Width {img.Width} /Height {img.Height}" +
                        " /Filter /FlateDecode");
                }
                WriteStream(img.ObjectNumber, img.Decoded ?? Array.Empty<byte>(),
                    $" /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height}" +
                    $" /ColorSpace {img.ColorSpace} /BitsPerComponent 8 /Filter /FlateDecode" +
                    (img.SoftMaskObject is { } s ? $" /SMask {s} 0 R" : ""));
            }
        }

        StartObj(resourcesId);
        var xobjects = _images.Count == 0
            ? string.Empty
            : " /XObject << " + string.Join(' ',
                _images.Select(i => $"/{i.Name} {i.ObjectNumber} 0 R")) + " >>";
        Text("<< /ProcSet [/PDF /Text /ImageB /ImageC] /Font << /F1 3 0 R /F2 4 0 R >>" +
             xobjects + " >>\nendobj\n");

        for (int i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            WriteStream(contentIds[i], System.Text.Encoding.Latin1.GetBytes(page.Content.ToString()), "");
            StartObj(pageObjIds[i]);
            Text($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(page.Width)} {F(page.Height)}]" +
                 $" /Resources {resourcesId} 0 R /Contents {contentIds[i]} 0 R >>\nendobj\n");
        }

        var xrefPos = pos;
        var xref = new System.Text.StringBuilder();
        xref.Append("xref\n0 ").Append(total).Append('\n');
        xref.Append("0000000000 65535 f \n");
        for (int id = 1; id < total; id++)
            xref.Append(offsets[id].ToString("D10")).Append(" 00000 n \n");
        xref.Append("trailer\n<< /Size ").Append(total).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xrefPos).Append("\n%%EOF\n");
        Text(xref.ToString());
        stream.Flush();
    }

    // ---------------------------------------------------------------- helpers

    private double MapY(double topLeftY) => _current.Height - topLeftY;

    private static string F(double v)
        => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static readonly Dictionary<char, char> WinAnsiMap = BuildWinAnsiMap();

    private static Dictionary<char, char> BuildWinAnsiMap()
    {
        var map = new Dictionary<char, char>();
        void Add(char from, char to) => map[from] = to;
        Add('−', '-'); Add('–', '-'); Add('—', '-');
        Add('‘', '\''); Add('’', '\'');
        Add('“', '"'); Add('”', '"');
        Add('…', '.'); Add('•', '.'); Add('′', '\''); Add('″', '"');
        Add(' ', ' ');
        return map;
    }

    /// <summary>Unicode → printable ASCII/WinAnsi so base-14 fonts render every glyph.</summary>
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var raw in text)
        {
            var ch = raw;
            if (WinAnsiMap.TryGetValue(ch, out var mapped)) ch = mapped;
            if (ch < 128 || (ch >= 160 && ch <= 255)) sb.Append(ch);
            else if (ch == '…') sb.Append("...");
            else sb.Append('?');
        }
        return sb.ToString();
    }

    private static readonly Dictionary<char, int> RegularWidths = BuildWidths(
        (32, 278), (33, 278), (34, 355), (35, 556), (36, 556), (37, 889), (38, 667), (39, 191),
        (40, 333), (41, 333), (42, 389), (43, 584), (44, 278), (45, 333), (46, 278), (47, 278),
        (48, 556), (49, 556), (50, 556), (51, 556), (52, 556), (53, 556), (54, 556), (55, 556),
        (56, 556), (57, 556), (58, 278), (59, 278), (60, 584), (61, 584), (62, 584), (63, 556),
        (64, 1015), (65, 667), (66, 667), (67, 722), (68, 722), (69, 667), (70, 611), (71, 778),
        (72, 722), (73, 278), (74, 500), (75, 667), (76, 556), (77, 833), (78, 722), (79, 778),
        (80, 667), (81, 778), (82, 722), (83, 667), (84, 611), (85, 722), (86, 667), (87, 944),
        (88, 667), (89, 667), (90, 611), (91, 278), (92, 278), (93, 278), (94, 469), (95, 556),
        (96, 333), (97, 556), (98, 556), (99, 500), (100, 556), (101, 556), (102, 278), (103, 556),
        (104, 556), (105, 222), (106, 222), (107, 500), (108, 222), (109, 833), (110, 556),
        (111, 556), (112, 556), (113, 556), (114, 333), (115, 500), (116, 278), (117, 556),
        (118, 500), (119, 722), (120, 500), (121, 500), (122, 500), (123, 334), (124, 260),
        (125, 334), (126, 584));

    private static readonly Dictionary<char, int> BoldWidths = BuildWidths(
        (32, 278), (33, 333), (34, 474), (35, 556), (36, 556), (37, 889), (38, 722), (39, 238),
        (40, 333), (41, 333), (42, 389), (43, 584), (44, 278), (45, 333), (46, 278), (47, 278),
        (48, 556), (49, 556), (50, 556), (51, 556), (52, 556), (53, 556), (54, 556), (55, 556),
        (56, 556), (57, 556), (58, 333), (59, 333), (60, 584), (61, 584), (62, 584), (63, 611),
        (64, 975), (65, 722), (66, 722), (67, 722), (68, 722), (69, 667), (70, 611), (71, 778),
        (72, 722), (73, 278), (74, 556), (75, 722), (76, 611), (77, 833), (78, 722), (79, 778),
        (80, 667), (81, 778), (82, 722), (83, 667), (84, 611), (85, 722), (86, 667), (87, 944),
        (88, 667), (89, 667), (90, 611), (91, 333), (92, 278), (93, 333), (94, 584), (95, 556),
        (96, 333), (97, 556), (98, 611), (99, 556), (100, 611), (101, 556), (102, 333), (103, 611),
        (104, 611), (105, 278), (106, 278), (107, 556), (108, 278), (109, 889), (110, 611),
        (111, 611), (112, 611), (113, 611), (114, 389), (115, 556), (116, 333), (117, 611),
        (118, 556), (119, 778), (120, 556), (121, 556), (122, 500), (123, 389), (124, 280),
        (125, 389), (126, 584));

    private static Dictionary<char, int> BuildWidths(params (int Ch, int Width)[] entries)
    {
        var dict = new Dictionary<char, int>(entries.Length + 64);
        foreach (var (ch, width) in entries) dict[(char)ch] = width;
        dict['…'] = 1000;
        dict['—'] = 1000;
        dict['–'] = 556;
        dict['‘'] = 333;
        dict['’'] = 333;
        dict['“'] = 500;
        dict['”'] = 500;
        dict['•'] = 350;
        dict['€'] = 556;
        for (var c = (char)160; c <= 255; c++)
            if (!dict.ContainsKey(c)) dict[c] = 556;
        return dict;
    }

    // ---------------------------------------------------------------- image headers

    private static bool TryReadJpegSize(byte[] data, out int width, out int height,
        out bool grayscale, out bool progressive)
    {
        width = height = 0;
        grayscale = false;
        progressive = false;
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
            if (marker == 0xFF) { i++; continue; }
            var length = (data[i + 2] << 8) | data[i + 3];
            if (marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                progressive = marker is 0xC2 or 0xC6 or 0xCA or 0xCE;
                height = (data[i + 5] << 8) | data[i + 6];
                width = (data[i + 7] << 8) | data[i + 8];
                var components = data[i + 9];
                grayscale = components == 1;
                if (components != 1 && components != 3) return false;
                return width > 0 && height > 0;
            }
            if (marker == 0xDA) break;
            if (length < 2) break;
            i += 2 + length;
        }
        return false;
    }

    private sealed class PngInfo
    {
        public int Width;
        public int Height;
        public string ColorSpace = "/DeviceRGB";
        public byte[] Decoded = Array.Empty<byte>();
        public byte[]? Alpha;
    }

    private static PngInfo? TryReadPng(byte[] data)
    {
        if (Model.ImageDetector.Detect(data) != Model.ImageFormat.Png) return null;

        int width = 0, height = 0, bitDepth = 8, colorType = 2, interlace = 0;
        var palette = new List<byte>();
        var idat = new List<byte>();

        int pos = 8;
        while (pos + 8 <= data.Length)
        {
            int len = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            var dataStart = pos + 8;
            if (len < 0 || dataStart + len + 4 > data.Length) break;
            if (type == "IHDR")
            {
                width = (data[dataStart] << 24) | (data[dataStart + 1] << 16) |
                        (data[dataStart + 2] << 8) | data[dataStart + 3];
                height = (data[dataStart + 4] << 24) | (data[dataStart + 5] << 16) |
                         (data[dataStart + 6] << 8) | data[dataStart + 7];
                bitDepth = data[dataStart + 8];
                colorType = data[dataStart + 9];
                interlace = data[dataStart + 12];
            }
            else if (type == "PLTE")
            {
                for (int k = 0; k < len; k++) palette.Add(data[dataStart + k]);
            }
            else if (type == "IDAT")
            {
                for (int k = 0; k < len; k++) idat.Add(data[dataStart + k]);
            }
            else if (type == "IEND")
            {
                break;
            }
            pos = dataStart + len + 4;
        }

        if (width <= 0 || height <= 0 || idat.Count == 0) return null;
        if (interlace != 0) return null;
        if (bitDepth != 8 && bitDepth != 16) return null;

        byte[] raw;
        try
        {
            using var input = new MemoryStream(idat.ToArray());
            using var zlib = new System.IO.Compression.ZLibStream(
                input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            raw = output.ToArray();
        }
        catch
        {
            return null;
        }

        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (channels == 0) return null;
        var sampleBytes = bitDepth / 8;
        var stride = width * channels * sampleBytes;
        var bpp = Math.Max(1, channels * sampleBytes);
        if (raw.Length < (long)(stride + 1) * height) return null;

        // Un-filter scanlines.
        var pixels = new byte[stride * height];
        var prior = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var src = y * (stride + 1) + 1;
            var dst = y * stride;
            for (int x = 0; x < stride; x++)
            {
                var b = raw[src + x];
                var a = x >= bpp ? pixels[dst + x - bpp] : (byte)0;
                var c = prior[x];
                pixels[dst + x] = filter switch
                {
                    0 => b,
                    1 => (byte)(b + a),
                    2 => (byte)(b + c),
                    3 => (byte)(b + ((a + c) >> 1)),
                    4 => (byte)(b + Paeth(a, c, x >= bpp ? prior[x - bpp] : (byte)0)),
                    _ => b,
                };
            }
            Buffer.BlockCopy(pixels, dst, prior, 0, stride);
        }

        // Convert to 8-bit RGB (+ alpha plane when needed).
        var rgb = new byte[width * height * 3];
        byte[]? alpha = colorType is 4 or 6 ? new byte[width * height] : null;
        for (int p = 0; p < width * height; p++)
        {
            var src = p * channels * sampleBytes;
            var dst = p * 3;
            switch (colorType)
            {
                case 0:
                {
                    var g = pixels[src];
                    rgb[dst] = rgb[dst + 1] = rgb[dst + 2] = g;
                    break;
                }
                case 2:
                    rgb[dst] = pixels[src];
                    rgb[dst + 1] = pixels[src + sampleBytes];
                    rgb[dst + 2] = pixels[src + 2 * sampleBytes];
                    break;
                case 3:
                {
                    var idx = pixels[src];
                    if (idx * 3 + 2 < palette.Count)
                    {
                        rgb[dst] = palette[idx * 3];
                        rgb[dst + 1] = palette[idx * 3 + 1];
                        rgb[dst + 2] = palette[idx * 3 + 2];
                    }
                    break;
                }
                case 4:
                {
                    var g = pixels[src];
                    rgb[dst] = rgb[dst + 1] = rgb[dst + 2] = g;
                    alpha![p] = pixels[src + sampleBytes];
                    break;
                }
                case 6:
                    rgb[dst] = pixels[src];
                    rgb[dst + 1] = pixels[src + sampleBytes];
                    rgb[dst + 2] = pixels[src + 2 * sampleBytes];
                    alpha![p] = pixels[src + 3 * sampleBytes];
                    break;
            }
        }

        return new PngInfo
        {
            Width = width,
            Height = height,
            ColorSpace = "/DeviceRGB",
            Decoded = Deflate(rgb),
            Alpha = alpha is null ? null : Deflate(alpha),
        };
    }

    private static byte Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return (byte)a;
        return pb <= pc ? (byte)b : (byte)c;
    }

    private static byte[] Deflate(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(
                   output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(payload, 0, payload.Length);
        }
        return output.ToArray();
    }
}
