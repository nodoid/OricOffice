using OricOffice.Core.Services;

namespace OricOffice.MauiShared.Services;

/// <summary>Quits the application where the platform allows it.</summary>
public sealed class MauiAppLifetimeService : IAppLifetimeService
{
    public void Exit() => Application.Current?.Quit();
}

/// <summary>Clipboard via MAUI Essentials.</summary>
public sealed class MauiTextClipboard : ITextClipboard
{
    public Task<string?> GetTextAsync() => Clipboard.Default.GetTextAsync();

    public Task SetTextAsync(string? text)
        => Clipboard.Default.SetTextAsync(text ?? string.Empty);
}

/// <summary>
/// File pickers, alerts and prompts.
///
/// Opening uses the platform document picker on every OS. Saving uses the native
/// Windows "Save as" dialog on Windows (desktop paradigm); on the mobile-style
/// platforms (Android/iOS/macCatalyst — which have no save dialogs) the file is
/// written to the app's Documents folder via a name prompt, with the full path
/// shown in the status bar afterwards.
/// </summary>
public sealed class MauiDialogService : IDialogService
{
    private static Page? CurrentPage => Application.Current?.Windows.FirstOrDefault()?.Page;

    public async Task<string?> PickOpenPathAsync(string title, params string[] extensions)
    {
        // No file-type filter: keeping the picker portable across platforms.
        // Non-suite files are rejected with a friendly message by OricFileStore.Load.
        var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = title });
        return result?.FullPath;
    }

    public Task<string?> PickSavePathAsync(string title, string suggestedFileName, string extension)
    {
#if WINDOWS
        return PickSavePathWindowsAsync(suggestedFileName, extension);
#else
        return PickSavePathMobileAsync(title, suggestedFileName, extension);
#endif
    }

    public async Task MessageAsync(string title, string message)
    {
        if (CurrentPage is { } page)
            await page.DisplayAlertAsync(title, message, "OK");
    }

    public async Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
    {
        if (CurrentPage is not { } page) return true;
        return await page.DisplayAlertAsync(title, message, accept, cancel);
    }

    public async Task<string?> PromptAsync(string title, string message, string? initialValue = null)
    {
        if (CurrentPage is not { } page) return null;
        return await page.DisplayPromptAsync(title, message,
            accept: "OK", cancel: "Cancel", initialValue: initialValue ?? string.Empty);
    }

#if WINDOWS
    private static async Task<string?> PickSavePathWindowsAsync(string suggestedFileName, string extension)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();

            // WinUI 3 pickers need an explicit owner window handle.
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is { } platformView)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(platformView);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            var label = extension.TrimStart('.').ToUpperInvariant() + " files";
            picker.FileTypeChoices.Add(label, new List<string> { extension });
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName);

            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }
        catch
        {
            // Fall back to the app documents folder if the dialog cannot be shown.
            return SaveToAppDocuments(suggestedFileName, extension);
        }
    }
#endif

#if !WINDOWS
    private static async Task<string?> PickSavePathMobileAsync(
        string title, string suggestedFileName, string extension)
    {
        if (CurrentPage is not { } page) return null;

        var suggested = Path.GetFileNameWithoutExtension(suggestedFileName);
        var answer = await page.DisplayPromptAsync(
            title,
            $"File name (saved in the app Documents folder with the {extension} extension):",
            accept: "Save",
            cancel: "Cancel",
            initialValue: suggested);
        if (string.IsNullOrWhiteSpace(answer)) return null;

        var name = answer.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            name += extension;

        return SaveToAppDocuments(name, extension);
    }
#endif

    private static string SaveToAppDocuments(string fileName, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(name)) name = "Untitled";
        if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            name += extension;

        var directory = Path.Combine(FileSystem.AppDataDirectory, "Documents");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name);
    }
}
