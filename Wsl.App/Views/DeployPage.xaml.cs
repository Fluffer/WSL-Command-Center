using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Wsl.Core.Scripting;
using WinRT.Interop;

namespace Wsl.App.Views;

public sealed partial class DeployPage : Page
{
    public DeployViewModel Vm { get; }
    private readonly IPowerShellExporter _ps;

    public DeployPage()
    {
        Vm = App.Services.GetRequiredService<DeployViewModel>();
        _ps = App.Services.GetRequiredService<IPowerShellExporter>();
        InitializeComponent();
        _ = Vm.LoadCatalogAsync();   // fetch the online catalog on entry; no need to press Load
    }

    private void CopyInstallPs_Click(object s, RoutedEventArgs e)
    {
        if (Vm.SelectedCatalogEntry is null) { Vm.ErrorMessage = "Select a distro first."; return; }
        ClipboardHelper.CopyText(_ps.Install(Vm.SelectedCatalogEntry.Name));
        Vm.StatusMessage = "Copied install command to clipboard.";
    }

    private void CopyImportPs_Click(object s, RoutedEventArgs e)
    {
        var fmt = Vm.ImportArchivePath.EndsWith(".vhdx", System.StringComparison.OrdinalIgnoreCase)
            ? ExportFormat.Vhd
            : ExportFormat.Tar;
        var cmd = _ps.Restore(Vm.ImportName, Vm.ImportInstallDir, Vm.ImportArchivePath, fmt, Vm.ImportVersion);
        ClipboardHelper.CopyText(cmd);
        Vm.StatusMessage = "Copied import command to clipboard.";
    }

    private async void BrowseArchive_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add(".tar");
        picker.FileTypeFilter.Add(".gz");
        picker.FileTypeFilter.Add(".vhdx");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) Vm.ImportArchivePath = file.Path;
    }

    private async void BrowseDir_Click(object s, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) Vm.ImportInstallDir = folder.Path;
    }
}
