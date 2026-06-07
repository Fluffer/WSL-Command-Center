using Wsl.Broker;
using Wsl.Contracts;
using Xunit;

namespace Wsl.Core.Tests;

public sealed class FakeDiskEnumerator : IDiskEnumerator
{
    private readonly IReadOnlyList<DiskInfo> _disks;
    public FakeDiskEnumerator(params DiskInfo[] disks) => _disks = disks;
    public IReadOnlyList<DiskInfo> Enumerate() => _disks;
}

public sealed class ThrowingDiskEnumerator : IDiskEnumerator
{
    public IReadOnlyList<DiskInfo> Enumerate() => throw new InvalidOperationException("WMI unavailable");
}

public class DiskBrokerOperationsTests
{
    private static readonly DiskInfo SystemDisk =
        new(@"\\.\PHYSICALDRIVE0", "Samsung SSD 990 PRO", "S7XYNL0X", 2_000_398_934_016, IsSystem: true);

    private static readonly DiskInfo DataDisk =
        new(@"\\.\PHYSICALDRIVE2", "WD Red 4TB", "WD-WCC7K1234567", 4_000_787_030_016, IsSystem: false);

    private static PrivilegedOperations Make(FakeProcessRunner runner)
        => new(runner, new FakeDiskEnumerator(SystemDisk, DataDisk));

    [Fact]
    public async Task MountDisk_fails_closed_when_enumeration_unavailable()
    {
        var runner = new FakeProcessRunner();
        var ops = new PrivilegedOperations(runner, new ThrowingDiskEnumerator());

        var resp = await ops.HandleAsync(new MountDiskRequest(
            @"\\.\PHYSICALDRIVE2", Vhd: false, Bare: true, null, null, null, null));

        Assert.False(resp.Success);
        Assert.Null(runner.LastExe); // wsl.exe never invoked
    }

    [Fact]
    public async Task MountDisk_composes_full_arg_set()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var ops = Make(runner);

        var resp = await ops.HandleAsync(new MountDiskRequest(
            @"\\.\PHYSICALDRIVE2", Vhd: false, Bare: false,
            Partition: 1, Type: "ext4", Options: "ro", Name: "data"));

        Assert.True(resp.Success);
        Assert.Equal(new[] { "--mount", @"\\.\PHYSICALDRIVE2", "--partition", "1",
            "--type", "ext4", "--options", "ro", "--name", "data" }, runner.LastArgs);
    }

    [Fact]
    public async Task MountDisk_vhd_appends_vhd_flag()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var ops = Make(runner);

        var resp = await ops.HandleAsync(new MountDiskRequest(
            @"D:\disks\data.vhdx", Vhd: true, Bare: false,
            Partition: null, Type: null, Options: null, Name: null));

        Assert.True(resp.Success);
        Assert.Equal(new[] { "--mount", @"D:\disks\data.vhdx", "--vhd" }, runner.LastArgs);
    }

    [Fact]
    public async Task MountDisk_bare_suppresses_mount_only_options()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var ops = Make(runner);

        var resp = await ops.HandleAsync(new MountDiskRequest(
            @"\\.\PHYSICALDRIVE2", Vhd: false, Bare: true,
            Partition: 1, Type: "ext4", Options: "ro", Name: "data"));

        Assert.True(resp.Success);
        Assert.Equal(new[] { "--mount", @"\\.\PHYSICALDRIVE2", "--bare" }, runner.LastArgs);
    }

    [Fact]
    public async Task MountDisk_refuses_system_disk()
    {
        var runner = new FakeProcessRunner();
        var ops = Make(runner);

        var resp = await ops.HandleAsync(new MountDiskRequest(
            @"\\.\PHYSICALDRIVE0", Vhd: false, Bare: false,
            Partition: null, Type: null, Options: null, Name: null));

        Assert.False(resp.Success);
        Assert.Contains("system disk", resp.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(runner.LastExe); // wsl.exe never invoked
    }

    [Fact]
    public async Task UnmountDisk_without_disk_unmounts_all()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var ops = Make(runner);

        var resp = await ops.HandleAsync(new UnmountDiskRequest(null));

        Assert.True(resp.Success);
        Assert.Equal(new[] { "--unmount" }, runner.LastArgs);
    }

    [Fact]
    public async Task UnmountDisk_with_disk_passes_device()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(0, "");
        var ops = Make(runner);

        var resp = await ops.HandleAsync(new UnmountDiskRequest(@"\\.\PHYSICALDRIVE2"));

        Assert.True(resp.Success);
        Assert.Equal(new[] { "--unmount", @"\\.\PHYSICALDRIVE2" }, runner.LastArgs);
    }

    [Fact]
    public async Task ListDisks_returns_enumerated_disks_with_system_flag()
    {
        var ops = Make(new FakeProcessRunner());

        var resp = await ops.HandleAsync(new ListDisksRequest());

        Assert.True(resp.Success);
        Assert.NotNull(resp.Disks);
        Assert.Equal(2, resp.Disks!.Count);
        Assert.True(resp.Disks[0].IsSystem);
        Assert.Equal(@"\\.\PHYSICALDRIVE2", resp.Disks[1].DeviceId);
        Assert.False(resp.Disks[1].IsSystem);
    }

    [Fact]
    public async Task MountDisk_failure_surfaces_stderr()
    {
        var runner = new FakeProcessRunner();
        runner.Enqueue(1, "", "The system cannot find the file specified.");
        var ops = Make(runner);

        var resp = await ops.HandleAsync(new MountDiskRequest(
            @"\\.\PHYSICALDRIVE2", Vhd: false, Bare: false,
            Partition: null, Type: null, Options: null, Name: null));

        Assert.False(resp.Success);
        Assert.Contains("cannot find", resp.Error);
    }
}
