using Microsoft.UI.Xaml;
using Wsl.App.Services;

namespace Wsl.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static object MainWindowHandleHost { get; private set; } = null!;
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ServiceRegistration.Build();
        _window = new MainWindow();
        MainWindowHandleHost = _window;
        _window.Activate();
    }
}
