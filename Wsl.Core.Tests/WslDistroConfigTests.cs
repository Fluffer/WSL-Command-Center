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
}
