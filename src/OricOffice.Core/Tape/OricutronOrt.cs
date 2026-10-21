namespace OricOffice.Core.Tape;

/// <summary>
/// Converts Oricutron .ORT raw-tape captures (F9 "Save tape output…") into
/// standard .TAP data. Two kinds of content are recognised, mirroring what
/// Oricutron itself writes (tape.c, GPL):
///   • $FF sections — when tape capture runs together with turbo tape,
///     program saves are patched straight into the capture as
///     $FF + big-endian length + the raw .tap bytes; they are lifted out
///     verbatim.
///   • Standard waveforms — a faithful port of <c>tapsections()</c> and
///     <c>next_ort_value()</c>: the ORT stream holds toggle-to-toggle
///     half-period counts in 2-cycle units, every bit cell of the standard
///     Oric waveform decodes to either a ~208-count ("1") or a ~416-count
///     ("0") full period, and decoded sections are reassembled as
///     3×$16 leader, $24 sync, 9-byte header, NUL-terminated name, payload.
///     Leftovers are re-scanned in "slow" mode, exactly as Oricutron's
///     WAV→ORT converter does. (Captures taken with turbo tape switched OFF
///     while a program bit-bangs its own custom waveform — e.g. Tansoft
///     turbo loaders — are not standard-encoded and cannot be decoded here.)
/// </summary>
public static class OricutronOrt
{
    // Timing bounds from tape.h, in ORT count units (2 cycles each):
    //   TAPE_DECODE_1_MIN = 208/4, TAPE_DECODE_0_MIN = 208 + 208/5
    private const int Decode1Min = 52;
    private const int Decode0Min = 249;

    /// <summary>True when the data starts with the "ORT\0" magic.</summary>
    public static bool IsOrt(byte[]? data)
        => data is { Length: >= 4 }
           && data[0] == (byte)'O' && data[1] == (byte)'R'
           && data[2] == (byte)'T' && data[3] == 0x00;

    /// <summary>Decodes a .ORT capture into a standard .TAP byte stream.</summary>
    public static byte[] ToTap(byte[] ort)
    {
        if (!IsOrt(ort))
            throw new InvalidDataException("That file is not an Oricutron .ORT tape capture.");

        // The whole capture, mutated in place exactly like Oricutron's buffer:
        // decoded sections are spliced out as $FF + length + section bytes.
        var buffer = new List<byte>(ort);

        // 1) $FF sections written by Oricutron's save-patch (turbo capture):
        //    their payload already IS .tap byte-stream data.
        var embedded = ExtractEmbeddedSections(buffer);

        // 2) Standard-encoded waveform sections, fast then slow pass.
        var sections = new List<byte[]>();
        Decode(buffer, slow: false, sections);
        Decode(buffer, slow: true, sections);

        using var stream = new MemoryStream();
        foreach (var section in embedded) stream.Write(section);
        foreach (var section in sections) stream.Write(section);
        return stream.ToArray();
    }

    /// <summary>
    /// Walks the count stream and lifts out every $FF + BE16-length section
    /// (Oricutron's save-patch format — see tape_stop_savepatch) in file
    /// order. Count escapes ($FC/$FD) are stepped over exactly as
    /// <c>next_ort_value</c> does; $FE marks invalid data and ends the walk.
    /// </summary>
    private static List<byte[]> ExtractEmbeddedSections(List<byte> buffer)
    {
        var result = new List<byte[]>();
        if (buffer.Count < 5) return result; // magic only — a capture with no signal
        int end = buffer.Count;
        int offset = 5; // "ORT\0" magic + initial tape level byte
        while (offset < end)
        {
            byte b = buffer[offset];
            if (b < 0xFC) { offset++; continue; }
            if (b == 0xFC) { offset += 2; continue; }
            if (b == 0xFD) { offset += 3; continue; }
            if (b == 0xFE) break;
            if (b == 0xFF)
            {
                if (offset + 3 > end) break;
                int length = (buffer[offset + 1] << 8) | buffer[offset + 2];
                int dataStart = offset + 3;
                if (length <= 0 || dataStart + length > end) break;
                result.Add(buffer.GetRange(dataStart, length).ToArray());
                offset = dataStart + length;
            }
        }
        return result;
    }

