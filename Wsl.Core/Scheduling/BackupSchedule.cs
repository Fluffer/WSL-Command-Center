namespace Wsl.Core.Scheduling;

public enum ScheduleFrequency { Daily, Weekly }

/// <summary>
/// A recurring backup definition. The scheduled task runs `wsl --export` (unelevated,
/// so no broker is involved) to a timestamped file, then prunes to KeepCount newest.
/// </summary>
/// <param name="DistroName">Distro to export.</param>
/// <param name="Folder">Destination folder for the archive files.</param>
/// <param name="Format">Export format.</param>
/// <param name="Frequency">Daily or Weekly.</param>
/// <param name="Time">Start time, "HH:mm" 24-hour.</param>
/// <param name="KeepCount">Number of most-recent archives to retain (older ones are deleted).</param>
public sealed record BackupSchedule(
    string DistroName,
    string Folder,
    ExportFormat Format,
    ScheduleFrequency Frequency,
    string Time,
    int KeepCount);
