using System.ComponentModel;
using Wsl.Core;
using Wsl.Core.Containers;
using Xunit;

namespace Wsl.Core.Tests;

public class WslcServiceTests
{
    /// <summary>Runner that throws like ProcessStartInfo.Start() when the exe is missing.</summary>
    private sealed class MissingExeRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string exe, string[] args, TimeSpan? timeout = null, CancellationToken ct = default)
            => throw new Win32Exception("The system cannot find the file specified.");
        public Task<ProcessResult> RunWithInputAsync(string exe, string[] args, string stdin, TimeSpan? timeout = null, CancellationToken ct = default)
            => throw new Win32Exception();
    }

    /// <summary>Runner that throws a timeout WslException like RealProcessRunner does.</summary>
    private sealed class TimeoutRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string exe, string[] args, TimeSpan? timeout = null, CancellationToken ct = default)
            => throw new WslException(WslErrorKind.Timeout, "wslc timed out");
        public Task<ProcessResult> RunWithInputAsync(string exe, string[] args, string stdin, TimeSpan? timeout = null, CancellationToken ct = default)
            => throw new WslException(WslErrorKind.Timeout, "wslc timed out");
    }

    // ── DetectAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Detect_available_when_version_succeeds()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "wslc version 0.3.1-preview\n");
        var svc = new WslcService(runner);

        var a = await svc.DetectAsync();

        Assert.Equal(WslcState.Available, a.State);
        Assert.True(a.IsAvailable);
        Assert.Equal("wslc version 0.3.1-preview", a.Version);
        Assert.Equal(new[] { "--version" }, runner.LastArgs);
    }

    [Fact]
    public async Task Detect_notfound_when_exe_missing()
    {
        var svc = new WslcService(new MissingExeRunner());
        var a = await svc.DetectAsync();
        Assert.Equal(WslcState.NotFound, a.State);
        Assert.False(a.IsAvailable);
    }

    [Fact]
    public async Task Detect_unreachable_on_timeout()
    {
        var svc = new WslcService(new TimeoutRunner());
        var a = await svc.DetectAsync();
        Assert.Equal(WslcState.Unreachable, a.State);
    }

    [Fact]
    public async Task Detect_unreachable_on_nonzero_exit()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "some preview error");
        var svc = new WslcService(runner);
        var a = await svc.DetectAsync();
        Assert.Equal(WslcState.Unreachable, a.State);
    }

    // ── ListContainersAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task List_returns_empty_on_failure()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "boom");
        var svc = new WslcService(runner);
        Assert.Empty(await svc.ListContainersAsync());
    }

    [Fact]
    public async Task List_never_throws_when_exe_missing()
    {
        var svc = new WslcService(new MissingExeRunner());
        Assert.Empty(await svc.ListContainersAsync());
    }

    [Fact]
    public async Task List_parses_columnar_rows()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0,
            "CONTAINER ID   IMAGE          STATUS          NAMES\n" +
            "abc123         alpine:3.20    Up 2 minutes    web\n" +
            "def456         nginx:latest   Exited (0)      proxy\n");
        var svc = new WslcService(runner);

        var list = await svc.ListContainersAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("abc123", list[0].Id);
        Assert.Equal("alpine:3.20", list[0].Image);
        Assert.Equal("web", list[0].Name);
        Assert.Equal("Up 2 minutes", list[0].Status);
        Assert.Equal("proxy", list[1].Name);
    }

    // ── RunRawAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Raw_passes_args_verbatim()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "hello", "");
        var svc = new WslcService(runner);

        var res = await svc.RunRawAsync(new[] { "inspect", "web" });

        Assert.True(res.Ok);
        Assert.Equal("hello", res.StdOut);
        Assert.Equal(new[] { "inspect", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task Raw_rejects_empty_args()
    {
        var svc = new WslcService(new FakeProcessRunner());
        var res = await svc.RunRawAsync(Array.Empty<string>());
        Assert.False(res.Ok);
    }

    [Fact]
    public async Task Raw_degrades_on_missing_exe()
    {
        var svc = new WslcService(new MissingExeRunner());
        var res = await svc.RunRawAsync(new[] { "ps" });
        Assert.False(res.Ok);
        Assert.Contains("wslc.exe", res.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Raw_degrades_on_timeout()
    {
        var svc = new WslcService(new TimeoutRunner());
        var res = await svc.RunRawAsync(new[] { "logs", "web" });
        Assert.False(res.Ok);
        Assert.Contains("timed out", res.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    // ── DetectRuntimeAsync ───────────────────────────────────────────────────

    [Theory]
    [InlineData("docker\n", ContainerRuntime.Docker)]
    [InlineData("podman\n", ContainerRuntime.Podman)]
    [InlineData("none\n", ContainerRuntime.None)]
    public async Task DetectRuntime_maps_token(string stdout, ContainerRuntime expected)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, stdout);
        var svc = new WslcService(runner);

        var rt = await svc.DetectRuntimeAsync("Ubuntu");

        Assert.Equal(expected, rt);
        Assert.Equal(new[] { "-d", "Ubuntu", "--", "sh", "-lc" }, runner.LastArgs![..5]);
    }

    [Fact]
    public async Task DetectRuntime_none_for_blank_distro()
    {
        var svc = new WslcService(new FakeProcessRunner());
        Assert.Equal(ContainerRuntime.None, await svc.DetectRuntimeAsync(""));
    }
}
