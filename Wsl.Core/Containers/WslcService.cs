using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wsl.Core.Containers;

/// <summary>
/// Thin, defensive wrapper over the preview <c>wslc</c> CLI. Every call is time-bound and never
/// throws to the caller for the expected failure modes (wslc absent, hung, or emitting an
/// unfamiliar format): detection degrades to a state enum, listing/stats degrade to an empty
/// list, and every lifecycle/raw call degrades to a non-zero <see cref="RawResult"/>. The raw
/// runner is the authoritative surface while the preview CLI's structured output churns.
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

    /// <summary>
    /// Lists containers via `wslc list --all --format json`. Falls back to the columnar
    /// `wslc list --all` table when the JSON invocation fails (non-zero exit) or its payload
    /// doesn't deserialize into the expected shape — the preview CLI's JSON format may churn.
    /// Never throws; degrades to an empty list on any unrecoverable failure.
    /// </summary>
    public async Task<IReadOnlyList<WslcContainer>> ListContainersAsync(CancellationToken ct = default)
    {
        try
        {
            var jsonResult = await _runner.RunAsync(
                "wslc.exe", new[] { "list", "--all", "--format", "json" }, ListTimeout, ct);
            if (jsonResult.ExitCode == 0)
            {
                var parsed = TryParseJsonContainers(jsonResult.StdOut);
                if (parsed is not null) return parsed;
            }

            var tableResult = await _runner.RunAsync("wslc.exe", new[] { "list", "--all" }, ListTimeout, ct);
            return tableResult.ExitCode == 0 ? ParsePs(tableResult.StdOut) : Array.Empty<WslcContainer>();
        }
        catch
        {
            return Array.Empty<WslcContainer>();
        }
    }

    /// <summary>Live resource usage via `wslc stats --all --format json`. Degrades to an empty
    /// list on any failure or unparseable output; never throws.</summary>
    public async Task<IReadOnlyList<WslcContainerStats>> GetStatsAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _runner.RunAsync(
                "wslc.exe", new[] { "stats", "--all", "--format", "json" }, ListTimeout, ct);
            return r.ExitCode == 0 ? ParseStats(r.StdOut) : Array.Empty<WslcContainerStats>();
        }
        catch
        {
            return Array.Empty<WslcContainerStats>();
        }
    }

    /// <summary>`wslc start &lt;id&gt;`.</summary>
    public Task<RawResult> StartAsync(string id, CancellationToken ct = default)
        => RunForResultAsync(new[] { "start", id }, ct);

    /// <summary>`wslc stop [-t &lt;sec&gt;] &lt;id&gt;`.</summary>
    public Task<RawResult> StopAsync(string id, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var args = new List<string> { "stop" };
        if (timeoutSeconds is not null) { args.Add("-t"); args.Add(timeoutSeconds.Value.ToString()); }
        args.Add(id);
        return RunForResultAsync(args.ToArray(), ct);
    }

    /// <summary>`wslc kill [-s &lt;signal&gt;] &lt;id&gt;`.</summary>
    public Task<RawResult> KillAsync(string id, string? signal = null, CancellationToken ct = default)
    {
        var args = new List<string> { "kill" };
        if (!string.IsNullOrWhiteSpace(signal)) { args.Add("-s"); args.Add(signal); }
        args.Add(id);
        return RunForResultAsync(args.ToArray(), ct);
    }

    /// <summary>`wslc remove [-f] &lt;id&gt;`.</summary>
    public Task<RawResult> RemoveAsync(string id, bool force = false, CancellationToken ct = default)
    {
        var args = new List<string> { "remove" };
        if (force) args.Add("-f");
        args.Add(id);
        return RunForResultAsync(args.ToArray(), ct);
    }

    /// <summary>`wslc container prune` — removes all stopped containers. There is no top-level
    /// `prune` alias for containers, unlike most other verbs.</summary>
    public Task<RawResult> PruneContainersAsync(CancellationToken ct = default)
        => RunForResultAsync(new[] { "container", "prune" }, ct);

    /// <summary>
    /// Restarts a container by composing `stop` then `start` — the contract confirms there is no
    /// `wslc restart` verb. Stops with the given timeout (if any); if the stop fails, the start is
    /// not attempted and the stop's <see cref="RawResult"/> is returned.
    /// </summary>
    public async Task<RawResult> RestartAsync(string id, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var stopResult = await StopAsync(id, timeoutSeconds, ct);
        return stopResult.Ok ? await StartAsync(id, ct) : stopResult;
    }

    /// <summary>`wslc logs [-n &lt;n&gt;] [-t] &lt;id&gt;`. Never passes `--follow` — streaming
    /// logs are out of scope for this surface.</summary>
    public Task<RawResult> GetLogsAsync(
        string id, int? tailLines = null, bool timestamps = false, CancellationToken ct = default)
    {
        var args = new List<string> { "logs" };
        if (tailLines is not null) { args.Add("-n"); args.Add(tailLines.Value.ToString()); }
        if (timestamps) args.Add("-t");
        args.Add(id);
        return RunForResultAsync(args.ToArray(), ct);
    }

    /// <summary>`wslc exec [-u &lt;user&gt;] [-w &lt;dir&gt;] &lt;id&gt; &lt;command...&gt;`.
    /// Never passes `-i`/`-t` — interactive exec is out of scope for this surface.</summary>
    public Task<RawResult> ExecAsync(
        string id, IReadOnlyList<string> command, string? user = null, string? workdir = null,
        CancellationToken ct = default)
    {
        var args = new List<string> { "exec" };
        if (!string.IsNullOrWhiteSpace(user)) { args.Add("-u"); args.Add(user); }
        if (!string.IsNullOrWhiteSpace(workdir)) { args.Add("-w"); args.Add(workdir); }
        args.Add(id);
        args.AddRange(command);
        return RunForResultAsync(args.ToArray(), ct);
    }

    /// <summary>`wslc run --detach &lt;options&gt; &lt;image&gt; [command...]`. Always passes
    /// `--detach` — a non-detached run blocks in the foreground, which the app must never do.</summary>
    public Task<RawResult> RunDetachedAsync(WslcRunOptions options, CancellationToken ct = default)
    {
        var args = new List<string> { "run", "--detach" };
        args.AddRange(BuildRunArgs(options));
        return RunForResultAsync(args.ToArray(), ct);
    }

    /// <summary>`wslc create &lt;options&gt; &lt;image&gt; [command...]`.</summary>
    public Task<RawResult> CreateAsync(WslcRunOptions options, CancellationToken ct = default)
    {
        var args = new List<string> { "create" };
        args.AddRange(BuildRunArgs(options));
        return RunForResultAsync(args.ToArray(), ct);
    }

    /// <summary>
    /// `wslc inspect &lt;id&gt;`. Returns the raw pretty-printed JSON via <see cref="RawResult.StdOut"/>
    /// for display — inspect's `State` is a nested object, unlike the integer `State` in `list`, so
    /// this deliberately does not build a typed model on top of it.
    /// </summary>
    public Task<RawResult> InspectAsync(string id, CancellationToken ct = default)
        => RunForResultAsync(new[] { "inspect", id }, ct);

    /// <summary>Runs an arbitrary wslc subcommand (the caller has already split + confirmed it).
    /// Args are passed as argv with no shell, so metacharacters cannot inject host commands.
    /// Failure modes degrade to a non-zero <see cref="RawResult"/> rather than throwing.</summary>
    public async Task<RawResult> RunRawAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        if (args.Count == 0) return new RawResult(-1, "", "No command entered.");
        return await RunForResultAsync(args.ToArray(), ct);
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

    /// <summary>Shared runner for lifecycle/raw calls: dispatches through the injected
    /// <see cref="IProcessRunner"/> and degrades expected failure modes to a non-zero
    /// <see cref="RawResult"/> instead of throwing.</summary>
    private async Task<RawResult> RunForResultAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var r = await _runner.RunAsync("wslc.exe", args, RawTimeout, ct);
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

    /// <summary>Builds the option flags shared by `run` and `create`, ending with the image and
    /// (if given) the command to run inside it.</summary>
    private static List<string> BuildRunArgs(WslcRunOptions options)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Name)) { args.Add("--name"); args.Add(options.Name); }
        if (!string.IsNullOrWhiteSpace(options.Memory)) { args.Add("-m"); args.Add(options.Memory); }
        if (!string.IsNullOrWhiteSpace(options.Cpus)) { args.Add("--cpus"); args.Add(options.Cpus); }
        if (!string.IsNullOrWhiteSpace(options.Network)) { args.Add("--network"); args.Add(options.Network); }
        if (options.Remove) args.Add("--rm");

        foreach (var (key, value) in options.Env ?? new Dictionary<string, string>())
        {
            args.Add("-e");
            args.Add($"{key}={value}");
        }
        foreach (var p in options.PublishedPorts ?? Array.Empty<string>())
        {
            args.Add("-p");
            args.Add(p);
        }
        foreach (var v in options.Volumes ?? Array.Empty<string>())
        {
            args.Add("-v");
            args.Add(v);
        }

        args.Add(options.Image);
        if (options.Command is { Count: > 0 }) args.AddRange(options.Command);
        return args;
    }

    private static string? FirstLine(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var line = s.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
    }

    /// <summary>Maps the integer `State` field from `list --format json` (1=Created, 2=Running,
    /// 3=Exited) to <see cref="WslcContainerState"/>. Anything else, including an absent value,
    /// maps to <see cref="WslcContainerState.Unknown"/> rather than assuming a meaning for it.</summary>
    internal static WslcContainerState MapState(int? raw) => raw switch
    {
        1 => WslcContainerState.Created,
        2 => WslcContainerState.Running,
        3 => WslcContainerState.Exited,
        _ => WslcContainerState.Unknown,
    };

    /// <summary>
    /// Parses the `wslc list --all --format json` payload. Returns null (never an empty list) when
    /// the payload isn't the expected JSON array of container objects, so the caller can fall back
    /// to the columnar parser — a genuinely empty result set is `[]`, which parses to an empty list
    /// here, not null.
    /// </summary>
    internal static IReadOnlyList<WslcContainer>? TryParseJsonContainers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var containers = new List<WslcContainer>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var container = ParseJsonContainer(el);
                if (container is not null) containers.Add(container);
            }
            return containers;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static WslcContainer? ParseJsonContainer(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        var id = GetString(el, "Id");
        if (string.IsNullOrEmpty(id)) return null; // no usable identifier — skip the row

        var name = GetString(el, "Name") ?? "";
        var image = GetString(el, "Image") ?? "";
        var rawState = el.TryGetProperty("State", out var stateProp) && stateProp.ValueKind == JsonValueKind.Number
            ? stateProp.GetInt32() : (int?)null;
        var state = MapState(rawState);
        var createdAt = GetUnixSeconds(el, "CreatedAt");
        var stateChangedAt = GetUnixSeconds(el, "StateChangedAt");
        var ports = el.TryGetProperty("Ports", out var portsProp) ? FormatPorts(portsProp) : "";
        var shortId = id.Length > 12 ? id[..12] : id;

        return new WslcContainer(id, shortId, name, image, createdAt, stateChangedAt, state, ports, state.ToString());
    }

    /// <summary>Renders the `Ports` array as a human-readable joined string. Its shape when
    /// non-empty hasn't been observed against the live CLI, so this treats entries defensively —
    /// strings pass through as-is, anything else falls back to its raw JSON text.</summary>
    private static string FormatPorts(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array) return "";
        var parts = new List<string>();
        foreach (var item in el.EnumerateArray())
            parts.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.GetRawText());
        return string.Join(", ", parts.Where(p => p.Length > 0));
    }

    private static string? GetString(JsonElement el, string property)
        => el.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static DateTimeOffset? GetUnixSeconds(JsonElement el, string property)
        => el.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(prop.GetInt64())
            : null;

    /// <summary>Parses the `wslc stats --all --format json` payload. Handles the `ID`/`Id` key
    /// discrepancy between `stats` and `list`. Degrades to an empty list on malformed JSON.</summary>
    internal static IReadOnlyList<WslcContainerStats> ParseStats(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<WslcContainerStats>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<WslcContainerStats>();

            var stats = new List<WslcContainerStats>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var id = GetString(el, "Id") ?? GetString(el, "ID") ?? "";
                var name = GetString(el, "Name") ?? "";
                var cpu = GetString(el, "CPUPerc") ?? "";
                var memUsage = GetString(el, "MemUsage") ?? "";
                var memPerc = GetString(el, "MemPerc") ?? "";
                var netIo = GetString(el, "NetIO") ?? "";
                var blockIo = GetString(el, "BlockIO") ?? "";
                var pids = el.TryGetProperty("PIDs", out var pidsProp) && pidsProp.ValueKind == JsonValueKind.Number
                    ? pidsProp.GetInt32() : 0;
                stats.Add(new WslcContainerStats(id, name, cpu, memPerc, memUsage, netIo, blockIo, pids));
            }
            return stats;
        }
        catch (JsonException)
        {
            return Array.Empty<WslcContainerStats>();
        }
    }

    /// <summary>
    /// Best-effort parse of `wslc list --all`-style columnar output, used only as a fallback when
    /// the JSON path fails. Assumes a header line followed by rows whose columns are separated by
    /// runs of 2+ spaces, ordered CONTAINER ID, NAME, IMAGE, CREATED, STATUS, PORTS — verified
    /// against the live CLI. This is a heuristic; the JSON path and the raw runner remain
    /// authoritative. Timestamps aren't available as epoch values here, so
    /// <see cref="WslcContainer.CreatedAt"/>/<see cref="WslcContainer.StateChangedAt"/> are left
    /// null and <see cref="WslcContainer.State"/> is left <see cref="WslcContainerState.Unknown"/>
    /// — the raw CREATED/STATUS text lands in <see cref="WslcContainer.Status"/> instead of being
    /// guessed at.
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

            var fields = Regex.Split(line.Trim(), @"\s{2,}").Where(f => f.Length > 0).ToArray();
            if (fields.Length == 0) continue;

            var id = fields[0];
            var name = fields.Length > 1 ? fields[1] : "";
            var image = fields.Length > 2 ? fields[2] : "";
            var created = fields.Length > 3 ? fields[3] : "";
            var status = fields.Length > 4 ? fields[4] : "";
            var ports = fields.Length > 5 ? string.Join(' ', fields[5..]) : "";
            // CREATED has no epoch equivalent in the table; fold it into Status only when STATUS
            // itself is missing so callers always have something human-readable to show.
            var displayStatus = status.Length > 0 ? status : created;

            containers.Add(new WslcContainer(
                id, id, name, image, null, null, WslcContainerState.Unknown, ports, displayStatus));
        }
        return containers;
    }
}
