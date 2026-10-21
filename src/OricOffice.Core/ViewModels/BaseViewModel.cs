using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OricOffice.Core.Base;
using OricOffice.Core.Export;
using OricOffice.Core.Localization;
using OricOffice.Core.Model;
using OricOffice.Core.Seeders;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.Ui;

namespace OricOffice.Core.ViewModels;

/// <summary>One field chip in the fields strip: name (editable) + type toggle + delete.</summary>
public sealed partial class FieldViewModel : ObservableObject
{
    private readonly BaseViewModel _owner;
    private readonly Action<FieldViewModel> _delete;

    public FieldViewModel(BaseViewModel owner, FieldDef def, Action<FieldViewModel> delete)
    {
        _owner = owner;
        _delete = delete;
        Name = def.Name;
        Type = def.Type;
        DeleteCommand = new RelayCommand(() => _delete(this));
    }

    [ObservableProperty]
    private string name = "Field";

    [ObservableProperty]
    private FieldType type = FieldType.Text;

    public RelayCommand DeleteCommand { get; }

    public string TypeLabel => Type switch
    {
        FieldType.Number => "123",
        FieldType.Date => "📅",
        _ => "ABC",
    };

    partial void OnNameChanged(string? oldValue, string newValue) => _owner.OnFieldRenamed(oldValue, newValue);

    partial void OnTypeChanged(FieldType value)
    {
        OnPropertyChanged(nameof(TypeLabel));
        _owner.OnFieldEdited();
    }

    [RelayCommand]
    private void ToggleType()
        => Type = Type switch
        {
            FieldType.Text => FieldType.Number,
            FieldType.Number => FieldType.Date,
            _ => FieldType.Text,
        };

    public FieldDef ToDef() => new(Name, Type);
}

/// <summary>One editable value inside the selected record.</summary>
public sealed partial class RecordCellViewModel : ObservableObject
{
    private readonly RecordRow _row;
    private readonly RecordViewModel _owner;

    public RecordCellViewModel(RecordRow row, RecordViewModel owner, string fieldName, FieldType fieldType)
    {
        _row = row;
        _owner = owner;
        FieldName = fieldName;
        FieldType = fieldType;
        value = row.Get(fieldName);
    }

    public string FieldName { get; }

    public FieldType FieldType { get; }

    [ObservableProperty]
    private string? value;

    partial void OnValueChanged(string? value)
    {
        _row.Set(FieldName, value ?? string.Empty);
        _owner.NotifyEdited();
    }
}

/// <summary>A record as shown in the list (number + summary) plus its editable cells.</summary>
public sealed partial class RecordViewModel : ObservableObject
{
    private readonly BaseViewModel _owner;
    private readonly RecordRow _row;

    public RecordViewModel(BaseViewModel owner, RecordRow row, int ordinal)
    {
        _owner = owner;
        _row = row;
        Ordinal = ordinal;
        Cells = new ObservableCollection<RecordCellViewModel>();
        RebuildCells();
        UpdateSummary();
    }

    public long Id => _row.Id;

    public int Ordinal { get; }

    public string Headline => Loc.Format("Record.Number", Ordinal);

    public string ListLabel => $"#{Ordinal}   {Summary}";

    public RecordRow Row => _row;

    public ObservableCollection<RecordCellViewModel> Cells { get; }

    [ObservableProperty]
    private string summary = string.Empty;

    public void RebuildCells()
    {
        Cells.Clear();
        foreach (var field in _owner.Fields)
            Cells.Add(new RecordCellViewModel(_row, this, field.Name, field.Type));
        UpdateSummary();
    }

    public void NotifyEdited()
    {
        UpdateSummary();
        _owner.MarkRecordEdited();
    }

    /// <summary>Raised after a culture change so localized labels repaint.</summary>
    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(ListLabel));
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var parts = _owner.Fields
            .Select(f => _row.Get(f.Name))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Take(3)
            .ToList();
        Summary = parts.Count == 0 ? Loc.T("Record.Empty") : string.Join("  ·  ", parts);
    }
}

/// <summary>
/// Oric Base: SQLite-backed records with an editable field strip, a record list,
/// a record editor pane, a query bar (the original's "easy query language") and sorting.
/// </summary>
public sealed partial class BaseViewModel : OricDocumentViewModelBase
{
    private readonly List<RecordRow> _all = new();
    private CompiledQuery? _activeQuery;

    public ObservableCollection<FieldViewModel> Fields { get; } = new();
    public ObservableCollection<string> FieldNames { get; } = new();
    public ObservableCollection<RecordViewModel> Records { get; } = new();

    [ObservableProperty]
    private RecordViewModel? selectedRecord;

