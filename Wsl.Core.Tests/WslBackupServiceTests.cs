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
