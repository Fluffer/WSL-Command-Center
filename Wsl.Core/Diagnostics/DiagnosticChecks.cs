using System.Linq;

namespace Wsl.Core.Diagnostics;

/// <summary>Verifies wsl.exe runs and reports a version.</summary>
public sealed class WslInstalledCheck : IDiagnosticCheck
{
    private readonly IProcessRunner _runner;
    public WslInstalledCheck(IProcessRunner runner) => _runner = runner;
    public string Id => "wsl-installed";

    public async Task<DiagnosticResult> RunAsync(CancellationToken ct = default)
    {
        ProcessResult r;
        try { r = await _runner.RunAsync("wsl.exe", new[] { "--version" }, null, ct); }
        catch (Exception ex)
        {
            return new(Id, "WSL installation", DiagnosticSeverity.Error,
                $"Could not run wsl.exe ({ex.Message}). WSL may not be installed.");
        }

        if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.StdOut))
            return new(Id, "WSL installation", DiagnosticSeverity.Error,
                "wsl.exe did not report a version. Install or update WSL.",
                new DiagnosticFix(WslDiagnosticsService.Fixes.UpdateWsl, "Update WSL", Destructive: false));

        var firstLine = r.StdOut.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        return new(Id, "WSL installation", DiagnosticSeverity.Ok, firstLine ?? "WSL is installed.");
    }
}

/// <summary>Flags distros stuck in a non-ready state (Unknown/Installing) or still on WSL 1.
/// Uses `wsl --list --verbose`, which does not boot any distro.</summary>
public sealed class DistroHealthCheck : IDiagnosticCheck
{
    private readonly WslDistroService _distros;
    public DistroHealthCheck(WslDistroService distros) => _distros = distros;
    public string Id => "distro-health";

    public async Task<DiagnosticResult> RunAsync(CancellationToken ct = default)
    {
        var list = await _distros.ListAsync(ct);
        if (list.Count == 0)
            return new(Id, "Distributions", DiagnosticSeverity.Info, "No WSL distributions are installed.");

        var broken = list.Where(d => d.State is DistroState.Unknown or DistroState.Installing).ToList();
        if (broken.Count > 0)
            return new(Id, "Distributions", DiagnosticSeverity.Warning,
                $"{broken.Count} distro(s) in a non-ready state: " +
                string.Join(", ", broken.Select(d => $"{d.Name} ({d.State})")) + ".",
                new DiagnosticFix(WslDiagnosticsService.Fixes.RestartWsl, "Restart WSL", Destructive: true));

        var wsl1 = list.Where(d => d.Version == 1).Select(d => d.Name).ToList();
        if (wsl1.Count > 0)
            return new(Id, "Distributions", DiagnosticSeverity.Info,
                $"{wsl1.Count} distro(s) on WSL 1 (VM/.wslconfig features do not apply): {string.Join(", ", wsl1)}.");

        return new(Id, "Distributions", DiagnosticSeverity.Ok, $"{list.Count} distro(s) registered and healthy.");
    }
}

/// <summary>When mirrored networking is enabled, warns that the Windows firewall can silently
/// block inbound traffic to Linux services. Reuses the F1 mode catalog.</summary>
public sealed class MirroredFirewallCheck : IDiagnosticCheck
{
    private readonly WslConfigService _config;
    public MirroredFirewallCheck(WslConfigService config) => _config = config;
    public string Id => "mirrored-firewall";

    public async Task<DiagnosticResult> RunAsync(CancellationToken ct = default)
    {
        var cfg = await _config.ReadGlobalAsync(ct);
        if (NetworkModeCatalog.Parse(cfg.Networking) == WslNetworkMode.Mirrored)
            return new(Id, "Mirrored networking firewall", DiagnosticSeverity.Info,
                "Mirrored networking is on. Windows Defender Firewall and Hyper-V firewall rules can silently " +
                "block inbound connections to Linux services. If a LAN host cannot reach a WSL port, check the firewall.");
        return new(Id, "Mirrored networking firewall", DiagnosticSeverity.Ok,
            "Not in mirrored mode — the mirrored-firewall caveat does not apply.");
    }
}
