using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class DeployViewModelTests
{
    private const string Catalog =
        "NAME       FRIENDLY NAME\r\n" +
        "Ubuntu     Ubuntu\r\n" +
        "Debian     Debian GNU/Linux\r\n";

    [Fact]
    public async Task LoadCatalog_populates_entries()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, Catalog);
        var vm = new DeployViewModel(new WslDeployService(runner));

        await vm.LoadCatalogAsync();

        Assert.Equal(2, vm.Catalog.Count);
        Assert.Equal("Ubuntu", vm.Catalog[0].Name);
    }

    [Fact]
    public async Task InstallSelected_calls_install()
    {
        var runner = new FakeProcessRunner();
        var vm = new DeployViewModel(new WslDeployService(runner))
        {
            SelectedCatalogEntry = new CatalogEntry("Debian", "Debian GNU/Linux")
        };

        await vm.InstallSelectedAsync();

        Assert.Equal(new[] { "--install", "-d", "Debian", "--no-launch" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task ImportArchive_tar_calls_import_tar()
    {
        var runner = new FakeProcessRunner();
        var vm = new DeployViewModel(new WslDeployService(runner))
        {
            ImportName = "Custom",
            ImportInstallDir = @"C:\wsl\custom",
            ImportArchivePath = @"C:\b\custom.tar",
            ImportVersion = 2,
        };

        await vm.ImportArchiveAsync();

        Assert.Equal(
            new[] { "--import", "Custom", @"C:\wsl\custom", @"C:\b\custom.tar", "--version", "2" },
            runner.AllArgs[0]);
    }

    [Fact]
    public async Task ImportArchive_vhdx_calls_import_vhd()
    {
        var runner = new FakeProcessRunner();
        var vm = new DeployViewModel(new WslDeployService(runner))
        {
            ImportName = "Custom",
            ImportInstallDir = @"C:\wsl\custom",
            ImportArchivePath = @"C:\b\custom.vhdx",
            ImportVersion = 2,
        };

        await vm.ImportArchiveAsync();

        Assert.Contains("--vhd", runner.AllArgs[0]);
    }
}
