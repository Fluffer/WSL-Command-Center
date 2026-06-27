# Status-Preserving Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every WSL export (manual backup, snapshot, scheduled) succeed regardless of VM state by shutting WSL down first, then restore each distro's prior running/stopped status — with a warning+confirm for interactive exports and silent operation for scheduled ones.

**Architecture:** A small `StatePreservingExport` orchestrator (capture running → `wsl --shutdown` → export delegate → restart-in-`finally`) is reused by manual backup and snapshot create. The scheduled path inlines the same routine into its generated PowerShell. Pages warn the user (listing affected distros) before invoking interactive exports.

**Tech Stack:** .NET 9, WinUI 3, CommunityToolkit.Mvvm, xUnit (`Wsl.Core.Tests`).

## Global Constraints

- Target `net9.0-windows10.0.26100.0`.
- Only `wsl --shutdown` releases distro VHDs (confirmed: `--terminate <distro>` does NOT). Export must always be preceded by shutdown.
- Status invariant: every distro running before an export is running after; stopped stays stopped. Restart runs in a `finally` so it happens even if export fails.
- Interactive (manual backup + snapshot create): warn + list ALL running distros, Proceed/Cancel, destructive-safe default button (`DefaultButton = ContentDialogButton.Close`). Scheduled: silent.
- All wsl/schtasks access via `IProcessRunner` in services; `FakeProcessRunner` (`.Enqueue(int exitCode, string stdOut)`, `.AllArgs` list of `string[]`, `.LastArgs`) for tests.
- TDD: failing test → implement → green. Commit per task. Build `dotnet build WslCommandCenter.sln -c Debug`; test `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj`.

---

## Task 1: `StatePreservingExport` orchestrator

**Files:**
- Create: `Wsl.Core/StatePreservingExport.cs`
- Modify: `Wsl.App/Services/ServiceRegistration.cs` (register singleton)
- Test: `Wsl.Core.Tests/StatePreservingExportTests.cs`

**Interfaces:**
- Consumes: `WslDistroService` — `Task<IReadOnlyList<Distro>> ListAsync(ct)` (`Distro` has `.Name`, `.State` of enum `DistroState.Running`), `Task ShutdownAsync(ct)` (`wsl --shutdown`), `Task StartAsync(string name, ct)` (`wsl -d <name> -- true`).
- Produces: `StatePreservingExport` with `Task<IReadOnlyList<string>> RunningAsync(ct = default)` and `Task<IReadOnlyList<string>> RunAsync(Func<CancellationToken,Task> export, ct = default)`.

- [ ] **Step 1: Write the failing test**

`Wsl.Core.Tests/StatePreservingExportTests.cs`:
```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class StatePreservingExportTests
{
    // " NAME STATE VERSION " verbose output: Ubuntu running, Debian stopped.
    private const string ListVerbose =
        "  NAME      STATE     VERSION\n* Ubuntu    Running   2\n  Debian    Stopped   2\n";

    [Fact]
    public async Task RunAsync_Shuts_Down_Then_Exports_Then_Restarts_Only_Running()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListVerbose); // ListAsync inside RunningAsync
        var sp = new StatePreservingExport(new WslDistroService(runner));

        var exportCalls = 0;
        var restored = await sp.RunAsync(_ => { exportCalls++; return Task.CompletedTask; });

        Assert.Equal(1, exportCalls);
        Assert.Equal(new[] { "Ubuntu" }, restored); // only the running one
        var flat = runner.AllArgs;
        // order: --list --verbose, --shutdown, then -d Ubuntu -- true
        Assert.Contains(flat, a => a.Length >= 2 && a[0] == "--list" && a[1] == "--verbose");
        Assert.Contains(flat, a => a.Length == 1 && a[0] == "--shutdown");
        Assert.Contains(flat, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Ubuntu");
        // Debian was stopped -> never restarted
        Assert.DoesNotContain(flat, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Debian");
    }

    [Fact]
    public async Task RunAsync_Restarts_Even_When_Export_Throws()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListVerbose);
        var sp = new StatePreservingExport(new WslDistroService(runner));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sp.RunAsync(_ => throw new InvalidOperationException("boom")));

        // finally still restarted Ubuntu
        Assert.Contains(runner.AllArgs, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Ubuntu");
    }

    [Fact]
    public async Task RunningAsync_Returns_Only_Running()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListVerbose);
        var sp = new StatePreservingExport(new WslDistroService(runner));
        Assert.Equal(new[] { "Ubuntu" }, await sp.RunningAsync());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter StatePreservingExportTests`
