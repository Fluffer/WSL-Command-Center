using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslProvisioningServiceTests
{
    private static DistroTemplate TwoStep() => new(
        "t", "Two step", "desc",
        new[]
        {
            new ProvisioningStep("first", "echo one"),
            new ProvisioningStep("second", "echo two"),
        });

    [Fact]
    public async Task ApplyTemplate_runs_each_step_as_root_bash()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "ok1");
        runner.Enqueue(0, "ok2");
        var svc = new WslProvisioningService(runner, new WslBackupService(runner));

        var results = await svc.ApplyTemplateAsync("Ubuntu", TwoStep());

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Success);
        Assert.True(results[1].Success);
        Assert.Equal(
            new[] { "-d", "Ubuntu", "-u", "root", "--", "bash", "-lc", "echo one" },
            runner.AllArgs[0]);
        Assert.Equal(
            new[] { "-d", "Ubuntu", "-u", "root", "--", "bash", "-lc", "echo two" },
            runner.AllArgs[1]);
    }

    [Fact]
    public async Task ApplyTemplate_stops_on_first_failure()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "boom");   // first step fails
        var svc = new WslProvisioningService(runner, new WslBackupService(runner));

        var results = await svc.ApplyTemplateAsync("Ubuntu", TwoStep());

        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Contains("boom", results[0].Output);
        Assert.Single(runner.AllArgs);   // second step never ran
    }

    private sealed class SyncProgress : IProgress<StepResult>
    {
        public List<StepResult> Items { get; } = new();
        public void Report(StepResult value) => Items.Add(value);
    }

    [Fact]
    public async Task ApplyTemplate_reports_progress_per_step()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "ok1");
        runner.Enqueue(0, "ok2");
        var svc = new WslProvisioningService(runner, new WslBackupService(runner));
        var progress = new SyncProgress();

        await svc.ApplyTemplateAsync("Ubuntu", TwoStep(), progress);

        Assert.Equal(2, progress.Items.Count);
        Assert.Equal("first", progress.Items[0].Description);
        Assert.Equal("second", progress.Items[1].Description);
    }

    [Fact]
    public async Task Clone_exports_then_imports_under_new_name()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");   // export
        runner.Enqueue(0, "");   // import
        var svc = new WslProvisioningService(runner, new WslBackupService(runner));

        await svc.CloneAsync("Ubuntu", "Ubuntu-clone", @"C:\wsl\clone");

        // export: --export Ubuntu <temp.tar> --format tar
        Assert.Equal("--export", runner.AllArgs[0][0]);
        Assert.Equal("Ubuntu", runner.AllArgs[0][1]);
        Assert.EndsWith(".tar", runner.AllArgs[0][2]);
        // import: --import Ubuntu-clone <dir> <temp.tar> --version 2
        Assert.Equal("--import", runner.AllArgs[1][0]);
        Assert.Equal("Ubuntu-clone", runner.AllArgs[1][1]);
        Assert.Equal(@"C:\wsl\clone", runner.AllArgs[1][2]);
        Assert.Equal(runner.AllArgs[0][2], runner.AllArgs[1][3]); // same temp archive
    }

    [Fact]
    public async Task Clone_unregisters_partial_distro_when_import_fails()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");          // export ok
        runner.Enqueue(1, "", "fail");  // import fails -> RestoreAsync throws
        var svc = new WslProvisioningService(runner, new WslBackupService(runner));

        await Assert.ThrowsAsync<WslException>(() =>
            svc.CloneAsync("Ubuntu", "Ubuntu-clone", @"C:\wsl\clone"));

        // last call must be the cleanup unregister of the half-imported distro
        Assert.Equal(new[] { "--unregister", "Ubuntu-clone" }, runner.LastArgs);
    }

    [Theory]
    [InlineData("", "new", @"C:\dir")]
    [InlineData("src", "", @"C:\dir")]
    [InlineData("src", "new", "")]
    public async Task Clone_rejects_blank_arguments(string source, string name, string dir)
    {
        var runner = new FakeProcessRunner();
        var svc = new WslProvisioningService(runner, new WslBackupService(runner));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CloneAsync(source, name, dir));
    }

    [Fact]
    public void TemplateCatalog_ships_builtins_with_unique_ids()
    {
        var ids = TemplateCatalog.BuiltIn.Select(t => t.Id).ToList();
        Assert.NotEmpty(TemplateCatalog.BuiltIn);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(TemplateCatalog.BuiltIn, t => Assert.NotEmpty(t.Steps));
    }
}
