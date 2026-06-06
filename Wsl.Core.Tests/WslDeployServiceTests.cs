using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslDeployServiceTests
{
    private const string CatalogOutput =
        "The following is a list of valid distributions that can be installed.\r\n" +
        "Install using 'wsl.exe --install <Distro>'.\r\n" +
        "\r\n" +
        "NAME                   FRIENDLY NAME\r\n" +
        "Ubuntu                 Ubuntu\r\n" +
        "Debian                 Debian GNU/Linux\r\n" +
        "kali-linux             Kali Linux Rolling\r\n";

    [Fact]
    public async Task Parses_catalog_entries()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, CatalogOutput);
        var entries = await new WslDeployService(runner).ListAvailableAsync();

        Assert.Equal(3, entries.Count);
        Assert.Equal("Ubuntu", entries[0].Name);
        Assert.Equal("Debian", entries[1].Name);
        Assert.Equal("Debian GNU/Linux", entries[1].FriendlyName);
        Assert.Equal("Kali Linux Rolling", entries[2].FriendlyName);
    }

    [Fact]
    public async Task InstallFromCatalog_uses_no_launch()
    {
        var runner = new FakeProcessRunner();
        await new WslDeployService(runner).InstallFromCatalogAsync("Debian");
        Assert.Equal(new[] { "--install", "-d", "Debian", "--no-launch" }, runner.LastArgs);
    }

    [Fact]
    public async Task ImportTar_builds_args()
    {
        var runner = new FakeProcessRunner();
        await new WslDeployService(runner)
            .ImportTarAsync("Custom", @"C:\wsl\custom", @"C:\backups\custom.tar", 2);
        Assert.Equal(
            new[] { "--import", "Custom", @"C:\wsl\custom", @"C:\backups\custom.tar", "--version", "2" },
            runner.LastArgs);
    }

    [Fact]
    public async Task ImportVhdx_adds_vhd_flag()
    {
        var runner = new FakeProcessRunner();
        await new WslDeployService(runner)
            .ImportVhdxAsync("Custom", @"C:\wsl\custom", @"C:\backups\custom.vhdx", 2);
        Assert.Equal(
            new[] { "--import", "Custom", @"C:\wsl\custom", @"C:\backups\custom.vhdx", "--vhd", "--version", "2" },
            runner.LastArgs);
    }
}
