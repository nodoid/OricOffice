using System.Text;
using OricOffice.Core.Model;
using OricOffice.Core.Tape;

namespace OricOffice.Core.Export;

/// <summary>
/// Imports Oric .tap cassette images — including the original 1984 Tansoft
/// program tapes — as readable text listings. BASIC files are detokenized;
/// machine-code blocks contribute their printable strings.
/// </summary>
public static class TapeImporter
{
    /// <summary>Builds a document of <paramref name="kind"/> whose text is the tape listing.</summary>
    public static OricFile ImportFile(string path, OricFileKind kind)
        => Import(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path), kind);

    public static OricFile Import(byte[] tap, string title, OricFileKind kind)
    {
        // A raw AUTHOR document image (install block + NUL-terminated text) is
        // imported as the document itself, not as a tape listing.
        if (OricAuthorText.IsAuthorDocument(tap))
        {
            var authorDoc = OricAuthorText.Parse(tap);
            var raw = OricFile.CreateNew(kind, string.IsNullOrWhiteSpace(authorDoc.Name) ? title : authorDoc.Name);
            raw.Text.Body = authorDoc.Body;
            return raw;
        }

        var files = OricTap.Read(tap);
        if (files.Count == 0)
            throw new InvalidDataException("That file does not contain any Oric tape files.");

        var body = new StringBuilder();
        foreach (var file in files)
        {
            var name = string.IsNullOrWhiteSpace(file.Name) ? "(unnamed)" : file.Name;

            // An AUTHOR text stored on tape carries the same install+text image
            // as its payload; import it as the document rather than a listing.
            if (OricAuthorText.IsAuthorDocument(file.Payload))
            {
                var doc = OricAuthorText.Parse(file.Payload);
                var document = OricFile.CreateNew(kind, name);
                document.Text.Body = doc.Body;
                return document;
            }

            body.Append("==== ")
                .Append(name)
                .Append(file.IsBasic ? " [BASIC]" : " [BLOCK]")
                .Append($" ${file.LoadAddress:X4}-${file.EndAddress:X4}")
                .Append(" ====\n");

            body.Append(file.IsBasic
                ? OricBasicListing.Detokenize(file.Payload, file.LoadAddress)
                : OricBasicListing.PrintableRuns(file.Payload));
            body.Append('\n');
        }

        var outDoc = OricFile.CreateNew(kind, title);
        outDoc.Text.Body = body.ToString().TrimEnd() + "\n";
        return outDoc;
    }
}
