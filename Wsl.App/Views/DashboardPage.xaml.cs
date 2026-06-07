using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Wsl.App.Logic.ViewModels;
using Wsl.App.Services;
using Wsl.Core;
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

    private async void SetDefaultUser_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement { Tag: string name }) return;

        // Implicitly starts the distro if stopped (`wsl -d` boots it) — acceptable per plan.
        var users = await Vm.ListUsersAsync(name);
        if (users.Count == 0) return; // failure already surfaced via Vm.ErrorMessage

        var combo = new ComboBox
        {
            ItemsSource = users,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Set default user for '{name}'",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "New sessions of this distro will log in as the selected user.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    combo,
                },
            },
            PrimaryButtonText = "Set default user",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && combo.SelectedItem is string user)
            await Vm.SetDefaultUserAsync(name, user);
    }

    private async void LaunchWithOptions_Click(object s, RoutedEventArgs e)
    {
        if (s is not FrameworkElement { Tag: string name }) return;

        var user = new TextBox { Header = "User", PlaceholderText = "distro default" };
        var cwd = new TextBox { Header = "Working directory", PlaceholderText = "~  or  /path  or  C:\\path" };
        // Items mirror the enum declaration order, so SelectedIndex casts straight to WslShellType.
        var shellType = new ComboBox
        {
            Header = "Shell type",
            ItemsSource = Enum.GetNames<WslShellType>(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var command = new TextBox { Header = "Command", PlaceholderText = "empty = interactive shell" };
        var useExec = new CheckBox { Content = "Run command without a shell (--exec)" };
        var system = new CheckBox { Content = "Launch the system distro instead (--system)" };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Launch '{name}' with options",
            Content = new StackPanel
            {
                Spacing = 8,
                MinWidth = 360,
                Children = { user, cwd, shellType, command, useExec, system },
            },
            PrimaryButtonText = "Launch",
            SecondaryButtonText = "Copy PowerShell",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;

        var options = new LaunchOptions
        {
            User = user.Text,
            WorkingDirectory = cwd.Text,
            ShellType = (WslShellType)shellType.SelectedIndex,
            Command = command.Text,
            UseExec = useExec.IsChecked == true,
            SystemDistro = system.IsChecked == true,
        };

        if (result == ContentDialogResult.Primary)
            TerminalLauncher.Launch(LaunchCommandBuilder.Build(name, options));
        else
            CopyCommand(_ps.Launch(name, options));
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
