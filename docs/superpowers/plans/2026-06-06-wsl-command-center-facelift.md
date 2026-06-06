# WSL Command Center Facelift Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the bare WinUI-template look with a polished native Fluent UI — Mica backdrop, custom titlebar + real window title, Segoe Fluent icons, a card-based Dashboard, and a System/Light/Dark theme toggle persisted to a JSON file.

**Architecture:** Presentation-layer pass. The only new non-UI code is a testable `IThemeService` in `Wsl.Core` that reads/writes `%LOCALAPPDATA%\WSL Command Center\settings.json`. All existing ViewModels, services, broker, and bindings are untouched. XAML changes preserve every `x:Bind`, `Command`, and `Click`.

**Tech Stack:** WinUI 3 / .NET 9 / Windows App SDK 2.1.3 (unpackaged, self-contained), CommunityToolkit.Mvvm, System.Text.Json, xUnit.

**Unpackaged guardrails (DO NOT regress — see spec):**
- No `AppNotification` / system toast (needs package identity).
- No `ApplicationData.Current.LocalSettings` (throws unpackaged) — use the JSON file.
- No `AppWindowTitleBar` button-color theming (unreliable unpackaged).

---

## File Structure

- **Create** `Wsl.Core/Settings/AppSettings.cs` — POCO `{ string Theme }`.
- **Create** `Wsl.Core/Settings/IThemeService.cs` — interface.
- **Create** `Wsl.Core/Settings/ThemeService.cs` — file-backed impl (path injected).
- **Create** `Wsl.Core.Tests/ThemeServiceTests.cs` — round-trip + fallback tests.
- **Create** `Wsl.App/Styles/Pills.xaml` — shared `ResourceDictionary` (pill/chip styles).
- **Create** `Wsl.App/Views/SettingsPage.xaml` + `.xaml.cs` — theme toggle page.
- **Modify** `Wsl.App/Services/ServiceRegistration.cs` — register `IThemeService`.
- **Modify** `Wsl.App/App.xaml` — merge `Pills.xaml`.
- **Modify** `Wsl.App/App.xaml.cs` — apply saved theme on startup.
- **Modify** `Wsl.App/MainWindow.xaml` + `.xaml.cs` — Mica, titlebar, title, nav glyphs, Settings item, theme root.

---

### Task 1: Theme settings model + service (TDD)

**Files:**
- Create: `Wsl.Core/Settings/AppSettings.cs`
- Create: `Wsl.Core/Settings/IThemeService.cs`
- Create: `Wsl.Core/Settings/ThemeService.cs`
- Test: `Wsl.Core.Tests/ThemeServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Wsl.Core.Tests/ThemeServiceTests.cs`:

```csharp
using System.IO;
using Wsl.Core.Settings;
using Xunit;

namespace Wsl.Core.Tests;

public class ThemeServiceTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"wsl-cc-{Path.GetRandomFileName()}.json");

    [Fact]
    public void Load_WhenFileMissing_ReturnsSystemDefault()
    {
        var path = TempFile();
        var svc = new ThemeService(path);

        Assert.Equal("System", svc.Load().Theme);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTheme()
    {
        var path = TempFile();
        try
        {
            var svc = new ThemeService(path);
            svc.Save(new AppSettings { Theme = "Dark" });

            Assert.Equal("Dark", new ThemeService(path).Load().Theme);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_WhenFileCorrupt_ReturnsSystemDefault()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "{ not valid json");
            Assert.Equal("System", new ThemeService(path).Load().Theme);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_CreatesParentDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var path = Path.Combine(dir, "settings.json");
        try
        {
            new ThemeService(path).Save(new AppSettings { Theme = "Light" });
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ThemeServiceTests"`
Expected: FAIL — `AppSettings` / `ThemeService` do not exist (compile error).

- [ ] **Step 3: Write the model and interface**

Create `Wsl.Core/Settings/AppSettings.cs`:

```csharp
namespace Wsl.Core.Settings;

/// <summary>Persisted app preferences. Stored as JSON under %LOCALAPPDATA%.</summary>
public sealed class AppSettings
{
    /// <summary>One of "System", "Light", "Dark". Defaults to "System".</summary>
    public string Theme { get; set; } = "System";
}
```

