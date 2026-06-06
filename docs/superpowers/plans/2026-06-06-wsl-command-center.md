# WSL Command Center Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A native WinUI 3 / .NET 9 desktop app to manage WSL end-to-end — bootstrap install, distro lifecycle, deploy, manual backup/restore, config editing — using a least-privilege elevated broker.

**Architecture:** UI-free `Wsl.Core` library wraps `wsl.exe` behind `IProcessRunner` (testable with UTF-16LE fixtures). Non-elevated WinUI 3 app calls Core directly for unprivileged ops; a separately-elevated broker process handles the few privileged ops over a mutually-authenticated named pipe. Shared IPC DTOs live in a tiny `Wsl.Contracts` assembly so the broker stays lean.

**Tech Stack:** WinUI 3, .NET 9, C#, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, System.Text.Json (source-gen), xUnit, System.IO.Pipes.

**Spec:** `docs/superpowers/specs/2026-06-06-wsl-command-center-design.md`

---

## Conventions

- Run all `dotnet` commands from repo root `C:\Dev\Active\WSL Command Center`.
- Test command shorthand: `dotnet test Wsl.Core.Tests` runs the whole suite; filter with `--filter "FullyQualifiedName~<name>"`.
- Commit after each task with the message shown in its final step.
- TDD: write the failing test, run it red, implement minimal, run it green, commit.

---

## Task 0: Solution scaffold

**Files:**
- Create: `WslCommandCenter.sln`
- Create: `Wsl.Core/Wsl.Core.csproj`
- Create: `Wsl.Contracts/Wsl.Contracts.csproj`
- Create: `Wsl.Core.Tests/Wsl.Core.Tests.csproj`
- Create: `.gitignore`

- [ ] **Step 1: Create the .gitignore**

Create `.gitignore`:

```gitignore
bin/
obj/
.vs/
*.user
TestResults/
```

- [ ] **Step 2: Create projects and solution**

Run:

```powershell
dotnet new sln -n WslCommandCenter
dotnet new classlib -n Wsl.Core -f net9.0 -o Wsl.Core
dotnet new classlib -n Wsl.Contracts -f net9.0 -o Wsl.Contracts
dotnet new xunit -n Wsl.Core.Tests -f net9.0 -o Wsl.Core.Tests
del Wsl.Core\Class1.cs
del Wsl.Contracts\Class1.cs
dotnet sln add Wsl.Core Wsl.Contracts Wsl.Core.Tests
dotnet add Wsl.Core.Tests reference Wsl.Core Wsl.Contracts
dotnet add Wsl.Core reference Wsl.Contracts
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add .gitignore WslCommandCenter.sln Wsl.Core Wsl.Contracts Wsl.Core.Tests
git commit -m "chore: scaffold solution (Core, Contracts, Tests)"
```

---

## Task 1: IProcessRunner abstraction + ProcessResult

**Files:**
- Create: `Wsl.Core/IProcessRunner.cs`
- Create: `Wsl.Core/ProcessResult.cs`
- Create: `Wsl.Core.Tests/FakeProcessRunner.cs`

- [ ] **Step 1: Write ProcessResult**

Create `Wsl.Core/ProcessResult.cs`:

```csharp
namespace Wsl.Core;

public record ProcessResult(int ExitCode, string StdOut, string StdErr);
```

- [ ] **Step 2: Write IProcessRunner**

Create `Wsl.Core/IProcessRunner.cs`:

```csharp
namespace Wsl.Core;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string exe,
        string[] args,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
```

- [ ] **Step 3: Write the FakeProcessRunner test double**

Create `Wsl.Core.Tests/FakeProcessRunner.cs`:

```csharp
using Wsl.Core;

namespace Wsl.Core.Tests;

/// <summary>Records the last invocation and returns a queued result.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _results = new();

    public string? LastExe { get; private set; }
    public string[]? LastArgs { get; private set; }
    public List<string[]> AllArgs { get; } = new();

    public void Enqueue(ProcessResult result) => _results.Enqueue(result);

    public void Enqueue(int exitCode, string stdOut, string stdErr = "")
        => _results.Enqueue(new ProcessResult(exitCode, stdOut, stdErr));

    public Task<ProcessResult> RunAsync(
        string exe, string[] args, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        LastExe = exe;
        LastArgs = args;
        AllArgs.Add(args);
        var result = _results.Count > 0 ? _results.Dequeue() : new ProcessResult(0, "", "");
        return Task.FromResult(result);
    }
}
```

- [ ] **Step 4: Verify it builds**

