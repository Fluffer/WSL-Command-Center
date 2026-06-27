using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Wsl.App.Logic.ViewModels;
using Wsl.Core.Snapshots;
using WinRT.Interop;

namespace Wsl.App.Views;

public sealed partial class SnapshotPage : Page
{
    public SnapshotViewModel Vm { get; }
    private bool _loaded;

    public SnapshotPage()
    {
        Vm = App.Services.GetRequiredService<SnapshotViewModel>();
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await Vm.LoadCommand.ExecuteAsync(null);
            _loaded = true;
        };
    }

    private async void DistroComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || Vm.IsBusy) return;
        await Vm.LoadCommand.ExecuteAsync(null);
    }

    private async void RestoreClone_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Snapshot snap }) return;

        // Step 1 — name dialog only (no picker embedded inside a modal)
        var nameBox = new TextBox
        {
            Header = "New distro name",
            PlaceholderText = $"{snap.Distro}-clone",
            MinWidth = 280,
        };
        var nameDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Clone snapshot '{snap.Label}'",
            Content = nameBox,
            PrimaryButtonText = "Clone",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await nameDialog.ShowAsync() != ContentDialogResult.Primary) return;

        var name = nameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Vm.ErrorMessage = "Enter a name for the cloned distro.";
            return;
        }

        // Step 2 — folder picker after dialog is fully dismissed
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        await Vm.RestoreCloneCommand.ExecuteAsync((snap, name, folder.Path));
    }

    private async void RestoreOverwrite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Snapshot snap }) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restore snapshot (overwrite)?",
            Content = $"This destroys the current state of '{snap.Distro}' and replaces it with this snapshot. " +
                      "All data added after the snapshot was taken will be lost. This cannot be undone.",
            PrimaryButtonText = "Overwrite",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        await Vm.RestoreOverwriteCommand.ExecuteAsync((snap, folder.Path));
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Snapshot snap }) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Delete snapshot?",
            Content = $"This permanently deletes snapshot '{snap.Label}' for '{snap.Distro}'. " +
                      "The snapshot file and its sidecar will be removed. This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.DeleteCommand.ExecuteAsync(snap);
    }
}
