using Microsoft.Win32;

namespace Wsl.Core;

/// <summary>The distro's virtual disk file and its current size on disk.</summary>
public record VhdxInfo(string Path, long SizeBytes);

/// <summary>
/// Disk maintenance for WSL2 distros. "Optimize" makes the distro's ext4.vhdx sparse via
/// `wsl --manage --set-sparse true`, so Windows reclaims space freed inside the distro.
/// "Move" relocates the distro's storage to another directory (`wsl --manage --move`).
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

    /// <summary>
    /// Moves the distro's storage to <paramref name="targetDir"/>. Terminates the distro first
    /// (move requires it stopped), then runs the copy with no timeout — moving a large vhdx
    /// can take many minutes and must not be killed midway.
    /// </summary>
    public async Task MoveAsync(string name, string targetDir, CancellationToken ct = default)
    {
        var terminate = await _runner.RunAsync("wsl.exe", new[] { "--terminate", name }, null, ct);
        WslErrorMapper.ThrowIfFailed(terminate, $"Terminate {name}");

        var move = await _runner.RunAsync("wsl.exe",
            new[] { "--manage", name, "--move", targetDir }, Timeout.InfiniteTimeSpan, ct);
        WslErrorMapper.ThrowIfFailed(move, $"Move {name}");
    }

    /// <summary>
    /// Locates the distro's ext4.vhdx via the per-user Lxss registry (DistributionName → BasePath)
    /// and returns its path and size. Thin I/O wrapper — preflight logic lives in
    /// <see cref="MovePreflight"/>. Returns null when the distro or its vhdx cannot be found.
    /// </summary>
    public static VhdxInfo? GetVhdxInfo(string name)
    {
        using var lxss = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Lxss");
        if (lxss is null) return null;

        foreach (var subName in lxss.GetSubKeyNames())
        {
            using var sub = lxss.OpenSubKey(subName);
            if (sub?.GetValue("DistributionName") as string != name) continue;
            if (sub.GetValue("BasePath") is not string basePath) return null;
            if (basePath.StartsWith(@"\\?\", StringComparison.Ordinal))
                basePath = basePath[4..]; // wsl writes extended-length paths; FileInfo wants plain
            var vhdx = new FileInfo(System.IO.Path.Combine(basePath, "ext4.vhdx"));
            return vhdx.Exists ? new VhdxInfo(vhdx.FullName, vhdx.Length) : null;
        }
        return null;
    }
}
