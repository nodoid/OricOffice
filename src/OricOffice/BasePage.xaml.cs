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

        // Home → Back to Start and File → Exit both return to the start view;
        // the Home menu group itself lives in the view model so it survives
        // culture changes.
        viewModel.ReturnToStart = () => Navigation.PopToRootAsync();

        BindingContext = viewModel;

        if (SuiteMenuBar.UsesNativeMenuBar)
        {
            CustomMenuBar.IsVisible = false;
            BindNativeMenus(viewModel.Menus);
            OricOffice.Core.Localization.Loc.Instance.CultureChanged +=
                () => BindNativeMenus(viewModel.Menus);
        }
    }

    /// <summary>Rebuilds the native menu bar after a culture change.</summary>
    private void BindNativeMenus(IEnumerable<OricOffice.Core.Ui.MenuGroup> menus)
    {
        MenuBarItems.Clear();
        SuiteMenuBar.Bind(this, menus);
    }

    private void OnQueryCompleted(object? sender, EventArgs e)
        => _viewModel.ApplyQueryCommand.Execute(null);
}
