using OricOffice.Core.Text;

namespace OricOffice.Core.Tests;

public class WordCounterTests
{
    [Fact]
    public void EmptyText()
    {
        var stats = WordCounter.Count("");
        Assert.Equal(0, stats.Words);
        Assert.Equal(0, stats.Characters);
        Assert.Equal(0, stats.Lines);
        Assert.Equal(0, stats.Paragraphs);
    }

    [Fact]
    public void NullTextBehavesAsEmpty()
    {
        var stats = WordCounter.Count(null);
        Assert.Equal(0, stats.Words);
    }

    [Fact]
    public void SimpleSentence()
    {
        var stats = WordCounter.Count("Hello world");
        Assert.Equal(2, stats.Words);
        Assert.Equal(11, stats.Characters);
        Assert.Equal(10, stats.CharactersNoSpaces);
        Assert.Equal(1, stats.Lines);
        Assert.Equal(1, stats.Paragraphs);
    }

    [Fact]
    public void ContractionsCountAsOneWord()
    {
        var stats = WordCounter.Count("don't stop believing");
        Assert.Equal(3, stats.Words);
    }

    [Fact]
    public void PunctuationSeparatesWords()
    {
        var stats = WordCounter.Count("one, two; three!");
        Assert.Equal(3, stats.Words);
    }

    [Fact]
    public void MultipleLinesAndParagraphs()
    {
        var stats = WordCounter.Count("line one\nline two\n\nsecond para");
        Assert.Equal(6, stats.Words);
        Assert.Equal(4, stats.Lines);
        Assert.Equal(2, stats.Paragraphs);
    }

    [Fact]
    public void HyphenatedWordCountsOnce()
    {
        var stats = WordCounter.Count("well-known fact");
        Assert.Equal(2, stats.Words);
    }

    [Fact]
    public void DigitsCountAsWords()
    {
        var stats = WordCounter.Count("42 is the answer");
        Assert.Equal(4, stats.Words);
    }
}
