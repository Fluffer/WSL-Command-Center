using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wsl.App.Logic.ViewModels;
using Wsl.Core.Containers;

namespace Wsl.App.Views;

public sealed partial class ContainersPage : Page
{
    public ContainersViewModel Vm { get; }
    private bool _loaded;

    public ContainersPage()
    {
        Vm = App.Services.GetRequiredService<ContainersViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (Vm.IsPreviewEnabled) await Vm.RefreshAsync();
            _loaded = true;
        };
    }

    // ── x:Bind visibility helpers (page-level → resolve against the page) ──

    internal Visibility ShowEnabled(bool enabled) => enabled ? Visibility.Visible : Visibility.Collapsed;

    internal Visibility ShowAvailable(WslcAvailability? a)
        => a?.IsAvailable == true ? Visibility.Visible : Visibility.Collapsed;

    internal bool ShowUnavailableBar(bool enabled, WslcAvailability? a)
        => enabled && a is not null && !a.IsAvailable;

    internal Visibility OutputVisibility(string? output)
        => string.IsNullOrEmpty(output) ? Visibility.Collapsed : Visibility.Visible;

    // ── Event handlers ──

    private async void PreviewToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;                 // skip the initial IsOn binding pass
        if (sender is not ToggleSwitch ts) return;
        if (ts.IsOn == Vm.IsPreviewEnabled) return;
        await Vm.SetPreviewAsync(ts.IsOn);
    }

    private async void RunRaw_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsBusy) return;

        if (Vm.RawNeedsConfirm)
        {
            var verb = WslcCommand.FirstVerb(Vm.RawCommand);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Run a non-read-only wslc command?",
                Content = $"\"{verb}\" is not a known read-only subcommand and may change container state. " +
                          $"Run `wslc {Vm.RawCommand}`?",
                PrimaryButtonText = "Run",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        await Vm.ExecuteRawCommand.ExecuteAsync(null);
    }
}
