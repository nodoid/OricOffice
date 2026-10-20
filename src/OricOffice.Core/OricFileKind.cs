namespace OricOffice.Core;

/// <summary>The three Oric products ported by this suite.</summary>
public enum OricFileKind
{
    Author = 0,
    Calc = 1,
    Base = 2,
}

/// <summary>Canonical file extensions: .oau (Author), .oca (Calc), .oba (Base).</summary>
public static class OricFileExtensions
{
    public const string Author = ".oau";
    public const string Calc = ".oca";
    public const string Base = ".oba";

    public static IReadOnlyList<string> All { get; } = new[] { Author, Calc, Base };

    public static string For(this OricFileKind kind) => kind switch
    {
        OricFileKind.Calc => Calc,
        OricFileKind.Base => Base,
        _ => Author,
    };

    public static string DisplayName(this OricFileKind kind) => kind switch
    {
        OricFileKind.Calc => "Oric Calc",
        OricFileKind.Base => "Oric Base",
        _ => "Oric Author",
    };

    /// <summary>Maps an extension to a kind (case-insensitive). Null when unknown.</summary>
    public static OricFileKind? KindFromExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        if (ext.Equals(Author, StringComparison.OrdinalIgnoreCase)) return OricFileKind.Author;
        if (ext.Equals(Calc, StringComparison.OrdinalIgnoreCase)) return OricFileKind.Calc;
        if (ext.Equals(Base, StringComparison.OrdinalIgnoreCase)) return OricFileKind.Base;
        return null;
    }

    public static bool IsSuiteExtension(string? extension) => KindFromExtension(extension) is not null;
}
