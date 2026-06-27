using System.Diagnostics;
namespace Wsl.Core.Monitoring;

/// <summary>Reads the WSL2 utility VM host process (vmmemWSL, fallback vmmem).
/// CPU% is the TotalProcessorTime delta between consecutive Read() calls over wall time.</summary>
public sealed class VmProcessProbe : IVmProcessProbe
{
    private TimeSpan _lastCpu; private DateTime _lastAt = DateTime.UtcNow;
    public (double cpuPercent, long workingSet) Read()
    {
        var p = Find();
        if (p is null) return (0, 0);
        var now = DateTime.UtcNow; var cpu = p.TotalProcessorTime;
        var wall = (now - _lastAt).TotalMilliseconds;
        double pct = 0;
        if (wall > 0 && _lastCpu != default)
            pct = Math.Round(100.0 * (cpu - _lastCpu).TotalMilliseconds / (wall * Environment.ProcessorCount), 1);
        _lastCpu = cpu; _lastAt = now;
        return (Math.Max(0, pct), p.WorkingSet64);
    }
    private static Process? Find()
    {
        foreach (var n in new[] { "vmmemWSL", "vmmem" })
        { var ps = Process.GetProcessesByName(n); if (ps.Length > 0) return ps[0]; }
        return null;
    }
}
