# Status-Preserving Export (Backup / Snapshot) — Design

**Date:** 2026-06-27
**Status:** Approved design, pending implementation plan

## Problem

`wsl --export <distro> <file> --format vhd` fails with
`Wsl/Service/ERROR_SHARING_VIOLATION` whenever the WSL2 utility VM holds the
distro's `ext4.vhdx`. Empirically confirmed: this happens even when the distro
shows **Stopped** — and `wsl --terminate <distro>` does **not** release the
disk. Only `wsl --shutdown` (which stops the whole VM and therefore **all**
distros) detaches the vhdx. There is no per-distro disk-release lever in
wsl.exe.

Today three export paths run `--export` directly and break on a live VM:
- Manual backup — `BackupViewModel.ExportAsync` → `WslBackupService.ExportAsync`
- Snapshot create — `WslSnapshotService.CreateAsync`
- Scheduled backup — `WslScheduleService.BuildScript` generates a `.ps1` that
  Task Scheduler runs (`wsl --export` inline)

## Goal

Every export (manual, snapshot, scheduled) succeeds regardless of VM state, and
**the running/stopped status of every distro is the same after the export as it
was before.** Manual/interactive exports warn the user first (listing every
distro that will be stopped) and let them cancel; scheduled exports do it
silently.

## Non-goals

- Per-distro disk detach (does not exist in wsl.exe).
- Avoiding the shutdown of unrelated distros (forced by WSL's single-VM
  architecture).
- Changing restore/import behavior.

## Core routine

`capture running → wsl --shutdown → export → restart each that was running`.

The restart runs in a **`finally`**, so distros are always brought back even if
the export throws. Distros that were already Stopped are left Stopped.

### C# orchestrator — `StatePreservingExport` (new)

**File:** `Wsl.Core/StatePreservingExport.cs`

```csharp
namespace Wsl.Core;

/// <summary>
/// Runs an export with the WSL2 VM shut down (the only way to release distro
/// VHDs), then restores the pre-export running/stopped status of every distro.
/// </summary>
public sealed class StatePreservingExport
{
    private readonly WslDistroService _distros;
    public StatePreservingExport(WslDistroService distros) => _distros = distros;

    /// <summary>Distros currently Running — used by the UI to warn before calling Run.</summary>
    public async Task<IReadOnlyList<string>> RunningAsync(CancellationToken ct = default) =>
        (await _distros.ListAsync(ct))
            .Where(d => d.State == DistroState.Running).Select(d => d.Name).ToList();

    /// <summary>
    /// Shuts WSL down, runs <paramref name="export"/>, then restarts every distro
    /// that was running. Restart happens even if export throws. Returns the names
    /// that were running (and therefore restarted).
    /// </summary>
    public async Task<IReadOnlyList<string>> RunAsync(
        Func<CancellationToken, Task> export, CancellationToken ct = default)
    {
        var running = await RunningAsync(ct);
        await _distros.ShutdownAsync(ct);
        try { await export(ct); }
        finally { foreach (var d in running) await _distros.StartAsync(d, ct); }
        return running;
    }
}
```

`WslDistroService` already exposes `ListAsync`, `ShutdownAsync`, and
`StartAsync` (`-d <name> -- true`, which boots the distro back to Running).

Registered as a singleton (stateless; singleton matches the other Core
services) in `Wsl.App/Services/ServiceRegistration.cs`.

## Path integration

### Manual backup
- `BackupViewModel.ExportAsync` wraps its existing call:
  `await _preserving.RunAsync(c => _backup.ExportAsync(ExportDistro, ExportPath, ExportFormat, c), ct);`
- `BackupViewModel` exposes `Task<IReadOnlyList<string>> RunningDistrosAsync()`
  (delegates to `_preserving.RunningAsync`) so the page can build the warning.
- `BackupPage` export handler: query running; if non-empty, show a
  `ContentDialog` (`DefaultButton = ContentDialogButton.Close`) — content:
  *"This will briefly stop ALL running distros (<comma-list>) and restart them
  after the backup."* — only on Primary invoke `ExportCommand`. If empty, invoke
  directly.

