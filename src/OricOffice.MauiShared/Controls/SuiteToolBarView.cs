using OricOffice.Core.Ui;
using OricOffice.MauiShared.Drawing;
using OricOffice.MauiShared.Theme;

namespace OricOffice.MauiShared.Controls;

/// <summary>
/// A LibreOffice-style toolbar rendered from <see cref="ToolButtonModel"/> models:
/// flat icon+label buttons with hover/pressed feedback, plus hairline separators.
/// Built from Border + gesture recognizers because MAUI's Button cannot host
/// arbitrary content.
/// </summary>
public sealed class SuiteToolBarView : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable<ToolButtonModel>),
        typeof(SuiteToolBarView),
        propertyChanged: OnItemsSourceChanged);

    private readonly StackLayout _row;

    public SuiteToolBarView()
    {
        BackgroundColor = SuiteTheme.Chrome;

        _row = new StackLayout
        {
            Orientation = StackOrientation.Horizontal,
            Spacing = 0,
            Padding = new Thickness(4, 3),
            VerticalOptions = LayoutOptions.Center,
        };

        var grid = new Grid { RowSpacing = 0 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        grid.RowDefinitions.Add(new RowDefinition { Height = 1 });
        grid.Add(_row, 0, 0);
        grid.Add(new BoxView { BackgroundColor = SuiteTheme.ChromeBorder, HeightRequest = 1 }, 0, 1);

        Content = grid;
    }

    public IEnumerable<ToolButtonModel>? ItemsSource
    {
        get => (IEnumerable<ToolButtonModel>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(BindableObject bindable, object? oldValue, object? newValue)
        => ((SuiteToolBarView)bindable).Rebuild();

    private void Rebuild()
    {
        _row.Children.Clear();
        if (ItemsSource is null) return;

        foreach (var model in ItemsSource)
        {
            if (model.IsSeparator)
            {
                _row.Children.Add(new BoxView
                {
                    BackgroundColor = SuiteTheme.ChromeBorder,
                    WidthRequest = 1,
                    HeightRequest = 22,
                    Margin = new Thickness(7, 0),
                    VerticalOptions = LayoutOptions.Center,
                });
                continue;
            }

            _row.Children.Add(BuildButton(model));
        }
    }

    private static Border BuildButton(ToolButtonModel model)
    {
        var content = new StackLayout
        {
            Orientation = StackOrientation.Horizontal,
            Spacing = 5,
            VerticalOptions = LayoutOptions.Center,
        };

        content.Children.Add(new GraphicsView
        {
            WidthRequest = 18,
            HeightRequest = 18,
            Drawable = new SuiteIconDrawable(model.Icon),
            VerticalOptions = LayoutOptions.Center,
        });

        if (!string.IsNullOrEmpty(model.Text))
        {
            content.Children.Add(new Label
            {
                Text = model.Text,
                FontFamily = "OpenSansRegular",
                FontSize = 13,
                TextColor = SuiteTheme.Text,
                VerticalOptions = LayoutOptions.Center,
            });
        }

        var button = new Border
        {
            Padding = new Thickness(9, 5),
            BackgroundColor = Colors.Transparent,
            Content = content,
            VerticalOptions = LayoutOptions.Center,
        };

        var command = model.Command;
        var enabled = command is not null;
        if (!enabled) button.Opacity = 0.4;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (command is null) return;
            if (command.CanExecute(null)) command.Execute(null);
        };
        button.GestureRecognizers.Add(tap);

        if (enabled)
        {
            var pointer = new PointerGestureRecognizer();
            pointer.PointerEntered += (_, _) => button.BackgroundColor = SuiteTheme.Hover;
            pointer.PointerExited += (_, _) => button.BackgroundColor = Colors.Transparent;
            pointer.PointerPressed += (_, _) => button.BackgroundColor = SuiteTheme.Pressed;
            pointer.PointerReleased += (_, _) => button.BackgroundColor = SuiteTheme.Hover;
            button.GestureRecognizers.Add(pointer);
        }

        return button;
    }
}
