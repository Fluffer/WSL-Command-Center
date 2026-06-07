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

    private const string ListOutput =
        "  NAME      STATE     VERSION\r\n" +
        "* Ubuntu    Stopped   2\r\n" +
        "  Debian    Running   2\r\n";

    private static DeployViewModel NewVm(FakeProcessRunner runner)
        => new(new WslDeployService(runner), new WslDistroService(runner));

    [Fact]
    public async Task LoadCatalog_populates_entries()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, Catalog);
        var vm = NewVm(runner);

        await vm.LoadCatalogAsync();

        Assert.Equal(2, vm.Catalog.Count);
        Assert.Equal("Ubuntu", vm.Catalog[0].Name);
    }

    [Fact]
    public async Task InstallSelected_calls_install()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);
        vm.SelectedCatalogEntry = new CatalogEntry("Debian", "Debian GNU/Linux");

        await vm.InstallSelectedAsync();

        Assert.Equal(new[] { "--install", "-d", "Debian", "--no-launch" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task ImportArchive_tar_calls_import_tar()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);
        vm.ImportName = "Custom";
        vm.ImportInstallDir = @"C:\wsl\custom";
        vm.ImportArchivePath = @"C:\b\custom.tar";
        vm.ImportVersion = 2;

        await vm.ImportArchiveAsync();

        Assert.Equal(
            new[] { "--import", "Custom", @"C:\wsl\custom", @"C:\b\custom.tar", "--version", "2" },
            runner.AllArgs[0]);
    }

    [Fact]
    public async Task ImportArchive_vhdx_calls_import_vhd()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);
        vm.ImportName = "Custom";
        vm.ImportInstallDir = @"C:\wsl\custom";
        vm.ImportArchivePath = @"C:\b\custom.vhdx";
        vm.ImportVersion = 2;

        await vm.ImportArchiveAsync();

        Assert.Contains("--vhd", runner.AllArgs[0]);
    }

    [Fact]
    public async Task InstallAdvanced_requires_distro_or_file()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);

        await vm.InstallAdvancedAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(runner.AllArgs);
    }

    [Fact]
    public async Task InstallAdvanced_requires_name_when_from_file()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);
        vm.AdvancedFromFile = @"D:\images\arch.wsl";

        await vm.InstallAdvancedAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(runner.AllArgs);
    }

    [Fact]
    public async Task InstallAdvanced_blocks_name_collision_without_installing()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput); // existing distros: Ubuntu, Debian
        var vm = NewVm(runner);
        vm.AdvancedFromFile = @"D:\images\arch.wsl";
        vm.AdvancedName = "ubuntu"; // collides case-insensitively

        await vm.InstallAdvancedAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("ubuntu", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Single(runner.AllArgs); // only the list call, no --install
        Assert.Equal(new[] { "--list", "--verbose" }, runner.AllArgs[0]);
    }

    [Fact]
    public async Task InstallAdvanced_from_file_composes_install_args()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput); // collision check
        runner.Enqueue(0, "");         // install
        var vm = NewVm(runner);
        vm.AdvancedFromFile = @"D:\images\arch.wsl";
        vm.AdvancedName = "arch-custom";

        await vm.InstallAdvancedAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(new[] { "--install", "--from-file", @"D:\images\arch.wsl",
            "--name", "arch-custom", "--version", "2", "--no-launch" }, runner.LastArgs);
    }

    [Fact]
    public async Task InstallAdvanced_catalog_with_options_composes_install_args()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput); // collision check
        runner.Enqueue(0, "");         // install
        var vm = NewVm(runner);
        vm.AdvancedCatalogEntry = new CatalogEntry("Ubuntu-24.04", "Ubuntu 24.04 LTS");
        vm.AdvancedName = "ubuntu-dev2";
        vm.AdvancedLocation = @"D:\wsl\ubuntu-dev2";
        vm.AdvancedWebDownload = true;

        await vm.InstallAdvancedAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(new[] { "--install", "Ubuntu-24.04", "--name", "ubuntu-dev2",
            "--location", @"D:\wsl\ubuntu-dev2", "--version", "2",
            "--web-download", "--no-launch" }, runner.LastArgs);
    }

    [Fact]
    public async Task InstallAdvanced_collision_check_survives_list_failure()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "no distros"); // list fails (e.g. nothing installed yet)
        runner.Enqueue(0, "");               // install proceeds
        var vm = NewVm(runner);
        vm.AdvancedFromFile = @"D:\images\arch.wsl";
        vm.AdvancedName = "arch-custom";

        await vm.InstallAdvancedAsync();

        Assert.Null(vm.ErrorMessage);
        Assert.Contains("--install", runner.LastArgs!);
    }

    [Fact]
    public void Advanced_catalog_and_file_are_mutually_exclusive()
    {
        var vm = NewVm(new FakeProcessRunner());
        Assert.True(vm.IsAdvancedCatalogEnabled);
        Assert.True(vm.IsAdvancedFileEnabled);

        vm.AdvancedFromFile = @"D:\images\arch.wsl";
        Assert.False(vm.IsAdvancedCatalogEnabled);
        Assert.True(vm.IsAdvancedFileEnabled);

        vm.AdvancedFromFile = "";
        vm.AdvancedCatalogEntry = new CatalogEntry("Ubuntu", "Ubuntu");
        Assert.True(vm.IsAdvancedCatalogEnabled);
        Assert.False(vm.IsAdvancedFileEnabled);
    }
}
