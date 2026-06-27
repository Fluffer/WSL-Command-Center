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
[JsonDerivedType(typeof(LaunchDebugShellRequest), "launchDebugShell")]
[JsonDerivedType(typeof(UninstallWslRequest), "uninstallWsl")]
[JsonDerivedType(typeof(DeletePortProxyRequest), "deletePortProxy")]
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

/// <summary>Open the WSL2 debug shell (<c>wsl.exe --debug-shell</c>) in its own console
/// window. Elevation-required diagnostics console for the WSL2 utility VM.</summary>
public record LaunchDebugShellRequest() : BrokerRequest;

/// <summary>Uninstall the WSL package itself (<c>wsl.exe --uninstall</c>). Removes the WSL
/// platform from the machine; every installed distribution stops working. NOT the same as
/// unregistering a single distribution.</summary>
public record UninstallWslRequest() : BrokerRequest;

/// <summary>Remove a netsh portproxy v4tov4 rule. Requires admin, hence the broker.</summary>
public record DeletePortProxyRequest(string ListenAddress, int ListenPort) : BrokerRequest;

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
