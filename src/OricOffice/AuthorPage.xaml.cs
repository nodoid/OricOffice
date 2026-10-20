using OricOffice.Core.Ui;
using OricOffice.Core.ViewModels;
using OricOffice.MauiShared.Controls;

namespace OricOffice;

public partial class AuthorPage : ContentPage
{
    public AuthorPage(AuthorViewModel viewModel)
    {
        InitializeComponent();

        // A way back to the start center (the File menu also stays native).
        viewModel.Menus.Insert(0, new MenuGroup("Home",
            new MenuItemModel("Back to Start", action: () => _ = Navigation.PopToRootAsync())));

        BindingContext = viewModel;

        // Desktop platforms get the platform's native menu bar;
        // tablets keep the tap-to-open menu row from the XAML.
        if (SuiteMenuBar.UsesNativeMenuBar)
        {
            CustomMenuBar.IsVisible = false;
            SuiteMenuBar.Bind(this, viewModel.Menus);
        }
    }
}
