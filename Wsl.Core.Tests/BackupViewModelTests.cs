using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class BackupViewModelTests
{
    private const string ListOutput =
        "  NAME      STATE     VERSION\r\n" +
        "* Ubuntu    Stopped   2\r\n" +
        "  Debian    Running   2\r\n";

    private static BackupViewModel NewVm(FakeProcessRunner runner)
    {
        var distros = new WslDistroService(runner);
        return new(new WslBackupService(runner), distros, new WslDeployService(runner), new StatePreservingExport(distros));
    }

    /// <summary>Creates a real temp .vhdx file so the VM's File.Exists guard passes.</summary>
    private static string TempVhdx()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wslcc-test-{Guid.NewGuid():N}.vhdx");
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    [Fact]
    public async Task Export_calls_export_with_selected_format()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME      STATE     VERSION\r\n* Ubuntu    Stopped   2\r\n"); // RunningAsync
        var vm = NewVm(runner);
        vm.ExportDistro = "Ubuntu";
        vm.ExportPath = @"C:\b\ubuntu.vhdx";
        vm.ExportFormat = ExportFormat.Vhd;

        await vm.ExportAsync();

        // Flow: list (queued), shutdown (auto), export, [no restart since Stopped]
        Assert.Contains(runner.AllArgs, a =>
            a.Length == 5 && a[0] == "--export" && a[1] == "Ubuntu" &&
            a[2] == @"C:\b\ubuntu.vhdx" && a[3] == "--format" && a[4] == "vhd");
        Assert.NotNull(vm.StatusMessage);
    }

    [Fact]
    public async Task Restore_calls_import_with_source_format()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);
        vm.RestoreName = "Ubuntu";
        vm.RestoreInstallDir = @"C:\wsl\ubuntu";
        vm.RestoreArchivePath = @"C:\b\ubuntu.tar";
        vm.RestoreFormat = ExportFormat.Tar;
        vm.RestoreVersion = 2;

        await vm.RestoreAsync();

        Assert.Equal(
            new[] { "--import", "Ubuntu", @"C:\wsl\ubuntu", @"C:\b\ubuntu.tar", "--version", "2" },
            runner.AllArgs[0]);
    }

    [Fact]
    public async Task Export_failure_sets_error()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ""); // list (RunningAsync)
        runner.Enqueue(1, "", "There is no distribution with the supplied name."); // export fails
        var vm = NewVm(runner);
        vm.ExportDistro = "Ghost";
        vm.ExportPath = @"C:\b\x.tar";
        vm.ExportFormat = ExportFormat.Tar;

        await vm.ExportAsync();

        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadDistros_populates_from_list()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);
        var vm = NewVm(runner);

        await vm.LoadDistrosAsync();

        Assert.Equal(new[] { "Ubuntu", "Debian" }, vm.Distros);
    }

    [Fact]
    public async Task ImportInPlace_registers_vhdx_after_guards_pass()
    {
        var vhdx = TempVhdx();
        try
        {
            var runner = new FakeProcessRunner();
            runner.Enqueue(0, ListOutput); // collision check
            runner.Enqueue(0, "");         // import-in-place
            var vm = NewVm(runner);
            vm.InPlaceName = "arch";
            vm.InPlaceVhdxPath = vhdx;

            await vm.ImportInPlaceAsync();

            Assert.Null(vm.ErrorMessage);
            Assert.Equal(new[] { "--import-in-place", "arch", vhdx }, runner.LastArgs);
            Assert.NotNull(vm.StatusMessage);
        }
        finally { File.Delete(vhdx); }
    }

    [Fact]
    public async Task ImportInPlace_refuses_when_file_missing()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);
        vm.InPlaceName = "arch";
        vm.InPlaceVhdxPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.vhdx");

        await vm.ImportInPlaceAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(runner.AllArgs);
    }

    [Fact]
    public async Task ImportInPlace_refuses_non_vhdx_extension()
    {
        var runner = new FakeProcessRunner();
        var vm = NewVm(runner);
        vm.InPlaceName = "arch";
        vm.InPlaceVhdxPath = @"C:\b\arch.tar";

        await vm.ImportInPlaceAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(runner.AllArgs);
    }

    [Fact]
    public async Task ImportInPlace_refuses_name_collision_without_importing()
    {
        var vhdx = TempVhdx();
        try
        {
            var runner = new FakeProcessRunner();
            runner.Enqueue(0, ListOutput); // existing distros: Ubuntu, Debian
            var vm = NewVm(runner);
            vm.InPlaceName = "ubuntu"; // collides case-insensitively
            vm.InPlaceVhdxPath = vhdx;

            await vm.ImportInPlaceAsync();

            Assert.NotNull(vm.ErrorMessage);
            Assert.Contains("ubuntu", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Single(runner.AllArgs); // only the list call, no --import-in-place
            Assert.Equal(new[] { "--list", "--verbose" }, runner.AllArgs[0]);
        }
        finally { File.Delete(vhdx); }
    }

    [Fact]
    public async Task ImportInPlace_collision_check_survives_list_failure()
    {
        var vhdx = TempVhdx();
        try
        {
            var runner = new FakeProcessRunner();
            runner.Enqueue(1, "", "no distros"); // list fails (nothing installed yet)
            runner.Enqueue(0, "");               // import proceeds
            var vm = NewVm(runner);
            vm.InPlaceName = "arch";
            vm.InPlaceVhdxPath = vhdx;

            await vm.ImportInPlaceAsync();

            Assert.Null(vm.ErrorMessage);
            Assert.Equal(new[] { "--import-in-place", "arch", vhdx }, runner.LastArgs);
        }
        finally { File.Delete(vhdx); }
    }

    [Fact]
    public async Task ImportInPlace_requires_name()
    {
        var vhdx = TempVhdx();
        try
        {
            var runner = new FakeProcessRunner();
            var vm = NewVm(runner);
            vm.InPlaceVhdxPath = vhdx;

            await vm.ImportInPlaceAsync();

            Assert.NotNull(vm.ErrorMessage);
            Assert.Empty(runner.AllArgs);
        }
        finally { File.Delete(vhdx); }
    }

    [Fact]
    public async Task ExportAsync_ShutsDownBeforeExport_AndRestartsRunning()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n"); // RunningAsync
        var distros = new WslDistroService(runner);
        var vm = new BackupViewModel(new WslBackupService(runner), distros,
            new WslDeployService(runner), new StatePreservingExport(distros));
        vm.ExportDistro = "Ubuntu"; vm.ExportPath = @"C:\b\u.tar"; vm.ExportFormat = ExportFormat.Tar;

        await vm.ExportAsync();

        var flat = runner.AllArgs;
        var shutdownIdx = flat.FindIndex(a => a.Length == 1 && a[0] == "--shutdown");
        var exportIdx = flat.FindIndex(a => a.Length >= 1 && a[0] == "--export");
        Assert.True(shutdownIdx >= 0 && exportIdx > shutdownIdx);
        Assert.Contains(flat, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Ubuntu"); // restarted
    }
}
