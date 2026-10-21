using System.Diagnostics;
using OricOffice.Core.Services;

namespace OricOffice.MauiShared.Services;

/// <summary>
/// Sends a rendered PDF to a printer through the Windows shell print verb.
/// When the platform has no print association for PDFs (common when a browser
/// is the PDF handler), the file is opened instead so the user can print from
/// their viewer — and a message explains what happened.
/// </summary>
public sealed class MauiPrintService : IPrintService
{
    public async Task<string?> PrintPdfAsync(byte[] pdf, string documentName)
    {
        var name = string.IsNullOrWhiteSpace(documentName) ? "Document" : documentName.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        var path = Path.Combine(Path.GetTempPath(), name + ".pdf");
        await File.WriteAllBytesAsync(path, pdf);

#if WINDOWS
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path)
            {
                Verb = "print",
                UseShellExecute = true,
            });
            // The shell hands the job to the registered print handler; we do not
            // wait for it — some handlers stay open after spooling.
            return process is null
                ? "No application accepted the print job. The document was saved to:\n" + path +
                  "\nOpen it and print from your viewer."
                : null;
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return "Your PDF handler does not expose a direct print action, so the " +
                       "document was opened instead — use its Print command. A copy is at:\n" + path;
            }
            catch
            {
                return "Printing is not available right now. The document was saved to:\n" + path;
            }
        }
#else
        await Task.CompletedTask;
        return "Printing is not available on this platform yet — use Export to PDF instead.";
#endif
    }
}
