using System.Linq;
using Wsl.App.Logic.ViewModels;
using Wsl.Core;
using Wsl.Core.Snapshots;
using Xunit;

namespace Wsl.Core.Tests;

public class SnapshotViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wslcc-svm-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateThenLoad_ShowsSnapshot()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n"); // LoadAsync distro list
        runner.Enqueue(0, ""); // export
        var svc = new WslSnapshotService(new WslDistroService(runner), () => _root, runner);
        var vm = new SnapshotViewModel(svc, new WslDistroService(runner));

        await vm.LoadAsync();
        vm.SelectedDistro = "Ubuntu";
        vm.NewLabel = "snap1";
        await vm.CreateAsync();

        Assert.Contains(vm.Snapshots, s => s.Label == "snap1");
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
