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

    public async Task InstallCustomAsync(CustomInstallOptions o, CancellationToken ct = default)
    {
        if (o.Distro is not null && o.FromFile is not null)
            throw new ArgumentException("Specify a catalog distro or a local file, not both.");
        if (o.Distro is null && o.FromFile is null)
            throw new ArgumentException("Specify a catalog distro or a local file.");

        var args = new List<string> { "--install" };
        if (o.Distro is not null) args.Add(o.Distro);
        if (o.FromFile is not null) { args.Add("--from-file"); args.Add(o.FromFile); }
        if (o.Name is not null) { args.Add("--name"); args.Add(o.Name); }
        if (o.Location is not null) { args.Add("--location"); args.Add(o.Location); }
        if (o.Version is not null) { args.Add("--version"); args.Add(o.Version.Value.ToString()); }
        if (o.WebDownload) args.Add("--web-download");
        args.Add("--no-launch");

        var result = await _runner.RunAsync("wsl.exe", args.ToArray(), null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Install {o.Distro ?? o.FromFile}");
    }

    public async Task ImportTarAsync(string name, string installDir, string tarPath, int version,
                                     CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe",
            new[] { "--import", name, installDir, tarPath, "--version", version.ToString() }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Import {name}");
    }

    /// <summary>Registers an existing .vhdx where it sits — the file is not copied.
    /// It must contain an ext4 filesystem.</summary>
    public async Task ImportInPlaceAsync(string name, string vhdxPath, CancellationToken ct = default)
    {
        if (!vhdxPath.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Import in place requires a .vhdx file.", nameof(vhdxPath));

        var result = await _runner.RunAsync("wsl.exe",
            new[] { "--import-in-place", name, vhdxPath }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Import {name} in place");
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
