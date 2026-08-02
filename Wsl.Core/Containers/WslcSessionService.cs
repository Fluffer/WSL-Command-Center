using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Wsl.Core.Containers;

/// <summary>
/// Sessions and their disk usage for the preview <c>wslc</c> CLI. A wslc session is the VM-level
/// container host; it holds real disk at <c>%LOCALAPPDATA%\wslc\sessions\&lt;DisplayName&gt;\</c>
/// (a storage.vhdx and a swap.vhdx) that nothing currently surfaces or reclaims. Mirrors
/// <see cref="WslcService"/>'s degrade-don't-throw style: wslc absent, hung, or emitting an
/// unfamiliar table all degrade to an empty result or a failed result record rather than an
/// exception. `enter`/`shell`/`run` are interactive and out of scope for this service.
/// </summary>
public class WslcSessionService
{
    private readonly IProcessRunner _runner;
    private readonly string _sessionsRoot;

    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TerminateTimeout = TimeSpan.FromSeconds(30);

    /// <param name="runner">Process runner used for every wslc invocation.</param>
    /// <param name="sessionsRoot">Root directory holding one subdirectory per session
    /// (named after the session's Display Name). Defaults to
    /// <c>%LOCALAPPDATA%\wslc\sessions</c>; overridable so tests never touch the real path.</param>
    public WslcSessionService(IProcessRunner runner, string? sessionsRoot = null)
    {
        _runner = runner;
        _sessionsRoot = sessionsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wslc", "sessions");
    }

