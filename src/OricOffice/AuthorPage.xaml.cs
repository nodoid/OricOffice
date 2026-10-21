using Microsoft.Maui.Controls.Shapes;
using OricOffice.Core.Model;
using OricOffice.Core.Ui;
using OricOffice.Core.ViewModels;
using OricOffice.MauiShared.Controls;

namespace OricOffice;

public partial class AuthorPage : ContentPage
{
    private static readonly string[] Palette =
    {
        "#FFFFFF", "#F2F2F2", "#FDE9D9", "#FFF2CC",
        "#E2EFDA", "#DDEBF7", "#E4DFEC", "#F8CBAD",
    };

    private readonly AuthorViewModel _viewModel;
    private Border? _selectedCellBorder;
    private (TableBlock Table, int Row, int Column)? _selectedCell;

    // Format bar controls (built in code so state sync stays simple).
    private bool _syncingFormatBar;
    private Picker _fontPicker = null!;
    private Picker _sizePicker = null!;
    private Button _boldButton = null!;
    private Button _italicButton = null!;
    private Button _underlineButton = null!;
    private Button _strikeButton = null!;
    private Button _superButton = null!;
    private Button _subButton = null!;
    private HorizontalStackLayout _formatRow = null!;
    private VerticalStackLayout _paletteHost = null!;
    private bool _paletteShowsForeground;

    // Table designer state.
    private BorderStyle _designerStyle = BorderStyle.Thin;
    private BorderSides _designerSides = BorderSides.All;
    private string? _designerColour; // null = default ink
    private readonly List<(Button Button, BorderSides Side)> _designerSideButtons = new();
    private Label? _designerTargetLabel;

    public AuthorPage(AuthorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        // A way back to the start center (the Home menu group itself is built
        // by the view model so it survives culture changes).
        viewModel.ReturnToStart = () => Navigation.PopToRootAsync();

        BindingContext = viewModel;

        // Desktop platforms get the platform's native menu bar;
        // tablets keep the tap-to-open menu row from the XAML.
        if (SuiteMenuBar.UsesNativeMenuBar)
        {
            CustomMenuBar.IsVisible = false;
            BindNativeMenus(viewModel.Menus);
            OricOffice.Core.Localization.Loc.Instance.CultureChanged +=
                () => BindNativeMenus(viewModel.Menus);
        }

        viewModel.ElementsChanged += RebuildElements;
        RebuildElements();

        BuildFormatBar();
        BuildDesignerPanel();

        // Everything this page builds in code carries localized text — rebuild
        // it when the culture changes (the XAML bindings update themselves).
        OricOffice.Core.Localization.Loc.Instance.CultureChanged += () =>
        {
            RebuildElements();
            BuildFormatBar();
            SyncFormatBar();
            _paletteHost.IsVisible = false;
            BuildDesignerPanel();
            if (_designerTargetLabel is not null && _selectedCell is { } cell)
                _designerTargetLabel.Text = OricOffice.Core.Localization.Loc.Format(
                    "UI.TargetCell", cell.Table.Name, cell.Row + 1, cell.Column + 1);
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AuthorViewModel.IsBold) or nameof(AuthorViewModel.IsItalic)
                or nameof(AuthorViewModel.IsUnderline) or nameof(AuthorViewModel.IsStrikethrough)
                or nameof(AuthorViewModel.IsSuperscript) or nameof(AuthorViewModel.IsSubscript)
                or nameof(AuthorViewModel.SelectedFontFamily) or nameof(AuthorViewModel.SelectedFontSize)
                or nameof(AuthorViewModel.SelectedForeground) or nameof(AuthorViewModel.SelectedBackground))
                SyncFormatBar();

            if (e.PropertyName is nameof(AuthorViewModel.StyleRevision))
                PushStylesToEditor();

