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

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<IDialogService, MauiDialogService>();
        builder.Services.AddSingleton<IAppLifetimeService, MauiAppLifetimeService>();
        builder.Services.AddSingleton<ITextClipboard, MauiTextClipboard>();
        builder.Services.AddSingleton<OricFileStore>();

        // One view model per module, each with its own document identity.
        builder.Services.AddSingleton(sp => new AuthorViewModel(
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IAppLifetimeService>(),
            sp.GetRequiredService<ITextClipboard>(),
            sp.GetRequiredService<OricFileStore>(),
            new AppMeta("Oric Author", "1.0.0", OricFileKind.Author)));

        builder.Services.AddSingleton(sp => new CalcViewModel(
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IAppLifetimeService>(),
            sp.GetRequiredService<OricFileStore>(),
            new AppMeta("Oric Calc", "1.0.0", OricFileKind.Calc)));

        builder.Services.AddSingleton(sp => new BaseViewModel(
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IAppLifetimeService>(),
            sp.GetRequiredService<OricFileStore>(),
            new AppMeta("Oric Base", "1.0.0", OricFileKind.Base)));

        builder.Services.AddSingleton<StartPage>();
        builder.Services.AddSingleton<AuthorPage>();
        builder.Services.AddSingleton<CalcPage>();
        builder.Services.AddSingleton<BasePage>();

        return builder.Build();
    }
}
