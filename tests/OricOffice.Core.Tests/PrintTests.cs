using System.Text;
using OricOffice.Core.Model;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.ViewModels;

namespace OricOffice.Core.Tests;

public class PrintTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeLifetime _lifetime = new();
    private readonly OricFileStore _store;
    private readonly FakePrinter _printer = new();

    public PrintTests() => _store = new OricFileStore(_dir.Path); // rooted: drafts/recents stay in the test dir

    public void Dispose() => _dir.Dispose();

    private static string PdfHeader(byte[] bytes)
        => Encoding.ASCII.GetString(bytes, 0, Math.Min(4, bytes.Length));

    private AuthorViewModel CreateAuthor()
        => new(_dialogs, _lifetime, new FakeClipboard(), _store,
            new AppMeta("Oric Author", "1.0.0", OricFileKind.Author), _printer);

    private CalcViewModel CreateCalc()
        => new(_dialogs, _lifetime, _store,
            new AppMeta("Oric Calc", "1.0.0", OricFileKind.Calc), _printer);

    private BaseViewModel CreateBase()
        => new(_dialogs, _lifetime, _store,
            new AppMeta("Oric Base", "1.0.0", OricFileKind.Base), _printer);

    // ------------------------------------------------------------ print to printer

    [Fact]
    public async Task AuthorPrint_SendsPdfToPrinter()
    {
        var vm = CreateAuthor();
        vm.Body = "Report text for printing";

        await vm.PrintCommand.ExecuteAsync(null);

        Assert.NotNull(_printer.LastPdf);
        Assert.Equal("%PDF", PdfHeader(_printer.LastPdf!));
        Assert.Equal("Sent to printer", vm.StatusText);
        Assert.Empty(_dialogs.Messages); // success is silent
    }

    [Fact]
    public async Task CalcPrint_SendsPdfToPrinter()
    {
        var vm = CreateCalc();
        vm.FormulaBarText = "=SUM(A1:A2)";

        await vm.PrintCommand.ExecuteAsync(null);

        Assert.NotNull(_printer.LastPdf);
        Assert.Equal("%PDF", PdfHeader(_printer.LastPdf!));
        Assert.Equal("Sent to printer", vm.StatusText);
    }

    [Fact]
    public async Task BasePrint_SendsPdfToPrinter()
    {
        var vm = CreateBase();

        await vm.PrintCommand.ExecuteAsync(null);

        Assert.NotNull(_printer.LastPdf);
        Assert.Equal("%PDF", PdfHeader(_printer.LastPdf!));
        Assert.Equal("Sent to printer", vm.StatusText);
    }

    [Fact]
    public async Task Print_WithoutPrinter_ShowsGuidanceMessage()
    {
        var vm = new AuthorViewModel(_dialogs, _lifetime, new FakeClipboard(), _store,
            new AppMeta("Oric Author", "1.0.0", OricFileKind.Author));

        await vm.PrintCommand.ExecuteAsync(null);

        Assert.Null(_printer.LastPdf);
        Assert.Contains(_dialogs.Messages, m => m.Title == "Printing");
    }

    [Fact]
    public async Task Print_WhenPrinterFails_ReportsProblem()
    {
        _printer.Problem = "No printer is configured.";
        var vm = CreateAuthor();

        await vm.PrintCommand.ExecuteAsync(null);

        Assert.Equal("Printing unavailable", vm.StatusText);
        Assert.Contains(_dialogs.Messages, m => m.Message.Contains("No printer"));
    }

    // ------------------------------------------------------------ print to file

    [Fact]
    public async Task AuthorPrintToFile_WritesPdfToChosenPath()
    {
        var vm = CreateAuthor();
        vm.Body = "Save this as a printed file";
        var target = _dir.File("printed.pdf");
        _dialogs.SavePath = target;

        await vm.PrintToFileCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target));
        Assert.Equal("%PDF", PdfHeader(File.ReadAllBytes(target)));
        Assert.Contains("Printed to file", vm.StatusText);
        Assert.Null(_printer.LastPdf); // nothing went to the printer
    }

    [Fact]
    public async Task CalcPrintToFile_WritesPdfToChosenPath()
    {
        var vm = CreateCalc();
        var target = _dir.File("calc-print.pdf");
        _dialogs.SavePath = target;

        await vm.PrintToFileCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target));
        Assert.Equal("%PDF", PdfHeader(File.ReadAllBytes(target)));
        Assert.Contains("Printed to file", vm.StatusText);
    }

    [Fact]
    public async Task BasePrintToFile_WritesPdfToChosenPath()
    {
        var vm = CreateBase();
        var target = _dir.File("base-print.pdf");
        _dialogs.SavePath = target;

        await vm.PrintToFileCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target));
        Assert.Equal("%PDF", PdfHeader(File.ReadAllBytes(target)));
        Assert.Contains("Printed to file", vm.StatusText);
    }

    [Fact]
    public async Task PrintToFile_Cancelled_DoesNothing()
    {
        var vm = CreateAuthor();
        vm.Body = "no save";
        _dialogs.SavePath = null;

        await vm.PrintToFileCommand.ExecuteAsync(null);

        Assert.Equal(0, Directory.GetFiles(_dir.Path).Length);
        Assert.Null(_printer.LastPdf);
    }
}
