using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wsl.App.Logic.ViewModels;
using Wsl.Core.Scripting;

namespace Wsl.App.Views;

public sealed partial class SetupPage : Page
{
    public SetupViewModel Vm { get; }
    private readonly IPowerShellExporter _ps;

    public SetupPage()
    {
        Vm = App.Services.GetRequiredService<SetupViewModel>();
        _ps = App.Services.GetRequiredService<IPowerShellExporter>();
        InitializeComponent();
        _ = Vm.ResumeAsync(); // continue any pending bootstrap on view load
    }

    private void Restart_Click(object s, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { UseShellExecute = true });

    private void CopyFeaturesPs_Click(object s, RoutedEventArgs e)
    {
        ClipboardHelper.CopyText(_ps.EnableFeatures());
        Vm.StatusMessage = "Copied setup commands. Run them in an elevated (admin) PowerShell.";
    }
}