            if (e.PropertyName is nameof(AuthorViewModel.IsTableDesignerOpen))
                DesignerHost.IsVisible = viewModel.IsTableDesignerOpen;
        };
        SyncFormatBar();

        // The RichEditBox is the source of truth for text and selection while editing.
        BodyEditor.TextChangedFromView += text => viewModel.Body = text;
        BodyEditor.SelectionChangedFromView += (start, length) =>
        {
            viewModel.CursorPosition = start;
            viewModel.SelectionLength = length;
        };
        PushStylesToEditor();

        // Images can be dragged in from the file explorer onto the page.
        var drop = new DropGestureRecognizer();
        drop.Drop += async (_, e) =>
        {
            var paths = new List<string>();
#if WINDOWS
            if (e.PlatformArgs is { } platform &&
                platform.DragEventArgs.DataView.Contains(
                    Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await platform.DragEventArgs.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                    if (item is Windows.Storage.StorageFile file)
                        paths.Add(file.Path);
            }
#endif
            if (paths.Count == 0)
            {
                viewModel.StatusText =
                    OricOffice.Core.Localization.Loc.T("Status.DropImages");
                return;
            }

            foreach (var path in paths)
                await viewModel.ImportImageFromPathAsync(path);
        };
        RootGrid.GestureRecognizers.Add(drop);
    }

    /// <summary>Rebuilds the native menu bar after a culture change.</summary>
    private void BindNativeMenus(IEnumerable<MenuGroup> menus)
    {
        MenuBarItems.Clear();
        SuiteMenuBar.Bind(this, menus);
    }

    /// <summary>Pushes the document's style runs (and pending style) into the editor.</summary>
    private void PushStylesToEditor()
    {
        if (BodyEditor is null) return;
        BodyEditor.Runs = _viewModel.Doc.Text.Runs;
        BodyEditor.PendingStyle = _viewModel.Doc.Text.PendingStyle;
        BodyEditor.StylesVersion++;
    }

    // ------------------------------------------------------------ format bar

    private void BuildFormatBar()
    {
        _formatRow = FormatRow;
        _paletteHost = PaletteHost;
        _formatRow.Children.Clear(); // idempotent: also re-run on culture changes

        _fontPicker = new Picker
        {
            ItemsSource = AuthorViewModel.FontFamilies.ToList(),
            Title = OricOffice.Core.Localization.Loc.T("UI.Font"),
            WidthRequest = 150,
            FontSize = 12,
            BackgroundColor = Colors.White,
            TextColor = Color.FromArgb("#1A1A1A"),
        };
        _fontPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingFormatBar) return;
            if (_fontPicker.SelectedItem is string family)
                _viewModel.SetFontFamilyCommand.Execute(family);
        };
        _formatRow.Children.Add(_fontPicker);

        _sizePicker = new Picker
        {
            ItemsSource = AuthorViewModel.FontSizes.ToList(),
            Title = OricOffice.Core.Localization.Loc.T("UI.Size"),
            WidthRequest = 72,
            FontSize = 12,
            BackgroundColor = Colors.White,
            TextColor = Color.FromArgb("#1A1A1A"),
        };
        _sizePicker.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingFormatBar) return;
            if (_sizePicker.SelectedItem is double size)
                _viewModel.SetFontSizeCommand.Execute(size);
        };
        _formatRow.Children.Add(_sizePicker);

        _formatRow.Children.Add(FormatSeparator());

        _boldButton = FormatButton("B");
        _boldButton.Clicked += (_, _) => _viewModel.ToggleBoldCommand.Execute(null);
        _formatRow.Children.Add(_boldButton);

        _italicButton = FormatButton("I");
        _italicButton.Clicked += (_, _) => _viewModel.ToggleItalicCommand.Execute(null);
        _formatRow.Children.Add(_italicButton);

        _underlineButton = FormatButton("U");
        _underlineButton.Clicked += (_, _) => _viewModel.ToggleUnderlineCommand.Execute(null);
        _formatRow.Children.Add(_underlineButton);

        _strikeButton = FormatButton("S");
        _strikeButton.Clicked += (_, _) => _viewModel.ToggleStrikethroughCommand.Execute(null);
        _formatRow.Children.Add(_strikeButton);

        _formatRow.Children.Add(FormatSeparator());

        _superButton = FormatButton("sup");
        _superButton.Clicked += (_, _) => _viewModel.ToggleSuperscriptCommand.Execute(null);
        _formatRow.Children.Add(_superButton);

        _subButton = FormatButton("sub");
        _subButton.Clicked += (_, _) => _viewModel.ToggleSubscriptCommand.Execute(null);
        _formatRow.Children.Add(_subButton);

        _formatRow.Children.Add(FormatSeparator());

        var foreground = FormatButton(OricOffice.Core.Localization.Loc.T("UI.TextColour"));
        foreground.Clicked += (_, _) => ShowPalette(foreground: true);
        _formatRow.Children.Add(foreground);

        var background = FormatButton(OricOffice.Core.Localization.Loc.T("UI.Highlight"));
        background.Clicked += (_, _) => ShowPalette(foreground: false);
        _formatRow.Children.Add(background);
    }

    /// <summary>Shows (or hides) the inline swatch strip for text or highlight colour.</summary>
    private void ShowPalette(bool foreground)
    {
        bool show = _paletteHost.IsVisible == false || _paletteShowsForeground != foreground;
        _paletteShowsForeground = foreground;
        _paletteHost.Children.Clear();
        _paletteHost.IsVisible = show;
        if (!show) return;

        var row = new HorizontalStackLayout { Spacing = 4, Padding = new Thickness(2, 2, 2, 4) };

        string clearLabel = foreground
            ? OricOffice.Core.Localization.Loc.T("UI.Automatic")
            : OricOffice.Core.Localization.Loc.T("UI.None");
        var clear = new Button
        {
            Text = clearLabel,
            FontSize = 11,
            HeightRequest = 26,
            Padding = new Thickness(8, 0),
            BackgroundColor = Colors.White,
            BorderColor = Color.FromArgb("#B0B0B0"),
            BorderWidth = 1,
            CornerRadius = 4,
        };
        clear.Clicked += (_, _) => ApplyPaletteColour(null);
        row.Children.Add(clear);

        foreach (var hex in foreground ? AuthorViewModel.TextForegrounds : AuthorViewModel.TextBackgrounds)
        {
            var colour = hex;
            var swatch = new Button
            {
                BackgroundColor = Color.FromArgb(colour),
                BorderColor = Color.FromArgb("#B0B0B0"),
                BorderWidth = 1,
                WidthRequest = 26,
                HeightRequest = 26,
                CornerRadius = 4,
                Padding = new Thickness(0),
            };
            swatch.Clicked += (_, _) => ApplyPaletteColour(colour);
            row.Children.Add(swatch);
        }

        _paletteHost.Children.Add(row);
    }

    private void ApplyPaletteColour(string? colour)
    {
        if (_paletteShowsForeground)
            _viewModel.SetForegroundCommand.Execute(colour);
        else
            _viewModel.SetBackgroundCommand.Execute(colour);
    }

    private static Button FormatButton(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansSemibold",
        FontSize = 12,
        WidthRequest = 32,
        HeightRequest = 28,
        Padding = new Thickness(0),
        BackgroundColor = Colors.White,
        BorderColor = Color.FromArgb("#B0B0B0"),
        BorderWidth = 1,
        CornerRadius = 4,
    };

    private static BoxView FormatSeparator() => new()
    {
        WidthRequest = 1,
        HeightRequest = 20,
        Color = Color.FromArgb("#D6D6D6"),
        Margin = new Thickness(4, 0),
        VerticalOptions = LayoutOptions.Center,
    };

    private void SyncFormatBar()
    {
        if (_fontPicker is null) return;

        _syncingFormatBar = true;
        try
        {
            var family = _viewModel.SelectedFontFamily;
            _fontPicker.SelectedItem =
                family is not null && AuthorViewModel.FontFamilies.Contains(family) ? family : null;
            _sizePicker.SelectedItem = AuthorViewModel.FontSizes
                .FirstOrDefault(s => Math.Abs(s - _viewModel.SelectedFontSize) < 0.01);

            ApplyActive(_boldButton, _viewModel.IsBold);
            ApplyActive(_italicButton, _viewModel.IsItalic);
            ApplyActive(_underlineButton, _viewModel.IsUnderline);
            ApplyActive(_strikeButton, _viewModel.IsStrikethrough);
            ApplyActive(_superButton, _viewModel.IsSuperscript);
            ApplyActive(_subButton, _viewModel.IsSubscript);
        }
        finally
        {
            _syncingFormatBar = false;
        }
    }

    private static void ApplyActive(Button button, bool active)
    {
        button.BackgroundColor = active ? Color.FromArgb("#CFE0F3") : Colors.White;
        button.TextColor = active ? Color.FromArgb("#1F4E79") : Color.FromArgb("#1A1A1A");
    }

    // ------------------------------------------------------------ elements panel

    private void RebuildElements()
    {
        ElementsPanel.Children.Clear();
        _selectedCell = null;
        _selectedCellBorder = null;

        BuildImagesSection(); // banner strip inside the page
        BuildTablesSection(); // below the page

        // The image button lives on the toolbar; tables are added here.
        var addBar = new HorizontalStackLayout { Spacing = 10 };
        addBar.Children.Add(new Button
        {
            Text = OricOffice.Core.Localization.Loc.T("UI.AddTable"),
            Style = SmallButton(),
            Command = _viewModel.AddTableCommand,
        });
        ElementsPanel.Children.Add(addBar);
    }

    private void BuildImagesSection()
    {
        var images = _viewModel.Doc.Text.Images;
        ImageBanner.Children.Clear();
        ImageBannerScroller.IsVisible = images.Count > 0;

        for (int i = 0; i < images.Count; i++)
        {
            var index = i;
            var image = images[i];

            var thumb = new Image
            {
                Source = ImageSource.FromStream(() => new MemoryStream(image.Data)),
                HeightRequest = 52,
                WidthRequest = 64,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
            };

            var remove = new Button
            {
                Text = "✕",
                FontSize = 10,
                HeightRequest = 22,
                Padding = new Thickness(6, 1),
                CornerRadius = 3,
                BackgroundColor = Colors.White,
                BorderColor = Color.FromArgb("#B0B0B0"),
                BorderWidth = 1,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 2, 0, 0),
            };
            remove.Clicked += (_, _) => _viewModel.RemoveImageCommand.Execute(index);

            var cardBody = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Start,
                HorizontalOptions = LayoutOptions.Center,
            };
            cardBody.Children.Add(thumb);
            cardBody.Children.Add(remove);

            var card = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#D6D6D6"),
                StrokeThickness = 1,
                StrokeShape = MakeCardShape(4),
                Padding = new Thickness(6, 5),
                VerticalOptions = LayoutOptions.Start,
                Content = cardBody,
            };
            ImageBanner.Children.Add(card);
        }
    }

    private void BuildTablesSection()
    {
        if (_viewModel.Doc.Text.Tables.Count == 0) return;

        ElementsPanel.Children.Add(SectionHeader(
            OricOffice.Core.Localization.Loc.Format("UI.TablesSection", _viewModel.Doc.Text.Tables.Count)));

        for (int t = 0; t < _viewModel.Doc.Text.Tables.Count; t++)
        {
            var index = t;
            var table = _viewModel.Doc.Text.Tables[t];
            ElementsPanel.Children.Add(BuildTableCard(table, index));
        }
    }

    private Border BuildTableCard(TableBlock table, int index)
    {
        var body = new VerticalStackLayout { Spacing = 8 };

        // ---- header: name + structure buttons
        var header = new HorizontalStackLayout { Spacing = 6 };
        header.Children.Add(new Label
        {
            Text = table.Name,
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            TextColor = Color.FromArgb("#1F1F1F"),
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        header.Children.Add(TableButton(OricOffice.Core.Localization.Loc.T("UI.RowPlus"), () => _viewModel.AddTableRowCommand.Execute(table)));
        header.Children.Add(TableButton(OricOffice.Core.Localization.Loc.T("UI.RowMinus"), () => _viewModel.RemoveTableRowCommand.Execute(table)));
        header.Children.Add(TableButton(OricOffice.Core.Localization.Loc.T("UI.ColPlus"), () => _viewModel.AddTableColumnCommand.Execute(table)));
        header.Children.Add(TableButton(OricOffice.Core.Localization.Loc.T("UI.ColMinus"), () => _viewModel.RemoveTableColumnCommand.Execute(table)));
        var delete = TableButton(OricOffice.Core.Localization.Loc.T("UI.Delete"), () => _viewModel.RemoveTableCommand.Execute(index));
        header.Children.Add(delete);
        body.Children.Add(header);

        // ---- the grid
        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
        for (int c = 0; c < table.Columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int r = 0; r < table.Rows; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        for (int r = 0; r < table.Rows; r++)
        {
            for (int c = 0; c < table.Columns; c++)
            {
                var rr = r;
                var cc = c;
                var entry = new Entry
                {
                    Text = table.TextAt(r, c),
                    FontFamily = "OpenSansRegular",
                    FontSize = 13,
                    BackgroundColor = Colors.Transparent,
                    TextColor = Color.FromArgb("#1A1A1A"),
                    HeightRequest = 30,
                    VerticalOptions = LayoutOptions.Center,
                };
                entry.TextChanged += (_, _) =>
                    _viewModel.SetTableCellText(table, rr, cc, entry.Text);
                var cell = BuildTableCell(table, r, c, entry);
                entry.Focused += (_, _) => SelectCell(table, rr, cc, cell);
                grid.Children.Add(cell);
                Grid.SetColumn(cell, c);
                Grid.SetRow(cell, r);
            }
        }
        body.Children.Add(grid);

        // ---- colour palette
        var paletteRow = new HorizontalStackLayout { Spacing = 6 };
        paletteRow.Children.Add(new Label
        {
            Text = OricOffice.Core.Localization.Loc.T("UI.CellColour"),
            FontFamily = "OpenSansRegular",
            FontSize = 12,
            TextColor = Color.FromArgb("#5F5F5F"),
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        foreach (var hex in Palette)
        {
            var colour = hex;
            var swatch = new Button
            {
                BackgroundColor = Color.FromArgb(colour),
                BorderColor = Color.FromArgb("#B0B0B0"),
                BorderWidth = 1,
                WidthRequest = 26,
                HeightRequest = 26,
                CornerRadius = 4,
                Padding = new Thickness(0),
                MinimumWidthRequest = 26,
            };
            swatch.Clicked += (_, _) => ApplyFill(colour);
            paletteRow.Children.Add(swatch);
        }
        var none = new Button
        {
            Text = OricOffice.Core.Localization.Loc.T("UI.None"),
            FontSize = 11,
            BackgroundColor = Colors.White,
            BorderColor = Color.FromArgb("#B0B0B0"),
            BorderWidth = 1,
            CornerRadius = 4,
            Padding = new Thickness(8, 3),
        };
        none.Clicked += (_, _) => ApplyFill(null);
        paletteRow.Children.Add(none);
        body.Children.Add(paletteRow);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#D6D6D6"),
            StrokeThickness = 1,
            StrokeShape = MakeCardShape(6),
            Padding = new Thickness(12, 10),
            Content = body,
        };
        return card;
    }

    // ------------------------------------------------------------ table cell borders

    /// <summary>Builds one table cell: the content plus per-side border lines,
    /// falling back to the classic thin grid for sides without an override.</summary>
    private static Border BuildTableCell(TableBlock table, int r, int c, View content)
    {
        var fill = table.FillAt(r, c);
        var host = new Grid
        {
            BackgroundColor = fill is null ? Colors.White : Color.FromArgb(fill),
        };
        host.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        host.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        host.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        content.Margin = new Thickness(7, 3);
        host.Children.Add(content);
        Grid.SetRow(content, 1);
        Grid.SetColumn(content, 1);

        AddCellSide(host, table, r, c, BorderSide.Top, horizontal: true, row: 0, column: 1);
        AddCellSide(host, table, r, c, BorderSide.Bottom, horizontal: true, row: 2, column: 1);
        AddCellSide(host, table, r, c, BorderSide.Left, horizontal: false, row: 1, column: 0);
        AddCellSide(host, table, r, c, BorderSide.Right, horizontal: false, row: 1, column: 2);

        return new Border
        {
            BackgroundColor = Colors.Transparent,
            Stroke = null,
            Padding = new Thickness(0),
            Content = host,
        };
    }

    private static void AddCellSide(
        Grid host, TableBlock table, int r, int c, BorderSide side, bool horizontal, int row, int column)
    {
        var border = table.BorderAt(r, c, side);
        var style = border?.Style ?? BorderStyle.Thin;
        var hex = border?.Colour ?? "#D6D6D6";
        if (style == BorderStyle.None) return;

        var line = MakeEdgeLine(horizontal, style, hex);
        host.Children.Add(line);
        Grid.SetRow(line, row);
        Grid.SetColumn(line, column);
        if (horizontal)
            Grid.SetColumnSpan(line, 3);
        else
            Grid.SetRowSpan(line, 3);
    }

    /// <summary>Draws one border line as stacked/segmented BoxViews (no dashes API needed).</summary>
    private static View MakeEdgeLine(bool horizontal, BorderStyle style, string hex)
    {
        var tint = Color.FromArgb(hex);

        BoxView Solid(double thickness)
        {
            var box = new BoxView { Color = tint };
            if (horizontal) box.HeightRequest = thickness; else box.WidthRequest = thickness;
            return box;
        }

        switch (style)
        {
            case BorderStyle.Double:
            {
                var pair = new StackLayout { Spacing = 1 };
                if (!horizontal) pair.Orientation = StackOrientation.Horizontal;
                pair.Children.Add(Solid(1));
                pair.Children.Add(Solid(1));
                return pair;
            }

            case BorderStyle.Dashed:
                return SegmentedEdge(horizontal, tint, segments: 7, thickness: 1);

            case BorderStyle.Dotted:
                return SegmentedEdge(horizontal, tint, segments: 13, thickness: 1);

            default:
                return Solid(style == BorderStyle.Thick ? 2 : 1);
        }
    }

    private static View SegmentedEdge(bool horizontal, Color tint, int segments, double thickness)
    {
        // Alternating star-sized segments and gaps: the pattern scales with the cell.
        var grid = new Grid { RowSpacing = 0, ColumnSpacing = 0 };
        for (int i = 0; i < segments; i++)
        {
            if (horizontal) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            else grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }

        for (int i = 0; i < segments; i += 2)
        {
            var box = new BoxView { Color = tint };
            if (horizontal)
            {
                box.HeightRequest = thickness;
                Grid.SetColumn(box, i);
            }
            else
            {
                box.WidthRequest = thickness;
                Grid.SetRow(box, i);
            }
            grid.Children.Add(box);
        }
        return grid;
    }

    // ------------------------------------------------------------ table designer

    private void BuildDesignerPanel()
    {
        DesignerHost.Children.Clear(); // idempotent: also re-run on culture changes
        _designerSideButtons.Clear();

        var panel = new Border
        {
            BackgroundColor = Color.FromArgb("#F5F5F5"),
            Stroke = Color.FromArgb("#B0B0B0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4) },
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 2, 0, 4),
        };
        var body = new VerticalStackLayout { Spacing = 6 };

        // ---- header: title + current target + close
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var titles = new HorizontalStackLayout { Spacing = 8 };
        titles.Children.Add(new Label
        {
            Text = OricOffice.Core.Localization.Loc.T("UI.TableDesigner"),
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            TextColor = Color.FromArgb("#1F1F1F"),
            VerticalOptions = LayoutOptions.Center,
        });
        _designerTargetLabel = new Label
        {
            Text = "",
            FontFamily = "OpenSansRegular",
            FontSize = 11,
            TextColor = Color.FromArgb("#5F5F5F"),
            VerticalOptions = LayoutOptions.Center,
        };
        titles.Children.Add(_designerTargetLabel);
        header.Children.Add(titles);
        Grid.SetColumn(titles, 0);
        var close = TableButton("✕", () => _viewModel.ToggleTableDesignerCommand.Execute(null));
        header.Children.Add(close);
        Grid.SetColumn(close, 1);
        body.Children.Add(header);

        // ---- border type + sides
        var row = new HorizontalStackLayout { Spacing = 6 };
        row.Children.Add(DesignerLabel(OricOffice.Core.Localization.Loc.T("UI.Border")));
        var stylePicker = new Picker
        {
            // Display strings are localized; the index still maps to BorderStyle + 1.
            ItemsSource = new[]
            {
                OricOffice.Core.Localization.Loc.T("BorderStyle.Thin"),
                OricOffice.Core.Localization.Loc.T("BorderStyle.Thick"),
                OricOffice.Core.Localization.Loc.T("BorderStyle.Double"),
                OricOffice.Core.Localization.Loc.T("BorderStyle.Dashed"),
                OricOffice.Core.Localization.Loc.T("BorderStyle.Dotted"),
                OricOffice.Core.Localization.Loc.T("BorderStyle.None"),
            },
            SelectedIndex = 0,
            WidthRequest = 96,
            FontSize = 12,
            BackgroundColor = Colors.White,
        };
        stylePicker.SelectedIndexChanged += (_, _) =>
        {
            if (stylePicker.SelectedIndex >= 0)
                _designerStyle = (BorderStyle)(stylePicker.SelectedIndex + 1);
        };
        row.Children.Add(stylePicker);

        row.Children.Add(DesignerLabel(OricOffice.Core.Localization.Loc.T("UI.Sides")));
        foreach (var (name, side) in new[]
                 {
                     (OricOffice.Core.Localization.Loc.T("UI.SideTop"), BorderSides.Top),
                     (OricOffice.Core.Localization.Loc.T("UI.SideRight"), BorderSides.Right),
                     (OricOffice.Core.Localization.Loc.T("UI.SideBottom"), BorderSides.Bottom),
                     (OricOffice.Core.Localization.Loc.T("UI.SideLeft"), BorderSides.Left),
                 })
        {
            var button = TableButton(name, () => ToggleDesignerSide(side));
            _designerSideButtons.Add((button, side));
            row.Children.Add(button);
        }
        var allSides = TableButton(OricOffice.Core.Localization.Loc.T("UI.SideAll"), () =>
        {
            _designerSides = BorderSides.All;
            RefreshDesignerSideButtons();
        });
        row.Children.Add(allSides);
        body.Children.Add(row);

        // ---- colour
        var colourRow = new HorizontalStackLayout { Spacing = 4 };
        colourRow.Children.Add(DesignerLabel(OricOffice.Core.Localization.Loc.T("UI.Colour")));
        foreach (var hex in new[]
                 {
                     "#000000", "#404040", "#C00000", "#0070C0",
                     "#70AD47", "#ED7D31", "#7030A0", "#FFFFFF",
                 })
        {
            var colour = hex;
            var swatch = new Button
            {
                BackgroundColor = Color.FromArgb(colour),
                BorderColor = Color.FromArgb("#B0B0B0"),
                BorderWidth = 1,
                WidthRequest = 22,
                HeightRequest = 22,
                CornerRadius = 3,
                Padding = new Thickness(0),
            };
            swatch.Clicked += (_, _) => _designerColour = colour;
            colourRow.Children.Add(swatch);
        }
        var ink = TableButton(OricOffice.Core.Localization.Loc.T("UI.Ink"), () => _designerColour = null);
        colourRow.Children.Add(ink);
        body.Children.Add(colourRow);

        // ---- apply
        var applyRow = new HorizontalStackLayout { Spacing = 6 };
        applyRow.Children.Add(TableButton(OricOffice.Core.Localization.Loc.T("UI.ApplyCell"), () => ApplyDesigner(singleCell: true)));
        applyRow.Children.Add(TableButton(OricOffice.Core.Localization.Loc.T("UI.ApplyTable"), () => ApplyDesigner(singleCell: false)));
        body.Children.Add(applyRow);

        panel.Content = body;
        DesignerHost.Children.Add(panel);
        RefreshDesignerSideButtons();
    }

    private static Label DesignerLabel(string text) => new()
    {
        Text = text,
        FontFamily = "OpenSansRegular",
        FontSize = 12,
        TextColor = Color.FromArgb("#3C3C3C"),
        VerticalOptions = LayoutOptions.Center,
        Margin = new Thickness(2, 0, 2, 0),
    };

    private void ToggleDesignerSide(BorderSides side)
    {
        _designerSides = _designerSides.HasFlag(side)
            ? _designerSides & ~side
            : _designerSides | side;
        RefreshDesignerSideButtons();
    }

    private void RefreshDesignerSideButtons()
    {
        foreach (var (button, side) in _designerSideButtons)
            ApplyActive(button, _designerSides.HasFlag(side));
    }

    private void ApplyDesigner(bool singleCell)
    {
        if (_selectedCell is not { } cell)
        {
            _viewModel.StatusText = OricOffice.Core.Localization.Loc.T("UI.ApplyDesignerHint");
            return;
        }

        var (table, row, column) = cell;
        int rows = singleCell ? 1 : table.Rows;
        int columns = singleCell ? 1 : table.Columns;
        _viewModel.ApplyTableBorder(table, row, column, rows, columns,
            _designerSides, _designerStyle, _designerColour);
    }

    private Button TableButton(string text, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            Style = SmallButton(),
        };
        button.Clicked += (_, _) => onClick();
        return button;
    }

    private void SelectCell(TableBlock table, int row, int column, Border border)
    {
        if (_selectedCellBorder is not null)
        {
            _selectedCellBorder.Stroke = null;
            _selectedCellBorder.StrokeThickness = 0;
        }
        _selectedCell = (table, row, column);
        _selectedCellBorder = border;
        border.Stroke = Color.FromArgb("#2E5EAA");
        border.StrokeThickness = 2;

        _designerTargetLabel!.Text = OricOffice.Core.Localization.Loc.Format(
            "UI.TargetCell", table.Name, row + 1, column + 1);
    }

    private void ApplyFill(string? fill)
    {
        if (_selectedCell is not { } cell)
        {
            _viewModel.StatusText = OricOffice.Core.Localization.Loc.T("Status.SelectCellFirst");
            return;
        }
        _viewModel.SetTableCellFill(cell.Table, cell.Row, cell.Column, fill);
    }

    private static Style SmallButton()
        => (Style)Application.Current!.Resources["Suite.SmallButton"];

    private static Label SectionHeader(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current!.Resources["Suite.SectionTitle"],
    };

    private static RoundRectangle MakeCardShape(double radius)
        => new() { CornerRadius = new CornerRadius(radius) };
}
