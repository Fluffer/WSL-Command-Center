using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wsl.Core;
using Wsl.Core.Scheduling;
using Xunit;

namespace Wsl.Core.Tests;

public class WslScheduleServiceTests
{
    private static (WslScheduleService svc, FakeProcessRunner runner, string dir) Make()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var runner = new FakeProcessRunner();
        return (new WslScheduleService(runner, dir), runner, dir);
    }

    [Fact]
    public void TaskNameFor_SanitizesNonAlphanumeric()
    {
        var (svc, _, _) = Make();
        Assert.Equal("WslCmdCenter_Backup_My_Distro_1", svc.TaskNameFor("My Distro.1"));
    }

    [Fact]
    public void BuildScript_Tar_HasExportAndRetentionLines()
    {
        var (svc, _, _) = Make();
        var s = new BackupSchedule("Ubuntu", @"C:\backups", ExportFormat.Tar,
                                   ScheduleFrequency.Daily, "02:30", 7);
        var script = svc.BuildScript(s);

        Assert.Contains("wsl.exe --export 'Ubuntu' $out --format tar", script);
        Assert.Contains("'Ubuntu-' + $stamp + '.tar'", script);
        Assert.Contains("-Filter 'Ubuntu-*.tar'", script);
        Assert.Contains("-Skip 7", script);
    }

    [Fact]
    public void BuildScript_GuardsExitCodeBeforePrune()
    {
        var (svc, _, _) = Make();
        var s = new BackupSchedule("Ubuntu", @"C:\backups", ExportFormat.Tar,
                                   ScheduleFrequency.Daily, "02:30", 7);
        var script = svc.BuildScript(s);

        Assert.Contains("$LASTEXITCODE -ne 0", script);
        // Guard must appear before the prune so a failed export cannot delete old backups.
        Assert.True(script.IndexOf("$LASTEXITCODE -ne 0", StringComparison.Ordinal)
                    < script.IndexOf("Remove-Item", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildScript_Vhd_UsesVhdxExtensionButVhdFormatFlag()
    {
        var (svc, _, _) = Make();
        var s = new BackupSchedule("Ubuntu", @"C:\b", ExportFormat.Vhd,
                                   ScheduleFrequency.Daily, "02:30", 3);
        var script = svc.BuildScript(s);

        Assert.Contains("--format vhd", script);
        Assert.Contains("'Ubuntu-' + $stamp + '.vhdx'", script);
        Assert.Contains("-Filter 'Ubuntu-*.vhdx'", script);
    }

    [Fact]
    public void BuildScript_EscapesSingleQuotes()
    {
        var (svc, _, _) = Make();
        var s = new BackupSchedule("o'brien", @"C:\b", ExportFormat.Tar,
                                   ScheduleFrequency.Daily, "02:30", 1);
        var script = svc.BuildScript(s);
        Assert.Contains("--export 'o''brien'", script);
    }

    [Fact]
    public async Task CreateAsync_WritesScriptFileAndSchedulesDaily()
    {
        var (svc, runner, dir) = Make();
        var s = new BackupSchedule("Ubuntu", @"C:\backups", ExportFormat.Tar,
                                   ScheduleFrequency.Daily, "02:30", 7);

        await svc.CreateAsync(s);

        var scriptPath = Path.Combine(dir, "WslCmdCenter_Backup_Ubuntu.ps1");
        Assert.True(File.Exists(scriptPath));

        Assert.Equal("schtasks.exe", runner.LastExe);
        var a = runner.LastArgs!;
        Assert.Equal("/Create", a[0]);
        Assert.Equal("/TN", a[1]);
        Assert.Equal("WslCmdCenter_Backup_Ubuntu", a[2]);
        Assert.Equal("/SC", a[3]);
        Assert.Equal("DAILY", a[4]);
        Assert.Equal("/ST", a[5]);
        Assert.Equal("02:30", a[6]);
        Assert.Equal("/TR", a[7]);
        Assert.Contains("powershell", a[8]);
        Assert.Contains(scriptPath, a[8]);
        Assert.Equal("/F", a[9]);
    }

    [Fact]
    public async Task CreateAsync_Weekly_UsesWeeklySchedule()
    {
        var (svc, runner, _) = Make();
        var s = new BackupSchedule("Ubuntu", @"C:\b", ExportFormat.Tar,
                                   ScheduleFrequency.Weekly, "03:00", 4);
        await svc.CreateAsync(s);
        Assert.Equal("WEEKLY", runner.LastArgs![4]);
    }

    [Fact]
    public async Task ListAsync_FiltersOurTaskPrefix()
    {
        var (svc, runner, _) = Make();
        runner.Enqueue(0,
            "\"\\WslCmdCenter_Backup_Ubuntu\",\"2/1/2026 2:30:00 AM\",\"Ready\"\r\n" +
            "\"\\Microsoft\\SomeOtherTask\",\"N/A\",\"Ready\"\r\n" +
            "\"\\WslCmdCenter_Backup_podman\",\"2/1/2026 3:00:00 AM\",\"Ready\"\r\n");

        var names = await svc.ListAsync();

        Assert.Equal(2, names.Count);
        Assert.Contains("WslCmdCenter_Backup_Ubuntu", names);
        Assert.Contains("WslCmdCenter_Backup_podman", names);
    }

    [Fact]
    public async Task DeleteAsync_IssuesSchtasksDelete()
    {
        var (svc, runner, _) = Make();
        await svc.DeleteAsync("WslCmdCenter_Backup_Ubuntu");
        var a = runner.LastArgs!;
        Assert.Equal("schtasks.exe", runner.LastExe);
        Assert.Equal(new[] { "/Delete", "/TN", "WslCmdCenter_Backup_Ubuntu", "/F" }, a);
    }

    [Fact]
    public void BuildScript_IsStatePreserving()
    {
        var svc = new WslScheduleService(new FakeProcessRunner(), Path.GetTempPath());
        var s = new Wsl.Core.Scheduling.BackupSchedule(
            "Ubuntu", @"C:\backups", ExportFormat.Tar,
            Wsl.Core.Scheduling.ScheduleFrequency.Daily, "02:00", 3);
        var script = svc.BuildScript(s);

        Assert.Contains("[Console]::OutputEncoding", script);          // UTF-16 fix
        Assert.Contains("--list --running --quiet", script);           // capture running
        Assert.Contains("wsl.exe --shutdown", script);                 // release VHDs
        Assert.Contains("--export 'Ubuntu' $out --format tar", script);// export
        Assert.Contains("finally", script);                            // restart wrapper
        Assert.Contains("-- true", script);                            // restart command
        // prune sits inside try (before finally) so a failed export keeps old backups
        Assert.True(script.IndexOf("Remove-Item", StringComparison.Ordinal)
                    < script.IndexOf("finally", StringComparison.Ordinal));
    }
}
