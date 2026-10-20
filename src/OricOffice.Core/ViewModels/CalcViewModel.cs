using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OricOffice.Core.Calc;
using OricOffice.Core.Model;
using OricOffice.Core.Seeders;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.Ui;

namespace OricOffice.Core.ViewModels;

/// <summary>One grid cell: raw input plus the computed display value.</summary>
public sealed partial class CellViewModel : ObservableObject
{
    private string _raw = string.Empty;

    public CellViewModel(int row, int column)
    {
        Row = row;
        Column = column;
    }

    public int Row { get; }
    public int Column { get; }

    public string Address { get; private set; } = "A1";

    public string Raw => _raw;

    public bool IsEmpty => _raw.Length == 0;

    [ObservableProperty]
    private string display = string.Empty;

    [ObservableProperty]
    private bool isSelected;

    public void SetAddress()
        => Address = $"{CellAddress.ColumnName(Column)}{Row + 1}";

    public void SetRaw(string raw)
    {
        _raw = raw ?? string.Empty;
    }
}

/// <summary>
/// Oric Calc: a grid with a LibreOffice-style name box + formula bar as the edit
/// surface, AutoSum, and row/column growth. Formulas recalculate through the engine.
/// </summary>
public sealed partial class CalcViewModel : OricDocumentViewModelBase
{
    [ObservableProperty]
    private string nameBoxText = "A1";

    [ObservableProperty]
    private string formulaBarText = string.Empty;

    [ObservableProperty]
    private CellViewModel? selectedCell;

    private int _rows = SheetDocument.MinRows;
    private int _columns = SheetDocument.MinColumns;
    private CellViewModel[,] _cells = new CellViewModel[0, 0];

    /// <summary>Raised whenever the grid dimensions change so the view can rebuild.</summary>
    public event Action? GridChanged;

    /// <summary>Raised after selection so the view can scroll the cell into view.</summary>
    public event Action<CellViewModel>? ScrollToCellRequested;

    public CalcViewModel(
        IDialogService dialogs,
        IAppLifetimeService lifetime,
        OricFileStore store,
        AppMeta meta)
        : base(dialogs, lifetime, store, meta)
    {
        RebuildGrid(SheetDocument.MinRows, SheetDocument.MinColumns);
        SelectCell(_cells[0, 0]);
    }

    public int Rows => _rows;
    public int Columns => _columns;

    public IReadOnlyList<string> ColumnHeaders { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<int> RowHeaders { get; private set; } = Array.Empty<int>();

    public CellViewModel CellAt(int row, int column) => _cells[row, column];

    // ------------------------------------------------------------ grid lifecycle

    private void RebuildGrid(int rows, int columns)
    {
        var previous = _cells;
        int previousRows = previous.GetLength(0);
        int previousColumns = previous.GetLength(1);

        _rows = Math.Max(rows, SheetDocument.MinRows);
        _columns = Math.Max(columns, SheetDocument.MinColumns);
        _cells = new CellViewModel[_rows, _columns];

        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _columns; c++)
            {
                var cell = new CellViewModel(r, c);
                cell.SetAddress();
                if (r < previousRows && c < previousColumns)
                {
                    var old = previous[r, c];
                    cell.SetRaw(old.Raw);
                    cell.Display = old.Display;
                    cell.IsSelected = old.IsSelected;
                }
                _cells[r, c] = cell;
            }
        }

        ColumnHeaders = Enumerable.Range(0, _columns).Select(CellAddress.ColumnName).ToList();
        RowHeaders = Enumerable.Range(1, _rows).ToList();

        if (SelectedCell is { } selected)
        {
            selected.IsSelected = false;
            var next = _cells[Math.Min(selected.Row, _rows - 1), Math.Min(selected.Column, _columns - 1)];
            SelectCellCore(next, raiseScroll: false);
        }

