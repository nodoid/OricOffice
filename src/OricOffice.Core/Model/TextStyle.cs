namespace OricOffice.Core.Model;

/// <summary>Character-level emphasis flags (superscript and subscript are exclusive).</summary>
[Flags]
public enum TextStyleFlags
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8,
    Superscript = 16,
    Subscript = 32,
}

/// <summary>
/// An immutable character style. Null/absent members inherit from the
/// document defaults (Open Sans, page size, default text colour, no fill).
/// </summary>
public sealed class TextStyle : IEquatable<TextStyle>
{
    public string? FontFamily { get; init; }

    public double? FontSize { get; init; }

    /// <summary>Foreground (text) colour as #RRGGBB.</summary>
    public string? Foreground { get; init; }

    /// <summary>Background (highlight) colour as #RRGGBB.</summary>
    public string? Background { get; init; }

    public TextStyleFlags Flags { get; init; }

    public bool Equals(TextStyle? other)
        => other is not null
           && FontFamily == other.FontFamily
           && FontSize == other.FontSize
           && Foreground == other.Foreground
           && Background == other.Background
           && Flags == other.Flags;

    public override bool Equals(object? obj) => obj is TextStyle t && Equals(t);

    public override int GetHashCode()
        => HashCode.Combine(FontFamily, FontSize, Foreground, Background, Flags);

    public static bool operator ==(TextStyle? a, TextStyle? b) => Equals(a, b);

    public static bool operator !=(TextStyle? a, TextStyle? b) => !Equals(a, b);

    /// <summary>True when two styles render identically (for run merging).</summary>
    public bool SameAs(TextStyle other) => Equals(other);
}

/// <summary>A styled slice of the document body: [Start, Start + Length).</summary>
public sealed class TextRun
{
    public TextRun()
    {
    }

    public TextRun(int start, int length, TextStyle style)
    {
        Start = start;
        Length = length;
        Style = style;
    }

    public int Start { get; set; }

    public int Length { get; set; }

    public TextStyle Style { get; set; } = new();

    public int End => Start + Length;
}
