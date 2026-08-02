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

    // ── ListContainersAsync — JSON path ────────────────────────────────────────

    private const string JsonListSample = """
        [
          {
            "CreatedAt": 1785630953,
            "Id": "d60f680b1d8b3cdb417830eeb296663726583e742f29180ca10ca1ce7cf2e6d6",
            "Image": "ubuntu",
            "Name": "wcc-probe",
            "Ports": [],
            "State": 2,
            "StateChangedAt": 1785630969
          }
        ]
        """;

    [Fact]
    public async Task List_passes_all_and_json_format_flags()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, JsonListSample);
        var svc = new WslcService(runner);

        await svc.ListContainersAsync();

        Assert.Equal(new[] { "list", "--all", "--format", "json" }, runner.LastArgs);
        Assert.Single(runner.AllArgs); // JSON path succeeded — no fallback call made.
    }

    [Fact]
    public async Task List_json_parses_full_container_shape()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, JsonListSample);
        var svc = new WslcService(runner);

        var list = await svc.ListContainersAsync();

        var c = Assert.Single(list);
        Assert.Equal("d60f680b1d8b3cdb417830eeb296663726583e742f29180ca10ca1ce7cf2e6d6", c.Id);
        Assert.Equal("d60f680b1d8b", c.ShortId);
        Assert.Equal("wcc-probe", c.Name);
        Assert.Equal("ubuntu", c.Image);
        Assert.Equal(WslcContainerState.Running, c.State);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1785630953), c.CreatedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1785630969), c.StateChangedAt);
        Assert.Equal("", c.Ports);
    }

    [Theory]
    [InlineData(1, WslcContainerState.Created)]
    [InlineData(2, WslcContainerState.Running)]
    [InlineData(3, WslcContainerState.Exited)]
    [InlineData(99, WslcContainerState.Unknown)]
    public async Task List_json_maps_state_values(int rawState, WslcContainerState expected)
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, $$"""
            [{"CreatedAt":1,"Id":"abc123","Image":"ubuntu","Name":"n","Ports":[],"State":{{rawState}},"StateChangedAt":1}]
            """);
        var svc = new WslcService(runner);

        var list = await svc.ListContainersAsync();

        Assert.Equal(expected, Assert.Single(list).State);
    }

    [Fact]
    public async Task List_json_joins_non_empty_ports()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, """
            [{"CreatedAt":1,"Id":"abc123","Image":"ubuntu","Name":"n","Ports":["0.0.0.0:8080->80/tcp"],"State":2,"StateChangedAt":1}]
            """);
        var svc = new WslcService(runner);

        var list = await svc.ListContainersAsync();

        Assert.Equal("0.0.0.0:8080->80/tcp", Assert.Single(list).Ports);
    }

    [Fact]
    public async Task List_json_empty_array_returns_empty_without_fallback()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "[]");
        var svc = new WslcService(runner);

        var list = await svc.ListContainersAsync();

        Assert.Empty(list);
        Assert.Single(runner.AllArgs); // no fallback call — "[]" is a valid, empty result.
    }

    // ── ListContainersAsync — columnar fallback ─────────────────────────────────

    [Fact]
    public async Task List_falls_back_to_table_on_nonzero_json_exit()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "unknown flag --format");
        runner.Enqueue(0,
            "CONTAINER ID   NAME        IMAGE          CREATED         STATUS          PORTS\n" +
            "abc123def456   web         alpine:3.20    2 minutes ago   Up 2 minutes    0.0.0.0:8080->80/tcp\n" +
            "789012ghijkl   proxy       nginx:latest   5 minutes ago   Exited (0)      \n");
        var svc = new WslcService(runner);

        var list = await svc.ListContainersAsync();

        Assert.Equal(2, runner.AllArgs.Count);
        Assert.Equal(new[] { "list", "--all", "--format", "json" }, runner.AllArgs[0]);
        Assert.Equal(new[] { "list", "--all" }, runner.AllArgs[1]);

        Assert.Equal(2, list.Count);
        Assert.Equal("abc123def456", list[0].Id);
        Assert.Equal("abc123def456", list[0].ShortId);
        Assert.Equal("web", list[0].Name);
        Assert.Equal("alpine:3.20", list[0].Image);
        Assert.Equal("Up 2 minutes", list[0].Status);
        Assert.Equal("0.0.0.0:8080->80/tcp", list[0].Ports);
        Assert.Equal(WslcContainerState.Unknown, list[0].State);
        Assert.Null(list[0].CreatedAt);
        Assert.Null(list[0].StateChangedAt);

        Assert.Equal("proxy", list[1].Name);
        Assert.Equal("nginx:latest", list[1].Image);
        Assert.Equal("Exited (0)", list[1].Status);
    }

    [Fact]
    public async Task List_falls_back_to_table_on_malformed_json()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "not valid json");
        runner.Enqueue(0,
            "CONTAINER ID   NAME   IMAGE   CREATED   STATUS   PORTS\n" +
            "abc123         web    alpine  now       Up       \n");
        var svc = new WslcService(runner);

        var list = await svc.ListContainersAsync();

        Assert.Equal(2, runner.AllArgs.Count);
        Assert.Single(list);
        Assert.Equal("web", list[0].Name);
    }

    [Fact]
    public async Task List_returns_empty_when_json_and_fallback_both_fail()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "boom");
        runner.Enqueue(1, "", "boom again");
        var svc = new WslcService(runner);

        Assert.Empty(await svc.ListContainersAsync());
    }

    [Fact]
    public async Task List_never_throws_when_exe_missing()
    {
        var svc = new WslcService(new MissingExeRunner());
        Assert.Empty(await svc.ListContainersAsync());
    }

    // ── GetStatsAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task Stats_passes_all_and_json_format_flags()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "[]");
        var svc = new WslcService(runner);

        await svc.GetStatsAsync();

        Assert.Equal(new[] { "stats", "--all", "--format", "json" }, runner.LastArgs);
    }

    [Fact]
    public async Task Stats_parses_uppercase_ID_key()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, """
            [{"BlockIO":"0 B / 0 B","CPUPerc":"0.00%","ID":"d60f680b1d8b...","MemPerc":"0.00%","MemUsage":"0 B / 0 B","Name":"wcc-probe","NetIO":"0 B / 0 B","PIDs":0}]
            """);
        var svc = new WslcService(runner);

        var stats = await svc.GetStatsAsync();

        var s = Assert.Single(stats);
        Assert.Equal("d60f680b1d8b...", s.Id);
        Assert.Equal("wcc-probe", s.Name);
        Assert.Equal("0.00%", s.CpuPercent);
        Assert.Equal("0.00%", s.MemPercent);
        Assert.Equal("0 B / 0 B", s.MemUsage);
        Assert.Equal("0 B / 0 B", s.NetIO);
        Assert.Equal("0 B / 0 B", s.BlockIO);
        Assert.Equal(0, s.Pids);
    }

    [Fact]
    public async Task Stats_degrades_to_empty_on_nonzero_exit()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "boom");
        var svc = new WslcService(runner);
        Assert.Empty(await svc.GetStatsAsync());
    }

    [Fact]
    public async Task Stats_degrades_to_empty_on_malformed_json()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "not json");
        var svc = new WslcService(runner);
        Assert.Empty(await svc.GetStatsAsync());
    }

    // ── Lifecycle verb argv ──────────────────────────────────────────────────

    [Fact]
    public async Task Start_passes_id_only()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web");
        var svc = new WslcService(runner);

        var res = await svc.StartAsync("web");

        Assert.True(res.Ok);
        Assert.Equal(new[] { "start", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task Stop_without_timeout_omits_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web");
        var svc = new WslcService(runner);

        await svc.StopAsync("web");

        Assert.Equal(new[] { "stop", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task Stop_with_timeout_passes_t_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web");
        var svc = new WslcService(runner);

        await svc.StopAsync("web", timeoutSeconds: 10);

        Assert.Equal(new[] { "stop", "-t", "10", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task Kill_without_signal_omits_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web");
        var svc = new WslcService(runner);

        await svc.KillAsync("web");

        Assert.Equal(new[] { "kill", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task Kill_with_signal_passes_s_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web");
        var svc = new WslcService(runner);

        await svc.KillAsync("web", signal: "SIGKILL");

        Assert.Equal(new[] { "kill", "-s", "SIGKILL", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task Remove_without_force_omits_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web");
        var svc = new WslcService(runner);

        await svc.RemoveAsync("web");

        Assert.Equal(new[] { "remove", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task Remove_with_force_passes_f_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web");
        var svc = new WslcService(runner);

        await svc.RemoveAsync("web", force: true);

        Assert.Equal(new[] { "remove", "-f", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task PruneContainers_uses_container_prune_group_command()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcService(runner);

        await svc.PruneContainersAsync();

        Assert.Equal(new[] { "container", "prune" }, runner.LastArgs);
    }

    [Fact]
    public async Task Restart_composes_stop_then_start()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web"); // stop
        runner.Enqueue(0, "web"); // start
        var svc = new WslcService(runner);

        var res = await svc.RestartAsync("web", timeoutSeconds: 5);

        Assert.True(res.Ok);
        Assert.Equal(2, runner.AllArgs.Count);
        Assert.Equal(new[] { "stop", "-t", "5", "web" }, runner.AllArgs[0]);
        Assert.Equal(new[] { "start", "web" }, runner.AllArgs[1]);
    }

    [Fact]
    public async Task Restart_short_circuits_when_stop_fails()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "cannot stop");
        var svc = new WslcService(runner);

        var res = await svc.RestartAsync("web");

        Assert.False(res.Ok);
        Assert.Equal("cannot stop", res.StdErr);
        Assert.Single(runner.AllArgs); // start never invoked
    }

    [Fact]
    public async Task Logs_defaults_omit_tail_and_timestamp_flags()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "log line");
        var svc = new WslcService(runner);

        await svc.GetLogsAsync("web");

        Assert.Equal(new[] { "logs", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task Logs_passes_tail_and_timestamps_flags()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "log line");
        var svc = new WslcService(runner);

        await svc.GetLogsAsync("web", tailLines: 50, timestamps: true);

        Assert.Equal(new[] { "logs", "-n", "50", "-t", "web" }, runner.LastArgs);
    }

    [Fact]
    public async Task Exec_basic_argv_has_no_i_or_t_flags()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcService(runner);

        await svc.ExecAsync("web", new[] { "ls", "-la" });

        Assert.Equal(new[] { "exec", "web", "ls", "-la" }, runner.LastArgs);
    }

    [Fact]
    public async Task Exec_passes_user_and_workdir()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var svc = new WslcService(runner);

        await svc.ExecAsync("web", new[] { "whoami" }, user: "root", workdir: "/app");

        Assert.Equal(new[] { "exec", "-u", "root", "-w", "/app", "web", "whoami" }, runner.LastArgs);
    }

    [Fact]
    public async Task RunDetached_always_passes_detach_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web");
        var svc = new WslcService(runner);

        await svc.RunDetachedAsync(new WslcRunOptions("ubuntu"));

        Assert.Equal(new[] { "run", "--detach", "ubuntu" }, runner.LastArgs);
    }

    [Fact]
    public async Task RunDetached_full_options_build_expected_argv()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web");
        var svc = new WslcService(runner);

        var options = new WslcRunOptions(
            Image: "ubuntu",
            Name: "web",
            Command: new[] { "sleep", "300" },
            Env: new Dictionary<string, string> { ["FOO"] = "bar" },
            PublishedPorts: new[] { "8080:80" },
            Volumes: new[] { "myvol:/data" },
            Memory: "512m",
            Cpus: "1.5",
            Network: "bridge",
            Remove: true);

        await svc.RunDetachedAsync(options);

        Assert.Equal(new[]
        {
            "run", "--detach", "--name", "web", "-m", "512m", "--cpus", "1.5", "--network", "bridge",
            "--rm", "-e", "FOO=bar", "-p", "8080:80", "-v", "myvol:/data", "ubuntu", "sleep", "300",
        }, runner.LastArgs);
    }

    [Fact]
    public async Task Create_uses_create_verb_without_detach()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "web");
        var svc = new WslcService(runner);

        await svc.CreateAsync(new WslcRunOptions("ubuntu", Name: "web"));

        Assert.Equal(new[] { "create", "--name", "web", "ubuntu" }, runner.LastArgs);
    }

    [Fact]
    public async Task Inspect_passes_id_and_returns_raw_stdout()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, """[{"Id":"abc123","Image":"ubuntu"}]""");
        var svc = new WslcService(runner);

        var res = await svc.InspectAsync("web");

        Assert.True(res.Ok);
        Assert.Equal(new[] { "inspect", "web" }, runner.LastArgs);
        Assert.Contains("\"Id\":\"abc123\"", res.StdOut);
    }

    [Fact]
    public async Task Lifecycle_call_degrades_on_missing_exe()
    {
        var svc = new WslcService(new MissingExeRunner());
        var res = await svc.StartAsync("web");
        Assert.False(res.Ok);
        Assert.Contains("wslc.exe", res.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lifecycle_call_degrades_on_timeout()
    {
        var svc = new WslcService(new TimeoutRunner());
        var res = await svc.StopAsync("web");
        Assert.False(res.Ok);
        Assert.Contains("timed out", res.StdErr, StringComparison.OrdinalIgnoreCase);
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
