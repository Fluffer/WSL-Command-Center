using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Wsl.App.Logic.ViewModels;
using Wsl.Core.Scripting;
using WinRT.Interop;

namespace Wsl.App.Views;

public sealed partial class BackupPage : Page
{
    public BackupViewModel Vm { get; }
    private readonly IPowerShellExporter _ps;

    public BackupPage()
    {
        Vm = App.Services.GetRequiredService<BackupViewModel>();
        _ps = App.Services.GetRequiredService<IPowerShellExporter>();
        InitializeComponent();
        _ = Vm.LoadDistrosAsync();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var running = await Vm.RunningDistrosAsync();
        if (running.Count > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Stop running distros for backup?",
                Content = $"This briefly stops ALL running distros ({string.Join(", ", running)}) " +
                          "and restarts them after the backup completes. Continue?",
                PrimaryButtonText = "Stop & back up",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        await Vm.ExportCommand.ExecuteAsync(null);
    }

    private void CopyExportPs_Click(object s, RoutedEventArgs e)
    {
        var cmd = _ps.Export(Vm.ExportDistro, Vm.ExportPath, Vm.ExportFormat);
        ClipboardHelper.CopyText(cmd);
        Vm.StatusMessage = "Copied export command to clipboard.";
    }

    private void CopyRestorePs_Click(object s, RoutedEventArgs e)
    {
        var cmd = _ps.Restore(Vm.RestoreName, Vm.RestoreInstallDir, Vm.RestoreArchivePath,
                              Vm.RestoreFormat, Vm.RestoreVersion);
        ClipboardHelper.CopyText(cmd);
        Vm.StatusMessage = "Copied restore command to clipboard.";
    }

    private async void BrowseExport_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeChoices.Add("Tar", new List<string> { ".tar" });
        picker.FileTypeChoices.Add("Gzip tar", new List<string> { ".gz" });
        picker.FileTypeChoices.Add("VHDX", new List<string> { ".vhdx" });
        var file = await picker.PickSaveFileAsync();
        if (file is not null) Vm.ExportPath = file.Path;
    }

    private async void BrowseRestoreDir_Click(object s, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) Vm.RestoreInstallDir = folder.Path;
    }

    private async void BrowseRestore_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add(".tar");
        picker.FileTypeFilter.Add(".gz");
        picker.FileTypeFilter.Add(".vhdx");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) Vm.RestoreArchivePath = file.Path;
    }

    private async void BrowseInPlace_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add(".vhdx");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) Vm.InPlaceVhdxPath = file.Path;
    }
}
