namespace OricOffice.Core.Model;

/// <summary>
/// Calc section: a sparse cell map over a reserved grid. Only non-empty cells are stored.
/// </summary>
public sealed class SheetDocument
{
    public const int MinRows = 30;
    public const int MinColumns = 12;

    public string Name { get; set; } = "Sheet1";

    /// <summary>Reserved visible grid rows (grows as the user adds rows).</summary>
    public int RowCount { get; set; } = MinRows;

    /// <summary>Reserved visible grid columns (grows as the user adds columns).</summary>
    public int ColumnCount { get; set; } = MinColumns;

    private readonly Dictionary<(int Row, int Column), Cell> _cells = new();

    public IReadOnlyDictionary<(int Row, int Column), Cell> Cells => _cells;

    public int UsedRowCount { get; private set; }
    public int UsedColumnCount { get; private set; }

    public bool IsEmpty => _cells.Count == 0 || _cells.Values.All(c => c.IsEmpty);

    public Cell? Find(int row, int column)
        => _cells.TryGetValue((row, column), out var cell) ? cell : null;

    public string RawAt(int row, int column)
        => _cells.TryGetValue((row, column), out var cell) ? cell.Raw : string.Empty;

    public string ValueAt(int row, int column)
        => _cells.TryGetValue((row, column), out var cell) ? cell.Value : string.Empty;

    /// <summary>Sets raw input and computed value for a cell. Removing empties prunes the map.</summary>
    public void Set(int row, int column, string raw, string value)
    {
        raw ??= string.Empty;
        value ??= string.Empty;
        if (raw.Length == 0 && value.Length == 0)
        {
            _cells.Remove((row, column));
        }
        else
        {
            _cells[(row, column)] = new Cell(row, column, raw, value);
        }
        RecomputeUsed();
    }

    public void AddOrUpdate(Cell cell)
    {
        if (cell.Raw.Length == 0 && cell.Value.Length == 0)
        {
            _cells.Remove((cell.Row, cell.Column));
        }
        else
        {
            _cells[(cell.Row, cell.Column)] = cell;
        }
        RecomputeUsed();
    }

    public void ClearCells() { _cells.Clear(); UsedRowCount = 0; UsedColumnCount = 0; }

    public void EnsureCapacity(int rows, int columns)
    {
        if (rows > RowCount) RowCount = rows;
        if (columns > ColumnCount) ColumnCount = columns;
    }

    /// <summary>Keeps the reserved grid big enough to show every stored cell.</summary>
    public void EnsureCapacityForCells()
    {
        foreach (var (row, col) in _cells.Keys)
        {
            if (row + 1 > RowCount) RowCount = row + 1;
            if (col + 1 > ColumnCount) ColumnCount = col + 1;
        }
    }

    private void RecomputeUsed()
    {
        UsedRowCount = _cells.Count == 0 ? 0 : _cells.Keys.Max(k => k.Row) + 1;
        UsedColumnCount = _cells.Count == 0 ? 0 : _cells.Keys.Max(k => k.Column) + 1;
    }
}
