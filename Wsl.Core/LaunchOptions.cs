namespace Wsl.Core;

/// <summary>How wsl.exe should start the shell (`--shell-type`). Default omits the flag.</summary>
public enum WslShellType { Default, Standard, Login, None }

/// <summary>Optional launch parameters for an interactive distro session
/// (mirrors wsl.exe --user/--cd/--shell-type/--exec/--system).</summary>
public class LaunchOptions
{
    public string? User { get; set; }
    public string? WorkingDirectory { get; set; }
    public WslShellType ShellType { get; set; } = WslShellType.Default;
    public string? Command { get; set; }
    public bool UseExec { get; set; }
    public bool SystemDistro { get; set; }
}