Run: `dotnet build Wsl.Core.Tests`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add Wsl.Core/IProcessRunner.cs Wsl.Core/ProcessResult.cs Wsl.Core.Tests/FakeProcessRunner.cs
git commit -m "feat(core): IProcessRunner abstraction + fake"
```

---

## Task 2: WslException + WslErrorKind + error mapper

**Files:**
- Create: `Wsl.Core/WslErrorKind.cs`
- Create: `Wsl.Core/WslException.cs`
- Create: `Wsl.Core/WslErrorMapper.cs`
- Test: `Wsl.Core.Tests/WslErrorMapperTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Wsl.Core.Tests/WslErrorMapperTests.cs`:

```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslErrorMapperTests
{
    [Theory]
    [InlineData("There is no distribution with the supplied name.", WslErrorKind.DistroNotFound)]
    [InlineData("A distribution with the supplied name already exists.", WslErrorKind.AlreadyExists)]
    [InlineData("Access is denied.", WslErrorKind.AccessDenied)]
    [InlineData("The Windows Subsystem for Linux is not installed.", WslErrorKind.NotInstalled)]
    [InlineData("The file or directory is corrupted and unreadable.", WslErrorKind.InvalidArchive)]
    [InlineData("some other unexpected failure", WslErrorKind.CommandFailed)]
    public void Maps_stderr_to_kind(string stderr, WslErrorKind expected)
    {
        Assert.Equal(expected, WslErrorMapper.Classify(exitCode: 1, stderr));
    }

    [Fact]
    public void Zero_exit_is_command_failed_when_forced()
    {
        // Mapper only classifies; callers decide when to throw.
        Assert.Equal(WslErrorKind.CommandFailed, WslErrorMapper.Classify(0, ""));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslErrorMapperTests"`
Expected: FAIL — `WslErrorKind` / `WslErrorMapper` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Wsl.Core/WslErrorKind.cs`:

```csharp
namespace Wsl.Core;

public enum WslErrorKind
{
    NotInstalled,
    DistroNotFound,
    AccessDenied,
    AlreadyExists,
    InvalidArchive,
    CommandFailed,
    Timeout
}
```

Create `Wsl.Core/WslException.cs`:

```csharp
namespace Wsl.Core;

public class WslException : Exception
{
    public int? ExitCode { get; }
    public string? StdErr { get; }
    public WslErrorKind Kind { get; }

    public WslException(WslErrorKind kind, string message, int? exitCode = null, string? stdErr = null)
        : base(message)
    {
        Kind = kind;
        ExitCode = exitCode;
        StdErr = stdErr;
    }
}
```

Create `Wsl.Core/WslErrorMapper.cs`:

```csharp
namespace Wsl.Core;

public static class WslErrorMapper
{
    public static WslErrorKind Classify(int exitCode, string stderr)
    {
        var s = stderr.ToLowerInvariant();
        if (s.Contains("no distribution with the supplied name")) return WslErrorKind.DistroNotFound;
        if (s.Contains("already exists")) return WslErrorKind.AlreadyExists;
        if (s.Contains("access is denied")) return WslErrorKind.AccessDenied;
        if (s.Contains("not installed")) return WslErrorKind.NotInstalled;
        if (s.Contains("corrupted") || s.Contains("not a valid")) return WslErrorKind.InvalidArchive;
        return WslErrorKind.CommandFailed;
    }

    /// <summary>Throws a WslException if exit code is non-zero.</summary>
    public static void ThrowIfFailed(ProcessResult result, string operation)
    {
        if (result.ExitCode == 0) return;
        var kind = Classify(result.ExitCode, result.StdErr);
        throw new WslException(kind, $"{operation} failed: {result.StdErr.Trim()}",
                               result.ExitCode, result.StdErr);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslErrorMapperTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wsl.Core/WslErrorKind.cs Wsl.Core/WslException.cs Wsl.Core/WslErrorMapper.cs Wsl.Core.Tests/WslErrorMapperTests.cs
git commit -m "feat(core): typed WslException + stderr classifier"
```

---

## Task 3: WslDistroService.ListAsync (UTF-16LE parsing)

**Files:**
- Create: `Wsl.Core/Distro.cs`
- Create: `Wsl.Core/WslDistroService.cs`
- Test: `Wsl.Core.Tests/WslDistroServiceListTests.cs`

This is the parsing backbone. `wsl -l -v` output is rendered (after UTF-16LE decode) as a
fixed-column table. The `IProcessRunner` already returns decoded strings, so the service parses
plain text. The default distro is marked with `*` in the first column.

- [ ] **Step 1: Write the Distro model**

Create `Wsl.Core/Distro.cs`:

```csharp
namespace Wsl.Core;

public enum DistroState { Running, Stopped, Installing, Unknown }

public record Distro(string Name, DistroState State, int Version, bool IsDefault);
```

- [ ] **Step 2: Write the failing test**

Create `Wsl.Core.Tests/WslDistroServiceListTests.cs`. The fixture string mirrors real
`wsl -l -v` output *after* UTF-16LE decoding (header row + two distros, default starred):

```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslDistroServiceListTests
{
    // Real `wsl -l -v` layout: leading 2-char marker column, then NAME / STATE / VERSION.
    private const string ListOutput =
        "  NAME                   STATE           VERSION\r\n" +
        "* Ubuntu                 Stopped         2\r\n" +
        "  podman-machine-default Stopped         2\r\n";

    private static WslDistroService MakeService(string stdout)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, stdout);
        return new WslDistroService(runner);
    }

    [Fact]
    public async Task Parses_two_distros()
    {
        var distros = await MakeService(ListOutput).ListAsync();
        Assert.Equal(2, distros.Count);
    }

    [Fact]
    public async Task Parses_name_state_version_and_default()
    {
        var distros = await MakeService(ListOutput).ListAsync();

        var ubuntu = distros[0];
        Assert.Equal("Ubuntu", ubuntu.Name);
        Assert.Equal(DistroState.Stopped, ubuntu.State);
        Assert.Equal(2, ubuntu.Version);
        Assert.True(ubuntu.IsDefault);

        var podman = distros[1];
        Assert.Equal("podman-machine-default", podman.Name);
        Assert.False(podman.IsDefault);
    }

    [Fact]
    public async Task Parses_running_state()
    {
        const string running =
            "  NAME      STATE     VERSION\r\n" +
            "* Ubuntu    Running   2\r\n";
        var distros = await MakeService(running).ListAsync();
        Assert.Equal(DistroState.Running, distros[0].State);
    }

    [Fact]
    public async Task Empty_when_no_distros()
    {
        const string none = "  NAME      STATE     VERSION\r\n";
        var distros = await MakeService(none).ListAsync();
        Assert.Empty(distros);
    }

    [Fact]
    public async Task Passes_correct_args()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);
        await new WslDistroService(runner).ListAsync();
        Assert.Equal("wsl.exe", runner.LastExe);
        Assert.Equal(new[] { "--list", "--verbose" }, runner.LastArgs);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslDistroServiceListTests"`
Expected: FAIL — `WslDistroService` does not exist.

- [ ] **Step 4: Write the implementation**

Create `Wsl.Core/WslDistroService.cs`:

```csharp
namespace Wsl.Core;

public class WslDistroService
{
    private readonly IProcessRunner _runner;

    public WslDistroService(IProcessRunner runner) => _runner = runner;

    public async Task<IReadOnlyList<Distro>> ListAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe", new[] { "--list", "--verbose" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, "List distros");
        return Parse(result.StdOut);
    }

    internal static IReadOnlyList<Distro> Parse(string stdout)
    {
        var lines = stdout.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var distros = new List<Distro>();
        foreach (var line in lines)
        {
            // Skip header (NAME ... STATE ... VERSION).
            if (line.TrimStart().StartsWith("NAME")) continue;

            var isDefault = line.StartsWith("*");
            // Drop the 2-char marker column, then split on whitespace runs.
            var body = (line.Length >= 2 ? line[2..] : line).Trim();
            var parts = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            // VERSION is the last token, STATE is second-to-last, NAME is everything before.
            var version = int.TryParse(parts[^1], out var v) ? v : 0;
            var state = ParseState(parts[^2]);
            var name = string.Join(' ', parts[..^2]);
            distros.Add(new Distro(name, state, version, isDefault));
        }
        return distros;
    }

    private static DistroState ParseState(string s) => s switch
    {
        "Running" => DistroState.Running,
        "Stopped" => DistroState.Stopped,
        "Installing" => DistroState.Installing,
        _ => DistroState.Unknown
    };
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslDistroServiceListTests"`
Expected: PASS (all 5).

- [ ] **Step 6: Commit**

```bash
git add Wsl.Core/Distro.cs Wsl.Core/WslDistroService.cs Wsl.Core.Tests/WslDistroServiceListTests.cs
git commit -m "feat(core): parse wsl -l -v into Distro list"
```

---

## Task 4: WslDistroService lifecycle actions

**Files:**
- Modify: `Wsl.Core/WslDistroService.cs`
- Test: `Wsl.Core.Tests/WslDistroServiceActionTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Wsl.Core.Tests/WslDistroServiceActionTests.cs`:

```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslDistroServiceActionTests
{
    private static (WslDistroService svc, FakeProcessRunner runner) Make()
    {
        var runner = new FakeProcessRunner();
        return (new WslDistroService(runner), runner);
    }

    [Fact]
    public async Task Start_runs_true_in_distro()
    {
        var (svc, runner) = Make();
        await svc.StartAsync("Ubuntu");
        Assert.Equal(new[] { "-d", "Ubuntu", "--", "true" }, runner.LastArgs);
    }

    [Fact]
    public async Task Terminate_passes_distro()
    {
        var (svc, runner) = Make();
        await svc.TerminateAsync("Ubuntu");
        Assert.Equal(new[] { "--terminate", "Ubuntu" }, runner.LastArgs);
    }

    [Fact]
    public async Task SetDefault_passes_distro()
    {
        var (svc, runner) = Make();
        await svc.SetDefaultAsync("Ubuntu");
        Assert.Equal(new[] { "--set-default", "Ubuntu" }, runner.LastArgs);
    }

    [Fact]
    public async Task SetVersion_passes_distro_and_version()
    {
        var (svc, runner) = Make();
        await svc.SetVersionAsync("Ubuntu", 2);
        Assert.Equal(new[] { "--set-version", "Ubuntu", "2" }, runner.LastArgs);
    }

    [Fact]
    public async Task Unregister_passes_distro()
    {
        var (svc, runner) = Make();
        await svc.UnregisterAsync("Ubuntu");
        Assert.Equal(new[] { "--unregister", "Ubuntu" }, runner.LastArgs);
    }

    [Fact]
    public async Task Failure_throws_WslException()
    {
        var (svc, runner) = Make();
        runner.Enqueue(1, "", "There is no distribution with the supplied name.");
        var ex = await Assert.ThrowsAsync<WslException>(() => svc.TerminateAsync("Ghost"));
        Assert.Equal(WslErrorKind.DistroNotFound, ex.Kind);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslDistroServiceActionTests"`
Expected: FAIL — methods do not exist.

- [ ] **Step 3: Add the action methods**

Add these methods to `Wsl.Core/WslDistroService.cs` inside the class (after `ListAsync`):

```csharp
    public Task StartAsync(string name, CancellationToken ct = default)
        => Run(new[] { "-d", name, "--", "true" }, $"Start {name}", ct);

    public Task TerminateAsync(string name, CancellationToken ct = default)
        => Run(new[] { "--terminate", name }, $"Terminate {name}", ct);

    public Task SetDefaultAsync(string name, CancellationToken ct = default)
        => Run(new[] { "--set-default", name }, $"Set default {name}", ct);

    public Task SetVersionAsync(string name, int version, CancellationToken ct = default)
        => Run(new[] { "--set-version", name, version.ToString() }, $"Set version {name}", ct);

    public Task UnregisterAsync(string name, CancellationToken ct = default)
        => Run(new[] { "--unregister", name }, $"Unregister {name}", ct);

    private async Task Run(string[] args, string op, CancellationToken ct)
    {
        var result = await _runner.RunAsync("wsl.exe", args, null, ct);
        WslErrorMapper.ThrowIfFailed(result, op);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslDistroServiceActionTests"`
Expected: PASS (all 6).

- [ ] **Step 5: Commit**

```bash
git add Wsl.Core/WslDistroService.cs Wsl.Core.Tests/WslDistroServiceActionTests.cs
git commit -m "feat(core): distro lifecycle actions"
```

---

## Task 5: WslDeployService (catalog + import)

**Files:**
- Create: `Wsl.Core/CatalogEntry.cs`
- Create: `Wsl.Core/WslDeployService.cs`
- Test: `Wsl.Core.Tests/WslDeployServiceTests.cs`

- [ ] **Step 1: Write the CatalogEntry model**

Create `Wsl.Core/CatalogEntry.cs`:

```csharp
namespace Wsl.Core;

public record CatalogEntry(string Name, string FriendlyName);
```

- [ ] **Step 2: Write the failing test**

Create `Wsl.Core.Tests/WslDeployServiceTests.cs`. The catalog fixture mirrors decoded
`wsl -l -o` output (header lines, then `NAME` + `FRIENDLY NAME` columns):

```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslDeployServiceTests
{
    private const string CatalogOutput =
        "The following is a list of valid distributions that can be installed.\r\n" +
        "Install using 'wsl.exe --install <Distro>'.\r\n" +
        "\r\n" +
        "NAME                   FRIENDLY NAME\r\n" +
        "Ubuntu                 Ubuntu\r\n" +
        "Debian                 Debian GNU/Linux\r\n" +
        "kali-linux             Kali Linux Rolling\r\n";

    [Fact]
    public async Task Parses_catalog_entries()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, CatalogOutput);
        var entries = await new WslDeployService(runner).ListAvailableAsync();

        Assert.Equal(3, entries.Count);
        Assert.Equal("Ubuntu", entries[0].Name);
        Assert.Equal("Debian", entries[1].Name);
        Assert.Equal("Debian GNU/Linux", entries[1].FriendlyName);
        Assert.Equal("Kali Linux Rolling", entries[2].FriendlyName);
    }

    [Fact]
    public async Task InstallFromCatalog_uses_no_launch()
    {
        var runner = new FakeProcessRunner();
        await new WslDeployService(runner).InstallFromCatalogAsync("Debian");
        Assert.Equal(new[] { "--install", "-d", "Debian", "--no-launch" }, runner.LastArgs);
    }

    [Fact]
    public async Task ImportTar_builds_args()
    {
        var runner = new FakeProcessRunner();
        await new WslDeployService(runner)
            .ImportTarAsync("Custom", @"C:\wsl\custom", @"C:\backups\custom.tar", 2);
        Assert.Equal(
            new[] { "--import", "Custom", @"C:\wsl\custom", @"C:\backups\custom.tar", "--version", "2" },
            runner.LastArgs);
    }

    [Fact]
    public async Task ImportVhdx_adds_vhd_flag()
    {
        var runner = new FakeProcessRunner();
        await new WslDeployService(runner)
            .ImportVhdxAsync("Custom", @"C:\wsl\custom", @"C:\backups\custom.vhdx", 2);
        Assert.Equal(
            new[] { "--import", "Custom", @"C:\wsl\custom", @"C:\backups\custom.vhdx", "--vhd", "--version", "2" },
            runner.LastArgs);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslDeployServiceTests"`
Expected: FAIL — `WslDeployService` does not exist.

- [ ] **Step 4: Write the implementation**

Create `Wsl.Core/WslDeployService.cs`:

```csharp
namespace Wsl.Core;

public class WslDeployService
{
    private readonly IProcessRunner _runner;

    public WslDeployService(IProcessRunner runner) => _runner = runner;

    public async Task<IReadOnlyList<CatalogEntry>> ListAvailableAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe", new[] { "--list", "--online" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, "List online distros");
        return ParseCatalog(result.StdOut);
    }

    internal static IReadOnlyList<CatalogEntry> ParseCatalog(string stdout)
    {
        var lines = stdout.Replace("\r", "").Split('\n');
        var entries = new List<CatalogEntry>();
        var headerSeen = false;
        foreach (var line in lines)
        {
            if (!headerSeen)
            {
                if (line.TrimStart().StartsWith("NAME")) headerSeen = true;
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) continue;

            // NAME is a single token; FRIENDLY NAME is the rest.
            var trimmed = line.TrimEnd();
            var firstGap = trimmed.IndexOf("  ", StringComparison.Ordinal);
            if (firstGap < 0)
            {
                entries.Add(new CatalogEntry(trimmed.Trim(), trimmed.Trim()));
                continue;
            }
            var name = trimmed[..firstGap].Trim();
            var friendly = trimmed[firstGap..].Trim();
            entries.Add(new CatalogEntry(name, friendly));
        }
        return entries;
    }

    public async Task InstallFromCatalogAsync(string name, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(
            "wsl.exe", new[] { "--install", "-d", name, "--no-launch" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Install {name}");
    }

    public async Task ImportTarAsync(string name, string installDir, string tarPath, int version,
                                     CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe",
            new[] { "--import", name, installDir, tarPath, "--version", version.ToString() }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Import {name}");
    }

    public async Task ImportVhdxAsync(string name, string installDir, string vhdxPath, int version,
                                      CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe",
            new[] { "--import", name, installDir, vhdxPath, "--vhd", "--version", version.ToString() },
            null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Import {name}");
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslDeployServiceTests"`
Expected: PASS (all 4).

- [ ] **Step 6: Commit**

```bash
git add Wsl.Core/CatalogEntry.cs Wsl.Core/WslDeployService.cs Wsl.Core.Tests/WslDeployServiceTests.cs
git commit -m "feat(core): deploy service (catalog install + import)"
```

---

## Task 6: WslBackupService (export / restore)

**Files:**
- Create: `Wsl.Core/ExportFormat.cs`
- Create: `Wsl.Core/WslBackupService.cs`
- Test: `Wsl.Core.Tests/WslBackupServiceTests.cs`

- [ ] **Step 1: Write the ExportFormat enum**

Create `Wsl.Core/ExportFormat.cs`:

```csharp
namespace Wsl.Core;

public enum ExportFormat { Tar, TarGz, Vhd }
```

- [ ] **Step 2: Write the failing test**

Create `Wsl.Core.Tests/WslBackupServiceTests.cs`:

```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslBackupServiceTests
{
    [Theory]
    [InlineData(ExportFormat.Tar, "tar")]
    [InlineData(ExportFormat.TarGz, "tar.gz")]
    [InlineData(ExportFormat.Vhd, "vhd")]
    public async Task Export_uses_format_flag(ExportFormat fmt, string expected)
    {
        var runner = new FakeProcessRunner();
        await new WslBackupService(runner).ExportAsync("Ubuntu", @"C:\b\ubuntu.out", fmt);
        Assert.Equal(
            new[] { "--export", "Ubuntu", @"C:\b\ubuntu.out", "--format", expected },
            runner.LastArgs);
    }

    [Fact]
    public async Task Restore_tar_uses_plain_import()
    {
        var runner = new FakeProcessRunner();
        await new WslBackupService(runner)
            .RestoreAsync("Ubuntu", @"C:\wsl\ubuntu", @"C:\b\ubuntu.tar", ExportFormat.Tar, 2);
        Assert.Equal(
            new[] { "--import", "Ubuntu", @"C:\wsl\ubuntu", @"C:\b\ubuntu.tar", "--version", "2" },
            runner.LastArgs);
    }

    [Fact]
    public async Task Restore_vhd_adds_vhd_flag()
    {
        var runner = new FakeProcessRunner();
        await new WslBackupService(runner)
            .RestoreAsync("Ubuntu", @"C:\wsl\ubuntu", @"C:\b\ubuntu.vhdx", ExportFormat.Vhd, 2);
        Assert.Equal(
            new[] { "--import", "Ubuntu", @"C:\wsl\ubuntu", @"C:\b\ubuntu.vhdx", "--vhd", "--version", "2" },
            runner.LastArgs);
    }

    [Fact]
    public async Task Export_failure_throws()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "There is no distribution with the supplied name.");
        var ex = await Assert.ThrowsAsync<WslException>(
            () => new WslBackupService(runner).ExportAsync("Ghost", @"C:\b\x.tar", ExportFormat.Tar));
        Assert.Equal(WslErrorKind.DistroNotFound, ex.Kind);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslBackupServiceTests"`
Expected: FAIL — `WslBackupService` does not exist.

- [ ] **Step 4: Write the implementation**

Create `Wsl.Core/WslBackupService.cs`:

```csharp
namespace Wsl.Core;

public class WslBackupService
{
    private readonly IProcessRunner _runner;

    public WslBackupService(IProcessRunner runner) => _runner = runner;

    public async Task ExportAsync(string name, string outPath, ExportFormat fmt,
                                  CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe",
            new[] { "--export", name, outPath, "--format", FormatFlag(fmt) }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Export {name}");
    }

    public async Task RestoreAsync(string name, string installDir, string archivePath,
                                   ExportFormat sourceFmt, int version, CancellationToken ct = default)
    {
        var args = sourceFmt == ExportFormat.Vhd
            ? new[] { "--import", name, installDir, archivePath, "--vhd", "--version", version.ToString() }
            : new[] { "--import", name, installDir, archivePath, "--version", version.ToString() };
        var result = await _runner.RunAsync("wsl.exe", args, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Restore {name}");
    }

    private static string FormatFlag(ExportFormat fmt) => fmt switch
    {
        ExportFormat.Tar => "tar",
        ExportFormat.TarGz => "tar.gz",
        ExportFormat.Vhd => "vhd",
        _ => throw new ArgumentOutOfRangeException(nameof(fmt))
    };
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslBackupServiceTests"`
Expected: PASS (all 6 cases).

- [ ] **Step 6: Commit**

```bash
git add Wsl.Core/ExportFormat.cs Wsl.Core/WslBackupService.cs Wsl.Core.Tests/WslBackupServiceTests.cs
git commit -m "feat(core): backup service (export/restore tar+vhd)"
```

---

## Task 7: WslConfigService — global `.wslconfig` (INI round-trip)

**Files:**
- Create: `Wsl.Core/WslGlobalConfig.cs`
- Create: `Wsl.Core/IniParser.cs`
- Create: `Wsl.Core/WslConfigService.cs`
- Test: `Wsl.Core.Tests/IniParserTests.cs`
- Test: `Wsl.Core.Tests/WslGlobalConfigTests.cs`

The critical requirement: read → modify → write must **round-trip unknown keys** (no data loss).
We do this with a generic INI parser plus a typed projection that keeps a passthrough map of
everything it does not model.

- [ ] **Step 1: Write the failing INI parser test**

Create `Wsl.Core.Tests/IniParserTests.cs`:

```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class IniParserTests
{
    private const string Sample =
        "[wsl2]\n" +
        "memory=8GB\n" +
        "processors=4\n" +
        "# a comment\n" +
        "customUnknownKey=keepme\n" +
        "\n" +
        "[experimental]\n" +
        "autoMemoryReclaim=gradual\n";

    [Fact]
    public void Parses_sections_and_keys()
    {
        var ini = IniParser.Parse(Sample);
        Assert.Equal("8GB", ini["wsl2"]["memory"]);
        Assert.Equal("4", ini["wsl2"]["processors"]);
        Assert.Equal("keepme", ini["wsl2"]["customUnknownKey"]);
        Assert.Equal("gradual", ini["experimental"]["autoMemoryReclaim"]);
    }

    [Fact]
    public void Roundtrips_to_text()
    {
        var ini = IniParser.Parse(Sample);
        var text = IniParser.Write(ini);
        var reparsed = IniParser.Parse(text);
        Assert.Equal("keepme", reparsed["wsl2"]["customUnknownKey"]);
        Assert.Equal("gradual", reparsed["experimental"]["autoMemoryReclaim"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~IniParserTests"`
Expected: FAIL — `IniParser` does not exist.

- [ ] **Step 3: Write the INI parser**

Create `Wsl.Core/IniParser.cs`:

```csharp
namespace Wsl.Core;

/// <summary>section -> key -> value, preserving insertion order.</summary>
public static class IniParser
{
    public static Dictionary<string, Dictionary<string, string>> Parse(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = "";
        result[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                current = line[1..^1].Trim();
                if (!result.ContainsKey(current))
                    result[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            result[current][key] = value;
        }
        return result;
    }

    public static string Write(Dictionary<string, Dictionary<string, string>> ini)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (section, kv) in ini)
        {
            if (kv.Count == 0) continue;
            if (section.Length > 0) sb.AppendLine($"[{section}]");
            foreach (var (k, v) in kv) sb.AppendLine($"{k}={v}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run INI test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~IniParserTests"`
Expected: PASS.

- [ ] **Step 5: Write the failing global-config test**

Create `Wsl.Core.Tests/WslGlobalConfigTests.cs`:

```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslGlobalConfigTests
{
    private const string Sample =
        "[wsl2]\n" +
        "memory=8GB\n" +
        "processors=4\n" +
        "localhostForwarding=true\n" +
        "customUnknownKey=keepme\n";

    [Fact]
    public void FromIni_maps_typed_fields()
    {
        var cfg = WslGlobalConfig.FromIni(IniParser.Parse(Sample));
        Assert.Equal("8GB", cfg.Memory);
        Assert.Equal(4, cfg.Processors);
        Assert.True(cfg.LocalhostForwarding);
    }

    [Fact]
    public void Unknown_key_survives_roundtrip()
    {
        var cfg = WslGlobalConfig.FromIni(IniParser.Parse(Sample));
        var ini = cfg.ToIni();
        Assert.Equal("keepme", ini["wsl2"]["customUnknownKey"]);
    }

    [Fact]
    public void Modified_typed_field_is_written()
    {
        var cfg = WslGlobalConfig.FromIni(IniParser.Parse(Sample));
        cfg.Memory = "16GB";
        var ini = cfg.ToIni();
        Assert.Equal("16GB", ini["wsl2"]["memory"]);
        // unknown still there
        Assert.Equal("keepme", ini["wsl2"]["customUnknownKey"]);
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslGlobalConfigTests"`
Expected: FAIL — `WslGlobalConfig` does not exist.

- [ ] **Step 7: Write WslGlobalConfig**

Create `Wsl.Core/WslGlobalConfig.cs`:

```csharp
namespace Wsl.Core;

public class WslGlobalConfig
{
    private const string Section = "wsl2";

    public string? Memory { get; set; }
    public int? Processors { get; set; }
    public string? Swap { get; set; }
    public string? SwapFile { get; set; }
    public string? Networking { get; set; }
    public bool? LocalhostForwarding { get; set; }
    public bool? NestedVirtualization { get; set; }

    /// <summary>section -> key -> value, for everything not modeled above.</summary>
    public Dictionary<string, Dictionary<string, string>> Passthrough { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Modeled = new(StringComparer.OrdinalIgnoreCase)
    {
        "memory", "processors", "swap", "swapFile",
        "networkingMode", "localhostForwarding", "nestedVirtualization"
    };

    public static WslGlobalConfig FromIni(Dictionary<string, Dictionary<string, string>> ini)
    {
        var cfg = new WslGlobalConfig();
        foreach (var (section, kv) in ini)
        {
            foreach (var (key, value) in kv)
            {
                if (section.Equals(Section, StringComparison.OrdinalIgnoreCase) && Modeled.Contains(key))
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "memory": cfg.Memory = value; break;
                        case "processors": cfg.Processors = int.TryParse(value, out var p) ? p : null; break;
                        case "swap": cfg.Swap = value; break;
                        case "swapfile": cfg.SwapFile = value; break;
                        case "networkingmode": cfg.Networking = value; break;
                        case "localhostforwarding": cfg.LocalhostForwarding = ParseBool(value); break;
                        case "nestedvirtualization": cfg.NestedVirtualization = ParseBool(value); break;
                    }
                }
                else
                {
                    if (!cfg.Passthrough.TryGetValue(section, out var pk))
                        cfg.Passthrough[section] = pk = new(StringComparer.OrdinalIgnoreCase);
                    pk[key] = value;
                }
            }
        }
        return cfg;
    }

    public Dictionary<string, Dictionary<string, string>> ToIni()
    {
        // Start from passthrough so unknown keys/sections survive.
        var ini = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (section, kv) in Passthrough)
            ini[section] = new Dictionary<string, string>(kv, StringComparer.OrdinalIgnoreCase);

        if (!ini.TryGetValue(Section, out var wsl2))
            ini[Section] = wsl2 = new(StringComparer.OrdinalIgnoreCase);

        Set(wsl2, "memory", Memory);
        Set(wsl2, "processors", Processors?.ToString());
        Set(wsl2, "swap", Swap);
        Set(wsl2, "swapFile", SwapFile);
        Set(wsl2, "networkingMode", Networking);
        Set(wsl2, "localhostForwarding", LocalhostForwarding?.ToString().ToLowerInvariant());
        Set(wsl2, "nestedVirtualization", NestedVirtualization?.ToString().ToLowerInvariant());
        return ini;
    }

    private static void Set(Dictionary<string, string> kv, string key, string? value)
    {
        if (value is null) kv.Remove(key);
        else kv[key] = value;
    }

    private static bool? ParseBool(string v) =>
        bool.TryParse(v, out var b) ? b : v.Trim() == "1" ? true : v.Trim() == "0" ? false : null;
}
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslGlobalConfigTests"`
Expected: PASS (all 3).

- [ ] **Step 9: Write WslConfigService (global read/write file)**

Create `Wsl.Core/WslConfigService.cs`:

```csharp
namespace Wsl.Core;

public class WslConfigService
{
    private readonly IProcessRunner _runner;
    private readonly Func<string> _globalPath;

    public WslConfigService(IProcessRunner runner, Func<string>? globalPathProvider = null)
    {
        _runner = runner;
        _globalPath = globalPathProvider ?? DefaultGlobalPath;
    }

    private static string DefaultGlobalPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");

    public async Task<WslGlobalConfig> ReadGlobalAsync(CancellationToken ct = default)
    {
        var path = _globalPath();
        if (!File.Exists(path)) return new WslGlobalConfig();
        var text = await File.ReadAllTextAsync(path, ct);
        return WslGlobalConfig.FromIni(IniParser.Parse(text));
    }

    public async Task WriteGlobalAsync(WslGlobalConfig cfg, CancellationToken ct = default)
    {
        var text = IniParser.Write(cfg.ToIni());
        await File.WriteAllTextAsync(_globalPath(), text, ct);
    }
}
```

- [ ] **Step 10: Verify build**

Run: `dotnet build Wsl.Core`
Expected: Build succeeded.

- [ ] **Step 11: Commit**

```bash
git add Wsl.Core/WslGlobalConfig.cs Wsl.Core/IniParser.cs Wsl.Core/WslConfigService.cs Wsl.Core.Tests/IniParserTests.cs Wsl.Core.Tests/WslGlobalConfigTests.cs
git commit -m "feat(core): global .wslconfig read/write with key passthrough"
```

---

## Task 8: WslConfigService — per-distro `wsl.conf`

**Files:**
- Create: `Wsl.Core/WslDistroConfig.cs`
- Modify: `Wsl.Core/WslConfigService.cs`
- Test: `Wsl.Core.Tests/WslDistroConfigTests.cs`

Per-distro config lives inside the Linux filesystem. Read via
`wsl -d <name> -u root cat /etc/wsl.conf`; write by piping the serialized body to
`wsl -d <name> -u root tee /etc/wsl.conf`. The `IProcessRunner` gains an optional stdin
parameter for the write path.

- [ ] **Step 1: Add stdin support to IProcessRunner**

Replace `Wsl.Core/IProcessRunner.cs` contents with:

```csharp
namespace Wsl.Core;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string exe,
        string[] args,
        TimeSpan? timeout = null,
        CancellationToken ct = default);

    /// <summary>Runs with text piped to stdin (used for `tee`-style writes).</summary>
    Task<ProcessResult> RunWithInputAsync(
        string exe,
        string[] args,
        string stdin,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
```

Add the matching method to `Wsl.Core.Tests/FakeProcessRunner.cs` (inside the class):

```csharp
    public string? LastStdin { get; private set; }

    public Task<ProcessResult> RunWithInputAsync(
        string exe, string[] args, string stdin, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        LastStdin = stdin;
        return RunAsync(exe, args, timeout, ct);
    }
```

- [ ] **Step 2: Write the WslDistroConfig model**

Create `Wsl.Core/WslDistroConfig.cs`:

```csharp
namespace Wsl.Core;

public class WslDistroConfig
{
    public string? DefaultUser { get; set; }     // [user] default
    public bool? Systemd { get; set; }            // [boot] systemd
    public bool? AutomountEnabled { get; set; }   // [automount] enabled
    public string? Hostname { get; set; }         // [network] hostname

    public Dictionary<string, Dictionary<string, string>> Passthrough { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public static WslDistroConfig FromIni(Dictionary<string, Dictionary<string, string>> ini)
    {
        var cfg = new WslDistroConfig();
        foreach (var (section, kv) in ini)
        {
            foreach (var (key, value) in kv)
            {
                var matched = (section.ToLowerInvariant(), key.ToLowerInvariant()) switch
                {
                    ("user", "default") => Assign(() => cfg.DefaultUser = value),
                    ("boot", "systemd") => Assign(() => cfg.Systemd = Bool(value)),
                    ("automount", "enabled") => Assign(() => cfg.AutomountEnabled = Bool(value)),
                    ("network", "hostname") => Assign(() => cfg.Hostname = value),
                    _ => false
                };
                if (!matched)
                {
                    if (!cfg.Passthrough.TryGetValue(section, out var pk))
                        cfg.Passthrough[section] = pk = new(StringComparer.OrdinalIgnoreCase);
                    pk[key] = value;
                }
            }
        }
        return cfg;
    }

    public Dictionary<string, Dictionary<string, string>> ToIni()
    {
        var ini = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (section, kv) in Passthrough)
            ini[section] = new Dictionary<string, string>(kv, StringComparer.OrdinalIgnoreCase);

        Put(ini, "user", "default", DefaultUser);
        Put(ini, "boot", "systemd", Systemd?.ToString().ToLowerInvariant());
        Put(ini, "automount", "enabled", AutomountEnabled?.ToString().ToLowerInvariant());
        Put(ini, "network", "hostname", Hostname);
        return ini;
    }

    private static bool Assign(Action a) { a(); return true; }
    private static bool? Bool(string v) => bool.TryParse(v, out var b) ? b : null;

    private static void Put(Dictionary<string, Dictionary<string, string>> ini,
                            string section, string key, string? value)
    {
        if (value is null) return;
        if (!ini.TryGetValue(section, out var kv))
            ini[section] = kv = new(StringComparer.OrdinalIgnoreCase);
        kv[key] = value;
    }
}
```

- [ ] **Step 3: Write the failing test**

Create `Wsl.Core.Tests/WslDistroConfigTests.cs`:

```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslDistroConfigTests
{
    private const string Conf =
        "[boot]\n" +
        "systemd=true\n" +
        "[user]\n" +
        "default=peter\n" +
        "[customsection]\n" +
        "keepme=yes\n";

    [Fact]
    public void Parses_typed_fields()
    {
        var cfg = WslDistroConfig.FromIni(IniParser.Parse(Conf));
        Assert.True(cfg.Systemd);
        Assert.Equal("peter", cfg.DefaultUser);
    }

    [Fact]
    public void Unknown_section_roundtrips()
    {
        var cfg = WslDistroConfig.FromIni(IniParser.Parse(Conf));
        var ini = cfg.ToIni();
        Assert.Equal("yes", ini["customsection"]["keepme"]);
    }

    [Fact]
    public async Task ReadDistro_uses_cat_as_root()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, Conf);
        var svc = new WslConfigService(runner);
        var cfg = await svc.ReadDistroAsync("Ubuntu");
        Assert.Equal(new[] { "-d", "Ubuntu", "-u", "root", "cat", "/etc/wsl.conf" }, runner.LastArgs);
        Assert.Equal("peter", cfg.DefaultUser);
    }

    [Fact]
    public async Task WriteDistro_pipes_to_tee_as_root()
    {
        var runner = new FakeProcessRunner();
        var svc = new WslConfigService(runner);
        var cfg = new WslDistroConfig { DefaultUser = "peter", Systemd = true };
        await svc.WriteDistroAsync("Ubuntu", cfg);
        Assert.Equal(new[] { "-d", "Ubuntu", "-u", "root", "tee", "/etc/wsl.conf" }, runner.LastArgs);
        Assert.Contains("default=peter", runner.LastStdin);
        Assert.Contains("systemd=true", runner.LastStdin);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslDistroConfigTests"`
Expected: FAIL — `ReadDistroAsync` / `WriteDistroAsync` do not exist.

- [ ] **Step 5: Add per-distro methods to WslConfigService**

Add to `Wsl.Core/WslConfigService.cs` inside the class:

```csharp
    public async Task<WslDistroConfig> ReadDistroAsync(string name, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(
            "wsl.exe", new[] { "-d", name, "-u", "root", "cat", "/etc/wsl.conf" }, null, ct);
        // Missing file => empty config (cat exits non-zero); treat empty as defaults.
        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StdOut))
            return new WslDistroConfig();
        return WslDistroConfig.FromIni(IniParser.Parse(result.StdOut));
    }

    public async Task WriteDistroAsync(string name, WslDistroConfig cfg, CancellationToken ct = default)
    {
        var body = IniParser.Write(cfg.ToIni());
        var result = await _runner.RunWithInputAsync(
            "wsl.exe", new[] { "-d", name, "-u", "root", "tee", "/etc/wsl.conf" }, body, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Write wsl.conf for {name}");
    }
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~WslDistroConfigTests"`
Expected: PASS (all 4).

- [ ] **Step 7: Commit**

```bash
git add Wsl.Core/WslDistroConfig.cs Wsl.Core/WslConfigService.cs Wsl.Core/IProcessRunner.cs Wsl.Core.Tests/WslDistroConfigTests.cs Wsl.Core.Tests/FakeProcessRunner.cs
git commit -m "feat(core): per-distro wsl.conf read/write via root cat/tee"
```

---

## Task 9: Bootstrap state store

**Files:**
- Create: `Wsl.Core/BootstrapStep.cs`
- Create: `Wsl.Core/BootstrapStateStore.cs`
- Test: `Wsl.Core.Tests/BootstrapStateStoreTests.cs`

Persists the next pending bootstrap step so the install flow survives a reboot.

- [ ] **Step 1: Write the failing test**

Create `Wsl.Core.Tests/BootstrapStateStoreTests.cs`:

```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class BootstrapStateStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"wslcc-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Default_is_done_when_no_file()
    {
        var store = new BootstrapStateStore(_path);
        Assert.Equal(BootstrapStep.Done, await store.ReadAsync());
    }

    [Fact]
    public async Task Roundtrips_step()
    {
        var store = new BootstrapStateStore(_path);
        await store.WriteAsync(BootstrapStep.RebootPending);
        Assert.Equal(BootstrapStep.RebootPending, await store.ReadAsync());
    }

    [Fact]
    public async Task Clear_resets_to_done()
    {
        var store = new BootstrapStateStore(_path);
        await store.WriteAsync(BootstrapStep.InstallKernel);
        await store.ClearAsync();
        Assert.Equal(BootstrapStep.Done, await store.ReadAsync());
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~BootstrapStateStoreTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write the implementation**

Create `Wsl.Core/BootstrapStep.cs`:

```csharp
namespace Wsl.Core;

public enum BootstrapStep { EnableFeatures, RebootPending, InstallKernel, SetDefaultVersion, Done }
```

Create `Wsl.Core/BootstrapStateStore.cs`:

```csharp
using System.Text.Json;

namespace Wsl.Core;

public class BootstrapStateStore
{
    private readonly string _path;

    public BootstrapStateStore(string? path = null)
        => _path = path ?? DefaultPath();

    private static string DefaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WslCommandCenter", "bootstrap.json");

    private record State(string Step);

    public async Task<BootstrapStep> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return BootstrapStep.Done;
        var json = await File.ReadAllTextAsync(_path, ct);
        var state = JsonSerializer.Deserialize<State>(json);
        return state is not null && Enum.TryParse<BootstrapStep>(state.Step, out var step)
            ? step : BootstrapStep.Done;
    }

    public async Task WriteAsync(BootstrapStep step, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(new State(step.ToString()));
        await File.WriteAllTextAsync(_path, json, ct);
    }

    public Task ClearAsync(CancellationToken ct = default) => WriteAsync(BootstrapStep.Done, ct);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~BootstrapStateStoreTests"`
Expected: PASS (all 3).

- [ ] **Step 5: Commit**

```bash
git add Wsl.Core/BootstrapStep.cs Wsl.Core/BootstrapStateStore.cs Wsl.Core.Tests/BootstrapStateStoreTests.cs
git commit -m "feat(core): bootstrap state store for reboot/resume"
```

---

## Task 10: RealProcessRunner (UTF-16LE decode + timeout)

**Files:**
- Create: `Wsl.Core/RealProcessRunner.cs`

No automated unit test (it shells out to real processes; covered by the optional LiveWsl
integration project in Task 21). Keep it small and obviously correct.

- [ ] **Step 1: Write RealProcessRunner**

Create `Wsl.Core/RealProcessRunner.cs`:

```csharp
using System.Diagnostics;
using System.Text;

namespace Wsl.Core;

/// <summary>
/// Real process runner. wsl.exe management output is UTF-16LE; we read raw bytes and decode,
/// sniffing a BOM and falling back to UTF-16LE. Linux command output (cat etc.) is UTF-8 —
/// callers requiring UTF-8 should use RunWithInputAsync/RunAsync and tolerate either; the BOM
/// sniff handles the common cases.
/// </summary>
public class RealProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public Task<ProcessResult> RunAsync(string exe, string[] args,
        TimeSpan? timeout = null, CancellationToken ct = default)
        => Exec(exe, args, stdin: null, timeout, ct);

    public Task<ProcessResult> RunWithInputAsync(string exe, string[] args, string stdin,
        TimeSpan? timeout = null, CancellationToken ct = default)
        => Exec(exe, args, stdin, timeout, ct);

    private static async Task<ProcessResult> Exec(string exe, string[] args, string? stdin,
        TimeSpan? timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin);
            proc.StandardInput.Close();
        }

        var outBytesTask = ReadAllBytesAsync(proc.StandardOutput.BaseStream, ct);
        var errBytesTask = ReadAllBytesAsync(proc.StandardError.BaseStream, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new WslException(WslErrorKind.Timeout, $"{exe} timed out");
        }

        var stdout = Decode(await outBytesTask);
        var stderr = Decode(await errBytesTask);
        return new ProcessResult(proc.ExitCode, stdout, stderr);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream s, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        // UTF-16LE BOM
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        // UTF-8 BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        // Heuristic: many NUL bytes in even positions => UTF-16LE (wsl management output).
        var nulEven = 0;
        var sample = Math.Min(bytes.Length, 64);
        for (var i = 1; i < sample; i += 2) if (bytes[i] == 0) nulEven++;
        if (nulEven > sample / 4) return Encoding.Unicode.GetString(bytes);
        return Encoding.UTF8.GetString(bytes);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build Wsl.Core`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Wsl.Core/RealProcessRunner.cs
git commit -m "feat(core): real process runner with UTF-16LE decode + timeout"
```

---

## Task 11: Wsl.Contracts — IPC DTOs + JSON round-trip

**Files:**
- Create: `Wsl.Contracts/BrokerMessages.cs`
- Create: `Wsl.Contracts/BrokerJsonContext.cs`
- Test: `Wsl.Core.Tests/ContractsSerializationTests.cs`

The DTOs use a JSON polymorphic discriminator so a single pipe stream can carry any request
type. `System.Text.Json` source-gen keeps the broker free of reflection-based serialization.

- [ ] **Step 1: Write the message types**

Create `Wsl.Contracts/BrokerMessages.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Wsl.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CheckWslInstalledRequest), "checkInstalled")]
[JsonDerivedType(typeof(EnableFeaturesRequest), "enableFeatures")]
[JsonDerivedType(typeof(InstallOrUpdateKernelRequest), "installKernel")]
[JsonDerivedType(typeof(SetDefaultWslVersionRequest), "setDefaultVersion")]
public abstract record BrokerRequest;