Expected: FAIL — `StatePreservingExport` does not exist.

- [ ] **Step 3: Implement the orchestrator**

`Wsl.Core/StatePreservingExport.cs`:
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

    /// <summary>Names of distros currently Running (used by the UI to warn before Run).</summary>
    public async Task<IReadOnlyList<string>> RunningAsync(CancellationToken ct = default) =>
        (await _distros.ListAsync(ct))
            .Where(d => d.State == DistroState.Running).Select(d => d.Name).ToList();

    /// <summary>
    /// Shuts WSL down, runs <paramref name="export"/>, then restarts every distro that
    /// was running. Restart runs even if export throws. Returns the restarted names.
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

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter StatePreservingExportTests`
Expected: PASS (3/3).

- [ ] **Step 5: Register in DI**

In `Wsl.App/Services/ServiceRegistration.cs`, alongside the other Core singletons, add:
```csharp
services.AddSingleton<StatePreservingExport>();
```

- [ ] **Step 6: Build + commit**

Run: `dotnet build WslCommandCenter.sln -c Debug`
```bash
git add Wsl.Core/StatePreservingExport.cs Wsl.Core.Tests/StatePreservingExportTests.cs Wsl.App/Services/ServiceRegistration.cs
git commit -m "feat(core): StatePreservingExport (shutdown/export/restore-running)"
```

---

## Task 2: Scheduled backup script — status-preserving

**Files:**
- Modify: `Wsl.Core/Scheduling/WslScheduleService.cs` (`BuildScript`)
- Test: `Wsl.Core.Tests/` — extend the existing schedule tests (find the file that tests `BuildScript`; if none, create `WslScheduleBuildScriptTests.cs`)

**Interfaces:**
- Consumes/Produces: `WslScheduleService.BuildScript(BackupSchedule s)` returns the generated `.ps1` text (signature unchanged). `BackupSchedule` has `.DistroName`, `.Folder`, `.Format` (`ExportFormat`), `.KeepCount`, `.Frequency`, `.Time`.

- [ ] **Step 1: Write the failing test**

Add to the schedule test file:
```csharp
[Fact]
public void BuildScript_IsStatePreserving()
{
    var svc = new WslScheduleService(new FakeProcessRunner(), Path.GetTempPath());
    var s = new Wsl.Core.Scheduling.BackupSchedule(
        "Ubuntu", @"C:\backups", ExportFormat.Tar, KeepCount: 3,
        Wsl.Core.Scheduling.ScheduleFrequency.Daily, "02:00");
    var script = svc.BuildScript(s);

    Assert.Contains("[Console]::OutputEncoding", script);          // UTF-16 fix
    Assert.Contains("--list --running --quiet", script);           // capture running
    Assert.Contains("wsl.exe --shutdown", script);                 // release VHDs
    Assert.Contains("--export 'Ubuntu' $out --format tar", script);// export
    Assert.Contains("finally", script);                            // restart wrapper
    Assert.Contains("-- true", script);                            // restart command
    // prune sits inside try (before finally) so a failed export keeps old backups
    Assert.True(script.IndexOf("Remove-Item", StringComparison.Ordinal)
                < script.IndexOf("finally", StringComparison.Ordinal));
}
```
Adjust the `BackupSchedule` constructor call to the type's actual shape if it differs (check `Wsl.Core/Scheduling/BackupSchedule.cs`); keep the assertions.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter BuildScript`
Expected: FAIL — current script has no shutdown/running-capture/finally.

