using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OricOffice.Core.Model;
using OricOffice.Core.Seeders;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.Text;
using OricOffice.Core.Ui;

namespace OricOffice.Core.ViewModels;

/// <summary>
/// Oric Author: a simple word processor with word wrap and word count —
/// the two documented features of Tansoft's 1984 original.
/// </summary>
public sealed partial class AuthorViewModel : OricDocumentViewModelBase
{
    public const double MinFontSize = 9;
    public const double MaxFontSize = 40;
    public const double DefaultFontSize = 15;

    [ObservableProperty]
    private string body = string.Empty;

    [ObservableProperty]
    private double documentFontSize = DefaultFontSize;

    [ObservableProperty]
    private int cursorPosition;

    [ObservableProperty]
    private int selectionLength;

    private TextStats _stats;
    private readonly ITextClipboard _clipboard;

    public AuthorViewModel(
        IDialogService dialogs,
        IAppLifetimeService lifetime,
        ITextClipboard clipboard,
        OricFileStore store,
        AppMeta meta)
        : base(dialogs, lifetime, store, meta)
    {
        _clipboard = clipboard;
        UpdateStats();
    }

    // ------------------------------------------------------------ live statistics

    public string StatsText
        => $"{_stats.Words} words · {_stats.Characters} characters · {_stats.Paragraphs} paragraphs";

    partial void OnBodyChanged(string value)
    {
        MarkDirty();
        UpdateStats();
    }

    partial void OnDocumentFontSizeChanged(double value) => MarkDirty();

    private void UpdateStats()
    {
        _stats = WordCounter.Count(Body);
        OnPropertyChanged(nameof(StatsText));
        StatusText = StatsText;
    }

    // ------------------------------------------------------------ editing commands

    [RelayCommand]
    private void Cut()
    {
        if (!TextSelectionOps.HasSelection(Body, CursorPosition, SelectionLength)) return;
        var (text, cursor) = TextSelectionOps.ReplaceSelection(Body, CursorPosition, SelectionLength, string.Empty);
        Body = text;
        CursorPosition = cursor;
        SelectionLength = 0;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        var selection = TextSelectionOps.GetSelection(Body, CursorPosition, SelectionLength);
        if (selection.Length == 0) return;
        await _clipboard.SetTextAsync(selection);
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        var clip = await _clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(clip)) return;
        var (text, cursor) = TextSelectionOps.ReplaceSelection(Body, CursorPosition, SelectionLength, clip);
        Body = text;
        CursorPosition = cursor;
        SelectionLength = 0;
    }

    [RelayCommand]
    private void SelectAll()
    {
        var (cursor, length) = TextSelectionOps.SelectAll(Body);
        CursorPosition = cursor;
        SelectionLength = length;
    }

    [RelayCommand]
    private void ZoomIn() => DocumentFontSize = Math.Min(MaxFontSize, DocumentFontSize + 1);

    [RelayCommand]
    private void ZoomOut() => DocumentFontSize = Math.Max(MinFontSize, DocumentFontSize - 1);

    [RelayCommand]
    private void ZoomReset() => DocumentFontSize = DefaultFontSize;

    // ------------------------------------------------------------ document hooks

    protected override void ApplyDocument()
    {
        ContentSeeder.EnsureText(Doc.Text, Doc.Sheet, Doc.Table);
        Body = Doc.Text.Body;
        DocumentFontSize = DefaultFontSize;
        SelectionLength = 0;
        CursorPosition = 0;
    }

    protected override void ResetDocument()
    {
        Body = string.Empty;
        CursorPosition = 0;
        SelectionLength = 0;
    }

    protected override void CollectDocument()
    {
        Doc.Text.Body = Body;
        if (string.IsNullOrWhiteSpace(Doc.Title) || Doc.Title == "Untitled")
            Doc.Title = Title;
    }

    // ------------------------------------------------------------ chrome

    protected override IEnumerable<MenuGroup> BuildMenus()
    {
        yield return new MenuGroup("File",
            new MenuItemModel("New", command: NewCommand),
            new MenuItemModel("Open…", command: OpenCommand),
            new MenuItemModel("Save", command: SaveCommand),
            new MenuItemModel("Save As…", command: SaveAsCommand),
            MenuItemModel.Separator(),
            new MenuItemModel("Exit", command: ExitCommand));

        yield return new MenuGroup("Edit",
            new MenuItemModel("Cut", command: CutCommand),
            new MenuItemModel("Copy", command: CopyCommand),
            new MenuItemModel("Paste", command: PasteCommand),
            new MenuItemModel("Select All", command: SelectAllCommand),
            MenuItemModel.Separator(),
            new MenuItemModel("Zoom In", command: ZoomInCommand),
            new MenuItemModel("Zoom Out", command: ZoomOutCommand),
            new MenuItemModel("Actual Size", command: ZoomResetCommand));

        yield return new MenuGroup("Help",
            new MenuItemModel($"About {Meta.Name}", command: AboutCommand));
    }

    protected override IEnumerable<ToolButtonModel> BuildToolbar()
    {
        yield return new ToolButtonModel("New", SuiteIcon.New, NewCommand);
        yield return new ToolButtonModel("Open", SuiteIcon.Open, OpenCommand);
        yield return new ToolButtonModel("Save", SuiteIcon.Save, SaveCommand);
        yield return new ToolButtonModel("Save As", SuiteIcon.SaveAs, SaveAsCommand);
        yield return ToolButtonModel.Separator();
        yield return new ToolButtonModel("Cut", SuiteIcon.Cut, CutCommand);
        yield return new ToolButtonModel("Copy", SuiteIcon.Copy, CopyCommand);
        yield return new ToolButtonModel("Paste", SuiteIcon.Paste, PasteCommand);
        yield return ToolButtonModel.Separator();
        yield return new ToolButtonModel("Zoom Out", SuiteIcon.ZoomOut, ZoomOutCommand);
        yield return new ToolButtonModel("Zoom In", SuiteIcon.ZoomIn, ZoomInCommand);
    }
}
