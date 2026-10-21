using OricOffice.Core.Export;
using OricOffice.Core.Model;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.ViewModels;
using System.IO.Compression;

namespace OricOffice.Core.Tests;

/// <summary>
/// LibreOffice Base .odb export/import: package structure, embedded HSQLDB
/// script fidelity (quotes/newlines/typed columns) and the Base view-model
/// commands. Opening in LibreOffice itself needs a JVM, which this machine
/// does not have — the embedded script and front-end are verified against
/// the researched HSQLDB 1.8 / ODF database formats instead.
/// </summary>
public class OdbTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeLifetime _lifetime = new();
    private readonly OricFileStore _store;

    public OdbTests() => _store = new OricFileStore(_dir.Path);

    public void Dispose() => _dir.Dispose();

    private BaseViewModel Create()
        => new(_dialogs, _lifetime, _store,
            new AppMeta("Oric Base", "1.0.0", OricFileKind.Base));

    private static OricFile SampleDocument()
    {
        var file = OricFile.CreateNew(OricFileKind.Base, "People Export");
        file.Table.Name = "People";
        file.Table.Fields.Add(new FieldDef("Name", FieldType.Text));
        file.Table.Fields.Add(new FieldDef("Joined", FieldType.Date));
        file.Table.Fields.Add(new FieldDef("Sales", FieldType.Number));
        file.Table.Fields.Add(new FieldDef("Notes", FieldType.Text));

        var first = new RecordRow { Id = 1, Ordinal = 0 };
        first.Set("Name", "O'Brien \"the Bold\"");
        first.Set("Joined", "1983-05-01");
        first.Set("Sales", "1200.5");
        first.Set("Notes", "line one\nline two");
        file.Table.Records.Add(first);

        var second = new RecordRow { Id = 7, Ordinal = 1 };
        second.Set("Name", "Anne Brennan");
        second.Set("Joined", "1984-01-20");
        second.Set("Sales", "not yet tallied"); // junk → column must fall back to VARCHAR
        second.Set("Notes", string.Empty);
        file.Table.Records.Add(second);

        return file;
    }

    // ------------------------------------------------------------ package structure

    [Fact]
    public void Export_WritesAnOdfDatabasePackage()
    {
        var path = _dir.File("sample.odb");
        OdbExporter.Export(SampleDocument(), path);

        using var zip = ZipFile.OpenRead(path);

        // ODF: mimetype must be first and stored uncompressed (local header
        // compression method 0 at offset 8 of the first local file header).
        var mime = zip.Entries.First();
        Assert.Equal("mimetype", mime.FullName);
        using (var raw = File.OpenRead(path))
        {
            var header = new byte[10];
            raw.ReadExactly(header, 0, header.Length);
            Assert.Equal(0x04034b50u, BitConverter.ToUInt32(header, 0)); // PK\3\4
            Assert.Equal(0, BitConverter.ToUInt16(header, 8));           // stored
        }
        using (var s = mime.Open())
        using (var reader = new StreamReader(s))
            Assert.Equal("application/vnd.oasis.opendocument.base", reader.ReadToEnd());

        string Read(string name)
        {
            var entry = zip.GetEntry(name);
            Assert.NotNull(entry);
            using var s = entry!.Open();
            using var reader = new StreamReader(s);
            return reader.ReadToEnd();
        }

        var manifest = Read("META-INF/manifest.xml");
        Assert.Contains("application/vnd.oasis.opendocument.base", manifest);
        Assert.Contains("database/script", manifest);

        var content = Read("content.xml");
        Assert.Contains("sdbc:embedded:hsqldb", content);
        Assert.Contains("db:table-representation db:name=\"People\"", content);
        Assert.Contains("office:database", content);

        Assert.Equal("modified=no", Read("database/properties").Trim());

        var script = Read("database/script");
        Assert.Contains("CREATE USER SA", script);
        Assert.Contains("CREATE MEMORY TABLE PUBLIC.\"People\"", script);
        Assert.Contains("IDENTITY(START WITH 8)", script); // above max id (7)
        Assert.Contains("DATE", script);            // clean Joined column keeps its type
        Assert.Contains("VARCHAR(", script);         // junk Sales value forces the fallback
        Assert.Contains("INSERT INTO PUBLIC.\"People\"", script);
    }

    // ------------------------------------------------------------ round-trip

    [Fact]
    public void ExportThenImport_PreservesFieldsRecordsAndTrickyValues()
    {
        var source = SampleDocument();
        var path = _dir.File("roundtrip.odb");
        OdbExporter.Export(source, path);

        var back = OdbImporter.Import(path);

        Assert.Equal("People", back.Table.Name);
        Assert.Equal(new[] { "Name", "Joined", "Sales", "Notes" },
            back.Table.Fields.Select(f => f.Name).ToArray());
        Assert.Equal(FieldType.Date, back.Table.Fields[1].Type);
        // Sales had junk → exporter fell back to VARCHAR → imported as Text.
        Assert.Equal(FieldType.Text, back.Table.Fields[2].Type);
        Assert.Equal(FieldType.Text, back.Table.Fields[3].Type);

        Assert.Equal(2, back.Table.Records.Count);

        var first = back.Table.Records[0];
        Assert.Equal(1, first.Id);
        Assert.Equal("O'Brien \"the Bold\"", first.Get("Name"));
        Assert.Equal("1983-05-01", first.Get("Joined"));
        Assert.Equal("1200.5", first.Get("Sales"));
        Assert.Equal("line one\nline two", first.Get("Notes")); // newline survives

        var second = back.Table.Records[1];
        Assert.Equal(7, second.Id);
        Assert.Equal("Anne Brennan", second.Get("Name"));
        Assert.Equal(string.Empty, second.Get("Notes")); // NULL round-trips as empty
    }

    [Fact]
    public void Export_WithCleanTypedColumns_KeepsNumberAndDateTypes()
    {
        var file = OricFile.CreateNew(OricFileKind.Base, "Typed");
        file.Table.Name = "Metrics";
        file.Table.Fields.Add(new FieldDef("Amount", FieldType.Number));
        file.Table.Fields.Add(new FieldDef("When", FieldType.Date));
        var record = new RecordRow { Id = 1, Ordinal = 0 };
        record.Set("Amount", "42.75");
        record.Set("When", "2026-02-28");
        file.Table.Records.Add(record);

        var path = _dir.File("typed.odb");
        OdbExporter.Export(file, path);
        var back = OdbImporter.Import(path);

        Assert.Equal(FieldType.Number, back.Table.Fields[0].Type);
        Assert.Equal(FieldType.Date, back.Table.Fields[1].Type);
        Assert.Equal("42.75", back.Table.Records[0].Get("Amount"));
    }

    [Fact]
    public void Import_RejectsPackagesWithoutAScript()
    {
        var notAnOdb = _dir.File("plain.odt");
        OdfExporter.ExportOdt(OricFile.CreateNew(OricFileKind.Author, "x"), notAnOdb);

        Assert.Throws<OricFileException>(() => OdbImporter.Import(notAnOdb));
    }

    // ------------------------------------------------------------ view-model commands

    [Fact]
    public async Task ExportCommand_WritesOdbFile()
    {
        var vm = Create();
        vm.AddRecordCommand.Execute(null); // one record to export

        var target = _dir.File("module-export.odb");
        _dialogs.SavePath = target;
        await vm.ExportOdbCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target));
        using var zip = ZipFile.OpenRead(target);
        Assert.Contains(zip.Entries, e => e.FullName == "database/script");
        Assert.StartsWith("Exported", vm.StatusText);
    }

    [Fact]
    public async Task ImportCommand_LoadsAnOdbFile()
    {
        var path = _dir.File("incoming.odb");
        OdbExporter.Export(SampleDocument(), path);

        var vm = Create();
        _dialogs.OpenPath = path;
        await vm.ImportOdbCommand.ExecuteAsync(null);

        Assert.Equal("People", vm.Doc.Table.Name);
        Assert.Equal(4, vm.Fields.Count);
        Assert.Equal(2, vm.Records.Count);
        Assert.StartsWith("Imported", vm.StatusText);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task ImportCommand_Cancelled_LeavesDocumentAlone()
    {
        var vm = Create();
        _dialogs.OpenPath = null;
        vm.AddRecordCommand.Execute(null);

        await vm.ImportOdbCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Records.Count);
        Assert.True(vm.IsDirty);
    }
}
