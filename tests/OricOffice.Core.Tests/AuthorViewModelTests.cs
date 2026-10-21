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
    private readonly OricFileStore _store;

    public AuthorViewModelTests() => _store = new OricFileStore(_dir.Path); // rooted: drafts/recents stay in the test dir

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

    [Fact]
    public async Task Exit_WhenHosted_ReturnsToStartInsteadOfQuitting()
    {
        var vm = Create();
        var returned = 0;
        vm.ReturnToStart = () => { returned++; return Task.CompletedTask; };

        vm.Body = "draft to discard";
        _dialogs.ConfirmResult = true;
        await vm.ExitCommand.ExecuteAsync(null);

        Assert.Equal(1, returned);      // left the module…
        Assert.False(_lifetime.Exited); // …without killing the app
        Assert.False(vm.IsDirty);       // fresh document for the next visit
        Assert.Equal(string.Empty, vm.Body);
        Assert.Null(vm.FilePath);
    }

    [Fact]
    public async Task Exit_WhenHosted_KeepsEditingIfDiscardDeclined()
    {
        var vm = Create();
        var returned = 0;
        vm.ReturnToStart = () => { returned++; return Task.CompletedTask; };

        vm.Body = "still working";
        _dialogs.ConfirmResult = false;
        await vm.ExitCommand.ExecuteAsync(null);

        Assert.Equal(0, returned);
        Assert.Equal("still working", vm.Body);
        Assert.True(vm.IsDirty);
    }

    // ------------------------------------------------------------ image import (drag & drop payload)

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task ImportImageFromPath_InsertsTheImageAndRaisesElementsChanged()
    {
        var vm = Create();
        var path = _dir.File("dropped.png");
        File.WriteAllBytes(path, TinyPng);
        var changes = 0;
        vm.ElementsChanged += () => changes++;

        await vm.ImportImageFromPathAsync(path);

        var image = Assert.Single(vm.Doc.Text.Images);
        Assert.Equal("dropped.png", image.Name);
        Assert.Equal(ImageFormat.Png, image.Format);
        Assert.True(vm.IsDirty);
        Assert.Equal(1, changes);
        Assert.StartsWith("Inserted", vm.StatusText);
    }

    [Fact]
    public async Task ImportImageFromPath_RejectsNonImagesWithAMessage()
    {
        var vm = Create();
        var path = _dir.File("not-really.png");
        File.WriteAllText(path, "plain text pretending to be an image");

        await vm.ImportImageFromPathAsync(path);

        Assert.Empty(vm.Doc.Text.Images);
        Assert.False(vm.IsDirty);
        Assert.Contains(_dialogs.Messages, m => m.Title == "Insert image");
    }

    // ------------------------------------------------------------ chrome model

    [Fact]
    public void MenusAndToolbar_AreBuilt()
    {
        var vm = Create();
        Assert.Equal(new[] { "Home", "File", "Insert", "Edit", "Format", "Help" }, vm.Menus.Select(m => m.Header));
        Assert.Contains(vm.Toolbar, b => b.Text == "Save As");
        Assert.Contains(vm.Toolbar, b => b.Text == "Cut");
        Assert.Contains(vm.Toolbar, b => b.Text == "Image"); // image insert lives on the banner bar
        Assert.Contains(vm.Toolbar, b => b.Text == "Add Table");
        Assert.Contains(vm.Toolbar, b => b.Text == "Table Designer");
        Assert.Contains(vm.Menus, g => g.Items.Any(i => i.Header == "Exit"));

        // Export / Print submenus (PDF, ODT, printer, print-to-file).
        var file = vm.Menus.First(m => m.Header == "File");
        var export = file.Items.First(i => i.Header == "Export");
        Assert.Contains(export.Children!, c => c.Header == "PDF…");
        Assert.Contains(export.Children!, c => c.Header == "OpenDocument (.odt)…");
        var print = file.Items.First(i => i.Header == "Print");
        Assert.Contains(print.Children!, c => c.Header == "Print…");
        Assert.Contains(print.Children!, c => c.Header == "Print to File…");
        Assert.Equal("Ctrl+P", print.Children!.First(c => c.Header == "Print…").Accelerator);
        Assert.Contains(vm.Toolbar, b => b.Text == "Print");
        Assert.Contains(vm.Toolbar, b => b.Text == "Print to File");

        // Insert menu carries image import and table creation.
        var insert = vm.Menus.First(m => m.Header == "Insert");
        Assert.Contains(insert.Items, i => i.Header == "Image…");
        Assert.Contains(insert.Items, i => i.Header == "Table");

        // Format menu carries the character styling with accelerators.
        var format = vm.Menus.First(m => m.Header == "Format");
        Assert.Equal("Ctrl+B", format.Items.First(i => i.Header == "Bold").Accelerator);
        Assert.Equal("Ctrl+I", format.Items.First(i => i.Header == "Italic").Accelerator);
        Assert.Equal("Ctrl+U", format.Items.First(i => i.Header == "Underline").Accelerator);
        Assert.Contains(format.Items, i => i.Header == "Strikethrough");
        Assert.Contains(format.Items, i => i.Header == "Superscript");
        Assert.Contains(format.Items, i => i.Header == "Subscript");
    }

    // ------------------------------------------------------------ character formatting

    [Fact]
    public void ToggleBold_WithSelection_StylesTheSelection()
    {
        var vm = Create();
        vm.Body = "Hello world";
        vm.CursorPosition = 0;
        vm.SelectionLength = 5;

        vm.ToggleBoldCommand.Execute(null);

        var run = Assert.Single(vm.Doc.Text.Runs);
        Assert.Equal((0, 5), (run.Start, run.Length));
        Assert.True(vm.IsBold);
        Assert.True(vm.IsDirty);

        vm.ToggleBoldCommand.Execute(null); // toggle off drops the run entirely
        Assert.Empty(vm.Doc.Text.Runs);
        Assert.False(vm.IsBold);
    }

    [Fact]
    public void ToggleBold_WithoutSelection_ArmsPendingStyleForTyping()
    {
        var vm = Create();
        vm.Body = "Hello world";
        vm.CursorPosition = 5;
        vm.SelectionLength = 0;

        vm.ToggleBoldCommand.Execute(null);
        Assert.True(vm.IsBold);
        Assert.Empty(vm.Doc.Text.Runs); // nothing styled yet — typing will be

        vm.Body = vm.Body.Insert(5, "now "); // typing at the caret

        var run = Assert.Single(vm.Doc.Text.Runs);
        Assert.Equal("now ", vm.Body[run.Start..run.End]);
        Assert.Equal(TextStyleFlags.Bold, run.Style.Flags);
    }

    [Fact]
    public void SetForeground_WithSelection_AndSuperscriptExclusivity()
    {
        var vm = Create();
        vm.Body = "word";
        vm.CursorPosition = 0;
        vm.SelectionLength = 4;

        vm.SetForegroundCommand.Execute("#C00000");
        Assert.Equal("#C00000", vm.Doc.Text.StyleAt(0)?.Foreground);
        Assert.Equal("#C00000", vm.SelectedForeground);

        vm.ToggleSuperscriptCommand.Execute(null);
        Assert.True(vm.IsSuperscript);
        Assert.False(vm.IsSubscript);

        vm.ToggleSubscriptCommand.Execute(null);
        Assert.False(vm.IsSuperscript);
        Assert.True(vm.IsSubscript);
    }

    [Fact]
    public void FontAndSize_ApplyToSelection_AndShowInToolbarState()
    {
        var vm = Create();
        vm.Body = "abc";
        vm.CursorPosition = 0;
        vm.SelectionLength = 3;

        vm.SetFontFamilyCommand.Execute("Georgia");
        vm.SetFontSizeCommand.Execute(20d);

        Assert.Equal("Georgia", vm.Doc.Text.StyleAt(1)?.FontFamily);
        Assert.Equal(20, vm.Doc.Text.StyleAt(2)?.FontSize);
        Assert.Equal("Georgia", vm.SelectedFontFamily);
        Assert.Equal(20, vm.SelectedFontSize);

        vm.SetBackgroundCommand.Execute("#FFF2CC");
        Assert.Equal("#FFF2CC", vm.Doc.Text.StyleAt(0)?.Background);
        Assert.Equal("#FFF2CC", vm.SelectedBackground);
    }

    [Fact]
    public async Task StyledText_SurvivesLaterTyping_AndPersists()
    {
        var vm = Create();
        vm.Body = "bold text";
        vm.CursorPosition = 0;
        vm.SelectionLength = 4;
        vm.ToggleBoldCommand.Execute(null);

        vm.CursorPosition = 9;
        vm.SelectionLength = 0;
        vm.Body = "bold text!"; // typing after the plain last char stays plain

        Assert.True(vm.Doc.Text.StyleAt(0)?.Flags.HasFlag(TextStyleFlags.Bold));
        Assert.Null(vm.Doc.Text.StyleAt(9));

        _dialogs.SavePath = _dir.File("styled");
        await vm.SaveAsCommand.ExecuteAsync(null);

        var reloaded = _store.Load(vm.FilePath!);
        Assert.Equal(TextStyleFlags.Bold, reloaded.Text.Runs.Single().Style.Flags);
        Assert.Equal((0, 4), (reloaded.Text.Runs[0].Start, reloaded.Text.Runs[0].Length));
    }

    [Fact]
    public void TableDesigner_TogglesOpen()
    {
        var vm = Create();
        Assert.False(vm.IsTableDesignerOpen);
        vm.ToggleTableDesignerCommand.Execute(null);
        Assert.True(vm.IsTableDesignerOpen);
        vm.ToggleTableDesignerCommand.Execute(null);
        Assert.False(vm.IsTableDesignerOpen);
    }
}
