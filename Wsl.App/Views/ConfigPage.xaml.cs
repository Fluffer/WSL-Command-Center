using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Wsl.Core.Scripting;

namespace Wsl.App.Views;

public sealed partial class ConfigPage : Page
{
    public ConfigViewModel Vm { get; }
    private readonly IPowerShellExporter _ps;

    public ConfigPage()
    {
        Vm = App.Services.GetRequiredService<ConfigViewModel>();
        _ps = App.Services.GetRequiredService<IPowerShellExporter>();
        InitializeComponent();
        _ = Vm.LoadGlobalAsync();   // show current .wslconfig immediately
        _ = Vm.LoadDistrosAsync();  // populate the per-distro dropdown
    }

    // Auto-load the selected distro's wsl.conf as soon as it's picked.
    private async void Distro_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((ComboBox)sender).SelectedItem is string name)
        {
            Vm.SelectedDistro = name;
            await Vm.LoadDistroAsync();
        }
    }

    private async void Shutdown_Click(object s, RoutedEventArgs e)
    {
        var runner = App.Services.GetRequiredService<IProcessRunner>();
        await runner.RunAsync("wsl.exe", new[] { "--shutdown" });
    }

    private void CopyShutdownPs_Click(object s, RoutedEventArgs e)
    {
        ClipboardHelper.CopyText(_ps.Shutdown());
        Vm.StatusMessage = "Copied shutdown command to clipboard.";
    }
}
