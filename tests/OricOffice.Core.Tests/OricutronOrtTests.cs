using System.Text;
using OricOffice.Core.Export;
using OricOffice.Core.Model;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.Tape;
using OricOffice.Core.ViewModels;

namespace OricOffice.Core.Tests;

/// <summary>
/// OricutronOrt converts .ORT tape captures into standard .TAP data. The
/// waveform decoder is exercised against a synthetic standard-encoded capture
/// (Oricutron's tape_ticktock model: 14-period byte cells, 208/416-cycle
/// pulses), and the $FF save-patch extraction against Oricutron's embedded
/// section format. The committed hello.tap fixture — a program CSAVE'd by the
/// emulated Oric Atmos ROM through Oricutron's tape patches — proves the whole
/// chain end to end.
/// </summary>
public class OricutronOrtTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeLifetime _lifetime = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Locates tests/fixtures by walking up from the test assembly.</summary>
    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Fixture '{name}' not found — regenerate it from an Oricutron capture.");
    }

    // ------------------------------------------------------------ synthetic ORT

    /// <summary>
    /// A tape section as the ROM writes it: 4×$16 leader, $24 sync, the
    /// 9-byte header, NUL-terminated name, payload.
    /// </summary>
    private static byte[] BuildSection(string name, byte[] payload, int load)
    {
        int end = load + payload.Length - 1;
        var data = new List<byte>
        {
            0x16, 0x16, 0x16, 0x16, 0x24,
            0x00, 0x00, 0x80, 0xC7,
            (byte)(end >> 8), (byte)(end & 0xFF),
            (byte)(load >> 8), (byte)(load & 0xFF),
            0x00,
        };
        data.AddRange(Encoding.ASCII.GetBytes(name));
        data.Add(0x00);
        data.AddRange(payload);
        return data.ToArray();
    }

    /// <summary>
    /// Encodes tap bytes as a standard wave .ORT: every byte becomes a
    /// 14-period cell (start, sync, D0–D7, parity, three stop pulses), each
    /// period a pair of equal half-period counts in 2-cycle units.
    /// </summary>
    private static byte[] EncodeStandardOrt(byte[] tapBytes)
    {
        var halves = new List<int>();
        foreach (var value in tapBytes)
        {
            var cell = new List<int> { 1, 0 };
            int parity = 1;
            for (int i = 0; i < 8; i++)
            {
                int bit = (value >> i) & 1;
                cell.Add(bit);
                parity ^= bit;
            }
            cell.Add(parity & 1);
            cell.AddRange(new[] { 1, 1, 1 });

            foreach (var bit in cell)
            {
                int width = bit == 1 ? 104 : 208;
                halves.Add(width);
                halves.Add(width);
            }
        }

        // "ORT\0", initial level high so the first rising edge completes a
        // full period, then one count per half-period.
        var ort = new List<byte> { (byte)'O', (byte)'R', (byte)'T', 0x00, 0x01 };
        foreach (var width in halves)
        {
            if (width < 0xFC)
            {
                ort.Add((byte)width);
            }
            else if (width < 0x100)
            {
                ort.Add(0xFC);
                ort.Add((byte)width);
            }
            else
            {
                ort.Add(0xFD);
                ort.Add((byte)(width >> 8));
                ort.Add((byte)(width & 0xFF));
            }
        }
        return ort.ToArray();
    }

    // ------------------------------------------------------------ waveform decode

    [Fact]
    public void Ort_StandardWaveform_DecodesBackToTheTapBytes()
    {
        var payload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var ort = EncodeStandardOrt(BuildSection("TESTDOC", payload, load: 0x0400));

        var tap = OricutronOrt.ToTap(ort);

        var file = Assert.Single(OricTap.Read(tap));
        Assert.Equal("TESTDOC", file.Name);
        Assert.False(file.IsBasic);
        Assert.Equal(0x0400, file.LoadAddress);
        Assert.Equal(payload, file.Payload);
    }

    [Fact]
    public void Ort_NonOrtData_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            OricutronOrt.ToTap(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    // ------------------------------------------------------------ embedded sections

    [Fact]
    public void Ort_EmbeddedSavePatchSections_AreExtractedVerbatim()
    {
        // Oricutron's save-patch writes $FF + BE16 length + the raw .tap bytes
        // straight into the capture (tape.c: tape_stop_savepatch).
        var section = Encoding.ASCII.GetBytes("payload bytes here");
        var ort = new List<byte> { (byte)'O', (byte)'R', (byte)'T', 0x00, 0x00 };
        ort.AddRange(new byte[] { 104, 104, 208 }); // some raw counts
        ort.Add(0xFF);
        ort.Add((byte)(section.Length >> 8));
        ort.Add((byte)(section.Length & 0xFF));
        ort.AddRange(section);
        ort.AddRange(new byte[] { 104, 208 }); // trailing counts

        var tap = OricutronOrt.ToTap(ort.ToArray());

        Assert.Equal(section, tap);
    }

    [Fact]
    public void Ort_EmptyCapture_ProducesNoTapFiles()
    {
        // A capture stopped before any tape activity is just the magic —
        // exactly what Oricutron left on disk for an empty recording.
        var ort = new byte[] { (byte)'O', (byte)'R', (byte)'T', 0x00 };
        Assert.Empty(OricTap.Read(OricutronOrt.ToTap(ort)));
    }

    // ------------------------------------------------------------ emulator-produced fixture

    [Fact]
    public void EmulatorFixture_HoldsTheSavedBasicProgram()
    {
        var files = OricTap.Read(File.ReadAllBytes(Fixture("hello.tap")));

        var file = Assert.Single(files);
        Assert.Equal("HELLO", file.Name);
        Assert.True(file.IsBasic);
        Assert.Equal(0x0501, file.LoadAddress);
        Assert.True(file.Length > 0);
    }

    [Fact]
    public void EmulatorFixture_DetokenizesToTheSavedProgram()
    {
        var file = Assert.Single(OricTap.Read(File.ReadAllBytes(Fixture("hello.tap"))));

        var listing = OricBasicListing.Detokenize(file.Payload, file.LoadAddress);

        Assert.Contains("10 PRINT\"HELLO WORLD\"", listing);
    }

    [Fact]
    public void EmulatorFixture_ImportsIntoAnAuthorDocument()
    {
        var doc = TapeImporter.ImportFile(Fixture("hello.tap"), OricFileKind.Author);

        Assert.Equal(OricFileKind.Author, doc.Kind);
        Assert.Contains("HELLO", doc.Text.Body);
        Assert.Contains("HELLO WORLD", doc.Text.Body);
    }

    [Fact]
    public async Task AuthorModule_ImportsTheEmulatorTape()
    {
        var vm = new AuthorViewModel(_dialogs, _lifetime, new FakeClipboard(),
            new OricFileStore(_dir.Path), new AppMeta("Oric Author", "1.0.0", OricFileKind.Author));

        _dialogs.OpenPath = Fixture("hello.tap");
        await vm.ImportTapeCommand.ExecuteAsync(null);

        Assert.Equal("hello", vm.Title);
        Assert.Contains("HELLO WORLD", vm.Body);
        Assert.False(vm.IsDirty); // the imported listing starts life clean
    }
}
