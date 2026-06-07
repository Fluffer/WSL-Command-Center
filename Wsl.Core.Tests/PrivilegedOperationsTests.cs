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
    public async Task InstallKernel_with_prerelease_runs_update_pre_release()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Installing: Windows Subsystem for Linux");
        var ops = new PrivilegedOperations(runner);

        var resp = await ops.HandleAsync(new InstallOrUpdateKernelRequest(PreRelease: true));

        Assert.True(resp.Success);
        Assert.Equal(new[] { "--update", "--pre-release" }, runner.LastArgs);
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
