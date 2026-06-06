using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Wsl.App.Theming;
using Wsl.Core.Settings;

namespace Wsl.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly IThemeService _theme;
    private bool _loading;

    private static readonly string[] Themes = { "System", "Light", "Dark" };

    public SettingsPage()
    {
        InitializeComponent();
        _theme = App.Services.GetRequiredService<IThemeService>();

        _loading = true;
        var s = _theme.Load();

        var ti = System.Array.IndexOf(Themes, s.Theme);
        ThemeCombo.SelectedIndex = ti >= 0 ? ti : 0;

        AccentCombo.ItemsSource = Accents.Names();
        AccentCombo.SelectedItem = System.Array.IndexOf(Accents.Names(), s.Accent) >= 0 ? s.Accent : "Default";

        FontCombo.ItemsSource = AppFonts.All;
        FontCombo.SelectedItem = System.Array.IndexOf(AppFonts.All, s.Font) >= 0 ? s.Font : AppFonts.All[0];

        _loading = false;
    }

    private void Setting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        var settings = new AppSettings
        {
            Theme = ThemeCombo.SelectedIndex >= 0 ? Themes[ThemeCombo.SelectedIndex] : "System",
            Accent = AccentCombo.SelectedItem as string ?? "Default",
            Font = FontCombo.SelectedItem as string ?? AppFonts.All[0],
        };
        _theme.Save(settings);

        if (App.MainWindowHandleHost is MainWindow mw)
            mw.ApplyAppearance(settings.Theme, settings.Accent, settings.Font, rebuild: true);
    }
}
