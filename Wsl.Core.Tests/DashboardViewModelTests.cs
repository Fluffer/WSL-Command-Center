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

    [Fact]
    public async Task Refresh_populates_distros()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);
        var vm = new DashboardViewModel(new WslDistroService(runner));

        await vm.RefreshAsync();

        Assert.Equal(2, vm.Distros.Count);
        Assert.Equal("Ubuntu", vm.Distros[0].Name);
    }

    [Fact]
    public async Task Refresh_surfaces_error_message_on_failure()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "The Windows Subsystem for Linux is not installed.");
        var vm = new DashboardViewModel(new WslDistroService(runner));

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
        var vm = new DashboardViewModel(new WslDistroService(runner));

        await vm.TerminateAsync("Debian");

        Assert.Equal(new[] { "--terminate", "Debian" }, runner.AllArgs[0]);
        Assert.Equal(2, vm.Distros.Count); // refreshed after action
    }
}
