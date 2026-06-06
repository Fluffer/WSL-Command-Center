# WSL Command Center — Visual Facelift Design

**Date:** 2026-06-06
**Status:** Approved (brainstormed with Ollama council: deepseek-pro, glm, minimax)
**Scope:** Visual/UX facelift only. No backend/service/broker logic changes. Feature backlog (VHDX compact, PowerShell export, scheduling, etc.) is out of scope — separate specs.

## Goal

Move the app from bare WinUI-template look (plain `NavigationView`, plain `ListView` text rows, default "WinUI Desktop" title, no Mica/theming/iconography) to a polished, modern Fluent product using **native WinUI controls only** (no heavy third-party UI). Must remain **unpackaged** (`WindowsPackageType=None`, `SelfContained`).

## Hard constraints

- **Unpackaged**: no API that requires MSIX package identity.
- **Native + WinUI Community Toolkit only** — no heavy third-party UI deps.
- **Preserve every existing binding**: all `x:Bind`, `Command`, and `Click` handlers stay intact. This is a presentation-layer pass; ViewModels and services are untouched except where noted (theme service is new, additive).
- Solo-dev feasible.

## Unpackaged gotchas (council-verified — DO NOT regress)

1. **No `AppNotification` / system toast.** Requires package identity → throws or needs fragile COM-activator registry registration unpackaged. **Use in-app feedback instead** (see Notifications).
2. **No `ApplicationData.Current.LocalSettings`.** Throws `InvalidOperationException` unpackaged. **Persist settings to a JSON file** under `%LOCALAPPDATA%`.
3. **Skip `AppWindowTitleBar` button-color customization** (hover/pressed/inactive). Unreliable unpackaged — let system chrome handle caption buttons.
4. **Mica** works unpackaged on Win11 22H2+; silently falls back to a solid background on older builds — acceptable, no explicit fallback code required.

## Design

### 1. Window chrome
- `MainWindow.SystemBackdrop = new MicaBackdrop()` (Base kind).
- `ExtendsContentIntoTitleBar = true`; `SetTitleBar(TitleBarGrid)` where `TitleBarGrid` is a 48px-tall `Grid` containing an app-icon `FontIcon` (`` Console glyph, 16px) + `TextBlock` "WSL Command Center" (`CaptionTextBlockStyle`).
- `AppWindow.Title = "WSL Command Center"` (taskbar / Alt-Tab). Kills default "WinUI Desktop".
- Do not duplicate the title text in the NavigationView pane header.

### 2. Navigation shell
- Keep `NavigationView`, `PaneDisplayMode="Left"`.
- Replace symbol `Icon=` with `FontIcon` (Segoe Fluent Icons) glyphs:
  - Dashboard ``, Deploy ``, Backup ``, Config ``, Setup ``.
- Add a **footer Settings item** (``) → new `SettingsPage`.

### 3. Standardized page header
Every page gets a consistent header block: `TextBlock` (`TitleTextBlockStyle`) + secondary caption `TextBlock` (`CaptionTextBlockStyle`, `TextFillColorSecondaryBrush`). Existing per-page explanatory text folds into the caption slot.

### 4. Dashboard — distro cards
Replace the plain-row `ListView.ItemTemplate` with a **card**:
- Root `Grid`/`Border`: `CornerRadius=8`, `Background={ThemeResource CardBackgroundFillColorDefaultBrush}`, 1px `BorderBrush={ThemeResource CardStrokeColorDefaultBrush}`, `Padding=16`.
- Left: distro icon (`FontIcon` ``).
- Name in `BodyStrongTextBlockStyle`.
- **State pill**: rounded `Border` (`CornerRadius=10`) with a small colored `Ellipse` dot + state text.
  - Running: bg `SystemFillColorSuccessBackgroundBrush`, dot/text `SystemFillColorSuccessBrush`.
  - Stopped: bg `SystemFillColorNeutralBackgroundBrush`, dot/text `SystemFillColorCriticalBrush` (muted).
- Version chip: small `Border` pill, neutral.
- Primary actions as **icon buttons** with tooltips: Start ``, Stop ``, Set Default ``.
- **Destructive Unregister** moves behind a `` (More) `Button` + `MenuFlyout`; selecting it raises a `ContentDialog` confirm (irreversible — keep the existing tooltip wording).
- Keep all existing `Click` handlers and `Tag="{x:Bind Name}"` wiring.
- Layout: vertical card list (`ListView`). Multi-column grid (`ItemsRepeater` + `UniformGridLayout`) is explicitly deferred.

### 5. Theme service (new, additive)
- New `IThemeService` (interface in `Wsl.Core`, file-based impl) — testable, no UI dependency:
  - Reads/writes `%LOCALAPPDATA%\WSL Command Center\settings.json` via `System.Text.Json`.
  - Setting: `Theme` ∈ {`System`, `Light`, `Dark`}.
- `SettingsPage` exposes a `RadioButtons`/`ComboBox` toggle; selection applies at runtime by setting the root `FrameworkElement.RequestedTheme` (`ElementTheme`) and persists via the service.
- TDD: `IThemeService` round-trip (read default, write, re-read) tested in `Wsl.Core.Tests` with a temp dir — no real `%LOCALAPPDATA%` writes in tests (path injected).

### 6. Notifications & progress
- **No system toast.** Long-op completion → in-app `InfoBar` (Severity=Success, `IsClosable=true`, auto-dismiss via `DispatcherTimer` ~3s) on the relevant page.
- `TeachingTip` only for callouts anchored to a specific control.
- Busy: `ProgressRing`. Export/import long ops: indeterminate `ProgressBar`.
- Errors: keep existing `InfoBar` Severity=Error.

### 7. Empty state (Dashboard)
When `Vm.Distros` is empty: centered `StackPanel` with a 48px `FontIcon` (``, `TextFillColorSecondaryBrush`), `TextBlock` "No distributions found" (`SubtitleTextBlockStyle`), and a "Deploy a distro" `Button` navigating to Deploy.

### 8. Spacing & motion tokens (apply across all pages)
- Page padding `24`; section gap `16`; control gap `8`/`12`; card radius `8`; border `1px`; titlebar height `48`.
- `NavigationView` default page transition; `EntranceThemeTransition` on dashboard cards.

## Testing strategy

- **`IThemeService`**: unit tests in `Wsl.Core.Tests` (path injected to temp dir) — default value, persist+reload round-trip, malformed-file fallback to `System`.
- **XAML/visual changes**: no unit tests (presentation only). Verification via build + manual launch (kill app → `dotnet build Wsl.App` → relaunch), per existing workflow.
- All 71 existing tests must stay green (`dotnet test --filter "Category!=LiveWsl"`).

## Units / boundaries

- `IThemeService` (Wsl.Core): owns settings file I/O + theme value. No UI dependency. Consumers: `SettingsPage` / App startup.
- Card visuals: contained in `DashboardPage.xaml` `DataTemplate`. No new code-behind beyond the existing handlers + the Unregister confirm dialog.
- Titlebar/backdrop: `MainWindow.xaml(.cs)` only.
- Page-header + pill + chip styles: shared `ResourceDictionary` merged in `App.xaml` (one place to tune tokens).

## Out of scope (future specs)
PowerShell-command export, scheduled backups (Task Scheduler), VHDX compact, port-proxy UI, systemd manager, file-explorer integration, perf dashboard, snapshots, health check. Tracked separately.
