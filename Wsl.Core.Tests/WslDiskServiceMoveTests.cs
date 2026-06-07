using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

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
