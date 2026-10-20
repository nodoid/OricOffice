using OricOffice.Core.Model;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.ViewModels;

namespace OricOffice.Core.Tests;

public class BaseViewModelTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeLifetime _lifetime = new();
    private readonly OricFileStore _store = new();

    public void Dispose() => _dir.Dispose();

    private BaseViewModel Create()
        => new(_dialogs, _lifetime, _store,
            new AppMeta("Oric Base", "1.0.0", OricFileKind.Base));

    // ------------------------------------------------------------ schema + records

    [Fact]
    public void NewDocument_StartsWithOneField()
    {
        var vm = Create();
        Assert.Single(vm.Fields);
        Assert.Equal("Field1", vm.Fields[0].Name);
        Assert.True(vm.HasFields);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void AddField_CreatesUniqueNames()
    {
        var vm = Create();
        vm.AddFieldCommand.Execute(null);
        vm.AddFieldCommand.Execute(null);
        Assert.Equal(3, vm.Fields.Count);
        Assert.Equal(new[] { "Field1", "Field2", "Field3" }, vm.FieldNames);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void AddRecord_SelectsItAndEditsFlowIntoSummary()
    {
        var vm = Create();
        vm.AddRecordCommand.Execute(null);
        Assert.Single(vm.Records);
        Assert.NotNull(vm.SelectedRecord);
        Assert.True(vm.HasSelectedRecord);

        vm.SelectedRecord!.Cells[0].Value = "Ann Smith";
        Assert.Contains("Ann Smith", vm.SelectedRecord.Summary);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void DeleteRecord_RemovesSelected()
    {
        var vm = Create();
        vm.AddRecordCommand.Execute(null);
        vm.AddRecordCommand.Execute(null);
        Assert.Equal(2, vm.Records.Count);

        vm.DeleteRecordCommand.Execute(null);
        Assert.Single(vm.Records);
    }

    // ------------------------------------------------------------ query + sort

    [Fact]
    public void Query_FiltersRecords()
    {
        var vm = Create();
        vm.AddRecordCommand.Execute(null);
        vm.SelectedRecord!.Cells[0].Value = "Ann";
        vm.AddRecordCommand.Execute(null);
        vm.SelectedRecord!.Cells[0].Value = "Bob";
        Assert.Equal(2, vm.Records.Count);

        vm.QueryText = "Field1 contains \"Ann\"";
        vm.ApplyQueryCommand.Execute(null);
        Assert.Single(vm.Records);
        Assert.Contains("1 of 2", vm.StatusText);

        vm.ClearQueryCommand.Execute(null);
        Assert.Equal(2, vm.Records.Count);
    }

    [Fact]
    public void Query_WithUnknownField_ShowsError_AndKeepsRecords()
    {
        var vm = Create();
        vm.AddRecordCommand.Execute(null);
        vm.QueryText = "nope = 1";
        vm.ApplyQueryCommand.Execute(null);
        Assert.Single(vm.Records);
        Assert.Contains("Unknown field", vm.StatusText);
    }

    [Fact]
    public void SortDescending_OrdersNumerically()
    {
        var vm = Create();
        vm.AddFieldCommand.Execute(null); // Field2 (number column is a type choice, sorting is value-aware)
        vm.Fields[1].Type = FieldType.Number;
        vm.AddFieldCommand.Execute(null); // Field3 for uniqueness safety

        vm.AddRecordCommand.Execute(null);
        vm.SelectedRecord!.Cells[1].Value = "10";
        vm.AddRecordCommand.Execute(null);
        vm.SelectedRecord!.Cells[1].Value = "9";

        vm.SortFieldName = "Field2";
        vm.SortDescendingCommand.Execute(null);

        Assert.Equal("10", vm.Records[0].Cells[1].Value);
        Assert.Equal("9", vm.Records[1].Cells[1].Value);
    }

    // ------------------------------------------------------------ file lifecycle + interop

    [Fact]
    public async Task SaveAs_WritesAnObaFile_WithFieldsAndRecords()
    {
        var vm = Create();
        vm.AddFieldCommand.Execute(null);
        vm.Fields[0].Name = "City";
        vm.AddRecordCommand.Execute(null);
        vm.SelectedRecord!.Cells[0].Value = "Leeds";

        _dialogs.SavePath = _dir.File("cities");
        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.EndsWith(".oba", vm.FilePath);
        var reloaded = _store.Load(vm.FilePath!);
        Assert.Equal(OricFileKind.Base, reloaded.Kind);
        Assert.Equal("City", reloaded.Table.Fields[0].Name);
        Assert.Single(reloaded.Table.Records);
        Assert.Equal("Leeds", reloaded.Table.Records[0].Get("City"));
    }

    [Fact]
    public async Task Open_CalcFile_SeedsFieldsFromHeaderRow_AndSaveKeepsSheet()
    {
        var calcDoc = OricFile.CreateNew(OricFileKind.Calc, "Scores");
        calcDoc.Sheet.Set(0, 0, "Name", "Name");
        calcDoc.Sheet.Set(0, 1, "Score", "Score");
        calcDoc.Sheet.Set(1, 0, "Ann", "Ann");
        calcDoc.Sheet.Set(1, 1, "42", "42");
        var path = _store.Save(calcDoc, _dir.File("scores"), OricFileKind.Calc);

        var vm = Create();
        _dialogs.OpenPath = path;
        await vm.OpenCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Name", "Score" }, vm.FieldNames);
        Assert.Single(vm.Records);
        Assert.Equal("Ann", vm.SelectedRecord!.Cells[0].Value);

        vm.SelectedRecord!.Cells[0].Value = "Anna";
        _dialogs.SavePath = _dir.File("scores-as-base");
        await vm.SaveAsCommand.ExecuteAsync(null);

        var reloaded = _store.Load(vm.FilePath!);
        Assert.Equal("Anna", reloaded.Table.Records[0].Get("Name"));
        Assert.Equal("Ann", reloaded.Sheet.ValueAt(1, 0));  // original sheet untouched
        Assert.Equal("42", reloaded.Sheet.ValueAt(1, 1));
    }

    [Fact]
    public async Task Open_AuthorFile_CreatesLineRecords()
    {
        var authorDoc = OricFile.CreateNew(OricFileKind.Author, "Lines");
        authorDoc.Text.Body = "alpha\nbeta";
        var path = _store.Save(authorDoc, _dir.File("lines"), OricFileKind.Author);

        var vm = Create();
        _dialogs.OpenPath = path;
        await vm.OpenCommand.ExecuteAsync(null);

        Assert.Equal("Line", vm.Fields[0].Name);
        Assert.Equal(2, vm.Records.Count);
        Assert.Equal("alpha", vm.Records[0].Cells[0].Value);
    }

    [Fact]
    public async Task Save_PreservesRenameAndNewRecordsTogether()
    {
        var vm = Create();
        vm.Fields[0].Name = "City";
        vm.AddRecordCommand.Execute(null);
        vm.SelectedRecord!.Cells[0].Value = "York";

        _dialogs.SavePath = _dir.File("places");
        await vm.SaveAsCommand.ExecuteAsync(null);
        Assert.EndsWith(".oba", vm.FilePath);

        // Rename after saving, save again over the same path.
        vm.Fields[0].Name = "Town";
        await vm.SaveCommand.ExecuteAsync(null);

        var reloaded = _store.Load(_dir.File("places.oba"));
        Assert.Equal("Town", reloaded.Table.Fields[0].Name);
        Assert.Equal("York", reloaded.Table.Records[0].Get("Town"));
    }

    [Fact]
    public void MenusAndToolbar_AreBuilt()
    {
        var vm = Create();
        Assert.Equal(new[] { "File", "Table", "Record", "Query", "Help" }, vm.Menus.Select(m => m.Header));
        Assert.Contains(vm.Toolbar, b => b.Text == "New Record");
        Assert.Contains(vm.Toolbar, b => b.Text == "+ Field");
    }
}