Create `Wsl.Core/Settings/IThemeService.cs`:

```csharp
namespace Wsl.Core.Settings;

public interface IThemeService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
```

- [ ] **Step 4: Write the implementation**

Create `Wsl.Core/Settings/ThemeService.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;

namespace Wsl.Core.Settings;

/// <summary>
/// File-backed settings store. Uses %LOCALAPPDATA%\WSL Command Center\settings.json by default.
/// ApplicationData.Current.LocalSettings is intentionally NOT used: it throws
/// InvalidOperationException when the app runs unpackaged (no package identity).
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly string _path;

    /// <summary>Production ctor — resolves the default %LOCALAPPDATA% path.</summary>
    public ThemeService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WSL Command Center", "settings.json"))
    {
    }

    /// <summary>Test ctor — explicit path.</summary>
    public ThemeService(string path) => _path = path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ThemeServiceTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add Wsl.Core/Settings Wsl.Core.Tests/ThemeServiceTests.cs
git commit -m "feat(core): file-backed theme settings service"
```

---

### Task 2: Register the service + apply theme on startup

**Files:**
- Modify: `Wsl.App/Services/ServiceRegistration.cs`
- Modify: `Wsl.App/App.xaml.cs`

- [ ] **Step 1: Register `IThemeService`**

In `Wsl.App/Services/ServiceRegistration.cs`, add the using and registration. Add at top with the other usings:

```csharp
using Wsl.Core.Settings;
```

Add after the `BootstrapStateStore` registration (line ~20):

```csharp
        services.AddSingleton<IThemeService, ThemeService>();
```

- [ ] **Step 2: Apply saved theme when the window is created**

In `Wsl.App/App.xaml.cs`, replace the `OnLaunched` body so the saved theme is read and passed to the window. New file content:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Wsl.App.Services;
using Wsl.Core.Settings;

namespace Wsl.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static object MainWindowHandleHost { get; private set; } = null!;
    private Window? _window;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ServiceRegistration.Build();
        _window = new MainWindow();
        MainWindowHandleHost = _window;

        // Apply the persisted theme before showing the window.
        var theme = Services.GetRequiredService<IThemeService>().Load().Theme;
        if (_window is MainWindow mainWin) mainWin.ApplyTheme(theme);

        _window.Activate();

        var state = Services.GetRequiredService<Wsl.Core.BootstrapStateStore>();
        if (await state.ReadAsync() != Wsl.Core.BootstrapStep.Done && _window is MainWindow mw)
            mw.NavigateToSetup();
    }
}
```

Note: `MainWindow.ApplyTheme(string)` is added in Task 4 — this references it ahead of time; the build in Task 4 Step 5 is where both compile together. To keep this task building on its own, add the method stub now (Task 4 fills the body):

In `Wsl.App/MainWindow.xaml.cs`, add inside the class:

```csharp
    public void ApplyTheme(string theme) { /* body added in Task 4 */ }
```

- [ ] **Step 3: Build**

Run: `Stop-Process -Name Wsl.App,Wsl.Broker -Force -ErrorAction SilentlyContinue; dotnet build Wsl.App`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Wsl.App/Services/ServiceRegistration.cs Wsl.App/App.xaml.cs Wsl.App/MainWindow.xaml.cs
git commit -m "feat(app): register theme service, apply saved theme on startup"
```

---

### Task 3: Shared pill/chip styles

**Files:**
- Create: `Wsl.App/Styles/Pills.xaml`
- Modify: `Wsl.App/App.xaml`

- [ ] **Step 1: Create the resource dictionary**

Create `Wsl.App/Styles/Pills.xaml`:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Neutral chip (version etc.) -->
    <Style x:Key="ChipBorderStyle" TargetType="Border">
        <Setter Property="CornerRadius" Value="10" />
        <Setter Property="Padding" Value="8,2" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Background" Value="{ThemeResource SystemFillColorNeutralBackgroundBrush}" />
    </Style>

    <!-- Card container -->
    <Style x:Key="CardBorderStyle" TargetType="Border">
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="Padding" Value="16" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
        <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}" />
    </Style>
