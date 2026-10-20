namespace OricOffice.Core.Calc;

/// <summary>An A1-style address (0-based row/column). Column A = 0, row 1 = 0.</summary>
public readonly struct CellAddress : IEquatable<CellAddress>
{
    public int Row { get; }
    public int Column { get; }

    public CellAddress(int row, int column)
    {
        Row = row;
        Column = column;
    }

    /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
    public static string ColumnName(int column)
    {
        if (column < 0) throw new ArgumentOutOfRangeException(nameof(column));
        Span<char> buffer = stackalloc char[8];
        int i = buffer.Length;
        int n = column;
        do
        {
            buffer[--i] = (char)('A' + n % 26);
            n = n / 26 - 1;
        } while (n >= 0 && i > 0);
        return new string(buffer[i..]);
    }

    /// <summary>Parses "B12", "$B$12", "b12" (case-insensitive).</summary>
    public static bool TryParse(string? text, out CellAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        int i = 0;
        if (i < s.Length && s[i] == '$') i++;

        int letterStart = i;
        while (i < s.Length && char.IsLetter(s[i])) i++;
        int letterCount = i - letterStart;
        if (letterCount is < 1 or > 3) return false;

        if (i < s.Length && s[i] == '$') i++;

        int digitStart = i;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        int digitCount = i - digitStart;
        if (digitCount is < 1 or > 7) return false;
        if (i != s.Length) return false;

        var column = 0;
        for (int k = letterStart; k < letterStart + letterCount; k++)
        {
            var c = char.ToUpperInvariant(s[k]);
            column = column * 26 + (c - 'A' + 1);
        }

        if (!int.TryParse(s[digitStart..i], out var rowNumber) || rowNumber < 1) return false;

        address = new CellAddress(rowNumber - 1, column - 1);
        return true;
    }

    public static CellAddress Parse(string text)
        => TryParse(text, out var address)
            ? address
            : throw new FormatException($"\"{text}\" is not a valid cell address (expected e.g. B12).");

    public override string ToString() => $"{ColumnName(Column)}{Row + 1}";

    public bool Equals(CellAddress other) => Row == other.Row && Column == other.Column;

    public override bool Equals(object? obj) => obj is CellAddress other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Row, Column);

    public static bool operator ==(CellAddress left, CellAddress right) => left.Equals(right);

    public static bool operator !=(CellAddress left, CellAddress right) => !left.Equals(right);
}
