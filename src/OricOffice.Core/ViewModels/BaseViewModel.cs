using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OricOffice.Core.Base;
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

    public string Headline => $"Record #{Ordinal}";

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

    private void UpdateSummary()
    {
        var parts = _owner.Fields
            .Select(f => _row.Get(f.Name))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Take(3)
            .ToList();
        Summary = parts.Count == 0 ? "(empty record)" : string.Join("  ·  ", parts);
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
        AppMeta meta)
        : base(dialogs, lifetime, store, meta)
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
        StatusText = $"Added field \"{vm.Name}\"";
    }

    private void RemoveFieldCore(FieldViewModel field)
    {
        Fields.Remove(field);
        Doc.Table.RemoveField(field.Name);
        foreach (var row in _all) row.Values.Remove(field.Name);
        RefreshFieldNames();
        foreach (var record in Records) record.RebuildCells();
        MarkDirty();
        StatusText = $"Removed field \"{field.Name}\"";
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
        StatusText = $"Added record #{_all.Count}";
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
        StatusText = $"Deleted record · {_all.Count} left";
    }

    internal void MarkRecordEdited() => MarkDirty();

    // ------------------------------------------------------------ query + sort

    [RelayCommand]
    private void ApplyQuery()
    {
        if (!RecordQuery.TryParse(QueryText, Fields.Select(f => f.ToDef()).ToList(),
                out var compiled, out var error))
        {
            StatusText = error ?? "Invalid query.";
            return;
        }
        _activeQuery = compiled;
        RefreshRecords();
        StatusText = string.IsNullOrWhiteSpace(QueryText)
            ? "Showing all records"
            : $"{Records.Count} of {_all.Count} records match: {QueryText}";
    }

    [RelayCommand]
    private void ClearQuery()
    {
        QueryText = string.Empty;
        _activeQuery = null;
        RefreshRecords();
        StatusText = "Filter cleared";
    }

    [RelayCommand]
    private void SortAscending() => SortBy(descending: false);

    [RelayCommand]
    private void SortDescending() => SortBy(descending: true);

    private void SortBy(bool descending)
    {
        if (SortFieldName is null)
        {
            StatusText = "Pick a field to sort by first.";
            return;
        }
        var selectedId = SelectedRecord?.Id;
        var fieldDef = Fields.FirstOrDefault(f => f.Name == SortFieldName)?.ToDef();
        if (fieldDef is null)
        {
            StatusText = $"Field \"{SortFieldName}\" no longer exists.";
            return;
        }

        var sorted = RecordQuery.Sort(_all, fieldDef, descending).ToList();
        _all.Clear();
        _all.AddRange(sorted);
        RefreshRecords();
        if (selectedId is { } id) SelectedRecord = Records.FirstOrDefault(r => r.Id == id);
        MarkDirty();
        StatusText = $"Sorted by {fieldDef.Name} {(descending ? "Z→A" : "A→Z")}";
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
            ? $"{_all.Count} records"
            : $"{Records.Count} of {_all.Count} records match the filter";
    }

    // ------------------------------------------------------------ document hooks

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

        yield return new MenuGroup("Table",
            new MenuItemModel("Add Field", command: AddFieldCommand));

        yield return new MenuGroup("Record",
            new MenuItemModel("New Record", command: AddRecordCommand),
            new MenuItemModel("Delete Record", command: DeleteRecordCommand),
            MenuItemModel.Separator(),
            new MenuItemModel("Sort A→Z", command: SortAscendingCommand),
            new MenuItemModel("Sort Z→A", command: SortDescendingCommand));

        yield return new MenuGroup("Query",
            new MenuItemModel("Apply Filter", command: ApplyQueryCommand),
            new MenuItemModel("Clear Filter", command: ClearQueryCommand));

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
        yield return new ToolButtonModel("New Record", SuiteIcon.Record, AddRecordCommand);
        yield return new ToolButtonModel("Delete", SuiteIcon.Trash, DeleteRecordCommand);
        yield return ToolButtonModel.Separator();
        yield return new ToolButtonModel("+ Field", SuiteIcon.AddField, AddFieldCommand);
        yield return new ToolButtonModel("Sort A→Z", SuiteIcon.SortAsc, SortAscendingCommand);
        yield return new ToolButtonModel("Sort Z→A", SuiteIcon.SortDesc, SortDescendingCommand);
    }
}
