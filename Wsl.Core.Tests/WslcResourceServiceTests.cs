using System.ComponentModel;
using Wsl.Core;
using Wsl.Core.Containers;
using Xunit;

namespace Wsl.Core.Tests;

public class WslcResourceServiceTests
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

    // ── ListImagesAsync ──────────────────────────────────────────────────────

    private const string ImagesJson = """
        [
          {
            "Created": 1781062173,
            "Id": "sha256:5a4c6b929c57abe310fad22db2820f1423e645cdc9344bb05adaca9b50c3403f",
            "Repository": "ubuntu",
            "Size": 100154952,
            "Tag": "latest"
          }
        ]
        """;

    [Fact]
    public async Task ListImages_parses_contract_json()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ImagesJson);
        var svc = new WslcResourceService(runner);

        var images = await svc.ListImagesAsync();

        Assert.Single(images);
        var img = images[0];
        Assert.Equal("sha256:5a4c6b929c57abe310fad22db2820f1423e645cdc9344bb05adaca9b50c3403f", img.FullId);
        Assert.Equal("5a4c6b929c57", img.ShortId);
        Assert.Equal("ubuntu", img.Repository);
        Assert.Equal("latest", img.Tag);
        Assert.Equal("ubuntu:latest", img.RepoTag);
        Assert.Equal(100154952, img.SizeBytes);
        Assert.False(string.IsNullOrWhiteSpace(img.SizeHuman));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1781062173), img.Created);
        Assert.Equal(new[] { "image", "list", "--format", "json" }, runner.LastArgs);
    }

    [Fact]
    public async Task ListImages_empty_array_yields_empty_list()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "[]");
        var svc = new WslcResourceService(runner);

        Assert.Empty(await svc.ListImagesAsync());
    }

    [Fact]
    public async Task ListImages_malformed_json_degrades_to_empty()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "not json");
        var svc = new WslcResourceService(runner);

        Assert.Empty(await svc.ListImagesAsync());
    }

    [Fact]
    public async Task ListImages_never_throws_when_exe_missing()
    {
        var svc = new WslcResourceService(new MissingExeRunner());
        Assert.Empty(await svc.ListImagesAsync());
    }

    [Fact]
    public async Task ListImages_empty_on_nonzero_exit()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "boom");
        var svc = new WslcResourceService(runner);
        Assert.Empty(await svc.ListImagesAsync());
    }

    // ── Image mutating verbs ─────────────────────────────────────────────────

    [Fact]
    public async Task RemoveImage_passes_force_and_no_prune_flags()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.RemoveImageAsync("ubuntu:latest", force: true, noPrune: true);

        Assert.Equal(new[] { "image", "remove", "--force", "--no-prune", "ubuntu:latest" }, runner.LastArgs);
    }

    [Fact]
    public async Task RemoveImage_omits_flags_by_default()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.RemoveImageAsync("ubuntu:latest");

        Assert.Equal(new[] { "image", "remove", "ubuntu:latest" }, runner.LastArgs);
    }

    [Fact]
    public async Task PruneImages_passes_all_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.PruneImagesAsync(all: true);

        Assert.Equal(new[] { "image", "prune", "--all" }, runner.LastArgs);
    }

    [Fact]
    public async Task PullImage_uses_toplevel_shortcut()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.PullImageAsync("ubuntu:latest");

        Assert.Equal(new[] { "pull", "ubuntu:latest" }, runner.LastArgs);
    }

    [Fact]
    public async Task PushImage_uses_toplevel_shortcut()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.PushImageAsync("ubuntu:latest");

        Assert.Equal(new[] { "push", "ubuntu:latest" }, runner.LastArgs);
    }

    [Fact]
    public async Task TagImage_passes_source_and_target()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.TagImageAsync("ubuntu:latest", "myrepo/ubuntu:v1");

        Assert.Equal(new[] { "tag", "ubuntu:latest", "myrepo/ubuntu:v1" }, runner.LastArgs);
    }

    // ── ListVolumesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListVolumes_parses_contract_json()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, """[ { "Driver": "guest", "Name": "wcc-probe-vol" } ]""");
        var svc = new WslcResourceService(runner);

        var volumes = await svc.ListVolumesAsync();

        Assert.Single(volumes);
        Assert.Equal("guest", volumes[0].Driver);
        Assert.Equal("wcc-probe-vol", volumes[0].Name);
        Assert.Equal(new[] { "volume", "list", "--format", "json" }, runner.LastArgs);
    }

    [Fact]
    public async Task ListVolumes_empty_array_yields_empty_list()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "[]");
        var svc = new WslcResourceService(runner);

        Assert.Empty(await svc.ListVolumesAsync());
    }

    [Fact]
    public async Task ListVolumes_malformed_json_degrades_to_empty()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "{not valid");
        var svc = new WslcResourceService(runner);

        Assert.Empty(await svc.ListVolumesAsync());
    }

    [Fact]
    public async Task CreateVolume_passes_driver_when_given()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.CreateVolumeAsync("wcc-probe-vol", driver: "vhd");

        Assert.Equal(new[] { "volume", "create", "--driver", "vhd", "wcc-probe-vol" }, runner.LastArgs);
    }

    [Fact]
    public async Task CreateVolume_omits_driver_flag_when_not_given()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.CreateVolumeAsync("wcc-probe-vol");

        Assert.Equal(new[] { "volume", "create", "wcc-probe-vol" }, runner.LastArgs);
    }

    [Fact]
    public async Task RemoveVolume_passes_name()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.RemoveVolumeAsync("wcc-probe-vol");

        Assert.Equal(new[] { "volume", "remove", "wcc-probe-vol" }, runner.LastArgs);
    }

    [Fact]
    public async Task PruneVolumes_passes_all_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.PruneVolumesAsync(all: true);

        Assert.Equal(new[] { "volume", "prune", "--all" }, runner.LastArgs);
    }

    // ── ListNetworksAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ListNetworks_parses_contract_json()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, """[ { "Driver": "bridge", "Id": "a1f055b7f891", "Name": "wcc-probe-net" } ]""");
        var svc = new WslcResourceService(runner);

        var networks = await svc.ListNetworksAsync();

        Assert.Single(networks);
        Assert.Equal("bridge", networks[0].Driver);
        Assert.Equal("a1f055b7f891", networks[0].Id);
        Assert.Equal("wcc-probe-net", networks[0].Name);
        Assert.Equal(new[] { "network", "list", "--format", "json" }, runner.LastArgs);
    }

    [Fact]
    public async Task ListNetworks_empty_array_yields_empty_list()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "[]");
        var svc = new WslcResourceService(runner);

        Assert.Empty(await svc.ListNetworksAsync());
    }

    [Fact]
    public async Task ListNetworks_malformed_json_degrades_to_empty()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "<xml>not json</xml>");
        var svc = new WslcResourceService(runner);

        Assert.Empty(await svc.ListNetworksAsync());
    }

    [Fact]
    public async Task CreateNetwork_passes_driver_when_given()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.CreateNetworkAsync("wcc-probe-net", driver: "bridge");

        Assert.Equal(new[] { "network", "create", "--driver", "bridge", "wcc-probe-net" }, runner.LastArgs);
    }

    [Fact]
    public async Task RemoveNetwork_passes_name()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.RemoveNetworkAsync("wcc-probe-net");

        Assert.Equal(new[] { "network", "remove", "wcc-probe-net" }, runner.LastArgs);
    }

    [Fact]
    public async Task PruneNetworks_sends_bare_prune()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.PruneNetworksAsync();

        Assert.Equal(new[] { "network", "prune" }, runner.LastArgs);
    }

    // ── Registry ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_sends_password_via_stdin_never_on_argv()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Login Succeeded");
        var svc = new WslcResourceService(runner);

        const string secret = "s3cr3t-p@ssw0rd";
        var result = await svc.LoginAsync("registry.example.com", "alice", secret);

        Assert.True(result.Ok);
        Assert.Equal(
            new[] { "registry", "login", "--username", "alice", "--password-stdin", "registry.example.com" },
            runner.LastArgs);
        Assert.DoesNotContain(runner.LastArgs!, a => a.Contains(secret));
        Assert.Equal(secret, runner.LastStdin);
    }

    [Fact]
    public async Task Login_omits_server_when_not_given()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Login Succeeded");
        var svc = new WslcResourceService(runner);

        await svc.LoginAsync(null, "alice", "hunter2");

        Assert.Equal(
            new[] { "registry", "login", "--username", "alice", "--password-stdin" },
            runner.LastArgs);
    }

    [Fact]
    public async Task Login_degrades_on_missing_exe()
    {
        var svc = new WslcResourceService(new MissingExeRunner());
        var result = await svc.LoginAsync("registry.example.com", "alice", "hunter2");
        Assert.False(result.Ok);
        Assert.Contains("wslc.exe", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_degrades_on_timeout()
    {
        var svc = new WslcResourceService(new TimeoutRunner());
        var result = await svc.LoginAsync("registry.example.com", "alice", "hunter2");
        Assert.False(result.Ok);
        Assert.Contains("timed out", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_passes_server_when_given()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.LogoutAsync("registry.example.com");

        Assert.Equal(new[] { "registry", "logout", "registry.example.com" }, runner.LastArgs);
    }

    [Fact]
    public async Task Logout_omits_server_when_not_given()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcResourceService(runner);

        await svc.LogoutAsync();

        Assert.Equal(new[] { "registry", "logout" }, runner.LastArgs);
    }

    // ── Failure degradation shared by all mutating verbs ────────────────────

    [Fact]
    public async Task Action_degrades_on_nonzero_exit_carrying_stderr()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "no such image");
        var svc = new WslcResourceService(runner);

        var result = await svc.RemoveImageAsync("missing:latest");

        Assert.False(result.Ok);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("no such image", result.StdErr);
    }

    [Fact]
    public async Task Action_degrades_on_missing_exe()
    {
        var svc = new WslcResourceService(new MissingExeRunner());
        var result = await svc.RemoveVolumeAsync("wcc-probe-vol");
        Assert.False(result.Ok);
        Assert.Contains("wslc.exe", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Action_degrades_on_timeout()
    {
        var svc = new WslcResourceService(new TimeoutRunner());
        var result = await svc.RemoveNetworkAsync("wcc-probe-net");
        Assert.False(result.Ok);
        Assert.Contains("timed out", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }
}
