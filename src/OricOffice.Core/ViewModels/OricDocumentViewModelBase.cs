using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private string statusText = "Ready";

    protected OricDocumentViewModelBase(
        IDialogService dialogs,
        IAppLifetimeService lifetime,
        OricFileStore store,
        AppMeta meta)
    {
        _dialogs = dialogs;
        _lifetime = lifetime;
        _store = store;
        Meta = meta;
        Doc = OricFile.CreateNew(meta.Kind);
    }

    public AppMeta Meta { get; }

    public OricFileKind Kind => Meta.Kind;

    public string Extension => Meta.Kind.For();

    public bool HasPath => FilePath is not null;

    /// <summary>The current document (all three sections).</summary>
    public OricFile Doc { get; protected set; }

    protected IDialogService Dialogs => _dialogs;
    protected OricFileStore Store => _store;

    /// <summary>Lazily built so derived view models are fully constructed first.</summary>
    public ObservableCollection<MenuGroup> Menus
    {
        get
        {
            if (_menus is null)
            {
                _menus = new ObservableCollection<MenuGroup>();
                foreach (var group in BuildMenus()) _menus.Add(group);
            }
            return _menus;
        }
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

    // ------------------------------------------------------------ file commands

    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync()) return;

        Doc = OricFile.CreateNew(Kind);
        RunSilently(ResetDocument);
        FilePath = null;
        Title = "Untitled";
        IsDirty = false;
        StatusText = "New file";
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;

        var path = await _dialogs.PickOpenPathAsync($"Open {Meta.Name} file", OricFileExtensions.All.ToArray());
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
            StatusText = $"Opened {Path.GetFileName(path)}";
        }
        catch (OricFileException ex)
        {
            await _dialogs.MessageAsync("Cannot open file", ex.Message);
        }
        catch (Exception ex)
        {
            await _dialogs.MessageAsync("Cannot open file", $"Something went wrong reading that file.\n\n{ex.Message}");
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

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        _lifetime.Exit();
    }

    [RelayCommand]
    private async Task AboutAsync()
    {
        await _dialogs.MessageAsync(
            $"About {Meta.Name}",
            $"{Meta.Name} {Meta.Version}\n\n" +
            $"A modern port of Tansoft's Oric program, part of the Oric Suite.\n" +
            $"Files use the {Extension} extension (SQLite) and can be opened by " +
            $"Oric Author, Oric Calc and Oric Base alike.");
    }

    // ------------------------------------------------------------ helpers

    private async Task SaveAsCoreAsync()
    {
        var suggested = OricFileStore.SuggestedFileName(Title, Kind);
        var path = await _dialogs.PickSavePathAsync($"Save {Meta.Name} file", suggested, Extension);
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
            StatusText = $"Saved {Path.GetFileName(finalPath)}";
        }
        catch (Exception ex)
        {
            await _dialogs.MessageAsync("Cannot save file", ex.Message);
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty) return true;
        return await _dialogs.ConfirmAsync(
            "Unsaved changes",
            $"\"{Title}\" has unsaved changes. Discard them?",
            accept: "Discard",
            cancel: "Keep editing");
    }
}
