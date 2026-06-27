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
        "cpu  100 0 100 800 0 0 0 0 0\n" +
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

    // Bug 1: malformed cpu line (< idle field) must not produce ~100%
    [Fact]
    public void ParseStat_MalformedCpuLine_DoesNotYieldHundredPercent()
    {
        // "cpu 100 0 100" has only 3 fields — idle column absent
        const string combined =
            "MemTotal: 1000 kB\nMemAvailable: 500 kB\n" +
            "---\n" +
            "cpu 100 0 100\n" +
            "---\n" +
            "Filesystem 1B-blocks Used Available Use% Mounted on\n" +
            "/dev/sdc 1000 100 900 10% /\n";
        // prev with total=0 so that if ParseStat returns (0,0) the condition cur.Total>prev.Total is false
        // but before fix ParseStat returns (200,200) so condition passes and cpuPct=100
        var prev = new CpuSample(Busy: 0, Total: 0);
        var m = WslMonitorService.ParseDistro("Test", combined, prev);
        Assert.Equal(0, m.CpuPercent);
    }

    // Bug 2: iowait must not be counted as busy time
    [Fact]
    public void ParseDistro_IowaitExcludedFromBusy()
    {
        // cpu 100 0 100 700 100 0 0 0 0 0
        // total=1000, idle=700, iowait=100 → busy=200, pct=200/1000*100=20.0
        // without fix: busy=300 (iowait counted), pct=30.0
        const string combined =
            "MemTotal: 1000 kB\nMemAvailable: 500 kB\n" +
            "---\n" +
            "cpu 100 0 100 700 100 0 0 0 0 0\n" +
            "---\n" +
            "Filesystem 1B-blocks Used Available Use% Mounted on\n" +
            "/dev/sdc 1000 100 900 10% /\n";
        var prev = new CpuSample(Busy: 0, Total: 0);
        var m = WslMonitorService.ParseDistro("Test", combined, prev);
        Assert.Equal(20.0, m.CpuPercent);
    }

    // Bug 3: negative CPU delta (counter reset) must clamp to 0
    [Fact]
    public void ParseDistro_NegativeDeltaClampsToZero()
    {
        // cpu 100 0 100 700 0 0 0 0 0 0 → total=900, idle=700, busy=200
        const string combined =
            "MemTotal: 1000 kB\nMemAvailable: 500 kB\n" +
            "---\n" +
            "cpu 100 0 100 700 0 0 0 0 0 0\n" +
            "---\n" +
            "Filesystem 1B-blocks Used Available Use% Mounted on\n" +
            "/dev/sdc 1000 100 900 10% /\n";
        // prev has higher Busy (500) than cur Busy (200); cur.Total(900)>prev.Total(800) so check passes
        var prev = new CpuSample(Busy: 500, Total: 800);
        var m = WslMonitorService.ParseDistro("Test", combined, prev);
        Assert.Equal(0, m.CpuPercent);
        Assert.True(m.CpuPercent >= 0, "CPU% must not be negative after counter reset");
    }

    // Bug 4: overlay/tmpfs device (no leading '/') must parse correctly
    [Fact]
    public void ParseDf_OverlayRootFsDetected()
    {
        const string combined =
            "MemTotal: 1000 kB\nMemAvailable: 500 kB\n" +
            "---\n" +
            "cpu 100 0 100 800 0 0 0 0 0\n" +
            "---\n" +
            "Filesystem 1B-blocks Used Available Use% Mounted on\n" +
            "overlay 1073741824 268435456 805306368 25% /\n";
        var m = WslMonitorService.ParseDistro("Test", combined, null);
        Assert.Equal(1073741824L, m.DiskTotalBytes);
        Assert.Equal(268435456L, m.DiskUsedBytes);
    }

    private sealed class FakeVmProbe(double cpu, long ws) : IVmProcessProbe
    { public (double, long) Read() => (cpu, ws); }
    private sealed class FakeVhdxProbe(long total) : IVhdxSizeProbe
    { public long TotalBytes() => total; }
}
