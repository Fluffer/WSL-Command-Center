using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Wsl.App.Logic.ViewModels;
using Wsl.Core.Diagnostics;

namespace Wsl.App.Views;

public sealed partial class NetworkPage : Page
{
    public NetworkViewModel Vm { get; }
    private bool _loaded;

    public NetworkPage()
    {
        Vm = App.Services.GetRequiredService<NetworkViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            _loaded = true;
            await Vm.RefreshAsync();
        };
    }

    // ── x:Bind helper methods (instance — x:Bind generator calls via this.Method()) ──

    internal string FormatBool(bool value) => value ? "Yes" : "No";

    internal Visibility ShowNvidiaDetails(GpuInfo? info) =>
        info?.NvidiaDetected == true ? Visibility.Visible : Visibility.Collapsed;

    internal Visibility ShowNoNvidiaNote(GpuInfo? info) =>
        info?.NvidiaDetected == true ? Visibility.Collapsed : Visibility.Visible;

    // ── Event handlers ─────────────────────────────────────────────────────

    private async void DistroComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard: skip during initial binding; skip if a refresh is already in flight.
        if (!_loaded || Vm.IsBusy) return;
        await Vm.RefreshAsync();
    }

    private async void RestartNetworking_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restart networking?",
            Content = "This shuts down the WSL2 VM, restarting all distros and their network stacks.",
            PrimaryButtonText = "Restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.RestartNetworkingCommand.ExecuteAsync(null);
    }

    private async void DeletePortForward_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not PortForward forward) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete port forward?",
            Content = $"Remove rule {forward.ListenAddress}:{forward.ListenPort} → {forward.ConnectAddress}:{forward.ConnectPort}? " +
                      "A UAC prompt will appear to authorize the broker.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.DeletePortForwardCommand.ExecuteAsync(forward);
    }
}
