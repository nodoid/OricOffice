namespace OricOffice.Core.Model;

/// <summary>
/// A table embedded in an Author document: a grid of text cells with optional
/// background fills. Rows and columns can be added and removed; the grid never
/// shrinks below one row or one column.
/// </summary>
public sealed class TableBlock
{
    public const int MinRows = 1;
    public const int MinColumns = 1;

    private string[][] _text = Array.Empty<string[]>();
    private string?[][] _fill = Array.Empty<string?[]>();
    private readonly Dictionary<(int Row, int Column, BorderSide Side), CellBorder> _borders = new();

    public TableBlock()
        : this("Table", 3, 3)
    {
    }

    public TableBlock(string name, int rows, int columns)
    {
        Name = name;
        Allocate(Math.Max(rows, MinRows), Math.Max(columns, MinColumns));
    }

    public string Name { get; set; } = "Table";

    public int Rows => _text.Length;

    public int Columns => _text.Length > 0 ? _text[0].Length : 0;

    public bool IsEmpty => _text.All(row => row.All(string.IsNullOrEmpty));

    // ------------------------------------------------------------ cell access

    public string TextAt(int row, int column)
        => InBounds(row, column) ? _text[row][column] : string.Empty;

    public string? FillAt(int row, int column)
        => InBounds(row, column) ? _fill[row][column] : null;

    public void SetText(int row, int column, string? text)
    {
        if (!InBounds(row, column)) return;
        _text[row][column] = text ?? string.Empty;
    }

    /// <summary>Sets a background colour as #RRGGBB, or null/empty to clear it.</summary>
    public void SetFill(int row, int column, string? fill)
    {
        if (!InBounds(row, column)) return;
        _fill[row][column] = string.IsNullOrWhiteSpace(fill) ? null : fill;
    }

    // ------------------------------------------------------------ borders

    /// <summary>All explicit cell borders (cells/sides without an entry render the default grid).</summary>
    public IReadOnlyDictionary<(int Row, int Column, BorderSide Side), CellBorder> Borders => _borders;

    /// <summary>The explicit border for one cell side, or null when unset.</summary>
    public CellBorder? BorderAt(int row, int column, BorderSide side)
        => InBounds(row, column) && _borders.TryGetValue((row, column, side), out var border)
            ? border
            : null;

    /// <summary>Sets one cell side; null (or style None) removes the override.</summary>
    public void SetBorder(int row, int column, BorderSide side, CellBorder? border)
    {
        if (!InBounds(row, column)) return;
        var key = (row, column, side);
        if (border is null || border.Style == BorderStyle.None)
            _borders.Remove(key);
        else
            _borders[key] = border;
    }

    /// <summary>Applies a style/colour to the chosen sides of a rectangular region
    /// (region is clamped to the grid; style None clears those sides).</summary>
    public void ApplyBorder(
        int row, int column, int rowCount, int columnCount,
        BorderSides sides, BorderStyle style, string? colour)
    {
        var all = new[]
        {
            (BorderSide.Top, BorderSides.Top),
            (BorderSide.Right, BorderSides.Right),
            (BorderSide.Bottom, BorderSides.Bottom),
            (BorderSide.Left, BorderSides.Left),
        };

        for (int r = Math.Max(0, row); r < Rows && r < row + rowCount; r++)
        for (int c = Math.Max(0, column); c < Columns && c < column + columnCount; c++)
        {
            foreach (var (side, flag) in all)
            {
                if (!sides.HasFlag(flag)) continue;
                SetBorder(r, c, side,
                    style == BorderStyle.None ? null : new CellBorder(style, colour));
            }
        }
    }

    // ------------------------------------------------------------ structure

    public void AddRow(int? afterRow = null)
    {
        var index = afterRow is { } a && a >= 0 && a < Rows ? a + 1 : Rows;
        InsertRow(index);
    }

    public void RemoveRow(int row)
    {
        if (!InBounds(row, 0) || Rows <= MinRows) return;
        var text = new string[Rows - 1][];
        var fill = new string?[Rows - 1][];
        for (int r = 0, w = 0; r < Rows; r++)
        {
            if (r == row) continue;
            text[w] = _text[r];
            fill[w] = _fill[r];
            w++;
        }
        _text = text;
        _fill = fill;
        RekeyBorders((r, c, s) => r == row ? (-1, -1, s) : (r > row ? r - 1 : r, c, s));
    }

    public void AddColumn(int? afterColumn = null)
    {
        var index = afterColumn is { } a && a >= 0 && a < Columns ? a + 1 : Columns;
        InsertColumn(index);
    }