    [ObservableProperty]
    private string queryText = string.Empty;

    [ObservableProperty]
    private string? sortFieldName;

    [ObservableProperty]
    private bool hasFields;

    [ObservableProperty]
    private bool hasRecords;

    [ObservableProperty]
    private bool hasSelectedRecord;

    partial void OnSelectedRecordChanged(RecordViewModel? value)
    {
        HasSelectedRecord = value is not null;
        OnPropertyChanged(nameof(NoRecordSelected));
    }

    partial void OnHasFieldsChanged(bool value) => OnPropertyChanged(nameof(NoFields));

    public bool NoRecordSelected => !HasSelectedRecord;

    public bool NoFields => !HasFields;

    public BaseViewModel(
        IDialogService dialogs,
        IAppLifetimeService lifetime,
        OricFileStore store,
        AppMeta meta,
        IPrintService? printer = null)
        : base(dialogs, lifetime, store, meta, printer)
    {
        // A brand-new Base document starts with one field so it is usable immediately.
        ResetDocument();
        IsDirty = false;
    }

    // ------------------------------------------------------------ field management

    [RelayCommand]
    private void AddField()
    {
        var def = Doc.Table.AddField($"Field{Doc.Table.Fields.Count + 1}");
        var vm = new FieldViewModel(this, def, RemoveFieldCore);
        Fields.Add(vm);
        RefreshFieldNames();
        foreach (var record in Records) record.RebuildCells();
        MarkDirty();
        StatusText = Loc.Format("Status.AddedField", vm.Name);
    }

    private void RemoveFieldCore(FieldViewModel field)
    {
        Fields.Remove(field);
        Doc.Table.RemoveField(field.Name);
        foreach (var row in _all) row.Values.Remove(field.Name);
        RefreshFieldNames();
        foreach (var record in Records) record.RebuildCells();
        MarkDirty();
        StatusText = Loc.Format("Status.RemovedField", field.Name);
    }

    internal void OnFieldEdited()
    {
        foreach (var record in Records) record.RebuildCells();
        MarkDirty();
    }

    internal void OnFieldRenamed(string? oldName, string? newName)
    {
        // A rename in the strip must carry record values along with it.
        if (!string.IsNullOrEmpty(oldName) &&
            !string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            foreach (var row in _all)
            {
                if (row.Values.Remove(oldName, out var carried))
                    row.Values[newName ?? string.Empty] = carried;
                if (!row.Values.ContainsKey(newName ?? string.Empty))
                    row.Values[newName ?? string.Empty] = string.Empty;
            }
            foreach (var record in Records) record.RebuildCells();
        }
        // Renames otherwise apply in CollectDocument (the strip is the source of truth);
        // keep names unique and refresh the picker.
        RefreshFieldNames();
        MarkDirty();
    }

    private void RefreshFieldNames()
    {
        FieldNames.Clear();
        foreach (var field in Fields) FieldNames.Add(field.Name);
        HasFields = Fields.Count > 0;
        if (SortFieldName is not null && !FieldNames.Contains(SortFieldName))
            SortFieldName = null;
    }

    // ------------------------------------------------------------ record management

    [RelayCommand]
    private void AddRecord()
    {
        if (Fields.Count == 0)
        {
            AddFieldCommand.Execute(null);
        }
        var id = _all.Count == 0 ? 1 : _all.Max(r => r.Id) + 1;
        var row = new RecordRow { Id = id, Ordinal = _all.Count + 1 };
        foreach (var field in Fields)
            row.Values[field.Name] = string.Empty;
        _all.Add(row);
        RefreshRecords();
        SelectedRecord = Records.FirstOrDefault(r => r.Id == row.Id) ?? Records.LastOrDefault();
        MarkDirty();
        StatusText = Loc.Format("Status.AddedRecord", _all.Count);
    }

    [RelayCommand]
    private void DeleteRecord()
    {
        if (SelectedRecord is not { } selected) return;
        var row = _all.FirstOrDefault(r => r.Id == selected.Id);
        if (row is null) return;
        _all.Remove(row);
        RefreshRecords();
        SelectedRecord = Records.FirstOrDefault();
        MarkDirty();
        StatusText = Loc.Format("Status.DeletedRecord", _all.Count);
    }

    internal void MarkRecordEdited() => MarkDirty();

    // ------------------------------------------------------------ query + sort

