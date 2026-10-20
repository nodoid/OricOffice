namespace OricOffice.Core.ViewModels;

/// <summary>
/// Pure helpers for editing the selection of a text body from the view model
/// (MAUI's Editor exposes CursorPosition/SelectionLength but not the selected text).
/// </summary>
public static class TextSelectionOps
{
    public static (int Start, int Length) Clamp(string text, int position, int length)
    {
        text ??= string.Empty;
        position = Math.Clamp(position, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - position);
        return (position, length);
    }

    /// <summary>The currently selected text (empty when there is no selection).</summary>
    public static string GetSelection(string text, int position, int length)
    {
        text ??= string.Empty;
        var (start, len) = Clamp(text, position, length);
        return len == 0 ? string.Empty : text.Substring(start, len);
    }

    /// <summary>Replaces the selection (or inserts at the caret) and returns the caret result.</summary>
    public static (string Text, int Cursor) ReplaceSelection(string text, int position, int length, string replacement)
    {
        text ??= string.Empty;
        replacement ??= string.Empty;
        var (start, len) = Clamp(text, position, length);
        var newText = text[..start] + replacement + text[(start + len)..];
        return (newText, start + replacement.Length);
    }

    /// <summary>Selects the entire text.</summary>
    public static (int Cursor, int Length) SelectAll(string text)
    {
        text ??= string.Empty;
        return (0, text.Length);
    }

    /// <summary>The selection, or, with no selection, nothing (used to decide copy/cut viability).</summary>
    public static bool HasSelection(string text, int position, int length)
        => Clamp(text, position, length).Length > 0;
}
