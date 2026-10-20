using Microsoft.Maui.Graphics;

namespace OricOffice.MauiShared.Theme;

/// <summary>
/// Neutral, LibreOffice-inspired chrome palette shared by all three apps.
/// App identity comes from the icons/splash and the per-app accent in Colors.xaml.
/// </summary>
public static class SuiteTheme
{
    public static readonly Color Chrome = Color.FromArgb("#F3F3F3");
    public static readonly Color ChromeBorder = Color.FromArgb("#DDDDDD");
    public static readonly Color Surface = Colors.White;
    public static readonly Color Canvas = Color.FromArgb("#E8E8E8");
    public static readonly Color Text = Color.FromArgb("#1F1F1F");
    public static readonly Color TextSecondary = Color.FromArgb("#5F5F5F");
    public static readonly Color Hover = Color.FromArgb("#E4E4E4");
    public static readonly Color Pressed = Color.FromArgb("#D3D3D3");
    public static readonly Color GridLine = Color.FromArgb("#D6D6D6");
    public static readonly Color HeaderBackground = Color.FromArgb("#F6F6F6");
    public static readonly Color Accent = Color.FromArgb("#2E5EAA");
    public static readonly Color SelectedRow = Color.FromArgb("#E4EEFB");
    public static readonly Color IconStroke = Color.FromArgb("#3A3A3A");
}
