using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Wsl.App.Logic.ViewModels;
using Wsl.Core.Diagnostics;

namespace Wsl.App.Views;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel Vm { get; }

    public DiagnosticsPage()
    {
        Vm = App.Services.GetRequiredService<DiagnosticsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await Vm.RunCommand.ExecuteAsync(null);
    }

    private async void ApplyFix_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.IsBusy) return;
        if (sender is not FrameworkElement { DataContext: DiagnosticRow row } || row.Fix is not { } fix) return;

        // Destructive fixes (e.g. wsl --shutdown) get an explicit confirmation.
        if (fix.Destructive)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"{fix.Label}?",
                Content = "This shuts down the WSL2 VM, stopping all running distros. Save your work first.",
                PrimaryButtonText = fix.Label,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        await Vm.ApplyFixCommand.ExecuteAsync(fix);
    }
}
