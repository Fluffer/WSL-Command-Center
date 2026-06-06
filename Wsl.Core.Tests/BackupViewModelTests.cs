using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class BackupViewModelTests
{
    [Fact]
    public async Task Export_calls_export_with_selected_format()
    {
        var runner = new FakeProcessRunner();
        var vm = new BackupViewModel(new WslBackupService(runner), new WslDistroService(runner))
        {
            ExportDistro = "Ubuntu",
            ExportPath = @"C:\b\ubuntu.vhdx",
            ExportFormat = ExportFormat.Vhd,
        };

        await vm.ExportAsync();

        Assert.Equal(
            new[] { "--export", "Ubuntu", @"C:\b\ubuntu.vhdx", "--format", "vhd" },
            runner.AllArgs[0]);
        Assert.NotNull(vm.StatusMessage);
    }

    [Fact]
    public async Task Restore_calls_import_with_source_format()
    {
        var runner = new FakeProcessRunner();
        var vm = new BackupViewModel(new WslBackupService(runner), new WslDistroService(runner))
        {
            RestoreName = "Ubuntu",
            RestoreInstallDir = @"C:\wsl\ubuntu",
            RestoreArchivePath = @"C:\b\ubuntu.tar",
            RestoreFormat = ExportFormat.Tar,
            RestoreVersion = 2,
        };

        await vm.RestoreAsync();

        Assert.Equal(
            new[] { "--import", "Ubuntu", @"C:\wsl\ubuntu", @"C:\b\ubuntu.tar", "--version", "2" },
            runner.AllArgs[0]);
    }

    [Fact]
    public async Task Export_failure_sets_error()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "There is no distribution with the supplied name.");
        var vm = new BackupViewModel(new WslBackupService(runner), new WslDistroService(runner))
        {
            ExportDistro = "Ghost", ExportPath = @"C:\b\x.tar", ExportFormat = ExportFormat.Tar,
        };

        await vm.ExportAsync();

        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadDistros_populates_from_list()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0,
            "  NAME      STATE     VERSION\r\n" +
            "* Ubuntu    Stopped   2\r\n" +
            "  Debian    Running   2\r\n");
        var vm = new BackupViewModel(new WslBackupService(runner), new WslDistroService(runner));

        await vm.LoadDistrosAsync();

        Assert.Equal(new[] { "Ubuntu", "Debian" }, vm.Distros);
    }
}
