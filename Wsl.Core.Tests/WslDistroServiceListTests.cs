using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslDistroServiceListTests
{
    // Real `wsl -l -v` layout: leading 2-char marker column, then NAME / STATE / VERSION.
    private const string ListOutput =
        "  NAME                   STATE           VERSION\r\n" +
        "* Ubuntu                 Stopped         2\r\n" +
        "  podman-machine-default Stopped         2\r\n";

    private static WslDistroService MakeService(string stdout)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, stdout);
        return new WslDistroService(runner);
    }

    [Fact]
    public async Task Parses_two_distros()
    {
        var distros = await MakeService(ListOutput).ListAsync();
        Assert.Equal(2, distros.Count);
    }

    [Fact]
    public async Task Parses_name_state_version_and_default()
    {
        var distros = await MakeService(ListOutput).ListAsync();

        var ubuntu = distros[0];
        Assert.Equal("Ubuntu", ubuntu.Name);
        Assert.Equal(DistroState.Stopped, ubuntu.State);
        Assert.Equal(2, ubuntu.Version);
        Assert.True(ubuntu.IsDefault);

        var podman = distros[1];
        Assert.Equal("podman-machine-default", podman.Name);
        Assert.False(podman.IsDefault);
    }

    [Fact]
    public async Task Parses_running_state()
    {
        const string running =
            "  NAME      STATE     VERSION\r\n" +
            "* Ubuntu    Running   2\r\n";
        var distros = await MakeService(running).ListAsync();
        Assert.Equal(DistroState.Running, distros[0].State);
    }

    [Fact]
    public async Task Empty_when_no_distros()
    {
        const string none = "  NAME      STATE     VERSION\r\n";
        var distros = await MakeService(none).ListAsync();
        Assert.Empty(distros);
    }

    [Fact]
    public async Task Passes_correct_args()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListOutput);
        await new WslDistroService(runner).ListAsync();
        Assert.Equal("wsl.exe", runner.LastExe);
        Assert.Equal(new[] { "--list", "--verbose" }, runner.LastArgs);
    }
}