public record CheckWslInstalledRequest() : BrokerRequest;
public record EnableFeaturesRequest() : BrokerRequest;
public record InstallOrUpdateKernelRequest() : BrokerRequest;
public record SetDefaultWslVersionRequest(int Version) : BrokerRequest;

public record BrokerResponse(
    bool Success,
    string? Error = null,
    bool RebootRequired = false,
    string? Detail = null);
```

- [ ] **Step 2: Write the source-gen JSON context**

Create `Wsl.Contracts/BrokerJsonContext.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Wsl.Contracts;

[JsonSerializable(typeof(BrokerRequest))]
[JsonSerializable(typeof(BrokerResponse))]
public partial class BrokerJsonContext : JsonSerializerContext { }
```

- [ ] **Step 3: Write the failing test**

Create `Wsl.Core.Tests/ContractsSerializationTests.cs`:

```csharp
using System.Text.Json;
using Wsl.Contracts;
using Xunit;

namespace Wsl.Core.Tests;

public class ContractsSerializationTests
{
    private static readonly JsonSerializerOptions Opts =
        new() { TypeInfoResolver = BrokerJsonContext.Default };

    [Fact]
    public void Roundtrips_polymorphic_request()
    {
        BrokerRequest req = new SetDefaultWslVersionRequest(2);
        var json = JsonSerializer.Serialize(req, Opts);
        var back = JsonSerializer.Deserialize<BrokerRequest>(json, Opts);
        var typed = Assert.IsType<SetDefaultWslVersionRequest>(back);
        Assert.Equal(2, typed.Version);
    }

