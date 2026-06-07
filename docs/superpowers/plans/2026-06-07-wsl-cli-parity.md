# WSL CLI Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the remaining wsl.exe capabilities (status, set-default-user, launch options, custom install, import-in-place, move, mount/unmount, debug-shell, uninstall, pre-release updates) in WSL Command Center.

**Architecture:** Each feature follows the established pattern: a method on a `Wsl.Core` service (constructor-injected `IProcessRunner`, tested with `FakeProcessRunner`), exposed through a CommunityToolkit.Mvvm ViewModel in `Wsl.App.Logic`, rendered as Win11 `SettingsCard` UI in `Wsl.App/Views`. Privileged operations (mount, debug-shell, uninstall, update) go through the `Wsl.Broker` named-pipe IPC: new record in `Wsl.Contracts/BrokerMessages.cs` → handler in `Wsl.Broker/PrivilegedOperations.cs` → `IBrokerClient.SendAsync` from the ViewModel.

**Tech Stack:** .NET / WinUI 3 (Windows App SDK), CommunityToolkit.Mvvm, CommunityToolkit SettingsControls, xUnit, named-pipe broker for elevation.

**Branch:** `feature/wsl-cli-parity` (created in Task 0).

---

## Conventions the implementer MUST follow

- **TDD:** write the failing test in `Wsl.Core.Tests` first, run it, watch it fail, implement, watch it pass, commit.
- **Run tests:** `dotnet test Wsl.Core.Tests` from repo root (`C:\Dev\Active\WSL Command Center`).
- **Build check:** `dotnet build WslCommandCenter.sln` must stay clean.
- **wsl.exe invocation:** always through `IProcessRunner.RunAsync("wsl.exe", args, ...)`. Never `Process.Start` inside Wsl.Core (exception: launching an interactive console, which is app-side, see Task 3).
- **Error mapping:** non-zero exit codes throw `WslException` via the same pattern used by existing services (see `Wsl.Core/WslErrorMapper.cs` usage in `WslDistroService.cs`).
- **ViewModels:** `partial class X : ObservableObject`, `[ObservableProperty]` fields, `[RelayCommand]` async methods, services via constructor. Register transient in `Wsl.App/Services/ServiceRegistration.cs`.
- **XAML:** follow the SettingsCard idiom used in `ConfigPage.xaml` / `SettingsPage.xaml`. Re-use existing styles and converters.
- **ViewModel tests** also live in `Wsl.Core.Tests` (see `DashboardViewModelTests.cs`).
- **Broker ops:** new request record in `Wsl.Contracts/BrokerMessages.cs` with `[JsonDerivedType]` discriminator, register in `BrokerJsonContext.cs` if source-generated, handle in `Wsl.Broker/PrivilegedOperations.cs` switch.
- **Commit per task**, conventional commits, message style of repo (`feat(core): ...`, `feat(app): ...`).

---

### Task 0: Branch

- [x] **Step 1:** `git checkout -b feature/wsl-cli-parity` (from master, clean tree). No commit.

---

## Phase 1 — safe, high-frequency

### Task 1: WSL system status + version (`--status`, `--version`) with Dashboard status card

**Files:**
- Create: `Wsl.Core/WslSystemService.cs`
- Create: `Wsl.Core/WslStatus.cs`
- Test: `Wsl.Core.Tests/WslSystemServiceTests.cs`
- Modify: `Wsl.App.Logic/ViewModels/DashboardViewModel.cs` (add status load)
- Modify: `Wsl.App/Views/DashboardPage.xaml` (status InfoBar/card at top)
- Modify: `Wsl.App/Services/ServiceRegistration.cs` (register `WslSystemService`)

- [x] **Step 1: Write failing tests**

```csharp
// Wsl.Core.Tests/WslSystemServiceTests.cs
using Wsl.Core;
using Xunit;

public class WslSystemServiceTests
{
    private const string StatusOutput =
        "Default Distribution: Ubuntu\r\nDefault Version: 2\r\n";

    private const string VersionOutput =
        "WSL version: 2.4.13.0\r\nKernel version: 5.15.167.4-1\r\n" +
        "WSLg version: 1.0.65\r\nWindows version: 10.0.26200.1\r\n";

    [Fact]
    public async Task GetStatusAsync_parses_default_distro_and_version()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, StatusOutput);
        var svc = new WslSystemService(runner);

        var status = await svc.GetStatusAsync();

        Assert.Equal(new[] { "--status" }, runner.LastArgs);
        Assert.Equal("Ubuntu", status.DefaultDistro);
        Assert.Equal(2, status.DefaultVersion);
    }

    [Fact]
    public async Task GetVersionInfoAsync_parses_wsl_and_kernel_versions()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, VersionOutput);
        var svc = new WslSystemService(runner);

        var v = await svc.GetVersionInfoAsync();

        Assert.Equal(new[] { "--version" }, runner.LastArgs);
        Assert.Equal("2.4.13.0", v.WslVersion);
        Assert.Equal("5.15.167.4-1", v.KernelVersion);
    }

    [Fact]
    public async Task GetVersionInfoAsync_exposes_parsed_wsl_version_for_gating()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, VersionOutput);
        var svc = new WslSystemService(runner);

        var v = await svc.GetVersionInfoAsync();

        Assert.True(v.WslVersionParsed >= new Version(2, 0, 14));
    }
}
```

