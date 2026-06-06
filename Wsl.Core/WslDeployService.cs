namespace Wsl.Core;

public class WslDeployService
{
    private readonly IProcessRunner _runner;

    public WslDeployService(IProcessRunner runner) => _runner = runner;

    public async Task<IReadOnlyList<CatalogEntry>> ListAvailableAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe", new[] { "--list", "--online" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, "List online distros");
        return ParseCatalog(result.StdOut);
    }

    internal static IReadOnlyList<CatalogEntry> ParseCatalog(string stdout)
    {
        var lines = stdout.Replace("\r", "").Split('\n');
        var entries = new List<CatalogEntry>();
        var headerSeen = false;
        foreach (var line in lines)
        {
            if (!headerSeen)
            {
                if (line.TrimStart().StartsWith("NAME")) headerSeen = true;
                continue;
            }
            if (string.IsNullOrWhiteSpace(line)) continue;

            // NAME is a single token; FRIENDLY NAME is the rest.
            var trimmed = line.TrimEnd();
            var firstGap = trimmed.IndexOf("  ", StringComparison.Ordinal);
            if (firstGap < 0)
            {
                entries.Add(new CatalogEntry(trimmed.Trim(), trimmed.Trim()));
                continue;
            }
            var name = trimmed[..firstGap].Trim();
            var friendly = trimmed[firstGap..].Trim();
            entries.Add(new CatalogEntry(name, friendly));
        }
        return entries;
    }

    public async Task InstallFromCatalogAsync(string name, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(
            "wsl.exe", new[] { "--install", "-d", name, "--no-launch" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Install {name}");
    }

    public async Task ImportTarAsync(string name, string installDir, string tarPath, int version,
                                     CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe",
            new[] { "--import", name, installDir, tarPath, "--version", version.ToString() }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Import {name}");
    }

    public async Task ImportVhdxAsync(string name, string installDir, string vhdxPath, int version,
                                      CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe",
            new[] { "--import", name, installDir, vhdxPath, "--vhd", "--version", version.ToString() },
            null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Import {name}");
    }
}
