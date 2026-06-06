using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Wsl.App.Logic.ViewModels;
using Wsl.Core.Scripting;

namespace Wsl.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel Vm { get; }
    private readonly IPowerShellExporter _ps;

    public DashboardPage()
    {
        Vm = App.Services.GetRequiredService<DashboardViewModel>();
        _ps = App.Services.GetRequiredService<IPowerShellExporter>();
        InitializeComponent();
        _ = Vm.RefreshAsync();
    }

    private static string Name(object sender) => (sender as FrameworkElement)?.Tag as string ?? "";

    private void CopyCommand(string cmd)
    {
        ClipboardHelper.CopyText(cmd);
        CopiedBar.Message = cmd;
        CopiedBar.IsOpen = true;
    }

    private void CopyStart_Click(object s, RoutedEventArgs e) => CopyCommand(_ps.Start(Name(s)));
    private void CopyStop_Click(object s, RoutedEventArgs e) => CopyCommand(_ps.Terminate(Name(s)));
    private void CopySetDefault_Click(object s, RoutedEventArgs e) => CopyCommand(_ps.SetDefault(Name(s)));
    private void CopyOptimize_Click(object s, RoutedEventArgs e) => CopyCommand(_ps.Optimize(Name(s)));
    private void CopyUnregister_Click(object s, RoutedEventArgs e) => CopyCommand(_ps.Unregister(Name(s)));

    private async void Optimize_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement { Tag: string name }) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Optimize disk?",
            Content = $"This shuts down '{name}', then marks its virtual disk sparse so Windows " +
                      "can reclaim space freed inside the distro. Any unsaved work in the running " +
                      "distro will be lost. Continue?",
            PrimaryButtonText = "Optimize",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.OptimizeAsync(name);
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
