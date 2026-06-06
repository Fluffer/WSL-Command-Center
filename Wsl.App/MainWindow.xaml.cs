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
    /// Applies theme + accent + font. Accent brushes and the default font family are overridden
    /// at Application scope (so every page/template sees them), the element theme is set, and when
    /// <paramref name="rebuild"/> is true the current page is re-navigated so templates that bound
    /// via {StaticResource} (which don't react to theme toggles) pick up the new resources.
    /// </summary>
    public void ApplyAppearance(string theme, string accent, string font, bool rebuild)
    {
        Theming.Appearance.OverrideResources(accent, font);

        if (!string.IsNullOrWhiteSpace(font))
            Nav.FontFamily = new FontFamily(font);   // inherited path for non-styled text

        RootGrid.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (rebuild)
        {
            // Force {ThemeResource} consumers (theme, font, accent buttons) to re-resolve.
            // NOTE: do NOT re-navigate the current page — that would destroy the Settings page
            // mid-interaction and make the selectors appear to do nothing. Pages that bind via
            // {StaticResource} simply pick up the new resources the next time they're navigated to.
            var t = RootGrid.RequestedTheme;
            RootGrid.RequestedTheme = t == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            RootGrid.RequestedTheme = t;
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
