using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Wsl.App.Logic.ViewModels;

namespace Wsl.App.Views;

public sealed partial class MonitorPage : Page
{
    public MonitorViewModel Vm { get; }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _isLoaded;

    public MonitorPage()
    {
        Vm = App.Services.GetRequiredService<MonitorViewModel>();
        InitializeComponent();
        _timer.Tick += async (_, _) => await Vm.RefreshAsync();
        Loaded += async (_, _) =>
        {
            _isLoaded = true;
            await Vm.RefreshAsync();
            if (_isLoaded) _timer.Start();
        };
        Unloaded += (_, _) => { _isLoaded = false; _timer.Stop(); };
    }

    private async void Terminate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Terminate distribution?",
            Content = $"This immediately stops '{name}'. Any unsaved work inside the distro will be lost.",
            PrimaryButtonText = "Terminate",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.TerminateCommand.ExecuteAsync(name);
    }

    private async void RestartVm_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restart WSL2 VM?",
            Content = "This shuts down the WSL2 virtual machine, stopping all running distributions. Any unsaved work will be lost.",
            PrimaryButtonText = "Restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.RestartVmCommand.ExecuteAsync(null);
    }
}
