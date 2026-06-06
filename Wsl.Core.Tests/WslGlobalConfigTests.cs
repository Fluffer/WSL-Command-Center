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
}
