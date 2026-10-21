using OricOffice.Core.IO;

namespace OricOffice.Core.Tape;

/// <summary>
/// Turns a set of Oric .tap cassette images into a CSV catalogue — one row per
/// file stored on the tapes (tape name, file name, BASIC or block, load/end
/// address, byte count). The catalogue imports cleanly into both Oric Calc
/// (row 1 becomes the header line of the grid) and Oric Base (row 1 becomes
/// the field names, with Load/End/Bytes inferred as numbers).
/// </summary>
public static class TapeCatalog
{
    public static readonly string[] Headers = { "Tape", "File", "Type", "Load", "End", "Bytes" };

    /// <summary>Builds the catalogue CSV for the given tapes (name = file name without extension).</summary>
    public static string BuildCsv(IEnumerable<(string TapeName, byte[] Bytes)> tapes)
    {
        var rows = new List<IReadOnlyList<string>> { Headers };

        foreach (var (tapeName, bytes) in tapes.OrderBy(t => t.TapeName, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in OricTap.Read(bytes))
            {
                rows.Add(new[]
                {
                    tapeName,
                    file.Name,
                    file.IsBasic ? "BASIC" : "BLOCK",
                    file.LoadAddress.ToString(),
                    file.EndAddress.ToString(),
                    file.Length.ToString(),
                });
            }
        }

        return Csv.Write(rows);
    }

    /// <summary>Every .tap/.TAP image in a directory, as (name, bytes) pairs.</summary>
    public static List<(string TapeName, byte[] Bytes)> ReadDirectory(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory)
                .Where(p => string.Equals(Path.GetExtension(p), ".tap", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .Select(p => (Path.GetFileNameWithoutExtension(p), File.ReadAllBytes(p)))
                .ToList()
            : new List<(string, byte[])>();
}
