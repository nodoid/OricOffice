using System.ComponentModel;
using System.Globalization;

namespace OricOffice.Core.Localization;

/// <summary>
/// Application localisation. Keys resolve against the current culture's table
/// with an automatic English fallback; unknown keys return the key itself so
/// nothing ever renders blank. The instance raises indexer change notifications
/// (XAML binds <c>{Binding [Key], Source={x:Static Loc.Instance}}</c>) plus a
/// <see cref="CultureChanged"/> signal for code-built chrome.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>Cultures the app ships translations for (plus English).</summary>
    public static readonly IReadOnlyList<string> SupportedCultures = new[] { "en", "fr", "it", "de" };

    // NOTE: field initializers run in textual order — SupportedCultures must be
    // declared before Instance, whose constructor reads it.
    public static readonly Loc Instance = new();

    private string _culture;

    private Loc()
    {
        // Start in the system language when we have translations for it.
        var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        _culture = SupportedCultures.Contains(system) ? system : "en";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the culture changes (rebuild code-built chrome).</summary>
    public event Action? CultureChanged;

    public string Culture
    {
        get => _culture;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "en" : value.ToLowerInvariant();
            if (next == _culture) return;
            _culture = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
            CultureChanged?.Invoke();
        }
    }

    public string Current => _culture;

    /// <summary>XAML-friendly indexer: <c>{Binding [Menu.File], Source={x:Static …}}</c>.</summary>
    public string this[string key] => T(key);

    /// <summary>Translates a key (English fallback, then the key itself).</summary>
    public static string T(string key)
    {
        var table = Instance._culture == "en" ? null : Translations.For(Instance._culture);
        if (table is not null && table.TryGetValue(key, out var text)) return text;
        if (Translations.En.TryGetValue(key, out var english)) return english;
        return key;
    }

    /// <summary>Translates and formats: <c>Loc.Format("Status.Opened", name)</c>.</summary>
    public static string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, T(key), args);
}
