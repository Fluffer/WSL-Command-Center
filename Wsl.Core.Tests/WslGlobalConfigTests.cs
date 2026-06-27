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
        Assert.Equal("keepme", ini["wsl2"]["customUnknownKey"]);
    }

    [Fact]
    public void RoundTrips_NewWsl2AndExperimentalKeys()
    {
        var ini = IniParser.Parse(
            "[wsl2]\n" +
            "guiApplications=false\n" +
            "vmIdleTimeout=30000\n" +
            "defaultVhdSize=256GB\n" +
            "firewall=false\n" +
            "dnsTunneling=true\n" +
            "dnsProxy=false\n" +
            "autoProxy=true\n" +
            "kernelCommandLine=vsyscall=emulate\n" +
            "safeMode=true\n" +
            "debugConsole=true\n" +
            "maxCrashDumpCount=5\n" +
            "kernel=C:\\\\k\\\\bzImage\n" +
            "kernelModules=C:\\\\k\\\\modules.vhdx\n" +
            "[experimental]\n" +
            "autoMemoryReclaim=gradual\n" +
            "sparseVhd=true\n" +
            "ignoredPorts=53,3000\n" +
            "hostAddressLoopback=true\n");

        var cfg = WslGlobalConfig.FromIni(ini);

        Assert.Equal(false, cfg.GuiApplications);
        Assert.Equal(30000, cfg.VmIdleTimeout);
        Assert.Equal("256GB", cfg.DefaultVhdSize);
        Assert.Equal("vsyscall=emulate", cfg.KernelCommandLine);
        Assert.Equal("gradual", cfg.AutoMemoryReclaim);
        Assert.Equal("53,3000", cfg.IgnoredPorts);
        Assert.True(cfg.SparseVhd);
        Assert.True(cfg.HostAddressLoopback);

        var back = cfg.ToIni();
        Assert.Equal("false", back["wsl2"]["guiApplications"]);
        Assert.Equal("30000", back["wsl2"]["vmIdleTimeout"]);
        Assert.Equal("gradual", back["experimental"]["autoMemoryReclaim"]);
        Assert.Equal("53,3000", back["experimental"]["ignoredPorts"]);
    }
}
