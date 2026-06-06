using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Wsl.App.Logic.ViewModels;
using WinRT.Interop;

namespace Wsl.App.Views;

public sealed partial class SchedulePage : Page
{
    public ScheduleViewModel Vm { get; }

    public SchedulePage()
    {
        Vm = App.Services.GetRequiredService<ScheduleViewModel>();
        InitializeComponent();
        _ = Vm.LoadAsync();
    }

    private async void BrowseFolder_Click(object s, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) Vm.Folder = folder.Path;
    }

    private async void Delete_Click(object s, RoutedEventArgs e)
    {
        if (s is FrameworkElement { Tag: string taskName })
            await Vm.DeleteAsync(taskName);
    }

    private void CopyPs_Click(object s, RoutedEventArgs e)
    {
        ClipboardHelper.CopyText(Vm.BuildScriptPreview());
        Vm.StatusMessage = "Copied backup script to clipboard.";
    }
}