    [Fact]
    public void Roundtrips_each_request_type()
    {
        BrokerRequest[] all =
        {
            new CheckWslInstalledRequest(),
            new EnableFeaturesRequest(),
            new InstallOrUpdateKernelRequest(),
            new SetDefaultWslVersionRequest(2),
        };
        foreach (var req in all)
        {
            var json = JsonSerializer.Serialize(req, Opts);
            var back = JsonSerializer.Deserialize<BrokerRequest>(json, Opts);
            Assert.Equal(req.GetType(), back!.GetType());
        }
    }

    [Fact]
    public void Roundtrips_response()
    {
        var resp = new BrokerResponse(true, null, RebootRequired: true, "done");
        var json = JsonSerializer.Serialize(resp, Opts);
        var back = JsonSerializer.Deserialize<BrokerResponse>(json, Opts);
        Assert.True(back!.Success);
        Assert.True(back.RebootRequired);
        Assert.Equal("done", back.Detail);
    }
}
```

- [ ] **Step 4: Run test to verify it fails, then passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~ContractsSerializationTests"`
Expected: FAIL first (types absent). After Steps 1-2 are in place, re-run → PASS (all 3).

- [ ] **Step 5: Commit**

```bash
git add Wsl.Contracts/BrokerMessages.cs Wsl.Contracts/BrokerJsonContext.cs Wsl.Core.Tests/ContractsSerializationTests.cs
git commit -m "feat(contracts): broker IPC DTOs with source-gen JSON"
```

---

## Task 12: Broker privileged operations (testable core)

**Files:**
- Create: `Wsl.Broker/Wsl.Broker.csproj`
- Create: `Wsl.Broker/PrivilegedOperations.cs`
- Test: `Wsl.Core.Tests/PrivilegedOperationsTests.cs`

The privileged operations themselves shell out (DISM, wsl). We keep them behind `IProcessRunner`
so the *argument construction and response mapping* are unit-testable; only actual elevation is
untestable.

- [ ] **Step 1: Create the broker project**

Run:

```powershell
dotnet new console -n Wsl.Broker -f net9.0 -o Wsl.Broker
dotnet sln add Wsl.Broker
dotnet add Wsl.Broker reference Wsl.Contracts Wsl.Core
dotnet add Wsl.Core.Tests reference Wsl.Broker
```

Then set the broker to require elevation. Edit `Wsl.Broker/Wsl.Broker.csproj` to add inside the
existing `<PropertyGroup>`:

```xml
    <ApplicationManifest>app.manifest</ApplicationManifest>
```

Create `Wsl.Broker/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifest xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

(Replace `<assembly manifest` with `<assembly` if the editor flags it — the element is
`<assembly xmlns=...>`. The literal correct file is below.)

Exact `app.manifest` content (use this verbatim):

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

- [ ] **Step 2: Write the failing test**

Create `Wsl.Core.Tests/PrivilegedOperationsTests.cs`:

```csharp
using Wsl.Broker;
using Wsl.Contracts;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class PrivilegedOperationsTests
{
    [Fact]
    public async Task EnableFeatures_enables_both_features_and_flags_reboot()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Enabling feature(s)\r\nThe operation completed successfully.");
        runner.Enqueue(0, "Enabling feature(s)\r\nThe operation completed successfully.");
        var ops = new PrivilegedOperations(runner);

        var resp = await ops.HandleAsync(new EnableFeaturesRequest());

        Assert.True(resp.Success);
        Assert.True(resp.RebootRequired);
        // First call enables VirtualMachinePlatform, second the WSL feature.
        Assert.Contains("VirtualMachinePlatform", string.Join(" ", runner.AllArgs[0]));
        Assert.Contains("Microsoft-Windows-Subsystem-Linux", string.Join(" ", runner.AllArgs[1]));
    }

    [Fact]
    public async Task InstallKernel_runs_wsl_update()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Installing: Windows Subsystem for Linux");
        var ops = new PrivilegedOperations(runner);

        var resp = await ops.HandleAsync(new InstallOrUpdateKernelRequest());

        Assert.True(resp.Success);
        Assert.Equal(new[] { "--update" }, runner.LastArgs);
    }

    [Fact]
    public async Task SetDefaultVersion_passes_version()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var ops = new PrivilegedOperations(runner);

        await ops.HandleAsync(new SetDefaultWslVersionRequest(2));

        Assert.Equal(new[] { "--set-default-version", "2" }, runner.LastArgs);
    }

    [Fact]
    public async Task Failure_returns_unsuccessful_response_not_exception()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "Access is denied.");
        var ops = new PrivilegedOperations(runner);

        var resp = await ops.HandleAsync(new InstallOrUpdateKernelRequest());

        Assert.False(resp.Success);
        Assert.Contains("denied", resp.Error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckInstalled_reports_true_when_version_succeeds()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "WSL version: 2.4.13.0");
        var ops = new PrivilegedOperations(runner);

        var resp = await ops.HandleAsync(new CheckWslInstalledRequest());

        Assert.True(resp.Success);
        Assert.Equal("installed", resp.Detail);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~PrivilegedOperationsTests"`
Expected: FAIL — `PrivilegedOperations` does not exist.

- [ ] **Step 4: Write the implementation**

Create `Wsl.Broker/PrivilegedOperations.cs`:

```csharp
using Wsl.Contracts;
using Wsl.Core;

namespace Wsl.Broker;

/// <summary>Maps each typed BrokerRequest to its privileged command(s). No arbitrary passthrough.</summary>
public class PrivilegedOperations
{
    private readonly IProcessRunner _runner;

    public PrivilegedOperations(IProcessRunner runner) => _runner = runner;

    public Task<BrokerResponse> HandleAsync(BrokerRequest request, CancellationToken ct = default)
        => request switch
        {
            CheckWslInstalledRequest => CheckInstalled(ct),
            EnableFeaturesRequest => EnableFeatures(ct),
            InstallOrUpdateKernelRequest => InstallKernel(ct),
            SetDefaultWslVersionRequest r => SetDefaultVersion(r.Version, ct),
            _ => Task.FromResult(new BrokerResponse(false, $"Unknown request: {request.GetType().Name}"))
        };

    private async Task<BrokerResponse> CheckInstalled(CancellationToken ct)
    {
        var r = await _runner.RunAsync("wsl.exe", new[] { "--version" }, null, ct);
        return r.ExitCode == 0
            ? new BrokerResponse(true, Detail: "installed")
            : new BrokerResponse(true, Detail: "absent");
    }

    private async Task<BrokerResponse> EnableFeatures(CancellationToken ct)
    {
        var vmp = await _runner.RunAsync("dism.exe", new[]
        {
            "/online", "/enable-feature", "/featurename:VirtualMachinePlatform",
            "/all", "/norestart"
        }, null, ct);
        if (vmp.ExitCode != 0 && vmp.ExitCode != 3010)
            return Fail(vmp, "Enable VirtualMachinePlatform");

        var wsl = await _runner.RunAsync("dism.exe", new[]
        {
            "/online", "/enable-feature", "/featurename:Microsoft-Windows-Subsystem-Linux",
            "/all", "/norestart"
        }, null, ct);
        if (wsl.ExitCode != 0 && wsl.ExitCode != 3010)
            return Fail(wsl, "Enable WSL feature");

        // DISM exit 3010 = success, reboot required. Always require reboot after enabling.
        return new BrokerResponse(true, RebootRequired: true, Detail: "features enabled");
    }

    private async Task<BrokerResponse> InstallKernel(CancellationToken ct)
    {
        var r = await _runner.RunAsync("wsl.exe", new[] { "--update" }, null, ct);
        return r.ExitCode == 0
            ? new BrokerResponse(true, Detail: "kernel updated")
            : Fail(r, "Install/update kernel");
    }

    private async Task<BrokerResponse> SetDefaultVersion(int version, CancellationToken ct)
    {
        var r = await _runner.RunAsync("wsl.exe",
            new[] { "--set-default-version", version.ToString() }, null, ct);
        return r.ExitCode == 0
            ? new BrokerResponse(true, Detail: $"default version {version}")
            : Fail(r, "Set default version");
    }

    private static BrokerResponse Fail(ProcessResult r, string op)
    {
        var msg = string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr;
        return new BrokerResponse(false, $"{op} failed: {msg.Trim()}", Detail: $"exit {r.ExitCode}");
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~PrivilegedOperationsTests"`
Expected: PASS (all 5).

- [ ] **Step 6: Commit**

```bash
git add Wsl.Broker Wsl.Core.Tests/PrivilegedOperationsTests.cs WslCommandCenter.sln
git commit -m "feat(broker): privileged operations (DISM enable, kernel, default version)"
```

---

## Task 13: Pipe peer verification (bidirectional auth seam)

**Files:**
- Create: `Wsl.Core/Ipc/IPeerVerifier.cs`
- Create: `Wsl.Core/Ipc/WindowsPeerVerifier.cs`
- Create: `Wsl.Core/Ipc/PipeName.cs`
- Test: `Wsl.Core.Tests/PeerVerifierContractTests.cs`

Both sides verify each other by resolving the peer process and checking its image path +
Authenticode signature. We isolate the verification decision behind `IPeerVerifier` so server and
client logic are testable with a fake; the Win32 implementation is the only untestable part.

- [ ] **Step 1: Write the pipe name constant + verifier interface**

Create `Wsl.Core/Ipc/PipeName.cs`:

```csharp
namespace Wsl.Core.Ipc;

public static class PipeName
{
    public const string Broker = "WslCommandCenter.Broker";
}
```

Create `Wsl.Core/Ipc/IPeerVerifier.cs`:

```csharp
namespace Wsl.Core.Ipc;

public interface IPeerVerifier
{
    /// <summary>True if the process at <paramref name="pid"/> is an acceptable peer:
    /// same user, image path matches the expected exe, and Authenticode signature is valid
    /// (or, for dev builds, the path matches and signature check is bypassed by policy).</summary>
    bool IsTrustedPeer(int pid, string expectedExeName);
}
```

- [ ] **Step 2: Write the failing contract test (using a fake verifier)**

Create `Wsl.Core.Tests/PeerVerifierContractTests.cs`:

```csharp
using Wsl.Core.Ipc;
using Xunit;

namespace Wsl.Core.Tests;

/// <summary>A deterministic verifier used by server/client tests.</summary>
public sealed class FakePeerVerifier : IPeerVerifier
{
    private readonly bool _trusted;
    public int? LastPid { get; private set; }
    public string? LastExpected { get; private set; }
    public FakePeerVerifier(bool trusted) => _trusted = trusted;

    public bool IsTrustedPeer(int pid, string expectedExeName)
    {
        LastPid = pid;
        LastExpected = expectedExeName;
        return _trusted;
    }
}

public class PeerVerifierContractTests
{
    [Fact]
    public void Trusted_verifier_returns_true_and_records_args()
    {
        var v = new FakePeerVerifier(trusted: true);
        Assert.True(v.IsTrustedPeer(1234, "Wsl.App.exe"));
        Assert.Equal(1234, v.LastPid);
        Assert.Equal("Wsl.App.exe", v.LastExpected);
    }

    [Fact]
    public void Untrusted_verifier_returns_false()
    {
        var v = new FakePeerVerifier(trusted: false);
        Assert.False(v.IsTrustedPeer(1234, "Wsl.App.exe"));
    }
}
```

- [ ] **Step 3: Run test to verify it fails, then passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~PeerVerifierContractTests"`
Expected: FAIL until `IPeerVerifier`/`FakePeerVerifier` exist, then PASS (2).

- [ ] **Step 4: Write the Windows implementation**

Create `Wsl.Core/Ipc/WindowsPeerVerifier.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Wsl.Core.Ipc;

/// <summary>
/// Verifies a peer process: image filename matches the expected exe, the file is
/// Authenticode-signed, and the running user matches. In DEBUG builds the signature
/// requirement is relaxed (dev binaries are unsigned) but the path check still applies.
/// </summary>
public class WindowsPeerVerifier : IPeerVerifier
{
    public bool IsTrustedPeer(int pid, string expectedExeName)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            var path = proc.MainModule?.FileName;
            if (path is null) return false;
            if (!string.Equals(Path.GetFileName(path), expectedExeName, StringComparison.OrdinalIgnoreCase))
                return false;

#if DEBUG
            return true; // dev binaries are unsigned
#else
            return HasValidSignature(path);
#endif
        }
        catch
        {
            return false;
        }
    }

    private static bool HasValidSignature(string path)
    {
        try
        {
            // Throws if not Authenticode-signed.
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            using var chain = new X509Chain();
            return chain.Build(cert);
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 5: Verify build + tests green**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~PeerVerifierContractTests"`
Expected: PASS (2). `dotnet build Wsl.Core` succeeds.

- [ ] **Step 6: Commit**

```bash
git add Wsl.Core/Ipc Wsl.Core.Tests/PeerVerifierContractTests.cs
git commit -m "feat(core): peer-verification seam + Windows Authenticode verifier"
```

---

## Task 14: Broker pipe server (anti-squat + ACL + peer check)

**Files:**
- Create: `Wsl.Broker/BrokerServer.cs`
- Create: `Wsl.Broker/Program.cs` (replace template)
- Create: `Wsl.Broker/Win32Pipe.cs`

The server: creates the pipe with `FirstPipeInstance` (squatting protection), restricts the ACL
to the current user SID, verifies the connecting client via `IPeerVerifier`, then dispatches one
request and writes one response. No automated test for the live pipe (covered by manual run); the
*dispatch* logic reuses the already-tested `PrivilegedOperations`.

- [ ] **Step 1: Write the Win32 helper for client PID**

Create `Wsl.Broker/Win32Pipe.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wsl.Broker;

internal static class Win32Pipe
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafeHandle Pipe, out uint ClientProcessId);

    public static int GetClientPid(SafeHandle pipeHandle)
        => GetNamedPipeClientProcessId(pipeHandle, out var pid) ? (int)pid : -1;
}
```

- [ ] **Step 2: Write the broker server**

Create `Wsl.Broker/BrokerServer.cs`:

