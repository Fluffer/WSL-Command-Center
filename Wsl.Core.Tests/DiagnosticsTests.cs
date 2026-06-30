using System.IO;
using System.Linq;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Wsl.Core.Diagnostics;
using Xunit;

namespace Wsl.Core.Tests;

public class DiagnosticsTests
{
    private static string TempCfg(string body)
    {
        var p = Path.Combine(Path.GetTempPath(), "diagcfg_" + System.Guid.NewGuid().ToString("N") + ".wslconfig");
        File.WriteAllText(p, body);
        return p;
    }

    // ── WslDiagnosticsService aggregation + fault isolation ──────────────────

    private sealed class StubCheck : IDiagnosticCheck
    {
        private readonly DiagnosticResult? _result;
        private readonly bool _throws;
        public StubCheck(string id, DiagnosticResult? result, bool throws = false)
        { Id = id; _result = result; _throws = throws; }
        public string Id { get; }
        public Task<DiagnosticResult> RunAsync(CancellationToken ct = default)
            => _throws ? throw new System.InvalidOperationException("boom")
                       : Task.FromResult(_result!);
    }

    [Fact]
    public async Task RunAllAsync_AggregatesAllChecks_InOrder()
    {
        var svc = new WslDiagnosticsService(new IDiagnosticCheck[]
        {
            new StubCheck("a", new DiagnosticResult("a", "A", DiagnosticSeverity.Ok, "ok")),
            new StubCheck("b", new DiagnosticResult("b", "B", DiagnosticSeverity.Warning, "warn")),
        });

        var results = await svc.RunAllAsync();

        Assert.Equal(new[] { "a", "b" }, results.Select(r => r.Id));
    }

    [Fact]
    public async Task RunAllAsync_ThrowingCheck_DegradesToWarning_DoesNotAbortRun()
    {
        var svc = new WslDiagnosticsService(new IDiagnosticCheck[]
        {
            new StubCheck("bad", null, throws: true),
            new StubCheck("good", new DiagnosticResult("good", "Good", DiagnosticSeverity.Ok, "ok")),
        });

        var results = await svc.RunAllAsync();

        Assert.Equal(DiagnosticSeverity.Warning, results[0].Severity);
        Assert.Equal("bad", results[0].Id);
        Assert.Equal(DiagnosticSeverity.Ok, results[1].Severity);   // run continued past the throw
    }

    // ── DiagnosticsViewModel orchestration ───────────────────────────────────

    [Fact]
    public async Task DiagnosticsViewModel_Run_PopulatesRowsAndCountsProblems()
    {
        var diag = new WslDiagnosticsService(new IDiagnosticCheck[]
        {
            new StubCheck("a", new DiagnosticResult("a", "A", DiagnosticSeverity.Ok, "ok")),
            new StubCheck("b", new DiagnosticResult("b", "B", DiagnosticSeverity.Warning, "warn")),
        });
        var runner = new FakeProcessRunner();
        var vm = new DiagnosticsViewModel(diag, new WslDistroService(runner), new WslSystemService(runner));

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Rows.Count);
        Assert.Contains("1 issue", vm.StatusMessage);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task DiagnosticsViewModel_ApplyRestartFix_CallsShutdown()
    {
        var diag = new WslDiagnosticsService(new IDiagnosticCheck[]
        {
            new StubCheck("a", new DiagnosticResult("a", "A", DiagnosticSeverity.Ok, "ok")),
        });
        var runner = new FakeProcessRunner();
        var vm = new DiagnosticsViewModel(diag, new WslDistroService(runner), new WslSystemService(runner));

        await vm.ApplyFixCommand.ExecuteAsync(
            new DiagnosticFix(WslDiagnosticsService.Fixes.RestartWsl, "Restart WSL", Destructive: true));

        Assert.Contains("--shutdown", runner.AllArgs.Last());
        Assert.Contains("Applied", vm.StatusMessage!);
    }

    // ── WslInstalledCheck ────────────────────────────────────────────────────

