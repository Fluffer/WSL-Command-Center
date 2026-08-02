namespace Wsl.Core;

public class WslSystemService
{
    private readonly IProcessRunner _runner;

    public WslSystemService(IProcessRunner runner) => _runner = runner;

    public async Task<WslStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe", new[] { "--status" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, "Get WSL status");
        return ParseStatus(result.StdOut);
    }

    public async Task<WslVersionInfo> GetVersionInfoAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe", new[] { "--version" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, "Get WSL version");
        return ParseVersion(result.StdOut);
    }

    /// <summary>Runs `wsl --update` to pull the latest WSL kernel/runtime from the Store/GitHub.</summary>
    public async Task UpdateAsync(CancellationToken ct = default)
    {
        // No timeout: `wsl --update` downloads and installs a new WSL build.
        var result = await _runner.RunAsync("wsl.exe", new[] { "--update" }, Timeout.InfiniteTimeSpan, ct);
        WslErrorMapper.ThrowIfFailed(result, "Update WSL");
    }

    internal static WslStatus ParseStatus(string stdout)
    {
        string? distro = null; int? version = null;
        foreach (var line in stdout.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("Default Distribution:", StringComparison.OrdinalIgnoreCase))
            {
                var val = t.Split(':', 2)[1].Trim();
                distro = string.IsNullOrWhiteSpace(val) ? null : val;
            }
            else if (t.StartsWith("Default Version:", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(t.Split(':', 2)[1].Trim(), out var v))
                version = v;
        }
        return new WslStatus(distro, version, stdout);
    }

    internal static WslVersionInfo ParseVersion(string stdout)
    {
        string? wsl = null, kernel = null, wslg = null;
        foreach (var line in stdout.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("WSL version:", StringComparison.OrdinalIgnoreCase)) wsl = After(t);
            else if (t.StartsWith("Kernel version:", StringComparison.OrdinalIgnoreCase)) kernel = After(t);
            else if (t.StartsWith("WSLg version:", StringComparison.OrdinalIgnoreCase)) wslg = After(t);
        }
        return new WslVersionInfo(wsl, kernel, wslg, stdout);

        static string? After(string s)
        {
            var val = s.Split(':', 2)[1].Trim();
            return string.IsNullOrWhiteSpace(val) ? null : val;
        }
    }
}