- [x] **Step 2:** Run `dotnet test Wsl.Core.Tests --filter WslSystemServiceTests` — expect FAIL (types missing).

- [x] **Step 3: Implement**

```csharp
// Wsl.Core/WslStatus.cs
namespace Wsl.Core;

public record WslStatus(string? DefaultDistro, int? DefaultVersion, string Raw);

public record WslVersionInfo(string? WslVersion, string? KernelVersion, string? WslgVersion, string Raw)
{
    public Version WslVersionParsed =>
        Version.TryParse(WslVersion, out var v) ? v : new Version(0, 0);
}
```

```csharp
// Wsl.Core/WslSystemService.cs
namespace Wsl.Core;

public class WslSystemService
{
    private readonly IProcessRunner _runner;
    public WslSystemService(IProcessRunner runner) => _runner = runner;

    public async Task<WslStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var r = await _runner.RunAsync("wsl.exe", new[] { "--status" }, null, ct);
        WslErrorMapper.ThrowIfFailed(r); // match existing services' error pattern
        return ParseStatus(r.StdOut);
    }

    public async Task<WslVersionInfo> GetVersionInfoAsync(CancellationToken ct = default)
    {
        var r = await _runner.RunAsync("wsl.exe", new[] { "--version" }, null, ct);
        WslErrorMapper.ThrowIfFailed(r);
        return ParseVersion(r.StdOut);
    }

    internal static WslStatus ParseStatus(string stdout)
    {
        string? distro = null; int? version = null;
        foreach (var line in stdout.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("Default Distribution:", StringComparison.OrdinalIgnoreCase))
                distro = t.Split(':', 2)[1].Trim();
            else if (t.StartsWith("Default Version:", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(t.Split(':', 2)[1].Trim(), out var v))
                version = v;
        }
        return new WslStatus(distro, version, stdout);
    }

    internal static WslVersionInfo ParseVersion(string stdout)
    {
        string? wsl = null, kernel = null, wslg = null;
        foreach (var line in stdout.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("WSL version:", StringComparison.OrdinalIgnoreCase)) wsl = After(t);
            else if (t.StartsWith("Kernel version:", StringComparison.OrdinalIgnoreCase)) kernel = After(t);
            else if (t.StartsWith("WSLg version:", StringComparison.OrdinalIgnoreCase)) wslg = After(t);
        }
        return new WslVersionInfo(wsl, kernel, wslg, stdout);
        static string After(string s) => s.Split(':', 2)[1].Trim();
    }
}
```

NOTE: if `WslErrorMapper` has a different API than `ThrowIfFailed(ProcessResult)`, use whatever the existing services (`WslDistroService`) actually call — match exactly.

- [x] **Step 4:** Run tests — expect PASS.

- [x] **Step 5: Wire into Dashboard.** Inject `WslSystemService` into `DashboardViewModel`; add `[ObservableProperty] private string? _wslStatusSummary;` set during `RefreshAsync` to e.g. `"WSL 2.4.13.0 · kernel 5.15.167.4 · default: Ubuntu (v2)"`. Failures here must NOT break distro refresh — wrap in try/catch, set summary null. Add a slim status line/InfoBar at top of `DashboardPage.xaml` bound to `Vm.WslStatusSummary` (collapsed when null — reuse existing null-to-visibility converter in `Wsl.App/Converters`). Register `WslSystemService` singleton in `ServiceRegistration.cs`. Add ViewModel test in `Wsl.Core.Tests` asserting summary populated after refresh (FakeProcessRunner: enqueue list output then version/status outputs in invocation order — check actual call order in implementation).

- [x] **Step 6:** `dotnet build WslCommandCenter.sln` + full `dotnet test Wsl.Core.Tests` — green.

- [x] **Step 7:** Commit `feat(core): WSL status and version service with dashboard status line`.

---

### Task 2: Set default user (`--manage <distro> --set-default-user`)

**Files:**
- Modify: `Wsl.Core/WslDistroService.cs` (add `SetDefaultUserAsync`, `ListUsersAsync`)
- Test: `Wsl.Core.Tests/WslDistroServiceTests.cs` (or new `SetDefaultUserTests.cs`)
- Modify: `Wsl.App.Logic/ViewModels/DashboardViewModel.cs` (command)
- Modify: `Wsl.App/Views/DashboardPage.xaml` + `.xaml.cs` (menu item + ContentDialog with user ComboBox)

- [x] **Step 1: Failing tests**