### Snapshot create
- `WslSnapshotService.CreateAsync` wraps its `--export --vhd` body in the
  injected `StatePreservingExport`; the sidecar JSON is written **after** the
  export succeeds (filesystem write, no lock). Constructor gains a
  `StatePreservingExport` parameter; DI registration and the
  `SnapshotViewModel`/page are unaffected beyond the new dependency.
- `SnapshotPage` create handler: same running-distro pre-check + warning dialog
  as manual backup before invoking `CreateCommand`.

### Scheduled backup
`WslScheduleService.BuildScript` rewritten to inline the routine and run
silently. Generated script shape:

```powershell
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::Unicode   # wsl.exe emits UTF-16
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path '<folder>' ('<distro>-' + $stamp + '.<ext>')
$running = @(wsl.exe --list --running --quiet |
             ForEach-Object { $_.Trim() } | Where-Object { $_ })
wsl.exe --shutdown
try {
    wsl.exe --export '<distro>' $out --format <fmt>
    Get-ChildItem -LiteralPath '<folder>' -Filter '<distro>-*.<ext>' |
        Sort-Object LastWriteTime -Descending | Select-Object -Skip <keep> |
        Remove-Item -Force
}
finally {
    foreach ($d in $running) { wsl.exe -d $d -- true }
}
```

Notes:
- `--list --running --quiet` yields names only (no header, no `(Default)`
  suffix). The `[Console]::OutputEncoding = Unicode` line is required because
  wsl.exe writes UTF-16; without it Windows PowerShell 5.1 (the `/TR` runner)
  produces null-padded strings and the `foreach` restart misfires. Trimming and
  the `Where-Object { $_ }` filter drop blank lines.
- Prune step unchanged in intent; placed inside `try` so a failed export does
  not delete old good backups, and restart still runs via `finally`.

### Copy-PowerShell preview
`PowerShellExporter.Export(name, outPath, fmt)` updated to emit the same
status-preserving sequence (capture/shutdown/export/restart), preserving its
contract of mirroring exactly what the app runs. Keep it a single returned
string with `\r\n` separators, consistent with `Optimize`/`EnableFeatures`.

## Error handling

- Export failure → `finally` restarts the previously-running distros, then the
  exception propagates (surfaced via the existing `Guarded` InfoBar for
  manual/snapshot; `$ErrorActionPreference='Stop'` for scheduled).
- A restart (`StartAsync`) failure should not mask the primary outcome; it
  surfaces as the operation's error if the export itself succeeded. (Acceptable:
  rare, and the user can start the distro manually.)

## Testing

- **StatePreservingExport** (`FakeProcessRunner`): asserts call order
  list → `--shutdown` → export-delegate → `-d <d> -- true` per running distro;
  restart still issued when the export delegate throws (finally); a distro that
  was Stopped is never restarted; `RunningAsync` filters to Running only.
- **WslScheduleService.BuildScript**: string-content asserts the script contains
  the encoding line, `--list --running --quiet` capture, `wsl.exe --shutdown`,
  the `--export`, the prune inside `try`, and the `finally { foreach ... -- true }`.
- **WslSnapshotService.CreateAsync**: with a fake orchestrator/runner, asserts
  shutdown precedes export and sidecar is written after.
- **BackupViewModel**: `ExportAsync` routes through the orchestrator;
  `RunningDistrosAsync` returns running names.
- Pages: build-verified; manual smoke for the warning dialog + restart.

## Risks

- **Wider blast radius:** backing up one distro stops all running distros
  briefly. Mitigated by the explicit manual warning that lists them; forced by
  WSL architecture for scheduled.
- **UTF-16 parsing in the scheduled script:** addressed by the encoding line +
  trim/filter; covered by a BuildScript content test.
- **Restart leaves distro "Running" but idle:** `-- true` boots it; matches
  prior state semantics (Running). Acceptable.