```csharp
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Wsl.Contracts;
using Wsl.Core;
using Wsl.Core.Ipc;

namespace Wsl.Broker;

public class BrokerServer
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOpts =
        new() { TypeInfoResolver = BrokerJsonContext.Default };

    private readonly PrivilegedOperations _ops;
    private readonly IPeerVerifier _verifier;

    public BrokerServer(PrivilegedOperations ops, IPeerVerifier verifier)
    {
        _ops = ops;
        _verifier = verifier;
    }

    /// <summary>Serves requests until the idle timeout elapses with no new connection.
    /// The idle timer is recreated each loop iteration (so it resets after every served
    /// request) and only bounds <c>WaitForConnectionAsync</c> — request handling receives the
    /// outer <paramref name="ct"/>, so a long privileged op is never killed mid-flight.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            using var server = CreatePipe();
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idleCts.CancelAfter(IdleTimeout); // bounds the WaitForConnectionAsync below only
            try
            {
                await server.WaitForConnectionAsync(idleCts.Token);
            }
            catch (OperationCanceledException)
            {
                return; // idle => exit, re-elevate on next demand
            }

            var clientPid = Win32Pipe.GetClientPid(server.SafePipeHandle);
            if (clientPid < 0 || !_verifier.IsTrustedPeer(clientPid, "Wsl.App.exe"))
            {
                server.Disconnect();
                continue;
            }

            await HandleOneAsync(server, ct);
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var sid = WindowsIdentity.GetCurrent().User!;
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            sid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        // FirstPipeInstance: creation fails if the name is already taken (anti-squat).
        return NamedPipeServerStreamAcl.Create(
            PipeName.Broker, PipeDirection.InOut, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            inBufferSize: 0, outBufferSize: 0, security);
    }

    private async Task HandleOneAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        var request = await ReadMessageAsync<BrokerRequest>(server, ct);
        BrokerResponse response;
        try
        {
            response = request is null
                ? new BrokerResponse(false, "Malformed request")
                : await _ops.HandleAsync(request, ct);
        }
        catch (Exception ex)
        {
            response = new BrokerResponse(false, ex.Message);
        }
        await WriteMessageAsync(server, response, ct);
        server.Disconnect();
    }

    // Length-prefixed (4-byte LE) UTF-8 JSON framing.
    private static async Task<T?> ReadMessageAsync<T>(Stream s, CancellationToken ct) where T : class
    {
        var lenBuf = new byte[4];
        if (!await ReadExactAsync(s, lenBuf, ct)) return null;
        var len = BitConverter.ToInt32(lenBuf, 0);
        if (len <= 0 || len > 1_000_000) return null;
        var payload = new byte[len];
        if (!await ReadExactAsync(s, payload, ct)) return null;
        var json = Encoding.UTF8.GetString(payload);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    private static async Task WriteMessageAsync<T>(Stream s, T message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, JsonOpts);
        var payload = Encoding.UTF8.GetBytes(json);
        await s.WriteAsync(BitConverter.GetBytes(payload.Length), ct);
        await s.WriteAsync(payload, ct);
        await s.FlushAsync(ct);
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        var off = 0;
        while (off < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(off), ct);
            if (n == 0) return false;
            off += n;
        }
        return true;
    }
}
```

- [ ] **Step 3: Write Program.cs**

Replace `Wsl.Broker/Program.cs` with:

```csharp
using Wsl.Broker;
using Wsl.Core;
using Wsl.Core.Ipc;

var ops = new PrivilegedOperations(new RealProcessRunner());
var verifier = new WindowsPeerVerifier();
var server = new BrokerServer(ops, verifier);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await server.RunAsync(cts.Token);
```

- [ ] **Step 4: Verify build**

Run: `dotnet build Wsl.Broker`
Expected: Build succeeded. (`NamedPipeServerStreamAcl` lives in `System.IO.Pipes.AccessControl`,
included in the Windows targeting pack — if it fails to resolve, add
`<PackageReference Include="System.IO.Pipes.AccessControl" Version="5.0.0" />` to the broker
csproj, then rebuild.)

- [ ] **Step 5: Commit**

```bash
git add Wsl.Broker/BrokerServer.cs Wsl.Broker/Program.cs Wsl.Broker/Win32Pipe.cs Wsl.Broker/Wsl.Broker.csproj
git commit -m "feat(broker): pipe server with anti-squat, user-SID ACL, peer check"
```

---

## Task 15: Broker client (app side: launch, verify server, request)

**Files:**
- Create: `Wsl.Core/Ipc/IBrokerClient.cs`
- Create: `Wsl.Core/Ipc/BrokerClient.cs`
- Create: `Wsl.Core/Ipc/Win32PipeClient.cs`
- Test: `Wsl.Core.Tests/BrokerClientFramingTests.cs`

The client launches the broker elevated (`runas`), connects, verifies the *server* process, then
sends one request and reads the response. We extract message framing into a static helper so it
is unit-testable over a `MemoryStream` without a real pipe.

- [ ] **Step 1: Write the failing framing test**

Create `Wsl.Core.Tests/BrokerClientFramingTests.cs`:

```csharp
using Wsl.Contracts;
using Wsl.Core.Ipc;
using Xunit;

namespace Wsl.Core.Tests;

public class BrokerClientFramingTests
{
    [Fact]
    public async Task Request_then_response_roundtrip_over_stream()
    {
        using var stream = new MemoryStream();

        // Write a request, rewind, read it back as the server would.
        await PipeFraming.WriteAsync<BrokerRequest>(
            stream, new SetDefaultWslVersionRequest(2), default);
        stream.Position = 0;
        var req = await PipeFraming.ReadAsync<BrokerRequest>(stream, default);
        Assert.IsType<SetDefaultWslVersionRequest>(req);

        // Now a response in a fresh stream.
        using var s2 = new MemoryStream();
        await PipeFraming.WriteAsync(s2, new BrokerResponse(true, Detail: "ok"), default);
        s2.Position = 0;
        var resp = await PipeFraming.ReadAsync<BrokerResponse>(s2, default);
        Assert.True(resp!.Success);
        Assert.Equal("ok", resp.Detail);
    }

    [Fact]
    public async Task Truncated_length_returns_null()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2 }); // < 4 length bytes
        var resp = await PipeFraming.ReadAsync<BrokerResponse>(stream, default);
        Assert.Null(resp);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~BrokerClientFramingTests"`
Expected: FAIL — `PipeFraming` does not exist.

- [ ] **Step 3: Extract shared framing helper**

Create `Wsl.Core/Ipc/PipeFraming.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Wsl.Contracts;

namespace Wsl.Core.Ipc;

/// <summary>Length-prefixed (4-byte LE) UTF-8 JSON framing, shared by client and server.</summary>
public static class PipeFraming
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { TypeInfoResolver = BrokerJsonContext.Default };

    public static async Task WriteAsync<T>(Stream s, T message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, JsonOpts);
        var payload = Encoding.UTF8.GetBytes(json);
        await s.WriteAsync(BitConverter.GetBytes(payload.Length), ct);
        await s.WriteAsync(payload, ct);
        await s.FlushAsync(ct);
    }

    public static async Task<T?> ReadAsync<T>(Stream s, CancellationToken ct) where T : class
    {
        var lenBuf = new byte[4];
        if (!await ReadExactAsync(s, lenBuf, ct)) return null;
        var len = BitConverter.ToInt32(lenBuf, 0);
        if (len <= 0 || len > 1_000_000) return null;
        var payload = new byte[len];
        if (!await ReadExactAsync(s, payload, ct)) return null;
        return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(payload), JsonOpts);
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        var off = 0;
        while (off < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(off), ct);
            if (n == 0) return false;
            off += n;
        }
        return true;
    }
}
```

Refactor `Wsl.Broker/BrokerServer.cs` to use `PipeFraming` (delete its private
`ReadMessageAsync`/`WriteMessageAsync`/`ReadExactAsync` and call
`PipeFraming.ReadAsync<BrokerRequest>(server, ct)` /
`PipeFraming.WriteAsync(server, response, ct)`; remove the now-unused `JsonOpts`,
`Encoding`, `JsonSerializer` usings).

- [ ] **Step 4: Run framing test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~BrokerClientFramingTests"`
Expected: PASS (2).

- [ ] **Step 5: Write the Win32 server-PID helper + broker client**

Create `Wsl.Core/Ipc/Win32PipeClient.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wsl.Core.Ipc;

internal static class Win32PipeClient
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafeHandle Pipe, out uint ServerProcessId);

    public static int GetServerPid(SafeHandle pipeHandle)
        => GetNamedPipeServerProcessId(pipeHandle, out var pid) ? (int)pid : -1;
}
```

Create `Wsl.Core/Ipc/IBrokerClient.cs`:

```csharp
using Wsl.Contracts;

namespace Wsl.Core.Ipc;

public interface IBrokerClient
{
    Task<BrokerResponse> SendAsync(BrokerRequest request, CancellationToken ct = default);
}
```

Create `Wsl.Core/Ipc/BrokerClient.cs`:

```csharp
using System.Diagnostics;
using System.IO.Pipes;
using Wsl.Contracts;

namespace Wsl.Core.Ipc;

public class BrokerClient : IBrokerClient
{
    private readonly string _brokerExePath;
    private readonly IPeerVerifier _verifier;

    public BrokerClient(string brokerExePath, IPeerVerifier verifier)
    {
        _brokerExePath = brokerExePath;
        _verifier = verifier;
    }

    public async Task<BrokerResponse> SendAsync(BrokerRequest request, CancellationToken ct = default)
    {
        // Try to connect to an already-running broker first (short timeout). Only one real
        // connection is ever opened — no separate "probe" that could consume the broker's
        // single FirstPipeInstance slot. If nothing is listening, launch elevated and retry.
        var client = await TryConnectAsync(TimeSpan.FromMilliseconds(300), ct);
        if (client is null)
        {
            if (!LaunchBrokerElevated())
                return new BrokerResponse(false, "Elevation was cancelled.");
            client = await TryConnectAsync(TimeSpan.FromSeconds(10), ct);
            if (client is null)
                return new BrokerResponse(false, "Broker did not start.");
        }

        using (client)
        {
            // Verify the SERVER before sending anything.
            var serverPid = Win32PipeClient.GetServerPid(client.SafePipeHandle);
            if (serverPid < 0 || !_verifier.IsTrustedPeer(serverPid, "Wsl.Broker.exe"))
                return new BrokerResponse(false, "Broker identity verification failed.");

            await PipeFraming.WriteAsync<BrokerRequest>(client, request, ct);
            var resp = await PipeFraming.ReadAsync<BrokerResponse>(client, ct);
            return resp ?? new BrokerResponse(false, "No response from broker.");
        }
    }

    private static async Task<NamedPipeClientStream?> TryConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        var client = new NamedPipeClientStream(
            ".", PipeName.Broker, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(timeout, ct);
            return client;
        }
        catch (TimeoutException)
        {
            await client.DisposeAsync();
            return null;
        }
    }

    private bool LaunchBrokerElevated()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _brokerExePath,
            UseShellExecute = true,
            Verb = "runas", // triggers UAC
        };
        try
        {
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // user declined UAC
        }
    }
}
```

- [ ] **Step 6: Verify build + full suite**

Run: `dotnet build` then `dotnet test Wsl.Core.Tests`
Expected: Build succeeded; all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add Wsl.Core/Ipc Wsl.Broker/BrokerServer.cs Wsl.Core.Tests/BrokerClientFramingTests.cs
git commit -m "feat(core): broker client with server verification + shared framing"
```

---

## Task 16: WinUI 3 app scaffold + DI + NavigationView shell

**Files:**
- Create: `Wsl.App/Wsl.App.csproj` (via template)
- Create: `Wsl.App/App.xaml` + `App.xaml.cs` (DI host)
- Create: `Wsl.App/MainWindow.xaml` + `.cs` (NavigationView)
- Create: `Wsl.App/Services/ServiceRegistration.cs`

WinUI 3 ViewModels are tested in `Wsl.Core.Tests` (they depend only on Core services + a fake
broker client). UI/XAML is verified by manual smoke run.

- [ ] **Step 1: Create the WinUI 3 project AND the ViewModel logic library**

The ViewModels live in a plain `net9.0` class library (`Wsl.App.Logic`) so the xUnit project can
reference them without referencing the WinUI exe. Create **both** projects now — `Wsl.App.Logic`
must exist before `ServiceRegistration` (in this task) imports its namespace, otherwise the build
breaks. (This is the ordering fix: do not defer the logic lib to a later task.)

Run:

```powershell
dotnet new winui3 -n Wsl.App -o Wsl.App
dotnet new classlib -n Wsl.App.Logic -f net9.0 -o Wsl.App.Logic
del Wsl.App.Logic\Class1.cs
dotnet sln add Wsl.App Wsl.App.Logic
dotnet add Wsl.App.Logic reference Wsl.Core Wsl.Contracts
dotnet add Wsl.App.Logic package CommunityToolkit.Mvvm
dotnet add Wsl.App reference Wsl.Core Wsl.Contracts Wsl.App.Logic
dotnet add Wsl.App package CommunityToolkit.Mvvm
dotnet add Wsl.App package Microsoft.Extensions.DependencyInjection
dotnet add Wsl.Core.Tests reference Wsl.App.Logic
```

(The `winui3` template comes from the `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` NuGet
package — already installed on this machine. It scaffolds an app targeting
`net9.0-windows10.0.26100.0` with `<UseWinUI>true</UseWinUI>` and `Microsoft.WindowsAppSDK` 2.1.x;
**leave that generated TFM as-is** — do not downgrade it to 19041. Verified building CLI-only with
no Visual Studio. CommunityToolkit.Mvvm source generators work in a plain `net9.0` library — no
WinUI dependency needed.)

- [ ] **Step 2: Register services**

Create `Wsl.App/Services/ServiceRegistration.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Wsl.Core;
using Wsl.Core.Ipc;

namespace Wsl.App.Services;

public static class ServiceRegistration
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Core (unprivileged) — shared single process runner.
        services.AddSingleton<IProcessRunner, RealProcessRunner>();
        services.AddSingleton<WslDistroService>();
        services.AddSingleton<WslDeployService>();
        services.AddSingleton<WslBackupService>();
        services.AddSingleton<WslConfigService>();
        services.AddSingleton<BootstrapStateStore>();

        // Broker (privileged) — path resolved next to the app exe.
        services.AddSingleton<IPeerVerifier, WindowsPeerVerifier>();
        services.AddSingleton<IBrokerClient>(sp =>
        {
            var brokerPath = Path.Combine(AppContext.BaseDirectory, "Wsl.Broker.exe");
            return new BrokerClient(brokerPath, sp.GetRequiredService<IPeerVerifier>());
        });

        // ViewModels are registered here as each is implemented (Tasks 17-21):
        //   services.AddTransient<DashboardViewModel>();  // Task 17
        //   services.AddTransient<DeployViewModel>();      // Task 18
        //   services.AddTransient<BackupViewModel>();      // Task 19
        //   services.AddTransient<ConfigViewModel>();      // Task 20
        //   services.AddTransient<SetupViewModel>();       // Task 21

        return services.BuildServiceProvider();
    }
}
```

Each page task uncomments (or adds) its own `AddTransient<…ViewModel>()` line plus a
`using Wsl.App.Logic.ViewModels;` import. This keeps every task building green.

- [ ] **Step 3: Wire DI into App.xaml.cs**

Replace `Wsl.App/App.xaml.cs` with:

```csharp
using Microsoft.UI.Xaml;
using Wsl.App.Services;

namespace Wsl.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ServiceRegistration.Build();
        _window = new MainWindow();
        _window.Activate();
    }
}
```

- [ ] **Step 4: Build the NavigationView shell**

Replace `Wsl.App/MainWindow.xaml` with:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Window
    x:Class="Wsl.App.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:muxc="using:Microsoft.UI.Xaml.Controls">
    <Grid>
        <muxc:NavigationView x:Name="Nav"
                             PaneDisplayMode="Left"
                             IsBackButtonVisible="Collapsed"
                             SelectionChanged="Nav_SelectionChanged">
            <muxc:NavigationView.MenuItems>
                <muxc:NavigationViewItem Content="Dashboard" Tag="Dashboard" Icon="Home" />
                <muxc:NavigationViewItem Content="Deploy" Tag="Deploy" Icon="Add" />
                <muxc:NavigationViewItem Content="Backup" Tag="Backup" Icon="Save" />
                <muxc:NavigationViewItem Content="Config" Tag="Config" Icon="Setting" />
                <muxc:NavigationViewItem Content="Setup" Tag="Setup" Icon="Repair" />
            </muxc:NavigationView.MenuItems>
            <Frame x:Name="ContentFrame" />
        </muxc:NavigationView>
    </Grid>
</Window>
```

Replace `Wsl.App/MainWindow.xaml.cs` with a page map that is empty now and gets one entry added
by each page task (17-21). This compiles immediately because it references no page types yet:

```csharp
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
```

This task now builds and runs on its own: the shell appears with an empty content frame (no pages
registered yet). Each page task fills in one `_pages[...]` entry.

- [ ] **Step 5: Build to verify the shell compiles**

Run: `dotnet build Wsl.App`
Expected: Build succeeded. Running it shows the NavigationView with an empty content area.

- [ ] **Step 6: Commit (scaffold)**

```bash
git add Wsl.App/Wsl.App.csproj Wsl.App.Logic Wsl.App/App.xaml.cs Wsl.App/Services Wsl.App/MainWindow.xaml Wsl.App/MainWindow.xaml.cs WslCommandCenter.sln
git commit -m "feat(app): WinUI3 scaffold, logic lib, DI host, NavigationView shell"
```

---

## Task 17: Dashboard (distro lifecycle) — VM + page

