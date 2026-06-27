using System.Text;

namespace Wsl.Core.Scheduling;

/// <summary>
/// Task Scheduler integration via schtasks.exe (routed through IProcessRunner so it is fully
/// testable). Each schedule generates a .ps1 in a no-space scripts directory; the scheduled
/// task runs `powershell -File &lt;script&gt;` — keeping the schtasks /TR value free of inner quotes.
/// Backups use `wsl --export`, which is unelevated, so the broker is never involved.
/// </summary>
public sealed class WslScheduleService : IWslScheduleService
{
    public const string TaskPrefix = "WslCmdCenter_Backup_";

    private readonly IProcessRunner _runner;
    private readonly string _scriptsDir;

    /// <summary>Production ctor — scripts live under a no-space %LOCALAPPDATA% path.</summary>
    public WslScheduleService(IProcessRunner runner)
        : this(runner, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WslCommandCenter", "schedules"))
    {
    }

    /// <summary>Test ctor — explicit scripts directory.</summary>
    public WslScheduleService(IProcessRunner runner, string scriptsDir)
    {
        _runner = runner;
        _scriptsDir = scriptsDir;
    }

    public string TaskNameFor(string distro)
    {
        var sb = new StringBuilder(TaskPrefix);
        foreach (var c in distro)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }

    public string BuildScript(BackupSchedule s)
    {
        var name = PsQuote(s.DistroName);                 // 'Ubuntu'
        var folder = PsQuote(s.Folder);                   // 'C:\backups'
        var ext = Extension(s.Format);
        var fmt = FormatFlag(s.Format);
        var prefix = PsQuote(s.DistroName + "-");         // 'Ubuntu-'
        var glob = PsQuote(s.DistroName + "-*." + ext);   // 'Ubuntu-*.tar'
        return string.Join("\r\n", new[]
        {
            "$ErrorActionPreference = 'Stop'",
            "[Console]::OutputEncoding = [System.Text.Encoding]::Unicode",
            "$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'",
            $"$out = Join-Path {folder} ({prefix} + $stamp + '.{ext}')",
            // Capture running distros (names only; trim + drop blanks for the UTF-16 output).
            "$running = @(wsl.exe --list --running --quiet | " +
                "ForEach-Object { $_.Trim() } | Where-Object { $_ })",
            "wsl.exe --shutdown",
            "try {",
            $"    wsl.exe --export {name} $out --format {fmt}",
            $"    Get-ChildItem -LiteralPath {folder} -Filter {glob} | " +
                $"Sort-Object LastWriteTime -Descending | Select-Object -Skip {s.KeepCount} | " +
                "Remove-Item -Force",
            "}",
            "finally {",
            "    foreach ($d in $running) { wsl.exe -d $d -- true }",
            "}",
            "",
        });
    }

    public async Task CreateAsync(BackupSchedule schedule, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_scriptsDir);
        var taskName = TaskNameFor(schedule.DistroName);
        var scriptPath = Path.Combine(_scriptsDir, taskName + ".ps1");
        await File.WriteAllTextAsync(scriptPath, BuildScript(schedule), ct);

        var sc = schedule.Frequency == ScheduleFrequency.Weekly ? "WEEKLY" : "DAILY";
        var tr = $"powershell -NoProfile -ExecutionPolicy Bypass -File {scriptPath}";
        var args = new[] { "/Create", "/TN", taskName, "/SC", sc, "/ST", schedule.Time, "/TR", tr, "/F" };
        var result = await _runner.RunAsync("schtasks.exe", args, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Schedule backup for {schedule.DistroName}");
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("schtasks.exe", new[] { "/Query", "/FO", "CSV", "/NH" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, "List scheduled backups");

        var names = new List<string>();
        foreach (var line in result.StdOut.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // First CSV field is the task path, e.g. "\WslCmdCenter_Backup_Ubuntu".
            var first = line.Split(',')[0].Trim().Trim('"').TrimStart('\\');
            if (first.StartsWith(TaskPrefix, StringComparison.Ordinal))
                names.Add(first);
        }
        return names;
    }

    public async Task DeleteAsync(string taskName, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("schtasks.exe",
            new[] { "/Delete", "/TN", taskName, "/F" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Delete scheduled task {taskName}");
    }

    private static string Extension(ExportFormat fmt) => fmt switch
    {
        ExportFormat.Tar => "tar",
        ExportFormat.TarGz => "tar.gz",
        ExportFormat.Vhd => "vhdx",
        _ => throw new ArgumentOutOfRangeException(nameof(fmt))
    };

    private static string FormatFlag(ExportFormat fmt) => fmt switch
    {
        ExportFormat.Tar => "tar",
        ExportFormat.TarGz => "tar.gz",
        ExportFormat.Vhd => "vhd",
        _ => throw new ArgumentOutOfRangeException(nameof(fmt))
    };

    /// <summary>Wrap a value as a single-quoted PowerShell literal, doubling embedded quotes.</summary>
    private static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";
}
