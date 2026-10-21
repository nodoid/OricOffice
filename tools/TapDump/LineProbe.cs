using System.Text;

/// <summary>
/// Brute-forces the BASIC line layout of a memory-image payload: tries start
/// offsets and endianness for a chain of [len][line#][text][0x00] records.
/// </summary>
internal static class LineProbe
{
    public static void Probe(byte[] data, int maxStart = 8)
    {
        Console.WriteLine($"-- line probe ({data.Length} bytes) --");
        for (int start = 0; start < maxStart; start++)
        foreach (var littleLen in new[] { true, false })
        foreach (var littleNumber in new[] { true, false })
        {
            int position = start;
            var numbers = new List<int>();
            bool ok = true;
            while (position < data.Length - 1)
            {
                int length = Read16(data, position, littleLen);
                if (length == 0 || length > data.Length - position || length < 4) { ok = false; break; }
                if (data[position + length - 1] != 0x00) { ok = false; break; }
                numbers.Add(Read16(data, position + 2, littleNumber));
                position += length;
            }

            if (ok && numbers.Count > 3)
            {
                Console.WriteLine($"  start={start} lenLittle={littleLen} numLittle={littleNumber} " +
                                  $"lines={numbers.Count} first=[{string.Join(",", numbers.Take(8))}] " +
                                  $"last=[{string.Join(",", numbers.Skip(Math.Max(0, numbers.Count - 4)))}] " +
                                  $"endedAt={position}/{data.Length}");
            }
        }
    }

    private static int Read16(byte[] data, int offset, bool little)
        => little ? data[offset] | (data[offset + 1] << 8)
                  : (data[offset] << 8) | data[offset + 1];
}
