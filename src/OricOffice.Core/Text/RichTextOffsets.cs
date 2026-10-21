namespace OricOffice.Core.Text;

/// <summary>
/// Maps caret/selection offsets between the document body (plain text with '\n'
/// newlines, as the view models and storage use it) and a rich-edit control that
/// stores newlines as the two-character sequence "\r\n".
/// </summary>
public static class RichTextOffsets
{
    /// <summary>Body offset → control document offset (each '\n' costs one extra char).</summary>
    public static int ToDocument(string body, int bodyOffset)
    {
        int limit = Math.Clamp(bodyOffset, 0, body.Length);
        int doc = limit;
        for (int i = 0; i < limit; i++)
            if (body[i] == '\n')
                doc++;
        return doc;
    }

    /// <summary>Control document offset → body offset (snaps to the body's single '\n').</summary>
    public static int ToBody(string body, int documentOffset)
    {
        int doc = 0;
        int i = 0;
        while (i < body.Length && doc < documentOffset)
        {
            doc += body[i] == '\n' ? 2 : 1;
            i++;
        }
        return i;
    }
}
