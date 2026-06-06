using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Wsl.App.Services;
using Wsl.Core.Settings;

namespace Wsl.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static object MainWindowHandleHost { get; private set; } = null!;
    private Window? _window;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ServiceRegistration.Build();

        // Override accent + font resources BEFORE the window/NavigationView is built, so the
        // chrome (selection indicator, accent buttons) picks up the accent on first paint.
        var settings = Services.GetRequiredService<IThemeService>().Load();
        Theming.Appearance.OverrideResources(settings.Accent, settings.Font, Theming.Palettes.Resolve(settings.Theme));

        _window = new MainWindow();
        MainWindowHandleHost = _window;

        if (_window is MainWindow mainWin)
            mainWin.ApplyAppearance(settings.Theme, settings.Accent, settings.Font, rebuild: false);

        _window.Activate();

        // If a bootstrap was pending (mid reboot/resume), continue it immediately.
        var state = Services.GetRequiredService<Wsl.Core.BootstrapStateStore>();
        if (await state.ReadAsync() != Wsl.Core.BootstrapStep.Done && _window is MainWindow mw)
            mw.NavigateToSetup();
    }
}
