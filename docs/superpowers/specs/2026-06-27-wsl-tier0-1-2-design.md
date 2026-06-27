# WSL Command Center — Tier 0/1/2 Feature Expansion

**Date:** 2026-06-27
**Status:** Approved design, pending implementation plan

## Goal

Close the gap between what the GUI exposes and what `wsl.exe` / `.wslconfig` /
`wsl.conf` actually support. An ollama council review of the existing app
(unanimous across DeepSeek-Pro, GLM, Qwen-Coder) found the highest-value missing
capabilities are **runtime diagnostics and snapshots**, not more config toggles.
This work ships those (Tier 0) plus the meaningful config keys still unexposed
(Tier 1/2).

## Non-goals

- Incremental / diff snapshots (full export only this phase).
- True kernel-accurate per-distro CPU accounting (WSL2 runs all distros in one
  shared VM; per-distro numbers are poll-delta estimates, labelled "approx").
- Bridged networking management, custom kernel build tooling.
- Tier 3 CLI niceties (`--list --running` view, `--manage --resize`, install
  variants) — deferred.

## Architecture

Follows the established pattern, no deviation:

- **Services** live in `Wsl.Core`, depend only on `IProcessRunner` (+ filesystem
  via injected path providers) so they are unit-testable with `FakeProcessRunner`.
- **ViewModels** live in `Wsl.App.Logic/ViewModels`.
- **Pages** live in `Wsl.App/Views`, resolve their VM from `App.Services`.
- **Privileged operations** go through `Wsl.Broker` via `BrokerRequest` records
  in `Wsl.Contracts`, dispatched in `PrivilegedOperations.HandleAsync`.

New nav items in `MainWindow.xaml`: **Monitor**, **Network**, **Snapshots**.

Only operations that genuinely require admin go through the broker. `wsl --shutdown`,
`wsl --terminate`, `netsh interface portproxy show`, and `nvidia-smi` all run
unelevated via the normal `IProcessRunner`.

---

## Tier 0 — Runtime diagnostics + snapshots

### 1. Monitor page

**Files:** `Wsl.Core/WslMonitorService.cs`, `Wsl.App.Logic/ViewModels/MonitorViewModel.cs`,
`Wsl.App/Views/MonitorPage.xaml(.cs)`.

**VM header (always available):**
- CPU% and working-set from the `vmmemWSL` process (fallback `vmmem`) via
  `System.Diagnostics.Process`. CPU% computed from `TotalProcessorTime` delta
  between polls.
