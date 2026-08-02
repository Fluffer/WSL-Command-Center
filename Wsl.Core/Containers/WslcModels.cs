namespace Wsl.Core.Containers;

/// <summary>Whether the wslc preview CLI is usable on this machine.</summary>
public enum WslcState
{
    /// <summary>wslc.exe was not found on PATH.</summary>
    NotFound,
    /// <summary>wslc responded successfully.</summary>
    Available,
    /// <summary>wslc exists but did not respond cleanly (timed out or errored).</summary>
    Unreachable,
}

/// <summary>Result of probing for the wslc CLI.</summary>
public record WslcAvailability(WslcState State, string? Version)
{
    public bool IsAvailable => State == WslcState.Available;
}

/// <summary>
/// A container's lifecycle state, mapped from the integer `State` field `wslc list --format json`
/// emits. Verified values: 1 = Created, 2 = Running, 3 = Exited. Any other value (including one
/// that hasn't been observed yet) maps to <see cref="Unknown"/> rather than being assumed away.
/// </summary>
public enum WslcContainerState
{
    Unknown,
    Created,
    Running,
    Exited,
}

/// <summary>
/// A container row from `wslc list --all`. <see cref="Id"/> is the full identifier when sourced
/// from JSON, or the truncated table id when parsed from the columnar fallback (in which case it
/// equals <see cref="ShortId"/>). <see cref="CreatedAt"/>/<see cref="StateChangedAt"/> are only
/// populated from the JSON path — the columnar fallback has no epoch timestamps, only display
/// text, which lands in <see cref="Status"/> instead. <see cref="State"/> is likewise only known
/// precisely from JSON; the columnar fallback reports <see cref="WslcContainerState.Unknown"/>
/// and leaves the raw status text for the caller to display.
/// </summary>
public record WslcContainer(
    string Id,
    string ShortId,
    string Name,
    string Image,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? StateChangedAt,
    WslcContainerState State,
    string Ports,
    string Status);

/// <summary>A single row from `wslc stats --all --format json`. The JSON key is `ID` there,
/// unlike `Id` in `list` — <see cref="WslcService"/> normalizes both into this one shape.</summary>
public record WslcContainerStats(
    string Id,
    string Name,
    string CpuPercent,
    string MemPercent,
    string MemUsage,
    string NetIO,
    string BlockIO,
    int Pids);

/// <summary>
/// Options shared by `wslc run` and `wslc create`. <see cref="WslcService"/> always adds
/// `--detach` for `run` — the contract confirms a non-detached run blocks in the foreground,
/// which the app must never do.
/// </summary>
public record WslcRunOptions(
    string Image,
    string? Name = null,
    IReadOnlyList<string>? Command = null,
    IReadOnlyDictionary<string, string>? Env = null,
    IReadOnlyList<string>? PublishedPorts = null,
    IReadOnlyList<string>? Volumes = null,
    string? Memory = null,
    string? Cpus = null,
    string? Network = null,
    bool Remove = false);

/// <summary>Raw outcome of an arbitrary wslc invocation.</summary>
public record RawResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Container runtime detected inside a distro (suggestion/info only — no host wiring).</summary>
public enum ContainerRuntime { None, Docker, Podman }
