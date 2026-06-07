using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wsl.App.Theming;
using Wsl.Contracts;
using Wsl.Core;
using Wsl.Core.Ipc;
using Wsl.Core.Settings;

namespace Wsl.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly IThemeService _theme;
    private bool _loading;

    /// <summary>Stored theme values, in combo order: base themes first, then palettes.</summary>
    private static readonly string[] Themes = BuildThemes();

    private static string[] BuildThemes()
    {
        var list = new List<string> { "System", "Light", "Dark" };
        list.AddRange(Palettes.Names());
        return list.ToArray();
    }

    /// <summary>"System" reads better as "Use system setting" in the combo.</summary>
    private static string Display(string theme) => theme == "System" ? "Use system setting" : theme;

    public SettingsPage()
    {
        InitializeComponent();
        _theme = App.Services.GetRequiredService<IThemeService>();

        _loading = true;
        var s = _theme.Load();

        var displayNames = new string[Themes.Length];
        for (var i = 0; i < Themes.Length; i++) displayNames[i] = Display(Themes[i]);
        ThemeCombo.ItemsSource = displayNames;
        var ti = System.Array.IndexOf(Themes, s.Theme);
        ThemeCombo.SelectedIndex = ti >= 0 ? ti : 0;

        AccentCombo.ItemsSource = Accents.Names();
        AccentCombo.SelectedItem = System.Array.IndexOf(Accents.Names(), s.Accent) >= 0 ? s.Accent : "Default";

        FontCombo.ItemsSource = AppFonts.All;
        FontCombo.SelectedItem = System.Array.IndexOf(AppFonts.All, s.Font) >= 0 ? s.Font : AppFonts.All[0];

        DeveloperToggle.IsOn = s.DeveloperMode;
        DiagnosticsCard.Visibility = s.DeveloperMode ? Visibility.Visible : Visibility.Collapsed;

        _loading = false;
    }

    /// <summary>Snapshot of every control-backed setting, ready to persist.</summary>
    private AppSettings CurrentSettings() => new()
    {
        Theme = ThemeCombo.SelectedIndex >= 0 ? Themes[ThemeCombo.SelectedIndex] : "System",
        Accent = AccentCombo.SelectedItem as string ?? "Default",
        Font = FontCombo.SelectedItem as string ?? AppFonts.All[0],
        DeveloperMode = DeveloperToggle.IsOn,
    };

    private void Setting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        var settings = CurrentSettings();
        _theme.Save(settings);

        if (App.MainWindowHandleHost is MainWindow mw)
            mw.ApplyAppearance(settings.Theme, settings.Accent, settings.Font, rebuild: true);
    }

    private void DeveloperToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _theme.Save(CurrentSettings());
        DiagnosticsCard.Visibility = DeveloperToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        if (!DeveloperToggle.IsOn) DiagnosticsErrorBar.IsOpen = false;
    }

    private async void DebugShell_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsErrorBar.IsOpen = false;
        DebugShellButton.IsEnabled = false;
        try
        {
            var broker = App.Services.GetRequiredService<IBrokerClient>();
            var resp = await broker.SendAsync(new LaunchDebugShellRequest());
            if (!resp.Success)
            {
                DiagnosticsErrorBar.Message = string.IsNullOrWhiteSpace(resp.Error)
                    ? "Could not open the debug shell." : resp.Error;
                DiagnosticsErrorBar.IsOpen = true;
            }
        }
        catch (System.Exception ex)
        {
            DiagnosticsErrorBar.Message = $"Could not open the debug shell: {ex.Message}";
            DiagnosticsErrorBar.IsOpen = true;
        }
        finally
        {
            DebugShellButton.IsEnabled = true;
        }
    }

    /// <summary>Guarded danger-zone flow: list affected distros, require an explicit checkbox
    /// acknowledgement AND a typed "UNINSTALL" before the primary button enables. The close
    /// button stays the default so Enter never triggers the destructive action.</summary>
    private async void UninstallWsl_Click(object sender, RoutedEventArgs e)
    {
        WslPackageInfoBar.IsOpen = false;
        // Disabled for the whole flow: a second click while the dialog (or the distro listing)
        // is pending would try to open a second ContentDialog, which throws.
        UninstallWslButton.IsEnabled = false;
        try
        {
            await RunUninstallWslFlowAsync();
        }
        finally
        {
            UninstallWslButton.IsEnabled = true;
        }
    }

    private async System.Threading.Tasks.Task RunUninstallWslFlowAsync()
    {
        var content = new StackPanel { Spacing = 12, MinWidth = 400 };
        content.Children.Add(new TextBlock
        {
            Text = "This removes the WSL platform itself from this machine. It is NOT the same " +
                   "as unregistering a single distribution — every installed distribution will " +
                   "stop working until WSL is reinstalled.",
            TextWrapping = TextWrapping.Wrap,
        });

        var distroList = new TextBlock { TextWrapping = TextWrapping.Wrap };
        try
        {
            var distros = await App.Services.GetRequiredService<WslDistroService>().ListAsync();
            distroList.Text = distros.Count == 0
                ? "No installed distributions were found."
                : "Affected distributions:\n" +
                  string.Join("\n", distros.Select(d => "  • " + d.Name));
        }
        catch (System.Exception) // listing fails when WSL itself is broken — still allow uninstall
        {
            distroList.Text = "Could not list installed distributions (WSL may already be " +
                              "broken). Any installed distributions will still become unavailable.";
        }
        content.Children.Add(distroList);

        var ack = new CheckBox { Content = "I understand all listed distributions will become unavailable" };
        var confirm = new TextBox { Header = "Type \"UNINSTALL\" to confirm", PlaceholderText = "UNINSTALL" };
        content.Children.Add(ack);
        content.Children.Add(confirm);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Uninstall the WSL package?",
            Content = new ScrollViewer { Content = content }, // distro list can be long
            PrimaryButtonText = "Uninstall WSL",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, // never make the destructive action the default
            IsPrimaryButtonEnabled = false,
        };

        void UpdatePrimary() => dialog.IsPrimaryButtonEnabled =
            ack.IsChecked == true
            && string.Equals(confirm.Text, "UNINSTALL", System.StringComparison.Ordinal);
        ack.Checked += (_, _) => UpdatePrimary();
        ack.Unchecked += (_, _) => UpdatePrimary();
        confirm.TextChanged += (_, _) => UpdatePrimary();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var broker = App.Services.GetRequiredService<IBrokerClient>();
            var resp = await broker.SendAsync(new UninstallWslRequest());
            if (resp.Success)
            {
                WslPackageInfoBar.Severity = InfoBarSeverity.Success;
                WslPackageInfoBar.Message = "WSL has been uninstalled. Restart this app — and " +
                                            "possibly Windows — for the change to take full effect.";
            }
            else
            {
                WslPackageInfoBar.Severity = InfoBarSeverity.Error;
                WslPackageInfoBar.Message = string.IsNullOrWhiteSpace(resp.Error)
                    ? "Uninstalling WSL failed." : resp.Error;
            }
        }
        catch (System.Exception ex)
        {
            WslPackageInfoBar.Severity = InfoBarSeverity.Error;
            WslPackageInfoBar.Message = $"Uninstalling WSL failed: {ex.Message}";
        }
        finally
        {
            WslPackageInfoBar.IsOpen = true;
        }
    }
}
