using OricOffice.Core.Ui;
using OricOffice.MauiShared.Theme;

namespace OricOffice.MauiShared.Controls;

/// <summary>
/// The drop-down menu row for the mobile-style platforms (Android/iOS).
///
/// On Windows and macOS the apps use the platform's native menu bar instead
/// (see <see cref="SuiteMenuBar"/>). Here, tapping a menu header opens its items
/// as an action sheet — the touch-idiomatic equivalent — driven by the same
/// view-model menu models.
/// </summary>
public sealed class SuiteMenuBarView : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable<MenuGroup>),
        typeof(SuiteMenuBarView),
        propertyChanged: OnItemsSourceChanged);

    private readonly StackLayout _row;

    public SuiteMenuBarView()
    {
        BackgroundColor = SuiteTheme.Chrome;

        _row = new StackLayout
        {
            Orientation = StackOrientation.Horizontal,
            Spacing = 0,
            Padding = new Thickness(4, 1),
            VerticalOptions = LayoutOptions.Center,
        };

        var grid = new Grid { RowSpacing = 0 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        grid.RowDefinitions.Add(new RowDefinition { Height = 1 });
        grid.Add(_row, 0, 0);
        grid.Add(new BoxView { BackgroundColor = SuiteTheme.ChromeBorder, HeightRequest = 1 }, 0, 1);

        Content = grid;
    }

    public IEnumerable<MenuGroup>? ItemsSource
    {
        get => (IEnumerable<MenuGroup>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
        => ((SuiteMenuBarView)bindable).Rebuild();

    private void Rebuild()
    {
        _row.Children.Clear();
        if (ItemsSource is null) return;

        foreach (var group in ItemsSource)
        {
            var captured = group;
            var button = new Button
            {
                Text = group.Header,
                BackgroundColor = Colors.Transparent,
                TextColor = SuiteTheme.Text,
                FontFamily = "OpenSansRegular",
                FontSize = 14,
                Padding = new Thickness(11, 4),
                CornerRadius = 4,
                BorderWidth = 0,
                HeightRequest = 29,
                MinimumHeightRequest = 29,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
            };
            AttachHoverStates(button);
            button.Clicked += async (_, _) => await ShowGroupAsync(captured);
            _row.Children.Add(button);
        }
    }

    private static async Task ShowGroupAsync(MenuGroup group)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return;

        // Flatten one level of sub-menu so everything is reachable from a sheet.
        var flat = new List<(string Label, MenuItemModel Model)>();
        foreach (var item in group.Items)
        {
            if (item.IsSeparator) continue;
            if (item.Children is { Count: > 0 })
            {
                foreach (var child in item.Children)
                {
                    if (!child.IsSeparator && child.Header is not null)
                        flat.Add((child.Header, child));
                }
            }
            else if (item.Header is not null)
            {
                flat.Add((item.Header, item));
            }
        }
        if (flat.Count == 0) return;

        var choice = await page.DisplayActionSheetAsync(
            group.Header, "Cancel", null, flat.Select(f => f.Label).ToArray());

        if (string.IsNullOrEmpty(choice)) return;
        var match = flat.FirstOrDefault(f => f.Label == choice);
        if (match.Model is null) return;

        if (match.Model.Command is not null)
        {
            if (match.Model.Command.CanExecute(null))
                match.Model.Command.Execute(null);
        }
        else
        {
            match.Model.Action?.Invoke();
        }
    }

    internal static void AttachHoverStates(Button button)
    {
        var common = new VisualStateGroup { Name = "CommonStates" };
        common.States.Add(new VisualState { Name = "Normal" });

        var pressed = new VisualState { Name = "Pressed" };
        pressed.Setters.Add(new Setter { Property = Button.BackgroundColorProperty, Value = SuiteTheme.Pressed });
        common.States.Add(pressed);

        var pointerOver = new VisualState { Name = "PointerOver" };
        pointerOver.Setters.Add(new Setter { Property = Button.BackgroundColorProperty, Value = SuiteTheme.Hover });
        common.States.Add(pointerOver);

        var disabled = new VisualState { Name = "Disabled" };
        disabled.Setters.Add(new Setter { Property = Button.TextColorProperty, Value = SuiteTheme.TextSecondary });
        disabled.Setters.Add(new Setter { Property = Button.BackgroundColorProperty, Value = Colors.Transparent });
        common.States.Add(disabled);

        var groups = new VisualStateGroupList { common };
        VisualStateManager.SetVisualStateGroups(button, groups);
    }
}
