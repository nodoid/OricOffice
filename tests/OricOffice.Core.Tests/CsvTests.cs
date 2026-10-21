using OricOffice.Core;
using OricOffice.Core.Export;
using OricOffice.Core.IO;
using OricOffice.Core.Model;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.ViewModels;

namespace OricOffice.Core.Tests;

public class CsvTests : IDisposable
{
    private readonly List<TempDir> _dirs = new();

    public void Dispose()
    {
        foreach (var dir in _dirs) dir.Dispose();
    }

    // ------------------------------------------------------------ codec

    [Fact]
    public void Parse_SimpleRows()
    {
        var rows = Csv.Parse("a,b,c\n1,2,3");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
    }

    [Fact]
    public void Parse_QuotedFields_WithCommasQuotesNewlines()
    {
        var rows = Csv.Parse("a,\"b,c\",\"say \"\"hi\"\"\",\"multi\nline\"\r\nd,e");
        Assert.Equal("b,c", rows[0][1]);
        Assert.Equal("say \"hi\"", rows[0][2]);
        Assert.Equal("multi\nline", rows[0][3]);
        Assert.Equal(new[] { "d", "e" }, rows[1]);
    }

    [Fact]
    public void Parse_TrailingNewline_DoesNotAddEmptyRow()
    {
        Assert.Single(Csv.Parse("only\r\n"));
        Assert.Single(Csv.Parse("only\n"));
        Assert.Empty(Csv.Parse(""));
    }

    [Fact]
    public void RoundTrip_PreservesEveryValue()
    {
        var source = new[]
        {
            new[] { "Name", "Note", "Price" },
            new[] { "Bolt", "has, comma", "1.5" },
            new[] { "Nut", "has \"quotes\"\nand newline", "" },
        };

        var text = Csv.Write(source);
        var back = Csv.Parse(text);

        Assert.Equal(source.Length, back.Count);
        for (int r = 0; r < source.Length; r++)
            Assert.Equal(source[r], back[r]);
    }

    // ------------------------------------------------------------ Calc

    [Fact]
    public async Task Calc_ExportCsv_WritesValues()
    {
        var dir = NewDir();
        var dialogs = new FakeDialogs();
        var vm = NewCalc(dialogs, dir);

        SetCell(vm, 0, 0, "Name");
        SetCell(vm, 0, 1, "Amount");
        SetCell(vm, 1, 0, "Widgets");
        SetCell(vm, 1, 1, "7");

        dialogs.SavePath = dir.File("sheet.csv");
        await vm.ExportCsvCommand.ExecuteAsync(null);

        Assert.NotNull(dialogs.SavePath);
        var rows = Csv.Parse(File.ReadAllText(dialogs.SavePath!));
        Assert.Equal(new[] { "Name", "Amount" }, rows[0]);
        Assert.Equal(new[] { "Widgets", "7" }, rows[1]);
    }

    [Fact]
    public async Task Calc_ExportCsv_WritesFormulaResults()
    {
        var dir = NewDir();
        var dialogs = new FakeDialogs();
        var vm = NewCalc(dialogs, dir);

        SetCell(vm, 0, 0, "2");
        SetCell(vm, 1, 0, "3");
        SetCell(vm, 2, 0, "=A1+A2");

        dialogs.SavePath = dir.File("sum.csv");
        await vm.ExportCsvCommand.ExecuteAsync(null);

        var rows = Csv.Parse(File.ReadAllText(dialogs.SavePath!));
        Assert.Equal("5", rows[2][0]);
    }

    [Fact]
    public async Task Calc_ImportCsv_FillsTheSheet()
    {
        var dir = NewDir();
        var dialogs = new FakeDialogs();
        var vm = NewCalc(dialogs, dir);

        var path = dir.File("in.csv");
        File.WriteAllText(path, "City,Pop\nRome,1\n\"Paris, City\",2");

        dialogs.OpenPath = path;
        await vm.ImportCsvCommand.ExecuteAsync(null);

        Assert.Equal("City", vm.Doc.Sheet.RawAt(0, 0));
        Assert.Equal("Pop", vm.Doc.Sheet.RawAt(0, 1));
        Assert.Equal("Rome", vm.Doc.Sheet.RawAt(1, 0));
        Assert.Equal("Paris, City", vm.Doc.Sheet.RawAt(2, 0));
        Assert.Equal("2", vm.Doc.Sheet.RawAt(2, 1));
        Assert.Equal(3, vm.Doc.Sheet.UsedRowCount);
        Assert.Equal(2, vm.Doc.Sheet.UsedColumnCount);
        Assert.Equal("Rome", vm.CellAt(1, 0).Display); // grid rebuilt from the import
    }

    // ------------------------------------------------------------ Base

    [Fact]
    public void Base_ExportCsv_WritesHeaderAndRecords()
    {
        var file = OricFile.CreateNew(OricFileKind.Base, "T");
        file.Table.Fields.Add(new FieldDef("Name"));
        file.Table.Fields.Add(new FieldDef("Qty", FieldType.Number));
        var r1 = file.Table.AddRecord();
        r1.Set("Name", "Bolt");
        r1.Set("Qty", "12");
        var r2 = file.Table.AddRecord();
        r2.Set("Name", "has, comma");
        r2.Set("Qty", "3");

        var rows = Csv.Parse(BaseCsv.Export(file));
        Assert.Equal(new[] { "Name", "Qty" }, rows[0]);
        Assert.Equal(new[] { "Bolt", "12" }, rows[1]);
        Assert.Equal(new[] { "has, comma", "3" }, rows[2]);
    }

    [Fact]
    public async Task Base_ImportCsv_RebuildsFieldsAndInfersNumbers()
    {
        var dir = NewDir();
        var dialogs = new FakeDialogs();
        var vm = NewBase(dialogs, dir);

        var path = dir.File("in.csv");
        File.WriteAllText(path, "Name,Qty,When\nBolt,12,early\nNut,3,late\n,\"\", ");

        dialogs.OpenPath = path;
        await vm.ImportCsvCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Name", "Qty", "When" }, vm.Doc.Table.Fields.Select(f => f.Name));
        Assert.Equal(FieldType.Number, vm.Doc.Table.Fields[1].Type);
        Assert.Equal(FieldType.Text, vm.Doc.Table.Fields[2].Type);
        Assert.Equal(2, vm.Doc.Table.Records.Count); // blank row dropped
        Assert.Equal("Bolt", vm.Doc.Table.Records[0].Get("Name"));
        Assert.Equal("late", vm.Doc.Table.Records[1].Get("When"));
    }

    // ------------------------------------------------------------ fixtures

    private TempDir NewDir()
    {
        var dir = new TempDir();
        _dirs.Add(dir);
        return dir;
    }

    private static void SetCell(CalcViewModel vm, int row, int column, string raw)
    {
        vm.SelectCell(vm.CellAt(row, column));
        vm.FormulaBarText = raw;
        vm.CommitFormulaCommand.Execute(null);
    }

    private static CalcViewModel NewCalc(FakeDialogs dialogs, TempDir dir)
        => new(dialogs, new FakeLifetime(), new OricFileStore(dir.Path),
            new AppMeta("Oric Calc", "1.0.0", OricFileKind.Calc));

    private static BaseViewModel NewBase(FakeDialogs dialogs, TempDir dir)
        => new(dialogs, new FakeLifetime(), new OricFileStore(dir.Path),
            new AppMeta("Oric Base", "1.0.0", OricFileKind.Base));
}
