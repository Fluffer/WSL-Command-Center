using System.Linq;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Wsl.Core.Snapshots;
using Xunit;

namespace Wsl.Core.Tests;

public class SnapshotViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wslcc-svm-" + Guid.NewGuid().ToString("N"));

    private WslSnapshotService Build(FakeProcessRunner runner) =>
        new WslSnapshotService(new WslDistroService(runner), () => _root, runner,
            new StatePreservingExport(new WslDistroService(runner)));

    [Fact]
    public async Task RunningDistrosAsync_ReturnsRunning()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n  Debian  Stopped  2\n");
        var vm = new SnapshotViewModel(Build(runner), new WslDistroService(runner));
        Assert.Equal(new[] { "Ubuntu" }, await vm.RunningDistrosAsync());
    }

    [Fact]
    public async Task CreateThenLoad_ShowsSnapshot()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n"); // LoadAsync distro list
        runner.Enqueue(0, ""); // export
        var svc = new WslSnapshotService(new WslDistroService(runner), () => _root, runner,
            new StatePreservingExport(new WslDistroService(runner)));
        var vm = new SnapshotViewModel(svc, new WslDistroService(runner));

        await vm.LoadAsync();
        vm.SelectedDistro = "Ubuntu";
        vm.NewLabel = "snap1";
        await vm.CreateAsync();

        Assert.Contains(vm.Snapshots, s => s.Label == "snap1");
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
