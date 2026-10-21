using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OricOffice.Core;
using OricOffice.Core.Services;
using OricOffice.Core.Storage;
using OricOffice.Core.ViewModels;
using OricOffice.MauiShared.Services;

namespace OricOffice;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if WINDOWS
        // The Author editor surface is a WinUI RichEditBox with character formats.
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<OricOffice.MauiShared.RichText.RichTextEditor,
                OricOffice.MauiShared.RichText.RichEditBoxHandler>());
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<IDialogService, MauiDialogService>();
        builder.Services.AddSingleton<IAppLifetimeService, MauiAppLifetimeService>();
        builder.Services.AddSingleton<ITextClipboard, MauiTextClipboard>();
        builder.Services.AddSingleton<IPrintService, MauiPrintService>();
        builder.Services.AddSingleton<OricFileStore>();

        // One view model per module, each with its own document identity.
        // OnModuleEntered restores a leftover draft and records the recents entry.
        builder.Services.AddSingleton(sp =>
        {
            var vm = new AuthorViewModel(
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IAppLifetimeService>(),
                sp.GetRequiredService<ITextClipboard>(),
                sp.GetRequiredService<OricFileStore>(),
                new AppMeta("Oric Author", "1.0.0", OricFileKind.Author),
                sp.GetRequiredService<IPrintService>());
            vm.OnModuleEntered();
            return vm;
        });

        builder.Services.AddSingleton(sp =>
        {
            var vm = new CalcViewModel(
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IAppLifetimeService>(),
                sp.GetRequiredService<OricFileStore>(),
                new AppMeta("Oric Calc", "1.0.0", OricFileKind.Calc),
                sp.GetRequiredService<IPrintService>());
            vm.OnModuleEntered();
            return vm;
        });

        builder.Services.AddSingleton(sp =>
        {
            var vm = new BaseViewModel(
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IAppLifetimeService>(),
                sp.GetRequiredService<OricFileStore>(),
                new AppMeta("Oric Base", "1.0.0", OricFileKind.Base),
                sp.GetRequiredService<IPrintService>());
            vm.OnModuleEntered();
            return vm;
        });

        builder.Services.AddSingleton<StartPage>();
        builder.Services.AddSingleton<AuthorPage>();
        builder.Services.AddSingleton<CalcPage>();
        builder.Services.AddSingleton<BasePage>();

        return builder.Build();
    }
}
