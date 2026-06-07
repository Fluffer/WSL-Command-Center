namespace Wsl.Core;

/// <summary>Composes wsl.exe argument vectors for interactive launches.
/// Pure logic — the actual console window is started app-side (TerminalLauncher).
/// Command handling: with "--" the command is appended as a single argument
/// (the shell parses quotes); with "--exec" it is tokenized quote-aware
/// (double-quoted segments form one token, quotes stripped).</summary>
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
            if (o.UseExec)
            {
                args.Add("--exec");
                args.AddRange(Tokenize(o.Command));
            }
            else
            {
                args.Add("--");
                args.Add(o.Command.Trim());
            }
        }
        return args.ToArray();
    }

    /// <summary>Whitespace-splits except inside double quotes (quotes stripped).
    /// No escape sequences; an unclosed quote treats the remainder as one token.</summary>
    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var hasContent = false;

        foreach (var c in command)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasContent = true;
            }
            else if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasContent) { tokens.Add(current.ToString()); current.Clear(); hasContent = false; }
            }
            else
            {
                current.Append(c);
                hasContent = true;
            }
        }
        if (hasContent) tokens.Add(current.ToString());
        return tokens;
    }
}
