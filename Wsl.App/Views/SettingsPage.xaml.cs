using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wsl.App.Theming;
using Wsl.Contracts;
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
                DiagnosticsErrorBar.Message = resp.Error ?? "Could not open the debug shell.";
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
}
