using System.Text.Json;
using Wsl.Contracts;
using Xunit;

namespace Wsl.Core.Tests;

public class ContractsSerializationTests
{
    private static readonly JsonSerializerOptions Opts =
        new() { TypeInfoResolver = BrokerJsonContext.Default };

    [Fact]
    public void Roundtrips_polymorphic_request()
    {
        BrokerRequest req = new SetDefaultWslVersionRequest(2);
        var json = JsonSerializer.Serialize(req, Opts);
        var back = JsonSerializer.Deserialize<BrokerRequest>(json, Opts);
        var typed = Assert.IsType<SetDefaultWslVersionRequest>(back);
        Assert.Equal(2, typed.Version);
    }

    [Fact]
    public void Roundtrips_each_request_type()
    {
        BrokerRequest[] all =
        {
            new CheckWslInstalledRequest(),
            new EnableFeaturesRequest(),
            new InstallOrUpdateKernelRequest(),
            new SetDefaultWslVersionRequest(2),
            new ListDisksRequest(),
            new MountDiskRequest(@"\\.\PHYSICALDRIVE2"),
            new UnmountDiskRequest(null),
        };
        foreach (var req in all)
        {
            var json = JsonSerializer.Serialize(req, Opts);
            var back = JsonSerializer.Deserialize<BrokerRequest>(json, Opts);
            Assert.Equal(req.GetType(), back!.GetType());
        }
    }

    [Fact]
    public void Roundtrips_prerelease_flag_on_kernel_update()
    {
        BrokerRequest req = new InstallOrUpdateKernelRequest(PreRelease: true);
        var json = JsonSerializer.Serialize(req, Opts);
        var back = JsonSerializer.Deserialize<BrokerRequest>(json, Opts);
        var typed = Assert.IsType<InstallOrUpdateKernelRequest>(back);
        Assert.True(typed.PreRelease);
    }

    [Fact]
    public void Kernel_update_request_defaults_to_stable_channel()
    {
        var back = JsonSerializer.Deserialize<BrokerRequest>(
            """{"$type":"installKernel"}""", Opts);
        var typed = Assert.IsType<InstallOrUpdateKernelRequest>(back);
        Assert.False(typed.PreRelease);
    }

    [Fact]
    public void Roundtrips_mount_disk_request_with_all_options()
    {
        BrokerRequest req = new MountDiskRequest(
            @"\\.\PHYSICALDRIVE2", Vhd: false, Bare: true,
            Partition: 1, Type: "ext4", Options: "ro", Name: "data");
        var json = JsonSerializer.Serialize(req, Opts);
        var back = JsonSerializer.Deserialize<BrokerRequest>(json, Opts);
        var typed = Assert.IsType<MountDiskRequest>(back);
        Assert.Equal(@"\\.\PHYSICALDRIVE2", typed.Disk);
        Assert.True(typed.Bare);
        Assert.Equal(1, typed.Partition);
        Assert.Equal("ext4", typed.Type);
        Assert.Equal("ro", typed.Options);
        Assert.Equal("data", typed.Name);
    }

    [Fact]
    public void Roundtrips_response_with_disk_payload()
    {
        var resp = new BrokerResponse(true, Disks: new[]
        {
            new DiskInfo(@"\\.\PHYSICALDRIVE0", "Samsung SSD", "S7XYNL0X", 2_000_398_934_016, IsSystem: true),
            new DiskInfo(@"\\.\PHYSICALDRIVE2", "WD Red", "WD-WCC7K", 4_000_787_030_016, IsSystem: false),
        });
        var json = JsonSerializer.Serialize(resp, Opts);
        var back = JsonSerializer.Deserialize<BrokerResponse>(json, Opts);
        Assert.NotNull(back!.Disks);
        Assert.Equal(2, back.Disks!.Count);
        Assert.True(back.Disks[0].IsSystem);
        Assert.Equal("WD Red", back.Disks[1].Model);
        Assert.Equal(4_000_787_030_016, back.Disks[1].SizeBytes);
    }

    [Fact]
    public void Roundtrips_response()
    {
        var resp = new BrokerResponse(true, null, RebootRequired: true, "done");
        var json = JsonSerializer.Serialize(resp, Opts);
        var back = JsonSerializer.Deserialize<BrokerResponse>(json, Opts);
        Assert.True(back!.Success);
        Assert.True(back.RebootRequired);
        Assert.Equal("done", back.Detail);
    }
}
