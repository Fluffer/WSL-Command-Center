using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wsl.App.Logic.ViewModels;

namespace Wsl.App.Views;

public sealed partial class SetupPage : Page
{
    public SetupViewModel Vm { get; }

    public SetupPage()
    {
        Vm = App.Services.GetRequiredService<SetupViewModel>();
        InitializeComponent();
        _ = Vm.ResumeAsync(); // continue any pending bootstrap on view load
    }

    private void Restart_Click(object s, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { UseShellExecute = true });
}