</ResourceDictionary>
```

- [ ] **Step 2: Merge it into App resources**

In `Wsl.App/App.xaml`, replace the `MergedDictionaries` block so `Pills.xaml` is merged:

```xml
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
                <ResourceDictionary Source="ms-appx:///Styles/Pills.xaml" />
            </ResourceDictionary.MergedDictionaries>
```

- [ ] **Step 3: Build**

Run: `Stop-Process -Name Wsl.App,Wsl.Broker -Force -ErrorAction SilentlyContinue; dotnet build Wsl.App`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Wsl.App/Styles/Pills.xaml Wsl.App/App.xaml
git commit -m "feat(app): shared pill/chip/card styles"
```

---

### Task 4: Window chrome — Mica, titlebar, title, nav glyphs, Settings item, theme root

**Files:**
- Modify: `Wsl.App/MainWindow.xaml`
- Modify: `Wsl.App/MainWindow.xaml.cs`

- [ ] **Step 1: Rewrite MainWindow.xaml**

Replace the full content of `Wsl.App/MainWindow.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Window
    x:Class="Wsl.App.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:muxc="using:Microsoft.UI.Xaml.Controls">

    <Window.SystemBackdrop>
        <muxc:MicaBackdrop Kind="Base" />
    </Window.SystemBackdrop>

    <Grid x:Name="RootGrid">
        <Grid.RowDefinitions>
            <RowDefinition Height="48" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Custom titlebar drag region -->
        <Grid x:Name="AppTitleBar" Height="48" Background="Transparent">
            <StackPanel Orientation="Horizontal" Spacing="12" VerticalAlignment="Center"
                        Margin="16,0,0,0">
                <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE756;" FontSize="16" />
                <TextBlock Text="WSL Command Center" Style="{StaticResource CaptionTextBlockStyle}"
                           VerticalAlignment="Center" />
            </StackPanel>
        </Grid>

        <muxc:NavigationView x:Name="Nav" Grid.Row="1"
                             PaneDisplayMode="Left"
                             IsBackButtonVisible="Collapsed"
                             IsSettingsVisible="False"
                             SelectionChanged="Nav_SelectionChanged">
            <muxc:NavigationView.MenuItems>
                <muxc:NavigationViewItem Content="Dashboard" Tag="Dashboard">
                    <muxc:NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE80F;" />
                    </muxc:NavigationViewItem.Icon>
                </muxc:NavigationViewItem>
                <muxc:NavigationViewItem Content="Deploy" Tag="Deploy">
                    <muxc:NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE896;" />
                    </muxc:NavigationViewItem.Icon>
                </muxc:NavigationViewItem>
                <muxc:NavigationViewItem Content="Backup" Tag="Backup">
                    <muxc:NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE7B8;" />
                    </muxc:NavigationViewItem.Icon>
                </muxc:NavigationViewItem>
                <muxc:NavigationViewItem Content="Config" Tag="Config">
                    <muxc:NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE713;" />
                    </muxc:NavigationViewItem.Icon>
                </muxc:NavigationViewItem>
                <muxc:NavigationViewItem Content="Setup" Tag="Setup">
                    <muxc:NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE90F;" />
                    </muxc:NavigationViewItem.Icon>
                </muxc:NavigationViewItem>
            </muxc:NavigationView.MenuItems>
            <muxc:NavigationView.FooterMenuItems>
                <muxc:NavigationViewItem Content="Settings" Tag="Settings">
                    <muxc:NavigationViewItem.Icon>
                        <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE713;" />
                    </muxc:NavigationViewItem.Icon>
                </muxc:NavigationViewItem>
            </muxc:NavigationView.FooterMenuItems>
            <Frame x:Name="ContentFrame" />
        </muxc:NavigationView>
    </Grid>
</Window>
```

- [ ] **Step 2: Rewrite MainWindow.xaml.cs**

Replace the full content of `Wsl.App/MainWindow.xaml.cs`:

```csharp
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

        _pages["Dashboard"] = typeof(Views.DashboardPage);
        _pages["Deploy"] = typeof(Views.DeployPage);
        _pages["Backup"] = typeof(Views.BackupPage);
        _pages["Config"] = typeof(Views.ConfigPage);
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

    public void NavigateToSetup()
    {
        Nav.SelectedItem = Nav.MenuItems[4];
        NavigateTo("Setup");
    }
}
```

