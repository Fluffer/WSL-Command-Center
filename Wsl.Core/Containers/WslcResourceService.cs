using System.ComponentModel;
using System.Text.Json;

namespace Wsl.Core.Containers;

/// <summary>
/// Thin, defensive wrapper over the image/volume/network/registry surface of the preview
/// <c>wslc</c> CLI. Mirrors <see cref="WslcService"/>'s contract: every call is time-bound and
/// never throws to the caller for expected failure modes (wslc absent, hung, non-zero exit, or
/// malformed JSON). Listing degrades to an empty list; mutating actions degrade to a non-zero
/// <see cref="WslcActionResult"/> carrying stderr.
/// </summary>
public class WslcResourceService
{
    private readonly IProcessRunner _runner;

    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(30);

    public WslcResourceService(IProcessRunner runner) => _runner = runner;

    // ── Images ───────────────────────────────────────────────────────────────

    /// <summary>Lists images via `wslc image list --format json`. Returns empty on any failure or
    /// unparseable output; never throws.</summary>
    public async Task<IReadOnlyList<WslcImage>> ListImagesAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _runner.RunAsync("wslc.exe", new[] { "image", "list", "--format", "json" }, ListTimeout, ct);
            return r.ExitCode == 0 ? ParseImages(r.StdOut) : Array.Empty<WslcImage>();
        }
        catch
        {
            return Array.Empty<WslcImage>();
        }
    }

    public Task<WslcActionResult> RemoveImageAsync(string image, bool force = false, bool noPrune = false, CancellationToken ct = default)
    {
        var args = new List<string> { "image", "remove" };
        if (force) args.Add("--force");
        if (noPrune) args.Add("--no-prune");
        args.Add(image);
        return RunActionAsync(args, ActionTimeout, ct);
    }

    public Task<WslcActionResult> PruneImagesAsync(bool all = false, CancellationToken ct = default)
    {
        var args = new List<string> { "image", "prune" };
        if (all) args.Add("--all");
        return RunActionAsync(args, ActionTimeout, ct);
    }

    public Task<WslcActionResult> PullImageAsync(string image, CancellationToken ct = default)
        => RunActionAsync(new[] { "pull", image }, TransferTimeout, ct);

    public Task<WslcActionResult> PushImageAsync(string image, CancellationToken ct = default)
        => RunActionAsync(new[] { "push", image }, TransferTimeout, ct);

    public Task<WslcActionResult> TagImageAsync(string source, string target, CancellationToken ct = default)
        => RunActionAsync(new[] { "tag", source, target }, ActionTimeout, ct);

    // ── Volumes ──────────────────────────────────────────────────────────────

    /// <summary>Lists volumes via `wslc volume list --format json`. Returns empty on any failure
    /// or unparseable output; never throws.</summary>
    public async Task<IReadOnlyList<WslcVolume>> ListVolumesAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _runner.RunAsync("wslc.exe", new[] { "volume", "list", "--format", "json" }, ListTimeout, ct);
            return r.ExitCode == 0 ? ParseVolumes(r.StdOut) : Array.Empty<WslcVolume>();
        }
        catch
        {
            return Array.Empty<WslcVolume>();
        }
    }

    /// <summary><paramref name="driver"/> defaults to `guest` when omitted (the CLI's own
    /// default); pass `"vhd"` for the other documented driver.</summary>
    public Task<WslcActionResult> CreateVolumeAsync(string name, string? driver = null, CancellationToken ct = default)
    {
        var args = new List<string> { "volume", "create" };
        if (!string.IsNullOrWhiteSpace(driver)) { args.Add("--driver"); args.Add(driver); }
        args.Add(name);
        return RunActionAsync(args, ActionTimeout, ct);
    }

    public Task<WslcActionResult> RemoveVolumeAsync(string name, CancellationToken ct = default)
        => RunActionAsync(new[] { "volume", "remove", name }, ActionTimeout, ct);

    public Task<WslcActionResult> PruneVolumesAsync(bool all = false, CancellationToken ct = default)
    {
        var args = new List<string> { "volume", "prune" };
        if (all) args.Add("--all");
        return RunActionAsync(args, ActionTimeout, ct);
    }

    // ── Networks ─────────────────────────────────────────────────────────────

    /// <summary>Lists networks via `wslc network list --format json`. Returns empty on any
    /// failure or unparseable output; never throws.</summary>
    public async Task<IReadOnlyList<WslcNetwork>> ListNetworksAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _runner.RunAsync("wslc.exe", new[] { "network", "list", "--format", "json" }, ListTimeout, ct);
            return r.ExitCode == 0 ? ParseNetworks(r.StdOut) : Array.Empty<WslcNetwork>();
        }
        catch
        {
            return Array.Empty<WslcNetwork>();
        }
    }

    /// <summary><paramref name="driver"/> defaults to `bridge` when omitted (the CLI's own
    /// default).</summary>
    public Task<WslcActionResult> CreateNetworkAsync(string name, string? driver = null, CancellationToken ct = default)
    {
        var args = new List<string> { "network", "create" };
        if (!string.IsNullOrWhiteSpace(driver)) { args.Add("--driver"); args.Add(driver); }
        args.Add(name);
        return RunActionAsync(args, ActionTimeout, ct);
    }

    public Task<WslcActionResult> RemoveNetworkAsync(string name, CancellationToken ct = default)
        => RunActionAsync(new[] { "network", "remove", name }, ActionTimeout, ct);

    public Task<WslcActionResult> PruneNetworksAsync(CancellationToken ct = default)
        => RunActionAsync(new[] { "network", "prune" }, ActionTimeout, ct);

    // ── Registry ─────────────────────────────────────────────────────────────

    /// <summary>Logs in via `registry login --password-stdin`. The password is piped over stdin —
    /// never placed on argv, since argv is world-readable via WMI/Task Manager on Windows — and is
    /// not logged, cached, or retained beyond this call.</summary>
    public async Task<WslcActionResult> LoginAsync(string? server, string username, string password, CancellationToken ct = default)
    {
        var args = new List<string> { "registry", "login", "--username", username, "--password-stdin" };
        if (!string.IsNullOrWhiteSpace(server)) args.Add(server);
        try
        {
            var r = await _runner.RunWithInputAsync("wslc.exe", args.ToArray(), password, LoginTimeout, ct);
            return new WslcActionResult(r.ExitCode == 0, r.ExitCode, r.StdErr ?? "");
        }
        catch (WslException ex) when (ex.Kind == WslErrorKind.Timeout)
        {
            return WslcActionResult.Failed("wslc timed out.");
        }
        catch (Win32Exception ex)
        {
            return WslcActionResult.Failed($"Could not start wslc.exe: {ex.Message}");
        }
    }

    public Task<WslcActionResult> LogoutAsync(string? server = null, CancellationToken ct = default)
    {
        var args = new List<string> { "registry", "logout" };
        if (!string.IsNullOrWhiteSpace(server)) args.Add(server);
        return RunActionAsync(args, ActionTimeout, ct);
    }

    // ── Shared plumbing ──────────────────────────────────────────────────────

    private async Task<WslcActionResult> RunActionAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var r = await _runner.RunAsync("wslc.exe", args.ToArray(), timeout, ct);
            return new WslcActionResult(r.ExitCode == 0, r.ExitCode, r.StdErr ?? "");
        }
        catch (WslException ex) when (ex.Kind == WslErrorKind.Timeout)
        {
            return WslcActionResult.Failed("wslc timed out.");
        }
        catch (Win32Exception ex)
        {
            return WslcActionResult.Failed($"Could not start wslc.exe: {ex.Message}");
        }
    }

    internal static IReadOnlyList<WslcImage> ParseImages(string stdout)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<List<WslcImageJson>>(stdout);
            if (raw is null) return Array.Empty<WslcImage>();

            var images = new List<WslcImage>(raw.Count);
            foreach (var i in raw)
            {
                var shortId = i.Id.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? i.Id["sha256:".Length..]
                    : i.Id;
                if (shortId.Length > 12) shortId = shortId[..12];

                images.Add(new WslcImage(
                    FullId: i.Id,
                    ShortId: shortId,
                    Repository: i.Repository,
                    Tag: i.Tag,
                    Created: DateTimeOffset.FromUnixTimeSeconds(i.Created),
                    SizeBytes: i.Size,
                    SizeHuman: FormatSize(i.Size)));
            }
            return images;
        }
        catch (JsonException)
        {
            return Array.Empty<WslcImage>();
        }
    }

    internal static IReadOnlyList<WslcVolume> ParseVolumes(string stdout)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<List<WslcVolumeJson>>(stdout);
            return raw is null
                ? Array.Empty<WslcVolume>()
                : raw.Select(v => new WslcVolume(v.Driver, v.Name)).ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<WslcVolume>();
        }
    }

    internal static IReadOnlyList<WslcNetwork> ParseNetworks(string stdout)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<List<WslcNetworkJson>>(stdout);
            return raw is null
                ? Array.Empty<WslcNetwork>()
                : raw.Select(n => new WslcNetwork(n.Driver, n.Id, n.Name)).ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<WslcNetwork>();
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.##} {units[unit]}";
    }
}