```csharp
[Fact]
public async Task SetDefaultUserAsync_invokes_manage_set_default_user()
{
    var runner = new FakeProcessRunner();
    runner.Enqueue(0, "");
    var svc = new WslDistroService(runner);

    await svc.SetDefaultUserAsync("Ubuntu", "peter");

    Assert.Equal(new[] { "--manage", "Ubuntu", "--set-default-user", "peter" }, runner.LastArgs);
}

[Fact]
public async Task ListUsersAsync_parses_passwd_users_with_login_shells()
{
    var runner = new FakeProcessRunner();
    runner.Enqueue(0,
        "root:x:0:0:root:/root:/bin/bash\n" +
        "daemon:x:1:1:daemon:/usr/sbin:/usr/sbin/nologin\n" +
        "peter:x:1000:1000::/home/peter:/bin/bash\n");
    var svc = new WslDistroService(runner);

    var users = await svc.ListUsersAsync("Ubuntu");

    Assert.Equal(new[] { "root", "peter" }, users);
    Assert.Equal(new[] { "-d", "Ubuntu", "-u", "root", "--", "getent", "passwd" }, runner.LastArgs);
}
```

- [x] **Step 2:** Run — FAIL.

- [x] **Step 3: Implement** on `WslDistroService`:

```csharp
public async Task SetDefaultUserAsync(string name, string user, CancellationToken ct = default)
{
    var r = await _runner.RunAsync("wsl.exe",
        new[] { "--manage", name, "--set-default-user", user }, null, ct);
    WslErrorMapper.ThrowIfFailed(r); // match existing error pattern
}

public async Task<IReadOnlyList<string>> ListUsersAsync(string name, CancellationToken ct = default)
{
    var r = await _runner.RunAsync("wsl.exe",
        new[] { "-d", name, "-u", "root", "--", "getent", "passwd" }, null, ct);
    WslErrorMapper.ThrowIfFailed(r);
    return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.Split(':'))
        .Where(p => p.Length >= 7)
        .Where(p => p[0] == "root" ||
                    (int.TryParse(p[2], out var uid) && uid >= 1000 && uid < 60000))
        .Where(p => !p[6].Trim().EndsWith("nologin") && !p[6].Trim().EndsWith("false"))
        .Select(p => p[0])
        .ToList();
}
```

- [x] **Step 4:** Run — PASS.

- [x] **Step 5: UI.** Dashboard per-distro overflow menu (find existing per-distro buttons/menu in `DashboardPage.xaml`): add "Set default user…". Opens `ContentDialog` with ComboBox populated via `ListUsersAsync` (guard: only when distro running or start it implicitly — `wsl -d` starts it; acceptable). Primary button calls `SetDefaultUserAsync`. VM command:

```csharp
[RelayCommand]
public async Task SetDefaultUserAsync((string distro, string user) p)
{
    try { await _distros.SetDefaultUserAsync(p.distro, p.user); }
    catch (WslException ex) { ErrorMessage = ex.Message; }
}
```

(Adapt parameter shape to how existing dialogs pass values — codebehind may call `Vm` methods directly; follow `DashboardPage.xaml.cs` existing dialog idiom.)

- [x] **Step 6:** Build + full test run green. Commit `feat(app): set default distro user from dashboard`.

---

### Task 3: Launch options (`--cd`, `--user`, `--shell-type`, `--exec`, `--system`) + launch dialog

**Files:**
- Create: `Wsl.Core/LaunchOptions.cs`
- Create: `Wsl.Core/LaunchCommandBuilder.cs`
- Test: `Wsl.Core.Tests/LaunchCommandBuilderTests.cs`
- Modify: `Wsl.App.Logic/ViewModels/DashboardViewModel.cs`
- Modify: `Wsl.App/Views/DashboardPage.xaml` + `.xaml.cs` ("Launch options…" dialog)

- [x] **Step 1: Failing tests**

```csharp
using Wsl.Core;
using Xunit;

public class LaunchCommandBuilderTests
{
    [Fact]
    public void Default_launch_only_selects_distro()
        => Assert.Equal(new[] { "-d", "Ubuntu" },
            LaunchCommandBuilder.Build("Ubuntu", new LaunchOptions()));

    [Fact]
    public void All_options_compose_in_canonical_order()
    {
        var opts = new LaunchOptions
        {
            User = "peter",
            WorkingDirectory = "~",
            ShellType = WslShellType.Login,
            Command = "htop",
        };
        Assert.Equal(
            new[] { "-d", "Ubuntu", "--user", "peter", "--cd", "~", "--shell-type", "login", "--", "htop" },
            LaunchCommandBuilder.Build("Ubuntu", opts));
    }

    [Fact]
    public void Exec_uses_exec_flag_instead_of_separator()
    {
        var opts = new LaunchOptions { Command = "htop", UseExec = true };
        Assert.Equal(new[] { "-d", "Ubuntu", "--exec", "htop" },
            LaunchCommandBuilder.Build("Ubuntu", opts));
    }

    [Fact]
    public void System_distro_replaces_distro_selection()
    {
        var opts = new LaunchOptions { SystemDistro = true };
        Assert.Equal(new[] { "--system" }, LaunchCommandBuilder.Build("Ubuntu", opts));
    }
}
```