    [Fact]
    public async Task WslInstalledCheck_Ok_WhenVersionReported()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "WSL version: 2.7.10.0\nKernel version: 6.6.0\n");
        var r = await new WslInstalledCheck(runner).RunAsync();
        Assert.Equal(DiagnosticSeverity.Ok, r.Severity);
        Assert.Contains("2.7.10", r.Detail);
    }

    [Fact]
    public async Task WslInstalledCheck_Error_WhenNonZeroExit_AndOffersUpdateFix()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "not recognized");
        var r = await new WslInstalledCheck(runner).RunAsync();
        Assert.Equal(DiagnosticSeverity.Error, r.Severity);
        Assert.Equal(WslDiagnosticsService.Fixes.UpdateWsl, r.Fix!.Id);
    }

    // ── DistroHealthCheck ────────────────────────────────────────────────────

    [Fact]
    public async Task DistroHealthCheck_Ok_WhenAllVersion2AndReady()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n  Debian  Stopped  2\n");
        var r = await new DistroHealthCheck(new WslDistroService(runner)).RunAsync();
        Assert.Equal(DiagnosticSeverity.Ok, r.Severity);
    }

    [Fact]
    public async Task DistroHealthCheck_Info_WhenNoDistros()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME    STATE    VERSION\n");
        var r = await new DistroHealthCheck(new WslDistroService(runner)).RunAsync();
        Assert.Equal(DiagnosticSeverity.Info, r.Severity);
    }

    [Fact]
    public async Task DistroHealthCheck_Info_WhenWsl1Present()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME    STATE    VERSION\n* Legacy  Stopped  1\n");
        var r = await new DistroHealthCheck(new WslDistroService(runner)).RunAsync();
        Assert.Equal(DiagnosticSeverity.Info, r.Severity);
        Assert.Contains("WSL 1", r.Detail);
    }

    // ── DiskSpaceCheck ───────────────────────────────────────────────────────

    private sealed class FakeDriveProbe : ISystemDriveProbe
    {
        private readonly DriveSpace _s;
        public FakeDriveProbe(long freeGb) => _s = new DriveSpace(freeGb * 1024L * 1024 * 1024, 512L * 1024 * 1024 * 1024);
        public DriveSpace Get() => _s;
    }

    [Theory]
    [InlineData(1, DiagnosticSeverity.Error)]
    [InlineData(5, DiagnosticSeverity.Warning)]
    [InlineData(50, DiagnosticSeverity.Ok)]
    public async Task DiskSpaceCheck_SeverityByFreeSpace(long freeGb, DiagnosticSeverity expected)
    {
        var r = await new DiskSpaceCheck(new FakeDriveProbe(freeGb)).RunAsync();
        Assert.Equal(expected, r.Severity);
        Assert.Contains("GB free", r.Detail);
    }

    // ── MirroredFirewallCheck ────────────────────────────────────────────────

    [Fact]
    public async Task MirroredFirewallCheck_Info_WhenMirrored_WithFirewallGuidance()
    {
        var cfg = TempCfg("[wsl2]\nnetworkingMode=mirrored\n");
        try
        {
            var svc = new WslConfigService(new FakeProcessRunner(), () => cfg);
            var r = await new MirroredFirewallCheck(svc).RunAsync();
            Assert.Equal(DiagnosticSeverity.Info, r.Severity);
            Assert.Contains("firewall", r.Detail.ToLowerInvariant());
        }
        finally { File.Delete(cfg); }
    }

    [Fact]
    public async Task MirroredFirewallCheck_Ok_WhenNotMirrored()
    {
        var cfg = TempCfg("[wsl2]\nnetworkingMode=nat\n");
        try
        {
            var svc = new WslConfigService(new FakeProcessRunner(), () => cfg);
            var r = await new MirroredFirewallCheck(svc).RunAsync();
            Assert.Equal(DiagnosticSeverity.Ok, r.Severity);
        }
        finally { File.Delete(cfg); }
    }
}