    [RelayCommand]
    private void ApplyQuery()
    {
        if (!RecordQuery.TryParse(QueryText, Fields.Select(f => f.ToDef()).ToList(),
                out var compiled, out var error))
        {
            StatusText = error ?? Loc.T("Status.InvalidQuery");
            return;
        }
        _activeQuery = compiled;
        RefreshRecords();
        StatusText = string.IsNullOrWhiteSpace(QueryText)
            ? Loc.T("Status.QueryAll")
            : Loc.Format("Status.QueryMatch", Records.Count, _all.Count, QueryText);
    }

    [RelayCommand]
    private void ClearQuery()
    {
        QueryText = string.Empty;
        _activeQuery = null;
        RefreshRecords();
        StatusText = Loc.T("Status.FilterCleared");
    }

    [RelayCommand]
    private void SortAscending() => SortBy(descending: false);

    [RelayCommand]
    private void SortDescending() => SortBy(descending: true);

    private void SortBy(bool descending)
    {
        if (SortFieldName is null)
        {
            StatusText = Loc.T("Status.PickSortField");
            return;
        }
        var selectedId = SelectedRecord?.Id;
        var fieldDef = Fields.FirstOrDefault(f => f.Name == SortFieldName)?.ToDef();
        if (fieldDef is null)
        {
            StatusText = Loc.Format("Status.FieldMissing", SortFieldName);
            return;
        }

        var sorted = RecordQuery.Sort(_all, fieldDef, descending).ToList();
        _all.Clear();
        _all.AddRange(sorted);
        RefreshRecords();
        if (selectedId is { } id) SelectedRecord = Records.FirstOrDefault(r => r.Id == id);
        MarkDirty();
        StatusText = Loc.Format("Status.SortedBy", fieldDef.Name,
            descending ? Loc.T("Sort.Desc") : Loc.T("Sort.Asc"));
    }

    // ------------------------------------------------------------ refresh

    private void RefreshRecords()
    {
        var previousId = SelectedRecord?.Id;
        var shown = RecordQuery.Apply(_all, _activeQuery).ToList();

        Records.Clear();
        int ordinal = 1;
        foreach (var row in shown)
            Records.Add(new RecordViewModel(this, row, ordinal++));

        HasRecords = Records.Count > 0;
        HasFields = Fields.Count > 0;
        SelectedRecord = (previousId is { } id ? Records.FirstOrDefault(r => r.Id == id) : null)
                         ?? Records.FirstOrDefault();

        StatusText = _all.Count == Records.Count
            ? Loc.Format("Status.RecordsCount", _all.Count)
            : Loc.Format("Status.RecordsMatch", Records.Count, _all.Count);
    }

    // ------------------------------------------------------------ document hooks

    protected override void RefreshLocalizedText()
    {
        foreach (var record in Records) record.RefreshLabels();
    }

    protected override void ApplyDocument()
    {
        ContentSeeder.EnsureTable(Doc.Table, Doc.Text, Doc.Sheet);

        Fields.Clear();
        foreach (var def in Doc.Table.Fields)
            Fields.Add(new FieldViewModel(this, def, RemoveFieldCore));
        RefreshFieldNames();

        _all.Clear();
        _all.AddRange(Doc.Table.Records);

        QueryText = string.Empty;
        _activeQuery = null;
        SortFieldName = null;
        RefreshRecords();
    }

    protected override void ResetDocument()
    {
        ContentSeeder.EnsureTable(Doc.Table, Doc.Text, Doc.Sheet);
        ApplyDocument();
    }

    protected override void CollectDocument()
    {
        // Fields: the strip is authoritative (renames flow back here).
        var defs = Fields.Select(f => f.ToDef()).ToList();
        Doc.Table.Fields.Clear();
        foreach (var def in defs) Doc.Table.Fields.Add(def);

        // Records keep their row objects (identity/ids preserved).
        Doc.Table.Records.Clear();
        Doc.Table.Records.AddRange(_all);
        Doc.Table.Renumber();

        if (string.IsNullOrWhiteSpace(Doc.Title) || Doc.Title == "Untitled")
            Doc.Title = Title;
    }

    // ------------------------------------------------------------ export / print

    [RelayCommand]
    private Task ExportSqliteAsync()
        => ExportAsync(SafeName() + ".sqlite",
            p => SqlExporters.ExportSqlite(Doc, p), "Status.Exported");

    [RelayCommand]
    private Task ExportMysqlAsync()
        => ExportAsync(SafeName() + "-mysql.sql",
            p => File.WriteAllText(p, SqlExporters.ExportMysqlScript(Doc)), "Status.Exported");

    [RelayCommand]
    private Task ExportMssqlAsync()
        => ExportAsync(SafeName() + "-mssql.sql",
            p => File.WriteAllText(p, SqlExporters.ExportMssqlScript(Doc)), "Status.Exported");