- [x] **Step 2:** Run — FAIL.

- [x] **Step 3: Implement**

```csharp
// Wsl.Core/LaunchOptions.cs
namespace Wsl.Core;

public enum WslShellType { Default, Standard, Login, None }

public class LaunchOptions
{
    public string? User { get; set; }
    public string? WorkingDirectory { get; set; }
    public WslShellType ShellType { get; set; } = WslShellType.Default;
    public string? Command { get; set; }
    public bool UseExec { get; set; }
    public bool SystemDistro { get; set; }
}
```

```csharp
// Wsl.Core/LaunchCommandBuilder.cs
namespace Wsl.Core;

public static class LaunchCommandBuilder
{
    public static string[] Build(string distro, LaunchOptions o)
    {
        var args = new List<string>();
        if (o.SystemDistro) args.Add("--system");
        else { args.Add("-d"); args.Add(distro); }

        if (!string.IsNullOrWhiteSpace(o.User)) { args.Add("--user"); args.Add(o.User); }
        if (!string.IsNullOrWhiteSpace(o.WorkingDirectory)) { args.Add("--cd"); args.Add(o.WorkingDirectory); }
        if (o.ShellType != WslShellType.Default)
        { args.Add("--shell-type"); args.Add(o.ShellType.ToString().ToLowerInvariant()); }

        if (!string.IsNullOrWhiteSpace(o.Command))
        {
            if (o.UseExec) args.Add("--exec");
            else args.Add("--");
            args.AddRange(o.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return args.ToArray();
    }
}
```

- [x] **Step 4:** Run — PASS.

- [x] **Step 5: UI.** Dashboard per-distro menu: "Launch with options…" → ContentDialog: user TextBox, working dir TextBox, shell type ComboBox (Default/Standard/Login/None), command TextBox, "use --exec" CheckBox, "system distro" CheckBox. Launch = open a real console window app-side (in `DashboardPage.xaml.cs` or a small `Wsl.App/Services/TerminalLauncher.cs`):

```csharp
// Wsl.App/Services/TerminalLauncher.cs
public static class TerminalLauncher
{
    public static void Launch(string[] wslArgs)
    {
        var psi = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = true, // new console window
        };
        foreach (var a in wslArgs) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
}
```

Also add a "Copy PowerShell" button in the dialog reusing the existing `IPowerShellExporter` pattern (command text: `wsl.exe` + joined args, quote args containing spaces).

- [x] **Step 6:** Build + tests green. Commit `feat(app): launch distro with custom options (user, cwd, shell type, exec)`.

---

### Task 4: Pre-release WSL update toggle (`--update --pre-release`)

**Files:**
- Modify: `Wsl.Contracts/BrokerMessages.cs` (add `bool PreRelease` to the existing update request record — find it; investigator located the handler at `Wsl.Broker/PrivilegedOperations.cs:55`)
- Modify: `Wsl.Broker/PrivilegedOperations.cs` (append `--pre-release` when set)
- Modify: the page/VM that triggers kernel update (search for the update request usage — likely `SetupViewModel` / `SetupPage`): add a CheckBox/ToggleSwitch "Include pre-release versions" wired to the request.
- Test: `Wsl.Core.Tests` — if broker handler has existing tests, extend; otherwise test at the contracts level (serialization round-trip) and any VM logic.

- [x] **Step 1:** Locate existing update request record + handler. Add `PreRelease` with default `false` (back-compat). If `BrokerJsonContext.cs` is a source-generated JSON context, no new registration needed for a property change.
- [x] **Step 2:** Failing test: handler invoked with `PreRelease = true` runs `wsl.exe --update --pre-release` (if `PrivilegedOperations` takes an injectable `IProcessRunner`, use `FakeProcessRunner`; check its constructor — if it news up the runner, refactor to inject, matching how `Wsl.Core` services do it).
- [x] **Step 3:** Implement: `args = req.PreRelease ? new[] { "--update", "--pre-release" } : new[] { "--update" }`.
- [x] **Step 4:** Tests pass. UI toggle added next to the existing update control.
- [x] **Step 5:** Build + tests green. Commit `feat(broker): opt-in pre-release WSL updates`.

---

## Phase 2 — new capability, guarded

### Task 5: Custom install (`--install --from-file/--name/--location/--version/--web-download`)

**Files:**
- Modify: `Wsl.Core/WslDeployService.cs` (add `InstallCustomAsync` + `CustomInstallOptions`)
- Create: `Wsl.Core/CustomInstallOptions.cs`
- Test: `Wsl.Core.Tests/WslDeployServiceTests.cs` (extend)
- Modify: `Wsl.App.Logic/ViewModels/DeployViewModel.cs`
- Modify: `Wsl.App/Views/DeployPage.xaml` + `.xaml.cs` (SettingsExpander "Advanced install")

