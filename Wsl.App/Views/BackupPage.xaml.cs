using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Wsl.App.Logic.ViewModels;
using WinRT.Interop;

namespace Wsl.App.Views;

public sealed partial class BackupPage : Page
{
    public BackupViewModel Vm { get; }

    public BackupPage()
    {
        Vm = App.Services.GetRequiredService<BackupViewModel>();
        InitializeComponent();
        _ = Vm.LoadDistrosAsync();
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
}
