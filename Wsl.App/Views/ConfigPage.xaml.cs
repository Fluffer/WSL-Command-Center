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

    // Toggle between the Global (.wslconfig) and Per-distro (wsl.conf) panels.
    private void ScopeBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var global = sender.SelectedItem == GlobalScope;
        GlobalPanel.Visibility = global ? Visibility.Visible : Visibility.Collapsed;
        DistroPanel.Visibility = global ? Visibility.Collapsed : Visibility.Visible;
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

    private async void ApplyVirtiofs_Click(object s, RoutedEventArgs e)
    {
        if (Vm.IsBusy) return;
        var enable = Vm.VirtiofsEnabled;
        var content = enable
            ? "Enable the experimental virtiofs file transport for ALL WSL 2 distros?\n\n" +
              "• Update WSL first (Diagnostics ▸ Update WSL) for a recent kernel.\n" +
              "• Known issues: bind-mounted files may appear root-owned and unwritable; automount of every drive can fail if a single share fails (you could lose /mnt/c access).\n" +
              "• Recovery: if that happens, just turn this toggle back off and Apply — it edits .wslconfig directly and needs no WSL access.\n\n" +
              "WSL will be shut down so the change applies on next launch."
            : "Disable virtiofs and return to the default 9p transport for all distros?\n\nWSL will be shut down so the change applies on next launch.";

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = enable ? "Enable virtiofs (experimental)?" : "Disable virtiofs?",
            Content = content,
            PrimaryButtonText = enable ? "Enable & shut down" : "Disable & shut down",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            // User cancelled — resync the toggle to the persisted flag so the UI doesn't lie.
            await Vm.LoadGlobalCommand.ExecuteAsync(null);
            return;
        }
        await Vm.ApplyVirtiofsCommand.ExecuteAsync(enable);
    }
}
