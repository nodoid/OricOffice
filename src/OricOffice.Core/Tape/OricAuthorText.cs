using System.Text;

namespace OricOffice.Core.Tape;

/// <summary>
/// Reads the document images produced by Tansoft's AUTHOR word processor
/// (John Dawson, 1983/84 — see docs/manual-command-audit.md). AUTHOR keeps its
/// file as a plain-ASCII text area, NUL-terminated, preceded by a 256-byte
/// printer installation block and the document name:
///
///   $3400..$347F  installation codes (print macros P0-P4, margins, lines/page,
///                 form length, copies, single-sheet — stored verbatim and opaque here)
///   $3480..$34FF  document name, space-padded
///   $3500..       the text, plain ASCII, ending at the first $00 byte
///
/// Printer control characters entered with [Control]C are stored with bit 6
/// set ($C0 | code) so the editor can show them in inverse video; a [Control]D
/// dot command is stored as $C0 | 'D' followed by the command letter and its
/// text, exactly as typed. The manual (pp. 28, 32) documents the codes:
/// I=$09 tab, J=$0A line feed, L=$0C form feed, M=$0D carriage return,
/// [=$1B escape. RETURN characters between paragraphs are stored as $0D.
/// </summary>
public sealed class OricAuthorDocument
{
    internal const int InstallSize = 256;
    internal const int NameOffset = 0x80;   // $3480 - $3400: space-padded document name
    internal const int TextOffset = 0x100;  // $3500 - $3400: the text begins here

    internal OricAuthorDocument(string name, byte[] install, string body)
    {
        Name = name;
        Install = install;
        Body = body;
    }

    /// <summary>Space-padded document name from the installation block (may be empty).</summary>
    public string Name { get; }

    /// <summary>The raw 256-byte printer installation block, preserved verbatim.</summary>
    public byte[] Install { get; }

    /// <summary>
    /// The text with AUTHOR's codes mapped to editor characters:
    /// RETURN ($0D) → newline, tab code ($C9) → tab, line-feed code ($CA) → newline,
    /// carriage-return code ($CD) → newline, form-feed code ($CC) → form feed,
    /// escape code ($DB) → ESC, text marker ($AE) → '·', auto-insert marker ($BE)
    /// → '>', dot-command marker ($C4) → '·' (the centred dot shown on screen).
    /// </summary>
    public string Body { get; }
}

/// <summary>Parses and detects AUTHOR document images (raw, or as a payload inside a .tap).</summary>
public static class OricAuthorText
{
    /// <summary>
    /// Detects the strict layout of an AUTHOR image so that other files — in
    /// particular a .tap that merely CONTAINS an AUTHOR payload — are never
    /// mistaken for one: the space-padded name field at $3480 must hold only
    /// printable text, and the NUL-terminated text must begin exactly at $3500
    /// and be made of printable ASCII plus AUTHOR's control codes.
    /// </summary>
    public static bool IsAuthorDocument(byte[]? data)
    {
        if (data is not { Length: > OricAuthorDocument.TextOffset + 8 }) return false;

        // The name field must look like a name: printable ASCII and spaces only.
        bool anyNameChar = false;
        for (int i = OricAuthorDocument.NameOffset; i < OricAuthorDocument.TextOffset; i++)
        {
            byte b = data[i];
            if (b == 0x20) continue;
            if (b is < 0x20 or >= 0x7F) return false;
            anyNameChar = true;
        }
        if (!anyNameChar) return false;

        int end = Array.IndexOf(data, (byte)0, OricAuthorDocument.TextOffset);
        if (end < OricAuthorDocument.TextOffset + 8) return false;

        int printable = 0, codes = 0;
        for (int i = OricAuthorDocument.TextOffset; i < end; i++)
        {
            byte b = data[i];
            if (b is >= 0x20 and < 0x7F) printable++;
            else if (b == 0x0D || b == 0xC9 || b == 0xCA || b == 0xCC || b == 0xCD ||
                     b == 0xDB || b == 0xAE || b == 0xBE || b == 0xC4) codes++;
            else return false;   // not an AUTHOR text stream
        }

        return printable >= 8 && printable + codes == end - OricAuthorDocument.TextOffset;
    }

    /// <summary>Parses an AUTHOR document image (see <see cref="OricAuthorDocument"/> for the layout).</summary>
    public static OricAuthorDocument Parse(byte[] image)
    {
        if (!IsAuthorDocument(image))
            throw new InvalidDataException("That file is not an AUTHOR document image.");

        var install = image.AsSpan(0, OricAuthorDocument.InstallSize).ToArray();

        var name = Encoding.ASCII
            .GetString(image, OricAuthorDocument.NameOffset,
                OricAuthorDocument.TextOffset - OricAuthorDocument.NameOffset)
            .Trim();

        int end = Array.IndexOf(image, (byte)0, OricAuthorDocument.TextOffset);
        var body = new StringBuilder(end - OricAuthorDocument.TextOffset);
        for (int i = OricAuthorDocument.TextOffset; i < end; i++)
            body.Append(Map(image[i]));

        return new OricAuthorDocument(name, install, body.ToString());
    }

    /// <summary>One AUTHOR text byte → its editor character (see <see cref="OricAuthorDocument.Body"/>).</summary>
    private static char Map(byte b) => b switch
    {
        0x0D => '\n',        // RETURN between paragraphs
        0xC9 => '\t',        // Control-I  tab
        0xCA => '\n',        // Control-J  line feed
        0xCC => '\f',        // Control-L  form feed (page break)
        0xCD => '\n',        // Control-M  carriage return
        0xDB => '\u001B',    // Control-[  escape
        0xAE => '·',         // text marker (inverse full stop on screen)
        0xBE => '>',         // auto-insert marker (inverse '>')
        0xC4 => '·',         // Control-D  dot command (centred dot on screen)
        _ => (char)b,        // printable ASCII
    };
}
