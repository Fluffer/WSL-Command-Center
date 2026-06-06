using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Wsl.App.Logic.ViewModels;

namespace Wsl.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel Vm { get; }

    public DashboardPage()
    {
        Vm = App.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();
        _ = Vm.RefreshAsync();
    }

    private async void Start_Click(object s, RoutedEventArgs e)
        => await Vm.StartAsync((string)((Button)s).Tag);

    private async void Stop_Click(object s, RoutedEventArgs e)
        => await Vm.TerminateAsync((string)((Button)s).Tag);

    private async void SetDefault_Click(object s, RoutedEventArgs e)
        => await Vm.SetDefaultAsync((string)((Button)s).Tag);

    private async void Unregister_Click(object s, RoutedEventArgs e)
    {
        // Sender is a MenuFlyoutItem (overflow menu), so cast via FrameworkElement, not Button.
        if (s is not FrameworkElement { Tag: string name }) return;
        var dialog = new ContentDialog
        {
            Title = "Unregister distribution?",
            Content = $"This permanently deletes '{name}' and its entire filesystem. This cannot be undone.",
            PrimaryButtonText = "Unregister",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.UnregisterAsync(name);
    }
}
