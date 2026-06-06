using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    /// <summary>Applies a persisted theme string ("System"/"Light"/"Dark") to the UI root.</summary>
    public void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
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
