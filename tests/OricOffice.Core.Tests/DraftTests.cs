using OricOffice.Core.Model;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.ViewModels;

namespace OricOffice.Core.Tests;

public class DraftTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeLifetime _lifetime = new();
    private readonly FakeClipboard _clipboard = new();
    private readonly OricFileStore _store;

    public DraftTests() => _store = new OricFileStore(_dir.Path);

    public void Dispose() => _dir.Dispose();

    private AuthorViewModel Create()
        => new(_dialogs, _lifetime, _clipboard, _store,
            new AppMeta("Oric Author", "1.0.0", OricFileKind.Author));

    // ------------------------------------------------------------ draft lifecycle

    [Fact]
    public async Task Exit_KeepingDraft_RestoresIntoAFreshInstance()
    {
        var vm = Create();
        vm.Body = "half-written chapter";
        _dialogs.ConfirmResult = true; // discard prompt + keep-draft prompt

        await vm.ExitCommand.ExecuteAsync(null);

        Assert.True(_store.DraftExists(OricFileKind.Author));
        Assert.False(vm.IsDirty);            // module left clean…
        Assert.Equal(string.Empty, vm.Body); // …with a fresh document

        // "Next process": a brand-new view model over the same store.
        var restored = Create();
        restored.OnModuleEntered();

        Assert.Equal("half-written chapter", restored.Body);
        Assert.False(restored.IsDirty);
        Assert.Null(restored.FilePath); // still an unsaved draft, not a real file
        Assert.Equal("Restored draft of Untitled", restored.StatusText);
    }

    [Fact]
    public async Task Exit_Discarding_DeletesAPreviousDraft()
    {
        // Create a draft first.
        var vm = Create();
        vm.Body = "first draft";
        _dialogs.ConfirmResult = true;
        await vm.ExitCommand.ExecuteAsync(null);
        Assert.True(_store.DraftExists(OricFileKind.Author));

        // Restore it, change it, then decline keeping a new draft.
        var restored = Create();
        restored.OnModuleEntered();
        restored.Body = restored.Body + " plus edits";

        _dialogs.ConfirmResults.Enqueue(true);  // discard unsaved changes…
        _dialogs.ConfirmResults.Enqueue(false); // …and do NOT keep a draft
        await restored.ExitCommand.ExecuteAsync(null);

        Assert.False(_store.DraftExists(OricFileKind.Author));
    }

    [Fact]
    public async Task Exit_OnCleanDocument_WritesNoDraft()
    {
        var vm = Create();
        await vm.ExitCommand.ExecuteAsync(null); // not dirty: only the discard prompt

        Assert.False(_store.DraftExists(OricFileKind.Author));
        Assert.True(_lifetime.Exited);
    }

    [Fact]
    public async Task RealSave_RemovesTheDraft()
    {
        var vm = Create();
        vm.Body = "save me for real";
        _dialogs.ConfirmResult = true;
        await vm.ExitCommand.ExecuteAsync(null);
        Assert.True(_store.DraftExists(OricFileKind.Author));

        var restored = Create();
        restored.OnModuleEntered();
        var target = $"{_dir.Path}/chapter{OricFileKind.Author.For()}";
        _dialogs.SavePath = target;

        await restored.SaveCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target));
        Assert.False(_store.DraftExists(OricFileKind.Author));
        Assert.Equal("save me for real", restored.Body);
    }

    // ------------------------------------------------------------ recents

    [Fact]
    public async Task Saving_RecordsTheFileInRecents_MostRecentFirstAndCapped()
    {
        var vm = Create();

        for (int i = 1; i <= 9; i++)
        {
            _dialogs.SavePath = _dir.File($"doc{i}{OricFileKind.Author.For()}");
            await vm.SaveAsCommand.ExecuteAsync(null);
        }

        var recents = _store.LoadRecents();
        Assert.Equal(8, recents.Count); // capped
        Assert.Equal(Path.GetFullPath(_dir.File($"doc9{OricFileKind.Author.For()}")),
            Path.GetFullPath(recents[0].Path)); // newest first
        Assert.Equal(OricFileKind.Author, recents[0].Kind);
        Assert.All(recents, r => Assert.True(File.Exists(r.Path)));
    }

    [Fact]
    public async Task TryOpenRecent_ReopensASavedFileIntoAFreshViewModel()
    {
        var vm = Create();
        vm.Body = "reopenable content";
        var file = _dir.File($"reopen{OricFileKind.Author.For()}");
        _dialogs.SavePath = file;
        await vm.SaveAsCommand.ExecuteAsync(null);

        var other = Create();
        Assert.True(other.TryOpenRecent(file));

        Assert.Equal("reopenable content", other.Body);
        Assert.Equal(Path.GetFullPath(file), Path.GetFullPath(other.FilePath!));
        Assert.False(other.IsDirty);
    }

    [Fact]
    public void TryOpenRecent_MissingFile_ReturnsFalse()
    {
        var vm = Create();
        Assert.False(vm.TryOpenRecent(_dir.File($"does-not-exist{OricFileKind.Author.For()}")));
    }

    [Fact]
    public async Task Recents_SurviveAcrossStoreInstances()
    {
        var vm = Create();
        var saved = _dir.File($"persisted{OricFileKind.Author.For()}");
        _dialogs.SavePath = saved;
        await vm.SaveAsCommand.ExecuteAsync(null);

        var freshStore = new OricFileStore(_dir.Path); // same root, new instance
        var recents = freshStore.LoadRecents();

        Assert.Contains(recents, r =>
            string.Equals(r.Path, saved, StringComparison.OrdinalIgnoreCase));
    }
}
