using OricOffice.Core.Ui;
using OricOffice.Core.ViewModels;
using OricOffice.MauiShared.Controls;
using OricOffice.MauiShared.Drawing;
using OricOffice.MauiShared.Theme;

namespace OricOffice;

public partial class CalcPage : ContentPage
{
    private const double ColumnWidth = 104;
    private const double RowHeight = 30;
    private const double RowHeaderWidth = 42;
    private const double HeaderHeight = 26;

    private readonly CalcViewModel _viewModel;

    public CalcPage(CalcViewModel viewModel)
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

        viewModel.GridChanged += RebuildGrid;
        viewModel.ScrollToCellRequested += ScrollToCell;
        RebuildGrid();
    }

    private void OnFormulaCommitted(object? sender, EventArgs e)
        => _viewModel.CommitFormulaCommand.Execute(null);

    private void OnNameBoxCompleted(object? sender, EventArgs e)
        => _viewModel.GoToAddressCommand.Execute(null);

    private void ScrollToCell(CellViewModel cell)
    {
        var y = HeaderHeight + cell.Row * RowHeight - 48;
        var x = RowHeaderWidth + cell.Column * ColumnWidth - 72;
        _ = GridScroller.ScrollToAsync(Math.Max(0, x), Math.Max(0, y), animated: true);
    }

    private void RebuildGrid()
    {
        GridContainer.Children.Clear();

        // Header row: corner + column letters.
        var header = new StackLayout { Orientation = StackOrientation.Horizontal, Spacing = 0 };
        header.Children.Add(MakeHeaderCell(string.Empty, RowHeaderWidth, HeaderHeight));
        foreach (var title in _viewModel.ColumnHeaders)
            header.Children.Add(MakeHeaderCell(title, ColumnWidth, HeaderHeight));
        GridContainer.Children.Add(header);

        // Data rows.
        for (int r = 0; r < _viewModel.Rows; r++)
        {
            var row = new StackLayout
            {
                Orientation = StackOrientation.Horizontal,
                Spacing = 0,
                HeightRequest = RowHeight,
            };
            row.Children.Add(MakeHeaderCell(_viewModel.RowHeaders[r].ToString(), RowHeaderWidth, RowHeight));
            for (int c = 0; c < _viewModel.Columns; c++)
                row.Children.Add(MakeCell(_viewModel.CellAt(r, c)));
            GridContainer.Children.Add(row);
        }
    }

    private static Border MakeHeaderCell(string text, double width, double height)
    {
        var label = new Label
        {
            Text = text,
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            TextColor = SuiteTheme.TextSecondary,
            BackgroundColor = SuiteTheme.HeaderBackground,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            Padding = new Thickness(2, 0),
        };

        return new Border
        {
            WidthRequest = width,
            HeightRequest = height,
            BackgroundColor = SuiteTheme.HeaderBackground,
            Stroke = SuiteTheme.GridLine,
            StrokeThickness = 1,
            Padding = new Thickness(0),
            Content = label,
        };
    }

    private Border MakeCell(CellViewModel cell)
    {
        var label = new Label
        {
            FontFamily = "OpenSansRegular",
            FontSize = 13,
            TextColor = SuiteTheme.Text,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        label.SetBinding(Microsoft.Maui.Controls.Label.TextProperty,
            new Binding(nameof(CellViewModel.Display), source: cell));

        var border = new Border
        {
            WidthRequest = ColumnWidth,
            HeightRequest = RowHeight,
            BackgroundColor = Colors.White,
            Stroke = SuiteTheme.GridLine,
            StrokeThickness = 1,
            Padding = new Thickness(7, 0),
            Content = label,
            BindingContext = cell,
        };

        border.Triggers.Add(new DataTrigger(typeof(Border))
        {
            Binding = new Binding(nameof(CellViewModel.IsSelected)),
            Value = true,
            Setters =
            {
                new Setter { Property = Border.StrokeProperty, Value = SuiteTheme.Accent },
                new Setter { Property = Border.StrokeThicknessProperty, Value = 2d },
            },
        });

        var captured = cell;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _viewModel.SelectCell(captured);
        border.GestureRecognizers.Add(tap);

        return border;
    }
}
