using Microsoft.Maui.Layouts;
using OricOffice.Core.Model;
using OricOffice.Core.Services;
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

    // Matches GridContainer's Padding so image anchors line up with cells.
    private const double GridPadX = 10;
    private const double GridPadY = 8;

    private readonly CalcViewModel _viewModel;
    private readonly IDialogService _dialogs;

    public CalcPage(CalcViewModel viewModel, IDialogService dialogs)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _dialogs = dialogs;

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

        viewModel.GridChanged += RebuildGrid;
        viewModel.ScrollToCellRequested += ScrollToCell;
        viewModel.ImagesChanged += RebuildImages;
        RebuildGrid();
        RebuildImages();
    }

    private void OnFormulaCommitted(object? sender, EventArgs e)
        => _viewModel.CommitFormulaCommand.Execute(null);

    /// <summary>Rebuilds the native menu bar after a culture change.</summary>
    private void BindNativeMenus(IEnumerable<MenuGroup> menus)
    {
        MenuBarItems.Clear();
        SuiteMenuBar.Bind(this, menus);
    }

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

    // ------------------------------------------------------------ floating images

    private void RebuildImages()
    {
        ImageLayer.Children.Clear();

        var minRight = GridPadX + RowHeaderWidth + _viewModel.Columns * ColumnWidth + 260;
        var minBottom = GridPadY + HeaderHeight + _viewModel.Rows * RowHeight + 240;

        for (int i = 0; i < _viewModel.Doc.Sheet.Images.Count; i++)
        {
            var index = i;
            var model = _viewModel.Doc.Sheet.Images[i];

            ComputeDisplaySize(model, minRight, minBottom, out var w, out var h);
            var x = GridPadX + RowHeaderWidth + model.AnchorColumn * ColumnWidth + model.OffsetX;
            var y = GridPadY + HeaderHeight + model.AnchorRow * RowHeight + model.OffsetY;

            var image = new Image
            {
                Source = ImageSource.FromStream(() => new MemoryStream(model.Data)),
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
            };

            // Corner grip: drag to resize (aspect ratio preserved).
            var grip = new BoxView
            {
                Color = SuiteTheme.Accent,
                WidthRequest = 12,
                HeightRequest = 12,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(0, 0, 1, 1),
            };

            var frame = new Grid { WidthRequest = w, HeightRequest = h };
            frame.Children.Add(image);
            frame.Children.Add(grip);
            AbsoluteLayout.SetLayoutBounds(frame, new Rect(x, y, w, h));
            AbsoluteLayout.SetLayoutFlags(frame, AbsoluteLayoutFlags.None);

            AttachDrag(frame, model, x, y);
            AttachResize(grip, frame, model, x, y);

            var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            doubleTap.Tapped += async (_, _) =>
            {
                var ok = await _dialogs.ConfirmAsync(
                    OricOffice.Core.Localization.Loc.T("Dialog.RemoveImage"),
                    OricOffice.Core.Localization.Loc.Format("Dialog.RemoveImage.Body", model.Name),
                    OricOffice.Core.Localization.Loc.T("Dialog.RemoveImage.Accept"),
                    OricOffice.Core.Localization.Loc.T("Dialog.Cancel"));
                if (ok) _viewModel.RemoveImageCommand.Execute(index);
            };
            frame.GestureRecognizers.Add(doubleTap);

            ImageLayer.Children.Add(frame);
        }
    }

    private static void ComputeDisplaySize(SheetImage image, double minW, double minH, out double w, out double h)
    {
        // The model stores the on-sheet size; shrink to fit the grid canvas only
        // when it would overflow (the model size is also what exports render).
        var mw = image.Width > 0 ? (double)image.Width : 160;
        var mh = image.Height > 0 ? (double)image.Height : 120;
        var scale = Math.Min(1.0, Math.Min(Math.Max(minW, 700) / mw, Math.Max(minH, 480) / mh));
        w = Math.Round(mw * scale);
        h = Math.Round(mh * scale);
    }

    /// <summary>
    /// Drag with window-relative pointer coordinates: the window origin never
    /// moves, so the cursor delta is simply (current − press).
    /// </summary>
    private void AttachDrag(Grid frame, SheetImage model, double baseX, double baseY)
    {
        var pointer = new PointerGestureRecognizer();
        Point? pressPoint = null;
        double deltaX = 0, deltaY = 0;

        pointer.PointerPressed += (_, e) =>
        {
            pressPoint = e.GetPosition(null);
            deltaX = deltaY = 0;
        };

        pointer.PointerMoved += (_, e) =>
        {
            if (pressPoint is not { } origin || e.GetPosition(null) is not { } current) return;
            deltaX = current.X - origin.X;
            deltaY = current.Y - origin.Y;
            AbsoluteLayout.SetLayoutBounds(frame,
                new Rect(baseX + deltaX, baseY + deltaY, frame.WidthRequest, frame.HeightRequest));
        };

        pointer.PointerReleased += (_, _) =>
        {
            if (pressPoint is null) return;
            pressPoint = null;
            if (deltaX == 0 && deltaY == 0) return;
            _viewModel.MoveImage(model,
                model.OffsetX + (int)Math.Round(deltaX),
                model.OffsetY + (int)Math.Round(deltaY));
        };

        frame.GestureRecognizers.Add(pointer);
    }

    /// <summary>
    /// Corner grip drag: resizes the frame while preserving the aspect ratio,
    /// committing the new size to the model on release.
    /// </summary>
    private void AttachResize(BoxView grip, Grid frame, SheetImage model, double baseX, double baseY)
    {
        var pointer = new PointerGestureRecognizer();
        Point? origin = null;
        double baseW = 0, baseH = 0;
        double newW = 0, newH = 0;

        pointer.PointerPressed += (_, e) =>
        {
            origin = e.GetPosition(null);
            baseW = frame.WidthRequest;
            baseH = frame.HeightRequest;
            newW = baseW;
            newH = baseH;
        };

        pointer.PointerMoved += (_, e) =>
        {
            if (origin is not { } o || e.GetPosition(null) is not { } cur) return;
            var ratio = baseH / baseW;
            newW = Math.Clamp(baseW + (cur.X - o.X), 32, 1600);
            newH = Math.Round(newW * ratio);
            frame.WidthRequest = newW;
            frame.HeightRequest = newH;
            AbsoluteLayout.SetLayoutBounds(frame, new Rect(baseX, baseY, newW, newH));
        };

        pointer.PointerReleased += (_, _) =>
        {
            if (origin is null) return;
            origin = null;
            if (newW == baseW && newH == baseH) return;
            _viewModel.ResizeImage(model, (int)Math.Round(newW), (int)Math.Round(newH));
        };

        grip.GestureRecognizers.Add(pointer);
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
