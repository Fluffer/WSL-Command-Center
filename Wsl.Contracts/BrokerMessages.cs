using System.Text.Json.Serialization;

namespace Wsl.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CheckWslInstalledRequest), "checkInstalled")]
[JsonDerivedType(typeof(EnableFeaturesRequest), "enableFeatures")]
[JsonDerivedType(typeof(InstallOrUpdateKernelRequest), "installKernel")]
[JsonDerivedType(typeof(SetDefaultWslVersionRequest), "setDefaultVersion")]
public abstract record BrokerRequest;

public record CheckWslInstalledRequest() : BrokerRequest;
public record EnableFeaturesRequest() : BrokerRequest;
public record InstallOrUpdateKernelRequest(bool PreRelease = false) : BrokerRequest;
public record SetDefaultWslVersionRequest(int Version) : BrokerRequest;

public record BrokerResponse(
    bool Success,
    string? Error = null,
    bool RebootRequired = false,
    string? Detail = null);
