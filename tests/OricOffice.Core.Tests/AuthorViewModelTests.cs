using OricOffice.Core.Model;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.ViewModels;

namespace OricOffice.Core.Tests;

public class AuthorViewModelTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeLifetime _lifetime = new();
    private readonly OricFileStore _store = new();

    public void Dispose() => _dir.Dispose();

    private AuthorViewModel Create()
        => new(_dialogs, _lifetime, _clipboard, _store,
            new AppMeta("Oric Author", "1.0.0", OricFileKind.Author));

    // ------------------------------------------------------------ editing + stats

    [Fact]
    public void NewDocument_StartsBlankWithLiveWordCount()
    {
        var vm = Create();
        Assert.Equal("0 words · 0 characters · 0 paragraphs", vm.StatsText);

        vm.Body = "Hello brave world";
        Assert.Contains("3 words", vm.StatsText);
        Assert.Contains("17 characters", vm.StatsText);
        Assert.True(vm.IsDirty);
        Assert.Equal("Hello brave world", vm.Body);
    }

    [Fact]
    public void Zoom_IsClamped()
    {
        var vm = Create();
        for (int i = 0; i < 100; i++) vm.ZoomInCommand.Execute(null);
        Assert.Equal(AuthorViewModel.MaxFontSize, vm.DocumentFontSize);
        for (int i = 0; i < 100; i++) vm.ZoomOutCommand.Execute(null);
        Assert.Equal(AuthorViewModel.MinFontSize, vm.DocumentFontSize);
        vm.ZoomResetCommand.Execute(null);
        Assert.Equal(AuthorViewModel.DefaultFontSize, vm.DocumentFontSize);
    }

    // ------------------------------------------------------------ clipboard ops

    [Fact]
    public async Task Copy_PlacesSelectionOnClipboard()
    {
        var vm = Create();
        vm.Body = "Hello world";
        vm.CursorPosition = 6;
        vm.SelectionLength = 5;

        await vm.CopyCommand.ExecuteAsync(null);
        Assert.Equal("world", _clipboard.Text);
    }

    [Fact]
    public async Task Cut_RemovesSelection()
    {
        var vm = Create();
        vm.Body = "Hello world";
        vm.CursorPosition = 6;
        vm.SelectionLength = 5;

        vm.CutCommand.Execute(null);
        Assert.Equal("Hello ", vm.Body);
        Assert.Equal(6, vm.CursorPosition);
        Assert.Equal(0, vm.SelectionLength);
    }

    [Fact]
    public async Task Paste_ReplacesSelectionFromClipboard()
    {
        var vm = Create();
        vm.Body = "Hello world";
        vm.CursorPosition = 6;
        vm.SelectionLength = 5;
        _clipboard.Text = "earth";

        await vm.PasteCommand.ExecuteAsync(null);
        Assert.Equal("Hello earth", vm.Body);
        Assert.Equal(11, vm.CursorPosition);
    }

    [Fact]
    public void SelectAll_SelectsWholeBody()
    {
        var vm = Create();
        vm.Body = "abcdef";
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(0, vm.CursorPosition);
        Assert.Equal(6, vm.SelectionLength);
    }

    // ------------------------------------------------------------ file lifecycle

    [Fact]
    public async Task SaveAs_WritesAnOauFile_AndClearsDirty()
    {
        var vm = Create();
        vm.Body = "Letter body";
        var path = _dir.File("letter"); // no extension yet

        _dialogs.SavePath = path;
        await vm.SaveAsCommand.ExecuteAsync(null);

        Assert.NotNull(vm.FilePath);
        Assert.EndsWith(".oau", vm.FilePath);
        Assert.True(File.Exists(vm.FilePath));
        Assert.False(vm.IsDirty);

        var reloaded = _store.Load(vm.FilePath!);
        Assert.Equal("Letter body", reloaded.Text.Body);
        Assert.Equal(OricFileKind.Author, reloaded.Kind);
    }

    [Fact]
    public async Task SaveAs_ReplacesForeignExtensionWithOwn()
    {
        var vm = Create();
        vm.Body = "text";
        _dialogs.SavePath = _dir.File("doc.oca"); // user picked a Calc name

        await vm.SaveAsCommand.ExecuteAsync(null);
        Assert.EndsWith(".oau", vm.FilePath);
        Assert.False(File.Exists(_dir.File("doc.oca")));
        Assert.True(File.Exists(_dir.File("doc.oau")));
    }

    [Fact]
    public async Task Open_InvalidFile_ShowsFriendlyError()
    {
        var junk = _dir.File("broken.oau");
        File.WriteAllText(junk, "not a database at all");

        var vm = Create();
        _dialogs.OpenPath = junk;
        await vm.OpenCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Messages, m => m.Title == "Cannot open file");
        Assert.True(vm.FilePath is null);
    }

    [Fact]
    public async Task Open_CalcFile_SeedsText_AndSavingKeepsSheetData()
    {
        // A Calc-authored file with only sheet content.
        var calcDoc = OricFile.CreateNew(OricFileKind.Calc, "Numbers");
        calcDoc.Sheet.Set(0, 0, "Revenue", "Revenue");
        calcDoc.Sheet.Set(1, 0, "42", "42");
        var calcPath = _store.Save(calcDoc, _dir.File("numbers"), OricFileKind.Calc);

        var vm = Create();
        _dialogs.OpenPath = calcPath;
        await vm.OpenCommand.ExecuteAsync(null);

        // Author sees the sheet content as text.
        Assert.Contains("Revenue", vm.Body);
        Assert.False(vm.IsDirty);

        // …edits and saves (same path, but the Author app keeps its own extension semantics).
        vm.Body += "\nAdded by Author";
        _dialogs.SavePath = _dir.File("numbers-copy");
        await vm.SaveAsCommand.ExecuteAsync(null);

        var reloaded = _store.Load(vm.FilePath!);
        Assert.Contains("Added by Author", reloaded.Text.Body);
        Assert.Equal("42", reloaded.Sheet.ValueAt(1, 0)); // sheet untouched
    }

    [Fact]
    public async Task New_AskBeforeDiscardingDirtyChanges()
    {
        var vm = Create();
        vm.Body = "unsaved work";
        _dialogs.ConfirmResult = false;

        await vm.NewCommand.ExecuteAsync(null);
        Assert.Equal(1, _dialogs.ConfirmCount);
        Assert.Equal("unsaved work", vm.Body); // cancelled → unchanged

        _dialogs.ConfirmResult = true;
        await vm.NewCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, vm.Body);
        Assert.False(vm.IsDirty);
        Assert.Null(vm.FilePath);
    }

    [Fact]
    public async Task Exit_QuitsOnlyWhenUserConfirmsDiscard()
    {
        var vm = Create();
        vm.Body = "dirty";
        _dialogs.ConfirmResult = false;
        await vm.ExitCommand.ExecuteAsync(null);
        Assert.False(_lifetime.Exited);

        _dialogs.ConfirmResult = true;
        await vm.ExitCommand.ExecuteAsync(null);
        Assert.True(_lifetime.Exited);
    }

    // ------------------------------------------------------------ chrome model

    [Fact]
    public void MenusAndToolbar_AreBuilt()
    {
        var vm = Create();
        Assert.Equal(new[] { "File", "Edit", "Help" }, vm.Menus.Select(m => m.Header));
        Assert.Contains(vm.Toolbar, b => b.Text == "Save As");
        Assert.Contains(vm.Toolbar, b => b.Text == "Cut");
        Assert.Contains(vm.Menus, g => g.Items.Any(i => i.Header == "Exit"));
    }
}
