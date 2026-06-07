namespace Wsl.Core;

public record MovePreflightResult(bool Ok, IReadOnlyList<string> Failures);

public static class MovePreflight
{
    public static readonly Version MinWslVersion = new(2, 0, 14);

    public static MovePreflightResult Evaluate(
        Version wslVersion, long vhdxSizeBytes, long targetFreeBytes, string targetDriveFormat)
    {
        var failures = new List<string>();
        if (wslVersion < MinWslVersion)
            failures.Add($"WSL {MinWslVersion} or newer required for safe move (found {wslVersion}).");
        var needed = (long)(vhdxSizeBytes * 1.1);
        if (targetFreeBytes < needed)
            failures.Add($"Not enough free space: need {needed / 1_073_741_824.0:F1} GB (incl. 10% buffer).");
        if (!string.Equals(targetDriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            failures.Add($"Target drive must be NTFS (found {targetDriveFormat}).");
        return new MovePreflightResult(failures.Count == 0, failures);
    }
}