- [ ] **Step 3: Rewrite `BuildScript`**

Replace the body of `BuildScript` in `Wsl.Core/Scheduling/WslScheduleService.cs` with:
```csharp
public string BuildScript(BackupSchedule s)
{
    var name = PsQuote(s.DistroName);
    var folder = PsQuote(s.Folder);
    var ext = Extension(s.Format);
    var fmt = FormatFlag(s.Format);
    var prefix = PsQuote(s.DistroName + "-");
    var glob = PsQuote(s.DistroName + "-*." + ext);
    return string.Join("\r\n", new[]
    {
        "$ErrorActionPreference = 'Stop'",
        "[Console]::OutputEncoding = [System.Text.Encoding]::Unicode",
        "$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'",
        $"$out = Join-Path {folder} ({prefix} + $stamp + '.{ext}')",
        // Capture running distros (names only; trim + drop blanks for the UTF-16 output).
        "$running = @(wsl.exe --list --running --quiet | " +
            "ForEach-Object { $_.Trim() } | Where-Object { $_ })",
        "wsl.exe --shutdown",
        "try {",
        $"    wsl.exe --export {name} $out --format {fmt}",
        $"    Get-ChildItem -LiteralPath {folder} -Filter {glob} | " +
            $"Sort-Object LastWriteTime -Descending | Select-Object -Skip {s.KeepCount} | " +
            "Remove-Item -Force",
        "}",
        "finally {",
        "    foreach ($d in $running) { wsl.exe -d $d -- true }",
        "}",
        "",
    });
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter BuildScript`
Expected: PASS. Also run the full schedule test class to confirm no existing BuildScript assertion regressed: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter Schedule`.

- [ ] **Step 5: Commit**

```bash
git add Wsl.Core/Scheduling/WslScheduleService.cs Wsl.Core.Tests
git commit -m "feat(schedule): status-preserving backup script (shutdown + restart running)"
```

---

## Task 3: Snapshot create — route through the orchestrator

**Files:**
- Modify: `Wsl.Core/Snapshots/WslSnapshotService.cs` (ctor + `CreateAsync`)
- Modify: `Wsl.App/Services/ServiceRegistration.cs` (snapshot registration gains the dependency)
- Modify: `Wsl.Core.Tests/WslSnapshotServiceTests.cs` and `Wsl.Core.Tests/SnapshotViewModelTests.cs` (`Build` helpers construct the new ctor)
- Test: add a case to `WslSnapshotServiceTests.cs`

**Interfaces:**
- Consumes: `StatePreservingExport.RunAsync(Func<CancellationToken,Task>, ct)` from Task 1.
- Produces: `WslSnapshotService` constructor becomes `(WslDistroService distros, Func<string> storeRootProvider, IProcessRunner runner, StatePreservingExport preserving)`. `CreateAsync` signature unchanged.

- [ ] **Step 1: Write the failing test**

Add to `WslSnapshotServiceTests.cs` (and update its `Build` helper — see Step 3):
```csharp
[Fact]
public async Task Create_ShutsDownBeforeExport()
{
    var runner = new FakeProcessRunner();
    runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n"); // RunningAsync list
    var svc = Build(runner);
    await svc.CreateAsync("Ubuntu", "lbl", 2);

    var flat = runner.AllArgs;
    var shutdownIdx = flat.FindIndex(a => a.Length == 1 && a[0] == "--shutdown");
    var exportIdx = flat.FindIndex(a => a.Length >= 1 && a[0] == "--export");
    Assert.True(shutdownIdx >= 0 && exportIdx > shutdownIdx, "shutdown must precede export");
    // Ubuntu was running -> restarted in finally after export
    Assert.Contains(flat, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Ubuntu");
}
```
`List<string[]>` has no `FindIndex` directly via the array; if `AllArgs` is `List<string[]>`, `FindIndex` works. If it's typed otherwise, use `.ToList().FindIndex(...)`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter WslSnapshotServiceTests`
Expected: FAIL — ctor arity mismatch (Build helper not yet updated) / no shutdown before export.

- [ ] **Step 3: Update ctor, CreateAsync, DI, and both Build helpers**

In `Wsl.Core/Snapshots/WslSnapshotService.cs`:
```csharp
private readonly WslDistroService _distros;
private readonly Func<string> _root;
private readonly IProcessRunner _runner;
private readonly StatePreservingExport _preserving;

public WslSnapshotService(WslDistroService distros, Func<string> storeRootProvider,
    IProcessRunner runner, StatePreservingExport preserving)
{
    _distros = distros;
    _root = storeRootProvider;
    _runner = runner;
    _preserving = preserving;
}
```
Wrap the export in `CreateAsync` (keep the sidecar write AFTER the export succeeds):
```csharp
public async Task<Snapshot> CreateAsync(string distro, string label, int wslVersion,
    CancellationToken ct = default)
{
    var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    var dir = DistroDir(distro);
    var vhdx = Path.Combine(dir, stamp + ".vhdx");

    await _preserving.RunAsync(async c =>
    {
        var r = await _runner.RunAsync("wsl.exe",
            new[] { "--export", distro, vhdx, "--vhd" }, null, c);
        WslErrorMapper.ThrowIfFailed(r, $"Snapshot export {distro}");
    }, ct);

    long bytes = File.Exists(vhdx) ? new FileInfo(vhdx).Length : 0;
    var snap = new Snapshot(distro, label, DateTime.UtcNow, bytes, "vhd", wslVersion,
        vhdx, Path.ChangeExtension(vhdx, ".json"));
    File.WriteAllText(snap.SidecarPath, JsonSerializer.Serialize(snap));
    return snap;
}
```
In `Wsl.App/Services/ServiceRegistration.cs`, update the snapshot registration lambda to pass the orchestrator (resolve it from the provider):
```csharp
services.AddSingleton<Wsl.Core.Snapshots.WslSnapshotService>(sp => new Wsl.Core.Snapshots.WslSnapshotService(
    sp.GetRequiredService<WslDistroService>(),
    () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                       "WslCommandCenter", "Snapshots"),
    sp.GetRequiredService<IProcessRunner>(),
    sp.GetRequiredService<StatePreservingExport>()));
```
Update the `Build` helper in **both** `WslSnapshotServiceTests.cs` and `SnapshotViewModelTests.cs` to construct the 4-arg ctor, e.g.:
```csharp
private WslSnapshotService Build(FakeProcessRunner runner) => new(
    new WslDistroService(runner), () => _root, runner,
    new StatePreservingExport(new WslDistroService(runner)));
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter "WslSnapshotServiceTests|SnapshotViewModelTests"`
Expected: PASS (existing snapshot tests + the new `Create_ShutsDownBeforeExport`). Note the existing `RestoreOverwrite`/`Delete`/round-trip tests must still pass; the running-list enqueue is only needed for tests that reach `CreateAsync`.

If a pre-existing `CreateAsync` test now fails because `RunningAsync` consumes an extra queued result (the `--list`), add a leading `runner.Enqueue(0, "...Stopped...")` (no running distro) to that test so the orchestrator sees an empty running set and issues only shutdown+export (no restart).

- [ ] **Step 5: Build + commit**

Run: `dotnet build WslCommandCenter.sln -c Debug`
```bash
git add Wsl.Core/Snapshots/WslSnapshotService.cs Wsl.App/Services/ServiceRegistration.cs Wsl.Core.Tests/WslSnapshotServiceTests.cs Wsl.Core.Tests/SnapshotViewModelTests.cs
git commit -m "feat(snapshots): create via StatePreservingExport (shutdown + restore running)"
```

---

## Task 4: Manual backup ViewModel integration

**Files:**
- Modify: `Wsl.App.Logic/ViewModels/BackupViewModel.cs`
- Test: `Wsl.Core.Tests/BackupViewModelTests.cs`

**Interfaces:**
- Consumes: `StatePreservingExport.RunAsync`/`RunningAsync` (Task 1); existing `WslBackupService.ExportAsync(name, outPath, ExportFormat, ct)`.
- Produces: `BackupViewModel` ctor gains `StatePreservingExport preserving`; new `Task<IReadOnlyList<string>> RunningDistrosAsync()`; `ExportAsync` runs through the orchestrator.

- [ ] **Step 1: Write the failing test**

Add to `BackupViewModelTests.cs` (mirror its existing construction of `BackupViewModel`):
```csharp
[Fact]
public async Task ExportAsync_ShutsDownBeforeExport_AndRestartsRunning()
{
    var runner = new FakeProcessRunner();
    runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n"); // RunningAsync
    var distros = new WslDistroService(runner);
    var vm = new BackupViewModel(new WslBackupService(runner), distros,
        new WslDeployService(runner), new StatePreservingExport(distros));
    vm.ExportDistro = "Ubuntu"; vm.ExportPath = @"C:\b\u.tar"; vm.ExportFormat = ExportFormat.Tar;

    await vm.ExportAsync();

    var flat = runner.AllArgs;
    var shutdownIdx = flat.FindIndex(a => a.Length == 1 && a[0] == "--shutdown");
    var exportIdx = flat.FindIndex(a => a.Length >= 1 && a[0] == "--export");
    Assert.True(shutdownIdx >= 0 && exportIdx > shutdownIdx);
    Assert.Contains(flat, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Ubuntu"); // restarted
}
```
Match the real `WslDeployService` ctor (`new WslDeployService(runner)`); if it needs different args, copy from the existing `BackupViewModelTests` setup.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter BackupViewModelTests`
Expected: FAIL — ctor arity / export not preceded by shutdown.

- [ ] **Step 3: Implement**

In `BackupViewModel.cs` add the field + ctor param and wrap `ExportAsync`:
```csharp
private readonly StatePreservingExport _preserving;

public BackupViewModel(WslBackupService backup, WslDistroService distros,
    WslDeployService deploy, StatePreservingExport preserving)
{
    _backup = backup;
    _distros = distros;
    _deploy = deploy;
    _preserving = preserving;
}

/// <summary>Running distros — the page warns about these before exporting.</summary>
public Task<IReadOnlyList<string>> RunningDistrosAsync() => _preserving.RunningAsync();
```
Replace the `ExportAsync` body's export call:
```csharp
[RelayCommand]
public async Task ExportAsync()
{
    await Guarded(async () =>
    {
        await _preserving.RunAsync(c => _backup.ExportAsync(ExportDistro, ExportPath, ExportFormat, c));
        StatusMessage = $"Exported {ExportDistro} → {ExportPath}";
    });
}
```
(Keep the rest of the class unchanged.) Update any other `new BackupViewModel(...)` call sites — DI resolves the new param automatically since `StatePreservingExport` is registered (Task 1); only test constructors need the extra arg.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter BackupViewModelTests`
Expected: PASS.

- [ ] **Step 5: Build + commit**

Run: `dotnet build WslCommandCenter.sln -c Debug`
```bash
git add Wsl.App.Logic/ViewModels/BackupViewModel.cs Wsl.Core.Tests/BackupViewModelTests.cs
git commit -m "feat(backup): route manual export through StatePreservingExport"
```

---

## Task 5: `PowerShellExporter.Export` preview matches the new sequence

**Files:**
- Modify: `Wsl.Core/Scripting/PowerShellExporter.cs` (`Export`)
- Test: `Wsl.Core.Tests/` — the file testing `PowerShellExporter` (find it; if none tests `Export`, add `PowerShellExporterTests.cs`)

**Interfaces:**
- Produces: `PowerShellExporter.Export(string name, string outPath, ExportFormat fmt)` returns a multi-line `\r\n` string mirroring the status-preserving routine (signature unchanged).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Export_PreviewIsStatePreserving()
{
    var x = new Wsl.Core.Scripting.PowerShellExporter();
    var s = x.Export("Ubuntu", @"C:\b\u.tar", ExportFormat.Tar);
    Assert.Contains("--list --running --quiet", s);
    Assert.Contains("wsl.exe --shutdown", s);
    Assert.Contains("--export Ubuntu", s);
    Assert.Contains("-- true", s);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter "PowerShellExporter|Export_Preview"`
Expected: FAIL — current `Export` is a single `--export` line.

- [ ] **Step 3: Implement**

Replace `Export` in `PowerShellExporter.cs`:
```csharp
public string Export(string name, string outPath, ExportFormat fmt) => string.Join("\r\n", new[]
{
    "$running = @(wsl.exe --list --running --quiet | ForEach-Object { $_.Trim() } | Where-Object { $_ })",
    "wsl.exe --shutdown",
    "try {",
    $"    wsl.exe --export {Q(name)} {Q(outPath)} --format {FormatFlag(fmt)}",
    "}",
    "finally {",
    "    foreach ($d in $running) { wsl.exe -d $d -- true }",
    "}",
});
```
(`Q` and `FormatFlag` already exist in the class.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter "PowerShellExporter|Export_Preview"`
Expected: PASS. If an existing test asserted the old single-line `Export` output, update it to the new sequence.

- [ ] **Step 5: Commit**

```bash
git add Wsl.Core/Scripting/PowerShellExporter.cs Wsl.Core.Tests
git commit -m "feat(scripting): Export preview mirrors status-preserving sequence"
```

---

## Task 6: Page warning dialogs (Backup + Snapshot)

**Files:**
- Modify: `Wsl.App/Views/BackupPage.xaml.cs` (export handler)
- Modify: `Wsl.App.Logic/ViewModels/SnapshotViewModel.cs` (add `RunningDistrosAsync`)
- Modify: `Wsl.App/Views/SnapshotPage.xaml.cs` (create handler)
- Test: `Wsl.Core.Tests/SnapshotViewModelTests.cs` (RunningDistrosAsync)

**Interfaces:**
- Consumes: `BackupViewModel.RunningDistrosAsync()` (Task 4); `WslDistroService.ListAsync` via the snapshot VM's existing `_distros`.
- Produces: `SnapshotViewModel.RunningDistrosAsync()` returning running names; both pages prompt before invoking their export/create command.

- [ ] **Step 1: Add `SnapshotViewModel.RunningDistrosAsync` (TDD)**

Test in `SnapshotViewModelTests.cs`:
```csharp
[Fact]
public async Task RunningDistrosAsync_ReturnsRunning()
{
    var runner = new FakeProcessRunner();
    runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n  Debian  Stopped  2\n");
    var vm = new SnapshotViewModel(Build(runner), new WslDistroService(runner));
    Assert.Equal(new[] { "Ubuntu" }, await vm.RunningDistrosAsync());
}
```
Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter RunningDistrosAsync` → FAIL.

In `SnapshotViewModel.cs` add (uses the existing `_distros` field):
```csharp
public async Task<IReadOnlyList<string>> RunningDistrosAsync() =>
    (await _distros.ListAsync())
        .Where(d => d.State == Wsl.Core.DistroState.Running).Select(d => d.Name).ToList();
```
Run the filter again → PASS.

- [ ] **Step 2: BackupPage export handler — warn before export**

In `Wsl.App/Views/BackupPage.xaml.cs`, find the handler that invokes the export command (the button bound to `ExportCommand`; if export is invoked purely via XAML `Command`, add a `Click` handler instead and drop the direct `Command` binding so the dialog can gate it). Implement:
```csharp
private async void Export_Click(object sender, RoutedEventArgs e)
{
    var running = await Vm.RunningDistrosAsync();
    if (running.Count > 0)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Stop running distros for backup?",
            Content = $"This briefly stops ALL running distros ({string.Join(", ", running)}) " +
                      "and restarts them after the backup completes. Continue?",
            PrimaryButtonText = "Stop & back up",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
    }
    await Vm.ExportCommand.ExecuteAsync(null);
}
```
Ensure the export button calls `Export_Click` (not `Command="{x:Bind Vm.ExportCommand}"`). Mirror the `using Microsoft.UI.Xaml.Controls;` and dialog pattern already used in `DashboardPage.xaml.cs`.

- [ ] **Step 3: SnapshotPage create handler — warn before create**

In `Wsl.App/Views/SnapshotPage.xaml.cs`, change the Create button to a `Click` handler (drop its direct `CreateCommand` binding):
```csharp
private async void Create_Click(object sender, RoutedEventArgs e)
{
    var running = await Vm.RunningDistrosAsync();
    if (running.Count > 0)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Stop running distros for snapshot?",
            Content = $"This briefly stops ALL running distros ({string.Join(", ", running)}) " +
                      "and restarts them after the snapshot completes. Continue?",
            PrimaryButtonText = "Stop & snapshot",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
    }
    await Vm.CreateCommand.ExecuteAsync(null);
}
```
Update `SnapshotPage.xaml` so the Create button uses `Click="Create_Click"` instead of `Command="{x:Bind Vm.CreateCommand}"`.

- [ ] **Step 4: Build + commit**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj --filter RunningDistrosAsync` (PASS) then `dotnet build WslCommandCenter.sln -c Debug` (0 errors).
```bash
git add Wsl.App/Views/BackupPage.xaml.cs Wsl.App/Views/SnapshotPage.xaml Wsl.App/Views/SnapshotPage.xaml.cs Wsl.App.Logic/ViewModels/SnapshotViewModel.cs Wsl.Core.Tests/SnapshotViewModelTests.cs
git commit -m "feat(app): warn + confirm before stopping distros for backup/snapshot"
```

