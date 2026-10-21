using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OricOffice.Core.Export;
using OricOffice.Core.Localization;
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

    /// <summary>
    /// The editable text — a live proxy over <see cref="TextDocument.Body"/>, so the
    /// character-style runs stay aligned with every caret edit, paste and deletion.
    /// </summary>
    public string Body
    {
        get => Doc.Text.Body;
        set
        {
            var text = value ?? string.Empty;
            if (Doc.Text.Body == text) return;
            Doc.Text.Body = text; // run offsets are repaired by the splice here
            OnPropertyChanged();
            MarkDirty();
            UpdateStats();
        }
    }

    [ObservableProperty]
    private double documentFontSize = DefaultFontSize;

    [ObservableProperty]
    private int cursorPosition;

    [ObservableProperty]
    private int selectionLength;

    // Character formatting state mirrored by the format bar.
    [ObservableProperty] private string? selectedFontFamily;
    [ObservableProperty] private double selectedFontSize = DefaultFontSize;
    [ObservableProperty] private string? selectedForeground;
    [ObservableProperty] private string? selectedBackground;
    [ObservableProperty] private bool isBold;
    [ObservableProperty] private bool isItalic;
    [ObservableProperty] private bool isUnderline;
    [ObservableProperty] private bool isStrikethrough;
    [ObservableProperty] private bool isSuperscript;
    [ObservableProperty] private bool isSubscript;
    [ObservableProperty] private bool isTableDesignerOpen;
    [ObservableProperty] private int styleRevision;

    /// <summary>Style armed for subsequently typed text (lives on the document).</summary>
    private TextStyle? PendingStyle
    {
        get => Doc.Text.PendingStyle;
        set => Doc.Text.PendingStyle = value;
    }

    private TextStats _stats;
    private readonly ITextClipboard _clipboard;

    public AuthorViewModel(
        IDialogService dialogs,
        IAppLifetimeService lifetime,
        ITextClipboard clipboard,
        OricFileStore store,
        AppMeta meta,
        IPrintService? printer = null)
        : base(dialogs, lifetime, store, meta, printer)
    {
        _clipboard = clipboard;
        UpdateStats();
    }

    // ------------------------------------------------------------ images & tables

    /// <summary>Raised after the embedded image or table collections change.</summary>
    public event Action? ElementsChanged;

    private void RaiseElementsChanged() => ElementsChanged?.Invoke();

    [RelayCommand]
    private async Task ImportImageAsync()
    {
        var path = await Dialogs.PickOpenPathAsync(Loc.T("Dialog.InsertImage"), ".png", ".jpg", ".jpeg");
        if (path is null) return;
        await ImportImageFromPathAsync(path);
    }

    /// <summary>
    /// Inserts an image from a file path — shared by the picker command and by
    /// drag-and-drop onto the page.
    /// </summary>
    public async Task ImportImageFromPathAsync(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (!ImageDetector.IsSupported(data))
            {
                await Dialogs.MessageAsync(Loc.T("Dialog.InsertImage"),
                    Loc.T("Dialog.InsertImage.NotAnImage"));
                return;
            }
            Doc.Text.Images.Add(new DocumentImage(Path.GetFileName(path), data));
            MarkDirty();
            RaiseElementsChanged();
            StatusText = Loc.Format("Status.InsertedImage", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(Loc.T("Dialog.InsertImage"), ex.Message);
        }
    }

    [RelayCommand]
    private void RemoveImage(int index)
    {
        if (index < 0 || index >= Doc.Text.Images.Count) return;
        Doc.Text.Images.RemoveAt(index);
        MarkDirty();
        RaiseElementsChanged();
    }

    [RelayCommand]
    private void AddTable()
    {
        Doc.Text.Tables.Add(new TableBlock($"Table {Doc.Text.Tables.Count + 1}", 3, 3));
        MarkDirty();
        RaiseElementsChanged();
        StatusText = Loc.T("Status.TableAdded");
    }

    [RelayCommand]
    private void RemoveTable(int index)
    {
        if (index < 0 || index >= Doc.Text.Tables.Count) return;
        Doc.Text.Tables.RemoveAt(index);
        MarkDirty();
        RaiseElementsChanged();
    }

    [RelayCommand]
    private void AddTableRow(TableBlock table) => EditTable(table, t => t.AddRow());

    [RelayCommand]
    private void RemoveTableRow(TableBlock table) => EditTable(table, t =>
    {
        if (t.Rows > 1) t.RemoveRow(t.Rows - 1);
    });

    [RelayCommand]
    private void AddTableColumn(TableBlock table) => EditTable(table, t => t.AddColumn());

    [RelayCommand]
    private void RemoveTableColumn(TableBlock table) => EditTable(table, t =>
    {
        if (t.Columns > 1) t.RemoveColumn(t.Columns - 1);
    });

    /// <summary>Called by the view for each keystroke in a table cell (no rebuild).</summary>
    public void SetTableCellText(TableBlock table, int row, int column, string? text)
    {
        table.SetText(row, column, text);
        MarkDirty();
    }

    /// <summary>Called by the view when a cell colour changes (rebuilds the view).</summary>
    public void SetTableCellFill(TableBlock table, int row, int column, string? fill)
    {
        table.SetFill(row, column, fill);
        MarkDirty();
        RaiseElementsChanged();
    }

    /// <summary>Called by the table designer to style borders over a cell region.</summary>
    public void ApplyTableBorder(
        TableBlock table, int row, int column, int rowCount, int columnCount,
        BorderSides sides, BorderStyle style, string? colour)
    {
        table.ApplyBorder(row, column, rowCount, columnCount, sides, style, colour);
        MarkDirty();
        RaiseElementsChanged();
        StatusText = style == BorderStyle.None
            ? Loc.Format("Status.BordersCleared", table.Name)
            : Loc.Format("Status.BordersApplied", table.Name, Loc.T($"BorderStyle.{style}"));
    }

    private void EditTable(TableBlock table, Action<TableBlock> edit)
    {
        edit(table);
        MarkDirty();
        RaiseElementsChanged();
    }

    // ------------------------------------------------------------ live statistics

    public string StatsText
        => Loc.Format("Status.Words", _stats.Words, _stats.Characters, _stats.Paragraphs);

    partial void OnDocumentFontSizeChanged(double value) => MarkDirty();

    partial void OnCursorPositionChanged(int value) => RefreshStyleState();

    partial void OnSelectionLengthChanged(int value) => RefreshStyleState();

    private void UpdateStats()
    {
        _stats = WordCounter.Count(Body);
        OnPropertyChanged(nameof(StatsText));
        StatusText = StatsText;
    }

    protected override void RefreshLocalizedText()
    {
        UpdateStats();
        RefreshStyleState();
    }

    // ------------------------------------------------------------ character formatting

    /// <summary>Font families offered by the format bar (available on Windows).</summary>
    public static IReadOnlyList<string> FontFamilies { get; } = new[]
    {
        "Open Sans", "Segoe UI", "Georgia", "Times New Roman",
        "Courier New", "Verdana", "Comic Sans MS",
    };

    /// <summary>Point sizes offered by the format bar.</summary>
    public static IReadOnlyList<double> FontSizes { get; } = new double[]
    {
        8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40,
    };

    /// <summary>Foreground swatches (plus "Automatic", which clears the override).</summary>
    public static IReadOnlyList<string> TextForegrounds { get; } = new[]
    {
        "#000000", "#404040", "#808080", "#FFFFFF",
        "#C00000", "#FF0000", "#ED7D31", "#FFC000",
        "#70AD47", "#00B050", "#0070C0", "#7030A0",
    };

    /// <summary>Background swatches (plus "None", which clears the fill).</summary>
    public static IReadOnlyList<string> TextBackgrounds { get; } = new[]
    {
        "#FFFFFF", "#F2F2F2", "#FDE9D9", "#FFF2CC",
        "#E2EFDA", "#DDEBF7", "#E4DFEC", "#F8CBAD",
    };

    [RelayCommand]
    private void ToggleBold() => ToggleFlags(TextStyleFlags.Bold, TextStyleFlags.None);

    [RelayCommand]
    private void ToggleItalic() => ToggleFlags(TextStyleFlags.Italic, TextStyleFlags.None);

    [RelayCommand]
    private void ToggleUnderline() => ToggleFlags(TextStyleFlags.Underline, TextStyleFlags.None);

    [RelayCommand]
    private void ToggleStrikethrough() => ToggleFlags(TextStyleFlags.Strikethrough, TextStyleFlags.None);

    // Superscript and subscript are mutually exclusive.
    [RelayCommand]
    private void ToggleSuperscript()
        => ToggleFlags(TextStyleFlags.Superscript, TextStyleFlags.Subscript);

    [RelayCommand]
    private void ToggleSubscript()
        => ToggleFlags(TextStyleFlags.Subscript, TextStyleFlags.Superscript);

    /// <summary>Applies to the selection, or arms the pending style when nothing is selected.</summary>
    private void ToggleFlags(TextStyleFlags flags, TextStyleFlags exclusive)
    {
        if (SelectionRange() is { } range)
        {
            bool on = AllHaveFlags(range.Start, range.Length, flags);
            Doc.Text.ApplyStyle(range.Start, range.Length, s => on
                ? WithFlags(s, TextStyleFlags.None, flags | exclusive)
                : WithFlags(s, flags, exclusive));
            MarkDirty();
        }
        else
        {
            var basis = PendingStyle ?? CaretStyle();
            bool on = basis.Flags.HasFlag(flags);
            PendingStyle = on
                ? WithFlags(basis, TextStyleFlags.None, flags | exclusive)
                : WithFlags(basis, flags, exclusive);
        }
        RefreshStyleState();
        StyleRevision++; // push runs + pending style to the editor view
    }

    [RelayCommand]
    private void SetFontFamily(string? family)
        => ApplyStyleToSelectionOrPending(s => WithFamily(s, family));

    [RelayCommand]
    private void SetFontSize(double size)
        => ApplyStyleToSelectionOrPending(s => WithSize(s, size));

    [RelayCommand]
    private void SetForeground(string? colour)
        => ApplyStyleToSelectionOrPending(s => WithForeground(s, colour));

    [RelayCommand]
    private void SetBackground(string? colour)
        => ApplyStyleToSelectionOrPending(s => WithBackground(s, colour));

    [RelayCommand]
    private void ToggleTableDesigner() => IsTableDesignerOpen = !IsTableDesignerOpen;

    /// <summary>Applies <paramref name="mutate"/> to the selection, or arms the pending style.</summary>
    private void ApplyStyleToSelectionOrPending(Func<TextStyle, TextStyle> mutate)
    {
        if (SelectionRange() is { } range)
        {
            Doc.Text.ApplyStyle(range.Start, range.Length, mutate);
            MarkDirty();
        }
        else
        {
            PendingStyle = mutate(PendingStyle ?? CaretStyle());
        }
        RefreshStyleState();
        StyleRevision++; // push runs + pending style to the editor view
    }

    /// <summary>The selection as (start, length), or null when nothing is selected.</summary>
    private (int Start, int Length)? SelectionRange()
    {
        var body = Body;
        if (SelectionLength <= 0 || body.Length == 0) return null;
        int start = Math.Clamp(CursorPosition, 0, body.Length);
        int end = Math.Clamp(start + SelectionLength, start, body.Length);
        return end > start ? (start, end - start) : null;
    }

    private bool AllHaveFlags(int start, int length, TextStyleFlags flags)
    {
        for (int i = start; i < start + length; i++)
            if (Doc.Text.StyleAt(i)?.Flags.HasFlag(flags) != true)
                return false;
        return true;
    }

    /// <summary>The style at the caret (the character before it when at the end).</summary>
    private TextStyle CaretStyle()
    {
        var body = Body;
        if (body.Length == 0) return new TextStyle();
        int pos = Math.Clamp(CursorPosition, 0, body.Length - 1);
        return Doc.Text.StyleAt(pos) ?? new TextStyle();
    }

    /// <summary>Style backing the toolbar state: pending, uniform selection, or caret.</summary>
    private TextStyle EffectiveStyle()
    {
        if (PendingStyle is not null) return PendingStyle;

        if (SelectionRange() is { } range)
        {
            var first = Doc.Text.StyleAt(range.Start) ?? new TextStyle();
            for (int i = range.Start + 1; i < range.Start + range.Length; i++)
                if (!first.Equals(Doc.Text.StyleAt(i) ?? new TextStyle()))
                    return new TextStyle(); // a mixed selection reads as "off"
            return first;
        }

        return CaretStyle();
    }

    private void RefreshStyleState()
    {
        var style = EffectiveStyle();
        IsBold = style.Flags.HasFlag(TextStyleFlags.Bold);
        IsItalic = style.Flags.HasFlag(TextStyleFlags.Italic);
        IsUnderline = style.Flags.HasFlag(TextStyleFlags.Underline);
        IsStrikethrough = style.Flags.HasFlag(TextStyleFlags.Strikethrough);
        IsSuperscript = style.Flags.HasFlag(TextStyleFlags.Superscript);
        IsSubscript = style.Flags.HasFlag(TextStyleFlags.Subscript);
        SelectedFontFamily = style.FontFamily;
        SelectedFontSize = style.FontSize ?? DocumentFontSize;
        SelectedForeground = style.Foreground;
        SelectedBackground = style.Background;
    }

    private static TextStyle WithFlags(TextStyle s, TextStyleFlags add, TextStyleFlags remove) => new()
    {
        FontFamily = s.FontFamily,
        FontSize = s.FontSize,
        Foreground = s.Foreground,
        Background = s.Background,
        Flags = (s.Flags | add) & ~remove,
    };

    private static TextStyle WithFamily(TextStyle s, string? family) => new()
    {
        FontFamily = family,
        FontSize = s.FontSize,
        Foreground = s.Foreground,
        Background = s.Background,
        Flags = s.Flags,
    };

    private static TextStyle WithSize(TextStyle s, double? size) => new()
    {
        FontFamily = s.FontFamily,
        FontSize = size,
        Foreground = s.Foreground,
        Background = s.Background,
        Flags = s.Flags,
    };

    private static TextStyle WithForeground(TextStyle s, string? foreground) => new()
    {
        FontFamily = s.FontFamily,
        FontSize = s.FontSize,
        Foreground = foreground,
        Background = s.Background,
        Flags = s.Flags,
    };

    private static TextStyle WithBackground(TextStyle s, string? background) => new()
    {
        FontFamily = s.FontFamily,
        FontSize = s.FontSize,
        Foreground = s.Foreground,
        Background = background,
        Flags = s.Flags,
    };

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
        DocumentFontSize = DefaultFontSize;
        SelectionLength = 0;
        CursorPosition = 0;
        OnPropertyChanged(nameof(Body)); // the proxy's source document changed
        UpdateStats();
        StyleRevision++;
        RaiseElementsChanged();
    }

    protected override void ResetDocument()
    {
        Body = string.Empty;
        CursorPosition = 0;
        SelectionLength = 0;
        RaiseElementsChanged();
    }

    protected override void CollectDocument()
    {
        // Body is a live proxy over Doc.Text.Body — nothing to copy.
        if (string.IsNullOrWhiteSpace(Doc.Title) || Doc.Title == "Untitled")
            Doc.Title = Title;
    }

    // ------------------------------------------------------------ chrome

    // ------------------------------------------------------------ export / print commands

    [RelayCommand]
    private Task ExportPdfAsync()
        => ExportAsync(SafeName() + ".pdf", p => AuthorPdfExporter.Export(Doc, p), "Status.Exported");

    [RelayCommand]
    private Task ExportOdtAsync()
        => ExportAsync(SafeName() + ".odt", p => OdfExporter.ExportOdt(Doc, p), "Status.Exported");

    [RelayCommand]
    private async Task PrintAsync()
    {
        CollectForOutput();
        await SendToPrinterAsync(AuthorPdfExporter.Render(Doc));
    }

    [RelayCommand]
    private Task PrintToFileAsync()
        => ExportAsync(SafeName() + ".pdf", p => AuthorPdfExporter.Export(Doc, p), "Status.PrintedToFile");

    [RelayCommand]
    private Task ImportOdtAsync()
        => ImportExternalAsync(Loc.T("Dialog.ImportOdt"), ".odt", OdfImporter.ImportOdt);

    [RelayCommand]
    private Task ImportTapeAsync()
        => ImportExternalAsync(Loc.T("Dialog.ImportTape"), ".tap",
            p => TapeImporter.ImportFile(p, OricFileKind.Author));

    private string SafeName()
    {
        var name = string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    // ------------------------------------------------------------ chrome

    protected override IEnumerable<MenuGroup> BuildMenus()
    {
        yield return new MenuGroup(Loc.T("Menu.File"),
            new MenuItemModel(Loc.T("Item.New"), command: NewCommand),
            new MenuItemModel(Loc.T("Item.Open"), command: OpenCommand),
            new MenuItemModel(Loc.T("Item.Save"), command: SaveCommand),
            new MenuItemModel(Loc.T("Item.SaveAs"), command: SaveAsCommand),
            MenuItemModel.Separator(),
            new MenuItemModel(Loc.T("Item.Export"), children: new[]
            {
                new MenuItemModel(Loc.T("Export.Pdf"), command: ExportPdfCommand),
                new MenuItemModel(Loc.T("Export.Odt"), command: ExportOdtCommand),
            }),
            new MenuItemModel(Loc.T("Item.Import"), children: new[]
            {
                new MenuItemModel(Loc.T("Import.Odt"), command: ImportOdtCommand),
                new MenuItemModel(Loc.T("Import.Tape"), command: ImportTapeCommand),
            }),
            new MenuItemModel(Loc.T("Item.Print"), children: new[]
            {
                new MenuItemModel(Loc.T("Item.PrintEllipsis"), command: PrintCommand, accelerator: "Ctrl+P"),
                new MenuItemModel(Loc.T("Item.PrintToFile"), command: PrintToFileCommand),
            }),
            MenuItemModel.Separator(),
            new MenuItemModel(Loc.T("Item.Exit"), command: ExitCommand));

        yield return new MenuGroup(Loc.T("Menu.Insert"),
            new MenuItemModel(Loc.T("Item.Image"), command: ImportImageCommand),
            new MenuItemModel(Loc.T("Item.Table"), command: AddTableCommand));

        yield return new MenuGroup(Loc.T("Menu.Edit"),
            new MenuItemModel(Loc.T("Item.Cut"), command: CutCommand),
            new MenuItemModel(Loc.T("Item.Copy"), command: CopyCommand),
            new MenuItemModel(Loc.T("Item.Paste"), command: PasteCommand),
            new MenuItemModel(Loc.T("Item.SelectAll"), command: SelectAllCommand),
            MenuItemModel.Separator(),
            new MenuItemModel(Loc.T("Item.ZoomIn"), command: ZoomInCommand),
            new MenuItemModel(Loc.T("Item.ZoomOut"), command: ZoomOutCommand),
            new MenuItemModel(Loc.T("Item.ActualSize"), command: ZoomResetCommand));

        yield return new MenuGroup(Loc.T("Menu.Format"),
            new MenuItemModel(Loc.T("Item.Bold"), command: ToggleBoldCommand, accelerator: "Ctrl+B"),
            new MenuItemModel(Loc.T("Item.Italic"), command: ToggleItalicCommand, accelerator: "Ctrl+I"),
            new MenuItemModel(Loc.T("Item.Underline"), command: ToggleUnderlineCommand, accelerator: "Ctrl+U"),
            new MenuItemModel(Loc.T("Item.Strikethrough"), command: ToggleStrikethroughCommand),
            MenuItemModel.Separator(),
            new MenuItemModel(Loc.T("Item.Superscript"), command: ToggleSuperscriptCommand),
            new MenuItemModel(Loc.T("Item.Subscript"), command: ToggleSubscriptCommand));

        yield return new MenuGroup(Loc.T("Menu.Help"),
            new MenuItemModel(Loc.Format("Item.About", Meta.Name), command: AboutCommand));
    }

    protected override IEnumerable<ToolButtonModel> BuildToolbar()
    {
        yield return new ToolButtonModel(Loc.T("Tool.New"), SuiteIcon.New, NewCommand);
        yield return new ToolButtonModel(Loc.T("Tool.Open"), SuiteIcon.Open, OpenCommand);
        yield return new ToolButtonModel(Loc.T("Tool.Save"), SuiteIcon.Save, SaveCommand);
        yield return new ToolButtonModel(Loc.T("Tool.SaveAs"), SuiteIcon.SaveAs, SaveAsCommand);
        yield return ToolButtonModel.Separator();
        yield return new ToolButtonModel(Loc.T("Tool.Cut"), SuiteIcon.Cut, CutCommand);
        yield return new ToolButtonModel(Loc.T("Tool.Copy"), SuiteIcon.Copy, CopyCommand);
        yield return new ToolButtonModel(Loc.T("Tool.Paste"), SuiteIcon.Paste, PasteCommand);
        yield return ToolButtonModel.Separator();
        yield return new ToolButtonModel(Loc.T("Tool.ZoomOut"), SuiteIcon.ZoomOut, ZoomOutCommand);
        yield return new ToolButtonModel(Loc.T("Tool.ZoomIn"), SuiteIcon.ZoomIn, ZoomInCommand);
        yield return ToolButtonModel.Separator();
        yield return new ToolButtonModel(Loc.T("Tool.Image"), SuiteIcon.Image, ImportImageCommand);
        yield return new ToolButtonModel(Loc.T("Tool.AddTable"), SuiteIcon.Table, AddTableCommand);
        yield return new ToolButtonModel(Loc.T("Tool.TableDesigner"), SuiteIcon.TableDesigner, ToggleTableDesignerCommand);
        yield return ToolButtonModel.Separator();
        yield return new ToolButtonModel(Loc.T("Tool.Print"), SuiteIcon.Print, PrintCommand);
        yield return new ToolButtonModel(Loc.T("Tool.PrintToFile"), SuiteIcon.Print, PrintToFileCommand);
    }
}
