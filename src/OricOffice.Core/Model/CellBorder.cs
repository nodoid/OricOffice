namespace OricOffice.Core.Model;

/// <summary>One side of one table cell.</summary>
public enum BorderSide
{
    Top = 0,
    Right = 1,
    Bottom = 2,
    Left = 3,
}

/// <summary>A set of cell sides (for applying borders in one step).</summary>
[Flags]
public enum BorderSides
{
    None = 0,
    Top = 1,
    Right = 2,
    Bottom = 4,
    Left = 8,
    All = Top | Right | Bottom | Left,
}

/// <summary>Border line styles offered by the table designer.</summary>
public enum BorderStyle
{
    None = 0,
    Thin = 1,
    Thick = 2,
    Double = 3,
    Dashed = 4,
    Dotted = 5,
}

/// <summary>Style and colour of one cell side.</summary>
public sealed class CellBorder : IEquatable<CellBorder>
{
    public CellBorder()
    {
    }

    public CellBorder(BorderStyle style, string? colour)
    {
        Style = style;
        Colour = colour;
    }

    public BorderStyle Style { get; set; } = BorderStyle.Thin;

    /// <summary>Colour as #RRGGBB; null means the default ink.</summary>
    public string? Colour { get; set; }

    public bool Equals(CellBorder? other)
        => other is not null && Style == other.Style && Colour == other.Colour;

    public override bool Equals(object? obj) => obj is CellBorder b && Equals(b);

    public override int GetHashCode() => HashCode.Combine(Style, Colour);
}
