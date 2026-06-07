using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class DashboardViewModelTests
{
    private const string ListOutput =
        "  NAME      STATE     VERSION\r\n" +
        "* Ubuntu    Stopped   2\r\n" +
        "  Debian    Running   2\r\n";

    private const string VersionOutput =
        "WSL version: 2.4.13.0\r\nKernel version: 5.15.167.4-1\r\n" +
        "WSLg version: 1.0.65\r\nWindows version: 10.0.26200.1\r\n";

    private const string StatusOutput =
        "Default Distribution: Ubuntu\r\nDefault Version: 2\r\n";

    private static DashboardViewModel NewVm(FakeProcessRunner runner)
        => new(new WslDistroService(runner), new WslDiskService(runner), new WslSystemService(runner));

    [Fact]
    public async Task Refresh_populates_distros()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);
        var vm = NewVm(runner);

        await vm.RefreshAsync();

        Assert.Equal(2, vm.Distros.Count);
        Assert.Equal("Ubuntu", vm.Distros[0].Name);
    }

    [Fact]
    public async Task Refresh_surfaces_error_message_on_failure()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "The Windows Subsystem for Linux is not installed.");
        var vm = NewVm(runner);

        await vm.RefreshAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(vm.Distros);
    }

    [Fact]
    public async Task Terminate_then_refresh_issues_terminate_then_list()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");          // terminate
        runner.Enqueue(0, ListOutput);  // refresh
        var vm = NewVm(runner);

        await vm.TerminateAsync("Debian");

        Assert.Equal(new[] { "--terminate", "Debian" }, runner.AllArgs[0]);
        Assert.Equal(2, vm.Distros.Count); // refreshed after action
    }

    [Fact]
    public async Task Refresh_populates_wsl_status_summary()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);     // --list --verbose
        runner.Enqueue(0, VersionOutput);  // --version
        runner.Enqueue(0, StatusOutput);   // --status
        var vm = NewVm(runner);

        await vm.RefreshAsync();

        Assert.Equal("WSL 2.4.13.0 · kernel 5.15.167.4-1 · default: Ubuntu (v2)", vm.WslStatusSummary);
    }

    [Fact]
    public async Task Refresh_status_failure_does_not_break_distro_refresh()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);                 // --list --verbose succeeds
        runner.Enqueue(1, "", "wsl.exe exploded");     // --version fails
        var vm = NewVm(runner);

        await vm.RefreshAsync();

        Assert.Equal(2, vm.Distros.Count);
        Assert.Null(vm.WslStatusSummary);
        Assert.Null(vm.ErrorMessage);
    }
}
