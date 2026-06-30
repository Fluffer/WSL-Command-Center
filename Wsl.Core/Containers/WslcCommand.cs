using System.Text;

namespace Wsl.Core.Containers;

/// <summary>
/// Pure helpers for the wslc raw-command surface: tokenizing a typed command line into argv
/// (quote-aware, no shell) and classifying whether the leading verb is read-only. Used to decide
/// when a confirmation prompt is required before running a potentially state-changing subcommand.
/// </summary>
public static class WslcCommand
{
    // Subcommands known to be observational. Anything not here is treated as potentially
    // state-changing and requires explicit user confirmation in the UI.
    private static readonly HashSet<string> ReadOnlyVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ps", "list", "ls", "version", "inspect", "logs", "log", "images",
        "image", "info", "status", "stats", "top", "help",
        "--version", "--help", "-h",
    };

    public static bool IsReadOnlyVerb(string? verb)
        => verb is not null && ReadOnlyVerbs.Contains(verb.Trim());

    /// <summary>The leading verb (first token) of a command line, or "" if blank.</summary>
    public static string FirstVerb(string? input)
    {
        var tokens = Tokenize(input);
        return tokens.Count > 0 ? tokens[0] : "";
    }

    /// <summary>True when the command line's leading verb is observational (no confirmation needed).</summary>
    public static bool IsReadOnly(string? input) => IsReadOnlyVerb(FirstVerb(input));

    /// <summary>
    /// Splits a command line into argv. Double quotes group tokens (so container names with spaces
    /// survive); there is no shell, so metacharacters like ; &amp; | are passed to wslc verbatim as
    /// argument text rather than interpreted.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? input)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return tokens;

        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var c in input)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }
}
