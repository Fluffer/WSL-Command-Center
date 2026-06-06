using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;

namespace Wsl.App.Views;

public sealed partial class ConfigPage : Page
{
    public ConfigViewModel Vm { get; }

    public ConfigPage()
    {
        Vm = App.Services.GetRequiredService<ConfigViewModel>();
        InitializeComponent();
        _ = Vm.LoadDistrosAsync();
    }

    private async void Shutdown_Click(object s, RoutedEventArgs e)
    {
        var runner = App.Services.GetRequiredService<IProcessRunner>();
        await runner.RunAsync("wsl.exe", new[] { "--shutdown" });
    }
}