    public void RemoveColumn(int column)
    {
        if (Columns <= MinColumns || column < 0 || column >= Columns) return;
        var rows = Rows;
        var columns = Columns;
        for (int r = 0; r < rows; r++)
        {
            var oldText = _text[r];
            var oldFill = _fill[r];
            var newText = new string[columns - 1];
            var newFill = new string?[columns - 1];
            for (int c = 0, w = 0; c < columns; c++)
            {
                if (c == column) continue;
                newText[w] = oldText[c] ?? string.Empty;
                newFill[w] = oldFill[c];
                w++;
            }
            _text[r] = newText;
            _fill[r] = newFill;
        }
        RekeyBorders((r, c, s) => c == column ? (-1, -1, s) : (r, c > column ? c - 1 : c, s));
    }

    // ------------------------------------------------------------ bulk (storage) access

    public (string[][] Text, string?[][] Fill) Snapshot() => (_text, _fill);

    /// <summary>Restores explicit borders (after the text grid has been restored).</summary>
    public void RestoreBorders(IReadOnlyDictionary<(int Row, int Column, BorderSide Side), CellBorder>? borders)
    {
        _borders.Clear();
        if (borders is null) return;
        foreach (var ((row, column, side), border) in borders)
            SetBorder(row, column, side, border);
    }

    public void Restore(string[][] text, string?[][]? fill, string? name)
    {
        if (name is not null) Name = name;
        if (text.Length == 0)
        {
            Allocate(MinRows, MinColumns);
            return;
        }

        var rows = text.Length;
        var columns = Math.Max(text.Max(r => r.Length), MinColumns);
        Allocate(rows, columns);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < text[r].Length && c < columns; c++)
                _text[r][c] = text[r][c] ?? string.Empty;

        if (fill is not null)
        {
            for (int r = 0; r < Math.Min(fill.Length, rows); r++)
                for (int c = 0; c < (fill[r]?.Length ?? 0) && c < columns; c++)
                    _fill[r][c] = fill[r]?[c];
        }
    }

    // ------------------------------------------------------------ internals

    /// <summary>Maps every border key through <paramref name="map"/>; (-1,-1) drops it.</summary>
    private void RekeyBorders(Func<int, int, BorderSide, (int Row, int Column, BorderSide Side)> map)
    {
        var rebuilt = new Dictionary<(int, int, BorderSide), CellBorder>();
        foreach (var ((row, column, side), border) in _borders)
        {
            var (r, c, s) = map(row, column, side);
            if (r >= 0 && c >= 0)
                rebuilt[(r, c, s)] = border;
        }
        _borders.Clear();
        foreach (var pair in rebuilt)
            _borders[pair.Key] = pair.Value;
    }

    private void Allocate(int rows, int columns)
    {
        _borders.Clear();
        _text = new string[rows][];
        _fill = new string?[rows][];
        for (int r = 0; r < rows; r++)
        {
            _text[r] = new string[columns];
            _fill[r] = new string?[columns];
            Array.Fill(_text[r], string.Empty);
        }
    }

    private void InsertRow(int index)
    {
        var rows = Rows;
        var columns = Columns;
        var text = new string[rows + 1][];
        var fill = new string?[rows + 1][];
        for (int r = 0, w = 0; r <= rows; r++)
        {
            if (r == index)
            {
                text[w] = new string[columns];
                Array.Fill(text[w], string.Empty);
                fill[w] = new string?[columns];
                w++;
                RekeyBorders((rr, cc, s) => rr >= index ? (rr + 1, cc, s) : (rr, cc, s));
                if (r >= rows) continue;
            }
            if (r < rows)
            {
                text[w] = _text[r];
                fill[w] = _fill[r];
                w++;
            }
        }
        _text = text;
        _fill = fill;
    }

    private void InsertColumn(int index)
    {
        var rows = Rows;
        var columns = Columns;
        for (int r = 0; r < rows; r++)
        {
            var oldText = _text[r];
            var oldFill = _fill[r];
            var newText = new string[columns + 1];
            var newFill = new string?[columns + 1];
            for (int c = 0, w = 0; c <= columns; c++)
            {
                if (c == index)
                {
                    newText[w] = string.Empty;
                    newFill[w] = null;
                    w++;
                    if (c >= columns) continue;
                }
                if (c < columns)
                {
                    newText[w] = oldText[c] ?? string.Empty;
                    newFill[w] = oldFill[c];
                    w++;
                }
            }
            _text[r] = newText;
            _fill[r] = newFill;
        }
        RekeyBorders((rr, cc, s) => cc >= index ? (rr, cc + 1, s) : (rr, cc, s));
    }

    private bool InBounds(int row, int column)
        => row >= 0 && row < Rows && column >= 0 && column < Columns;
}
