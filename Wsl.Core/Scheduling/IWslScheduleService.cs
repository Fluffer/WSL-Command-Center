namespace Wsl.Core.Scheduling;

/// <summary>Creates, lists, and deletes recurring WSL backup tasks via Windows Task Scheduler.</summary>
public interface IWslScheduleService
{
    Task CreateAsync(BackupSchedule schedule, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(string taskName, CancellationToken ct = default);
    string TaskNameFor(string distro);
    string BuildScript(BackupSchedule schedule);
}
