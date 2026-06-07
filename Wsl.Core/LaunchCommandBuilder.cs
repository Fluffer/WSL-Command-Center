namespace Wsl.Core;

/// <summary>Composes wsl.exe argument vectors for interactive launches.
/// Pure logic — the actual console window is started app-side (TerminalLauncher).</summary>
public static class LaunchCommandBuilder
{
    public static string[] Build(string distro, LaunchOptions o)
    {
        var args = new List<string>();
        if (o.SystemDistro) args.Add("--system");
        else { args.Add("-d"); args.Add(distro); }

        if (!string.IsNullOrWhiteSpace(o.User)) { args.Add("--user"); args.Add(o.User); }
        if (!string.IsNullOrWhiteSpace(o.WorkingDirectory)) { args.Add("--cd"); args.Add(o.WorkingDirectory); }
        if (o.ShellType != WslShellType.Default)
        { args.Add("--shell-type"); args.Add(o.ShellType.ToString().ToLowerInvariant()); }

        if (!string.IsNullOrWhiteSpace(o.Command))
        {
            if (o.UseExec) args.Add("--exec");
            else args.Add("--");
            args.AddRange(o.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return args.ToArray();
    }
}
