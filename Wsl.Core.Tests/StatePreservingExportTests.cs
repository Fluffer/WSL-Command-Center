using Wsl.Core;
using Xunit;

namespace Wsl.Core.Tests;

public class StatePreservingExportTests
{
    // " NAME STATE VERSION " verbose output: Ubuntu running, Debian stopped.
    private const string ListVerbose =
        "  NAME      STATE     VERSION\n* Ubuntu    Running   2\n  Debian    Stopped   2\n";

    [Fact]
    public async Task RunAsync_Shuts_Down_Then_Exports_Then_Restarts_Only_Running()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListVerbose); // ListAsync inside RunningAsync
        var sp = new StatePreservingExport(new WslDistroService(runner));

        var exportCalls = 0;
        var restored = await sp.RunAsync(_ => { exportCalls++; return Task.CompletedTask; });

        Assert.Equal(1, exportCalls);
        Assert.Equal(new[] { "Ubuntu" }, restored); // only the running one
        var flat = runner.AllArgs;
        // order: --list --verbose, --shutdown, then -d Ubuntu -- true
        Assert.Contains(flat, a => a.Length >= 2 && a[0] == "--list" && a[1] == "--verbose");
        Assert.Contains(flat, a => a.Length == 1 && a[0] == "--shutdown");
        Assert.Contains(flat, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Ubuntu");
        // Debian was stopped -> never restarted
        Assert.DoesNotContain(flat, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Debian");
    }

    [Fact]
    public async Task RunAsync_Restarts_Even_When_Export_Throws()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListVerbose);
        var sp = new StatePreservingExport(new WslDistroService(runner));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sp.RunAsync(_ => throw new InvalidOperationException("boom")));

        // finally still restarted Ubuntu
        Assert.Contains(runner.AllArgs, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Ubuntu");
    }

    [Fact]
    public async Task RunningAsync_Returns_Only_Running()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ListVerbose);
        var sp = new StatePreservingExport(new WslDistroService(runner));
        Assert.Equal(new[] { "Ubuntu" }, await sp.RunningAsync());
    }

    [Fact]
    public async Task RunAsync_BestEffortRestart_ContinuesDespiteFirstStartThrows()
    {
        const string bothRunning =
            "  NAME      STATE     VERSION\n* Ubuntu    Running   2\n  Debian    Running   2\n";

        var runner = new FakeProcessRunner();
        runner.Enqueue(0, bothRunning);  // ListAsync (--list --verbose)
        runner.Enqueue(0, "");           // ShutdownAsync (--shutdown)
        // export delegate is a no-op lambda; no runner call
        runner.Enqueue(1, "");           // StartAsync Ubuntu → non-zero → WslException (swallowed)
        runner.Enqueue(0, "");           // StartAsync Debian → success

        var sp = new StatePreservingExport(new WslDistroService(runner));
        // Export succeeds, first restart throws, second must still run → no exception propagates.
        await sp.RunAsync(_ => Task.CompletedTask);

        var flat = runner.AllArgs;
        Assert.Contains(flat, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Ubuntu");
        Assert.Contains(flat, a => a.Length >= 2 && a[0] == "-d" && a[1] == "Debian");
    }
}
