# WSL Command Center — Design Spec

**Date:** 2026-06-06
**Status:** Approved (design phase)
**Author:** brainstormed with Claude Code

## Goal

A native Windows 11 desktop app (WinUI 3 / C# .NET 8) that gives one GUI for managing
WSL end-to-end: first-time install (bootstrap), distro lifecycle, deploying new distros,
manual backup/restore, and editing WSL config.

## Scope (MVP)

All five included:

1. **Full bootstrap** — install WSL on a machine where it is absent: enable
   `VirtualMachinePlatform` + `Microsoft-Windows-Subsystem-Linux` features, install/update
   kernel (`wsl --update`), set default version, handle reboot + resume. Requires admin.
2. **Distro lifecycle** — list + state, start, stop/terminate, set default, set WSL version,
   unregister.
3. **Deploy new distro** — install from online catalog (`wsl --install -d`), import from
   `.tar` / `.vhdx`.
4. **Backup / restore (manual only)** — export to `tar` / `tar.gz` / `vhd`, restore by import.
   Scheduled/automated backups are **deferred** (YAGNI).
5. **Config editor** — `.wslconfig` (global) + `/etc/wsl.conf` (per-distro).

**Explicitly out of scope (MVP):** scheduled backups, multi-user/remote management, WSL1
feature work beyond install flag, distro file-browser, terminal emulator.

## Architecture

**Elevation model:** least-privilege broker. A non-elevated WinUI 3 UI process talks to a
small, separately-elevated **broker** process over a named pipe. Only the few privileged
operations cross the boundary; everything else runs unprivileged directly from a shared core
library.

**Layering:** a UI-free, testable `Wsl.Core` class library wraps all WSL logic. Both the UI
and the broker consume it (the broker uses only a thin slice).

### Solution structure

```
WslCommandCenter.sln
├─ Wsl.Core           (class lib, net8.0)      ← all WSL logic, no UI, fully testable
├─ Wsl.Contracts      (class lib, net8.0)      ← shared IPC DTOs (broker + app reference this)
├─ Wsl.Broker         (console exe, elevated)  ← privileged ops only, named-pipe server
├─ Wsl.App            (WinUI 3, non-elevated)  ← UI, named-pipe client
└─ Wsl.Core.Tests     (xUnit)                  ← unit tests for Core + Contracts
```

**Why `Wsl.Contracts` is a separate assembly (not merged into Core):** the broker runs
elevated. It must reference the *minimum* code. Putting the IPC DTOs in their own tiny
assembly means the broker references `Wsl.Contracts` only — never the full `Wsl.Core` —
keeping the privileged process small and its attack surface auditable.

### Privileged vs non-privileged split

| Privileged (broker) | Non-privileged (app → `Wsl.Core` directly) |
|---|---|
| Enable `VirtualMachinePlatform` + WSL features (DISM) | List distros + state |
| Install / update WSL kernel (`wsl --update`) | Start / stop / terminate / unregister |
| Set system-wide default WSL version (`--set-default-version`) | Export / import (backup/restore) |
| Reboot orchestration | Set default distro, set per-distro WSL version |
| | Edit `.wslconfig` + `wsl.conf` |
| | Deploy new distro (catalog install, import) |

## Component design

### `Wsl.Core`

All WSL access goes through one process abstraction so logic is testable without a live WSL.

```csharp
public interface IProcessRunner {
    Task<ProcessResult> RunAsync(string exe, string[] args,
                                 TimeSpan? timeout = null, CancellationToken ct = default);
}
public record ProcessResult(int ExitCode, string StdOut, string StdErr);
```

**Encoding handling (gotcha):** `wsl.exe` emits its management output as **UTF-16LE** (verified
on WSL 2.4.13 — `wsl -l -v` returns spaced/NUL-interleaved bytes). The real `IProcessRunner`
implementation decodes management commands as UTF-16LE, but hardens against drift: sniff for a
BOM, fall back to UTF-16LE, and keep all decoding in this single place. Linux command output
(`wsl -d <n> -- cat ...`) is read as UTF-8.

**Timeout:** every invocation takes a timeout (default 60s, caller-overridable). A hung
`wsl.exe` (broken distro) surfaces as `WslErrorKind.Timeout` instead of blocking the UI.

#### Services (one file, one responsibility)

**`WslDistroService`** — parses `wsl -l -v` (UTF-16LE, default `*` marker, name may contain
spaces, trailing state/version columns):
```csharp
Task<IReadOnlyList<Distro>> ListAsync(CancellationToken ct = default);
Task StartAsync(string name, CancellationToken ct = default);        // wsl -d <name> -- true
Task TerminateAsync(string name, CancellationToken ct = default);    // wsl --terminate <name>
Task SetDefaultAsync(string name, CancellationToken ct = default);   // wsl --set-default <name>
Task SetVersionAsync(string name, int version, CancellationToken ct = default); // wsl --set-version
Task UnregisterAsync(string name, CancellationToken ct = default);   // wsl --unregister  (DESTRUCTIVE)

public record Distro(string Name, DistroState State, int Version, bool IsDefault);
public enum DistroState { Running, Stopped, Installing, Unknown }
```

**`WslDeployService`**:
```csharp
Task<IReadOnlyList<CatalogEntry>> ListAvailableAsync(CancellationToken ct = default); // wsl -l -o
Task InstallFromCatalogAsync(string name, CancellationToken ct = default);            // wsl --install -d <name> --no-launch
Task ImportTarAsync(string name, string installDir, string tarPath, int version, CancellationToken ct = default);
    // wsl --import <name> <installDir> <tarPath> --version <version>
Task ImportVhdxAsync(string name, string installDir, string vhdxPath, int version, CancellationToken ct = default);
    // wsl --import <name> <installDir> <vhdxPath> --vhd --version <version>   (copies the vhdx)

public record CatalogEntry(string Name, string FriendlyName);
```

**`WslBackupService`** — exact verified flags for WSL 2.4.x:
```csharp
Task ExportAsync(string name, string outPath, ExportFormat fmt, CancellationToken ct = default);
    // wsl --export <name> <outPath> --format <tar|tar.gz|vhd>
Task RestoreAsync(string name, string installDir, string archivePath, ExportFormat sourceFmt,
                  int version, CancellationToken ct = default);
    // tar/tar.gz: wsl --import <name> <installDir> <archivePath> --version <version>
    // vhd:        wsl --import <name> <installDir> <archivePath> --vhd --version <version>

public enum ExportFormat { Tar, TarGz, Vhd }
```
Note: `wsl --import-in-place <name> <file.vhdx>` (mounts an existing ext4 vhdx without copying)
is available but **not** used in MVP — restore always imports a copy to keep the source archive
intact. Documented for future use.

**`WslConfigService`** — global is a Windows file; per-distro lives inside the Linux fs:
```csharp
Task<WslGlobalConfig> ReadGlobalAsync(CancellationToken ct = default);   // %USERPROFILE%\.wslconfig (INI)
Task WriteGlobalAsync(WslGlobalConfig cfg, CancellationToken ct = default);
Task<WslDistroConfig> ReadDistroAsync(string name, CancellationToken ct = default);
    // wsl -d <name> -u root cat /etc/wsl.conf     (distro auto-starts; UTF-8)
Task WriteDistroAsync(string name, WslDistroConfig cfg, CancellationToken ct = default);
    // serialize INI, then: wsl -d <name> -u root tee /etc/wsl.conf   (stdin = file body)
```

**Per-distro config access decision:** read/write `/etc/wsl.conf` via
`wsl -d <name> -u root cat` / `tee`. This requires the distro to be runnable (it auto-starts
for the op). Rationale over the `\\wsl$\` network path: works regardless of 9P share state and
gets root access cleanly via `-u root`. If the distro cannot start, the op surfaces
`WslErrorKind.CommandFailed` with stderr.

**`WslGlobalConfig`** — typed fields for modeled keys plus a passthrough map so unknown keys
round-trip without data loss:
```csharp
public class WslGlobalConfig {
    public string? Memory { get; set; }            // [wsl2] memory
    public int? Processors { get; set; }           // [wsl2] processors
    public string? Swap { get; set; }              // [wsl2] swap
    public string? SwapFile { get; set; }          // [wsl2] swapFile
    public string? Networking { get; set; }        // [wsl2] networkingMode
    public bool? LocalhostForwarding { get; set; } // [wsl2] localhostForwarding
    public bool? NestedVirtualization { get; set; }// [wsl2] nestedVirtualization
    public Dictionary<string, Dictionary<string,string>> Passthrough { get; set; } = new();
        // section -> key -> value, for every key we do not model
}
public class WslDistroConfig {
    public string? DefaultUser { get; set; }   // [user] default
    public bool? Systemd { get; set; }          // [boot] systemd
    public bool? AutomountEnabled { get; set; } // [automount] enabled
    public string? Hostname { get; set; }       // [network] hostname
    public Dictionary<string, Dictionary<string,string>> Passthrough { get; set; } = new();
}
```

#### Error model

```csharp
public enum WslErrorKind {
    NotInstalled, DistroNotFound, AccessDenied, AlreadyExists,
    InvalidArchive, CommandFailed, Timeout
}
public class WslException : Exception {
    public int? ExitCode { get; }
    public string? StdErr { get; }
    public WslErrorKind Kind { get; }
}
```
Services map non-zero exit code + stderr text → `WslErrorKind`. The UI maps `Kind` → friendly
message and exposes raw `StdErr` in a "Details" expander.

### `Wsl.Contracts` (IPC DTOs)

```csharp
public abstract record BrokerRequest;
public record CheckWslInstalledRequest()                : BrokerRequest;
public record EnableFeaturesRequest()                   : BrokerRequest; // VMPlatform + WSL via DISM
public record InstallOrUpdateKernelRequest()            : BrokerRequest; // wsl --update
public record SetDefaultWslVersionRequest(int Version)  : BrokerRequest; // wsl --set-default-version

public record BrokerResponse(
    bool Success,
    string? Error,
    bool RebootRequired,   // true after EnableFeatures requires a restart
    string? Detail);
```
There is **no** "run arbitrary command" request. The broker's accepted vocabulary is exactly
these typed requests.

### `Wsl.Broker` (elevated server)

**Transport:** `NamedPipeServerStream`, pipe name `WslCommandCenter.Broker`, length-prefixed
JSON (`System.Text.Json` source-gen over the `Wsl.Contracts` records), one request → one
response.

**Security — bidirectional authentication (both directions verified):**

1. **Anti-squatting:** broker creates the pipe with the `FirstPipeInstance` option. If the
   name already exists (a malicious process squatted it), creation fails and the broker aborts
   — it never serves on a pre-existing pipe.
2. **Pipe ACL:** `PipeSecurity` grants access to the **current interactive user's SID only**.
   No other account on the box can connect.
3. **Server → client (broker verifies caller):** on connect, broker resolves the client PID
   (`GetNamedPipeClientProcessId`), confirms same user, and checks the client image path +
   Authenticode signature match the expected `Wsl.App` binary. Rejects otherwise.
4. **Client → server (app verifies broker):** after connecting, the App resolves the server
   PID (`GetNamedPipeServerProcessId`) and verifies that process's image path + signature match
   the expected `Wsl.Broker` binary before sending any request. Defeats a same-user fake broker.

**Lifecycle:**
1. App needs a privileged op → probes whether the broker pipe is live.
2. If not, App launches `Wsl.Broker.exe` via `ShellExecute` with the `runas` verb → **one UAC
   prompt**.
3. Broker starts, creates pipe (FirstPipeInstance + user-SID ACL), serves requests.
4. Idle timeout (configurable, default 60s, no requests) → broker exits. Re-elevates next time.
5. App surfaces `Error` / `RebootRequired` to the user.

### Bootstrap reboot/resume (full-install path)

State persists so the flow survives a reboot:

- **State store:** `%LOCALAPPDATA%\WslCommandCenter\bootstrap.json`, holding the next pending
  step: `{ "step": "EnableFeatures" | "RebootPending" | "InstallKernel" | "SetDefaultVersion" | "Done" }`.
- **Flow:**
  1. App detects WSL absent (`CheckWslInstalledRequest` → not installed) → Setup wizard.
  2. `EnableFeaturesRequest` → broker enables features → `RebootRequired=true`. App writes
     `step=RebootPending`, shows "Restart required to finish enabling WSL. Restart now / later."
  3. On next App launch, if `bootstrap.json` shows a pending step, App resumes automatically:
     `InstallOrUpdateKernelRequest` → `SetDefaultWslVersionRequest(2)` → writes `step=Done`,
     clears the wizard.
- App never assumes resume happened in-memory; it always reads the state file on startup.

### `Wsl.App` (WinUI 3 UI)

`NavigationView` shell, MVVM via `CommunityToolkit.Mvvm` (`ObservableObject`, `RelayCommand`),
DI via `Microsoft.Extensions.DependencyInjection`. One ViewModel per page; services injected.
All service calls run off the UI thread. Global busy overlay; errors → `InfoBar` (non-blocking)
or `ContentDialog` (blocking failures).

**Pages:**

1. **Dashboard** — distro list (Name, State badge, WSL version, Default star). Per-row: Start,
   Stop, Set Default, Unregister. Top bar: Refresh + WSL-status chip. Auto-refresh on focus and
   after actions. **Unregister** → confirm dialog requiring the distro name typed.
2. **Deploy** — wizard. Mode: *Install from catalog* (`wsl -l -o`) or *Import from archive*
   (.tar / .tar.gz / .vhdx file picker → name, install dir, version). Progress UI.
3. **Backup** — Export (pick distro, format Tar/TarGz/Vhd, output path) / Restore (name, install
   dir, archive, source format). Long ops: cancelable progress (`CancellationToken`).
4. **Config** — tabs: **Global** (`.wslconfig` typed form + raw advanced key/value grid for
   passthrough) and **Per-distro** (`wsl.conf`, distro dropdown: default user, systemd, automount,
   hostname). Save banner: "Changes apply after `wsl --shutdown`" + a Shutdown WSL button.
5. **Setup** — first-run / on-demand bootstrap wizard (enable features → reboot → resume →
   kernel → default version). Also reachable as "Repair / Update WSL".

## Testing strategy

**`Wsl.Core.Tests` (xUnit)** is primary coverage, driven by a `FakeProcessRunner` returning
canned `ProcessResult` built from **real UTF-16LE-captured fixtures** from actual `wsl.exe`:

- `wsl -l -v` parsing → correct `Distro` list (default star, states, versions, UTF-16/NUL bytes,
  name-with-spaces case).
- `wsl -l -o` catalog parsing.
- `.wslconfig` INI read → modify → write **round-trips unknown passthrough keys** (no data loss).
- `wsl.conf` parse/serialize.
- Exit-code + stderr → correct `WslErrorKind` mapping (incl. Timeout).
- Argument construction per command (export `--format vhd`; import `--vhd` + `--version` order;
  import-in-place not emitted in MVP).
- Bootstrap state-store read/write + resume-step transitions.

**Contracts** — JSON round-trip serialize/deserialize each `BrokerRequest` / `BrokerResponse`.

**Broker** — pipe ACL is set to user SID; FirstPipeInstance squatting attempt fails; wrong-user
and unknown-request-type are rejected. (PID/signature verification covered by an interface seam
so it can be faked in tests.)

**No live-WSL tests in CI** (CI has no WSL). One optional manual integration project marked
`[Trait("Category","LiveWsl")]`, skipped by default, run locally on a real machine.

ViewModels are unit-testable with mocked services; XAML/UI is not auto-tested — manual smoke per
page.

**Method:** TDD throughout — capture fixture → write failing parse/serialize test → implement.
The UTF-16LE fixtures are the backbone of parser reliability.

## Tech stack

- WinUI 3, .NET 8, C#
- `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`
- `System.Text.Json` (source-gen) for IPC
- xUnit for tests
- Named pipes (`System.IO.Pipes`) for IPC; `GetNamedPipeServerProcessId` /
  `GetNamedPipeClientProcessId` (P/Invoke) + Authenticode signature checks for mutual auth
- DISM API / `Dism.exe` for enabling Windows features (broker)

## Open items (acceptable for MVP, noted for later)

- Broker 60s idle → re-UAC on next privileged op (configurable; accepted UX trade-off).
- `--import-in-place` (no-copy vhdx mount) deferred but documented.
- Scheduled backups deferred.
