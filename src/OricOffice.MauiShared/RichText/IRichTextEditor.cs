using OricOffice.Core.Model;

namespace OricOffice.MauiShared.RichText;

/// <summary>
/// A rich-text editor surface: the shared control exposes plain-text semantics
/// (Body '\n' newlines) plus style runs, and the platform handler (a WinUI
/// RichEditBox on Windows) renders them and reports user edits back.
/// </summary>
public interface IRichTextEditor : IView
{
    /// <summary>Document text with '\n' newlines, two-way bound to the view model.</summary>
    string Text { get; set; }

    /// <summary>Hint shown while the document is empty.</summary>
    string? PlaceholderText { get; }

    /// <summary>Base point size for unstyled text (the zoom level).</summary>
    double BaseFontSize { get; }

    /// <summary>Style runs to render; read when <see cref="StylesVersion"/> changes.</summary>
    IReadOnlyList<TextRun>? Runs { get; }

    /// <summary>Raised by the handler after the user edits the text.</summary>
    event Action<string>? TextChangedFromView;

    /// <summary>Raised by the handler when the selection moves (body-space offsets).</summary>
    event Action<int, int>? SelectionChangedFromView;
}
