namespace Wsl.Core;

/// <summary>
/// Disk maintenance for WSL2 distros. "Optimize" makes the distro's ext4.vhdx sparse via
/// `wsl --manage --set-sparse true`, so Windows reclaims space freed inside the distro.
/// The distro must be stopped first, so we terminate it. This is unelevated — no broker.
/// </summary>
public class WslDiskService
{
    private readonly IProcessRunner _runner;

    public WslDiskService(IProcessRunner runner) => _runner = runner;

    public async Task OptimizeAsync(string name, CancellationToken ct = default)
    {
        var terminate = await _runner.RunAsync("wsl.exe", new[] { "--terminate", name }, null, ct);
        WslErrorMapper.ThrowIfFailed(terminate, $"Terminate {name}");

        var sparse = await _runner.RunAsync("wsl.exe",
            new[] { "--manage", name, "--set-sparse", "true" }, null, ct);
        WslErrorMapper.ThrowIfFailed(sparse, $"Optimize {name}");
    }
}
