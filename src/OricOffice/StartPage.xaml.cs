using OricOffice.Core.Ui;
using OricOffice.Core.Services;
using OricOffice.MauiShared.Controls;

namespace OricOffice;

public partial class StartPage : ContentPage
{
    private readonly IServiceProvider _services;

    public StartPage(
        IServiceProvider services,
        IDialogService dialogs,
        IAppLifetimeService lifetime)
    {
        _services = services;

        Menus = new List<MenuGroup>
        {
            new("File",
                new MenuItemModel("New Document", action: () => Open<AuthorPage>()),
                new MenuItemModel("New Spreadsheet", action: () => Open<CalcPage>()),
                new MenuItemModel("New Database", action: () => Open<BasePage>()),
                new MenuItemModel(isSeparator: true),
                new MenuItemModel("Exit", action: lifetime.Exit)),
            new("Help",
                new MenuItemModel("About Oric Office", action: () => _ = dialogs.MessageAsync(
                    "About Oric Office",
                    "Oric Office 1.0\n\nAuthor, Calc and Base — an office suite\nfor the modern era.\n\nLicensed under DILLIGAF.\n© Paul F. Johnson."))),
        };

        InitializeComponent();
        BindingContext = this;

        if (SuiteMenuBar.UsesNativeMenuBar)
        {
            CustomMenuBar.IsVisible = false;
            SuiteMenuBar.Bind(this, Menus);
        }
    }

    public IReadOnlyList<MenuGroup> Menus { get; }

    private void Open<TPage>() where TPage : Page
        => _ = Navigation.PushAsync(_services.GetRequiredService<TPage>());

    private void OnOpenAuthor(object? sender, TappedEventArgs e) => Open<AuthorPage>();

    private void OnOpenCalc(object? sender, TappedEventArgs e) => Open<CalcPage>();

    private void OnOpenBase(object? sender, TappedEventArgs e) => Open<BasePage>();
}
