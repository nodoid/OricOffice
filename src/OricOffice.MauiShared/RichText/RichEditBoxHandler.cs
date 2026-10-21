#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.UI.Text;
using OricOffice.Core.Model;
using OricOffice.Core.Text;
using WinUIColor = Windows.UI.Color;
using WinUIRichEditBox = Microsoft.UI.Xaml.Controls.RichEditBox;

namespace OricOffice.MauiShared.RichText;

/// <summary>
/// Hosts a WinUI <see cref="WinUIRichEditBox"/> as the native editing surface:
/// pushes text and character-format runs down, and reports user edits and
/// selection changes back in body space ('\n' newlines).
/// </summary>
public sealed class RichEditBoxHandler : ViewHandler<RichTextEditor, WinUIRichEditBox>
{
    public static IPropertyMapper<RichTextEditor, RichEditBoxHandler> Mapper =
        new PropertyMapper<RichTextEditor, RichEditBoxHandler>(ViewHandler.ViewMapper)
        {
            [nameof(RichTextEditor.Text)] = MapText,
            [nameof(RichTextEditor.StylesVersion)] = MapStyles,
            [nameof(RichTextEditor.PlaceholderText)] = MapPlaceholder,
            [nameof(RichTextEditor.BaseFontSize)] = MapStyles,
        };

    public RichEditBoxHandler() : base(Mapper)
    {
    }

    private bool _internalChange;
    private string _lastText = string.Empty;

    protected override WinUIRichEditBox CreatePlatformView()
    {
        var box = new WinUIRichEditBox();
        box.TextChanged += (_, _) => OnNativeTextChanged();
        box.SelectionChanged += (_, _) => OnNativeSelectionChanged();
        return box;
    }

    protected override void ConnectHandler(WinUIRichEditBox nativeView)
    {
        base.ConnectHandler(nativeView);
        PushText();
        PushStyles();
        PushPlaceholder();
        // Re-read styles whenever the control signals them (runs, zoom, placeholder).
        VirtualView.StylesChanged += OnVirtualStylesChanged;
    }

    protected override void DisconnectHandler(WinUIRichEditBox nativeView)
    {
        VirtualView.StylesChanged -= OnVirtualStylesChanged;
        base.DisconnectHandler(nativeView);
    }

    private void OnVirtualStylesChanged()
    {
        PushStyles();
        PushPlaceholder();
    }

    public static void MapText(RichEditBoxHandler handler, RichTextEditor editor) => handler.PushText();

    public static void MapStyles(RichEditBoxHandler handler, RichTextEditor editor) => handler.PushStyles();

    public static void MapPlaceholder(RichEditBoxHandler handler, RichTextEditor editor)
        => handler.PushPlaceholder();

    private void PushPlaceholder()
    {
        if (VirtualView.PlaceholderText is { } hint)
            PlatformView.PlaceholderText = hint;
    }

    private void PushText()
    {
        var text = VirtualView.Text ?? string.Empty;
        if (text == _lastText) return;

        _internalChange = true;
        try
        {
            var all = PlatformView.Document.GetRange(0, -1);
            all.SetText(TextSetOptions.None, text.Replace("\n", "\r\n"));
        }
        finally
        {
            _internalChange = false;
        }

        _lastText = text;
        PushStyles(); // replacing the text resets native formatting
    }

    private void PushStyles()
    {
        if (PlatformView is null) return;

        var editor = VirtualView;
        var text = editor.Text ?? string.Empty;
        var document = PlatformView.Document;

        _internalChange = true;
        try
        {
            // Base format for unstyled text: the app font at the current zoom.
            var baseFormat = document.GetDefaultCharacterFormat();
            baseFormat.Name = "Open Sans";
            baseFormat.Size = (float)editor.BaseFontSize;
            baseFormat.ForegroundColor = AutomaticColour();
            baseFormat.BackgroundColor = AutomaticColour();
            baseFormat.Bold = FormatEffect.Off;
            baseFormat.Italic = FormatEffect.Off;
            baseFormat.Strikethrough = FormatEffect.Off;
            baseFormat.Superscript = FormatEffect.Off;
            baseFormat.Subscript = FormatEffect.Off;
            baseFormat.Underline = UnderlineType.None;
            document.GetRange(0, -1).CharacterFormat = baseFormat;

            if (editor.Runs is { } runs)
            {
                foreach (var run in runs)
                {
                    if (run.Start >= text.Length) continue;
                    int start = RichTextOffsets.ToDocument(text, run.Start);
                    int end = RichTextOffsets.ToDocument(text, Math.Min(run.End, text.Length));
                    if (end <= start) continue;
                    document.GetRange(start, end).CharacterFormat = FormatFrom(run.Style, document);
                }
            }

            // Pending style goes to the current selection (the caret types into it).
            if (editor.PendingStyle is { } pending)
                document.Selection.CharacterFormat = FormatFrom(pending, document);
        }
        finally
        {
            _internalChange = false;
        }
    }

    private void OnNativeTextChanged()
    {
        if (_internalChange || VirtualView is null) return;

        PlatformView.Document.GetText(TextGetOptions.None, out var raw);
        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        _lastText = text;
        VirtualView.RaiseTextChangedFromView(text);
    }

    private void OnNativeSelectionChanged()
    {
        if (_internalChange || VirtualView is null) return;

        var selection = PlatformView.Document.Selection;
        int start = RichTextOffsets.ToBody(_lastText, selection.StartPosition);
        int end = RichTextOffsets.ToBody(_lastText, selection.EndPosition);
        VirtualView.RaiseSelectionChangedFromView(start, Math.Max(0, end - start));
    }

    // ------------------------------------------------------------ style mapping

    private static ITextCharacterFormat FormatFrom(TextStyle style, RichEditTextDocument document)
    {
        var format = document.GetDefaultCharacterFormat();

        if (style.FontFamily is { } family)
            format.Name = family;
        if (style.FontSize is { } size)
            format.Size = (float)size;

        format.ForegroundColor = style.Foreground is { } fg ? FromHex(fg) : AutomaticColour();
        format.BackgroundColor = style.Background is { } bg ? FromHex(bg) : AutomaticColour();

        format.Bold = On(style.Flags.HasFlag(TextStyleFlags.Bold));
        format.Italic = On(style.Flags.HasFlag(TextStyleFlags.Italic));
        format.Strikethrough = On(style.Flags.HasFlag(TextStyleFlags.Strikethrough));
        format.Underline = style.Flags.HasFlag(TextStyleFlags.Underline)
            ? UnderlineType.Single
            : UnderlineType.None;

        format.Superscript = On(style.Flags.HasFlag(TextStyleFlags.Superscript));
        format.Subscript = On(style.Flags.HasFlag(TextStyleFlags.Subscript));

        return format;
    }

    private static FormatEffect On(bool value) => value ? FormatEffect.On : FormatEffect.Off;

    private static WinUIColor AutomaticColour() => WinUIColor.FromArgb(0, 0, 0, 0);

    private static WinUIColor FromHex(string hex)
    {
        var text = hex.TrimStart('#');
        if (text.Length == 6 &&
            byte.TryParse(text[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(text[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(text[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            return WinUIColor.FromArgb(255, r, g, b);
        return AutomaticColour();
    }
}
#endif
