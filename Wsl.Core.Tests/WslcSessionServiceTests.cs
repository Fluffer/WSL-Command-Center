using System.ComponentModel;
using Wsl.Core;
using Wsl.Core.Containers;
using Xunit;

namespace Wsl.Core.Tests;

public class WslcSessionServiceTests : IDisposable
{
    private readonly string _sessionsRoot;

    public WslcSessionServiceTests()
    {
        _sessionsRoot = Path.Combine(Path.GetTempPath(), "wslc-session-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_sessionsRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sessionsRoot))
            Directory.Delete(_sessionsRoot, recursive: true);
    }

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

    // ── ListSessionsAsync / ParseSessionList ────────────────────────────────

    [Fact]
    public async Task List_parses_the_verified_table()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0,
            "[wslc] Found 1 session\n" +
            "ID   Creator PID   Display Name\n" +
            "1    6132          wslc-cli-peter\n");
        var svc = new WslcSessionService(runner, _sessionsRoot);

        var sessions = await svc.ListSessionsAsync();

        Assert.Single(sessions);
        Assert.Equal(1, sessions[0].Id);
        Assert.Equal(6132, sessions[0].CreatorPid);
        Assert.Equal("wslc-cli-peter", sessions[0].DisplayName);
        Assert.Equal(new[] { "system", "session", "list", "--verbose" }, runner.LastArgs);
    }

    [Fact]
    public async Task List_returns_empty_for_zero_sessions()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "[wslc] Found 0 sessions\nID   Creator PID   Display Name\n");
        var svc = new WslcSessionService(runner, _sessionsRoot);

        Assert.Empty(await svc.ListSessionsAsync());
    }

    [Fact]
    public async Task List_returns_empty_on_nonzero_exit()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "boom");
        var svc = new WslcSessionService(runner, _sessionsRoot);

        Assert.Empty(await svc.ListSessionsAsync());
    }

    [Fact]
    public async Task List_never_throws_when_exe_missing()
    {
        var svc = new WslcSessionService(new MissingExeRunner(), _sessionsRoot);
        Assert.Empty(await svc.ListSessionsAsync());
    }

    [Fact]
    public void ParseSessionList_skips_malformed_rows_without_losing_good_ones()
    {
        var stdout =
            "[wslc] Found 2 sessions\n" +
            "ID   Creator PID   Display Name\n" +
            "1    6132          wslc-cli-peter\n" +
            "garbled line with no columns\n" +
            "not-a-number   999   broken-id\n" +
            "2    7777          wslc-cli-second\n";

        var sessions = WslcSessionService.ParseSessionList(stdout);

        Assert.Equal(2, sessions.Count);
        Assert.Equal("wslc-cli-peter", sessions[0].DisplayName);
        Assert.Equal("wslc-cli-second", sessions[1].DisplayName);
    }

    [Fact]
    public void ParseSessionList_missing_header_does_not_swallow_first_data_row()
    {
        // No header at all — the first line is a real data row and must survive.
        var stdout = "1    6132          wslc-cli-peter\n";

        var sessions = WslcSessionService.ParseSessionList(stdout);

        Assert.Single(sessions);
        Assert.Equal("wslc-cli-peter", sessions[0].DisplayName);
    }

    // ── GetDiskUsage ─────────────────────────────────────────────────────────

    [Fact]
    public void GetDiskUsage_reports_unknown_for_missing_directory()
    {
        var svc = new WslcSessionService(new FakeProcessRunner(), _sessionsRoot);

        var usage = svc.GetDiskUsage("no-such-session");

        Assert.Null(usage.Storage);
        Assert.Null(usage.Swap);
        Assert.Equal("unknown", usage.TotalHumanReadable);
    }

    [Fact]
    public void GetDiskUsage_sums_known_files_and_reports_missing_ones_as_null()
    {
        var dir = Path.Combine(_sessionsRoot, "wslc-cli-peter");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "storage.vhdx"), new byte[1_000_000]); // no swap.vhdx written

        var svc = new WslcSessionService(new FakeProcessRunner(), _sessionsRoot);
        var usage = svc.GetDiskUsage("wslc-cli-peter");

        Assert.NotNull(usage.Storage);
        Assert.Equal(1_000_000, usage.Storage!.LogicalBytes);
        Assert.Null(usage.Swap);
        Assert.NotEqual("unknown", usage.TotalHumanReadable);
    }

    // ── FormatBytes ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData(0L, "0 B")]
    [InlineData(37_748_736L, "36 MB")]     // this machine's real wslc-cli-peter swap.vhdx size
    [InlineData(770_703_360L, "735 MB")]   // this machine's real wslc-cli-peter storage.vhdx size
    public void FormatBytes_renders_human_readable(long? bytes, string expected)
    {
        Assert.Equal(expected, WslcSessionService.FormatBytes(bytes));
    }

    // ── TerminateDefaultSessionAsync ────────────────────────────────────────

    [Fact]
    public async Task Terminate_passes_no_session_argument()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Session terminated.\n");
        var svc = new WslcSessionService(runner, _sessionsRoot);

        var result = await svc.TerminateDefaultSessionAsync();

        Assert.True(result.Success);
        Assert.Equal(new[] { "system", "session", "terminate" }, runner.LastArgs);
    }

    [Fact]
    public async Task Terminate_degrades_on_nonzero_exit()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "no session running");
        var svc = new WslcSessionService(runner, _sessionsRoot);

        var result = await svc.TerminateDefaultSessionAsync();

        Assert.False(result.Success);
        Assert.Contains("no session running", result.Message);
    }

    [Fact]
    public async Task Terminate_degrades_on_missing_exe()
    {
        var svc = new WslcSessionService(new MissingExeRunner(), _sessionsRoot);
        var result = await svc.TerminateDefaultSessionAsync();
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Terminate_degrades_on_timeout()
    {
        var svc = new WslcSessionService(new TimeoutRunner(), _sessionsRoot);
        var result = await svc.TerminateDefaultSessionAsync();
        Assert.False(result.Success);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── ReclaimAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Reclaim_terminates_the_default_session_first()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Session terminated.\n");
        var svc = new WslcSessionService(runner, _sessionsRoot);

        var result = await svc.ReclaimAsync("no-such-session");

        Assert.Equal(new[] { "system", "session", "terminate" }, runner.LastArgs);
        Assert.False(result.MarkedSparse); // nothing on disk for this session
        Assert.False(result.Success);
        Assert.True(result.RequiresElevationToCompact);
    }

    [Fact]
    public async Task Reclaim_aborts_and_leaves_disk_untouched_when_terminate_fails()
    {
        var dir = Path.Combine(_sessionsRoot, "wslc-cli-peter");
        Directory.CreateDirectory(dir);
        var storagePath = Path.Combine(dir, "storage.vhdx");
        File.WriteAllBytes(storagePath, new byte[1024]);

        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "terminate failed");
        var svc = new WslcSessionService(runner, _sessionsRoot);

        var result = await svc.ReclaimAsync("wslc-cli-peter");

        Assert.False(result.Success);
        Assert.False(result.MarkedSparse);
        Assert.Contains("Reclaim aborted", result.Message);
        Assert.False((File.GetAttributes(storagePath) & FileAttributes.SparseFile) != 0);
    }

    [Fact]
    public async Task Reclaim_marks_existing_vhds_sparse_after_successful_terminate()
    {
        var dir = Path.Combine(_sessionsRoot, "wslc-cli-peter");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "storage.vhdx"), new byte[1024]);

        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "Session terminated.\n");
        var svc = new WslcSessionService(runner, _sessionsRoot);

        var result = await svc.ReclaimAsync("wslc-cli-peter");

        Assert.True(result.Success);
        Assert.True(result.MarkedSparse);
        Assert.True(result.RequiresElevationToCompact);
    }
}