    /// <summary>TIME_TO_BIT from tape.h: too fast is invalid, ~208 is 1, ~416 is 0.</summary>
    private static int TimeToBit(int cycles)
        => cycles < Decode1Min ? -1 : cycles < Decode0Min ? 1 : 0;

    /// <summary>validbyte() from tape.c: start bit set, fixed bit clear, odd parity over bits 2–10.</summary>
    private static bool ValidByte(int accum)
    {
        if ((accum & 3) != 1) return false;
        int parity = 0;
        for (int i = 4; i != 0x800; i <<= 1)
            if ((accum & i) != 0) parity ^= 1;
        return parity != 0;
    }

    /// <summary>
    /// next_ort_value() from tape.c: the next half-period count, or 0 for
    /// invalid data (which ends the pass). $FF markers — sections decoded by an
    /// earlier pass — are skipped instead.
    /// </summary>
    private static int NextValue(List<byte> buffer, ref int offset, int end, ref bool anySection)
    {
        while (offset < end)
        {
            switch (buffer[offset])
            {
                case 0xFC:
                    if (offset >= end - 1) return 0;
                    offset += 2;
                    return buffer[offset - 1];

                case 0xFD:
                    if (offset >= end - 2) return 0;
                    int wide = (buffer[offset + 1] << 8) | buffer[offset + 2];
                    offset += 3;
                    return wide;

                case 0xFE:
                    return 0;

                case 0xFF:
                    if (offset >= end - 2) return 0;
                    int skip = (buffer[offset + 1] << 8) | buffer[offset + 2];
                    offset += 3;
                    if (offset >= end - skip) return 0;
                    offset += skip;
                    anySection = true;
                    break;

                default:
                    return buffer[offset++];
            }
        }
        return 0;
    }

    private enum State
    {
        Search,
        Leader,
        FindHeader,
        Header,
        FileName,
        FindData,
        Data,
    }

