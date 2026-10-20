using OricOffice.Core.ViewModels;

namespace OricOffice.Core.Tests;

public class TextSelectionOpsTests
{
    [Fact]
    public void GetSelection_ReturnsSelectedRange()
        => Assert.Equal("world", TextSelectionOps.GetSelection("Hello world", 6, 5));

    [Fact]
    public void GetSelection_NoSelection_ReturnsEmpty()
        => Assert.Equal(string.Empty, TextSelectionOps.GetSelection("Hello", 2, 0));

    [Fact]
    public void GetSelection_ClampsOutOfRangeValues()
        => Assert.Equal("lo", TextSelectionOps.GetSelection("Hello", 3, 99));

    [Fact]
    public void ReplaceSelection_DeletesSelection()
    {
        var (text, cursor) = TextSelectionOps.ReplaceSelection("Hello world", 6, 5, string.Empty);
        Assert.Equal("Hello ", text);
        Assert.Equal(6, cursor);
    }

    [Fact]
    public void ReplaceSelection_InsertsAtCaret()
    {
        var (text, cursor) = TextSelectionOps.ReplaceSelection("Hello", 5, 0, " there");
        Assert.Equal("Hello there", text);
        Assert.Equal(11, cursor);
    }

    [Fact]
    public void ReplaceSelection_ReplacesSelection()
    {
        var (text, cursor) = TextSelectionOps.ReplaceSelection("Hello world", 6, 5, "earth");
        Assert.Equal("Hello earth", text);
        Assert.Equal(11, cursor);
    }

    [Fact]
    public void SelectAll_ReturnsWholeRange()
    {
        var (cursor, length) = TextSelectionOps.SelectAll("abc");
        Assert.Equal(0, cursor);
        Assert.Equal(3, length);
    }

    [Fact]
    public void HasSelection_DistinguishesCaretFromSelection()
    {
        Assert.False(TextSelectionOps.HasSelection("abc", 1, 0));
        Assert.True(TextSelectionOps.HasSelection("abc", 1, 1));
    }
}
