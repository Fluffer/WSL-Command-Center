using Wsl.Core;
using Wsl.Core.Snapshots;
using Xunit;

namespace Wsl.Core.Tests;

public class WslSnapshotServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wslcc-snap-" + Guid.NewGuid().ToString("N"));

    private WslSnapshotService Build(FakeProcessRunner runner) => new(
        new WslDistroService(runner), () => _root, runner);

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

    [Fact]
    public async Task RestoreOverwrite_Throws_AndDoesNotUnregister_WhenSnapshotFileMissing()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, ""); // export
        var svc = Build(runner);
        var snap = await svc.CreateAsync("Ubuntu", "x", 2);
        // VhdxPath does NOT exist — fake runner never creates files.

        // ListAsync (running check) returns Ubuntu Stopped.
        runner.Enqueue(0, "  NAME    STATE    VERSION\n* Ubuntu  Stopped  2\n");

        await Assert.ThrowsAsync<WslException>(() => svc.RestoreOverwriteAsync(snap, _root));
        Assert.DoesNotContain("--unregister", runner.AllArgs.SelectMany(a => a));
    }

    [Fact]
    public async Task Sanitize_Rejected_ForTraversalNames()
    {
        var runner = new FakeProcessRunner();
        var svc = Build(runner);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync("..", "x", 2));
    }

    [Fact]
    public void Delete_IgnoresPathsOutsideStoreRoot()
    {
        var runner = new FakeProcessRunner();
        var svc = Build(runner);
        var outsideFile = Path.Combine(Path.GetTempPath(),
            "wslcc-outside-" + Guid.NewGuid().ToString("N") + ".vhdx");
        File.WriteAllBytes(outsideFile, new byte[1024]);
        try
        {
            var snap = new Snapshot("Ubuntu", "test", DateTime.UtcNow, 1024, "vhd", 2,
                outsideFile, outsideFile + ".json");
            svc.Delete(snap);
            Assert.True(File.Exists(outsideFile), "File outside store root must not be deleted");
        }
        finally
        {
            if (File.Exists(outsideFile)) File.Delete(outsideFile);
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
