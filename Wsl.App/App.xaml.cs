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
        _window = new MainWindow();
        MainWindowHandleHost = _window;

        // Apply the persisted theme before showing the window.
        var theme = Services.GetRequiredService<IThemeService>().Load().Theme;
        if (_window is MainWindow mainWin) mainWin.ApplyTheme(theme);

        _window.Activate();

        // If a bootstrap was pending (mid reboot/resume), continue it immediately.
        var state = Services.GetRequiredService<Wsl.Core.BootstrapStateStore>();
        if (await state.ReadAsync() != Wsl.Core.BootstrapStep.Done && _window is MainWindow mw)
            mw.NavigateToSetup();
    }
}
