using OricOffice.Core.Ui;

namespace OricOffice.MauiShared.Controls;

/// <summary>
/// Binds the view-model menu models to the platform's native menu bar
/// (Windows and Mac Catalyst). On the other platforms the page shows the
/// tap-to-open <see cref="SuiteMenuBarView"/> row instead.
/// </summary>
public static class SuiteMenuBar
{
    /// <summary>True when this platform renders a native window menu bar.</summary>
    public static bool UsesNativeMenuBar
    {
        get
        {
#if WINDOWS || MACCATALYST
            return true;
#else
            return false;
#endif
        }
    }

    public static void Bind(ContentPage page, IEnumerable<MenuGroup> groups)
    {
#if WINDOWS || MACCATALYST
        foreach (var group in groups)
        {
            var menuBar = new MenuBarItem { Text = group.Header };
            foreach (var item in group.Items)
                Add(menuBar, item);
            page.MenuBarItems.Add(menuBar);
        }
#endif
    }

#if WINDOWS || MACCATALYST
    private static void Add(MenuBarItem menuBar, MenuItemModel model)
    {
        if (model.IsSeparator)
        {
            menuBar.Add(new MenuFlyoutSeparator());
            return;
        }

        if (model.HasChildren)
        {
            var sub = new MenuFlyoutSubItem { Text = model.Header ?? string.Empty, IsEnabled = model.IsEnabled };
            foreach (var child in model.Children!)
                Add(child, sub);
            menuBar.Add(sub);
            return;
        }

        menuBar.Add(BuildItem(model));
    }

    private static void Add(MenuItemModel model, MenuFlyoutSubItem sub)
    {
        if (model.IsSeparator)
        {
            sub.Add(new MenuFlyoutSeparator());
            return;
        }

        if (model.HasChildren)
        {
            var nested = new MenuFlyoutSubItem { Text = model.Header ?? string.Empty, IsEnabled = model.IsEnabled };
            foreach (var child in model.Children!)
                Add(child, nested);
            sub.Add(nested);
            return;
        }

        sub.Add(BuildItem(model));
    }

    private static MenuFlyoutItem BuildItem(MenuItemModel model)
    {
        var item = new MenuFlyoutItem
        {
            Text = model.Header ?? string.Empty,
            IsEnabled = model.IsEnabled,
        };
        if (model.Command is not null)
            item.Command = model.Command;
        else if (model.Action is not null)
            item.Clicked += (_, _) => model.Action();
        return item;
    }
#endif
}
