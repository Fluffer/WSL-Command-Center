using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Wsl.App.Logic.ViewModels;

namespace Wsl.App.Views;

public sealed partial class DisksPage : Page
{
    public DisksViewModel Vm { get; }

    public DisksPage()
    {
        Vm = App.Services.GetRequiredService<DisksViewModel>();
        InitializeComponent();
        _ = Vm.LoadDisksAsync();
    }

    private async void MountDisk_Click(object s, RoutedEventArgs e)
    {
        // System disk rows are disabled in XAML; the broker refuses them too (defense in depth).
        if (s is not FrameworkElement { Tag: DiskRow row } || row.IsSystem) return;
        await ShowMountDialogAsync(row.DeviceId, vhd: false, confirmToken: row.ShortName);
    }

    private async void UnmountDisk_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement { Tag: string device }) return;
        await Vm.UnmountAsync(device);
    }

    private async void BrowseVhd_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add(".vhdx");
        picker.FileTypeFilter.Add(".vhd");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) Vm.VhdPath = file.Path;
    }

    private async void MountVhd_Click(object s, RoutedEventArgs e)
    {
        var path = Vm.VhdPath.Trim();
        if (path.Length == 0)
        { Vm.ErrorMessage = "Choose a .vhdx or .vhd file first."; return; }
        if (!File.Exists(path))
        { Vm.ErrorMessage = $"File not found: {path}"; return; }

        // VHD mounts get a plain confirm (the dialog itself); physical disks need a typed confirm.
        await ShowMountDialogAsync(path, vhd: true, confirmToken: null);
    }

    /// <summary>Mount options dialog (codebehind dialog idiom, like DashboardPage). For physical
    /// disks the primary button stays disabled until the user types the device's short name
    /// (e.g. PHYSICALDRIVE2) — council-mandated typed confirmation.</summary>
    private async Task ShowMountDialogAsync(string disk, bool vhd, string? confirmToken)
    {
        var bare = new CheckBox { Content = "Attach without mounting (--bare)" };
        var partition = new NumberBox
        {
            Header = "Partition (0 = whole disk)",
            Minimum = 0,
            Value = 0,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var type = new TextBox { Header = "Filesystem type", Text = "ext4" };
        var options = new TextBox { Header = "Mount options", PlaceholderText = "e.g. ro" };
        var name = new TextBox { Header = "Mount name", PlaceholderText = "default: device name" };

        // Bare attach skips mounting, so the mount-only fields become meaningless.
        void SyncBare()
        {
            var enabled = bare.IsChecked != true;
            partition.IsEnabled = enabled;
            type.IsEnabled = enabled;
            options.IsEnabled = enabled;
            name.IsEnabled = enabled;
        }
        bare.Checked += (_, _) => SyncBare();
        bare.Unchecked += (_, _) => SyncBare();

        var content = new StackPanel
        {
            Spacing = 8,
            MinWidth = 360,
            Children = { bare, partition, type, options, name },
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = vhd ? "Mount VHD into WSL2" : $"Mount {disk} into WSL2",
            Content = content,
            PrimaryButtonText = "Mount",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, // mounting a raw disk is not a default action
        };

        if (confirmToken is not null)
        {
            var confirm = new TextBox
            {
                Header = $"Type \"{confirmToken}\" to confirm",
                PlaceholderText = confirmToken,
            };
            content.Children.Add(confirm);
            dialog.IsPrimaryButtonEnabled = false;
            confirm.TextChanged += (_, _) =>
                dialog.IsPrimaryButtonEnabled =
                    string.Equals(confirm.Text.Trim(), confirmToken, StringComparison.OrdinalIgnoreCase);
        }

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        int? part = !double.IsNaN(partition.Value) && partition.Value >= 1
            ? (int)partition.Value
            : null;
        await Vm.MountAsync(disk, vhd, bare.IsChecked == true,
            part, type.Text, options.Text, name.Text);
    }
}
