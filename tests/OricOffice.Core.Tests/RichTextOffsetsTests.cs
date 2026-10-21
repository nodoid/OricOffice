using OricOffice.Core.Text;

namespace OricOffice.Core.Tests;

public class RichTextOffsetsTests
{
    [Fact]
    public void SingleLine_OffsetsMatch()
    {
        const string body = "hello world";
        Assert.Equal(0, RichTextOffsets.ToDocument(body, 0));
        Assert.Equal(5, RichTextOffsets.ToDocument(body, 5));
        Assert.Equal(5, RichTextOffsets.ToBody(body, 5));
    }

    [Fact]
    public void EachNewline_ShiftsDocumentByOne()
    {
        const string body = "ab\ncd\nef";
        // body: a0 b1 \n2 c3 d4 \n5 e6 f7  (length 8)
        // doc:  a0 b1 \r2 \n3 c4 d5 \r6 \n7 e8 f9  (length 10)
        Assert.Equal(0, RichTextOffsets.ToDocument(body, 0));
        Assert.Equal(2, RichTextOffsets.ToDocument(body, 2));  // the \r of the first CRLF
        Assert.Equal(4, RichTextOffsets.ToDocument(body, 3));  // after first \n
        Assert.Equal(10, RichTextOffsets.ToDocument(body, 8)); // end
    }

    [Fact]
    public void RoundTrips_EveryPosition()
    {
        const string body = "one\ntwo\n\nthree";
        for (int i = 0; i <= body.Length; i++)
        {
            int doc = RichTextOffsets.ToDocument(body, i);
            Assert.Equal(i, RichTextOffsets.ToBody(body, doc));
        }
    }

    [Fact]
    public void OffsetInsideCrLf_SnapsConsistently()
    {
        const string body = "a\nb";
        // doc: a0 \r1 \n2 b3 — the greedy walk lands at the body newline (1)
        // before the \r and just past it (2) on the \n itself.
        Assert.Equal(1, RichTextOffsets.ToBody(body, 1));
        Assert.Equal(2, RichTextOffsets.ToBody(body, 2));
    }

    [Fact]
    public void BeyondEnd_Clamps()
    {
        const string body = "xy";
        Assert.Equal(2, RichTextOffsets.ToDocument(body, 99));
        Assert.Equal(2, RichTextOffsets.ToBody(body, 99));
        Assert.Equal(0, RichTextOffsets.ToBody(body, -5));
    }
}