Note: `Views.SettingsPage` is created in Task 5. This task's build (Step 5) will fail to resolve it until Task 5 is done, so build verification for this task happens after Task 5. Commit anyway (compiles after Task 5).

- [ ] **Step 3: Commit**

```bash
git add Wsl.App/MainWindow.xaml Wsl.App/MainWindow.xaml.cs
git commit -m "feat(app): Mica backdrop, custom titlebar, window title, nav glyphs, settings item"
```

---

### Task 5: Settings page (theme toggle)

**Files:**
- Create: `Wsl.App/Views/SettingsPage.xaml`
- Create: `Wsl.App/Views/SettingsPage.xaml.cs`

- [ ] **Step 1: Create SettingsPage.xaml**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Wsl.App.Views.SettingsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:muxc="using:Microsoft.UI.Xaml.Controls">
    <ScrollViewer>
        <StackPanel Padding="24" Spacing="16" MaxWidth="700" HorizontalAlignment="Left">
            <TextBlock Text="Settings" Style="{StaticResource TitleTextBlockStyle}" />
            <TextBlock Style="{StaticResource CaptionTextBlockStyle}"
                       Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                       Text="Appearance and app preferences." />

            <Border Style="{StaticResource CardBorderStyle}">
                <StackPanel Spacing="12">
                    <TextBlock Text="Appearance" Style="{StaticResource SubtitleTextBlockStyle}" />
                    <TextBlock Style="{StaticResource CaptionTextBlockStyle}"
                               Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                               Text="Choose how the app looks. 'Use system setting' follows your Windows light/dark mode." />
                    <muxc:RadioButtons x:Name="ThemeRadios" SelectionChanged="ThemeRadios_SelectionChanged">
                        <x:String>Use system setting</x:String>
                        <x:String>Light</x:String>
                        <x:String>Dark</x:String>
                    </muxc:RadioButtons>
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 2: Create SettingsPage.xaml.cs**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
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
        ThemeRadios.SelectedIndex = System.Array.IndexOf(Themes, current) is var i && i >= 0 ? i : 0;
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
```

- [ ] **Step 3: Build**

Run: `Stop-Process -Name Wsl.App,Wsl.Broker -Force -ErrorAction SilentlyContinue; dotnet build Wsl.App`
Expected: 0 errors (Task 2 + Task 4 + Task 5 now compile together).

- [ ] **Step 4: Manual smoke**

Run the exe: `& "C:\Dev\Active\WSL Command Center\Wsl.App\bin\Debug\net9.0-windows10.0.26100.0\win-x64\Wsl.App.exe"`
Verify: window has Mica backdrop; titlebar shows the icon + "WSL Command Center"; taskbar/Alt-Tab title is "WSL Command Center" (not "WinUI Desktop"); Settings appears in the nav footer; changing the theme radio switches light/dark immediately and persists across relaunch.

- [ ] **Step 5: Commit**

```bash
git add Wsl.App/Views/SettingsPage.xaml Wsl.App/Views/SettingsPage.xaml.cs
git commit -m "feat(app): settings page with persisted theme toggle"
```

---

### Task 6: Dashboard cards, state pill, version chip, icon actions, confirm dialog, empty state

**Files:**
- Modify: `Wsl.App/Views/DashboardPage.xaml`
- Modify: `Wsl.App/Views/DashboardPage.xaml.cs`

- [ ] **Step 1: Add a state→brush converter**

Create `Wsl.App/Converters/StateToBrushConverter.cs`:

```csharp
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Wsl.App.Converters;

