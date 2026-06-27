namespace Wsl.Core;

/// <summary>
/// Runs an export with the WSL2 VM shut down (the only way to release distro
/// VHDs), then restores the pre-export running/stopped status of every distro.
/// </summary>
public sealed class StatePreservingExport
{
    private readonly WslDistroService _distros;
    public StatePreservingExport(WslDistroService distros) => _distros = distros;

    /// <summary>Names of distros currently Running (used by the UI to warn before Run).</summary>
    public async Task<IReadOnlyList<string>> RunningAsync(CancellationToken ct = default) =>
        (await _distros.ListAsync(ct))
            .Where(d => d.State == DistroState.Running).Select(d => d.Name).ToList();

    /// <summary>
    /// Shuts WSL down, runs <paramref name="export"/>, then restarts every distro that
    /// was running. Restart runs even if export throws. Returns the restarted names.
    /// </summary>
    public async Task<IReadOnlyList<string>> RunAsync(
        Func<CancellationToken, Task> export, CancellationToken ct = default)
    {
        var running = await RunningAsync(ct);
        await _distros.ShutdownAsync(ct);
        try { await export(ct); }
        finally
        {
            foreach (var d in running)
            {
                try { await _distros.StartAsync(d, ct); }
                catch { /* best-effort restart; don't let one failure strand the others or mask the export error */ }
            }
        }
        return running;
    }
}
