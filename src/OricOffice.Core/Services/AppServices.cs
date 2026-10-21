namespace OricOffice.Core.Services;

/// <summary>Per-app identity, supplied by each MAUI head at startup.</summary>
public sealed record AppMeta(string Name, string Version, OricFileKind Kind);

/// <summary>
/// UI services the view models need. Implemented in the MAUI shared library
/// (file pickers, alerts, prompts, quit) so the core stays testable.
/// </summary>
public interface IDialogService
{
    /// <summary>Returns the picked file path, or null when cancelled.</summary>
    Task<string?> PickOpenPathAsync(string title, params string[] extensions);

    /// <summary>Returns a writable path for the given extension, or null when cancelled.</summary>
    Task<string?> PickSavePathAsync(string title, string suggestedFileName, string extension);

    Task MessageAsync(string title, string message);

    /// <summary>Returns true when the user accepts (the "accept" button).</summary>
    Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel");

    /// <summary>Returns the entered text, or null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string? initialValue = null);
}

/// <summary>Application lifetime (quit), kept out of the core so it stays MAUI-free.</summary>
public interface IAppLifetimeService
{
    void Exit();
}

/// <summary>Clipboard access, implemented with MAUI Essentials in the shared library.</summary>
public interface ITextClipboard
{
    Task<string?> GetTextAsync();
    Task SetTextAsync(string? text);
}

/// <summary>
/// Sends a rendered document to a printer. Implemented by the MAUI head;
/// "print to file" is handled by the export commands instead (PDF).
/// </summary>
public interface IPrintService
{
    /// <summary>
    /// Prints the given PDF. Returns null on success, or a message explaining
    /// why printing could not be completed (shown to the user by the caller).
    /// </summary>
    Task<string?> PrintPdfAsync(byte[] pdf, string documentName);
}
