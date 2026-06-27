namespace Wsl.Core.Diagnostics;

public record GpuInfo(bool DxgPresent, bool NvidiaDetected,
    string? Name, string? DriverVersion, long? MemUsedMb, long? MemTotalMb);

public class WslGpuService
{
    private readonly IProcessRunner _runner;
    public WslGpuService(IProcessRunner runner) => _runner = runner;

    public async Task<GpuInfo> ProbeAsync(string distro, CancellationToken ct = default)
    {
        var dxg = (await _runner.RunAsync("wsl.exe",
            new[] { "-d", distro, "--", "test", "-e", "/dev/dxg" }, null, ct)).ExitCode == 0;
        var smi = await _runner.RunAsync("wsl.exe",
            new[] { "-d", distro, "--", "nvidia-smi",
                    "--query-gpu=name,driver_version,memory.used,memory.total",
                    "--format=csv,noheader,nounits" }, null, ct);
        return ParseNvidiaSmi(dxg, smi.ExitCode, smi.StdOut);
    }

    public static GpuInfo ParseNvidiaSmi(bool dxg, int exitCode, string csv)
    {
        if (exitCode != 0 || string.IsNullOrWhiteSpace(csv))
            return new GpuInfo(dxg, false, null, null, null, null);
        var first = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        var f = first.Split(',').Select(x => x.Trim()).ToArray();
        if (f.Length < 4) return new GpuInfo(dxg, false, null, null, null, null);
        long? used = long.TryParse(f[2], System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var u) ? u : null;
        long? total = long.TryParse(f[3], System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;
        return new GpuInfo(dxg, true, f[0], f[1], used, total);
    }
}
