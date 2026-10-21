using OricOffice.Core.Model;

namespace OricOffice.Core.Tests;

/// <summary>
/// The rich-text run model: styled ranges, boundary splits, offset repair across
/// caret edits/deletions/pastes, and the pending style for typed text.
/// </summary>
public class TextRunsTests
{
    private static readonly TextStyle Bold = new() { Flags = TextStyleFlags.Bold };
    private static readonly TextStyle Italic = new() { Flags = TextStyleFlags.Italic };

    private static TextDocument Document(string body)
    {
        var doc = new TextDocument { Body = body };
        return doc;
    }

    // ------------------------------------------------------------ apply + split

    [Fact]
    public void ApplyStyle_MiddleRange_SplitsSurroundingRuns()
    {
        var doc = Document("abcdefghij");
        doc.ApplyStyle(0, 10, _ => Bold);
        Assert.Single(doc.Runs);

        doc.ApplyStyle(3, 4, _ => Italic);

        Assert.Equal(3, doc.Runs.Count);
        Assert.Equal((0, 3, TextStyleFlags.Bold), (doc.Runs[0].Start, doc.Runs[0].Length, doc.Runs[0].Style.Flags));
        Assert.Equal(TextStyleFlags.Italic, doc.Runs[1].Style.Flags);
        Assert.Equal(3, doc.Runs[1].Start);
        Assert.Equal(4, doc.Runs[1].Length);
        Assert.Equal(TextStyleFlags.Bold, doc.Runs[2].Style.Flags);
        Assert.Equal(7, doc.Runs[2].Start);
    }

    [Fact]
    public void ApplyStyle_MutateOnlyTouchesOverlappingRuns()
    {
        var doc = Document("hello world");
        doc.ApplyStyle(0, 5, _ => Bold);    // "hello"
        doc.ApplyStyle(6, 5, _ => Italic);  // "world"

        // Remove bold from the first six characters: only the first run changes.
        doc.ApplyStyle(0, 6, s => new TextStyle
        {
            FontFamily = s.FontFamily,
            FontSize = s.FontSize,
            Foreground = s.Foreground,
            Background = s.Background,
            Flags = s.Flags & ~TextStyleFlags.Bold,
        });

        Assert.Equal(0, doc.Runs.Count(r => r.Style.Flags.HasFlag(TextStyleFlags.Bold)));
        Assert.Equal(TextStyleFlags.Italic, doc.Runs.Single().Style.Flags);
    }

    [Fact]
    public void AdjacentIdenticalRuns_AreMerged()
    {
        var doc = Document("abcdefgh");
        doc.ApplyStyle(0, 4, _ => Bold);
        doc.ApplyStyle(4, 4, _ => Bold);

        var run = Assert.Single(doc.Runs);
        Assert.Equal((0, 8), (run.Start, run.Length));
    }

    [Fact]
    public void StyleBeyondBody_IsClamped()
    {
        var doc = Document("abc");
        doc.ApplyStyle(1, 99, _ => Bold);
        var run = Assert.Single(doc.Runs);
        Assert.Equal((1, 2), (run.Start, run.Length));
    }

    // ------------------------------------------------------------ offset repair

    [Fact]
    public void TypingInsideAStyledRun_GrowsItAndKeepsStyle()
    {
        var doc = Document("hello world");
        doc.ApplyStyle(0, 5, _ => Bold); // "hello"

        doc.Body = "hellXo world"; // one character replaced by two, inside the run

        var run = Assert.Single(doc.Runs);
        Assert.Equal("hellXo", run.BodySlice(doc.Body));
        Assert.Equal(TextStyleFlags.Bold, run.Style.Flags);
    }

    [Fact]
    public void TypingBeforeARun_UsesTheFollowingRunStyle()
    {
        var doc = Document("world");
        doc.ApplyStyle(0, 5, _ => Bold);

        doc.Body = "say world";

        // Inserted text inherits the style at the insertion point (the following
        // character), so "say " joins the bold run.
        var run = Assert.Single(doc.Runs);
        Assert.Equal((0, 9), (run.Start, run.Length));
        Assert.Equal("say world", doc.Body[run.Start..run.End]);
    }

    [Fact]
    public void DeletingInsideAStyledRun_ShrinksIt()
    {
        var doc = Document("hello brave world");
        doc.ApplyStyle(0, 12, _ => Bold); // "hello brave"

        doc.Body = "hello world"; // deleted " brave"

        var run = Assert.Single(doc.Runs);
        Assert.Equal((0, 6), (run.Start, run.Length)); // "hello " kept, gap at "world"
    }

    [Fact]
    public void DeletingAWholeRun_DropsIt()
    {
        var doc = Document("one two");
        doc.ApplyStyle(4, 3, _ => Bold); // "two"

        doc.Body = "one "; // run fully inside the deletion
        Assert.Empty(doc.Runs);
    }

    [Fact]
    public void PastingOverARunBoundary_KeepsBothSides()
    {
        var doc = Document("aaaa bbbb");
        doc.ApplyStyle(5, 4, _ => Bold); // "bbbb"

        doc.Body = "aaaa XY bbbb"; // replaced space with " XY"

        // Pasted text inherits the style at the insertion point, so it joins the run.
        var run = Assert.Single(doc.Runs);
        Assert.Equal("XY bbbb", doc.Body[run.Start..run.End]);
        Assert.Equal(5, run.Start);
    }

    [Fact]
    public void DeletingTextBeforeARun_KeepsItsRelativePosition()
    {
        var doc = Document("prefix styled");
        doc.ApplyStyle(7, 6, _ => Italic); // "styled"

        doc.Body = "styled";

        var run = Assert.Single(doc.Runs);
        Assert.Equal((0, 6), (run.Start, run.Length));
        Assert.Equal("styled", doc.Body[run.Start..run.End]);
    }

    // ------------------------------------------------------------ pending style

    [Fact]
    public void PendingStyle_TypesNewTextWithThatStyle()
    {
        var doc = Document("start");
        doc.PendingStyle = Bold;

        doc.Body = "start"; // no change yet
        doc.Body = "start!"; // typed '!' at the end

        var run = Assert.Single(doc.Runs);
        Assert.Equal(5, run.Start);
        Assert.Equal(1, run.Length);
        Assert.Equal(TextStyleFlags.Bold, run.Style.Flags);
    }

    [Fact]
    public void PendingStyle_AppliesToEachNewInsertion()
    {
        var doc = Document("");
        doc.PendingStyle = Italic;
        doc.Body = "a";
        doc.Body = "ab";

        Assert.Equal(2, doc.Runs.Sum(r => r.Length)); // both characters styled
        Assert.All(doc.Runs, r => Assert.Equal(TextStyleFlags.Italic, r.Style.Flags));
    }

    // ------------------------------------------------------------ misc

    [Fact]
    public void IsEmpty_ThinksStyledTextIsNotEmpty()
    {
        var doc = new TextDocument { Body = "x" };
        doc.ApplyStyle(0, 1, _ => Bold);
        Assert.False(doc.IsEmpty);
    }
}

internal static class TextRunTestExtensions
{
    public static string BodySlice(this TextRun run, string body)
        => body.Substring(run.Start, Math.Min(run.Length, body.Length - run.Start));
}