- [x] **Step 1: Failing tests**

```csharp
[Fact]
public async Task InstallCustomAsync_composes_all_flags()
{
    var runner = new FakeProcessRunner();
    runner.Enqueue(0, "");
    var svc = new WslDeployService(runner);

    await svc.InstallCustomAsync(new CustomInstallOptions
    {
        Distro = "Ubuntu-24.04",
        Name = "ubuntu-dev2",
        Location = @"D:\wsl\ubuntu-dev2",
        Version = 2,
        WebDownload = true,
    });

    Assert.Equal(new[] { "--install", "Ubuntu-24.04", "--name", "ubuntu-dev2",
        "--location", @"D:\wsl\ubuntu-dev2", "--version", "2",
        "--web-download", "--no-launch" }, runner.LastArgs);
}

[Fact]
public async Task InstallCustomAsync_from_file_replaces_catalog_distro()
{
    var runner = new FakeProcessRunner();
    runner.Enqueue(0, "");
    var svc = new WslDeployService(runner);

    await svc.InstallCustomAsync(new CustomInstallOptions
    {
        FromFile = @"D:\images\arch.wsl",
        Name = "arch-custom",
    });

    Assert.Equal(new[] { "--install", "--from-file", @"D:\images\arch.wsl",
        "--name", "arch-custom", "--no-launch" }, runner.LastArgs);
}

[Fact]
public async Task InstallCustomAsync_rejects_both_distro_and_from_file()
{
    var svc = new WslDeployService(new FakeProcessRunner());
    await Assert.ThrowsAsync<ArgumentException>(() =>
        svc.InstallCustomAsync(new CustomInstallOptions
        { Distro = "Ubuntu", FromFile = @"D:\x.wsl" }));
}
```

- [x] **Step 2:** FAIL. **Step 3: Implement**

```csharp
// Wsl.Core/CustomInstallOptions.cs
namespace Wsl.Core;

public class CustomInstallOptions
{
    public string? Distro { get; set; }       // catalog name
    public string? FromFile { get; set; }     // local .wsl/.tar file — mutually exclusive with Distro
    public string? Name { get; set; }
    public string? Location { get; set; }
    public int? Version { get; set; }
    public bool WebDownload { get; set; }
}
```

```csharp
// on WslDeployService
public async Task InstallCustomAsync(CustomInstallOptions o, CancellationToken ct = default)
{
    if (o.Distro is not null && o.FromFile is not null)
        throw new ArgumentException("Specify a catalog distro or a local file, not both.");
    if (o.Distro is null && o.FromFile is null)
        throw new ArgumentException("Specify a catalog distro or a local file.");

    var args = new List<string> { "--install" };
    if (o.Distro is not null) args.Add(o.Distro);
    if (o.FromFile is not null) { args.Add("--from-file"); args.Add(o.FromFile); }
    if (o.Name is not null) { args.Add("--name"); args.Add(o.Name); }
    if (o.Location is not null) { args.Add("--location"); args.Add(o.Location); }
    if (o.Version is not null) { args.Add("--version"); args.Add(o.Version.Value.ToString()); }
    if (o.WebDownload) args.Add("--web-download");
    args.Add("--no-launch");

    var r = await _runner.RunAsync("wsl.exe", args.ToArray(), TimeSpan.FromMinutes(30), ct);
    WslErrorMapper.ThrowIfFailed(r);
}
```

(Match the timeout idiom of the existing `InstallAsync` — if it passes a long/None timeout, do same.)

- [x] **Step 4:** PASS. **Step 5: UI.** `DeployPage.xaml`: `SettingsExpander` "Advanced install" containing: distro ComboBox (reuse existing online catalog list) OR file picker for `.wsl`/`.tar` (FileOpenPicker, follow the existing import picker in `BackupPage.xaml.cs`), name TextBox, location folder picker, version ComboBox (1/2), web-download CheckBox. Validation in VM: name must be non-empty when FromFile chosen; name not already in `ListAsync` result (collision → ErrorMessage, no call).
- [x] **Step 6:** Build + tests green. Commit `feat(app): advanced install — custom name, location, local file, multi-instance`.

---

### Task 6: Import in place (`--import-in-place`)

**Files:**
- Modify: `Wsl.Core/WslDeployService.cs` (add `ImportInPlaceAsync`)
- Test: `Wsl.Core.Tests/WslDeployServiceTests.cs`
- Modify: `Wsl.App.Logic/ViewModels/BackupViewModel.cs`
- Modify: `Wsl.App/Views/BackupPage.xaml` + `.xaml.cs` (card "Register existing VHDX")

- [x] **Step 1: Failing tests**

