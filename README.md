# WSL Command Center

A native Windows 11 desktop app (WinUI 3 / .NET 9) for managing the Windows Subsystem for Linux end-to-end — from first-time installation to day-to-day distro management — without touching the command line.

## What it does

- **Bootstrap** — install WSL on a machine that doesn't have it: enables the `VirtualMachinePlatform` and `Microsoft-Windows-Subsystem-Linux` Windows features, installs/updates the kernel, sets the default WSL version, and handles the reboot-and-resume flow.
- **Distro lifecycle** — list distros with live state, start, stop/terminate, set the default distro, switch a distro between WSL 1 and WSL 2, unregister, and move a distro to another drive (with preflight space/path guards).
- **Deploy new distros** — install from the online catalog (`wsl --install -d`), or import from a `.tar` / `.vhdx` file (including import-in-place).
- **Backup & restore** — export a distro to `tar`, `tar.gz`, or `vhd` and restore by import.
- **Scheduled backups** — recurring `wsl --export` backups via Windows Task Scheduler, created and managed from the app.
- **Config editing** — edit the global `.wslconfig` and each distro's `/etc/wsl.conf` from a GUI.
- **Disk mounting** — mount and unmount physical disks and VHDs into WSL2 (system disk detection prevents mounting the boot disk).
- **System status & maintenance** — WSL version/status card, kernel updates (including pre-release), default-user management, launch options, a WSL2 debug shell (behind a developer-mode toggle), and guarded uninstall of the WSL package.

## Architecture

The app uses a **least-privilege broker** model: the WinUI 3 UI runs unelevated, and the few operations that genuinely need admin rights (enabling Windows features, kernel updates, disk mounting, uninstall) are sent over a named pipe to a small, separately-elevated broker process.

```
WslCommandCenter.sln
├─ Wsl.Core           class lib        all WSL logic — wraps wsl.exe, no UI, fully testable
├─ Wsl.Contracts      class lib        shared IPC DTOs (the only assembly the broker shares with the app)
├─ Wsl.Broker         console exe      elevated named-pipe server; privileged operations only
├─ Wsl.App.Logic      class lib        ViewModels (CommunityToolkit.Mvvm), UI-framework-free
├─ Wsl.App            WinUI 3 app      XAML views, Fluent / Windows 11 SettingsCard design
├─ Wsl.Core.Tests     xUnit            unit tests (fake process runner — no live WSL needed)
└─ Wsl.Live.Tests     xUnit            integration tests against a real WSL installation
```

Design notes:

- All `wsl.exe` calls go through a single `IProcessRunner` abstraction, so every service is unit-testable with a fake runner. Every invocation has a timeout, so a hung `wsl.exe` surfaces as an error instead of freezing the UI.
- `wsl.exe` emits management output as UTF-16LE; decoding is handled (with BOM sniffing) in one place.
- `Wsl.Contracts` is deliberately tiny: the elevated broker references only it, never the full core library, keeping the privileged attack surface small and auditable.

## Requirements

- Windows 11 (Windows App SDK / WinUI 3)
- .NET 9 SDK to build
- Admin consent (UAC) only for the broker-backed operations listed above

## Build & run

```powershell
dotnet build WslCommandCenter.sln
dotnet run --project Wsl.App
```

## Tests

```powershell
# Unit tests — no WSL required
dotnet test Wsl.Core.Tests

# Integration tests — require a real WSL installation
dotnet test Wsl.Live.Tests
```

## Publishing

`Publish.ps1` produces a distributable build in `dist/`.
