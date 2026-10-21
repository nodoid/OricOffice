using OricOffice.Core.Services;

namespace OricOffice.Core.Tests;

internal sealed class FakeDialogs : IDialogService
{
    public string? OpenPath { get; set; }
    public string? SavePath { get; set; }
    public bool ConfirmResult { get; set; } = true;

    /// <summary>Queued answers for successive confirms; falls back to <see cref="ConfirmResult"/>.</summary>
    public Queue<bool> ConfirmResults { get; } = new();
    public string? PromptResult { get; set; }

    public List<(string Title, string Message)> Messages { get; } = new();
    public int ConfirmCount { get; private set; }

    public Task<string?> PickOpenPathAsync(string title, params string[] extensions)
        => Task.FromResult(OpenPath);

    public Task<string?> PickSavePathAsync(string title, string suggestedFileName, string extension)
        => Task.FromResult(SavePath);

    public Task MessageAsync(string title, string message)
    {
        Messages.Add((title, message));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
    {
        ConfirmCount++;
        return Task.FromResult(ConfirmResults.Count > 0 ? ConfirmResults.Dequeue() : ConfirmResult);
    }

    public Task<string?> PromptAsync(string title, string message, string? initialValue = null)
        => Task.FromResult(PromptResult);
}

internal sealed class FakeClipboard : ITextClipboard
{
    public string? Text { get; set; }

    public Task<string?> GetTextAsync() => Task.FromResult(Text);

    public Task SetTextAsync(string? text)
    {
        Text = text;
        return Task.CompletedTask;
    }
}

internal sealed class FakeLifetime : IAppLifetimeService
{
    public bool Exited { get; private set; }

    public void Exit() => Exited = true;
}

internal sealed class FakePrinter : IPrintService
{
    public byte[]? LastPdf { get; private set; }

    public string? LastName { get; private set; }

    /// <summary>When set, returned as the "problem" report instead of success.</summary>
    public string? Problem { get; set; }

    public Task<string?> PrintPdfAsync(byte[] pdf, string documentName)
    {
        LastPdf = pdf;
        LastName = documentName;
        return Task.FromResult(Problem);
    }
}
