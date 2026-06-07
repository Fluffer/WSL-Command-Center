using System.Management;
using Wsl.Contracts;

namespace Wsl.Broker;

/// <summary>Seam for disk enumeration so PrivilegedOperations stays testable without WMI.</summary>
public interface IDiskEnumerator
{
    IReadOnlyList<DiskInfo> Enumerate();
}

/// <summary>Enumerates physical disks via WMI <c>Win32_DiskDrive</c>. Runs inside the elevated
/// broker (council requirement). Thin I/O wrapper — intentionally untested.</summary>
public sealed class WmiDiskEnumerator : IDiskEnumerator
{
    public IReadOnlyList<DiskInfo> Enumerate()
    {
        // Fail closed: without a reliable system-disk identification no disk may be
        // offered for mounting (PrivilegedOperations refuses mounts when this throws).
        var systemIndex = GetSystemDiskIndex()
            ?? throw new InvalidOperationException("Could not identify the system disk.");
        var disks = new List<(uint Index, DiskInfo Info)>();
        using var search = new ManagementObjectSearcher(
            "SELECT DeviceID, Model, SerialNumber, Size, Index FROM Win32_DiskDrive");
        foreach (var drive in search.Get())
        {
            var index = Convert.ToUInt32(drive["Index"]);
            disks.Add((index, new DiskInfo(
                DeviceId: drive["DeviceID"] as string ?? "",
                Model: (drive["Model"] as string ?? "").Trim(),
                SerialNumber: (drive["SerialNumber"] as string ?? "").Trim(),
                SizeBytes: drive["Size"] is null ? 0 : Convert.ToInt64(drive["Size"]),
                IsSystem: index == systemIndex)));
        }
        return disks.OrderBy(d => d.Index).Select(d => d.Info).ToList();
    }

    /// <summary>Index of the disk hosting %SystemDrive%, resolved via the
    /// LogicalDisk→Partition→DiskDrive WMI associations. Null when it cannot be
    /// determined — a fixed fallback (e.g. disk 0) would misidentify systems whose
    /// boot disk is not index 0 and let the system disk slip past the mount guard.</summary>
    private static uint? GetSystemDiskIndex()
    {
        try
        {
            // Validate before embedding in WQL — env vars are caller-controlled.
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") is { Length: 2 } sd
                && char.IsAsciiLetter(sd[0]) && sd[1] == ':' ? sd : "C:";
            using var partitions = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemDrive}'}} " +
                "WHERE AssocClass=Win32_LogicalDiskToPartition");
            foreach (var partition in partitions.Get())
            {
                using var drives = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} " +
                    "WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                foreach (var drive in drives.Get())
                    return Convert.ToUInt32(drive["Index"]);
            }
        }
        catch
        {
            // WMI association lookup failed — fall through to "unknown".
        }
        return null;
    }
}
