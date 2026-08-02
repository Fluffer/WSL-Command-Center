namespace Wsl.Core;

public class WslBackupService
{
    private readonly IProcessRunner _runner;

    public WslBackupService(IProcessRunner runner) => _runner = runner;

    public async Task ExportAsync(string name, string outPath, ExportFormat fmt,
                                  CancellationToken ct = default)
    {
        // No timeout: exporting a real distro is minutes-to-hours of I/O (a 20 GB distro blows
        // straight past RealProcessRunner's 60s default and the whole clone/backup/snapshot chain
        // dies with "wsl.exe timed out"). Cancellation is the caller's job via ct, same as MoveAsync.
        var result = await _runner.RunAsync("wsl.exe",
            new[] { "--export", name, outPath, "--format", FormatFlag(fmt) },
            Timeout.InfiniteTimeSpan, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Export {name}");
    }

    public async Task RestoreAsync(string name, string installDir, string archivePath,
                                   ExportFormat sourceFmt, int version, CancellationToken ct = default)
    {
        var args = sourceFmt == ExportFormat.Vhd
            ? new[] { "--import", name, installDir, archivePath, "--vhd", "--version", version.ToString() }
            : new[] { "--import", name, installDir, archivePath, "--version", version.ToString() };
        // Same reasoning as ExportAsync — importing a multi-GB archive must not be killed midway.
        var result = await _runner.RunAsync("wsl.exe", args, Timeout.InfiniteTimeSpan, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Restore {name}");
    }

    private static string FormatFlag(ExportFormat fmt) => fmt switch
    {
        ExportFormat.Tar => "tar",
        ExportFormat.TarGz => "tar.gz",
        ExportFormat.Vhd => "vhd",
        _ => throw new ArgumentOutOfRangeException(nameof(fmt))
    };
}
