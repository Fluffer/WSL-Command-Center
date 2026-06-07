using Wsl.Contracts;
using Wsl.Core;

namespace Wsl.Broker;

/// <summary>Maps each typed BrokerRequest to its privileged command(s). No arbitrary passthrough.</summary>
public class PrivilegedOperations
{
    private readonly IProcessRunner _runner;
    private readonly IDiskEnumerator _disks;

    public PrivilegedOperations(IProcessRunner runner, IDiskEnumerator? disks = null)
    {
        _runner = runner;
        _disks = disks ?? new WmiDiskEnumerator();
    }

    public Task<BrokerResponse> HandleAsync(BrokerRequest request, CancellationToken ct = default)
        => request switch
        {
            CheckWslInstalledRequest => CheckInstalled(ct),
            EnableFeaturesRequest => EnableFeatures(ct),
            InstallOrUpdateKernelRequest r => InstallKernel(r.PreRelease, ct),
            SetDefaultWslVersionRequest r => SetDefaultVersion(r.Version, ct),
            ListDisksRequest => Task.FromResult(ListDisks()),
            MountDiskRequest r => MountDisk(r, ct),
            UnmountDiskRequest r => UnmountDisk(r.Disk, ct),
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

    private async Task<BrokerResponse> InstallKernel(bool preRelease, CancellationToken ct)
    {
        var args = preRelease
            ? new[] { "--update", "--pre-release" }
            : new[] { "--update" };
        var r = await _runner.RunAsync("wsl.exe", args, null, ct);
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

    private BrokerResponse ListDisks()
    {
        try
        {
            var disks = _disks.Enumerate();
            return new BrokerResponse(true, Detail: $"{disks.Count} disks", Disks: disks);
        }
        catch (Exception ex)
        {
            return new BrokerResponse(false, $"List disks failed: {ex.Message}");
        }
    }

    private async Task<BrokerResponse> MountDisk(MountDiskRequest req, CancellationToken ct)
    {
        // Defense in depth: the UI already disables the system disk row, but the broker is the
        // privileged boundary, so it re-checks against the same enumeration. Fail closed: if the
        // disk cannot be verified, a physical mount is refused rather than waved through.
        if (!req.Vhd)
        {
            var isSystem = IsSystemDisk(req.Disk);
            if (isSystem is null)
                return new BrokerResponse(false, "Could not verify the disk against the system disk; refusing to mount.");
            if (isSystem == true)
                return new BrokerResponse(false, $"Refusing to mount the system disk ({req.Disk}).");
        }

        var args = new List<string> { "--mount", req.Disk };
        if (req.Vhd) args.Add("--vhd");
        if (req.Bare)
        {
            args.Add("--bare"); // attach-only: partition/type/options/name are not applicable
        }
        else
        {
            if (req.Partition is not null) { args.Add("--partition"); args.Add(req.Partition.Value.ToString()); }
            if (!string.IsNullOrWhiteSpace(req.Type)) { args.Add("--type"); args.Add(req.Type); }
            if (!string.IsNullOrWhiteSpace(req.Options)) { args.Add("--options"); args.Add(req.Options); }
            if (!string.IsNullOrWhiteSpace(req.Name)) { args.Add("--name"); args.Add(req.Name); }
        }

        var r = await _runner.RunAsync("wsl.exe", args.ToArray(), null, ct);
        return r.ExitCode == 0
            ? new BrokerResponse(true, Detail: $"mounted {req.Disk}")
            : Fail(r, "Mount disk");
    }

    private async Task<BrokerResponse> UnmountDisk(string? disk, CancellationToken ct)
    {
        var args = string.IsNullOrWhiteSpace(disk)
            ? new[] { "--unmount" }
            : new[] { "--unmount", disk };
        var r = await _runner.RunAsync("wsl.exe", args, null, ct);
        return r.ExitCode == 0
            ? new BrokerResponse(true, Detail: disk is null ? "unmounted all" : $"unmounted {disk}")
            : Fail(r, "Unmount disk");
    }

    /// <summary>True/false when the disk could be checked; null when enumeration failed
    /// (caller must fail closed for physical mounts).</summary>
    private bool? IsSystemDisk(string disk)
    {
        try
        {
            return _disks.Enumerate().Any(d =>
                d.IsSystem && string.Equals(d.DeviceId, disk, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static BrokerResponse Fail(ProcessResult r, string op)
    {
        var msg = string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr;
        return new BrokerResponse(false, $"{op} failed: {msg.Trim()}", Detail: $"exit {r.ExitCode}");
    }
}