```csharp
[Fact]
public async Task ImportInPlaceAsync_registers_vhdx()
{
    var runner = new FakeProcessRunner();
    runner.Enqueue(0, "");
    var svc = new WslDeployService(runner);

    await svc.ImportInPlaceAsync("arch", @"D:\wsl\arch\ext4.vhdx");

    Assert.Equal(new[] { "--import-in-place", "arch", @"D:\wsl\arch\ext4.vhdx" }, runner.LastArgs);
}

[Fact]
public async Task ImportInPlaceAsync_rejects_non_vhdx_extension()
{
    var svc = new WslDeployService(new FakeProcessRunner());
    await Assert.ThrowsAsync<ArgumentException>(() =>
        svc.ImportInPlaceAsync("arch", @"D:\wsl\arch.tar"));
}
```

- [x] **Step 2:** FAIL. **Step 3: Implement** — extension check (`.vhdx`, OrdinalIgnoreCase) then run. **Step 4:** PASS.
- [x] **Step 5: UI + guards.** BackupPage card: name TextBox + `.vhdx` FileOpenPicker. VM guards before calling: (a) file exists; (b) **name-collision check** against `WslDistroService.ListAsync` — import-in-place silently clobbers an existing registration (council foot-gun); surface ErrorMessage and refuse. Note in UI text: "The VHDX is used where it is — it is not copied. It must contain an ext4 filesystem."
- [x] **Step 6:** Build + tests green. Commit `feat(app): register existing VHDX via import-in-place`.

---

### Task 7: Move distro (`--manage <distro> --move`) with full guard rails

**Files:**
- Modify: `Wsl.Core/WslDiskService.cs` (add `MoveAsync` + preflight)
- Create: `Wsl.Core/MovePreflight.cs`
- Test: `Wsl.Core.Tests/WslDiskServiceMoveTests.cs`
- Modify: `Wsl.App.Logic/ViewModels/DashboardViewModel.cs` (move command + preflight state)
- Modify: `Wsl.App/Views/DashboardPage.xaml` + `.xaml.cs` ("Move distro…" dialog: folder picker, checks list, typed confirm)

**Council-mandated guards (ALL required):**
1. WSL version gate ≥ 2.0.14 (use `WslSystemService.GetVersionInfoAsync().WslVersionParsed`)
2. Distro must be Stopped (terminate first, same as the existing optimize flow in `WslDiskService`)
3. Target drive: free space ≥ current vhdx size × 1.1
4. Target drive format must be NTFS (no FAT32/exFAT/network)
5. UI: typed confirmation = distro name; warning "cannot be cancelled once started"; suggest backup first (link to Backup page)

- [x] **Step 1: Failing tests** (preflight is pure logic → fully testable; filesystem facts passed in)

```csharp
using Wsl.Core;
using Xunit;

public class WslDiskServiceMoveTests
{
    [Fact]
    public void Preflight_passes_when_all_conditions_met()
    {
        var p = MovePreflight.Evaluate(
            wslVersion: new Version(2, 4, 13),
            vhdxSizeBytes: 10_000_000_000,
            targetFreeBytes: 12_000_000_000,
            targetDriveFormat: "NTFS");
        Assert.True(p.Ok);
        Assert.Empty(p.Failures);
    }

    [Theory]
    [InlineData(1, 2, 5)]   // wsl too old
    public void Preflight_fails_on_old_wsl(int maj, int min, int build)
    {
        var p = MovePreflight.Evaluate(new Version(maj, min, build),
            1_000, 1_000_000, "NTFS");
        Assert.False(p.Ok);
        Assert.Contains(p.Failures, f => f.Contains("WSL"));
    }

    [Fact]
    public void Preflight_fails_when_free_space_below_110_percent()
    {
        var p = MovePreflight.Evaluate(new Version(2, 4, 13),
            vhdxSizeBytes: 10_000_000_000,
            targetFreeBytes: 10_500_000_000, // < 11 GB needed
            targetDriveFormat: "NTFS");
        Assert.False(p.Ok);
        Assert.Contains(p.Failures, f => f.Contains("space"));
    }

    [Theory]
    [InlineData("FAT32")]
    [InlineData("exFAT")]
    public void Preflight_fails_on_non_ntfs(string fmt)
    {
        var p = MovePreflight.Evaluate(new Version(2, 4, 13), 1_000, 1_000_000, fmt);
        Assert.False(p.Ok);
        Assert.Contains(p.Failures, f => f.Contains("NTFS"));
    }

    [Fact]
    public async Task MoveAsync_terminates_then_moves()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ""); // terminate
        runner.Enqueue(0, ""); // move
        var svc = new WslDiskService(runner);

        await svc.MoveAsync("Ubuntu", @"D:\wsl\ubuntu");

        Assert.Equal(new[] { "--terminate", "Ubuntu" }, runner.AllArgs[^2]);
        Assert.Equal(new[] { "--manage", "Ubuntu", "--move", @"D:\wsl\ubuntu" }, runner.AllArgs[^1]);
    }
}
```

- [x] **Step 2:** FAIL. **Step 3: Implement**

