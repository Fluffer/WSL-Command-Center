using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Wsl.App.Logic.ViewModels;
using WinRT.Interop;

namespace Wsl.App.Views;

public sealed partial class ProvisioningPage : Page
{
    public ProvisioningViewModel Vm { get; }

    public ProvisioningPage()
    {
        Vm = App.Services.GetRequiredService<ProvisioningViewModel>();
        InitializeComponent();
        _ = Vm.LoadDistrosAsync();
    }

    private async void BrowseCloneDir_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) Vm.CloneInstallDir = folder.Path;
    }

    private async void ApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsBusy) return;
        if (Vm.SelectedDistro is null) { Vm.ErrorMessage = "Select a distro to provision."; return; }
        if (Vm.SelectedTemplate is null) { Vm.ErrorMessage = "Select a template to apply."; return; }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Apply template?",
            Content = $"Run \"{Vm.SelectedTemplate.DisplayName}\" inside {Vm.SelectedDistro.Name} as root? " +
                      "This installs/upgrades packages and may take a while.",
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.ApplyTemplateCommand.ExecuteAsync(null);
    }

    private async void Clone_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsBusy) return;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Clone distro?",
            Content = $"Clone {Vm.CloneSource?.Name ?? "(none)"} to \"{Vm.CloneNewName}\"? " +
                      "The source distro is exported and re-imported — this can take several minutes for a large distro.",
            PrimaryButtonText = "Clone",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.CloneCommand.ExecuteAsync(null);
    }
}