    /// <summary>Lists sessions via `wslc system session list --verbose` (there is no
    /// `--format json` for this command — the table is parsed). Returns empty on any failure or
    /// unparseable output; never throws.</summary>
    public async Task<IReadOnlyList<WslcSession>> ListSessionsAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _runner.RunAsync(
                "wslc.exe", new[] { "system", "session", "list", "--verbose" }, ListTimeout, ct);
            return r.ExitCode == 0 ? ParseSessionList(r.StdOut) : Array.Empty<WslcSession>();
        }
        catch
        {
            return Array.Empty<WslcSession>();
        }
    }

    /// <summary>
    /// Resolves a session's on-disk footprint at
    /// <c>%LOCALAPPDATA%\wslc\sessions\&lt;DisplayName&gt;\</c>. A missing directory or file
    /// reports as unknown size (a null <see cref="WslcSessionVhdSize"/>), never as an error.
    /// </summary>
    public WslcSessionDiskUsage GetDiskUsage(string displayName)
    {
        var dir = Path.Combine(_sessionsRoot, displayName);
        var storage = ResolveVhdSize(Path.Combine(dir, "storage.vhdx"));
        var swap = ResolveVhdSize(Path.Combine(dir, "swap.vhdx"));

        long? total = null;
        if (storage?.LogicalBytes is long s) total = (total ?? 0) + s;
        if (swap?.LogicalBytes is long w) total = (total ?? 0) + w;

        return new WslcSessionDiskUsage(displayName, storage, swap, FormatBytes(total));
    }

    /// <summary>
    /// Terminates the DEFAULT wslc session. Per the CLI contract, `wslc system session
    /// terminate` takes no session argument — there is no way to target a specific session, so
    /// this always kills whatever is currently the default. <b>This kills every running
    /// container in that session.</b> Returns a result record rather than throwing; the caller
    /// is expected to gate this behind a confirmation prompt.
    /// </summary>
    public async Task<WslcTerminateResult> TerminateDefaultSessionAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _runner.RunAsync(
                "wslc.exe", new[] { "system", "session", "terminate" }, TerminateTimeout, ct);
            if (r.ExitCode == 0)
            {
                var msg = string.IsNullOrWhiteSpace(r.StdOut) ? "Session terminated." : r.StdOut.Trim();
                return new WslcTerminateResult(true, msg);
            }
            var err = string.IsNullOrWhiteSpace(r.StdErr) ? $"wslc exited with code {r.ExitCode}." : r.StdErr.Trim();
            return new WslcTerminateResult(false, err);
        }
        catch (WslException ex) when (ex.Kind == WslErrorKind.Timeout)
        {
            return new WslcTerminateResult(false, "wslc timed out while terminating the session.");
        }
        catch (Win32Exception ex)
        {
            return new WslcTerminateResult(false, $"Could not start wslc.exe: {ex.Message}");
        }
    }

    /// <summary>
    /// Reclaims disk space held by a session's VHDs. Ordering is deliberate: the default session
    /// is terminated first — this project already learned (state-preserving export) that only
    /// releasing a vhdx's handle lets Windows touch the file — then both VHDs are marked sparse
    /// via the unelevated NTFS <c>FSCTL_SET_SPARSE</c> control code, the same primitive
    /// <c>WslDiskService.OptimizeAsync</c> uses (via `wsl --manage --set-sparse`) for distro
    /// VHDs. Marking sparse only lets Windows reclaim space the guest frees from now on; it does
    /// not shrink space already allocated. Shrinking that needs elevated Hyper-V tooling
    /// (Optimize-VHD / diskpart "compact vdisk") which requires a privileged broker this service
    /// does not invoke — see <see cref="WslcReclaimResult.RequiresElevationToCompact"/>. Never
    /// throws; every expected failure degrades to <c>Success == false</c>.
    /// </summary>
    public async Task<WslcReclaimResult> ReclaimAsync(string displayName, CancellationToken ct = default)
    {
        var terminate = await TerminateDefaultSessionAsync(ct);
        if (!terminate.Success)
            return new WslcReclaimResult(false, $"Reclaim aborted: {terminate.Message}");

        var dir = Path.Combine(_sessionsRoot, displayName);
        var storageMarked = TryMarkSparse(Path.Combine(dir, "storage.vhdx"));
        var swapMarked = TryMarkSparse(Path.Combine(dir, "swap.vhdx"));
        var marked = storageMarked || swapMarked;

        var message = marked
            ? "Session terminated and VHDs marked sparse; Windows reclaims freed space as the guest writes going forward. Compacting space already allocated needs elevated tooling not available here."
            : "Session terminated, but no session VHDs were found to mark sparse.";

        return new WslcReclaimResult(marked, message) { MarkedSparse = marked };
    }

    /// <summary>
    /// Best-effort parse of `wslc system session list --verbose` output: a leading `[wslc]`
    /// banner line, a header row, then data rows split on runs of 2+ spaces into Id, CreatorPid,
    /// DisplayName. Unparseable rows are skipped rather than failing the whole list — the header
    /// row is recognized because its Id column isn't numeric, so a missing/renamed header still
    /// can't be mistaken for a data row (which always starts with a numeric session id).
    /// </summary>
    internal static IReadOnlyList<WslcSession> ParseSessionList(string stdout)
    {
        var sessions = new List<WslcSession>();
        if (string.IsNullOrWhiteSpace(stdout)) return sessions;

        var lines = stdout.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("[wslc]", StringComparison.OrdinalIgnoreCase)) continue;

            var fields = Regex.Split(line, @"\s{2,}").Where(f => f.Length > 0).ToArray();
            if (fields.Length < 3) continue;
            if (!int.TryParse(fields[0], out var id)) continue;       // header row, or unparseable — skip
            if (!int.TryParse(fields[1], out var pid)) continue;      // malformed row — skip, keep the rest

            sessions.Add(new WslcSession(id, pid, fields[2]));
        }
        return sessions;
    }

    /// <summary>Formats a byte count as a short human-readable string (e.g. "770.7 MB"). Null
    /// (nothing known) formats as "unknown".</summary>
    internal static string FormatBytes(long? bytes)
    {
        if (bytes is not long b) return "unknown";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = b;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    private static WslcSessionVhdSize? ResolveVhdSize(string path)
    {
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        var isSparse = (info.Attributes & FileAttributes.SparseFile) != 0;
        return new WslcSessionVhdSize(info.Length, TryGetActualSizeOnDisk(path), isSparse);
    }

    /// <summary>Actual bytes allocated on disk (accounts for NTFS sparse holes) via the
    /// standard unelevated Win32 compressed/sparse file size API. Returns null on any failure —
    /// this is a best-effort enrichment, never a required value.</summary>
    private static long? TryGetActualSizeOnDisk(string path)
    {
        try
        {
            var low = GetCompressedFileSizeW(path, out var high);
            if (low == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0) return null;
            return ((long)high << 32) | low;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Marks a file sparse via `FSCTL_SET_SPARSE` (no admin required — only that the
    /// file isn't held open elsewhere, which is why the session is terminated first). Returns
    /// false on any failure (missing file, still locked, access denied) rather than throwing.</summary>
    private static bool TryMarkSparse(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.Read);
            return DeviceIoControl(handle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }
        catch
        {
            return false;
        }
    }

    private const uint FsctlSetSparse = 0x000900c4;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeHandle hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);
}
