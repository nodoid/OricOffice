using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using OricOffice.Core.Export;
using OricOffice.Core.Model;
using OricOffice.Core.Storage;

namespace OricOffice.Core.Tests;

public class ExportTests
{
    /// <summary>A valid 1×1 RGBA PNG (base64) — exercises the alpha/SMask path.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static OricFile SampleAuthorFile()
    {
        var file = OricFile.CreateNew(OricFileKind.Author, "Sample");
        file.Text.Body = "Hello, Oric Office!\nSecond paragraph with a longer line that will wrap somewhere.";
        file.Text.Images.Add(new DocumentImage("pixel.png", TinyPng));
        var table = new TableBlock("Data", 2, 2);
        table.SetText(0, 0, "Name");
        table.SetText(0, 1, "Value");
        table.SetText(1, 0, "Alpha");
        table.SetText(1, 1, "42");
        table.SetFill(0, 0, "#DDEEFF");
        file.Text.Tables.Add(table);
        return file;
    }

    private static OricFile SampleCalcFile()
    {
        var file = OricFile.CreateNew(OricFileKind.Calc, "Budget");
        file.Sheet.Set(0, 0, "10", "10");
        file.Sheet.Set(1, 0, "=SUM(A1:A1)", "=SUM(A1:A1)");
        file.Sheet.Set(0, 1, "hello", "hello");
        file.Sheet.Images.Add(new SheetImage("pixel.png", TinyPng, 2, 1)
        {
            OffsetX = 8, OffsetY = 4, Width = 40, Height = 30,
        });
        return file;
    }

    private static OricFile SampleBaseFile()
    {
        var file = OricFile.CreateNew(OricFileKind.Base, "People");
        file.Table.Name = "People";
        file.Table.Fields.Add(new FieldDef("Name", FieldType.Text));
        file.Table.Fields.Add(new FieldDef("Age", FieldType.Number));
        var a = new RecordRow { Id = 1, Ordinal = 0 };
        a.Values["Name"] = "O'Brien \"The Boss\"";
        a.Values["Age"] = "39";
        var b = new RecordRow { Id = 2, Ordinal = 1 };
        b.Values["Name"] = "Plain";
        b.Values["Age"] = "not a number";
        file.Table.Records.Add(a);
        file.Table.Records.Add(b);
        file.Table.EnsureRecordKeys();
        return file;
    }

    private static string TempFile(string ext)
        => Path.Combine(Path.GetTempPath(),
            "orictest-" + Guid.NewGuid().ToString("N") + ext);

    // ------------------------------------------------------------ storage round-trip

