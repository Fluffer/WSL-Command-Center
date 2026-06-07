using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class WslSystemServiceTests
{
    private const string StatusOutput =
        "Default Distribution: Ubuntu\r\nDefault Version: 2\r\n";

    private const string VersionOutput =
        "WSL version: 2.4.13.0\r\nKernel version: 5.15.167.4-1\r\n" +
        "WSLg version: 1.0.65\r\nWindows version: 10.0.26200.1\r\n";

    [Fact]
    public async Task GetStatusAsync_parses_default_distro_and_version()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, StatusOutput);
        var svc = new WslSystemService(runner);

        var status = await svc.GetStatusAsync();

        Assert.Equal(new[] { "--status" }, runner.LastArgs);
        Assert.Equal("Ubuntu", status.DefaultDistro);
        Assert.Equal(2, status.DefaultVersion);
    }

    [Fact]
    public async Task GetVersionInfoAsync_parses_wsl_and_kernel_versions()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, VersionOutput);
        var svc = new WslSystemService(runner);

        var v = await svc.GetVersionInfoAsync();

        Assert.Equal(new[] { "--version" }, runner.LastArgs);
        Assert.Equal("2.4.13.0", v.WslVersion);
        Assert.Equal("5.15.167.4-1", v.KernelVersion);
    }

    [Fact]
    public async Task GetVersionInfoAsync_exposes_parsed_wsl_version_for_gating()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, VersionOutput);
        var svc = new WslSystemService(runner);

        var v = await svc.GetVersionInfoAsync();

        Assert.True(v.WslVersionParsed >= new Version(2, 0, 14));
    }

    [Fact]
    public async Task Empty_values_after_colon_parse_as_null()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Default Distribution:\r\nDefault Version:\r\n");
        var svc = new WslSystemService(runner);
        var status = await svc.GetStatusAsync();
        Assert.Null(status.DefaultDistro);
        Assert.Null(status.DefaultVersion);

        runner.Enqueue(0, "WSL version:\r\nKernel version:  \r\nWSLg version:\r\n");
        var v = await svc.GetVersionInfoAsync();
        Assert.Null(v.WslVersion);
        Assert.Null(v.KernelVersion);
        Assert.Null(v.WslgVersion);
    }
}