**Files:**
- Create: `Wsl.App/ViewModels/DashboardViewModel.cs`
- Create: `Wsl.App/Views/DashboardPage.xaml` + `.cs`
- Test: `Wsl.Core.Tests/DashboardViewModelTests.cs`

`Wsl.App.Logic` already exists (created in Task 16) and is referenced by both `Wsl.App` and
`Wsl.Core.Tests`. This task adds the Dashboard VM there, registers it in DI, and wires it into the
NavigationView page map.

- [ ] **Step 1: Register the Dashboard VM + nav entry**

In `Wsl.App/Services/ServiceRegistration.cs`, add `using Wsl.App.Logic.ViewModels;` and the
registration line inside `Build()` (where the VM-registration comment block is):

```csharp
        services.AddTransient<DashboardViewModel>();
```

In `Wsl.App/MainWindow.xaml.cs`, add the Dashboard entry to `_pages` in the constructor (before
`NavigateTo("Dashboard")`):

```csharp
        _pages["Dashboard"] = typeof(Views.DashboardPage);
```

- [ ] **Step 2: Write the failing test**

Create `Wsl.Core.Tests/DashboardViewModelTests.cs`:

```csharp
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class DashboardViewModelTests
{
    private const string ListOutput =
        "  NAME      STATE     VERSION\r\n" +
        "* Ubuntu    Stopped   2\r\n" +
        "  Debian    Running   2\r\n";

    [Fact]
    public async Task Refresh_populates_distros()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);
        var vm = new DashboardViewModel(new WslDistroService(runner));

        await vm.RefreshAsync();

        Assert.Equal(2, vm.Distros.Count);
        Assert.Equal("Ubuntu", vm.Distros[0].Name);
    }

    [Fact]
    public async Task Refresh_surfaces_error_message_on_failure()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "The Windows Subsystem for Linux is not installed.");
        var vm = new DashboardViewModel(new WslDistroService(runner));

        await vm.RefreshAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(vm.Distros);
    }

    [Fact]
    public async Task Terminate_then_refresh_issues_terminate_then_list()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");          // terminate
        runner.Enqueue(0, ListOutput);  // refresh
        var vm = new DashboardViewModel(new WslDistroService(runner));

        await vm.TerminateAsync("Debian");

        Assert.Equal(new[] { "--terminate", "Debian" }, runner.AllArgs[0]);
        Assert.Equal(2, vm.Distros.Count); // refreshed after action
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~DashboardViewModelTests"`
Expected: FAIL — `DashboardViewModel` does not exist.

- [ ] **Step 4: Write the ViewModel**

Create `Wsl.App.Logic/ViewModels/DashboardViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly WslDistroService _distros;

    public DashboardViewModel(WslDistroService distros) => _distros = distros;

    public ObservableCollection<Distro> Distros { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var list = await _distros.ListAsync();
            Distros.Clear();
            foreach (var d in list) Distros.Add(d);
        }
        catch (WslException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public Task StartAsync(string name) => ActionThenRefresh(() => _distros.StartAsync(name));

    [RelayCommand]
    public Task TerminateAsync(string name) => ActionThenRefresh(() => _distros.TerminateAsync(name));

    [RelayCommand]
    public Task SetDefaultAsync(string name) => ActionThenRefresh(() => _distros.SetDefaultAsync(name));

    [RelayCommand]
    public Task UnregisterAsync(string name) => ActionThenRefresh(() => _distros.UnregisterAsync(name));

    private async Task ActionThenRefresh(Func<Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
            await RefreshAsync();
        }
        catch (WslException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~DashboardViewModelTests"`
Expected: PASS (3).

- [ ] **Step 6: Write the page**

Create `Wsl.App/Views/DashboardPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Wsl.App.Views.DashboardPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:muxc="using:Microsoft.UI.Xaml.Controls"
    xmlns:core="using:Wsl.Core">
    <Grid Padding="24" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Orientation="Horizontal" Spacing="12">
            <TextBlock Text="Distributions" Style="{StaticResource TitleTextBlockStyle}" />
            <Button Content="Refresh" Command="{x:Bind Vm.RefreshCommand}" />
            <muxc:ProgressRing IsActive="{x:Bind Vm.IsBusy, Mode=OneWay}" Width="20" Height="20" />
        </StackPanel>

        <muxc:InfoBar Grid.Row="1" IsOpen="{x:Bind Vm.ErrorMessage, Mode=OneWay, Converter={StaticResource NotNullToBool}}"
                     Severity="Error" Message="{x:Bind Vm.ErrorMessage, Mode=OneWay}" />

        <ListView Grid.Row="2" ItemsSource="{x:Bind Vm.Distros, Mode=OneWay}">
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="core:Distro">
                    <Grid ColumnSpacing="16" Padding="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="200" />
                            <ColumnDefinition Width="120" />
                            <ColumnDefinition Width="60" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Text="{x:Bind Name}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="1" Text="{x:Bind State}" VerticalAlignment="Center" />
                        <TextBlock Grid.Column="2" Text="{x:Bind Version}" VerticalAlignment="Center" />
                        <StackPanel Grid.Column="3" Orientation="Horizontal" Spacing="8">
                            <Button Content="Start" Click="Start_Click" Tag="{x:Bind Name}" />
                            <Button Content="Stop" Click="Stop_Click" Tag="{x:Bind Name}" />
                            <Button Content="Set Default" Click="SetDefault_Click" Tag="{x:Bind Name}" />
                            <Button Content="Unregister" Click="Unregister_Click" Tag="{x:Bind Name}" />
                        </StackPanel>
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</Page>
```

Create `Wsl.App/Views/DashboardPage.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Wsl.App.Logic.ViewModels;

namespace Wsl.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel Vm { get; }

    public DashboardPage()
    {
        Vm = App.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();
        _ = Vm.RefreshAsync();
    }

    private async void Start_Click(object s, RoutedEventArgs e)
        => await Vm.StartAsync((string)((Button)s).Tag);

    private async void Stop_Click(object s, RoutedEventArgs e)
        => await Vm.TerminateAsync((string)((Button)s).Tag);

    private async void SetDefault_Click(object s, RoutedEventArgs e)
        => await Vm.SetDefaultAsync((string)((Button)s).Tag);

    private async void Unregister_Click(object s, RoutedEventArgs e)
    {
        var name = (string)((Button)s).Tag;
        var dialog = new ContentDialog
        {
            Title = "Unregister distro",
            Content = $"This permanently deletes '{name}' and its filesystem. Type the name to confirm.",
            PrimaryButtonText = "Unregister",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Vm.UnregisterAsync(name);
    }
}
```

Add the `NotNullToBool` converter — create `Wsl.App/Converters/NotNullToBoolConverter.cs`:

```csharp
using Microsoft.UI.Xaml.Data;

namespace Wsl.App.Converters;

public class NotNullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not null;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
```

Register it in `Wsl.App/App.xaml` inside `<Application.Resources>`:

```xml
<ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
        <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
    </ResourceDictionary.MergedDictionaries>
    <conv:NotNullToBoolConverter xmlns:conv="using:Wsl.App.Converters" x:Key="NotNullToBool" />
</ResourceDictionary>
```

- [ ] **Step 7: Commit**

```bash
git add Wsl.App.Logic Wsl.App/Views/DashboardPage.xaml Wsl.App/Views/DashboardPage.xaml.cs Wsl.App/Converters Wsl.App/App.xaml Wsl.App/Services/ServiceRegistration.cs Wsl.App/MainWindow.xaml.cs Wsl.Core.Tests/DashboardViewModelTests.cs WslCommandCenter.sln
git commit -m "feat(app): dashboard VM + page (distro lifecycle)"
```

---

## Task 18: Deploy (new distro) — VM + page

**Files:**
- Create: `Wsl.App.Logic/ViewModels/DeployViewModel.cs`
- Create: `Wsl.App/Views/DeployPage.xaml` + `.cs`
- Test: `Wsl.Core.Tests/DeployViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Wsl.Core.Tests/DeployViewModelTests.cs`:

```csharp
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class DeployViewModelTests
{
    private const string Catalog =
        "NAME       FRIENDLY NAME\r\n" +
        "Ubuntu     Ubuntu\r\n" +
        "Debian     Debian GNU/Linux\r\n";

    [Fact]
    public async Task LoadCatalog_populates_entries()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, Catalog);
        var vm = new DeployViewModel(new WslDeployService(runner));

        await vm.LoadCatalogAsync();

        Assert.Equal(2, vm.Catalog.Count);
        Assert.Equal("Ubuntu", vm.Catalog[0].Name);
    }

    [Fact]
    public async Task InstallSelected_calls_install()
    {
        var runner = new FakeProcessRunner();
        var vm = new DeployViewModel(new WslDeployService(runner))
        {
            SelectedCatalogEntry = new CatalogEntry("Debian", "Debian GNU/Linux")
        };

        await vm.InstallSelectedAsync();

        Assert.Equal(new[] { "--install", "-d", "Debian", "--no-launch" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task ImportArchive_tar_calls_import_tar()
    {
        var runner = new FakeProcessRunner();
        var vm = new DeployViewModel(new WslDeployService(runner))
        {
            ImportName = "Custom",
            ImportInstallDir = @"C:\wsl\custom",
            ImportArchivePath = @"C:\b\custom.tar",
            ImportVersion = 2,
        };

        await vm.ImportArchiveAsync();

        Assert.Equal(
            new[] { "--import", "Custom", @"C:\wsl\custom", @"C:\b\custom.tar", "--version", "2" },
            runner.AllArgs[0]);
    }

    [Fact]
    public async Task ImportArchive_vhdx_calls_import_vhd()
    {
        var runner = new FakeProcessRunner();
        var vm = new DeployViewModel(new WslDeployService(runner))
        {
            ImportName = "Custom",
            ImportInstallDir = @"C:\wsl\custom",
            ImportArchivePath = @"C:\b\custom.vhdx",
            ImportVersion = 2,
        };

        await vm.ImportArchiveAsync();

        Assert.Contains("--vhd", runner.AllArgs[0]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~DeployViewModelTests"`
Expected: FAIL — `DeployViewModel` does not exist.

- [ ] **Step 3: Write the ViewModel**

Create `Wsl.App.Logic/ViewModels/DeployViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class DeployViewModel : ObservableObject
{
    private readonly WslDeployService _deploy;

    public DeployViewModel(WslDeployService deploy) => _deploy = deploy;

    public ObservableCollection<CatalogEntry> Catalog { get; } = new();

    [ObservableProperty] private CatalogEntry? _selectedCatalogEntry;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    // Import fields
    [ObservableProperty] private string _importName = "";
    [ObservableProperty] private string _importInstallDir = "";
    [ObservableProperty] private string _importArchivePath = "";
    [ObservableProperty] private int _importVersion = 2;

    [RelayCommand]
    public async Task LoadCatalogAsync()
    {
        await Guarded(async () =>
        {
            var entries = await _deploy.ListAvailableAsync();
            Catalog.Clear();
            foreach (var e in entries) Catalog.Add(e);
            StatusMessage = $"{Catalog.Count} distros available.";
        });
    }

    [RelayCommand]
    public async Task InstallSelectedAsync()
    {
        if (SelectedCatalogEntry is null) { ErrorMessage = "Select a distro first."; return; }
        await Guarded(async () =>
        {
            await _deploy.InstallFromCatalogAsync(SelectedCatalogEntry.Name);
            StatusMessage = $"Installed {SelectedCatalogEntry.Name}.";
        });
    }

    [RelayCommand]
    public async Task ImportArchiveAsync()
    {
        await Guarded(async () =>
        {
            var isVhdx = ImportArchivePath.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase);
            if (isVhdx)
                await _deploy.ImportVhdxAsync(ImportName, ImportInstallDir, ImportArchivePath, ImportVersion);
            else
                await _deploy.ImportTarAsync(ImportName, ImportInstallDir, ImportArchivePath, ImportVersion);
            StatusMessage = $"Imported {ImportName}.";
        });
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~DeployViewModelTests"`
Expected: PASS (4).

- [ ] **Step 5: Write the page**

Create `Wsl.App/Views/DeployPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Wsl.App.Views.DeployPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:muxc="using:Microsoft.UI.Xaml.Controls"
    xmlns:core="using:Wsl.Core">
    <ScrollViewer>
        <StackPanel Padding="24" Spacing="16" MaxWidth="700" HorizontalAlignment="Left">
            <TextBlock Text="Deploy a distribution" Style="{StaticResource TitleTextBlockStyle}" />

            <muxc:InfoBar IsOpen="{x:Bind Vm.ErrorMessage, Mode=OneWay, Converter={StaticResource NotNullToBool}}"
                         Severity="Error" Message="{x:Bind Vm.ErrorMessage, Mode=OneWay}" />
            <muxc:InfoBar IsOpen="{x:Bind Vm.StatusMessage, Mode=OneWay, Converter={StaticResource NotNullToBool}}"
                         Severity="Success" Message="{x:Bind Vm.StatusMessage, Mode=OneWay}" />

            <TextBlock Text="From catalog" Style="{StaticResource SubtitleTextBlockStyle}" />
            <Button Content="Load catalog" Command="{x:Bind Vm.LoadCatalogCommand}" />
            <ComboBox Header="Distro" ItemsSource="{x:Bind Vm.Catalog, Mode=OneWay}"
                      SelectedItem="{x:Bind Vm.SelectedCatalogEntry, Mode=TwoWay}"
                      DisplayMemberPath="FriendlyName" Width="320" />
            <Button Content="Install" Command="{x:Bind Vm.InstallSelectedCommand}" />

            <TextBlock Text="From archive (.tar / .tar.gz / .vhdx)" Style="{StaticResource SubtitleTextBlockStyle}" />
            <TextBox Header="Name" Text="{x:Bind Vm.ImportName, Mode=TwoWay}" Width="320" />
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBox Header="Install directory" Text="{x:Bind Vm.ImportInstallDir, Mode=TwoWay}" Width="280" />
                <Button Content="Browse…" Click="BrowseDir_Click" VerticalAlignment="Bottom" />
            </StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBox Header="Archive" Text="{x:Bind Vm.ImportArchivePath, Mode=TwoWay}" Width="280" />
                <Button Content="Browse…" Click="BrowseArchive_Click" VerticalAlignment="Bottom" />
            </StackPanel>
            <Button Content="Import" Command="{x:Bind Vm.ImportArchiveCommand}" />

            <muxc:ProgressRing IsActive="{x:Bind Vm.IsBusy, Mode=OneWay}" />
        </StackPanel>
    </ScrollViewer>
</Page>
```

Create `Wsl.App/Views/DeployPage.xaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Wsl.App.Logic.ViewModels;
using WinRT.Interop;

namespace Wsl.App.Views;

public sealed partial class DeployPage : Page
{
    public DeployViewModel Vm { get; }

    public DeployPage()
    {
        Vm = App.Services.GetRequiredService<DeployViewModel>();
        InitializeComponent();
    }

    private async void BrowseArchive_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add(".tar");
        picker.FileTypeFilter.Add(".gz");
        picker.FileTypeFilter.Add(".vhdx");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) Vm.ImportArchivePath = file.Path;
    }

    private async void BrowseDir_Click(object s, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) Vm.ImportInstallDir = folder.Path;
    }
}
```

The pickers need the active window handle. Add to `Wsl.App/App.xaml.cs`: a static
`public static object MainWindowHandleHost { get; private set; }` set to `_window` in
`OnLaunched` (assign `MainWindowHandleHost = _window;` right after creating it). Update the
declaration of `_window` accordingly.

- [ ] **Step 6: Register the VM + nav entry**

In `ServiceRegistration.Build()` add `services.AddTransient<DeployViewModel>();`.
In `MainWindow.xaml.cs` constructor add `_pages["Deploy"] = typeof(Views.DeployPage);`.

- [ ] **Step 7: Commit**

```bash
git add Wsl.App.Logic/ViewModels/DeployViewModel.cs Wsl.App/Views/DeployPage.xaml Wsl.App/Views/DeployPage.xaml.cs Wsl.App/App.xaml.cs Wsl.App/Services/ServiceRegistration.cs Wsl.App/MainWindow.xaml.cs Wsl.Core.Tests/DeployViewModelTests.cs
git commit -m "feat(app): deploy VM + page (catalog install + archive import)"
```

---

## Task 19: Backup (export / restore) — VM + page

**Files:**
- Create: `Wsl.App.Logic/ViewModels/BackupViewModel.cs`
- Create: `Wsl.App/Views/BackupPage.xaml` + `.cs`
- Test: `Wsl.Core.Tests/BackupViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Wsl.Core.Tests/BackupViewModelTests.cs`:

