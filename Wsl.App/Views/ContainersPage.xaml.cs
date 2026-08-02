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

    // ── x:Bind visibility / formatting helpers (page-level → resolve against the page) ──

    internal Visibility ShowEnabled(bool enabled) => enabled ? Visibility.Visible : Visibility.Collapsed;

    internal Visibility ShowAvailable(WslcAvailability? a)
        => a?.IsAvailable == true ? Visibility.Visible : Visibility.Collapsed;

    internal bool ShowUnavailableBar(bool enabled, WslcAvailability? a)
        => enabled && a is not null && !a.IsAvailable;

    internal Visibility OutputVisibility(string? output)
        => string.IsNullOrEmpty(output) ? Visibility.Collapsed : Visibility.Visible;

    internal Visibility ErrorVisibility(string? error)
        => string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Shows an empty-state message below a list instead of leaving a bare blank area.
    /// ObservableCollection raises PropertyChanged for Count, so this stays live via x:Bind.</summary>
    internal Visibility EmptyVisibility(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // Per-row Start/Stop/Restart gating and image-created formatting inside the ListView
    // DataTemplates go through ContainerActionVisibilityConverter / DateTimeOffsetToStringConverter
    // instead — compiled (x:Bind) function-call bindings can't reach back to Page methods from
    // inside a DataTemplate whose x:DataType differs from the page.

    internal string TagSourceText(WslcImage? image)
        => image is null ? "Select an image above to tag it." : $"Tag \"{image.RepoTag}\" as:";

    internal string SettingsFileHint(string path)
        => $"Reads and writes {path} directly (comment-preserving). A missing file is created with WSL's built-in defaults.";

    // ── Event handlers: page shell ─────────────────────────────────────────

    private async void PreviewToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;                 // skip the initial IsOn binding pass
        if (sender is not ToggleSwitch ts) return;
        if (ts.IsOn == Vm.IsPreviewEnabled) return;
        await Vm.SetPreviewAsync(ts.IsOn);
    }

    private async void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var item = sender.SelectedItem;
        ContainersPanel.Visibility = item == ContainersTab ? Visibility.Visible : Visibility.Collapsed;
        ImagesPanel.Visibility = item == ImagesTab ? Visibility.Visible : Visibility.Collapsed;
        VolumesPanel.Visibility = item == VolumesTab ? Visibility.Visible : Visibility.Collapsed;
        NetworksPanel.Visibility = item == NetworksTab ? Visibility.Visible : Visibility.Collapsed;
        SessionsPanel.Visibility = item == SessionsTab ? Visibility.Visible : Visibility.Collapsed;
        ConfigPanel.Visibility = item == ConfigTab ? Visibility.Visible : Visibility.Collapsed;

        // Lazy-load each tab's data the first time it's selected, so opening the page doesn't
        // fan out into a burst of wslc invocations before the user asks for them.
        if (Vm.IsBusy) return;
        if (item == ImagesTab && Vm.Images.Count == 0) await Vm.RefreshImagesCommand.ExecuteAsync(null);
        else if (item == VolumesTab && Vm.Volumes.Count == 0) await Vm.RefreshVolumesCommand.ExecuteAsync(null);
        else if (item == NetworksTab && Vm.Networks.Count == 0) await Vm.RefreshNetworksCommand.ExecuteAsync(null);
        else if (item == SessionsTab && Vm.Sessions.Count == 0) await Vm.RefreshSessionsCommand.ExecuteAsync(null);
        else if (item == ConfigTab && !_settingsLoaded)
        {
            _settingsLoaded = true;
            await Vm.LoadSettingsCommand.ExecuteAsync(null);
        }
    }

    private bool _settingsLoaded;

    private async Task<bool> ConfirmAsync(string title, string content, string primaryText = "Confirm")
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // ── Event handlers: Containers tab ─────────────────────────────────────

    private async void StartContainer_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcContainer c) return;
        await Vm.StartContainerCommand.ExecuteAsync(c);
    }

    private async void StopContainer_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcContainer c) return;
        await Vm.StopContainerCommand.ExecuteAsync(c);
    }

    private async void RestartContainer_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcContainer c) return;
        await Vm.RestartContainerCommand.ExecuteAsync(c);
    }

    private async void KillContainer_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcContainer c) return;
        if (!await ConfirmAsync("Kill container?",
                $"Send a kill signal to \"{c.Name}\"? This forcibly stops the container without a graceful shutdown.",
                "Kill"))
            return;
        await Vm.KillContainerCommand.ExecuteAsync(c);
    }

    private async void RemoveContainer_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcContainer c) return;
        // A running container can only be removed with --force. Say that plainly rather than
        // force-removing behind a dialog that reads like an ordinary delete.
        var force = ContainersViewModel.NeedsForceRemove(c.State);
        var body = force
            ? $"Container \"{c.Name}\" is still running. Removing it now force-stops it first, "
              + "so anything running inside is killed without a graceful shutdown. This cannot be undone."
            : $"Remove container \"{c.Name}\"? This deletes the container instance.";
        if (!await ConfirmAsync("Remove container?", body, force ? "Force remove" : "Remove"))
            return;
        await Vm.RemoveContainerCommand.ExecuteAsync(c);
    }

    private async void ContainerLogs_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcContainer c) return;
        await Vm.ShowContainerLogsCommand.ExecuteAsync(c);
    }

    private async void InspectContainer_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcContainer c) return;
        await Vm.InspectContainerCommand.ExecuteAsync(c);
    }

    private async void PruneContainers_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Prune stopped containers?",
                "Remove ALL stopped containers? This cannot be undone.", "Prune"))
            return;
        await Vm.PruneContainersCommand.ExecuteAsync(null);
    }

    // ── Event handlers: Deploy container form ──────────────────────────────
    // These pickers are conveniences over the free-text Image/Network boxes — selecting an entry
    // just copies its identifier into the corresponding TwoWay-bound field.

    private void DeployImagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: WslcImage image }) Vm.DeployImage = image.RepoTag;
    }

    private void DeployNetworkPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: WslcNetwork network }) Vm.DeployNetwork = network.Name;
    }

    // ── Event handlers: Images tab ─────────────────────────────────────────

    private async void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcImage img) return;
        if (!await ConfirmAsync("Remove image?",
                $"Remove image \"{img.RepoTag}\"? You won't be able to create new containers from it until it's pulled again.",
                "Remove"))
            return;
        await Vm.RemoveImageCommand.ExecuteAsync(img);
    }

    private async void PruneImages_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Prune unused images?",
                "Remove all images not referenced by any container? This cannot be undone.", "Prune"))
            return;
        await Vm.PruneImagesCommand.ExecuteAsync(null);
    }

    private async void RegistryLogin_Click(object sender, RoutedEventArgs e)
    {
        var password = RegistryPasswordBox.Password;
        RegistryPasswordBox.Password = "";     // never held longer than the call
        await Vm.RegistryLoginCommand.ExecuteAsync(password);
    }

    // ── Event handlers: Volumes tab ────────────────────────────────────────

    private async void RemoveVolume_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcVolume v) return;
        if (!await ConfirmAsync("Remove volume?",
                $"Remove volume \"{v.Name}\"? Any data stored in it will be permanently lost.", "Remove"))
            return;
        await Vm.RemoveVolumeCommand.ExecuteAsync(v);
    }

    private async void PruneVolumes_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Prune unused volumes?",
                "Remove ALL unused volumes? Any data in them will be permanently lost.", "Prune"))
            return;
        await Vm.PruneVolumesCommand.ExecuteAsync(null);
    }

    // ── Event handlers: Networks tab ───────────────────────────────────────

    private async void RemoveNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcNetwork n) return;
        if (!await ConfirmAsync("Remove network?", $"Remove network \"{n.Name}\"?", "Remove")) return;
        await Vm.RemoveNetworkCommand.ExecuteAsync(n);
    }

    private async void PruneNetworks_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Prune unused networks?",
                "Remove all networks not used by any container?", "Prune"))
            return;
        await Vm.PruneNetworksCommand.ExecuteAsync(null);
    }

    // ── Event handlers: Sessions tab ───────────────────────────────────────

    private async void TerminateSession_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("Terminate the default session?",
                "This stops and removes every container currently running in the default wslc session — not just one.",
                "Terminate"))
            return;
        await Vm.TerminateSessionCommand.ExecuteAsync(null);
    }

    private async void ReclaimSession_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not WslcSessionRow row) return;
        if (!await ConfirmAsync("Reclaim session disk space?",
                $"Reclaim disk space for session \"{row.DisplayName}\"? This first terminates the default wslc " +
                "session (stopping every container in it), then marks its VHD files sparse so Windows can reclaim " +
                "freed space over time.",
                "Reclaim"))
            return;
        await Vm.ReclaimSessionCommand.ExecuteAsync(row);
    }

    // ── Event handlers: raw command box ────────────────────────────────────

    private async void RunRaw_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsBusy) return;

        if (Vm.RawNeedsConfirm)
        {
            var verb = WslcCommand.FirstVerb(Vm.RawCommand);
            if (!await ConfirmAsync("Run a non-read-only wslc command?",
                    $"\"{verb}\" is not a known read-only subcommand and may change container state. " +
                    $"Run `wslc {Vm.RawCommand}`?",
                    "Run"))
                return;
        }

        await Vm.ExecuteRawCommand.ExecuteAsync(null);
    }
}