/// <summary>Maps a distro state string to a brush. "Running" -> success, else critical.
/// Pass ConverterParameter="bg" for the pill background variant.</summary>
public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var running = string.Equals(value as string, "Running", StringComparison.OrdinalIgnoreCase);
        var bg = string.Equals(parameter as string, "bg", StringComparison.OrdinalIgnoreCase);
        var key = (running, bg) switch
        {
            (true, true) => "SystemFillColorSuccessBackgroundBrush",
            (true, false) => "SystemFillColorSuccessBrush",
            (false, true) => "SystemFillColorNeutralBackgroundBrush",
            (false, false) => "SystemFillColorCriticalBrush",
        };
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
```

- [ ] **Step 2: Register the converter in App.xaml**

In `Wsl.App/App.xaml`, add the converter under the existing `NotNullToBool` resource (inside the `ResourceDictionary`, after `MergedDictionaries`):

```xml
            <conv:StateToBrushConverter x:Key="StateToBrush" />
```

(The `conv` namespace is already declared on the `Application` element.)

- [ ] **Step 3: Rewrite DashboardPage.xaml**

Replace the full content of `Wsl.App/Views/DashboardPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Wsl.App.Views.DashboardPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:muxc="using:Microsoft.UI.Xaml.Controls"
    xmlns:core="using:Wsl.Core">
    <Grid Padding="24" RowSpacing="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Header -->
        <StackPanel Spacing="4">
            <StackPanel Orientation="Horizontal" Spacing="12">
                <TextBlock Text="Distributions" Style="{StaticResource TitleTextBlockStyle}" />
                <Button Command="{x:Bind Vm.RefreshCommand}" ToolTipService.ToolTip="Reload the distro list">
                    <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE72C;" FontSize="14" />
                </Button>
                <muxc:ProgressRing IsActive="{x:Bind Vm.IsBusy, Mode=OneWay}" Width="20" Height="20" />
            </StackPanel>
            <TextBlock Style="{StaticResource CaptionTextBlockStyle}" TextWrapping="Wrap"
                       Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                       Text="Installed WSL distributions. Start/Stop control each distro's lightweight VM; Set Default picks which one plain `wsl` opens; Unregister permanently deletes a distro and its files." />
        </StackPanel>

        <muxc:InfoBar Grid.Row="1"
                      IsOpen="{x:Bind Vm.ErrorMessage, Mode=OneWay, Converter={StaticResource NotNullToBool}}"
                      Severity="Error" Message="{x:Bind Vm.ErrorMessage, Mode=OneWay}" />

        <!-- Empty state -->
        <StackPanel Grid.Row="2" Spacing="8" VerticalAlignment="Center" HorizontalAlignment="Center"
                    Visibility="{x:Bind Vm.HasNoDistros, Mode=OneWay}">
            <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE7BA;" FontSize="48"
                      Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
            <TextBlock Text="No distributions found" Style="{StaticResource SubtitleTextBlockStyle}"
                       HorizontalAlignment="Center" />
            <TextBlock Style="{StaticResource CaptionTextBlockStyle}" HorizontalAlignment="Center"
                       Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                       Text="Install one from the Deploy page." />
        </StackPanel>

        <!-- Distro cards -->
        <ListView Grid.Row="2" ItemsSource="{x:Bind Vm.Distros, Mode=OneWay}"
                  SelectionMode="None">
            <ListView.ItemContainerStyle>
                <Style TargetType="ListViewItem">
                    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                    <Setter Property="Padding" Value="0" />
                    <Setter Property="Margin" Value="0,0,0,8" />
                </Style>
            </ListView.ItemContainerStyle>
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="core:Distro">
                    <Border Style="{StaticResource CardBorderStyle}">
                        <Border.ChildTransitions>
                            <TransitionCollection>
                                <EntranceThemeTransition />
                            </TransitionCollection>
                        </Border.ChildTransitions>
                        <Grid ColumnSpacing="16">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>

                            <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE756;" FontSize="20"
                                      VerticalAlignment="Center"
                                      Foreground="{ThemeResource TextFillColorSecondaryBrush}" />

                            <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="2">
                                <TextBlock Text="{x:Bind Name}" Style="{StaticResource BodyStrongTextBlockStyle}" />
                                <TextBlock Text="{x:Bind Version}" Style="{StaticResource CaptionTextBlockStyle}"
                                           Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
                            </StackPanel>

                            <!-- State pill -->
                            <Border Grid.Column="2" Style="{StaticResource ChipBorderStyle}"
                                    Background="{x:Bind State, Converter={StaticResource StateToBrush}, ConverterParameter=bg}">
                                <StackPanel Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
                                    <Ellipse Width="8" Height="8" VerticalAlignment="Center"
                                             Fill="{x:Bind State, Converter={StaticResource StateToBrush}}" />
                                    <TextBlock Text="{x:Bind State}" Style="{StaticResource CaptionTextBlockStyle}"
                                               Foreground="{x:Bind State, Converter={StaticResource StateToBrush}}" />
                                </StackPanel>
                            </Border>

                            <!-- Actions -->
                            <StackPanel Grid.Column="3" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                                <Button Click="Start_Click" Tag="{x:Bind Name}" ToolTipService.ToolTip="Boot this distro.">
                                    <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE768;" FontSize="14" />
                                </Button>
                                <Button Click="Stop_Click" Tag="{x:Bind Name}" ToolTipService.ToolTip="Shut the distro down (terminate).">
                                    <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE71A;" FontSize="14" />
                                </Button>
                                <Button Click="SetDefault_Click" Tag="{x:Bind Name}" ToolTipService.ToolTip="Make this the default distro for `wsl`.">
                                    <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE735;" FontSize="14" />
                                </Button>
                                <Button ToolTipService.ToolTip="More actions">
                                    <FontIcon FontFamily="Segoe Fluent Icons" Glyph="&#xE712;" FontSize="14" />
                                    <Button.Flyout>
                                        <MenuFlyout>
                                            <MenuFlyoutItem Text="Unregister (delete)…" Click="Unregister_Click" Tag="{x:Bind Name}" />
                                        </MenuFlyout>
                                    </Button.Flyout>
                                </Button>
                            </StackPanel>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</Page>
