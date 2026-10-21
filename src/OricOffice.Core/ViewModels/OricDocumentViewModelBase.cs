using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OricOffice.Core.Localization;
using OricOffice.Core.Model;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.Ui;

namespace OricOffice.Core.ViewModels;

/// <summary>
/// Shared MVVM plumbing for the three apps: file lifecycle commands (New / Open /
/// Save / Save As / Exit / About), dirty tracking and the menu/toolbar models.
/// Derived view models implement the product-specific sections.
/// </summary>
public abstract partial class OricDocumentViewModelBase : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IAppLifetimeService _lifetime;
    private readonly OricFileStore _store;
    private readonly IPrintService? _printer;

    private ObservableCollection<MenuGroup>? _menus;
    private ObservableCollection<ToolButtonModel>? _toolbar;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPath))]
    private string? filePath;

    [ObservableProperty]
    private string title = "Untitled";

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private string statusText = Loc.T("Status.Ready");

    protected OricDocumentViewModelBase(
        IDialogService dialogs,
        IAppLifetimeService lifetime,
        OricFileStore store,
        AppMeta meta,
        IPrintService? printer = null)
    {
        _dialogs = dialogs;
        _lifetime = lifetime;
        _store = store;
        _printer = printer;
        Meta = meta;
        Doc = OricFile.CreateNew(meta.Kind);
        InitialDocument = Doc;
        Loc.Instance.CultureChanged += OnCultureChanged;
    }

    /// <summary>Rebuilds the menu/toolbar chrome and derived text after a culture change.</summary>
    private void OnCultureChanged()
    {
        if (_menus is not null)
        {
            _menus.Clear();
            foreach (var group in ComposeMenus()) _menus.Add(group);
        }
        if (_toolbar is not null)
        {
            _toolbar.Clear();
            foreach (var button in BuildToolbar()) _toolbar.Add(button);
        }
        OnPropertyChanged(nameof(Menus));
        OnPropertyChanged(nameof(Toolbar));
        RefreshLocalizedText();
    }

    /// <summary>Derived view models re-render culture-dependent status text here.</summary>
    protected virtual void RefreshLocalizedText()
    {
    }

    /// <summary>The document as constructed; draft restore only runs while this is still current.</summary>
    protected OricFile InitialDocument { get; }

    public AppMeta Meta { get; }

    public OricFileKind Kind => Meta.Kind;

    public string Extension => Meta.Kind.For();

    public bool HasPath => FilePath is not null;

    /// <summary>The current document (all three sections).</summary>
    public OricFile Doc { get; protected set; }

    protected IDialogService Dialogs => _dialogs;
    protected OricFileStore Store => _store;
    protected IPrintService? Printer => _printer;

    /// <summary>Lazily built so derived view models are fully constructed first.</summary>
    public ObservableCollection<MenuGroup> Menus
    {
        get
        {
            if (_menus is null)
            {
                _menus = new ObservableCollection<MenuGroup>();
                foreach (var group in ComposeMenus()) _menus.Add(group);
            }
            return _menus;
        }
    }

    /// <summary>
    /// The Home group (a way back to the start center) followed by the
    /// derived menus. Composed here — not by the hosting pages — so a culture
    /// change rebuilds it along with everything else.
    /// </summary>
    private IEnumerable<MenuGroup> ComposeMenus()
    {
        yield return new MenuGroup(Loc.T("Menu.Home"),
            new MenuItemModel(Loc.T("Item.BackToStart"), action: () =>
            {
                if (ReturnToStart is { } back) _ = back();
            }));

        foreach (var group in BuildMenus()) yield return group;
    }

    public ObservableCollection<ToolButtonModel> Toolbar
    {
        get
        {
            if (_toolbar is null)
            {
                _toolbar = new ObservableCollection<ToolButtonModel>();
                foreach (var button in BuildToolbar()) _toolbar.Add(button);
            }
            return _toolbar;
        }
    }

    // ------------------------------------------------------------ chrome model

    protected abstract IEnumerable<MenuGroup> BuildMenus();

    protected abstract IEnumerable<ToolButtonModel> BuildToolbar();

    // ------------------------------------------------------------ document hooks

    /// <summary>Loads the (possibly seeded) document into the UI state.</summary>
    protected abstract void ApplyDocument();

    /// <summary>Resets the UI state to a brand-new blank document.</summary>
    protected abstract void ResetDocument();

    /// <summary>Copies the UI state back into <see cref="Doc"/> before saving.</summary>
    protected abstract void CollectDocument();

    // ------------------------------------------------------------ dirty tracking

    private bool _suppressDirty;

    protected void MarkDirty()
    {
        if (!_suppressDirty) IsDirty = true;
    }

    private void RunSilently(Action action)
    {
        _suppressDirty = true;
        try { action(); }
        finally { _suppressDirty = false; }
    }

    /// <summary>Copies UI state into <see cref="Doc"/> for rendering (export/print) without marking dirty.</summary>
    protected void CollectForOutput() => RunSilently(CollectDocument);

    // ------------------------------------------------------------ file commands

    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        ResetToNewDocument();
        StatusText = Loc.T("Status.NewFile");
    }

    private void ResetToNewDocument()
    {
        Doc = OricFile.CreateNew(Kind);
        RunSilently(ResetDocument);
        FilePath = null;
        Title = "Untitled"; // file titles stay literal (they become file names)
        IsDirty = false;
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;

        var path = await _dialogs.PickOpenPathAsync(Loc.Format("Dialog.OpenFile", Meta.Name), OricFileExtensions.All.ToArray());
        if (path is null) return;

        try
        {
            var doc = _store.Load(path);
            RunSilently(() =>
            {
                Doc = doc;
                ApplyDocument();
            });
            FilePath = path;
            Title = string.IsNullOrWhiteSpace(doc.Title) ? Path.GetFileNameWithoutExtension(path) : doc.Title;
            IsDirty = false;
            StatusText = Loc.Format("Status.Opened", Path.GetFileName(path));
            DeleteDraft();
            RecordRecent();
        }
        catch (OricFileException ex)
        {
            await _dialogs.MessageAsync(Loc.T("Dialog.CannotOpen"), ex.Message);
        }
        catch (Exception ex)
        {
            await _dialogs.MessageAsync(Loc.T("Dialog.CannotOpen"), Loc.Format("Dialog.CannotOpen.Body", ex.Message));
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (FilePath is null)
        {
            await SaveAsCoreAsync();
            return;
        }
        await SaveToAsync(FilePath);
    }

    [RelayCommand]
    private async Task SaveAsAsync() => await SaveAsCoreAsync();

    /// <summary>
    /// Set by the hosting page: leaves this module and returns to the start
    /// view (the three-choice picker), where the app itself can be exited.
    /// Hosts without navigation (unit tests, headless) fall back to quitting
    /// the process through <see cref="IAppLifetimeService.Exit"/>.
    /// </summary>
    public Func<Task>? ReturnToStart { get; set; }

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (!await ConfirmDiscardAsync()) return;

        // Offer to keep an unsaved working copy BEFORE resetting the document.
        await OfferDraftAsync();
        ResetToNewDocument();

        if (ReturnToStart is not null)
            await ReturnToStart();
        else
            _lifetime.Exit();
    }

    // ------------------------------------------------------------ drafts + recents

    /// <summary>
    /// Called by the host when the module is entered (first construction or
    /// re-entry): restores an unsaved draft left by a previous session and
    /// records the document in the recents list.
    /// </summary>
    public void OnModuleEntered()
    {
        RestoreDraft();
        RecordRecent();
    }

    /// <summary>Best-effort: an unreadable draft is deleted rather than blocking startup.</summary>
    private void RestoreDraft()
    {
        if (HasPath || IsDirty || !ReferenceEquals(Doc, InitialDocument)) return;

        var path = _store.DraftPath(Kind);
        if (!File.Exists(path)) return;
        try
        {
            var doc = _store.Load(path);
            RunSilently(() =>
            {
                Doc = doc;
                ApplyDocument();
            });
            Title = string.IsNullOrWhiteSpace(doc.Title) ? "Untitled" : doc.Title;
            IsDirty = false;
            StatusText = Loc.Format("Status.RestoredDraft", Title);
        }
        catch
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    private async Task OfferDraftAsync()
    {
        if (HasPath || !IsDirty) return;
        var keep = await _dialogs.ConfirmAsync(
            Loc.T("Dialog.Unsaved"),
            Loc.Format("Dialog.Draft.Body", Title),
            accept: Loc.T("Dialog.KeepDraft"),
            cancel: Loc.T("Dialog.Discard"));
        if (keep) SaveDraftCore();
        else DeleteDraft();
    }

    private void SaveDraftCore()
    {
        try
        {
            RunSilently(CollectDocument);
            _store.Save(Doc, _store.DraftPath(Kind), Kind);
        }
        catch
        {
            // Drafts are best-effort: never block leaving the module.
        }
    }

    private void DeleteDraft()
    {
        try { _store.DeleteDraft(Kind); } catch { /* best effort */ }
    }

    /// <summary>Re-opens a recent path (including a draft path) into this module.</summary>
    public bool TryOpenRecent(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var doc = _store.Load(path);
            RunSilently(() =>
            {
                Doc = doc;
                ApplyDocument();
            });

            var isDraft = string.Equals(Path.GetFullPath(path),
                Path.GetFullPath(_store.DraftPath(Kind)), StringComparison.OrdinalIgnoreCase);
            FilePath = isDraft ? null : path;
            Title = string.IsNullOrWhiteSpace(doc.Title)
                ? (isDraft ? "Untitled" : Path.GetFileNameWithoutExtension(path))
                : doc.Title;
            IsDirty = false;
            StatusText = Loc.Format("Status.Opened", Path.GetFileName(path));
            RecordRecent();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Pushes the current document's path to the front of the recents list.</summary>
    private void RecordRecent()
    {
        var draftPath = _store.DraftPath(Kind);
        var path = FilePath ?? (File.Exists(draftPath) ? draftPath : null);
        if (path is null) return;

        var recents = _store.LoadRecents();
        recents.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        recents.Insert(0, new RecentDocumentEntry
        {
            Path = path,
            Title = Title,
            Kind = Kind,
            LastOpenedUtc = DateTime.UtcNow,
        });
        if (recents.Count > 8) recents.RemoveRange(8, recents.Count - 8);
        _store.SaveRecents(recents);
    }

    [RelayCommand]
    private async Task AboutAsync()
    {
        await _dialogs.MessageAsync(
            Loc.Format("Item.About", Meta.Name),
            Loc.Format("About.Body", Meta.Name, Meta.Version, Extension));
    }

    // ------------------------------------------------------------ helpers

    private async Task SaveAsCoreAsync()
    {
        var suggested = OricFileStore.SuggestedFileName(Title, Kind);
        var path = await _dialogs.PickSavePathAsync(Loc.Format("Dialog.SaveFile", Meta.Name), suggested, Extension);
        if (path is null) return;
        await SaveToAsync(path);
    }

    private async Task SaveToAsync(string path)
    {
        try
        {
            RunSilently(CollectDocument);
            var finalPath = _store.Save(Doc, path, Kind);
            FilePath = finalPath;
            Title = Path.GetFileNameWithoutExtension(finalPath);
            IsDirty = false;
            StatusText = Loc.Format("Status.Saved", Path.GetFileName(finalPath));
            DeleteDraft();
            RecordRecent();
        }
        catch (Exception ex)
        {
            await _dialogs.MessageAsync(Loc.T("Dialog.CannotSave"), ex.Message);
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty) return true;
        return await _dialogs.ConfirmAsync(
            Loc.T("Dialog.Unsaved"),
            Loc.Format("Dialog.Unsaved.Body", Title),
            accept: Loc.T("Dialog.Discard"),
            cancel: Loc.T("Dialog.KeepEditing"));
    }

    // ------------------------------------------------------------ export / print

    /// <summary>
    /// Collects the document, asks for a destination and runs <paramref name="write"/>
    /// to produce the exported file. <paramref name="successStatusKey"/> names the
    /// status-bar key to show ({0} receives the file name).
    /// </summary>
    protected async Task ExportAsync(string suggestedFileName, Action<string> write, string successStatusKey)
    {
        var path = await _dialogs.PickSavePathAsync(Loc.T("Dialog.Export"), suggestedFileName,
            Path.GetExtension(suggestedFileName));
        if (path is null) return;
        try
        {
            RunSilently(CollectDocument);
            write(path);
            StatusText = Loc.Format(successStatusKey, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            await _dialogs.MessageAsync(Loc.T("Dialog.ExportFailed"), ex.Message);
        }
    }

    /// <summary>
    /// Asks for a foreign file (LibreOffice…), imports it into a fresh document
    /// and shows the result. The unsaved-changes guard runs first.
    /// </summary>
    protected async Task ImportExternalAsync(string dialogTitle, string extension, Func<string, OricFile> import)
    {
        if (!await ConfirmDiscardAsync()) return;

        var path = await _dialogs.PickOpenPathAsync(dialogTitle, extension);
        if (path is null) return;

        try
        {
            var doc = import(path);
            RunSilently(() =>
            {
                Doc = doc;
                ApplyDocument();
            });
            FilePath = null;
            Title = string.IsNullOrWhiteSpace(doc.Title)
                ? Path.GetFileNameWithoutExtension(path)
                : doc.Title;
            IsDirty = false;
            StatusText = Loc.Format("Status.Imported", Path.GetFileName(path));
            DeleteDraft();
        }
        catch (Exception ex)
        {
            await _dialogs.MessageAsync(Loc.T("Dialog.ImportFailed"), ex.Message);
        }
    }

    /// <summary>Collects the document, renders a PDF and sends it to the printer.</summary>
    protected async Task SendToPrinterAsync(byte[] pdf)
    {
        if (_printer is null)
        {
            await _dialogs.MessageAsync(Loc.T("Dialog.Printing"),
                Loc.T("Dialog.Printing.Unavailable"));
            return;
        }

        var problem = await _printer.PrintPdfAsync(pdf, Title);
        StatusText = problem is null ? Loc.T("Status.SentToPrinter") : Loc.T("Status.PrintingUnavailable");
        if (problem is not null)
            await _dialogs.MessageAsync(Loc.T("Dialog.Printing"), problem);
    }
}
