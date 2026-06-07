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

    [Fact]
    public async Task InstallCustomAsync_composes_all_flags()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslDeployService(runner);

        await svc.InstallCustomAsync(new CustomInstallOptions
        {
            Distro = "Ubuntu-24.04",
            Name = "ubuntu-dev2",
            Location = @"D:\wsl\ubuntu-dev2",
            Version = 2,
            WebDownload = true,
        });

        Assert.Equal(new[] { "--install", "Ubuntu-24.04", "--name", "ubuntu-dev2",
            "--location", @"D:\wsl\ubuntu-dev2", "--version", "2",
            "--web-download", "--no-launch" }, runner.LastArgs);
    }

    [Fact]
    public async Task InstallCustomAsync_from_file_replaces_catalog_distro()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslDeployService(runner);

        await svc.InstallCustomAsync(new CustomInstallOptions
        {
            FromFile = @"D:\images\arch.wsl",
            Name = "arch-custom",
        });

        Assert.Equal(new[] { "--install", "--from-file", @"D:\images\arch.wsl",
            "--name", "arch-custom", "--no-launch" }, runner.LastArgs);
    }

    [Fact]
    public async Task InstallCustomAsync_rejects_both_distro_and_from_file()
    {
        var svc = new WslDeployService(new FakeProcessRunner());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.InstallCustomAsync(new CustomInstallOptions
            { Distro = "Ubuntu", FromFile = @"D:\x.wsl" }));
    }
}
