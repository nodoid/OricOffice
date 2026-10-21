// Parses Oric-1/Atmos .TAP cassette files (ROM byte-stream format):
//   leader ($16 x N), $24 sync, 9-byte header (type at [2], BE end/start addresses
//   at [4-5]/[6-7]), NUL-terminated filename, payload of (end-start+1) bytes.
// Dumps each file's metadata and printable text runs; optionally writes payloads.

using System.Text;

if (args.Length == 0)
{
    Console.WriteLine("usage: TapDump <file.tap> [payloadDir] [--lines]");
    return 1;
}

bool probeLines = args.Contains("--lines");
bool listing = args.Contains("--listing");

var tap = File.ReadAllBytes(args[0]);
var payloadDir = args.Length > 1 ? args[1] : null;
if (payloadDir is not null) Directory.CreateDirectory(payloadDir);

int position = 0;
int fileIndex = 0;
while (position < tap.Length - 14)
{
    // Find the next leader+sync.
    if (tap[position] != 0x16 || tap[position + 1] != 0x16)
    {
        position++;
        continue;
    }
    int p = position;
    while (p < tap.Length && tap[p] == 0x16) p++;
    if (p >= tap.Length || tap[p] != 0x24) { position++; continue; }
    p++; // past sync

    if (p + 9 > tap.Length) break;
    var header = tap.AsSpan(p, 9);
    int kind = header[2];
    int end = (header[4] << 8) | header[5];
    int start = (header[6] << 8) | header[7];
    p += 9;

    int nameStart = p;
    while (p < tap.Length && tap[p] != 0x00) p++;
    var name = Encoding.ASCII.GetString(tap, nameStart, p - nameStart);
    p++; // past NUL

    int length = end - start + 1;
    if (length <= 0 || length > tap.Length - p) { position++; continue; }

    var payload = tap.AsSpan(p, length).ToArray();
    Console.WriteLine($"file {fileIndex++}: leader@0x{position:X} name=\"{name}\" " +
                      $"type=0x{kind:X2} load=0x{start:X4} end=0x{end:X4} len={length}");

    if (payloadDir is not null)
    {
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        File.WriteAllBytes(Path.Combine(payloadDir, $"{Path.GetFileNameWithoutExtension(args[0])}_{fileIndex - 1}_{safe}.bin"), payload);
    }

    if (probeLines && kind == 0x00)
        LineProbe.Probe(payload);

    if (listing && kind == 0x00)
    {
        Console.WriteLine("-- BASIC listing --");
        Console.WriteLine(OricOffice.Core.Tape.OricBasicListing.Detokenize(payload, start));
        Console.WriteLine("-- end listing --");
    }

    // Printable runs (>= 6 chars) hint at embedded content.
    var run = new StringBuilder();
    for (int i = 0; i < payload.Length; i++)
    {
        char c = (char)payload[i];
        if (c is >= ' ' and <= '~') run.Append(c);
        else
        {
            if (run.Length >= 6) Console.WriteLine($"    +{i - run.Length}: \"{run}\"");
            run.Clear();
        }
    }
    if (run.Length >= 6) Console.WriteLine($"    end: \"{run}\"");

    position = p + length;
}

return 0;
