namespace Wsl.Core.Monitoring;

public record DistroMetrics(string Name, double CpuPercent,
    long MemUsedBytes, long MemTotalBytes, long DiskUsedBytes, long DiskTotalBytes);
public record VmMetrics(double CpuPercent, long WorkingSetBytes, long DiskBytes);
public record MonitorSnapshot(VmMetrics Vm, IReadOnlyList<DistroMetrics> Distros);
public record CpuSample(long Busy, long Total);

public interface IVmProcessProbe { (double cpuPercent, long workingSet) Read(); }
public interface IVhdxSizeProbe { long TotalBytes(); }
