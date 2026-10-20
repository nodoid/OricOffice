namespace OricOffice.Core.Model;

/// <summary>
/// The unified document model. Every Oric Suite file (whatever its extension) carries
/// all three sections so that Author, Calc and Base can load each other's files
/// without losing content.
/// </summary>
public sealed class OricFile
{
    public const int CurrentFormatVersion = 1;

    /// <summary>Which product created/saved the file last (drives extension and default view).</summary>
    public OricFileKind Kind { get; set; } = OricFileKind.Author;

    public string Title { get; set; } = "Untitled";

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Author section: plain text body (paragraphs separated by newlines).</summary>
    public TextDocument Text { get; set; } = new();

    /// <summary>Calc section: cell grid with raw input and computed values.</summary>
    public SheetDocument Sheet { get; set; } = new();

    /// <summary>Base section: fields and records.</summary>
    public TableDocument Table { get; set; } = new();

    public static OricFile CreateNew(OricFileKind kind, string? title = null)
        => new()
        {
            Kind = kind,
            Title = title ?? "Untitled",
            CreatedUtc = DateTime.UtcNow,
        };
}
