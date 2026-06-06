using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Wsl.Core.Settings;

namespace Wsl.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly IThemeService _theme;
    private bool _loading;

    // Index <-> theme-string mapping for the RadioButtons.
    private static readonly string[] Themes = { "System", "Light", "Dark" };

    public SettingsPage()
    {
        InitializeComponent();
        _theme = App.Services.GetRequiredService<IThemeService>();

        _loading = true;
        var current = _theme.Load().Theme;
        var idx = System.Array.IndexOf(Themes, current);
        ThemeRadios.SelectedIndex = idx >= 0 ? idx : 0;
        _loading = false;
    }

    private void ThemeRadios_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeRadios.SelectedIndex < 0) return;

        var theme = Themes[ThemeRadios.SelectedIndex];
        _theme.Save(new AppSettings { Theme = theme });

        // Apply live to the current window.
        if (App.MainWindowHandleHost is MainWindow mw) mw.ApplyTheme(theme);
    }
}
