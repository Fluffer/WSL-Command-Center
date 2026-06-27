using Wsl.Core;
using Wsl.Core.Snapshots;
using Xunit;

namespace Wsl.Core.Tests;

public class WslSnapshotServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wslcc-snap-" + Guid.NewGuid().ToString("N"));

    private WslSnapshotService Build(FakeProcessRunner runner) => new(
        new WslBackupService(runner), new WslDistroService(runner), () => _root, runner);

    [Fact]
    public async Task Create_WritesVhdxSidecar_ThenListAndDelete()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ""); // export
        var svc = Build(runner);

        var snap = await svc.CreateAsync("Ubuntu", "before-upgrade", wslVersion: 2);
        // Simulate the export producing a file (the fake runner won't create it).
        File.WriteAllBytes(snap.VhdxPath, new byte[2048]);
        // Re-stamp sidecar size as the service would after a real export:
        Assert.True(File.Exists(snap.SidecarPath));

        var list = svc.List("Ubuntu");
        Assert.Single(list);
        Assert.Equal("before-upgrade", list[0].Label);

        svc.Delete(list[0]);
        Assert.Empty(svc.List("Ubuntu"));
    }

    [Fact]
    public async Task RestoreOverwrite_Throws_WhenDistroRunning()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ""); // export
        var svc = Build(runner);
        var snap = await svc.CreateAsync("Ubuntu", "x", 2);

        // ListAsync (running check) returns Ubuntu Running.
        runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Running  2\n");
        await Assert.ThrowsAsync<WslException>(() => svc.RestoreOverwriteAsync(snap, _root));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
