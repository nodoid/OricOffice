using Microsoft.Data.Sqlite;
using OricOffice.Core.Model;
using OricOffice.Core.Storage;

namespace OricOffice.Core.Tests;

public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oricsuite-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}

public class OricFileStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly OricFileStore _store = new();

    public void Dispose() => _dir.Dispose();

    private static OricFile SampleFile()
    {
        var file = OricFile.CreateNew(OricFileKind.Author, "Sample");
        file.Text.Body = "Dear Sir,\n\nThe quick brown fox.\n\nYours, Ann.";

        file.Sheet.Set(0, 0, "Name", "Name");
        file.Sheet.Set(0, 1, "Score", "Score");
        file.Sheet.Set(1, 0, "Ann", "Ann");
        file.Sheet.Set(1, 1, "=1+1", "2");
        file.Sheet.EnsureCapacity(30, 12);

        file.Table.AddField("City", FieldType.Text);
        file.Table.AddField("Age", FieldType.Number);
        var record = file.Table.AddRecord();
        record.Set("City", "Leeds");
        record.Set("Age", "29");

        return file;
    }

    // ------------------------------------------------------------ round trips

    [Theory]
    [InlineData(OricFileKind.Author, ".oau")]
    [InlineData(OricFileKind.Calc, ".oca")]
    [InlineData(OricFileKind.Base, ".oba")]
    public void SaveAndLoad_RoundTripsEveryExtension(OricFileKind kind, string extension)
    {
        var original = SampleFile();
        var path = _store.Save(original, _dir.File("sample"), kind);

        Assert.EndsWith(extension, path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path));
        Assert.True(_store.IsSuiteFile(path));

        var loaded = _store.Load(path);

        Assert.Equal(kind, loaded.Kind);
        Assert.Equal("Sample", loaded.Title);
        Assert.Equal(original.Text.Body, loaded.Text.Body);

        Assert.Equal("Ann", loaded.Sheet.ValueAt(1, 0));
        Assert.Equal("=1+1", loaded.Sheet.Find(1, 1)!.Raw);
        Assert.Equal("2", loaded.Sheet.ValueAt(1, 1));
        Assert.True(loaded.Sheet.RowCount >= 30);

        Assert.Equal(new[] { "City", "Age" }, loaded.Table.Fields.Select(f => f.Name));
        Assert.Equal(FieldType.Number, loaded.Table.Fields[1].Type);
        Assert.Single(loaded.Table.Records);
        Assert.Equal("29", loaded.Table.Records[0].Get("Age"));
    }

    // ------------------------------------------------------------ cross-product interop

    [Fact]
    public void EveryAppCanOpenEveryExtension()
    {
        // One file saved by each product must load for every product.
        foreach (var kind in new[] { OricFileKind.Author, OricFileKind.Calc, OricFileKind.Base })
        {
            var path = _store.Save(SampleFile(), _dir.File("interop-" + kind), kind);

            foreach (var reader in new[] { OricFileKind.Author, OricFileKind.Calc, OricFileKind.Base })
            {
                var loaded = _store.Load(path); // extension/reader does not matter
                var label = $"{reader} reading {kind}";
                Assert.True(kind == loaded.Kind, $"{label}: wrong kind");
                Assert.True(!string.IsNullOrEmpty(loaded.Text.Body), $"{label}: lost the text");
                Assert.True(loaded.Sheet.ValueAt(1, 0) == "Ann", $"{label}: lost the sheet");
                Assert.True(loaded.Table.Records.Count == 1, $"{label}: lost the records");
            }
        }
    }

    [Fact]
    public void SaveFromAnotherApp_PreservesAllSections_AndSwitchesExtension()
    {
        // Author writes a file …
        var authorDoc = OricFile.CreateNew(OricFileKind.Author, "Letter");
        authorDoc.Text.Body = "Original author text";
        var path = _store.Save(authorDoc, _dir.File("letter"), OricFileKind.Author);
        Assert.EndsWith(".oau", path);

        // … Calc opens it, adds a formula, and saves as .oca …
        var forCalc = _store.Load(path);
        forCalc.Sheet.Set(0, 0, "=2*3", "6");
        var calcPath = _store.Save(forCalc, _dir.File("letter.oca"), OricFileKind.Calc);
        Assert.EndsWith(".oca", calcPath);

        // … Base opens that, adds a record, and saves as .oba …
        var forBase = _store.Load(calcPath);
        forBase.Table.EnsureUsable();
        var record = forBase.Table.AddRecord();
        record.Set("Field1", "from base");
        var basePath = _store.Save(forBase, _dir.File("letter"), OricFileKind.Base);
        Assert.EndsWith(".oba", basePath);

        // Everything survives the whole trip.
        var final = _store.Load(basePath);
        Assert.Equal("Original author text", final.Text.Body);
        Assert.Equal("6", final.Sheet.ValueAt(0, 0));
        Assert.Equal("=2*3", final.Sheet.Find(0, 0)!.Raw);
        Assert.Single(final.Table.Records);
        Assert.Equal("from base", final.Table.Records[0].Get("Field1"));
    }

    // ------------------------------------------------------------ errors

    [Fact]
    public void Load_MissingFile_Throws()
        => Assert.Throws<OricFileException>(() => _store.Load(_dir.File("nope.oau")));

    [Fact]
    public void Load_NonSqliteFile_ThrowsFriendlyError()
    {
        var path = _dir.File("notes.oau");
        File.WriteAllText(path, "This is just a text file pretending to be a document.");
        var ex = Assert.Throws<OricFileException>(() => _store.Load(path));
        Assert.Contains("not an Oric Suite file", ex.Message);
    }

    [Fact]
    public void Load_ForeignSqliteDatabase_ThrowsFriendlyError()
    {
        var path = _dir.File("other.oau");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE something (id INTEGER)";
            cmd.ExecuteNonQuery();
        }
        var ex = Assert.Throws<OricFileException>(() => _store.Load(path));
        Assert.Contains("not an Oric Suite document", ex.Message);
    }

    // ------------------------------------------------------------ paths

    [Theory]
    [InlineData("report.oau", OricFileKind.Author, "report.oau")]
    [InlineData("report.oca", OricFileKind.Author, "report.oau")]
    [InlineData("report", OricFileKind.Calc, "report.oca")]
    [InlineData("report.txt", OricFileKind.Base, "report.oba")]
    [InlineData("REPORT.OAU", OricFileKind.Calc, "REPORT.oca")]
    public void NormalizeSavePath_ForcesOwnExtension(string input, OricFileKind kind, string expected)
        => Assert.Equal(expected, OricFileStore.NormalizeSavePath(input, kind));

    [Fact]
    public void Extensions_AreTheDocumentedOnes()
        => Assert.Equal(new[] { ".oau", ".oca", ".oba" }, OricFileExtensions.All);

    [Fact]
    public void SuggestedFileName_SanitisesAndAppendsExtension()
    {
        var name = OricFileStore.SuggestedFileName("My/Report?", OricFileKind.Author);
        Assert.EndsWith(".oau", name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("?", name);
    }
}
