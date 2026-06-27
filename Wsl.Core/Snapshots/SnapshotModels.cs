namespace Wsl.Core.Snapshots;

public record Snapshot(string Distro, string Label, DateTime CreatedUtc, long Bytes,
    string Format, int WslVersion, string VhdxPath, string SidecarPath);
