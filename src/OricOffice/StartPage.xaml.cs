using System.Collections.ObjectModel;
using OricOffice.Core;
using OricOffice.Core.Localization;
using OricOffice.Core.Model;
using OricOffice.Core.Ui;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.ViewModels;
using OricOffice.MauiShared.Controls;

namespace OricOffice;

public partial class StartPage : ContentPage
{
    /// <summary>Language switcher labels — each language names itself.</summary>
    private static readonly (string Code, string Label)[] Languages =
    {
        ("en", "English"),
        ("fr", "Français"),
        ("it", "Italiano"),
        ("de", "Deutsch"),
    };

    private readonly IServiceProvider _services;
    private readonly OricFileStore _store;

    public StartPage(
        IServiceProvider services,
        IDialogService dialogs,
        IAppLifetimeService lifetime,
        OricFileStore store)
    {
        _services = services;
        _store = store;

        // Restore the persisted language choice before the bindings read Loc.
        var saved = LoadSavedCulture();
        if (saved.Length > 0 && Loc.SupportedCultures.Contains(saved))
            Loc.Instance.Culture = saved;

        Menus = new ObservableCollection<MenuGroup>();
        RebuildMenus(dialogs, lifetime);

        InitializeComponent();
        BindingContext = this;

        BuildLanguageRow();

        if (SuiteMenuBar.UsesNativeMenuBar)
        {
            CustomMenuBar.IsVisible = false;
            BindNativeMenus();
        }

        // A culture change rebuilds everything this page builds in code:
        // the menu bar, the recents header and the language buttons.
        Loc.Instance.CultureChanged += () =>
        {
            RebuildMenus(dialogs, lifetime);
            BuildLanguageRow();
            RenderRecents();
            BindNativeMenus();
        };
    }

    public ObservableCollection<MenuGroup> Menus { get; }

    // ------------------------------------------------------------ culture persistence
    //
    // A plain file rather than MAUI Preferences: the app runs unpackaged on
    // Windows (WindowsPackageType=None) where ApplicationData-backed APIs are
    // unreliable. Reads and writes are best-effort — the app runs fine without them.

    private static string CultureFilePath
        => Path.Combine(FileSystem.AppDataDirectory, "culture.txt");

    private static string LoadSavedCulture()
    {
        try { return File.Exists(CultureFilePath) ? File.ReadAllText(CultureFilePath).Trim() : string.Empty; }
        catch { return string.Empty; }
    }

    private static void SaveCulture(string culture)
    {
        try { File.WriteAllText(CultureFilePath, culture); }
        catch { /* best effort */ }
    }

    private void RebuildMenus(IDialogService dialogs, IAppLifetimeService lifetime)
    {
        Menus.Clear();

        Menus.Add(new MenuGroup(Loc.T("Menu.File"),
            new MenuItemModel(Loc.T("Start.NewDocument"), action: () => Open<AuthorPage>()),
            new MenuItemModel(Loc.T("Start.NewSpreadsheet"), action: () => Open<CalcPage>()),
            new MenuItemModel(Loc.T("Start.NewDatabase"), action: () => Open<BasePage>()),
            new MenuItemModel(isSeparator: true),
            new MenuItemModel(Loc.T("Item.Exit"), action: lifetime.Exit)));

        Menus.Add(new MenuGroup(Loc.T("Menu.Help"),
            new MenuItemModel(Loc.Format("Item.About", "Oric Office"), action: () => _ = dialogs.MessageAsync(
                Loc.Format("Item.About", "Oric Office"),
                Loc.T("Start.About.Body")))));
    }

    private void BindNativeMenus()
    {
        if (!SuiteMenuBar.UsesNativeMenuBar) return;
        MenuBarItems.Clear();
        SuiteMenuBar.Bind(this, Menus);
    }

    // ------------------------------------------------------------ language

    private void BuildLanguageRow()
    {
        // Keep the leading "Language:" label, replace the buttons.
        while (LanguageRow.Children.Count > 1) LanguageRow.Children.RemoveAt(LanguageRow.Children.Count - 1);

        var current = Loc.Instance.Culture;
        foreach (var (code, label) in Languages)
        {
            var captured = code;
            var button = new Button
            {
                Text = label,
                Style = (Style)Application.Current!.Resources["Suite.SmallButton"],
                Opacity = captured == current ? 1.0 : 0.65,
            }; // A pressed look for the active language.
            if (captured == current)
                button.FontAttributes = FontAttributes.Bold;
            button.Clicked += (_, _) =>
            {
                Loc.Instance.Culture = captured;
                SaveCulture(captured);
                BuildLanguageRow(); // re-render pressed state (also runs via CultureChanged)
            };
            LanguageRow.Children.Add(button);
        }
    }

    // ------------------------------------------------------------ recents

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RenderRecents(); // refresh after coming back from a module
    }

    private void RenderRecents()
    {
        RecentRow.Children.Clear();

        var all = _store.LoadRecents();
        var recents = all
            .Where(r => !string.IsNullOrEmpty(r.Path) && File.Exists(r.Path))
            .ToList();

        // Prune entries whose files vanished so the list stays honest.
        if (recents.Count != all.Count)
            _store.SaveRecents(recents);

        RecentSection.IsVisible = recents.Count > 0;
        RecentHeader.Text = recents.Count == 1
            ? Loc.T("Start.RecentSingle")
            : Loc.Format("Start.Recents", recents.Count);

        foreach (var entry in recents)
        {
            var captured = entry;
            var button = new Button
            {
                Text = $"{entry.Title}  ·  {entry.Kind.For()}",
                Style = (Style)Application.Current!.Resources["Suite.SmallButton"],
                HorizontalOptions = LayoutOptions.Start,
            };
            button.Clicked += async (_, _) => await OpenRecentAsync(captured);
            RecentRow.Children.Add(button);
        }
    }

    private async Task OpenRecentAsync(RecentDocumentEntry entry)
    {
        if (!File.Exists(entry.Path))
        {
            PruneRecent(entry);
            return;
        }

        var vm = entry.Kind switch
        {
            OricFileKind.Author => (OricDocumentViewModelBase)_services.GetRequiredService<AuthorViewModel>(),
            OricFileKind.Calc => _services.GetRequiredService<CalcViewModel>(),
            OricFileKind.Base => _services.GetRequiredService<BaseViewModel>(),
            _ => null,
        };
        if (vm is null) return;

        if (!vm.TryOpenRecent(entry.Path))
        {
            PruneRecent(entry);
            return;
        }

        Page page = entry.Kind switch
        {
            OricFileKind.Author => _services.GetRequiredService<AuthorPage>(),
            OricFileKind.Calc => _services.GetRequiredService<CalcPage>(),
            _ => _services.GetRequiredService<BasePage>(),
        };
        await Navigation.PushAsync(page);
    }

    private void PruneRecent(RecentDocumentEntry entry)
    {
        var recents = _store.LoadRecents();
        recents.RemoveAll(r => string.Equals(r.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        _store.SaveRecents(recents);
        RenderRecents();
    }

    // ------------------------------------------------------------ picker tiles

    private void Open<TPage>() where TPage : Page
        => _ = Navigation.PushAsync(_services.GetRequiredService<TPage>());

    private void OnOpenAuthor(object? sender, TappedEventArgs e) => Open<AuthorPage>();

    private void OnOpenCalc(object? sender, TappedEventArgs e) => Open<CalcPage>();

    private void OnOpenBase(object? sender, TappedEventArgs e) => Open<BasePage>();
}
