using System.ComponentModel;

namespace Wsl.Core.Containers;

/// <summary>
/// Thin, defensive wrapper over the preview <c>wslc</c> CLI. Every call is time-bound and never
/// throws to the caller for the expected failure modes (wslc absent, hung, or emitting an
/// unfamiliar format): detection degrades to a state enum, listing degrades to an empty list, and
/// the raw runner degrades to a non-zero <see cref="RawResult"/>. The raw runner is the
/// authoritative surface while the preview CLI's structured output churns.
/// </summary>
public class WslcService
{
    private readonly IProcessRunner _runner;

    private static readonly TimeSpan DetectTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RawTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(15);

    public WslcService(IProcessRunner runner) => _runner = runner;

    /// <summary>Probes for wslc. Distinguishes not-installed (exe missing) from
    /// installed-but-unreachable (timeout or non-zero exit).</summary>
    public async Task<WslcAvailability> DetectAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _runner.RunAsync("wslc.exe", new[] { "--version" }, DetectTimeout, ct);
            if (r.ExitCode == 0)
            {
                var version = FirstLine(r.StdOut);
                return new WslcAvailability(WslcState.Available, version);
            }
            return new WslcAvailability(WslcState.Unreachable, null);
        }
        catch (WslException ex) when (ex.Kind == WslErrorKind.Timeout)
        {
            return new WslcAvailability(WslcState.Unreachable, null);
        }
        catch (Win32Exception)
        {
            // ProcessStartInfo.Start() throws this when wslc.exe is not on PATH.
            return new WslcAvailability(WslcState.NotFound, null);
        }
    }

    /// <summary>Lists containers best-effort via `wslc ps`. Returns empty on any failure or
    /// unparseable output; never throws.</summary>
    public async Task<IReadOnlyList<WslcContainer>> ListContainersAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _runner.RunAsync("wslc.exe", new[] { "ps" }, ListTimeout, ct);
            return r.ExitCode == 0 ? ParsePs(r.StdOut) : Array.Empty<WslcContainer>();
        }
        catch
        {
            return Array.Empty<WslcContainer>();
        }
    }

    /// <summary>Runs an arbitrary wslc subcommand (the caller has already split + confirmed it).
    /// Args are passed as argv with no shell, so metacharacters cannot inject host commands.
    /// Failure modes degrade to a non-zero <see cref="RawResult"/> rather than throwing.</summary>
    public async Task<RawResult> RunRawAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        if (args.Count == 0) return new RawResult(-1, "", "No command entered.");
        try
        {
            var r = await _runner.RunAsync("wslc.exe", args.ToArray(), RawTimeout, ct);
            return new RawResult(r.ExitCode, r.StdOut ?? "", r.StdErr ?? "");
        }
        catch (WslException ex) when (ex.Kind == WslErrorKind.Timeout)
        {
            return new RawResult(-1, "", "wslc timed out.");
        }
        catch (Win32Exception ex)
        {
            // Typically "file not found" (wslc not on PATH), but also covers access-denied etc.
            return new RawResult(-1, "", $"Could not start wslc.exe: {ex.Message}");
        }
    }

    /// <summary>Detects which container runtime, if any, is installed inside a distro. Informational
    /// only — does not configure DOCKER_HOST or wire anything up. Returns None on any failure.</summary>
    public async Task<ContainerRuntime> DetectRuntimeAsync(string distro, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(distro)) return ContainerRuntime.None;
        try
        {
            // The detection script is a single argv element to `sh -lc`; the host shell never sees it.
            const string script =
                "command -v docker >/dev/null 2>&1 && echo docker || " +
                "{ command -v podman >/dev/null 2>&1 && echo podman || echo none; }";
            var r = await _runner.RunAsync("wsl.exe",
                new[] { "-d", distro, "--", "sh", "-lc", script }, BridgeTimeout, ct);
            var token = (r.StdOut ?? "").Trim().ToLowerInvariant();
            if (token.Contains("docker")) return ContainerRuntime.Docker;
            if (token.Contains("podman")) return ContainerRuntime.Podman;
            return ContainerRuntime.None;
        }
        catch
        {
            return ContainerRuntime.None;
        }
    }

    private static string? FirstLine(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var line = s.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
    }

    /// <summary>
    /// Best-effort parse of `wslc ps`-style columnar output. Assumes a header line followed by rows
    /// whose columns are separated by runs of 2+ spaces, ordered ID, IMAGE, STATUS…, NAME (mirroring
    /// docker ps). Rows that don't yield at least an ID are skipped. This is a heuristic; the raw
    /// runner remains authoritative.
    /// </summary>
    internal static IReadOnlyList<WslcContainer> ParsePs(string stdout)
    {
        var containers = new List<WslcContainer>();
        if (string.IsNullOrWhiteSpace(stdout)) return containers;

        var lines = stdout.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var headerSeen = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (!headerSeen)
            {
                var upper = line.ToUpperInvariant();
                if (upper.Contains("CONTAINER") || upper.Contains("IMAGE") || upper.StartsWith("ID")
                    || upper.Contains("NAME"))
                {
                    headerSeen = true;
                    continue;
                }
                // No recognizable header — treat all lines as data.
                headerSeen = true;
            }

            var fields = System.Text.RegularExpressions.Regex
                .Split(line.Trim(), @"\s{2,}")
                .Where(f => f.Length > 0)
                .ToArray();
            if (fields.Length == 0) continue;

            var id = fields[0];
            var image = fields.Length > 1 ? fields[1] : "";
            var name = fields.Length > 2 ? fields[^1] : "";
            var status = fields.Length > 3 ? string.Join(' ', fields[2..^1]) : "";
            containers.Add(new WslcContainer(id, name, image, status));
        }
        return containers;
    }
}
