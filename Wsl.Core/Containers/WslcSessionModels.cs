namespace Wsl.Core.Containers;

/// <summary>A wslc session parsed best-effort from `wslc system session list --verbose`. All
/// fields come straight off the columnar table — there is no `--format json` for this command.</summary>
public record WslcSession(int Id, int CreatorPid, string DisplayName);

/// <summary>Size of one VHD file backing a session. <see cref="LogicalBytes"/> is the file's
/// reported length; <see cref="ActualBytes"/> is what Windows has actually allocated on disk
/// (they diverge once the file is sparse). Either can be null when the platform call fails —
/// that degrades to "unknown", never an error.</summary>
public record WslcSessionVhdSize(long? LogicalBytes, long? ActualBytes, bool IsSparse);

/// <summary>Disk footprint of one wslc session: its storage.vhdx and swap.vhdx under
/// %LOCALAPPDATA%\wslc\sessions\&lt;DisplayName&gt;\. A missing directory or file surfaces as a
/// null <see cref="Storage"/>/<see cref="Swap"/>, never as an error.</summary>
public record WslcSessionDiskUsage(
    string DisplayName,
    WslcSessionVhdSize? Storage,
    WslcSessionVhdSize? Swap,
    string TotalHumanReadable);

/// <summary>Outcome of terminating the wslc DEFAULT session. Never thrown — expected failures
/// (wslc absent, timeout, non-zero exit) degrade to <c>Success == false</c> with a message.</summary>
public record WslcTerminateResult(bool Success, string Message);

/// <summary>
/// Outcome of a reclaim attempt against one session's VHDs. Reclaim here means: terminate the
/// session (releases the vhdx handles) and mark the VHDs sparse, the same primitive
/// <c>WslDiskService.OptimizeAsync</c> uses for distro VHDs. Marking sparse only lets Windows
/// reclaim space the guest frees from now on — it does not shrink space already allocated
/// inside the file. Actually compacting already-allocated space needs elevated Hyper-V tooling
/// (Optimize-VHD / diskpart "compact vdisk"), which requires a privileged broker this service
/// does not invoke; <see cref="RequiresElevationToCompact"/> is always true to surface that.
/// </summary>
public record WslcReclaimResult(bool Success, string Message)
{
    /// <summary>True when at least one of the session's VHDs was successfully marked sparse.</summary>
    public bool MarkedSparse { get; init; }

    /// <summary>Always true: shrinking space already allocated inside the VHDs needs elevated
    /// tooling this service does not have access to in this pass.</summary>
    public bool RequiresElevationToCompact { get; init; } = true;
}
