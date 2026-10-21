namespace OricOffice.Core.Model;

/// <summary>
/// Author section: plain text with word wrap handled by the view, plus the
/// document's embedded images and tables and character-level style runs over the body.
///
/// Runs are rebuilt from a per-character style array on every edit, which keeps them
/// exactly aligned with the text across caret edits, deletions and pastes: unchanged
/// characters keep their style, inserted characters inherit the style at the insertion
/// point (the following character, or the last character when typing at the end of the
/// document), and a set <see cref="PendingStyle"/> overrides that for newly typed text.
/// </summary>
public sealed class TextDocument
{
    private static readonly TextStyle DefaultStyle = new();

    private string _body = string.Empty;

    public string Body
    {
        get => _body;
        set
        {
            var newValue = value ?? string.Empty;
            if (newValue == _body) return;

            var oldText = _body;
            _body = newValue;

            // Nothing to carry over and no pending style: plain stays plain.
            if (Runs.Count == 0 && PendingStyle is null) return;

            RebuildRuns(SpliceStyles(oldText, newValue));
        }
    }

    /// <summary>Style applied to subsequently typed text; session-only (not persisted).</summary>
    public TextStyle? PendingStyle { get; set; }

    /// <summary>Styled slices of <see cref="Body"/>, sorted and non-overlapping.</summary>
    public List<TextRun> Runs { get; } = new();

    /// <summary>Embedded images, in document order.</summary>
    public List<DocumentImage> Images { get; } = new();

    /// <summary>Embedded tables, in document order.</summary>
    public List<TableBlock> Tables { get; } = new();

    public bool IsEmpty
        => string.IsNullOrWhiteSpace(Body) && Images.Count == 0 && Tables.Count == 0;

    // ------------------------------------------------------------ style editing

    /// <summary>
    /// Applies <paramref name="mutate"/> to every character in the range
    /// (receiving the character's current style, defaulting to <c>new TextStyle()</c>),
    /// then rebuilds the run list.
    /// </summary>
    public void ApplyStyle(int start, int length, Func<TextStyle, TextStyle> mutate)
    {
        if (length <= 0 || _body.Length == 0) return;

        start = Math.Clamp(start, 0, _body.Length);
        var end = Math.Clamp(start + length, start, _body.Length);
        if (end <= start) return;

        var styles = CaptureStyles(_body.Length);
        for (int i = start; i < end; i++)
            styles[i] = mutate(styles[i] ?? DefaultStyle) ?? DefaultStyle;
        RebuildRuns(styles);
    }

    /// <summary>The style of the character at <paramref name="index"/>; null when unstyled.</summary>
    public TextStyle? StyleAt(int index)
    {
        if (index < 0 || index >= _body.Length) return null;
        foreach (var run in Runs)
            if (index >= run.Start && index < run.End)
                return run.Style;
        return null;
    }

    /// <summary>Rebuilds the run list from the body: clamps, sorts and merges adjacent identical runs.</summary>
    internal void Normalize()
    {
        if (Runs.Count == 0) return;
        RebuildRuns(CaptureStyles(_body.Length));
    }

    // ------------------------------------------------------------ internals

    /// <summary>One style per character of <paramref name="body"/>, from the run list.</summary>
    private TextStyle?[] CaptureStyles(int length)
    {
        var styles = new TextStyle?[length];
        foreach (var run in Runs)
        {
            int start = Math.Max(0, run.Start);
            int end = Math.Min(length, run.End);
            for (int i = start; i < end; i++)
                styles[i] = run.Style;
        }
        return styles;
    }

    /// <summary>
    /// Carries the old character styles across an edit from <paramref name="oldText"/> to
    /// <paramref name="newText"/> using a common-prefix/suffix diff: text around the edit
    /// keeps its style, inserted text inherits the style at the insertion point, and
    /// <see cref="PendingStyle"/> (when set) styles the inserted text.
    /// </summary>
    private TextStyle?[] SpliceStyles(string oldText, string newText)
    {
        int oldLen = oldText.Length;

        int prefix = 0;
        int max = Math.Min(oldLen, newText.Length);
        while (prefix < max && oldText[prefix] == newText[prefix]) prefix++;

        int suffix = 0;
        int maxSuffix = max - prefix;
        while (suffix < maxSuffix
               && oldText[oldLen - 1 - suffix] == newText[newText.Length - 1 - suffix])
            suffix++;

        int deleted = oldLen - prefix - suffix;
        int inserted = newText.Length - prefix - suffix;

        var old = CaptureStyles(oldLen);
        var next = new TextStyle?[newText.Length];

        for (int i = 0; i < prefix; i++)
            next[i] = old[i];
        for (int i = prefix + deleted; i < oldLen; i++)
            next[prefix + inserted + (i - prefix - deleted)] = old[i];

        // Inserted text inherits the style at the insertion point: the following
        // character, or the last character when typing at the end of the document.
        var inherit = prefix < oldLen ? old[prefix] : oldLen > 0 ? old[oldLen - 1] : null;
        for (int i = 0; i < inserted; i++)
            next[prefix + i] = inherit;

        // Typed/pasted text takes the pending style (Office-style stickiness).
        if (PendingStyle is not null && inserted > 0)
            for (int i = 0; i < inserted; i++)
                next[prefix + i] = PendingStyle;

        return next;
    }

    /// <summary>Replaces the run list with the maximal non-default style spans of <paramref name="styles"/>.</summary>
    private void RebuildRuns(TextStyle?[] styles)
    {
        Runs.Clear();
        int i = 0;
        while (i < styles.Length)
        {
            var style = styles[i];
            if (style is null || style.Equals(DefaultStyle))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < styles.Length && style.Equals(styles[i])) i++;
            Runs.Add(new TextRun(start, i - start, style));
        }
    }
}
