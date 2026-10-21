using OricOffice.Core.Export;
using OricOffice.Core.Model;
using OricOffice.Core.Tape;

namespace OricOffice.Core.Tests;

/// <summary>
/// AUTHOR document images. The committed fixture tests/fixtures/author-esctest.bin
/// is emulator-produced: it was dumped from the memory of an Oricutron session
/// driving the real Tansoft AUTHOR v2.01, after typing a document that contains
/// every printer control code ([Control]C I/J/M/L/[), a text marker, an
/// auto-insert marker and eight dot commands, then Store-ing it. AUTHOR keeps
/// the file as plain ASCII — a 256-byte printer installation block, the
/// space-padded document name, then the NUL-terminated text with control
/// characters stored as $C0 | code (the manual, pp. 28 and 32).
/// </summary>
public class OricAuthorTextTests
{
    /// <summary>Locates tests/fixtures by walking up from the test assembly.</summary>
    private static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent!;
        }
        throw new FileNotFoundException($"Fixture '{name}' not found under tests/fixtures.");
    }

    private static byte[] FixtureBytes(string name) => File.ReadAllBytes(Fixture(name));

    /// <summary>The exact text of the fixture once AUTHOR's codes are mapped.</summary>
    private const string ExpectedBody =
        "esctest file\n" +
        "\t tab after control i\n" +
        "\n\n after two linefeeds\n" +
        "before control m\n after it\n" +
        "\f form feed here\n" +
        "esc char:\u001B tail\n" +
        "· marker next\n" +
        "> autoslot\n" +
        "· c centred line\n" +
        "· i 5 indent five\n" +
        "pg· # end\n" +
        "· n 2 set page two\n" +
        "· k 2 two copies\n" +
        "· * count mark\n" +
        "· f ff toggle\n" +
        "· s sheet toggle\n";

    [Fact]
    public void Fixture_Is_Detected_As_Author_Document()
        => Assert.True(OricAuthorText.IsAuthorDocument(FixtureBytes("author-esctest.bin")));

    [Fact]
    public void Parse_Maps_Every_Control_Code()
    {
        var doc = OricAuthorText.Parse(FixtureBytes("author-esctest.bin"));
        Assert.Equal(ExpectedBody, doc.Body);

        // Spot-check each of the five [Control]C escape codes the manual lists.
        Assert.Contains("\t tab", doc.Body);              // Control-I  $C9
        Assert.Contains("\n\n after two", doc.Body);      // Control-J  $CA (twice)
        Assert.Contains("before control m\n after it", doc.Body); // Control-M $CD
        Assert.Contains("\f form feed", doc.Body);        // Control-L  $CC
        Assert.Contains("esc char:\u001B tail", doc.Body);// Control-[  $DB

        // Markers and dot commands keep their visible forms.
        Assert.Contains("· marker next", doc.Body);       // inverse '.' text marker
        Assert.Contains("> autoslot", doc.Body);          // inverse '>' auto-insert
        Assert.Contains("· c centred line", doc.Body);    // [Control]D dot command
    }

    [Fact]
    public void Parse_Keeps_Name_And_Installation_Block()
    {
        var doc = OricAuthorText.Parse(FixtureBytes("author-esctest.bin"));
        Assert.Equal("esctest", doc.Name);
        Assert.Equal(256, doc.Install.Length);
        // The ORIC installation defaults from the manual (p. 22) survive verbatim:
        // left margin 1, right margin 39, lines/page 55, form length 60.
        Assert.Equal(1, doc.Install[0]);
        Assert.Equal(39, doc.Install[1]);
        Assert.Equal(55, doc.Install[2]);
        Assert.Equal(60, doc.Install[3]);
    }

    [Fact]
    public void Raw_Image_Imports_Through_TapeImporter()
    {
        var file = TapeImporter.Import(FixtureBytes("author-esctest.bin"), "title", OricFileKind.Author);
        Assert.Equal("esctest", file.Title);
        Assert.Equal(ExpectedBody, file.Text.Body);
    }

    [Fact]
    public void Author_Payload_Inside_Tap_Imports_As_Document()
    {
        var payload = FixtureBytes("author-esctest.bin");

        // Wrap the image in standard .tap framing: leader, sync, 9-byte
        // header, NUL-terminated name, payload of (end - start + 1) bytes.
        int start = 0x3400;
        int end = start + payload.Length - 1;
        var tap = new List<byte>();
        tap.AddRange(new byte[] { 0x16, 0x16, 0x16, 0x16, 0x24 });
        tap.AddRange(new byte[]
        {
            0x00, 0x00, 0x80, 0x00,
            (byte)(end >> 8), (byte)(end & 0xFF),
            (byte)(start >> 8), (byte)(start & 0xFF),
            0x00,
        });
        tap.AddRange(System.Text.Encoding.ASCII.GetBytes("ESCTEST"));
        tap.Add(0x00);
        tap.AddRange(payload);

        var file = TapeImporter.Import(tap.ToArray(), "title", OricFileKind.Author);
        Assert.Equal("ESCTEST", file.Title);
        Assert.Equal(ExpectedBody, file.Text.Body);
    }

    [Fact]
    public void Non_Author_Data_Is_Rejected()
    {
        Assert.False(OricAuthorText.IsAuthorDocument(null));
        Assert.False(OricAuthorText.IsAuthorDocument(Array.Empty<byte>()));
        Assert.False(OricAuthorText.IsAuthorDocument(new byte[600]));           // all NULs
        Assert.False(OricAuthorText.IsAuthorDocument(FixtureBytes("hello.tap"))); // a real .tap
        Assert.Throws<InvalidDataException>(
            () => OricAuthorText.Parse(FixtureBytes("hello.tap")));
    }
}
