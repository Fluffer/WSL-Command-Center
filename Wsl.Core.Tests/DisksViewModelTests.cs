using Wsl.App.Logic.ViewModels;
using Wsl.Contracts;
using Xunit;

namespace Wsl.Core.Tests;

public class DisksViewModelTests
{
    private static readonly DiskInfo SystemDisk =
        new(@"\\.\PHYSICALDRIVE0", "Samsung SSD 990 PRO", "S7XYNL0X", 2_000_398_934_016, IsSystem: true);

    private static readonly DiskInfo DataDisk =
        new(@"\\.\PHYSICALDRIVE2", "WD Red 4TB", "WD-WCC7K1234567", 4_000_787_030_016, IsSystem: false);

    [Fact]
    public async Task LoadDisks_populates_rows_from_broker_payload()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(true, Disks: new[] { SystemDisk, DataDisk }));
        var vm = new DisksViewModel(client);

        await vm.LoadDisksAsync();

        Assert.IsType<ListDisksRequest>(client.Sent[0]);
        Assert.Equal(2, vm.Disks.Count);
        Assert.True(vm.Disks[0].IsSystem);
        Assert.False(vm.Disks[0].CanMount);
        Assert.Equal("PHYSICALDRIVE2", vm.Disks[1].ShortName);
        Assert.True(vm.Disks[1].CanMount);
    }

    [Fact]
    public async Task LoadDisks_failure_surfaces_error()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(false, "Access is denied."));
        var vm = new DisksViewModel(client);

        await vm.LoadDisksAsync();

        Assert.Equal("Access is denied.", vm.ErrorMessage);
        Assert.Empty(vm.Disks);
    }

    [Fact]
    public async Task Mount_sends_composed_request_and_reports_success()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(true));
        var vm = new DisksViewModel(client);

        await vm.MountAsync(@"\\.\PHYSICALDRIVE2", vhd: false, bare: false,
            partition: 1, type: "ext4", options: "ro", name: "data");

        var req = Assert.IsType<MountDiskRequest>(client.Sent[0]);
        Assert.Equal(@"\\.\PHYSICALDRIVE2", req.Disk);
        Assert.False(req.Vhd);
        Assert.False(req.Bare);
        Assert.Equal(1, req.Partition);
        Assert.Equal("ext4", req.Type);
        Assert.Equal("ro", req.Options);
        Assert.Equal("data", req.Name);
        Assert.NotNull(vm.StatusMessage);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Mount_normalizes_blank_optionals_to_null()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(true));
        var vm = new DisksViewModel(client);

        await vm.MountAsync(@"D:\disks\data.vhdx", vhd: true, bare: false,
            partition: null, type: "  ", options: "", name: null);

        var req = Assert.IsType<MountDiskRequest>(client.Sent[0]);
        Assert.True(req.Vhd);
        Assert.Null(req.Type);
        Assert.Null(req.Options);
        Assert.Null(req.Name);
    }

    [Fact]
    public async Task Mount_failure_surfaces_error()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(false, "Mount disk failed: bad partition"));
        var vm = new DisksViewModel(client);

        await vm.MountAsync(@"\\.\PHYSICALDRIVE2", vhd: false, bare: false,
            partition: 9, type: null, options: null, name: null);

        Assert.Contains("bad partition", vm.ErrorMessage);
        Assert.Null(vm.StatusMessage);
    }

    [Fact]
    public async Task UnmountAll_sends_request_without_disk()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(true));
        var vm = new DisksViewModel(client);

        await vm.UnmountAllAsync();

        var req = Assert.IsType<UnmountDiskRequest>(client.Sent[0]);
        Assert.Null(req.Disk);
        Assert.NotNull(vm.StatusMessage);
    }

    [Fact]
    public async Task Unmount_specific_disk_passes_device()
    {
        var client = new FakeBrokerClient();
        client.Enqueue(new BrokerResponse(true));
        var vm = new DisksViewModel(client);

        await vm.UnmountAsync(@"\\.\PHYSICALDRIVE2");

        var req = Assert.IsType<UnmountDiskRequest>(client.Sent[0]);
        Assert.Equal(@"\\.\PHYSICALDRIVE2", req.Disk);
    }

    [Fact]
    public void DiskRow_formats_size_in_gigabytes()
    {
        var row = new DiskRow(DataDisk);
        Assert.Contains("GB", row.SizeText);
        Assert.StartsWith("3726", row.SizeText.Replace(",", "."));
    }
}
