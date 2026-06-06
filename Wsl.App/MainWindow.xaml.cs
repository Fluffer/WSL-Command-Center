using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Wsl.App;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, Type> _pages = new();

    public MainWindow()
    {
        InitializeComponent();

        // Custom titlebar (no AppWindowTitleBar button-color theming — unreliable unpackaged).
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Title = "WSL Command Center";
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        _pages["Dashboard"] = typeof(Views.DashboardPage);
        _pages["Deploy"] = typeof(Views.DeployPage);
        _pages["Backup"] = typeof(Views.BackupPage);
        _pages["Config"] = typeof(Views.ConfigPage);
        _pages["Schedule"] = typeof(Views.SchedulePage);
        _pages["Setup"] = typeof(Views.SetupPage);
        _pages["Settings"] = typeof(Views.SettingsPage);

        Nav.SelectedItem = Nav.MenuItems[0];
        NavigateTo("Dashboard");
    }

    /// <summary>
    /// Applies theme + accent + font. The theme is either a base theme (System/Light/Dark) or a
    /// color-palette name (Dracula, Nord, …). Accent/palette brushes and the default font family
    /// are overridden at Application scope (so every page/template sees them), the element theme
    /// is set, and when <paramref name="rebuild"/> is true the theme is flipped and restored so
    /// {ThemeResource} consumers re-resolve the new resources.
    /// A palette replaces the Mica backdrop with its solid background and forces the dark base.
    /// </summary>
    public void ApplyAppearance(string theme, string accent, string font, bool rebuild)
    {
        var pal = Theming.Palettes.Resolve(theme);
        Theming.Appearance.OverrideResources(accent, font, pal);

        if (!string.IsNullOrWhiteSpace(font))
            Nav.FontFamily = new FontFamily(font);   // inherited path for non-styled text

        if (pal is not null)
        {
            // Solid palette background; Mica would tint it with the desktop wallpaper.
            // The dark theme's translucent pane/card/text layers compose over this color,
            // so the whole app takes the palette tint without any brush overrides.
            SystemBackdrop = null;
            RootGrid.Background = new SolidColorBrush(pal.Background);
            RootGrid.RequestedTheme = ElementTheme.Dark;   // all palettes are dark-based
        }
        else
        {
            RootGrid.Background = null;
            SystemBackdrop ??= new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
            RootGrid.RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        if (rebuild)
        {
            // Force {ThemeResource} consumers (theme, font, accent buttons) to re-resolve.
            var t = RootGrid.RequestedTheme;
            RootGrid.RequestedTheme = t == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            RootGrid.RequestedTheme = t;

            // Controls capture card/text brushes at construction (StaticResource semantics), so a
            // palette change leaves the visible page half-recolored. Re-create the current page —
            // DEFERRED via the dispatcher, never synchronously: a synchronous re-navigate inside a
            // SelectionChanged handler destroys the combo mid-event and the selectors appear dead
            // (the b909c51 regression). The page reconstructs from the just-saved settings.
            var cur = ContentFrame.CurrentSourcePageType;
            if (cur is not null)
                DispatcherQueue.TryEnqueue(() => ContentFrame.Navigate(cur));
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            NavigateTo(tag);
    }

    private void NavigateTo(string tag)
    {
        if (_pages.TryGetValue(tag, out var pageType))
            ContentFrame.Navigate(pageType);
    }

    /// <summary>Called by App after first-run detection (Task 23).</summary>
    public void NavigateToSetup()
    {
        foreach (var item in Nav.MenuItems)
            if (item is NavigationViewItem { Tag: "Setup" })
            {
                Nav.SelectedItem = item;
                break;
            }
        NavigateTo("Setup");
    }
}
