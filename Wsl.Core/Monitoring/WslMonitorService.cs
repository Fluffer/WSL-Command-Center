using System.Globalization;

namespace Wsl.Core.Monitoring;

public class WslMonitorService
{
    private readonly IProcessRunner _runner;
    private readonly IVmProcessProbe _vmProbe;
    private readonly IVhdxSizeProbe _vhdxProbe;
    private readonly Dictionary<string, CpuSample> _prev = new();

    public WslMonitorService(IProcessRunner runner, IVmProcessProbe vmProbe, IVhdxSizeProbe vhdxProbe)
    { _runner = runner; _vmProbe = vmProbe; _vhdxProbe = vhdxProbe; }

    public async Task<MonitorSnapshot> SampleAsync(
        IReadOnlyList<string> runningDistros, CancellationToken ct = default)
    {
        var (cpu, ws) = _vmProbe.Read();
        var vm = new VmMetrics(cpu, ws, _vhdxProbe.TotalBytes());

        var rows = new List<DistroMetrics>();
        foreach (var name in runningDistros)
        {
            var r = await _runner.RunAsync("wsl.exe",
                new[] { "-d", name, "--", "sh", "-c",
                        "cat /proc/meminfo; echo ---; cat /proc/stat; echo ---; df -B1 /" },
                null, ct);
            if (r.ExitCode != 0) continue;
            _prev.TryGetValue(name, out var prev);
            var m = ParseDistro(name, r.StdOut, prev);
            _prev[name] = SampleFromStat(r.StdOut);
            rows.Add(m);
        }

        // Prune stale baselines for distros no longer running
        foreach (var key in _prev.Keys.Except(runningDistros).ToList())
            _prev.Remove(key);

        return new MonitorSnapshot(vm, rows);
    }

    public static DistroMetrics ParseDistro(string name, string combined, CpuSample? prev)
    {
        var parts = combined.Replace("\r", "").Split("---", StringSplitOptions.None);
        var meminfo = parts.Length > 0 ? parts[0] : "";
        var stat = parts.Length > 1 ? parts[1] : "";
        var df = parts.Length > 2 ? parts[2] : "";

        long memTotal = ReadMemKb(meminfo, "MemTotal") * 1024;
        long memAvail = ReadMemKb(meminfo, "MemAvailable") * 1024;
        long memUsed = Math.Max(0, memTotal - memAvail);

        var cur = ParseStat(stat);
        double cpuPct = 0;
        if (prev is not null && cur.Total > prev.Total)
        {
            var dTotal = cur.Total - prev.Total;
            var dBusy = cur.Busy - prev.Busy;
            cpuPct = dBusy >= 0
                ? Math.Max(0, Math.Round(100.0 * dBusy / dTotal, 1))
                : 0;
        }

        var (used, total) = ParseDf(df);
        return new DistroMetrics(name, cpuPct, memUsed, memTotal, used, total);
    }

    private static CpuSample SampleFromStat(string combined)
    {
        var parts = combined.Replace("\r", "").Split("---", StringSplitOptions.None);
        return ParseStat(parts.Length > 1 ? parts[1] : "");
    }

    private static long ReadMemKb(string meminfo, string key)
    {
        foreach (var line in meminfo.Split('\n'))
        {
            if (!line.StartsWith(key + ":")) continue;
            var tok = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length >= 2 && long.TryParse(tok[1], out var kb)) return kb;
        }
        return 0;
    }

    private static CpuSample ParseStat(string stat)
    {
        foreach (var line in stat.Split('\n'))
        {
            if (!line.StartsWith("cpu ") && !line.StartsWith("cpu\t")) continue;
            var tok = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // tok[0]="cpu"; fields: user nice system idle iowait irq softirq steal guest guest_nice
            // Require at least the idle field at tok[4]; fewer fields means malformed line
            if (tok.Length < 5) return new CpuSample(0, 0);
            long total = 0, idle = 0, iowait = 0;
            for (int i = 1; i < tok.Length; i++)
            {
                if (long.TryParse(tok[i], out var v))
                {
                    total += v;
                    if (i == 4) idle = v;
                    if (i == 5) iowait = v;
                }
            }
            return new CpuSample(total - idle - iowait, total);
        }
        return new CpuSample(0, 0);
    }

    private static (long used, long total) ParseDf(string df)
    {
        // Flatten all non-header lines into one token list to handle wrapped lines,
        // overlay/tmpfs/none device names, and device paths with spaces.
        var allTok = new List<string>();
        foreach (var line in df.Split('\n'))
        {
            if (line.Contains("Filesystem")) continue; // skip header
            var tok = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            allTok.AddRange(tok);
        }
        // Find the Use% token (ends with '%'); total is 3 positions before, used is 2 positions before.
        for (int k = 0; k < allTok.Count; k++)
        {
            if (!allTok[k].EndsWith("%")) continue;
            if (k >= 3
                && long.TryParse(allTok[k - 3], out var t)
                && long.TryParse(allTok[k - 2], out var u))
                return (u, t);
        }
        return (0, 0);
    }
}