```csharp
// Wsl.Core/MovePreflight.cs
namespace Wsl.Core;

public record MovePreflightResult(bool Ok, IReadOnlyList<string> Failures);

public static class MovePreflight
{
    public static readonly Version MinWslVersion = new(2, 0, 14);

    public static MovePreflightResult Evaluate(
        Version wslVersion, long vhdxSizeBytes, long targetFreeBytes, string targetDriveFormat)
    {
        var failures = new List<string>();
        if (wslVersion < MinWslVersion)
            failures.Add($"WSL {MinWslVersion} or newer required for safe move (found {wslVersion}).");
        var needed = (long)(vhdxSizeBytes * 1.1);
        if (targetFreeBytes < needed)
            failures.Add($"Not enough free space: need {needed / 1_073_741_824.0:F1} GB (incl. 10% buffer).");
        if (!string.Equals(targetDriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            failures.Add($"Target drive must be NTFS (found {targetDriveFormat}).");
        return new MovePreflightResult(failures.Count == 0, failures);
    }
}
```

`WslDiskService.MoveAsync(string name, string targetDir, CancellationToken ct = default)`: terminate → `--manage {name} --move {targetDir}` with **no timeout** (long copy; pass `Timeout.InfiniteTimeSpan` or the service's long-running idiom — match how export/import handle long ops). Also add helper `GetVhdxInfo(string name)` in `WslDiskService` reading the distro's `BasePath` from registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Lxss\*` (DistributionName match) → `FileInfo(ext4.vhdx).Length`. Keep registry access in one small method; it stays untested (thin I/O wrapper), preflight logic is what's tested.

- [x] **Step 4:** PASS. **Step 5: UI.** Dashboard distro menu → "Move distro…" dialog: folder picker, then run preflight (`DriveInfo` for free bytes + format, `GetVhdxInfo` for size, `WslSystemService` for version) and show pass/fail checklist in the dialog. Primary button disabled until preflight Ok **and** TextBox text equals distro name exactly. Warning InfoBar: "This cannot be cancelled once started. Consider exporting a backup first." Progress ring while running; refresh list after.
- [x] **Step 6:** Build + tests green. Commit `feat(app): move distro to another drive with preflight guard rails`.

---

## Phase 3 — elevated / destructive / niche

### Task 8: Disk mount/unmount (`--mount`/`--unmount`) — broker + new Disks page

**Files:**
- Modify: `Wsl.Contracts/BrokerMessages.cs` (+`BrokerJsonContext.cs` registration): `ListDisksRequest`, `MountDiskRequest(string Disk, bool Vhd, bool Bare, int? Partition, string? Type, string? Options, string? Name)`, `UnmountDiskRequest(string? Disk)` — plus a `DiskInfo(string DeviceId, string Model, string SerialNumber, long SizeBytes, bool IsSystem)` payload record.
- Modify: `Wsl.Broker/PrivilegedOperations.cs` (3 handlers)
- Create: `Wsl.App.Logic/ViewModels/DisksViewModel.cs`
- Create: `Wsl.App/Views/DisksPage.xaml` + `.xaml.cs`
- Modify: `Wsl.App/MainWindow.xaml` + `.xaml.cs` (nav item "Disks", page map entry)
- Modify: `Wsl.App/Services/ServiceRegistration.cs` (register `DisksViewModel`)
- Test: `Wsl.Core.Tests` — arg-composition tests for the broker handlers (inject `FakeProcessRunner` into `PrivilegedOperations` — Task 4 already established injectability) + `DisksViewModel` tests with a fake `IBrokerClient`.

**Council requirements:** disk enumeration runs **inside the elevated broker** (WMI `Win32_DiskDrive` via `System.Management` or `Get-Disk` via PowerShell — pick what Wsl.Broker can reference; CIM via `Microsoft.Management.Infrastructure` is also fine); physical-disk mount = typed confirm of device id in UI; VHD mount = plain confirm; mark system disk (`IsSystem`) and refuse to mount it.

- [x] **Step 1: Failing tests** — handler arg composition:

```csharp
[Fact]
public async Task MountDisk_composes_full_arg_set()
{
    var runner = new FakeProcessRunner();
    runner.Enqueue(0, "");
    var ops = new PrivilegedOperations(runner);

    await ops.HandleAsync(new MountDiskRequest(
        @"\\.\PHYSICALDRIVE2", Vhd: false, Bare: false,
        Partition: 1, Type: "ext4", Options: "ro", Name: "data"));

    Assert.Equal(new[] { "--mount", @"\\.\PHYSICALDRIVE2", "--partition", "1",
        "--type", "ext4", "--options", "ro", "--name", "data" }, runner.LastArgs);
}

[Fact]
public async Task UnmountDisk_without_disk_unmounts_all()
{
    var runner = new FakeProcessRunner();
    runner.Enqueue(0, "");
    var ops = new PrivilegedOperations(runner);

    await ops.HandleAsync(new UnmountDiskRequest(null));

    Assert.Equal(new[] { "--unmount" }, runner.LastArgs);
}
```

(Adapt to `PrivilegedOperations.HandleAsync`'s real request/response signature; response should carry success/stderr like existing ops.)

- [x] **Step 2:** FAIL. **Step 3: Implement** handlers. `--vhd` and `--bare` appended when flags set. `ListDisksRequest` handler enumerates `Win32_DiskDrive` (DeviceID, Model, SerialNumber, Size) and flags the system disk (disk index 0 or query `Win32_DiskPartition` Bootable→ simplest robust check: the disk hosting `%SystemDrive%`; if ambiguous, mark index 0). Refuse `MountDiskRequest` for the system disk server-side too (defense in depth) — return error response.
- [x] **Step 4:** PASS. **Step 5: UI.** New `DisksPage`: "Mounted in WSL" intro text, disk ListView (model, serial, size, system badge), VHD file picker row. Mount flyout/dialog: bare CheckBox, partition NumberBox, type TextBox (default ext4), options TextBox, name TextBox. Physical disk → typed confirm of `PHYSICALDRIVE{n}`; system disk row disabled. "Unmount all" button. All calls via `IBrokerClient` (elevation prompt handled by existing BrokerClient launch flow). Nav item with appropriate Fluent icon (e.g. `` or HardDrive glyph).
- [x] **Step 6:** Build + tests green. Commit `feat(app): mount physical and virtual disks into WSL2 via elevated broker`.

---

### Task 9: Diagnostics — debug shell behind developer toggle

**Files:**
- Modify: `Wsl.Contracts/BrokerMessages.cs`: `LaunchDebugShellRequest()`
- Modify: `Wsl.Broker/PrivilegedOperations.cs`: handler starts `wsl.exe --debug-shell` **detached with its own console window** (`ProcessStartInfo { UseShellExecute = true }` — do NOT capture output, do NOT wait for exit; return success once started)
- Modify: `Wsl.App/Views/SettingsPage.xaml` + `.xaml.cs`: "Developer mode" ToggleSwitch (persist in same local-settings store the page already uses for theme — follow existing idiom) + when on, show "Diagnostics" SettingsCard with "Open WSL2 debug shell" button (calls broker) and disclaimer text.
- Test: handler test = starts process detached — keep handler thin; if untestable without abstraction, test only the request serialization round-trip and that non-debug path unaffected. Don't over-engineer.

- [ ] **Step 1:** Add request + handler + UI. Debug shell is elevation-required → broker. Settings page is imperative (no VM) — keep that style.
- [ ] **Step 2:** Build + tests green. Commit `feat(app): WSL2 debug shell behind developer mode toggle`.

---

### Task 10: Uninstall WSL package — danger zone

**Files:**
- Modify: `Wsl.Contracts/BrokerMessages.cs`: `UninstallWslRequest()`
- Modify: `Wsl.Broker/PrivilegedOperations.cs`: handler runs `wsl.exe --uninstall`
- Modify: `Wsl.App/Views/SettingsPage.xaml` + `.xaml.cs`: "WSL package" SettingsCard group at bottom — card explains difference vs unregister ("Removes the WSL platform itself from this machine. Installed distributions will stop working. This is NOT the same as unregistering a single distribution."). Button (red/destructive style) → ContentDialog listing current distros (via `WslDistroService.ListAsync`) that will be affected, CheckBox "I understand all listed distributions will become unavailable", TextBox typed confirm — must equal `UNINSTALL` — primary button disabled until both satisfied.
- Test: handler arg test (`--uninstall`) with `FakeProcessRunner`.

- [ ] **Step 1:** Failing handler test → implement → pass.
- [ ] **Step 2:** UI per above. **Do not wire a default/Enter key to the primary button.**
- [ ] **Step 3:** Build + tests green. Commit `feat(app): guarded WSL package uninstall in settings danger zone`.

---

### Task 11: Final pass

- [ ] **Step 1:** Full `dotnet build WslCommandCenter.sln` + `dotnet test` (all test projects except `Wsl.Live.Tests` unless it's part of default run — check; live tests hit real WSL, skip if so marked).
- [ ] **Step 2:** Update `README`/docs if a docs page lists features (check `docs/`).
- [ ] **Step 3:** Commit any leftovers. Branch ready for merge review.

---

## Self-review notes

- All wsl.exe arg compositions are exact-match asserted with `FakeProcessRunner.LastArgs` — same convention as existing tests.
- `WslErrorMapper.ThrowIfFailed` is a placeholder name: Task 1 implementer must mirror the real call used in `WslDistroService` and every later task follows Task 1's resolved form.
- `PrivilegedOperations` injectability is established in Task 4 and reused in Tasks 8–10. If it already takes `IProcessRunner`, Task 4 Step 2's refactor is a no-op.
- Unresolved council question (does `--move`/`--import-in-place` need elevation?): both are implemented unelevated; `WslErrorMapper` surfaces failures. If real-world testing shows elevation needed, route through broker in a follow-up — the service API shape stays the same.