---

## Task 7: Full verification + GUI smoke

**Files:** none (verification only)

- [ ] **Step 1: Full suite**

Run: `dotnet test Wsl.Core.Tests/Wsl.Core.Tests.csproj`
Expected: all PASS (216 prior + new). Fix root causes, not tests.

- [ ] **Step 2: Clean build**

Run: `dotnet build WslCommandCenter.sln -c Debug`
Expected: 0 errors.

- [ ] **Step 3: Manual GUI smoke**

Build the `win-x64` exe (`dotnet build Wsl.App/Wsl.App.csproj -c Debug -r win-x64`), launch it. With a running distro: open Backup → Export → confirm the warning dialog lists the running distro(s) → Proceed → verify the export file is produced and the distro is running again afterward (`wsl -l -v`). Repeat for Snapshots → Create. Confirm Cancel aborts with no shutdown.

- [ ] **Step 4: Commit any fixups**

```bash
git add -A
git commit -m "test: verify status-preserving export suite + smoke"
```

---

## Self-Review Notes

- **Spec coverage:** orchestrator (T1), scheduled script (T2), snapshot create (T3), manual backup VM (T4), copy-PS preview (T5), page warnings + snapshot VM running peek (T6), verify+smoke (T7). All spec sections mapped.
- **Type consistency:** `RunAsync(Func<CancellationToken,Task>, ct)` and `RunningAsync(ct)` used identically across T1/T3/T4; `WslSnapshotService` 4-arg ctor consistent in T3 service + DI + both test `Build` helpers; `BackupViewModel` 4-arg ctor consistent T4.
- **Assumptions flagged inline:** exact `BackupSchedule` ctor shape (T2); `WslDeployService` ctor in `BackupViewModelTests` (T4); whether `FakeProcessRunner.AllArgs` supports `FindIndex` (use `.ToList()` if not); whether the Backup/Snapshot buttons currently bind `Command` directly (switch to `Click`).