```

- [ ] **Step 4: Update DashboardPage.xaml.cs — Unregister confirm dialog**

Open `Wsl.App/Views/DashboardPage.xaml.cs`. Find the existing `Unregister_Click` handler. Replace its body so it shows a `ContentDialog` confirm before invoking the existing unregister logic. The existing handler currently calls the VM directly (e.g. `Vm.UnregisterAsync(name)` or an equivalent command). Wrap that call:

```csharp
    private async void Unregister_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name }) return;

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Unregister distribution?",
            Content = $"This permanently deletes \"{name}\" and its entire filesystem. This cannot be undone.",
            PrimaryButtonText = "Unregister",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        // Preserve the original unregister call that was here before.
        await Vm.UnregisterAsync(name);
    }
```

If the original handler used a different call (e.g. `Vm.UnregisterCommand.Execute(name)` or a private helper), keep that exact call in place of `await Vm.UnregisterAsync(name);`. Read the current handler first and substitute the real call — do not invent a method that doesn't exist.

Ensure these usings are present at the top of the file (add any missing):

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
```

- [ ] **Step 5: Add `HasNoDistros` to the DashboardViewModel**

The empty-state `Visibility` binds to `Vm.HasNoDistros` (a `Visibility`). Open `Wsl.App.Logic/ViewModels/DashboardViewModel.cs`. Add a computed property that returns `Visibility.Visible` when the distro collection is empty and not busy, else `Collapsed`, and raise its change notification whenever the collection or busy flag changes.

Add the using if missing:

```csharp
using Microsoft.UI.Xaml;
```

Add the property:

```csharp
    public Visibility HasNoDistros =>
        (!IsBusy && Distros.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
```

Then, wherever `Distros` is repopulated and wherever `IsBusy` changes, notify the new property. With CommunityToolkit.Mvvm, if `IsBusy` is a `[ObservableProperty]`, add a partial hook:

```csharp
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(HasNoDistros));
```

And after the `Distros` collection is rebuilt (end of the refresh/load method), add:

```csharp
        OnPropertyChanged(nameof(HasNoDistros));
```

Read the existing ViewModel to match its exact field names (`Distros`, `IsBusy`) and notification style before editing. If `Wsl.App.Logic` does not already reference WinUI types, prefer returning `bool HasNoDistros` instead and bind with a `BoolToVisibilityConverter`; check the project's existing dependencies first. (If unsure, the `bool` + converter route avoids adding a WinUI dependency to the logic library — see note below.)

