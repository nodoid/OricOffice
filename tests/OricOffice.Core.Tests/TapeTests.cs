using OricOffice.Core.Export;
using OricOffice.Core.Model;
using OricOffice.Core.Tape;

namespace OricOffice.Core.Tests;

public class TapeTests
{
    // The real 1984 Tansoft tapes live in the Oricutron emulator folder; tests
    // over them run when present and stay silent elsewhere.
    private static string? RealTapeDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "Oricutron_win32_v12", "tapes");
        return Directory.Exists(dir) ? dir : null;
    }

    private static byte[] BuildBasicImage(params (int Number, byte[] Text)[] lines)
    {
        // [next LE16][number LE16][text][0x00] per line, ending with a zero pointer.
        var output = new List<byte>();
        int current = 0;
        foreach (var (number, text) in lines)
        {
            int length = 4 + text.Length + 1;
            int next = current + length;
            output.Add((byte)(next & 0xFF));
            output.Add((byte)(next >> 8));
            output.Add((byte)(number & 0xFF));
            output.Add((byte)(number >> 8));
            output.AddRange(text);
            output.Add(0x00);
            current = next;
        }
        output.Add(0x00);
        output.Add(0x00); // end of program
        return output.ToArray();
    }

    // ------------------------------------------------------------ container

    [Fact]
    public void Tap_RoundTrip_PreservesFiles()
    {
        var files = new List<TapFile>
        {
            new() { Name = "LOADER", IsBasic = false, LoadAddress = 0x0281, Payload = new byte[] { 1, 2, 3, 4 } },
            new() { Name = "AUTEUR", IsBasic = false, LoadAddress = 0x0400, Payload = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray() },
        };

        var back = OricTap.Read(OricTap.Write(files));

        Assert.Equal(2, back.Count);
        Assert.Equal("LOADER", back[0].Name);
        Assert.False(back[0].IsBasic);
        Assert.Equal(0x0281, back[0].LoadAddress);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, back[0].Payload);
        Assert.Equal("AUTEUR", back[1].Name);
        Assert.Equal(300, back[1].Payload.Length);
    }

    [Fact]
    public void Tap_Read_ToleratesJunkBetweenFiles()
    {
        var one = OricTap.Write(new[]
        {
            new TapFile { Name = "X", LoadAddress = 0x0501, Payload = new byte[] { 9 } },
        });

        var back = OricTap.Read(one);
        Assert.Single(back);
        Assert.Equal("X", back[0].Name);
    }

    // ------------------------------------------------------------ detokenizer

    [Fact]
    public void Detokenize_PrintsLinesWithKeywords()
    {
        var image = BuildBasicImage(
            (10, new byte[] { 0xBA, (byte)'"', (byte)'H', (byte)'I', (byte)'"' }), // PRINT"HI"
            (20, new byte[] { 0x80 }));                                            // END

        var listing = OricBasicListing.Detokenize(image, loadAddress: 0);

        Assert.Equal("10 PRINT\"HI\"\n20 END\n", listing);
    }

    [Fact]
    public void Detokenize_HandlesRemAndAsciiNumbers()
    {
        var image = BuildBasicImage(
            (5, new byte[] { 0x9D, (byte)'O', (byte)'L', (byte)'D' }),             // REM OLD
            (100, new byte[] { 0x97, (byte)'2', (byte)'0', (byte)'0' }));          // GOTO200

        var listing = OricBasicListing.Detokenize(image, loadAddress: 0);

        Assert.Contains("5 REM OLD", listing);
        Assert.Contains("100 GOTO200", listing);
    }

    // ------------------------------------------------------------ importer

    [Fact]
    public void Import_BuildsAListingDocument()
    {
        var tap = OricTap.Write(new[]
        {
            new TapFile
            {
                Name = "DEMO",
                IsBasic = true,
                LoadAddress = 0,
                Payload = BuildBasicImage((10, new byte[] { 0xBA, (byte)'1' })),
            },
        });

        var file = TapeImporter.Import(tap, "demo", OricFileKind.Author);

        Assert.Equal(OricFileKind.Author, file.Kind);
        Assert.Contains("==== DEMO [BASIC] $0000-", file.Text.Body);
        Assert.Contains("10 PRINT1", file.Text.Body);
    }

    [Fact]
    public void Import_EmptyTap_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            TapeImporter.Import(new byte[] { 1, 2, 3 }, "x", OricFileKind.Author));
    }

    // ------------------------------------------------------------ the real tapes

    [Fact]
    public void RealTapes_ContainTheTansoftPrograms()
    {
        var dir = RealTapeDir();
        if (dir is null) return; // tapes not on this machine

        var author = OricTap.Read(File.ReadAllBytes(Path.Combine(dir, "author.tap")));
        Assert.Contains(author, f => f.Name == "AUTEUR" && !f.IsBasic && f.Length > 10_000);

        var calc = OricTap.Read(File.ReadAllBytes(Path.Combine(dir, "oriccalc.tap")));
        Assert.Contains(calc, f => f.Name == "ORIC-CALC" && f.IsBasic);

        var baseTape = OricTap.Read(File.ReadAllBytes(Path.Combine(dir, "base.tap")));
        Assert.Contains(baseTape, f => f.Name == "ORICBASE" && f.IsBasic);
    }

    [Fact]
    public void RealTapes_OricBaseLoaderDetokenizesToReadableBasic()
    {
        var dir = RealTapeDir();
        if (dir is null) return;

        var files = OricTap.Read(File.ReadAllBytes(Path.Combine(dir, "base.tap")));
        var program = Assert.Single(files);
        var listing = OricBasicListing.Detokenize(program.Payload);

        // The loader's strings and structure survive detokenizing.
        Assert.Contains("Please enter a command", listing);
        Assert.Contains("TANSOFT", listing);
        Assert.Contains("HIMEM", listing);
        Assert.Contains("Please wait", listing);
        // Lines carry real numbers (the loader's main starts at line 20005).
        Assert.Contains("20005:", listing);
    }

    [Fact]
    public void RealTapes_ImportIntoAnAuthorDocument()
    {
        var dir = RealTapeDir();
        if (dir is null) return;

        var file = TapeImporter.ImportFile(Path.Combine(dir, "author.tap"), OricFileKind.Author);

        Assert.Contains("AUTEUR", file.Text.Body);
        Assert.Contains("TANSOFT", file.Text.Body);
        Assert.Contains("John Dawson", file.Text.Body);
        Assert.False(string.IsNullOrWhiteSpace(file.Text.Body));
    }
}