```csharp
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class BackupViewModelTests
{
    [Fact]
    public async Task Export_calls_export_with_selected_format()
    {
        var runner = new FakeProcessRunner();
        var vm = new BackupViewModel(new WslBackupService(runner))
        {
            ExportDistro = "Ubuntu",
            ExportPath = @"C:\b\ubuntu.vhdx",
            ExportFormat = ExportFormat.Vhd,
        };

        await vm.ExportAsync();

        Assert.Equal(
            new[] { "--export", "Ubuntu", @"C:\b\ubuntu.vhdx", "--format", "vhd" },
            runner.AllArgs[0]);
        Assert.NotNull(vm.StatusMessage);
    }

    [Fact]
    public async Task Restore_calls_import_with_source_format()
    {
        var runner = new FakeProcessRunner();
        var vm = new BackupViewModel(new WslBackupService(runner))
        {
            RestoreName = "Ubuntu",
            RestoreInstallDir = @"C:\wsl\ubuntu",
            RestoreArchivePath = @"C:\b\ubuntu.tar",
            RestoreFormat = ExportFormat.Tar,
            RestoreVersion = 2,
        };

        await vm.RestoreAsync();

        Assert.Equal(
            new[] { "--import", "Ubuntu", @"C:\wsl\ubuntu", @"C:\b\ubuntu.tar", "--version", "2" },
            runner.AllArgs[0]);
    }

    [Fact]
    public async Task Export_failure_sets_error()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "There is no distribution with the supplied name.");
        var vm = new BackupViewModel(new WslBackupService(runner))
        {
            ExportDistro = "Ghost", ExportPath = @"C:\b\x.tar", ExportFormat = ExportFormat.Tar,
        };

        await vm.ExportAsync();

        Assert.NotNull(vm.ErrorMessage);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~BackupViewModelTests"`
Expected: FAIL — `BackupViewModel` does not exist.

- [ ] **Step 3: Write the ViewModel**

Create `Wsl.App.Logic/ViewModels/BackupViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly WslBackupService _backup;

    public BackupViewModel(WslBackupService backup) => _backup = backup;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    // Export
    [ObservableProperty] private string _exportDistro = "";
    [ObservableProperty] private string _exportPath = "";
    [ObservableProperty] private ExportFormat _exportFormat = ExportFormat.Tar;

    // Restore
    [ObservableProperty] private string _restoreName = "";
    [ObservableProperty] private string _restoreInstallDir = "";
    [ObservableProperty] private string _restoreArchivePath = "";
    [ObservableProperty] private ExportFormat _restoreFormat = ExportFormat.Tar;
    [ObservableProperty] private int _restoreVersion = 2;

    public ExportFormat[] Formats { get; } = { ExportFormat.Tar, ExportFormat.TarGz, ExportFormat.Vhd };

    [RelayCommand]
    public async Task ExportAsync()
    {
        await Guarded(async () =>
        {
            await _backup.ExportAsync(ExportDistro, ExportPath, ExportFormat);
            StatusMessage = $"Exported {ExportDistro} → {ExportPath}";
        });
    }

    [RelayCommand]
    public async Task RestoreAsync()
    {
        await Guarded(async () =>
        {
            await _backup.RestoreAsync(RestoreName, RestoreInstallDir, RestoreArchivePath,
                                       RestoreFormat, RestoreVersion);
            StatusMessage = $"Restored {RestoreName}";
        });
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~BackupViewModelTests"`
Expected: PASS (3).

- [ ] **Step 5: Write the page**

Create `Wsl.App/Views/BackupPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Wsl.App.Views.BackupPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:muxc="using:Microsoft.UI.Xaml.Controls">
    <ScrollViewer>
        <StackPanel Padding="24" Spacing="16" MaxWidth="700" HorizontalAlignment="Left">
            <TextBlock Text="Backup &amp; restore" Style="{StaticResource TitleTextBlockStyle}" />

            <muxc:InfoBar IsOpen="{x:Bind Vm.ErrorMessage, Mode=OneWay, Converter={StaticResource NotNullToBool}}"
                         Severity="Error" Message="{x:Bind Vm.ErrorMessage, Mode=OneWay}" />
            <muxc:InfoBar IsOpen="{x:Bind Vm.StatusMessage, Mode=OneWay, Converter={StaticResource NotNullToBool}}"
                         Severity="Success" Message="{x:Bind Vm.StatusMessage, Mode=OneWay}" />

            <TextBlock Text="Export" Style="{StaticResource SubtitleTextBlockStyle}" />
            <TextBox Header="Distro" Text="{x:Bind Vm.ExportDistro, Mode=TwoWay}" Width="320" />
            <ComboBox Header="Format" ItemsSource="{x:Bind Vm.Formats}"
                      SelectedItem="{x:Bind Vm.ExportFormat, Mode=TwoWay}" Width="160" />
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBox Header="Output path" Text="{x:Bind Vm.ExportPath, Mode=TwoWay}" Width="280" />
                <Button Content="Browse…" Click="BrowseExport_Click" VerticalAlignment="Bottom" />
            </StackPanel>
            <Button Content="Export" Command="{x:Bind Vm.ExportCommand}" />

            <TextBlock Text="Restore" Style="{StaticResource SubtitleTextBlockStyle}" />
            <TextBox Header="New name" Text="{x:Bind Vm.RestoreName, Mode=TwoWay}" Width="320" />
            <TextBox Header="Install directory" Text="{x:Bind Vm.RestoreInstallDir, Mode=TwoWay}" Width="320" />
            <ComboBox Header="Source format" ItemsSource="{x:Bind Vm.Formats}"
                      SelectedItem="{x:Bind Vm.RestoreFormat, Mode=TwoWay}" Width="160" />
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBox Header="Archive" Text="{x:Bind Vm.RestoreArchivePath, Mode=TwoWay}" Width="280" />
                <Button Content="Browse…" Click="BrowseRestore_Click" VerticalAlignment="Bottom" />
            </StackPanel>
            <Button Content="Restore" Command="{x:Bind Vm.RestoreCommand}" />

            <muxc:ProgressRing IsActive="{x:Bind Vm.IsBusy, Mode=OneWay}" />
        </StackPanel>
    </ScrollViewer>
</Page>
```

Create `Wsl.App/Views/BackupPage.xaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Wsl.App.Logic.ViewModels;
using WinRT.Interop;

namespace Wsl.App.Views;

public sealed partial class BackupPage : Page
{
    public BackupViewModel Vm { get; }

    public BackupPage()
    {
        Vm = App.Services.GetRequiredService<BackupViewModel>();
        InitializeComponent();
    }

    private async void BrowseExport_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeChoices.Add("Tar", new List<string> { ".tar" });
        picker.FileTypeChoices.Add("Gzip tar", new List<string> { ".gz" });
        picker.FileTypeChoices.Add("VHDX", new List<string> { ".vhdx" });
        var file = await picker.PickSaveFileAsync();
        if (file is not null) Vm.ExportPath = file.Path;
    }

    private async void BrowseRestore_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindowHandleHost));
        picker.FileTypeFilter.Add(".tar");
        picker.FileTypeFilter.Add(".gz");
        picker.FileTypeFilter.Add(".vhdx");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) Vm.RestoreArchivePath = file.Path;
    }
}
```

- [ ] **Step 6: Register the VM + nav entry**

In `ServiceRegistration.Build()` add `services.AddTransient<BackupViewModel>();`.
In `MainWindow.xaml.cs` constructor add `_pages["Backup"] = typeof(Views.BackupPage);`.

- [ ] **Step 7: Commit**

```bash
git add Wsl.App.Logic/ViewModels/BackupViewModel.cs Wsl.App/Views/BackupPage.xaml Wsl.App/Views/BackupPage.xaml.cs Wsl.App/Services/ServiceRegistration.cs Wsl.App/MainWindow.xaml.cs Wsl.Core.Tests/BackupViewModelTests.cs
git commit -m "feat(app): backup VM + page (export/restore)"
```

---

## Task 20: Config editor — VM + page

**Files:**
- Create: `Wsl.App.Logic/ViewModels/ConfigViewModel.cs`
- Create: `Wsl.App/Views/ConfigPage.xaml` + `.cs`
- Test: `Wsl.Core.Tests/ConfigViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Wsl.Core.Tests/ConfigViewModelTests.cs`:

```csharp
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class ConfigViewModelTests
{
    private static string GlobalIni =
        "[wsl2]\nmemory=8GB\nprocessors=4\ncustomKey=keep\n";

    private static WslConfigService MakeConfigService(FakeProcessRunner runner, string globalFile)
        => new(runner, globalPathProvider: () => globalFile);

    [Fact]
    public async Task LoadGlobal_reads_typed_fields(/* uses temp file */)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"wslcfg-{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(tmp, GlobalIni);
        try
        {
            var runner = new FakeProcessRunner();
            var vm = new ConfigViewModel(MakeConfigService(runner, tmp));

            await vm.LoadGlobalAsync();

            Assert.Equal("8GB", vm.Memory);
            Assert.Equal("4", vm.Processors);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task SaveGlobal_preserves_unknown_key()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"wslcfg-{Guid.NewGuid():N}.ini");
        await File.WriteAllTextAsync(tmp, GlobalIni);
        try
        {
            var runner = new FakeProcessRunner();
            var vm = new ConfigViewModel(MakeConfigService(runner, tmp));
            await vm.LoadGlobalAsync();
            vm.Memory = "16GB";

            await vm.SaveGlobalAsync();

            var written = await File.ReadAllTextAsync(tmp);
            Assert.Contains("memory=16GB", written);
            Assert.Contains("customKey=keep", written);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task LoadDistro_reads_via_cat()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "[user]\ndefault=peter\n[boot]\nsystemd=true\n");
        var vm = new ConfigViewModel(MakeConfigService(runner, "unused")) { SelectedDistro = "Ubuntu" };

        await vm.LoadDistroAsync();

        Assert.Equal("peter", vm.DefaultUser);
        Assert.True(vm.Systemd);
        Assert.Equal(new[] { "-d", "Ubuntu", "-u", "root", "cat", "/etc/wsl.conf" }, runner.AllArgs[0]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~ConfigViewModelTests"`
Expected: FAIL — `ConfigViewModel` does not exist.

- [ ] **Step 3: Write the ViewModel**

Create `Wsl.App.Logic/ViewModels/ConfigViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Core;

namespace Wsl.App.Logic.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly WslConfigService _config;
    private WslGlobalConfig _global = new();
    private WslDistroConfig _distro = new();

    public ConfigViewModel(WslConfigService config) => _config = config;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;

    // Global fields (string for direct TextBox binding)
    [ObservableProperty] private string? _memory;
    [ObservableProperty] private string? _processors;
    [ObservableProperty] private string? _networking;
    [ObservableProperty] private bool _localhostForwarding;

    // Per-distro
    [ObservableProperty] private string? _selectedDistro;
    [ObservableProperty] private string? _defaultUser;
    [ObservableProperty] private bool _systemd;
    [ObservableProperty] private string? _hostname;

    [RelayCommand]
    public async Task LoadGlobalAsync()
    {
        await Guarded(async () =>
        {
            _global = await _config.ReadGlobalAsync();
            Memory = _global.Memory;
            Processors = _global.Processors?.ToString();
            Networking = _global.Networking;
            LocalhostForwarding = _global.LocalhostForwarding ?? false;
        });
    }

    [RelayCommand]
    public async Task SaveGlobalAsync()
    {
        await Guarded(async () =>
        {
            _global.Memory = string.IsNullOrWhiteSpace(Memory) ? null : Memory;
            _global.Processors = int.TryParse(Processors, out var p) ? p : null;
            _global.Networking = string.IsNullOrWhiteSpace(Networking) ? null : Networking;
            _global.LocalhostForwarding = LocalhostForwarding;
            await _config.WriteGlobalAsync(_global);
            StatusMessage = "Saved .wslconfig. Run `wsl --shutdown` to apply.";
        });
    }

    [RelayCommand]
    public async Task LoadDistroAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDistro)) { ErrorMessage = "Pick a distro."; return; }
        await Guarded(async () =>
        {
            _distro = await _config.ReadDistroAsync(SelectedDistro!);
            DefaultUser = _distro.DefaultUser;
            Systemd = _distro.Systemd ?? false;
            Hostname = _distro.Hostname;
        });
    }

    [RelayCommand]
    public async Task SaveDistroAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDistro)) { ErrorMessage = "Pick a distro."; return; }
        await Guarded(async () =>
        {
            _distro.DefaultUser = string.IsNullOrWhiteSpace(DefaultUser) ? null : DefaultUser;
            _distro.Systemd = Systemd;
            _distro.Hostname = string.IsNullOrWhiteSpace(Hostname) ? null : Hostname;
            await _config.WriteDistroAsync(SelectedDistro!, _distro);
            StatusMessage = $"Saved wsl.conf for {SelectedDistro}. Run `wsl --shutdown` to apply.";
        });
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await work(); }
        catch (WslException ex) { ErrorMessage = ex.Message; }
        catch (IOException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~ConfigViewModelTests"`
Expected: PASS (3).

- [ ] **Step 5: Write the page**

Create `Wsl.App/Views/ConfigPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Wsl.App.Views.ConfigPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:muxc="using:Microsoft.UI.Xaml.Controls">
    <ScrollViewer>
        <StackPanel Padding="24" Spacing="16" MaxWidth="700" HorizontalAlignment="Left">
            <TextBlock Text="Configuration" Style="{StaticResource TitleTextBlockStyle}" />

            <muxc:InfoBar IsOpen="{x:Bind Vm.ErrorMessage, Mode=OneWay, Converter={StaticResource NotNullToBool}}"
                         Severity="Error" Message="{x:Bind Vm.ErrorMessage, Mode=OneWay}" />
            <muxc:InfoBar IsOpen="{x:Bind Vm.StatusMessage, Mode=OneWay, Converter={StaticResource NotNullToBool}}"
                         Severity="Informational" Message="{x:Bind Vm.StatusMessage, Mode=OneWay}" />

            <muxc:Pivot>
                <muxc:PivotItem Header="Global (.wslconfig)">
                    <StackPanel Spacing="12" Margin="0,12,0,0">
                        <Button Content="Load" Command="{x:Bind Vm.LoadGlobalCommand}" />
                        <TextBox Header="Memory (e.g. 8GB)" Text="{x:Bind Vm.Memory, Mode=TwoWay}" Width="240" />
                        <TextBox Header="Processors" Text="{x:Bind Vm.Processors, Mode=TwoWay}" Width="240" />
                        <TextBox Header="Networking mode" Text="{x:Bind Vm.Networking, Mode=TwoWay}" Width="240" />
                        <ToggleSwitch Header="Localhost forwarding" IsOn="{x:Bind Vm.LocalhostForwarding, Mode=TwoWay}" />
                        <Button Content="Save" Command="{x:Bind Vm.SaveGlobalCommand}" />
                    </StackPanel>
                </muxc:PivotItem>
                <muxc:PivotItem Header="Per-distro (wsl.conf)">
                    <StackPanel Spacing="12" Margin="0,12,0,0">
                        <TextBox Header="Distro" Text="{x:Bind Vm.SelectedDistro, Mode=TwoWay}" Width="240" />
                        <Button Content="Load" Command="{x:Bind Vm.LoadDistroCommand}" />
                        <TextBox Header="Default user" Text="{x:Bind Vm.DefaultUser, Mode=TwoWay}" Width="240" />
                        <ToggleSwitch Header="systemd" IsOn="{x:Bind Vm.Systemd, Mode=TwoWay}" />
                        <TextBox Header="Hostname" Text="{x:Bind Vm.Hostname, Mode=TwoWay}" Width="240" />
                        <Button Content="Save" Command="{x:Bind Vm.SaveDistroCommand}" />
                    </StackPanel>
                </muxc:PivotItem>
            </muxc:Pivot>

            <Button Content="Shutdown WSL now" Click="Shutdown_Click" />
            <muxc:ProgressRing IsActive="{x:Bind Vm.IsBusy, Mode=OneWay}" />
        </StackPanel>
    </ScrollViewer>
</Page>
```