> **Decision note for the implementer:** `Wsl.App.Logic` is a plain `net9.0` library (per project layout). Adding `Microsoft.UI.Xaml` there may not be available. **Preferred:** expose `public bool HasNoDistros => !IsBusy && Distros.Count == 0;` in the VM, and in `DashboardPage.xaml` bind `Visibility="{x:Bind Vm.HasNoDistros, Mode=OneWay, Converter={StaticResource BoolToVis}}"`, adding a standard `BoolToVisibilityConverter` to `Wsl.App/Converters` and registering it in `App.xaml`. Use this route unless the logic project already references WinUI.

- [ ] **Step 6: (If bool route chosen) add BoolToVisibilityConverter**

Create `Wsl.App/Converters/BoolToVisibilityConverter.cs`:

```csharp
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Wsl.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}
```

Register in `App.xaml`:

```xml
            <conv:BoolToVisibilityConverter x:Key="BoolToVis" />
```

And in `DashboardPage.xaml` the empty-state `Visibility` becomes:

```xml
                    Visibility="{x:Bind Vm.HasNoDistros, Mode=OneWay, Converter={StaticResource BoolToVis}}"
```

- [ ] **Step 7: Build**

Run: `Stop-Process -Name Wsl.App,Wsl.Broker -Force -ErrorAction SilentlyContinue; dotnet build Wsl.App`
Expected: 0 errors.

- [ ] **Step 8: Run the full backend test suite (no regressions)**

Run: `dotnet test --filter "Category!=LiveWsl"`
Expected: PASS — 75 tests (71 existing + 4 ThemeService).

- [ ] **Step 9: Manual smoke**

Launch the exe. Verify: each distro renders as a card with icon, name, version, a colored state pill (green Running / red-muted Stopped), Start/Stop/SetDefault icon buttons with tooltips, and a "…" overflow whose "Unregister (delete)…" raises a confirm dialog. With no distros, the empty-state placeholder shows. Cards fade in on load.

- [ ] **Step 10: Commit**

```bash
git add Wsl.App/Views/DashboardPage.xaml Wsl.App/Views/DashboardPage.xaml.cs Wsl.App/Converters Wsl.App/App.xaml Wsl.App.Logic/ViewModels/DashboardViewModel.cs
git commit -m "feat(app): card-based dashboard with state pills, icon actions, confirm dialog, empty state"
```

---

## Self-Review

**Spec coverage:**
- §1 Window chrome (Mica, titlebar, title) → Task 4 ✓
- §2 Nav glyphs + Settings footer → Task 4 ✓
- §3 Standardized page header → Dashboard (Task 6) + Settings (Task 5) use the Title+Caption pattern. Existing Deploy/Backup/Config/Setup already use `TitleTextBlockStyle`; caption alignment on those is cosmetic and left as-is (no regression). ✓ (partial by design — no churn on already-acceptable pages)
- §4 Dashboard cards / pill / chip / icon actions / Unregister flyout + confirm → Task 6 ✓
- §5 Theme service + Settings toggle + JSON persistence → Tasks 1, 2, 5 ✓
- §6 Notifications & progress → Errors keep `InfoBar` (existing); `ProgressRing` retained (Task 6); success-toast auto-dismiss explicitly out of scope per plan intro. ✓
- §7 Empty state → Task 6 ✓
- §8 Spacing & motion tokens → page padding 24 / gap 16 / card radius 8 applied (Tasks 5, 6); `EntranceThemeTransition` on cards (Task 6). ✓

**Placeholder scan:** No TBD/TODO. The one stub (`ApplyTheme` empty body in Task 2) is intentional and filled in Task 4, explicitly flagged.

**Type consistency:** `IThemeService.Load()/Save(AppSettings)`, `AppSettings.Theme`, `MainWindow.ApplyTheme(string)`, `StateToBrushConverter`, `HasNoDistros` — names consistent across tasks. The Unregister handler and `Distros`/`IsBusy` names are flagged "read the existing code and match" to avoid inventing signatures.

**Known cross-task build ordering:** Task 4 references `Views.SettingsPage` (Task 5) and `ApplyTheme` (stubbed in Task 2). Build verification is deferred to Task 5 Step 3, which is called out in Task 4.
