using Wsl.Core.Diagnostics;
using Xunit;

namespace Wsl.Core.Tests;

public class WslGpuServiceTests
{
    [Fact]
    public void ParseNvidiaSmi_ReadsCsvRow()
    {
        var g = WslGpuService.ParseNvidiaSmi(dxg: true, exitCode: 0,
            csv: "NVIDIA GeForce RTX 4090, 560.94, 1024, 24564\n");
        Assert.True(g.DxgPresent);
        Assert.True(g.NvidiaDetected);
        Assert.Equal("NVIDIA GeForce RTX 4090", g.Name);
        Assert.Equal("560.94", g.DriverVersion);
        Assert.Equal(1024, g.MemUsedMb);
        Assert.Equal(24564, g.MemTotalMb);
    }

    [Fact]
    public void ParseNvidiaSmi_NonZeroExit_MeansNoNvidia()
    {
        var g = WslGpuService.ParseNvidiaSmi(dxg: true, exitCode: 127, csv: "");
        Assert.True(g.DxgPresent);
        Assert.False(g.NvidiaDetected);
        Assert.Null(g.Name);
    }
}
