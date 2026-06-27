using Wsl.Core;
using Wsl.Core.Monitoring;
using Xunit;

namespace Wsl.Core.Tests;

public class WslMonitorServiceTests
{
    private const string Combined =
        "MemTotal:        8000000 kB\n" +
        "MemAvailable:    2000000 kB\n" +
        "---\n" +
        "cpu  100 0 100 800 0 0 0 0 0 0\n" +
        "---\n" +
        "Filesystem 1B-blocks Used Available Use% Mounted\n" +
        "/dev/sdc 1073741824 268435456 805306368 25% /\n";

    [Fact]
    public void ParseDistro_ComputesMemDiskAndCpuDelta()
    {
        // First sample: no prev -> CPU 0, establishes baseline.
        var first = WslMonitorService.ParseDistro("Ubuntu", Combined, prev: null);
        Assert.Equal(8000000L * 1024, first.MemTotalBytes);
        Assert.Equal((8000000L - 2000000L) * 1024, first.MemUsedBytes);
        Assert.Equal(268435456L, first.DiskUsedBytes);
        Assert.Equal(1073741824L, first.DiskTotalBytes);
        Assert.Equal(0, first.CpuPercent);
    }

    [Fact]
    public async Task SampleAsync_ReturnsVmHeaderAndPerDistroRows()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, Combined); // one combined call for Ubuntu
        var svc = new WslMonitorService(
            runner,
            new FakeVmProbe(cpu: 12.5, ws: 4L * 1024 * 1024 * 1024),
            new FakeVhdxProbe(total: 20L * 1024 * 1024 * 1024));

        var snap = await svc.SampleAsync(new[] { "Ubuntu" });

        Assert.Equal(12.5, snap.Vm.CpuPercent);
        Assert.Equal(20L * 1024 * 1024 * 1024, snap.Vm.DiskBytes);
        Assert.Single(snap.Distros);
        Assert.Equal("Ubuntu", snap.Distros[0].Name);
    }

    private sealed class FakeVmProbe(double cpu, long ws) : IVmProcessProbe
    { public (double, long) Read() => (cpu, ws); }
    private sealed class FakeVhdxProbe(long total) : IVhdxSizeProbe
    { public long TotalBytes() => total; }
}
