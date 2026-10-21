using OricOffice.Core.Model;

namespace OricOffice.MauiShared.RichText;

/// <summary>
/// Cross-platform rich-text editor control. The platform handler supplies the
/// native editing surface (WinUI RichEditBox on Windows); on other platforms the
/// control degrades to an empty view until a handler exists.
/// </summary>
public sealed class RichTextEditor : ContentView, IRichTextEditor
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(RichTextEditor), string.Empty,
        BindingMode.TwoWay);

    public static readonly BindableProperty PlaceholderTextProperty = BindableProperty.Create(
        nameof(PlaceholderText), typeof(string), typeof(RichTextEditor), null,
        propertyChanged: OnStylesWorthyPropertyChanged);

    public static readonly BindableProperty BaseFontSizeProperty = BindableProperty.Create(
        nameof(BaseFontSize), typeof(double), typeof(RichTextEditor), 15d,
        propertyChanged: OnStylesWorthyPropertyChanged);

    public static readonly BindableProperty StylesVersionProperty = BindableProperty.Create(
        nameof(StylesVersion), typeof(int), typeof(RichTextEditor), 0,
        propertyChanged: OnStylesWorthyPropertyChanged);

    private static void OnStylesWorthyPropertyChanged(BindableObject bindable, object oldValue, object newValue)
        => ((RichTextEditor)bindable).StylesChanged?.Invoke();

    /// <summary>Raised when the handler should re-read text styles (runs, zoom, placeholder).</summary>
    internal event Action? StylesChanged;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public double BaseFontSize
    {
        get => (double)GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    /// <summary>Bump after changing <see cref="Runs"/> or the pending style to push them to the view.</summary>
    public int StylesVersion
    {
        get => (int)GetValue(StylesVersionProperty);
        set => SetValue(StylesVersionProperty, value);
    }

    /// <summary>Current style runs (page code-behind sets this before bumping <see cref="StylesVersion"/>).</summary>
    public IReadOnlyList<TextRun>? Runs { get; set; }

    /// <summary>Style armed for subsequently typed text, applied to the caret/selection on push.</summary>
    public TextStyle? PendingStyle { get; set; }

    public event Action<string>? TextChangedFromView;

    public event Action<int, int>? SelectionChangedFromView;

    /// <summary>Called by the handler when the user edited the text.</summary>
    internal void RaiseTextChangedFromView(string text) => TextChangedFromView?.Invoke(text);

    /// <summary>Called by the handler when the selection moved (body-space offsets).</summary>
    internal void RaiseSelectionChangedFromView(int start, int length)
        => SelectionChangedFromView?.Invoke(start, length);
}
