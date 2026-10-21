using System.Text;

namespace OricOffice.Core.Tape;

/// <summary>One file stored inside an Oric .tap cassette image.</summary>
public sealed class TapFile
{
    public string Name { get; set; } = string.Empty;

    /// <summary>True for tokenized BASIC (type $00), false for machine-code blocks ($80).</summary>
    public bool IsBasic { get; set; }

    public int LoadAddress { get; set; }

    /// <summary>Inclusive end address (payload length = EndAddress − LoadAddress + 1).</summary>
    public int EndAddress { get; set; }

    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public int Length => Payload.Length;
}

/// <summary>
/// The Oric-1/Atmos .TAP cassette byte-stream format, as written and read by
/// the ROM tape routines and emulated by Oricutron:
///   leader bytes ($16 × n, n ≥ 3), $24 sync, a 9-byte header (type at [2],
///   big-endian inclusive end/start addresses at [4-5]/[6-7]), a NUL-terminated
///   filename, then the payload. Files follow one another back to back.
/// </summary>
public static class OricTap
{
    public const byte LeaderByte = 0x16;
    public const byte SyncByte = 0x24;
    public const byte KindBasic = 0x00;
    public const byte KindBlock = 0x80;

    /// <summary>Parses every file in a .tap image. Tolerates short leaders and
    /// non-zero reserved header bytes (real tapes vary).</summary>
    public static List<TapFile> Read(byte[] tap)
    {
        var files = new List<TapFile>();
        if (tap is null || tap.Length < 14) return files;

        int position = 0;
        while (position < tap.Length - 14)
        {
            if (tap[position] != LeaderByte || tap[position + 1] != LeaderByte)
            {
                position++;
                continue;
            }

            int p = position;
            while (p < tap.Length && tap[p] == LeaderByte) p++;
            if (p >= tap.Length || tap[p] != SyncByte)
            {
                position++;
                continue;
            }
            p++; // past sync

            if (p + 9 > tap.Length) break;
            var header = tap.AsSpan(p, 9);
            bool isBasic = header[2] == KindBasic;
            int end = (header[4] << 8) | header[5];
            int start = (header[6] << 8) | header[7];
            p += 9;

            int nameStart = p;
            while (p < tap.Length && tap[p] != 0x00) p++;
            var name = Encoding.ASCII.GetString(tap, nameStart, p - nameStart).Trim();
            p++; // past the NUL

            int length = end - start + 1;
            if (length <= 0 || length > tap.Length - p)
            {
                position++; // not a real header — keep scanning
                continue;
            }

            files.Add(new TapFile
            {
                Name = name,
                IsBasic = isBasic,
                LoadAddress = start,
                EndAddress = end,
                Payload = tap.AsSpan(p, length).ToArray(),
            });

            position = p + length;
        }

        return files;
    }

    /// <summary>Writes files back into the .tap byte-stream format (3-byte leader).</summary>
    public static byte[] Write(IEnumerable<TapFile> files)
    {
        using var stream = new MemoryStream();
        foreach (var file in files)
        {
            var payload = file.Payload ?? Array.Empty<byte>();
            int end = file.LoadAddress + payload.Length - 1;

            stream.Write(new[]
            {
                LeaderByte, LeaderByte, LeaderByte, SyncByte,
                (byte)0x00, (byte)0x00,
                file.IsBasic ? KindBasic : KindBlock,
                (byte)0xC7,
                (byte)(end >> 8), (byte)(end & 0xFF),
                (byte)(file.LoadAddress >> 8), (byte)(file.LoadAddress & 0xFF),
                (byte)0x00,
            });
            var name = Encoding.ASCII.GetBytes(file.Name ?? string.Empty);
            stream.Write(name);
            stream.WriteByte(0x00);
            stream.Write(payload);
        }
        return stream.ToArray();
    }
}
