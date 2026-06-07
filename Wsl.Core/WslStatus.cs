namespace Wsl.Core;

public record WslStatus(string? DefaultDistro, int? DefaultVersion, string Raw);

public record WslVersionInfo(string? WslVersion, string? KernelVersion, string? WslgVersion, string Raw)
{
    public Version WslVersionParsed =>
        Version.TryParse(WslVersion, out var v) ? v : new Version(0, 0);
}
