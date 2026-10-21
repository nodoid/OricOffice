using OricOffice.Core.Export;
using OricOffice.Core.IO;
using OricOffice.Core.Model;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.Tape;
using OricOffice.Core.ViewModels;

namespace OricOffice.Core.Tests;

/// <summary>
/// The tape catalogue fixture (tests/fixtures/tape-catalog.csv) is generated
/// from the real 1984 Tansoft tapes that ship inside the Oricotron emulator
/// (tools/FixtureGen). These tests prove the committed file imports into both
/// Calc and Base, round-trips through CSV export, and still matches the real
/// tapes on machines where the emulator is installed.
/// </summary>
public class TapeCatalogFixtureTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeLifetime _lifetime = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Locates tests/fixtures by walking up from the test assembly.</summary>
    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Fixture '{name}' not found — regenerate with: dotnet run --project tools/FixtureGen -- tests/fixtures");
    }

    private static string? RealTapeDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "Oricutron_win32_v12", "tapes");
        return Directory.Exists(dir) ? dir : null;
    }

    private CalcViewModel NewCalc()
        => new(_dialogs, _lifetime, new OricFileStore(_dir.Path),
            new AppMeta("Oric Calc", "1.0.0", OricFileKind.Calc));

    private BaseViewModel NewBase()
        => new(_dialogs, _lifetime, new OricFileStore(_dir.Path),
            new AppMeta("Oric Base", "1.0.0", OricFileKind.Base));

    // ------------------------------------------------------------ fixture contents

    [Fact]
    public void Fixture_CataloguesTheRealTansoftTapes()
    {
        var rows = Csv.Parse(File.ReadAllText(Fixture("tape-catalog.csv")));

        Assert.Equal(new[] { "Tape", "File", "Type", "Load", "End", "Bytes" }, rows[0]);
        Assert.True(rows.Count > 5, "expected one row per file on the tapes");

        // Known facts about the shipped tapes.
        Assert.Contains(rows, r => r[0] == "author" && r[1] == "AUTEUR" && r[2] == "BLOCK" && r[5] == "10241");
        Assert.Contains(rows, r => r[0] == "oriccalc" && r[1] == "ORIC-CALC" && r[2] == "BASIC" && r[5] == "14009");
        Assert.Contains(rows, r => r[0] == "base" && r[1] == "ORICBASE" && r[2] == "BASIC" && r[5] == "13193");

        // Addresses and sizes are numeric so Base can type the columns.
        foreach (var row in rows.Skip(1))
            foreach (var field in row.Skip(3))
                Assert.True(int.TryParse(field, out _), $"'{field}' should be a plain integer");
    }

    // ------------------------------------------------------------ Calc import

    [Fact]
    public async Task CalcModule_ImportsTheCatalogAndRoundTrips()
    {
        var vm = NewCalc();
        _dialogs.OpenPath = Fixture("tape-catalog.csv");
        await vm.ImportCsvCommand.ExecuteAsync(null);

        // Row 1 of the CSV becomes row 1 of the grid.
        Assert.Equal("Tape", vm.CellAt(0, 0).Display);
        Assert.Equal("Bytes", vm.CellAt(0, 5).Display);
        Assert.Contains("ORIC-CALC", Enumerable.Range(0, vm.Rows)
            .Select(r => vm.CellAt(r, 1).Display));

        // Exporting the imported grid reproduces the fixture content.
        var exported = CalcCsv.Export(vm.Doc.Sheet).Replace("\r\n", "\n");
        var committed = File.ReadAllText(Fixture("tape-catalog.csv")).Replace("\r\n", "\n");
        Assert.Equal(committed, exported);
    }

    // ------------------------------------------------------------ Base import

    [Fact]
    public async Task BaseModule_ImportsTheCatalogAsTypedFields()
    {
        var vm = NewBase();
        _dialogs.OpenPath = Fixture("tape-catalog.csv");
        await vm.ImportCsvCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Tape", "File", "Type", "Load", "End", "Bytes" }, vm.FieldNames);
        Assert.Equal(FieldType.Text, vm.Fields[0].Type);
        Assert.Equal(FieldType.Number, vm.Fields[3].Type);
        Assert.Equal(FieldType.Number, vm.Fields[5].Type);
        Assert.Equal(8, vm.Records.Count);
        Assert.Contains(vm.Records, r => r.Summary.Contains("ORICBASE"));

        // Re-export keeps the header and every record.
        var rows = Csv.Parse(BaseCsv.Export(vm.Doc));
        Assert.Equal(new[] { "Tape", "File", "Type", "Load", "End", "Bytes" }, rows[0]);
        Assert.Equal(9, rows.Count);
    }

    // ------------------------------------------------------------ honesty check

    [Fact]
    public void WhenOricutronIsInstalled_TheFixtureStillMatchesTheRealTapes()
    {
        var tapeDir = RealTapeDir();
        if (tapeDir is null) return; // emulator not on this machine

        var committed = File.ReadAllText(Fixture("tape-catalog.csv")).Replace("\r\n", "\n");

        // Only the tapes the fixture catalogues: the emulator folder may
        // collect further .tap files over time (our own experiment runs).
        var wanted = Csv.Parse(committed).Skip(1)
            .Select(r => r[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var tapes = TapeCatalog.ReadDirectory(tapeDir)
            .Where(t => wanted.Contains(t.TapeName, StringComparer.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(tapes);

        var rebuilt = TapeCatalog.BuildCsv(tapes).Replace("\r\n", "\n");
        Assert.Equal(committed, rebuilt);
    }
}