        GridChanged?.Invoke();
    }

    // ------------------------------------------------------------ selection

    public void SelectCell(CellViewModel cell) => SelectCellCore(cell, raiseScroll: true);

    private void SelectCellCore(CellViewModel cell, bool raiseScroll)
    {
        if (SelectedCell is { } previous) previous.IsSelected = false;
        cell.IsSelected = true;
        SelectedCell = cell;
        NameBoxText = cell.Address;
        FormulaBarText = cell.Raw;
        UpdateCellStatus(cell);
        if (raiseScroll) ScrollToCellRequested?.Invoke(cell);
    }

    private void UpdateCellStatus(CellViewModel cell)
    {
        var shown = string.IsNullOrEmpty(cell.Display) ? "(blank)" : cell.Display;
        StatusText = $"{cell.Address} = {shown}    ·    {Rows} rows × {Columns} columns";
    }

    // ------------------------------------------------------------ editing

    [RelayCommand]
    private void CommitFormula()
    {
        if (SelectedCell is not { } cell) return;
        CommitTo(cell, FormulaBarText);
    }

    /// <summary>Called by the view when the user presses Enter in the name box.</summary>
    [RelayCommand]
    private void GoToAddress()
    {
        if (SelectedCell is null) return;
        if (!CellAddress.TryParse(NameBoxText, out var address))
        {
            StatusText = $"\"{NameBoxText}\" is not a cell address (expected e.g. B12).";
            return;
        }
        if (address.Row >= _rows || address.Column >= _columns)
        {
            StatusText = $"{address} is outside the current grid ({Rows} × {Columns}). Add rows/columns first.";
            return;
        }
        SelectCell(_cells[address.Row, address.Column]);
    }

    private void CommitTo(CellViewModel cell, string raw)
    {
        raw ??= string.Empty;
        cell.SetRaw(raw.Trim());
        Recalculate();
        FormulaBarText = cell.Raw;
        NameBoxText = cell.Address;
        UpdateCellStatus(cell);
        MarkDirty();
    }

    [RelayCommand]
    private void ClearCell()
    {
        if (SelectedCell is not { } cell) return;
        CommitTo(cell, string.Empty);
    }

    [RelayCommand]
    private void AutoSum()
    {
        if (SelectedCell is not { } cell) return;

        var range = FindNumericRunAbove(cell) ?? FindNumericRunLeft(cell);
        if (range is null)
        {
            StatusText = "AutoSum: no numbers found next to this cell.";
            return;
        }

        var (start, end) = range.Value;
        var formula = $"=SUM({start}:{end})";
        CommitTo(cell, formula);
        StatusText = $"{cell.Address} = {formula}";
    }

    private (string Start, string End)? FindNumericRunAbove(CellViewModel cell)
    {
        int end = cell.Row - 1;
        if (end < 0) return null;
        if (!IsNumericCell(_cells[end, cell.Column])) return null;
        int start = end;
        while (start - 1 >= 0 && IsNumericCell(_cells[start - 1, cell.Column])) start--;
        return (Addr(start, cell.Column), Addr(end, cell.Column));
    }

    private (string Start, string End)? FindNumericRunLeft(CellViewModel cell)
    {
        int end = cell.Column - 1;
        if (end < 0) return null;
        if (!IsNumericCell(_cells[cell.Row, end])) return null;
        int start = end;
        while (start - 1 >= 0 && IsNumericCell(_cells[cell.Row, start - 1])) start--;
        return (Addr(cell.Row, start), Addr(cell.Row, end));
    }

    private static string Addr(int row, int column)
        => new CellAddress(row, column).ToString();

    private static bool IsNumericCell(CellViewModel cell)
        => !cell.IsEmpty &&
           double.TryParse(cell.Display, System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out _);

    // ------------------------------------------------------------ grid growth

    [RelayCommand]
    private void AddRow()
    {
        RebuildGrid(_rows + 1, _columns);
        Recalculate();
        MarkDirty();
        StatusText = $"Grid is now {Rows} rows × {Columns} columns";
    }

    [RelayCommand]
    private void AddColumn()
    {
        RebuildGrid(_rows, _columns + 1);
        Recalculate();
        MarkDirty();
        StatusText = $"Grid is now {Rows} rows × {Columns} columns";
    }

    // ------------------------------------------------------------ recalculation

    public void Recalculate()
    {
        var calculator = new SheetCalculator((r, c) =>
            r >= 0 && r < _rows && c >= 0 && c < _columns ? _cells[r, c].Raw : string.Empty);

        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _columns; c++)
            {
                var cell = _cells[r, c];
                var display = calculator.DisplayAt(r, c);
                if (!string.Equals(cell.Display, display, StringComparison.Ordinal))
                    cell.Display = display;
            }
        }
    }

    // ------------------------------------------------------------ document hooks

    protected override void ApplyDocument()
    {
        ContentSeeder.EnsureSheet(Doc.Sheet, Doc.Text, Doc.Table);

        var rows = Math.Max(Doc.Sheet.RowCount, SheetDocument.MinRows);
        var columns = Math.Max(Doc.Sheet.ColumnCount, SheetDocument.MinColumns);
        RebuildGrid(rows, columns);

        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _columns; c++)
                _cells[r, c].SetRaw(string.Empty);

        foreach (var (position, source) in Doc.Sheet.Cells)
        {
            if (position.Row < _rows && position.Column < _columns)
                _cells[position.Row, position.Column].SetRaw(source.Raw);
        }

        Recalculate();
        SelectCell(_cells[0, 0]);
    }

    protected override void ResetDocument()
    {
        RebuildGrid(SheetDocument.MinRows, SheetDocument.MinColumns);
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _columns; c++)
            {
                _cells[r, c].SetRaw(string.Empty);
                _cells[r, c].Display = string.Empty;
            }
        Recalculate();
        SelectCell(_cells[0, 0]);
    }

    protected override void CollectDocument()
    {
        Doc.Sheet.ClearCells();
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _columns; c++)
            {
                var cell = _cells[r, c];
                if (cell.IsEmpty) continue;
                Doc.Sheet.Set(r, c, cell.Raw, cell.Display);
            }
        }
        Doc.Sheet.EnsureCapacity(_rows, _columns);
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

        yield return new MenuGroup("Insert",
            new MenuItemModel("Add Row", command: AddRowCommand),
            new MenuItemModel("Add Column", command: AddColumnCommand));

        yield return new MenuGroup("Cell",
            new MenuItemModel("AutoSum", command: AutoSumCommand),
            new MenuItemModel("Clear Cell", command: ClearCellCommand));

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
        yield return new ToolButtonModel("AutoSum", SuiteIcon.Sum, AutoSumCommand);
        yield return new ToolButtonModel("Clear", SuiteIcon.Clear, ClearCellCommand);
        yield return ToolButtonModel.Separator();
        yield return new ToolButtonModel("+ Row", SuiteIcon.AddRow, AddRowCommand);
        yield return new ToolButtonModel("+ Column", SuiteIcon.AddColumn, AddColumnCommand);
    }
}
