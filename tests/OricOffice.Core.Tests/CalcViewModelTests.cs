using OricOffice.Core.Model;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.ViewModels;

namespace OricOffice.Core.Tests;

public class CalcViewModelTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeLifetime _lifetime = new();
    private readonly OricFileStore _store;

    public CalcViewModelTests() => _store = new OricFileStore(_dir.Path); // rooted: drafts/recents stay in the test dir

    public void Dispose() => _dir.Dispose();

    private CalcViewModel Create()
        => new(_dialogs, _lifetime, _store,
            new AppMeta("Oric Calc", "1.0.0", OricFileKind.Calc));

    private static void Enter(CalcViewModel vm, int row, int col, string input)
    {
        vm.SelectCell(vm.CellAt(row, col));
        vm.FormulaBarText = input;
        vm.CommitFormulaCommand.Execute(null);
    }

    // ------------------------------------------------------------ grid basics

    [Fact]
    public void NewGrid_StartsAtDefaultSizeWithA1Selected()
    {
        var vm = Create();
        Assert.Equal(SheetDocument.MinRows, vm.Rows);
        Assert.Equal(SheetDocument.MinColumns, vm.Columns);
        Assert.Equal("A1", vm.SelectedCell!.Address);
        Assert.Equal("A1", vm.NameBoxText);
    }

    [Fact]
    public void CommitFormula_ComputesAndDisplays()
    {
        var vm = Create();
        Enter(vm, 0, 0, "=1+2");
        Assert.Equal("3", vm.CellAt(0, 0).Display);
        Assert.Equal("=1+2", vm.CellAt(0, 0).Raw);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void LiteralTextCells_StoreVerbatim()
    {
        var vm = Create();
        Enter(vm, 0, 0, "hello");
        Assert.Equal("hello", vm.CellAt(0, 0).Display);
    }

    // ------------------------------------------------------------ floating images

    [Fact]
    public void ResizeImage_UpdatesSizeMarksDirtyAndClamps()
    {
        var vm = Create();
        var image = new SheetImage("photo.png", new byte[] { 1, 2, 3 }, 0, 0)
        {
            Width = 160,
            Height = 120,
        };
        vm.Doc.Sheet.Images.Add(image);

        vm.ResizeImage(image, 320, 240);
        Assert.Equal(320, image.Width);
        Assert.Equal(240, image.Height);
        Assert.True(vm.IsDirty);

        vm.ResizeImage(image, 5, 5); // clamped to the minimum
        Assert.Equal(24, image.Width);
        Assert.Equal(24, image.Height);
    }

    [Fact]
    public void ChangingAParentCell_RecalculatesDependents()
    {
        var vm = Create();
        Enter(vm, 0, 0, "5");
        Enter(vm, 1, 0, "=A1*2");
        Assert.Equal("10", vm.CellAt(1, 0).Display);

        Enter(vm, 0, 0, "7");
        Assert.Equal("14", vm.CellAt(1, 0).Display);
    }

    [Fact]
    public void AutoSum_FindsTheNumbersAbove()
    {
        var vm = Create();
        Enter(vm, 0, 0, "1");
        Enter(vm, 1, 0, "2");
        Enter(vm, 2, 0, "3");

        vm.SelectCell(vm.CellAt(3, 0));
        vm.AutoSumCommand.Execute(null);

        Assert.Equal("=SUM(A1:A3)", vm.CellAt(3, 0).Raw);
        Assert.Equal("6", vm.CellAt(3, 0).Display);
    }

    [Fact]
    public void ClearCell_RemovesInputAndValue()
    {
        var vm = Create();
        Enter(vm, 0, 0, "5");
        vm.SelectCell(vm.CellAt(0, 0));
        vm.ClearCellCommand.Execute(null);
        Assert.Equal(string.Empty, vm.CellAt(0, 0).Raw);
        Assert.Equal(string.Empty, vm.CellAt(0, 0).Display);
    }

    [Fact]
    public void GoToAddress_SelectsWithinBounds_AndRejectsBadInput()
    {
        var vm = Create();
        vm.NameBoxText = "B2";
        vm.GoToAddressCommand.Execute(null);
        Assert.Equal("B2", vm.SelectedCell!.Address);

        vm.NameBoxText = "not-a-cell";
        vm.GoToAddressCommand.Execute(null);
        Assert.Equal("B2", vm.SelectedCell!.Address);
        Assert.Contains("not a cell address", vm.StatusText);
    }

    [Fact]
    public void AddRowAndColumn_GrowTheGrid()
    {
        var vm = Create();
        var rows = vm.Rows;
        var cols = vm.Columns;
        vm.AddRowCommand.Execute(null);
        vm.AddColumnCommand.Execute(null);
        Assert.Equal(rows + 1, vm.Rows);
        Assert.Equal(cols + 1, vm.Columns);
        Assert.Equal(vm.Columns, vm.ColumnHeaders.Count);
    }

    // ------------------------------------------------------------ file lifecycle + interop

    [Fact]
    public async Task SaveAs_WritesAnOcaFile()
    {
        var vm = Create();
        Enter(vm, 0, 0, "=2*3");
        _dialogs.SavePath = _dir.File("sheet");

        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.EndsWith(".oca", vm.FilePath);
        var reloaded = _store.Load(vm.FilePath!);
        Assert.Equal(OricFileKind.Calc, reloaded.Kind);
        Assert.Equal("=2*3", reloaded.Sheet.Find(0, 0)!.Raw);
        Assert.Equal("6", reloaded.Sheet.ValueAt(0, 0));
    }

    [Fact]
    public async Task Open_AuthorFile_ShowsTextAsGrid_AndSaveKeepsAuthorContent()
    {
        var authorDoc = OricFile.CreateNew(OricFileKind.Author, "Notes");
        authorDoc.Text.Body = "first line\nsecond line";
        var path = _store.Save(authorDoc, _dir.File("notes"), OricFileKind.Author);

        var vm = Create();
        _dialogs.OpenPath = path;
        await vm.OpenCommand.ExecuteAsync(null);

        // Seeded from the author's text.
        Assert.Equal("first line", vm.CellAt(0, 0).Display);

        // Edit a cell, save as .oca, and the author text must survive.
        Enter(vm, 0, 2, "=100/4");
        _dialogs.SavePath = _dir.File("notes-as-calc");
        await vm.SaveAsCommand.ExecuteAsync(null);

        var reloaded = _store.Load(vm.FilePath!);
        Assert.Contains("second line", reloaded.Text.Body);
        Assert.Equal("25", reloaded.Sheet.ValueAt(0, 2));
    }

    [Fact]
    public async Task Open_RecalculatesFormulasOnLoad()
    {
        var doc = OricFile.CreateNew(OricFileKind.Calc, "Calc");
        doc.Sheet.Set(0, 0, "10", "10");
        doc.Sheet.Set(1, 0, "=A1*3", "999"); // stale cached value on purpose
        var path = _store.Save(doc, _dir.File("stale"), OricFileKind.Calc);

        var vm = Create();
        _dialogs.OpenPath = path;
        await vm.OpenCommand.ExecuteAsync(null);

        Assert.Equal("30", vm.CellAt(1, 0).Display); // recomputed, not the stale 999
    }

    [Fact]
    public void MenusAndToolbar_AreBuilt()
    {
        var vm = Create();
        Assert.Equal(new[] { "Home", "File", "Insert", "Cell", "Help" }, vm.Menus.Select(m => m.Header));
        Assert.Contains(vm.Toolbar, b => b.Text == "AutoSum");
        Assert.Contains(vm.Toolbar, b => b.Text == "+ Row");
    }
}