    [RelayCommand]
    private async Task PrintAsync()
    {
        CollectForOutput();
        await SendToPrinterAsync(BasePdfExporter.Render(Doc));
    }

    [RelayCommand]
    private Task PrintToFileAsync()
        => ExportAsync(SafeName() + ".pdf", p => BasePdfExporter.Export(Doc, p), "Status.PrintedToFile");

    [RelayCommand]
    private Task ExportOdbAsync()
        => ExportAsync(SafeName() + ".odb", p => OdbExporter.Export(Doc, p), "Status.Exported");

    [RelayCommand]
    private Task ExportCsvAsync()
        => ExportAsync(SafeName() + ".csv",
            p => File.WriteAllText(p, BaseCsv.Export(Doc), new System.Text.UTF8Encoding(false)),
            "Status.Exported");

    [RelayCommand]
    private Task ImportCsvAsync()
        => ImportExternalAsync(Loc.T("Dialog.ImportCsv"), ".csv", BaseCsv.ImportFile);

    [RelayCommand]
    private Task ImportOdbAsync()
        => ImportExternalAsync(Loc.T("Dialog.ImportOdb"), ".odb", OdbImporter.Import);

    [RelayCommand]
    private Task ImportTapeAsync()
        => ImportExternalAsync(Loc.T("Dialog.ImportTape"), ".tap",
            p => TapeImporter.ImportFile(p, OricFileKind.Base));

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
                new MenuItemModel(Loc.T("Export.Sqlite"), command: ExportSqliteCommand),
                new MenuItemModel(Loc.T("Export.Mysql"), command: ExportMysqlCommand),
                new MenuItemModel(Loc.T("Export.Mssql"), command: ExportMssqlCommand),
                new MenuItemModel(Loc.T("Export.Odb"), command: ExportOdbCommand),
                new MenuItemModel(Loc.T("Export.Csv"), command: ExportCsvCommand),
            }),
            new MenuItemModel(Loc.T("Item.Import"), children: new[]
            {
                new MenuItemModel(Loc.T("Import.Odb"), command: ImportOdbCommand),
                new MenuItemModel(Loc.T("Import.Csv"), command: ImportCsvCommand),
                new MenuItemModel(Loc.T("Import.Tape"), command: ImportTapeCommand),
            }),
            new MenuItemModel(Loc.T("Item.Print"), children: new[]
            {
                new MenuItemModel(Loc.T("Item.PrintEllipsis"), command: PrintCommand, accelerator: "Ctrl+P"),
                new MenuItemModel(Loc.T("Item.PrintToFile"), command: PrintToFileCommand),
            }),
            MenuItemModel.Separator(),
            new MenuItemModel(Loc.T("Item.Exit"), command: ExitCommand));

        yield return new MenuGroup(Loc.T("Menu.Table"),
            new MenuItemModel(Loc.T("Item.AddField"), command: AddFieldCommand));

        yield return new MenuGroup(Loc.T("Menu.Record"),
            new MenuItemModel(Loc.T("Item.NewRecord"), command: AddRecordCommand),
            new MenuItemModel(Loc.T("Item.DeleteRecord"), command: DeleteRecordCommand),
            MenuItemModel.Separator(),
            new MenuItemModel(Loc.T("Item.SortAsc"), command: SortAscendingCommand),
            new MenuItemModel(Loc.T("Item.SortDesc"), command: SortDescendingCommand));

        yield return new MenuGroup(Loc.T("Menu.Query"),
            new MenuItemModel(Loc.T("Item.ApplyFilter"), command: ApplyQueryCommand),
            new MenuItemModel(Loc.T("Item.ClearFilter"), command: ClearQueryCommand));

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
        yield return new ToolButtonModel(Loc.T("Tool.NewRecord"), SuiteIcon.Record, AddRecordCommand);
        yield return new ToolButtonModel(Loc.T("Tool.Delete"), SuiteIcon.Trash, DeleteRecordCommand);
        yield return ToolButtonModel.Separator();
        yield return new ToolButtonModel(Loc.T("Tool.AddField"), SuiteIcon.AddField, AddFieldCommand);
        yield return new ToolButtonModel(Loc.T("Tool.SortAsc"), SuiteIcon.SortAsc, SortAscendingCommand);
        yield return new ToolButtonModel(Loc.T("Tool.SortDesc"), SuiteIcon.SortDesc, SortDescendingCommand);
        yield return ToolButtonModel.Separator();
        yield return new ToolButtonModel(Loc.T("Tool.Print"), SuiteIcon.Print, PrintCommand);
        yield return new ToolButtonModel(Loc.T("Tool.PrintToFile"), SuiteIcon.Print, PrintToFileCommand);
    }
}
