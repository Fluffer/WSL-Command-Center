using Wsl.Contracts;
using Wsl.Core;

namespace Wsl.Broker;

/// <summary>Maps each typed BrokerRequest to its privileged command(s). No arbitrary passthrough.</summary>
public class PrivilegedOperations
{
    private readonly IProcessRunner _runner;

    public PrivilegedOperations(IProcessRunner runner) => _runner = runner;

    public Task<BrokerResponse> HandleAsync(BrokerRequest request, CancellationToken ct = default)
        => request switch
        {
            CheckWslInstalledRequest => CheckInstalled(ct),
            EnableFeaturesRequest => EnableFeatures(ct),
            InstallOrUpdateKernelRequest => InstallKernel(ct),
            SetDefaultWslVersionRequest r => SetDefaultVersion(r.Version, ct),
            _ => Task.FromResult(new BrokerResponse(false, $"Unknown request: {request.GetType().Name}"))
        };

    private async Task<BrokerResponse> CheckInstalled(CancellationToken ct)
    {
        var r = await _runner.RunAsync("wsl.exe", new[] { "--version" }, null, ct);
        return r.ExitCode == 0
            ? new BrokerResponse(true, Detail: "installed")
            : new BrokerResponse(true, Detail: "absent");
    }

    private async Task<BrokerResponse> EnableFeatures(CancellationToken ct)
    {
        var vmp = await _runner.RunAsync("dism.exe", new[]
        {
            "/online", "/enable-feature", "/featurename:VirtualMachinePlatform",
            "/all", "/norestart"
        }, null, ct);
        if (vmp.ExitCode != 0 && vmp.ExitCode != 3010)
            return Fail(vmp, "Enable VirtualMachinePlatform");

        var wsl = await _runner.RunAsync("dism.exe", new[]
        {
            "/online", "/enable-feature", "/featurename:Microsoft-Windows-Subsystem-Linux",
            "/all", "/norestart"
        }, null, ct);
        if (wsl.ExitCode != 0 && wsl.ExitCode != 3010)
            return Fail(wsl, "Enable WSL feature");

        // DISM exit 3010 = success, reboot required. Always require reboot after enabling.
        return new BrokerResponse(true, RebootRequired: true, Detail: "features enabled");
    }

    private async Task<BrokerResponse> InstallKernel(CancellationToken ct)
    {
        var r = await _runner.RunAsync("wsl.exe", new[] { "--update" }, null, ct);
        return r.ExitCode == 0
            ? new BrokerResponse(true, Detail: "kernel updated")
            : Fail(r, "Install/update kernel");
    }

    private async Task<BrokerResponse> SetDefaultVersion(int version, CancellationToken ct)
    {
        var r = await _runner.RunAsync("wsl.exe",
            new[] { "--set-default-version", version.ToString() }, null, ct);
        return r.ExitCode == 0
            ? new BrokerResponse(true, Detail: $"default version {version}")
            : Fail(r, "Set default version");
    }

    private static BrokerResponse Fail(ProcessResult r, string op)
    {
        var msg = string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr;
        return new BrokerResponse(false, $"{op} failed: {msg.Trim()}", Detail: $"exit {r.ExitCode}");
    }
}
