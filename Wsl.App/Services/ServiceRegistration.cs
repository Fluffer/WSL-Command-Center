using Microsoft.Extensions.DependencyInjection;
using Wsl.Core;
using Wsl.Core.Ipc;
using Wsl.Core.Settings;
using Wsl.Core.Scripting;
using Wsl.Core.Scheduling;
using Wsl.App.Logic.ViewModels;

namespace Wsl.App.Services;

public static class ServiceRegistration
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Core (unprivileged) — shared single process runner.
        services.AddSingleton<IProcessRunner, RealProcessRunner>();
        services.AddSingleton<WslDistroService>();
        services.AddSingleton<WslSystemService>();
        services.AddSingleton<WslDiskService>();
        services.AddSingleton<WslDeployService>();
        services.AddSingleton<WslBackupService>();
        services.AddSingleton<WslConfigService>();
        services.AddSingleton<BootstrapStateStore>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IPowerShellExporter, PowerShellExporter>();
        services.AddSingleton<IWslScheduleService, WslScheduleService>();

        // Broker (privileged) — path resolved next to the app exe.
        services.AddSingleton<IPeerVerifier, WindowsPeerVerifier>();
        services.AddSingleton<IBrokerClient>(sp =>
        {
            var brokerPath = Path.Combine(AppContext.BaseDirectory, "Wsl.Broker.exe");
            return new BrokerClient(brokerPath, sp.GetRequiredService<IPeerVerifier>());
        });

        // ViewModels are registered here as each is implemented (Tasks 17-21):
        services.AddTransient<DashboardViewModel>();       // Task 17
        services.AddTransient<DeployViewModel>();          // Task 18
        services.AddTransient<BackupViewModel>();          // Task 19
        services.AddTransient<ConfigViewModel>();          // Task 20
        services.AddTransient<SetupViewModel>();           // Task 21
        services.AddTransient<ScheduleViewModel>();
        services.AddTransient<DisksViewModel>();

        return services.BuildServiceProvider();
    }
}
