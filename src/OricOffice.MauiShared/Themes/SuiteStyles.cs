using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace OricOffice.MauiShared.Themes;

/// <summary>
/// Shared, LibreOffice-inspired chrome styles, built in C# so the class library
/// needs no XAML compilation. Merged into each app's resources at startup.
/// </summary>
public sealed class SuiteStyles : ResourceDictionary
{
    public SuiteStyles()
    {
        // ---------------------------------------------------------------- colors
        Add("Suite.Chrome", Color.FromArgb("#F3F3F3"));
        Add("Suite.ChromeBorder", Color.FromArgb("#DDDDDD"));
        Add("Suite.Canvas", Color.FromArgb("#E8E8E8"));
        Add("Suite.Surface", Colors.White);
        Add("Suite.Text", Color.FromArgb("#1F1F1F"));
        Add("Suite.TextSecondary", Color.FromArgb("#5F5F5F"));
        Add("Suite.GridLine", Color.FromArgb("#D6D6D6"));
        Add("Suite.HeaderBg", Color.FromArgb("#F6F6F6"));
        Add("Suite.Accent", Color.FromArgb("#2E5EAA"));
        Add("Suite.SelectedRow", Color.FromArgb("#E4EEFB"));

        // ---------------------------------------------------------------- brushes
        Add("Suite.ChromeBrush", Brush(Color.FromArgb("#F3F3F3")));
        Add("Suite.ChromeBorderBrush", Brush(Color.FromArgb("#DDDDDD")));
        Add("Suite.CanvasBrush", Brush(Color.FromArgb("#E8E8E8")));
        Add("Suite.SurfaceBrush", Brush(Colors.White));
        Add("Suite.TextBrush", Brush(Color.FromArgb("#1F1F1F")));
        Add("Suite.TextSecondaryBrush", Brush(Color.FromArgb("#5F5F5F")));
        Add("Suite.GridLineBrush", Brush(Color.FromArgb("#D6D6D6")));
        Add("Suite.HeaderBgBrush", Brush(Color.FromArgb("#F6F6F6")));
        Add("Suite.AccentBrush", Brush(Color.FromArgb("#2E5EAA")));
        Add("Suite.SelectedRowBrush", Brush(Color.FromArgb("#E4EEFB")));

        // ---------------------------------------------------------------- styles
        Add("Suite.StatusBar", StatusBarStyle());
        Add("Suite.Hint", HintStyle());
        Add("Suite.SectionTitle", SectionTitleStyle());
        Add("Suite.FieldLabel", FieldLabelStyle());
        Add("Suite.SmallButton", SmallButtonStyle());
        Add("Suite.BarEntry", BarEntryStyle());
    }

    private static SolidColorBrush Brush(Color color) => new(color);

    private static Style StatusBarStyle()
    {
        var style = new Style(typeof(Label));
        style.Setters.Add(new Setter { Property = Label.FontFamilyProperty, Value = "OpenSansRegular" });
        style.Setters.Add(new Setter { Property = Label.FontSizeProperty, Value = 12d });
        style.Setters.Add(new Setter { Property = Label.TextColorProperty, Value = Color.FromArgb("#5F5F5F") });
        style.Setters.Add(new Setter { Property = Label.BackgroundColorProperty, Value = Color.FromArgb("#F3F3F3") });
        style.Setters.Add(new Setter { Property = Label.PaddingProperty, Value = new Thickness(12, 6, 12, 5) });
        return style;
    }

    private static Style HintStyle()
    {
        var style = new Style(typeof(Label));
        style.Setters.Add(new Setter { Property = Label.FontFamilyProperty, Value = "OpenSansRegular" });
        style.Setters.Add(new Setter { Property = Label.FontSizeProperty, Value = 13d });
        style.Setters.Add(new Setter { Property = Label.TextColorProperty, Value = Color.FromArgb("#5F5F5F") });
        style.Setters.Add(new Setter { Property = Label.LineBreakModeProperty, Value = LineBreakMode.WordWrap });
        return style;
    }

    private static Style SectionTitleStyle()
    {
        var style = new Style(typeof(Label));
        style.Setters.Add(new Setter { Property = Label.FontFamilyProperty, Value = "OpenSansSemibold" });
        style.Setters.Add(new Setter { Property = Label.FontSizeProperty, Value = 14d });
        style.Setters.Add(new Setter { Property = Label.TextColorProperty, Value = Color.FromArgb("#1F1F1F") });
        return style;
    }

    private static Style FieldLabelStyle()
    {
        var style = new Style(typeof(Label));
        style.Setters.Add(new Setter { Property = Label.FontFamilyProperty, Value = "OpenSansSemibold" });
        style.Setters.Add(new Setter { Property = Label.FontSizeProperty, Value = 12d });
        style.Setters.Add(new Setter { Property = Label.TextColorProperty, Value = Color.FromArgb("#5F5F5F") });
        style.Setters.Add(new Setter { Property = Label.MarginProperty, Value = new Thickness(0, 0, 0, 3) });
        return style;
    }

    private static Style SmallButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter { Property = Button.FontFamilyProperty, Value = "OpenSansRegular" });
        style.Setters.Add(new Setter { Property = Button.FontSizeProperty, Value = 13d });
        style.Setters.Add(new Setter { Property = Button.TextColorProperty, Value = Color.FromArgb("#1F1F1F") });
        style.Setters.Add(new Setter { Property = Button.BackgroundColorProperty, Value = Color.FromArgb("#ECECEC") });
        style.Setters.Add(new Setter { Property = Button.BorderColorProperty, Value = Color.FromArgb("#D6D6D6") });
        style.Setters.Add(new Setter { Property = Button.BorderWidthProperty, Value = 1d });
        style.Setters.Add(new Setter { Property = Button.CornerRadiusProperty, Value = 4 });
        style.Setters.Add(new Setter { Property = Button.PaddingProperty, Value = new Thickness(12, 5) });
        style.Setters.Add(new Setter { Property = Button.HeightRequestProperty, Value = 30d });
        style.Setters.Add(new Setter { Property = Button.MinimumHeightRequestProperty, Value = 28d });
        style.Setters.Add(new Setter { Property = Button.MinimumWidthRequestProperty, Value = 0d });
        return style;
    }

    private static Style BarEntryStyle()
    {
        var style = new Style(typeof(Entry));
        style.Setters.Add(new Setter { Property = Entry.FontFamilyProperty, Value = "OpenSansRegular" });
        style.Setters.Add(new Setter { Property = Entry.FontSizeProperty, Value = 13d });
        style.Setters.Add(new Setter { Property = Entry.TextColorProperty, Value = Color.FromArgb("#1F1F1F") });
        style.Setters.Add(new Setter { Property = Entry.BackgroundColorProperty, Value = Colors.White });
        style.Setters.Add(new Setter { Property = Entry.HeightRequestProperty, Value = 30d });
        style.Setters.Add(new Setter { Property = Entry.MinimumHeightRequestProperty, Value = 26d });
        style.Setters.Add(new Setter { Property = Entry.PlaceholderColorProperty, Value = Color.FromArgb("#9A9A9A") });
        return style;
    }
}
