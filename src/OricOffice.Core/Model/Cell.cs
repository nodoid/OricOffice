using OricOffice.Core.Calc;

namespace OricOffice.Core.Model;

/// <summary>A single spreadsheet cell: raw user input plus the last computed display value.</summary>
public sealed class Cell
{
    /// <summary>0-based row index.</summary>
    public int Row { get; set; }

    /// <summary>0-based column index (0 = column A).</summary>
    public int Column { get; set; }

    /// <summary>Exactly what the user typed ("=A1+2", "42", "hello"). Empty means blank cell.</summary>
    public string Raw { get; set; } = string.Empty;

    /// <summary>Last computed display value. Kept in the file so non-Calc apps can show it.</summary>
    public string Value { get; set; } = string.Empty;

    public Cell()
    {
    }

    public Cell(int row, int column, string raw = "", string value = "")
    {
        Row = row;
        Column = column;
        Raw = raw ?? string.Empty;
        Value = value ?? string.Empty;
    }

    public bool HasFormula => FormulaEngine.IsFormula(Raw);

    public bool IsEmpty => string.IsNullOrEmpty(Raw);
}