- Disk: sum of the WSL `.vhdx` file sizes — swap vhdx (`%TEMP%\swap.vhdx` or
  configured `swapFile`) plus each distro's `ext4.vhdx`. Distro vhdx paths
  discovered from the registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Lxss\*\BasePath`.

**Per-distro rows (running distros only):**
- One combined call per distro per tick to bound process spawns:
  `wsl -d <name> -- sh -c "cat /proc/meminfo; echo ---; cat /proc/stat; echo ---; df -B1 /"`.
- Parse: MemTotal/MemAvailable → used MB; `/proc/stat` first `cpu` line →
  busy/idle jiffies, CPU% from delta vs previous tick; `df` → root fs used/total.
- Running set determined from `wsl --list --verbose` (already wired in
  `WslDistroService`).

**Cadence:** 5s auto-poll while page visible. Start on `Loaded`, stop on
`Unloaded` (dispose timer + cancel in-flight). Manual **Refresh** button.

**Live actions (confirm dialog each, unelevated):**
- **Terminate distro** → `wsl --terminate <name>`.
- **Restart VM** → `wsl --shutdown` (warns all distros stop).

### 2. Network page (Network + GPU sections)

**Files:** `Wsl.Core/WslNetworkService.cs`, `Wsl.App.Logic/ViewModels/NetworkViewModel.cs`,
`Wsl.App/Views/NetworkPage.xaml(.cs)`. GPU probe may be a sibling
`WslGpuService.cs` consumed by the same VM/page.

**Network read (per selected running distro):**
- Distro IP: `wsl -d <name> -- hostname -I`.
- Host/gateway IP as seen from WSL: `wsl -d <name> -- ip route show` → default route gw.
- DNS: `wsl -d <name> -- cat /etc/resolv.conf` (nameservers) + `networkingMode`
  read from `.wslconfig` (via existing `WslConfigService`).
- Port forwards: `netsh interface portproxy show all` (unelevated read) → table
  of listenAddress:listenPort → connectAddress:connectPort.

**GPU read (per selected running distro):**
- `/dev/dxg` presence (`wsl -d <name> -- test -e /dev/dxg`) → WSL GPU passthrough wired.
- `wsl -d <name> -- nvidia-smi --query-gpu=name,driver_version,memory.used,memory.total --format=csv,noheader`
  → GPU name/driver/mem. Absent/non-zero exit → "no NVIDIA GPU detected".
- WSLg status from `.wslconfig` `guiApplications` (default true).
- **GPU re-detect** button re-runs the probe on demand.

**Live actions:**
- **Restart networking** → `wsl --shutdown` (confirm; notes distros restart).
  Unelevated.
- **Delete port forward** → **new broker request** `DeletePortProxyRequest`
  (`ListenAddress`, `ListenPort`) → `netsh interface portproxy delete v4tov4
  listenport=<p> listenaddress=<a>`. Elevation via broker. Confirm dialog.

### 3. Snapshots page

**Files:** `Wsl.Core/WslSnapshotService.cs`, `Wsl.App.Logic/ViewModels/SnapshotViewModel.cs`,
`Wsl.App/Views/SnapshotPage.xaml(.cs)`. Reuses `WslBackupService` export/import.

**Store layout:**
```
%LOCALAPPDATA%\WslCommandCenter\Snapshots\<distro>\<utc-stamp>.vhdx
%LOCALAPPDATA%\WslCommandCenter\Snapshots\<distro>\<utc-stamp>.json   (sidecar)
```
Sidecar JSON: `{ label, distro, createdUtc, bytes, format, wslVersion }`.
Base path overridable via injected provider (testability + user relocation later).

**Operations:**
- **Create** = `wsl --export <distro> <file> --vhd` then write sidecar with file
  size. Optional user label.
- **List** = enumerate sidecars under the store, newest first.
- **Restore — clone** = `wsl --import <newName> <installDir> <file> --vhd`
  (user supplies new name + install dir).
- **Restore — overwrite** = `wsl --unregister <distro>` then
  `wsl --import <distro> <installDir> <file> --vhd`. Hard confirm ("destroys
  current state of `<distro>`"). Refuse if distro running (require terminate first).
- **Delete** = remove `.vhdx` + `.json`.

---

## Tier 1/2 — config keys

Mechanical extension of the existing typed-config model. Each key becomes a typed
property with entries in `Modeled` (global) / the match switch (distro),
`FromIni`, and `ToIni`. The `Passthrough` dictionary already preserves unknown
keys, so round-trip safety is the invariant to test.

### `WslGlobalConfig` (`Wsl.Core/WslGlobalConfig.cs`)

`[wsl2]`: `guiApplications` (bool), `vmIdleTimeout` (int ms), `defaultVhdSize`
(size string), `firewall` (bool), `dnsTunneling` (bool), `dnsProxy` (bool),
`autoProxy` (bool), `kernelCommandLine` (string), `safeMode` (bool),
`debugConsole` (bool), `maxCrashDumpCount` (int), `kernel` (path string),
`kernelModules` (path string).

`[experimental]`: `autoMemoryReclaim` (enum: disabled/gradual/dropCache),
`sparseVhd` (bool), `ignoredPorts` (csv string), `hostAddressLoopback` (bool).

Note: `ignoredPorts` and `hostAddressLoopback` only apply when
`networkingMode=mirrored` — UI shows an inline hint.

### `WslDistroConfig` (`Wsl.Core/WslDistroConfig.cs`)

`[automount]`: `mountFsTab` (bool), `root` (string), `options` (string).
`[interop]`: `enabled` (bool), `appendWindowsPath` (bool).
`[network]`: `generateHosts` (bool), `generateResolvConf` (bool), `dns` (string,
static nameserver).
`[boot]`: `command` (string), `protectBinfmt` (bool).
`[gpu]`: `enabled` (bool).
`[time]`: `useWindowsTimezone` (bool).

### UI

New fields rendered on the existing `ConfigPage` using the established
`SettingsCard` grouping. Global keys grouped under wsl2 / experimental;
distro keys under automount / interop / network / boot / gpu / time.

---

## Contracts changes

`Wsl.Contracts/BrokerMessages.cs`:
```csharp
public record DeletePortProxyRequest(string ListenAddress, int ListenPort) : BrokerRequest;
```
Register in `BrokerJsonContext`. Handle in `PrivilegedOperations`.

---

## Testing

- **WslMonitorService**: parse canned `/proc/meminfo`, `/proc/stat` (two ticks →
  CPU% delta), `df` output via `FakeProcessRunner`; vmmem/vhdx via injected
  providers.
- **WslNetworkService**: parse canned `hostname -I`, `ip route`, `resolv.conf`,
  `netsh portproxy show all`; GPU parse canned `nvidia-smi` CSV + the
  no-GPU/non-zero-exit path.
- **WslSnapshotService**: create→list→delete against a temp store dir; sidecar
  round-trip; overwrite-refuses-when-running guard.
- **WslGlobalConfig / WslDistroConfig**: `FromIni`→`ToIni` round-trip per new key;
  passthrough preservation unaffected.
- **Broker**: `DeletePortProxyRequest` serialization (`ContractsSerializationTests`)
  + handler builds correct `netsh` args.

## Risks / mitigations

- **Process-spawn cost of 5s polling** → single combined `sh -c` call per distro,
  poll only running distros, stop when page hidden.
- **Per-distro CPU accuracy** → explicit "approx" label; it is a delta estimate.
- **Overwrite restore is destructive** → hard confirm + running-distro guard +
  reuse battle-tested export/import.
- **netsh elevation** → only the delete path crosses the broker; reads stay
  unelevated.
