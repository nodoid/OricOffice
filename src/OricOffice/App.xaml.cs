using Microsoft.Extensions.DependencyInjection;
using OricOffice.MauiShared.Themes;

namespace OricOffice;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
        // SuiteStyles must be merged here, before any page is constructed:
        // pages resolved earlier would find their {StaticResource Suite.*}
        // lookups against an empty scope and WinUI would fail fast at startup.
        Resources.MergedDictionaries.Add(new SuiteStyles());
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var start = _services.GetRequiredService<StartPage>();
        var navigation = new NavigationPage(start);

        // Convenience switch: OricOffice.exe --module=author|calc|base
        // opens a module directly (handy for testing and deep links).
        var arg = System.Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--module=", StringComparison.OrdinalIgnoreCase));
        if (arg is not null)
        {
            var module = arg["--module=".Length..].ToLowerInvariant();
            Page? page = module switch
            {
                "author" => _services.GetRequiredService<AuthorPage>(),
                "calc" => _services.GetRequiredService<CalcPage>(),
                "base" => _services.GetRequiredService<BasePage>(),
                _ => null,
            };
            if (page is not null)
                _ = navigation.PushAsync(page);
        }

        return new Window(navigation)
        {
            Title = "Oric Office",
        };
    }
}
