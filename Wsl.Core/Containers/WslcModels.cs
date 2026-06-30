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

/// <summary>A container row parsed best-effort from `wslc ps`. All fields are best-effort —
/// the raw command runner is the authoritative surface while the preview CLI format churns.</summary>
public record WslcContainer(string Id, string Name, string Image, string Status);

/// <summary>Raw outcome of an arbitrary wslc invocation.</summary>
public record RawResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Container runtime detected inside a distro (suggestion/info only — no host wiring).</summary>
public enum ContainerRuntime { None, Docker, Podman }
