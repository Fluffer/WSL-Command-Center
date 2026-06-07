using System.Text.Json.Serialization;

namespace Wsl.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CheckWslInstalledRequest), "checkInstalled")]
[JsonDerivedType(typeof(EnableFeaturesRequest), "enableFeatures")]
[JsonDerivedType(typeof(InstallOrUpdateKernelRequest), "installKernel")]
[JsonDerivedType(typeof(SetDefaultWslVersionRequest), "setDefaultVersion")]
[JsonDerivedType(typeof(ListDisksRequest), "listDisks")]
[JsonDerivedType(typeof(MountDiskRequest), "mountDisk")]
[JsonDerivedType(typeof(UnmountDiskRequest), "unmountDisk")]
public abstract record BrokerRequest;

public record CheckWslInstalledRequest() : BrokerRequest;
public record EnableFeaturesRequest() : BrokerRequest;
public record InstallOrUpdateKernelRequest(bool PreRelease = false) : BrokerRequest;
public record SetDefaultWslVersionRequest(int Version) : BrokerRequest;
public record ListDisksRequest() : BrokerRequest;

/// <summary>Mount a physical disk (<c>\\.\PHYSICALDRIVEn</c>) or, when <paramref name="Vhd"/>
/// is set, a virtual disk file into WSL2. <paramref name="Bare"/> attaches without mounting,
/// which makes the mount-only options (partition/type/options/name) inapplicable.</summary>
public record MountDiskRequest(
    string Disk,
    bool Vhd = false,
    bool Bare = false,
    int? Partition = null,
    string? Type = null,
    string? Options = null,
    string? Name = null) : BrokerRequest;

/// <summary>Unmount one disk, or every mounted disk when <paramref name="Disk"/> is null.</summary>
public record UnmountDiskRequest(string? Disk) : BrokerRequest;

public record DiskInfo(
    string DeviceId,
    string Model,
    string SerialNumber,
    long SizeBytes,
    bool IsSystem);

public record BrokerResponse(
    bool Success,
    string? Error = null,
    bool RebootRequired = false,
    string? Detail = null,
    IReadOnlyList<DiskInfo>? Disks = null);
