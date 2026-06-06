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
        var name = (string)((Button)s).Tag;
        var dialog = new ContentDialog
        {
            Title = "Unregister distro",
            Content = $"This permanently deletes '{name}' and its filesystem. Type the name to confirm.",
            PrimaryButtonText = "Unregister",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.UnregisterAsync(name);
    }
}