    /// <summary>
    /// One tapsections() pass over the buffer. Completed sections are appended
    /// to <paramref name="sections"/> and spliced out of the buffer as
    /// $FF + big-endian length + section bytes, so a following pass skips them.
    /// </summary>
    private static void Decode(List<byte> buffer, bool slow, List<byte[]> sections)
    {
        if (buffer.Count < 5) return;        // magic only — a capture with no signal
        int offset = 4;                      // "ORT\0" magic
        int tapeBit = buffer[offset++];      // initial tape level
        int cycleCount = 0;
        var state = State.Search;
        int accum = 0;
        int bitCount = 0;
        int leaderStart = 0, leaderCount = 0;
        int dataBytes = 0, tapBytes = 0;
        int slow0s = 0, slow1s = 0;
        bool anySection = false;

        // Recent offsets, so a detected leader knows where its counts began.
        var lastOffsets = new int[20];
        for (int i = 0; i < 20; i++) lastOffsets[i] = 5;

        var scratch = new byte[70000];       // section under construction (header+name+payload)

        int end = buffer.Count;
        while (offset < end)
        {
            Array.Copy(lastOffsets, 1, lastOffsets, 0, 19);
            lastOffsets[19] = offset;

            int value = NextValue(buffer, ref offset, end, ref anySection);
            if (value < 1) return; // invalid ORT data — this pass is over

            cycleCount += value;
            tapeBit ^= 1;
            // Falling edge: keep accumulating — only rising edges decode
            // (cyccount spans one full period) and reset the counter.
            if (tapeBit == 0) continue;

            if (cycleCount < 100) continue; // too fast — merge with the next half-period

            int thisBit = TimeToBit(cycleCount);
            if (thisBit < 0)
            {
                // Outside standard encoding — start searching again.
                state = State.Search;
                cycleCount = 0;
                accum = 0;
                slow1s = 0;
                slow0s = 0;
                continue;
            }

            if (slow)
            {
                // Slow tape repeats each cell; require two equal cells in a row.
                if (thisBit == 1)
                {
                    slow1s++;
                    slow0s = 0;
                    if (slow1s != 2)
                    {
                        if (slow1s >= 8) slow1s = 0;
                        cycleCount = 0;
                        continue;
                    }
                }
                else
                {
                    slow0s++;
                    slow1s = 0;
                    if (slow0s != 2)
                    {
                        if (slow0s >= 4) slow0s = 0;
                        cycleCount = 0;
                        continue;
                    }
                }
            }

            accum = (accum >> 1) | (thisBit << 10);

            switch (state)
            {
                case State.Search:
                    // Look for $16 fully encoded ($58 with start bit and parity)
                    if ((accum & 0x7FE) == 0x58)
                    {
                        leaderStart = lastOffsets[8];
                        leaderCount = 1;
                        state = State.Leader;
                        bitCount = 0;
                    }
                    break;

                case State.Leader:
                    bitCount = (bitCount + 1) % 14;
                    if (bitCount == 13 && ValidByte(accum)) bitCount = 0;
                    if (bitCount == 0)
                    {
                        if ((accum & 0x7FE) != 0x58)
                        {
                            state = State.Search;
                            break;
                        }
                        leaderCount++;
                        if (leaderCount == 4) state = State.FindHeader;
                    }
                    break;

                case State.FindHeader:
                    bitCount = (bitCount + 1) % 14;
                    if (bitCount == 13 && ValidByte(accum)) bitCount = 0;
                    if (bitCount == 0 && (accum & 0x7FE) == 0x490) // $24 fully encoded
                    {
                        scratch[0] = 0x16;
                        scratch[1] = 0x16;
                        scratch[2] = 0x16;
                        scratch[3] = 0x24;
                        tapBytes = 4;
                        state = State.Header;
                    }
                    break;

                case State.Header:
                    bitCount = (bitCount + 1) % 14;
                    if (bitCount == 13 && ValidByte(accum)) bitCount = 0;
                    if (bitCount == 0)
                    {
                        scratch[tapBytes++] = (byte)((accum >> 2) & 0xFF);
                        if (tapBytes == 13)
                        {
                            int loadStart = (scratch[10] << 8) | scratch[11];
                            int loadEnd = (scratch[8] << 8) | scratch[9];
                            if (loadEnd <= loadStart)
                            {
                                state = State.Search;
                                break;
                            }
                            dataBytes = loadEnd - loadStart + 1;
                            state = State.FileName;
                        }
                    }
                    break;

                case State.FileName:
                    bitCount = (bitCount + 1) % 14;
                    if (bitCount == 13 && ValidByte(accum)) bitCount = 0;
                    if (bitCount == 0)
                    {
                        scratch[tapBytes++] = (byte)((accum >> 2) & 0xFF);
                        if (scratch[tapBytes - 1] == 0) state = State.FindData;
                    }
                    break;

                case State.FindData:
                    // The payload can start at any bit position: hunt a valid byte.
                    if (ValidByte(accum))
                    {
                        scratch[tapBytes++] = (byte)((accum >> 2) & 0xFF);
                        dataBytes--;
                        bitCount = 0;
                        state = State.Data;
                    }
                    break;

                case State.Data:
                    bitCount = (bitCount + 1) % 14;
                    if (bitCount == 13 && ValidByte(accum)) bitCount = 0;
                    if (bitCount == 0)
                    {
                        scratch[tapBytes++] = (byte)((accum >> 2) & 0xFF);
                        dataBytes--;
                        if (dataBytes < 0)
                        {
                            state = State.Search;
                            break;
                        }
                        if (dataBytes > 0) break;

                        // A whole standard Oric tape section.
                        if (!anySection && leaderStart < 512) leaderStart = 5;
                        var section = scratch.AsSpan(0, tapBytes).ToArray();
                        sections.Add(section);

                        // Splice it out of the raw stream as $FF + length + bytes.
                        buffer.RemoveRange(leaderStart, offset - leaderStart);
                        buffer.InsertRange(leaderStart, new byte[]
                        {
                            0xFF, (byte)(tapBytes >> 8), (byte)(tapBytes & 0xFF),
                        });
                        buffer.InsertRange(leaderStart + 3, section);
                        end -= offset - (leaderStart + 3 + tapBytes);
                        offset = leaderStart + 3 + tapBytes;

                        state = State.Search;
                        accum = 0;
                        anySection = true;
                    }
                    break;
            }

            cycleCount = 0;
        }
    }
}