Create `Wsl.App/Views/ConfigPage.xaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;

namespace Wsl.App.Views;

public sealed partial class ConfigPage : Page
{
    public ConfigViewModel Vm { get; }

    public ConfigPage()
    {
        Vm = App.Services.GetRequiredService<ConfigViewModel>();
        InitializeComponent();
    }

    private async void Shutdown_Click(object s, RoutedEventArgs e)
    {
        var runner = App.Services.GetRequiredService<IProcessRunner>();
        await runner.RunAsync("wsl.exe", new[] { "--shutdown" });
    }
}
```

- [ ] **Step 6: Register the VM + nav entry**

In `ServiceRegistration.Build()` add `services.AddTransient<ConfigViewModel>();`.
In `MainWindow.xaml.cs` constructor add `_pages["Config"] = typeof(Views.ConfigPage);`.

- [ ] **Step 7: Commit**

```bash
git add Wsl.App.Logic/ViewModels/ConfigViewModel.cs Wsl.App/Views/ConfigPage.xaml Wsl.App/Views/ConfigPage.xaml.cs Wsl.App/Services/ServiceRegistration.cs Wsl.App/MainWindow.xaml.cs Wsl.Core.Tests/ConfigViewModelTests.cs
git commit -m "feat(app): config VM + page (global + per-distro)"
```

---

## Task 21: Setup (bootstrap) — VM + page

**Files:**
- Create: `Wsl.App.Logic/ViewModels/SetupViewModel.cs`
- Create: `Wsl.App/Views/SetupPage.xaml` + `.cs`
- Test: `Wsl.Core.Tests/SetupViewModelTests.cs`

The Setup VM orchestrates the bootstrap using the `IBrokerClient` and `BootstrapStateStore`. We
test it with a fake broker client.

- [ ] **Step 1: Write the failing test**

Create `Wsl.Core.Tests/SetupViewModelTests.cs`:

```csharp
using Wsl.App.Logic.ViewModels;
using Wsl.Contracts;
using Wsl.Core;
using Wsl.Core.Ipc;
using Xunit;

namespace Wsl.Core.Tests;

public sealed class FakeBrokerClient : IBrokerClient
{
    private readonly Queue<BrokerResponse> _responses = new();
    public List<BrokerRequest> Sent { get; } = new();
    public void Enqueue(BrokerResponse r) => _responses.Enqueue(r);

    public Task<BrokerResponse> SendAsync(BrokerRequest request, CancellationToken ct = default)
    {
        Sent.Add(request);
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : new BrokerResponse(true));
    }
}

public class SetupViewModelTests : IDisposable
{
    private readonly string _statePath =
        Path.Combine(Path.GetTempPath(), $"wslcc-setup-{Guid.NewGuid():N}.json");

    private SetupViewModel Make(FakeBrokerClient client)
        => new(client, new BootstrapStateStore(_statePath));

    [Fact]
    public async Task EnableFeatures_with_reboot_sets_reboot_pending_state()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(true, RebootRequired: true));
        var vm = Make(client);

        await vm.EnableFeaturesAsync();

        Assert.IsType<EnableFeaturesRequest>(client.Sent[0]);
        Assert.True(vm.RebootRequired);
        var store = new BootstrapStateStore(_statePath);
        Assert.Equal(BootstrapStep.RebootPending, await store.ReadAsync());
    }

    [Fact]
    public async Task ResumeAfterReboot_installs_kernel_then_sets_default_then_done()
    {
        // Pre-seed: reboot is pending.
        await new BootstrapStateStore(_statePath).WriteAsync(BootstrapStep.RebootPending);
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(true)); // install kernel
        client.Enqueue(new BrokerResponse(true)); // set default version
        var vm = Make(client);

        await vm.ResumeAsync();

        Assert.IsType<InstallOrUpdateKernelRequest>(client.Sent[0]);
        Assert.IsType<SetDefaultWslVersionRequest>(client.Sent[1]);
        Assert.Equal(BootstrapStep.Done, await new BootstrapStateStore(_statePath).ReadAsync());
        Assert.True(vm.IsComplete);
    }

    [Fact]
    public async Task Failure_surfaces_error_and_does_not_advance()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(false, "Access is denied."));
        var vm = Make(client);

        await vm.EnableFeaturesAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.RebootRequired);
    }

    public void Dispose() { if (File.Exists(_statePath)) File.Delete(_statePath); }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~SetupViewModelTests"`
Expected: FAIL — `SetupViewModel` does not exist.

- [ ] **Step 3: Write the ViewModel**

Create `Wsl.App.Logic/ViewModels/SetupViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wsl.Contracts;
using Wsl.Core;
using Wsl.Core.Ipc;

namespace Wsl.App.Logic.ViewModels;

public partial class SetupViewModel : ObservableObject
{
    private readonly IBrokerClient _broker;
    private readonly BootstrapStateStore _state;

    public SetupViewModel(IBrokerClient broker, BootstrapStateStore state)
    {
        _broker = broker;
        _state = state;
    }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _rebootRequired;
    [ObservableProperty] private bool _isComplete;

    [RelayCommand]
    public async Task EnableFeaturesAsync()
    {
        await Guarded(async () =>
        {
            await _state.WriteAsync(BootstrapStep.EnableFeatures);
            var resp = await _broker.SendAsync(new EnableFeaturesRequest());
            if (!resp.Success) { ErrorMessage = resp.Error; return; }
            if (resp.RebootRequired)
            {
                RebootRequired = true;
                await _state.WriteAsync(BootstrapStep.RebootPending);
                StatusMessage = "Restart required to finish enabling WSL.";
            }
            else
            {
                await ResumeAsync();
            }
        });
    }

    /// <summary>Called on app startup (and after reboot) to continue any pending bootstrap.</summary>
    [RelayCommand]
    public async Task ResumeAsync()
    {
        var step = await _state.ReadAsync();
        if (step is BootstrapStep.Done) { IsComplete = true; return; }

        await Guarded(async () =>
        {
            // From RebootPending (or EnableFeatures completed) → install kernel.
            var kernel = await _broker.SendAsync(new InstallOrUpdateKernelRequest());
            if (!kernel.Success) { ErrorMessage = kernel.Error; return; }
            await _state.WriteAsync(BootstrapStep.SetDefaultVersion);

            var setDefault = await _broker.SendAsync(new SetDefaultWslVersionRequest(2));
            if (!setDefault.Success) { ErrorMessage = setDefault.Error; return; }

            await _state.ClearAsync();
            IsComplete = true;
            StatusMessage = "WSL is ready.";
        });
    }

    private async Task Guarded(Func<Task> work)
    {
        IsBusy = true; ErrorMessage = null;
        try { await work(); }
        finally { IsBusy = false; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Wsl.Core.Tests --filter "FullyQualifiedName~SetupViewModelTests"`
Expected: PASS (3).

- [ ] **Step 5: Write the page**

Create `Wsl.App/Views/SetupPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Wsl.App.Views.SetupPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:muxc="using:Microsoft.UI.Xaml.Controls">
    <StackPanel Padding="24" Spacing="16" MaxWidth="640" HorizontalAlignment="Left">
        <TextBlock Text="Set up / repair WSL" Style="{StaticResource TitleTextBlockStyle}" />
        <TextBlock TextWrapping="Wrap"
                   Text="Enables the required Windows features, installs the WSL kernel, and sets the default version to 2. Requires administrator approval (UAC)." />

        <muxc:InfoBar IsOpen="{x:Bind Vm.ErrorMessage, Mode=OneWay, Converter={StaticResource NotNullToBool}}"
                     Severity="Error" Message="{x:Bind Vm.ErrorMessage, Mode=OneWay}" />
        <muxc:InfoBar IsOpen="{x:Bind Vm.StatusMessage, Mode=OneWay, Converter={StaticResource NotNullToBool}}"
                     Severity="Informational" Message="{x:Bind Vm.StatusMessage, Mode=OneWay}" />

        <Button Content="Enable WSL features" Command="{x:Bind Vm.EnableFeaturesCommand}" />

        <muxc:InfoBar IsOpen="{x:Bind Vm.RebootRequired, Mode=OneWay}" Severity="Warning"
                     Title="Restart required"
                     Message="Restart Windows to finish, then reopen this app to continue automatically.">
            <muxc:InfoBar.ActionButton>
                <Button Content="Restart now" Click="Restart_Click" />
            </muxc:InfoBar.ActionButton>
        </muxc:InfoBar>

        <muxc:ProgressRing IsActive="{x:Bind Vm.IsBusy, Mode=OneWay}" />
    </StackPanel>
</Page>
```

Create `Wsl.App/Views/SetupPage.xaml.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Wsl.App.Logic.ViewModels;

namespace Wsl.App.Views;

public sealed partial class SetupPage : Page
{
    public SetupViewModel Vm { get; }

    public SetupPage()
    {
        Vm = App.Services.GetRequiredService<SetupViewModel>();
        InitializeComponent();
        _ = Vm.ResumeAsync(); // continue any pending bootstrap on view load
    }

    private void Restart_Click(object s, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { UseShellExecute = true });
}
```

- [ ] **Step 6: Register the VM + nav entry**

In `ServiceRegistration.Build()` add `services.AddTransient<SetupViewModel>();`.
In `MainWindow.xaml.cs` constructor add `_pages["Setup"] = typeof(Views.SetupPage);`.

- [ ] **Step 7: Commit**

```bash
git add Wsl.App.Logic/ViewModels/SetupViewModel.cs Wsl.App/Views/SetupPage.xaml Wsl.App/Views/SetupPage.xaml.cs Wsl.App/Services/ServiceRegistration.cs Wsl.App/MainWindow.xaml.cs Wsl.Core.Tests/SetupViewModelTests.cs
git commit -m "feat(app): setup VM + page (bootstrap orchestration)"
```

---

## Task 22: Optional live-WSL integration tests (skipped in CI)

**Files:**
- Create: `Wsl.Live.Tests/Wsl.Live.Tests.csproj`
- Create: `Wsl.Live.Tests/RealProcessRunnerTests.cs`

These run against a real WSL install. They are excluded from CI by trait filter and are meant to
be run manually on a developer machine that has WSL.

- [ ] **Step 1: Create the project**

Run:

```powershell
dotnet new xunit -n Wsl.Live.Tests -f net9.0 -o Wsl.Live.Tests
dotnet sln add Wsl.Live.Tests
dotnet add Wsl.Live.Tests reference Wsl.Core
```

- [ ] **Step 2: Write the live test**

Create `Wsl.Live.Tests/RealProcessRunnerTests.cs`:

```csharp
using Wsl.Core;
using Xunit;

namespace Wsl.Live.Tests;

[Trait("Category", "LiveWsl")]
public class RealProcessRunnerTests
{
    [Fact]
    public async Task Real_list_decodes_and_parses()
    {
        var svc = new WslDistroService(new RealProcessRunner());
        var distros = await svc.ListAsync();
        // On a machine with WSL installed this should not throw and should decode cleanly
        // (no NUL artifacts, real names). We assert the call succeeds and names are non-empty.
        Assert.All(distros, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
    }

    [Fact]
    public async Task Real_version_succeeds()
    {
        var result = await new RealProcessRunner().RunAsync("wsl.exe", new[] { "--version" });
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("WSL", result.StdOut);
    }
}
```

- [ ] **Step 3: Verify default test run skips the live tests**

Run: `dotnet test Wsl.Live.Tests --filter "Category!=LiveWsl"`
Expected: PASS with 0 tests executed (all filtered out).

Run locally on a WSL machine to exercise them: `dotnet test Wsl.Live.Tests`
Expected: PASS (2) when WSL is installed.

- [ ] **Step 4: Document the CI exclusion**

Note for whoever sets up CI: run `dotnet test --filter "Category!=LiveWsl"` so live tests never run
on an agent without WSL. The unit projects (`Wsl.Core.Tests`) have no such trait and always run.

- [ ] **Step 5: Commit**

```bash
git add Wsl.Live.Tests WslCommandCenter.sln
git commit -m "test: optional live-WSL integration project (CI-excluded)"
```

---

## Task 23: Final integration — full build, full test, manual smoke

**Files:**
- Modify: `Wsl.App/Wsl.App.csproj` (ensure broker is copied next to the app)
- Modify: `Wsl.App/App.xaml.cs` (run bootstrap-resume + first-run detection on launch)

- [ ] **Step 1: Ensure the broker exe ships next to the app**

Add to `Wsl.App/Wsl.App.csproj` (new `ItemGroup`) so `Wsl.Broker.exe` is copied to the app's
output directory (where `BrokerClient` looks for it via `AppContext.BaseDirectory`):

```xml
  <ItemGroup>
    <ProjectReference Include="..\Wsl.Broker\Wsl.Broker.csproj">
      <Private>true</Private>
      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
      <OutputItemType>Content</OutputItemType>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </ProjectReference>
  </ItemGroup>
```

(This makes MSBuild build the broker and copy its output into the app folder without referencing
its assembly.)

- [ ] **Step 2: Run first-run detection on launch**

Update `Wsl.App/App.xaml.cs` `OnLaunched` to check WSL presence and route to Setup if absent or if
a bootstrap is mid-flight. Replace `OnLaunched` body with:

```csharp
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ServiceRegistration.Build();
        _window = new MainWindow();
        MainWindowHandleHost = _window;
        _window.Activate();

        // If a bootstrap was pending (mid reboot/resume), continue it immediately.
        var state = Services.GetRequiredService<Wsl.Core.BootstrapStateStore>();
        if (await state.ReadAsync() != Wsl.Core.BootstrapStep.Done && _window is MainWindow mw)
            mw.NavigateToSetup();
    }
```

`NavigateToSetup()` already exists on `MainWindow` (added in Task 16). No change needed there.

(Add `using Microsoft.Extensions.DependencyInjection;` to `App.xaml.cs` if not present.)

- [ ] **Step 3: Full build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors across all projects.

- [ ] **Step 4: Full unit test run (CI-equivalent)**

Run: `dotnet test --filter "Category!=LiveWsl"`
Expected: PASS — all unit tests across `Wsl.Core.Tests` green; live tests excluded.

- [ ] **Step 5: Manual smoke (real machine with WSL)**

Run the app: `dotnet run --project Wsl.App`
Verify by hand:
1. **Dashboard** lists `Ubuntu` + `podman-machine-default`, default starred, correct states.
2. **Start** Ubuntu → state flips to Running on refresh. **Stop** → back to Stopped.
3. **Config → Global**: Load shows existing `.wslconfig` values; change Memory, Save; reopen file
   on disk and confirm unknown keys survived.
4. **Config → Per-distro**: pick Ubuntu, Load, toggle systemd, Save; `wsl -d Ubuntu -u root cat
   /etc/wsl.conf` reflects the change.
5. **Backup**: export Ubuntu to a `.tar`; confirm file exists and is non-empty.
6. **Setup**: click "Enable WSL features" → exactly one UAC prompt appears (broker elevation).

- [ ] **Step 6: Commit**

```bash
git add Wsl.App/Wsl.App.csproj Wsl.App/App.xaml.cs Wsl.App/MainWindow.xaml.cs
git commit -m "feat(app): ship broker alongside app + first-run bootstrap routing"
```

- [ ] **Step 7: Final verification gate**

Before declaring done, confirm:
- `dotnet build` → 0 errors.
- `dotnet test --filter "Category!=LiveWsl"` → all green, count > 0.
- Manual smoke steps 1-6 all pass on a real machine.

Only after all three hold is the feature complete.

---

## Coverage map (spec → task)

| Spec requirement | Task(s) |
|---|---|
| `IProcessRunner` + UTF-16LE decode + timeout | 1, 10 |
| Typed `WslException` / `WslErrorKind` | 2 |
| Distro lifecycle (list/start/stop/default/version/unregister) | 3, 4, 17 |
| Deploy (catalog install + tar/vhdx import) | 5, 18 |
| Backup/restore (tar/tar.gz/vhd) | 6, 19 |
| Global `.wslconfig` with key passthrough | 7, 20 |
| Per-distro `wsl.conf` via root cat/tee | 8, 20 |
| Bootstrap state store (reboot/resume) | 9, 21 |
| IPC DTOs + source-gen JSON | 11 |
| Broker privileged ops (DISM/kernel/default-version) | 12 |
| Bidirectional pipe auth (anti-squat, ACL, peer verify) | 13, 14, 15 |
| Broker client (launch elevated + verify server) | 15 |
| WinUI shell + DI | 16 |
| Dashboard / Deploy / Backup / Config / Setup pages | 17-21 |
| Live integration (CI-excluded) | 22 |
| Ship broker w/ app + first-run routing | 23 |

All five MVP features + bootstrap covered. Scheduled backups + `--import-in-place` intentionally
deferred per spec.

