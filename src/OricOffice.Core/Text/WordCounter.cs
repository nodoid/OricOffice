namespace OricOffice.Core.Text;

public readonly record struct TextStats(
    int Words,
    int Characters,
    int CharactersNoSpaces,
    int Lines,
    int Paragraphs);

/// <summary>Word counting for the Author status bar (word wrap and word count were the
/// original Oric Author's headline features).
///
/// Lines = visual lines (newline count + 1, like a text editor).
/// Paragraphs = blocks of consecutive non-empty lines (separated by blank lines).
/// </summary>
public static class WordCounter
{
    public static TextStats Count(string? text)
    {
        text ??= string.Empty;

        if (text.Length == 0)
            return new TextStats(0, 0, 0, 0, 0);

        int words = 0;
        bool inWord = false;
        int charsNoSpaces = 0;

        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c)) charsNoSpaces++;

            if (IsWordChar(c))
            {
                if (!inWord)
                {
                    // Apostrophes and hyphens continue words but never start one,
                    // so "don't" and "a-b" count once while "— x" starts fresh.
                    if (c is '\'' or '-' or '’')
                        continue;
                    inWord = true;
                    words++;
                }
            }
            else
            {
                inWord = false;
            }
        }

        int lines = 1;
        foreach (var c in text)
            if (c == '\n') lines++;

        int paragraphs = 0;
        bool inBlock = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Trim().Length > 0)
            {
                if (!inBlock)
                {
                    paragraphs++;
                    inBlock = true;
                }
            }
            else
            {
                inBlock = false;
            }
        }

        return new TextStats(words, text.Length, charsNoSpaces, lines, paragraphs);
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '\'' || c == '-' || c == '’' || c == '_';
}
