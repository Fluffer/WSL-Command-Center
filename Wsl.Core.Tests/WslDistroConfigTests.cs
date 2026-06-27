using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslDistroConfigTests
{
    private const string Conf =
        "[boot]\n" +
        "systemd=true\n" +
        "[user]\n" +
        "default=peter\n" +
        "[customsection]\n" +
        "keepme=yes\n";

    [Fact]
    public void Parses_typed_fields()
    {
        var cfg = WslDistroConfig.FromIni(IniParser.Parse(Conf));
        Assert.True(cfg.Systemd);
        Assert.Equal("peter", cfg.DefaultUser);
    }

    [Fact]
    public void Unknown_section_roundtrips()
    {
        var cfg = WslDistroConfig.FromIni(IniParser.Parse(Conf));
        var ini = cfg.ToIni();
        Assert.Equal("yes", ini["customsection"]["keepme"]);
    }

    [Fact]
    public async Task ReadDistro_uses_cat_as_root()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, Conf);
        var svc = new WslConfigService(runner);
        var cfg = await svc.ReadDistroAsync("Ubuntu");
        Assert.Equal(new[] { "-d", "Ubuntu", "-u", "root", "cat", "/etc/wsl.conf" }, runner.LastArgs);
        Assert.Equal("peter", cfg.DefaultUser);
    }

    [Fact]
    public async Task WriteDistro_pipes_to_tee_as_root()
    {
        var runner = new FakeProcessRunner();
        var svc = new WslConfigService(runner);
        var cfg = new WslDistroConfig { DefaultUser = "peter", Systemd = true };
        await svc.WriteDistroAsync("Ubuntu", cfg);
        Assert.Equal(new[] { "-d", "Ubuntu", "-u", "root", "tee", "/etc/wsl.conf" }, runner.LastArgs);
        Assert.Contains("default=peter", runner.LastStdin);
        Assert.Contains("systemd=true", runner.LastStdin);
    }

    [Fact]
    public void RoundTrips_NewSectionsAndKeys()
    {
        var ini = IniParser.Parse(
            "[automount]\nenabled=true\nmountFsTab=false\nroot=/\noptions=metadata,uid=1000\n" +
            "[interop]\nenabled=false\nappendWindowsPath=false\n" +
            "[network]\nhostname=devbox\ngenerateHosts=false\ngenerateResolvConf=false\ndns=1.1.1.1\n" +
            "[boot]\nsystemd=true\ncommand=service docker start\nprotectBinfmt=false\n" +
            "[gpu]\nenabled=false\n" +
            "[time]\nuseWindowsTimezone=false\n");

        var cfg = WslDistroConfig.FromIni(ini);
        Assert.Equal(false, cfg.MountFsTab);
        Assert.Equal("/", cfg.AutomountRoot);
        Assert.Equal("metadata,uid=1000", cfg.AutomountOptions);
        Assert.Equal(false, cfg.InteropEnabled);
        Assert.Equal(false, cfg.AppendWindowsPath);
        Assert.Equal("1.1.1.1", cfg.Dns);
        Assert.Equal("service docker start", cfg.BootCommand);
        Assert.Equal(false, cfg.GpuEnabled);
        Assert.Equal(false, cfg.UseWindowsTimezone);

        var back = cfg.ToIni();
        Assert.Equal("false", back["automount"]["mountFsTab"]);
        Assert.Equal("metadata,uid=1000", back["automount"]["options"]);
        Assert.Equal("service docker start", back["boot"]["command"]);
        Assert.Equal("1.1.1.1", back["network"]["dns"]);
        // existing modeled keys still survive
        Assert.Equal("true", back["automount"]["enabled"]);
        Assert.Equal("devbox", back["network"]["hostname"]);
    }
}
