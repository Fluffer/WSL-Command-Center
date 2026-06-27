using System.Linq;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Wsl.Core.Monitoring;
using Xunit;

namespace Wsl.Core.Tests;

public class MonitorViewModelTests
{
    [Fact]
    public async Task RefreshAsync_PopulatesVmAndRunningRows()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME      STATE     VERSION\n* Ubuntu    Running   2\n  Debian    Stopped   2\n"); // ListAsync
        runner.Enqueue(0,
            "MemTotal: 8000000 kB\nMemAvailable: 2000000 kB\n---\ncpu 100 0 100 800 0 0 0 0 0 0\n---\n/dev/sdc 1073741824 268435456 805306368 25% /\n"); // Ubuntu sample
        var monitor = new WslMonitorService(runner, new ZeroVmProbe(), new ZeroVhdxProbe());
        var vm = new MonitorViewModel(monitor, new WslDistroService(runner));

        await vm.RefreshAsync();

        Assert.NotNull(vm.Vm);
        Assert.Single(vm.Rows);
        Assert.Equal("Ubuntu", vm.Rows.First().Name);
    }

    private sealed class ZeroVmProbe : IVmProcessProbe { public (double, long) Read() => (0, 0); }
    private sealed class ZeroVhdxProbe : IVhdxSizeProbe { public long TotalBytes() => 0; }
}
