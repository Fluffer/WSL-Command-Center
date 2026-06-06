using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Wsl.App.Logic.ViewModels;
using WinRT.Interop;

namespace Wsl.App.Views;

public sealed partial class DeployPage : Page
{
    public DeployViewModel Vm { get; }

    public DeployPage()
    {
        Vm = App.Services.GetRequiredService<DeployViewModel>();
        InitializeComponent();
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