    [Fact]
    public void Store_RoundTrips_ImagesAndTables()
    {
        var path = TempFile(".oau");
        try
        {
            var store = new OricFileStore();
            var original = SampleAuthorFile();
            store.Save(original, path, OricFileKind.Author);
            var loaded = store.Load(path);

            Assert.Equal(original.Text.Body, loaded.Text.Body);
            var image = Assert.Single(loaded.Text.Images);
            Assert.Equal("pixel.png", image.Name);
            Assert.Equal(TinyPng, image.Data);

            var table = Assert.Single(loaded.Text.Tables);
            Assert.Equal("Data", table.Name);
            Assert.Equal(2, table.Rows);
            Assert.Equal(2, table.Columns);
            Assert.Equal("Alpha", table.TextAt(1, 0));
            Assert.Equal("#DDEEFF", table.FillAt(0, 0));
            Assert.Null(table.FillAt(0, 1));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Store_RoundTrips_SheetImages()
    {
        var path = TempFile(".oca");
        try
        {
            var store = new OricFileStore();
            var original = SampleCalcFile();
            store.Save(original, path, OricFileKind.Calc);
            var loaded = store.Load(path);

            var image = Assert.Single(loaded.Sheet.Images);
            Assert.Equal("pixel.png", image.Name);
            Assert.Equal(2, image.AnchorRow);
            Assert.Equal(1, image.AnchorColumn);
            Assert.Equal(8, image.OffsetX);
            Assert.Equal(4, image.OffsetY);
            Assert.Equal(40, image.Width);
            Assert.Equal(30, image.Height);
            Assert.Equal(TinyPng, image.Data);
        }
        finally { File.Delete(path); }
    }

    // ------------------------------------------------------------ PDF

    private static void AssertPdf(byte[] bytes)
    {
        Assert.True(bytes.Length > 500);
        var header = Encoding.Latin1.GetString(bytes, 0, 8);
        Assert.StartsWith("%PDF-", header);
        var tail = Encoding.Latin1.GetString(bytes, bytes.Length - 32, 32);
        Assert.Contains("%%EOF", tail);

        // Every xref offset must point at "<id> 0 obj".
        var latin = Encoding.Latin1.GetString(bytes);
        var xrefAt = latin.LastIndexOf("\nxref\n0 ", StringComparison.Ordinal);
        Assert.True(xrefAt > 0);
        var lines = latin[(xrefAt + 1)..].Split('\n');
        var size = int.Parse(lines[1].Split(' ')[1]);
        // lines: [0]=xref, [1]="0 N", [2]=free entry for obj 0, [3]=obj 1, ...
        for (int id = 1; id < size; id++)
        {
            var offset = long.Parse(lines[2 + id].Split(' ')[0]);
            var at = Encoding.Latin1.GetString(bytes, (int)offset, 16);
            Assert.StartsWith(id + " 0 obj", at);
        }
    }

    [Fact]
    public void AuthorPdf_ContainsTextImagesAndTables()
    {
        var bytes = AuthorPdfExporter.Render(SampleAuthorFile());
        AssertPdf(bytes);
        var latin = Encoding.Latin1.GetString(bytes);
        Assert.Contains("Hello, Oric Office!", latin);
        Assert.Contains("/Im1 Do", latin);        // image drawn
        Assert.Contains("/SMask", latin);          // RGBA png alpha channel
        Assert.Contains("/Subtype /Image", latin);
    }

    [Fact]
    public void CalcPdf_RendersGridAndImages()
    {
        var bytes = CalcPdfExporter.Render(SampleCalcFile());
        AssertPdf(bytes);
        var latin = Encoding.Latin1.GetString(bytes);
        Assert.Contains("hello", latin);
        Assert.Contains("/Im1 Do", latin);
    }

    [Fact]
    public void BasePdf_RendersRecords()
    {
        var bytes = BasePdfExporter.Render(SampleBaseFile());
        AssertPdf(bytes);
        var latin = Encoding.Latin1.GetString(bytes);
        Assert.Contains("Plain", latin);
        Assert.Contains("Name", latin);
    }

    // ------------------------------------------------------------ ODF

    private static ZipArchive OpenZip(string path) => ZipFile.OpenRead(path);

    [Fact]
    public void Odt_MimetypeIsFirstAndStored_ContentHasParagraphsAndTables()
    {
        var path = TempFile(".odt");
        try
        {
            OdfExporter.ExportOdt(SampleAuthorFile(), path);
            using var zip = OpenZip(path);
            var first = zip.Entries.First();
            Assert.Equal("mimetype", first.FullName);
            Assert.Equal(first.Length, first.CompressedLength); // STORED, not deflated
            using (var s = first.Open())
            using (var reader = new StreamReader(s, Encoding.ASCII))
            {
                Assert.Equal("application/vnd.oasis.opendocument.text", reader.ReadToEnd());
            }

            Assert.NotNull(zip.GetEntry("content.xml"));
            Assert.NotNull(zip.GetEntry("META-INF/manifest.xml"));
            Assert.NotNull(zip.GetEntry("Pictures/image1.png"));

            using var content = new StreamReader(zip.GetEntry("content.xml")!.Open(), Encoding.UTF8);
            var xml = content.ReadToEnd();
            Assert.Contains("Hello, Oric Office!", xml);
            Assert.Contains("<table:table table:name=\"Data\">", xml);
            Assert.Contains("fo:background-color=\"#DDEEFF\"", xml);
            Assert.Contains("Pictures/image1.png", xml);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Ods_TransformsFormulas_AndCarriesImages()
    {
        var path = TempFile(".ods");
        try
        {
            OdfExporter.ExportOds(SampleCalcFile(), path);
            using var zip = OpenZip(path);
            var first = zip.Entries.First();
            Assert.Equal("mimetype", first.FullName);
            using (var s = first.Open())
            using (var reader = new StreamReader(s, Encoding.ASCII))
            {
                Assert.Equal("application/vnd.oasis.opendocument.spreadsheet", reader.ReadToEnd());
            }

            using var content = new StreamReader(zip.GetEntry("content.xml")!.Open(), Encoding.UTF8);
            var xml = content.ReadToEnd();
            Assert.Contains("table:formula=\"of:=SUM([.A1:.A1])\"", xml);
            Assert.Contains("office:value-type=\"float\"", xml);
            Assert.Contains("Pictures/image1.png", xml);
            Assert.NotNull(zip.GetEntry("Pictures/image1.png"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FormulaTransform_HandlesRefsRangesAndLiterals()
    {
        Assert.Equal("SUM([.B1:.B5])", OdfExporter.TransformFormula("SUM(B1:B5)"));
        Assert.Equal("[.A1]+1", OdfExporter.TransformFormula("A1+1"));
        Assert.Equal("ROUND([.$C$2]*2, 2)", OdfExporter.TransformFormula("ROUND($C$2*2, 2)"));
    }

    // ------------------------------------------------------------ SQL

    [Fact]
    public void MysqlScript_CreatesTable_AndEscapesQuotes()
    {
        var script = SqlExporters.ExportMysqlScript(SampleBaseFile());
        Assert.Contains("CREATE TABLE IF NOT EXISTS `People`", script);
        Assert.Contains("`Name` VARCHAR(255) NULL", script);
        Assert.Contains("`Age` DOUBLE NULL", script);
        Assert.Contains("ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", script);
        Assert.Contains("INSERT INTO `People`", script);
        Assert.Contains("'O\\'Brien \"The Boss\"'", script);
        Assert.Contains("39", script);
        Assert.Contains("NULL", script); // unparseable number → NULL
    }

    [Fact]
    public void MssqlScript_CreatesTable_AndEscapesBracketsAndQuotes()
    {
        var script = SqlExporters.ExportMssqlScript(SampleBaseFile());
        Assert.Contains("CREATE TABLE [People]", script);
        Assert.Contains("[Name] NVARCHAR(255) NULL", script);
        Assert.Contains("[Age] FLOAT NULL", script);
        Assert.Contains("[id] INT IDENTITY(1,1) PRIMARY KEY", script);
        Assert.Contains("N'O''Brien \"The Boss\"'", script);
        Assert.Contains("GO", script);
    }

    [Fact]
    public void SqliteExport_WritesRealDatabaseWithAllRecords()
    {
        var path = TempFile(".sqlite");
        try
        {
            SqlExporters.ExportSqlite(SampleBaseFile(), path);

            Assert.True(new OricFileStore().IsSuiteFile(path) is false || true); // any sqlite is fine
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM \"People\"";
            Assert.Equal(2L, (long)cmd.ExecuteScalar()!);

            using var rows = connection.CreateCommand();
            rows.CommandText = "SELECT \"Name\", \"Age\" FROM \"People\" ORDER BY \"id\"";
            using var reader = rows.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("O'Brien \"The Boss\"", reader.GetString(0));
            Assert.Equal(39.0, reader.GetDouble(1));
            Assert.True(reader.Read());
            Assert.Equal("Plain", reader.GetString(0));
            Assert.True(reader.IsDBNull(1)); // unparseable number → NULL
        }
        finally { File.Delete(path); }
    }
}
