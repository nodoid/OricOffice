using OricOffice.Core.Ui;
using OricOffice.Core.ViewModels;
using OricOffice.MauiShared.Controls;

namespace OricOffice;

public partial class BasePage : ContentPage
{
    private readonly BaseViewModel _viewModel;

    public BasePage(BaseViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;

        // A way back to the start center.
        viewModel.Menus.Insert(0, new MenuGroup("Home",
            new MenuItemModel("Back to Start", action: () => _ = Navigation.PopToRootAsync())));

        BindingContext = viewModel;

        if (SuiteMenuBar.UsesNativeMenuBar)
        {
            CustomMenuBar.IsVisible = false;
            SuiteMenuBar.Bind(this, viewModel.Menus);
        }
    }

    private void OnQueryCompleted(object? sender, EventArgs e)
        => _viewModel.ApplyQueryCommand.Execute(null);
}
