using Wsl.Core;

namespace Wsl.Core.Tests;

/// <summary>
/// Regression guard for a defect found by cloning a real 20 GB distro through the GUI: every one
/// of these operations passed no timeout, so <c>RealProcessRunner</c>'s 60-second default applied
/// and the operation died with "wsl.exe timed out". That silently broke export, restore, snapshots,
/// clone and scheduled backup for any distro large enough to take over a minute — i.e. all of them.
///
/// Unit tests could not have caught it before: the fake runner did not record the timeout argument.
/// These assert the argument explicitly, so dropping back to the default fails the build.
/// </summary>
public class LongRunningTimeoutTests
{
    private static void AssertNoTimeout(FakeProcessRunner runner, int callIndex, string what)
    {
        var actual = runner.AllTimeouts[callIndex];
        Assert.True(actual == Timeout.InfiniteTimeSpan,
            $"{what} must run without a timeout (got {(actual?.ToString() ?? "null → 60s default")}). "
            + "A multi-GB WSL operation cannot complete inside the default and must not be killed midway.");
    }

    [Fact]
    public async Task Export_runs_without_a_timeout()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        await new WslBackupService(runner).ExportAsync("Ubuntu", @"C:\out.tar", ExportFormat.Tar);

        Assert.Equal("--export", runner.AllArgs[0][0]);
        AssertNoTimeout(runner, 0, "wsl --export");
    }

    [Fact]
    public async Task Restore_runs_without_a_timeout()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        await new WslBackupService(runner)
            .RestoreAsync("Clone", @"C:\dir", @"C:\out.tar", ExportFormat.Tar, 2);

        Assert.Equal("--import", runner.AllArgs[0][0]);
        AssertNoTimeout(runner, 0, "wsl --import");
    }

    [Fact]
    public async Task Optimize_set_sparse_runs_without_a_timeout()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");   // --terminate
        runner.Enqueue(0, "");   // --manage --set-sparse
        await new WslDiskService(runner).OptimizeAsync("Ubuntu");

        Assert.Contains("--set-sparse", runner.AllArgs[1]);
        AssertNoTimeout(runner, 1, "wsl --manage --set-sparse");
    }

    [Fact]
    public async Task Trim_runs_without_a_timeout()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "/: 1 GiB trimmed");
        await new WslDiskService(runner).TrimAsync("Ubuntu");

        Assert.Contains("fstrim", runner.AllArgs[0]);
        AssertNoTimeout(runner, 0, "fstrim");
    }

    [Fact]
    public async Task Wsl_update_runs_without_a_timeout()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        await new WslSystemService(runner).UpdateAsync();

        Assert.Equal("--update", runner.AllArgs[0][0]);
        AssertNoTimeout(runner, 0, "wsl --update");
    }
}
