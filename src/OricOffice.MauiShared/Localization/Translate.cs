using System.Globalization;
using OricOffice.Core.Localization;

namespace OricOffice.MauiShared.Localization;

/// <summary>
/// Live translation for XAML: <c>Text="{loc:Translate Menu.File}"</c>.
///
/// A plain indexer binding (<c>{Binding [Menu.File], Source=…}</c>) cannot be
/// used because MAUI's binding-path parser splits the path on every dot before
/// parsing the indexer, which rejects dotted keys at compile time (MAUIG2041).
/// This extension instead binds the plain <see cref="Loc.Current"/> property
/// and runs the key through <see cref="TranslateConverter"/>, so the text
/// re-renders automatically whenever the culture changes.
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class TranslateExtension : IMarkupExtension
{
    /// <summary>The translation key, e.g. <c>Menu.File</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public object ProvideValue(IServiceProvider serviceProvider)
        => new Binding
        {
            Path = nameof(Loc.Current),
            Source = Loc.Instance,
            Converter = TranslateConverter.Instance,
            ConverterParameter = Key,
        };
}

/// <summary>Resolves a translation key (the binding's ConverterParameter) against the current culture.</summary>
public sealed class TranslateConverter : IValueConverter
{
    public static readonly TranslateConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => parameter is string key ? Loc.T(key) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
