using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Wsl.App;

public sealed partial class MainWindow : Window
{
    // Page tasks (17-21) each add one entry, e.g. ["Dashboard"] = typeof(Views.DashboardPage).
    private readonly Dictionary<string, Type> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        _pages["Dashboard"] = typeof(Views.DashboardPage);
        _pages["Deploy"] = typeof(Views.DeployPage);
        _pages["Backup"] = typeof(Views.BackupPage);
        _pages["Config"] = typeof(Views.ConfigPage);
        Nav.SelectedItem = Nav.MenuItems[0];
        NavigateTo("Dashboard");
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
        Nav.SelectedItem = Nav.MenuItems[4];
        NavigateTo("Setup");
    }
}
