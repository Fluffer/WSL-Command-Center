using System.Text.RegularExpressions;

namespace Wsl.Core.Containers;

/// <summary>
/// Comment-preserving reader/writer for the <c>wslc</c> container-runtime configuration file at
/// <c>%LOCALAPPDATA%\wslc\settings.yaml</c>. Mirrors <see cref="WslConfigService"/>'s approach for
/// <c>.wslconfig</c>: never rewrite the whole file from a template (only when it is missing), and
/// mutate exactly the line a key lives on so hand-written comments survive. Never throws for the
/// expected failure modes (missing file, unreadable file, permission denied) — those degrade to an
/// all-unset <see cref="WslcSettings"/> plus a message the UI can show.
/// </summary>
public class WslcSettingsService
{
    /// <summary>
    /// Session settings (cpuCount, memorySize, maxStorageSize, defaultBindingAddress) only take
    /// effect for a new wslc session. An already-running session keeps its previous values until
    /// it is terminated with <c>wslc system session terminate</c>.
    /// </summary>
    public const string SessionChangesRequireNewSessionMessage =
        "Session settings only take effect for a new wslc session — an already-running session " +
        "keeps its previous CPU, memory, and storage values until it is terminated.";

    private static readonly string[] SessionKeys =
        { "cpuCount", "memorySize", "maxStorageSize", "defaultBindingAddress" };

    private const string CredentialStoreKey = "credentialStore";

    private static readonly string[] DefaultTemplateLines =
    {
        "# wslc user settings",
        "# https://aka.ms/wslc-settings",
        "# All settings support string value \"default\" which uses built-in defaults.",
        "",
        "session:",
        "  # Number of virtual CPUs allocated to the session (e.g. 4 default: all available CPUs)",
        "  # cpuCount: default",
        "",
        "  # Memory limit for the session (e.g. 2GB default: half of available memory)",
        "  # memorySize: default",
        "",
        "  # Maximum disk image size (e.g. 500GB default: 1TB)",
        "  # maxStorageSize: default",
        "",
        "  # Default host address that published ports bind to when 'container run -p' is",
        "  # used without an explicit address (default: 127.0.0.1)",
        "  # defaultBindingAddress: default",
        "",
        "# Credential storage backend: \"wincred\" or \"file\" (default: wincred)",
        "# credentialStore: wincred",
    };

    private readonly Func<string> _settingsPathProvider;

    public WslcSettingsService(Func<string>? settingsPathProvider = null)
    {
        _settingsPathProvider = settingsPathProvider ?? DefaultSettingsPath;
    }

    private static string DefaultSettingsPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wslc", "settings.yaml");

    /// <summary>Resolves <c>%LOCALAPPDATA%\wslc\settings.yaml</c> (or the overridden path in tests).</summary>
    public string SettingsFilePath => _settingsPathProvider();

    public async Task<WslcSettingsReadResult> ReadAsync(CancellationToken ct = default)
    {
        var path = SettingsFilePath;
        if (!File.Exists(path))
            return new WslcSettingsReadResult { Settings = new WslcSettings(), FileExists = false };

        string text;
        try
        {
            text = await File.ReadAllTextAsync(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WslcSettingsReadResult { Settings = new WslcSettings(), FileExists = true, ErrorMessage = ex.Message };
        }

        var lines = SplitLines(text, out _);
        var settings = new WslcSettings
        {
            CpuCount = ReadValue(lines, "cpuCount"),
            MemorySize = ReadValue(lines, "memorySize"),
            MaxStorageSize = ReadValue(lines, "maxStorageSize"),
            DefaultBindingAddress = ReadValue(lines, "defaultBindingAddress"),
            CredentialStore = ParseCredentialStore(ReadValue(lines, CredentialStoreKey)),
        };
        return new WslcSettingsReadResult { Settings = settings, FileExists = true };
    }

    public async Task<WslcSettingsWriteResult> WriteAsync(WslcSettings settings, CancellationToken ct = default)
    {
        var path = SettingsFilePath;
        try
        {
            string original;
            bool isNewFile;
            if (File.Exists(path))
            {
                original = await File.ReadAllTextAsync(path, ct);
                isNewFile = false;
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                original = string.Join("\r\n", DefaultTemplateLines) + "\r\n";
                isNewFile = true;
            }

            var lines = SplitLines(original, out var hadTrailingNewline);
            var newline = isNewFile ? "\r\n" : DetectLineEnding(original);

            ApplyKey(lines, "cpuCount", settings.CpuCount, isSessionKey: true);
            ApplyKey(lines, "memorySize", settings.MemorySize, isSessionKey: true);
            ApplyKey(lines, "maxStorageSize", settings.MaxStorageSize, isSessionKey: true);
            ApplyKey(lines, "defaultBindingAddress", settings.DefaultBindingAddress, isSessionKey: true);
            ApplyKey(lines, CredentialStoreKey, FormatCredentialStore(settings.CredentialStore), isSessionKey: false);

            var content = string.Join(newline, lines);
            if (hadTrailingNewline) content += newline;

            await WriteAtomicAsync(path, content, ct);
            return new WslcSettingsWriteResult { Success = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WslcSettingsWriteResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        var tmp = Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, content, ct);
        File.Move(tmp, path, overwrite: true);
    }

    private static string DetectLineEnding(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

    private static List<string> SplitLines(string text, out bool hadTrailingNewline)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        hadTrailingNewline = normalized.Length > 0 && normalized.EndsWith('\n');
        var parts = normalized.Split('\n').ToList();
        if (hadTrailingNewline && parts.Count > 0 && parts[^1].Length == 0)
            parts.RemoveAt(parts.Count - 1);
        return parts;
    }

    private static Regex KeyRegex(string key)
        => new($@"^(?<indent>[ \t]*)(?<hash>#[ \t]*)?(?<keypart>{Regex.Escape(key)}\s*:.*)$");

    private static bool TryFindKeyLine(List<string> lines, string key, out int index, out Match match)
    {
        var regex = KeyRegex(key);
        for (var i = 0; i < lines.Count; i++)
        {
            var m = regex.Match(lines[i]);
            if (m.Success) { index = i; match = m; return true; }
        }
        index = -1;
        match = null!;
        return false;
    }

    private static string? ReadValue(List<string> lines, string key)
    {
        if (!TryFindKeyLine(lines, key, out _, out var m)) return null;
        if (m.Groups["hash"].Success) return null; // commented out => unset
        var keypart = m.Groups["keypart"].Value;
        var colon = keypart.IndexOf(':');
        return colon < 0 ? null : keypart[(colon + 1)..].Trim();
    }

    /// <summary>
    /// Mutates <paramref name="lines"/> in place so that <paramref name="key"/> ends up matching
    /// <paramref name="desiredValue"/>: uncommenting (and overwriting the value) when
    /// <paramref name="desiredValue"/> is non-null, or re-commenting the existing line — without
    /// inventing new text — when it is null. Surrounding comments and indentation are untouched.
    /// If the key is entirely absent from the file, a non-null desired value is appended (under
    /// <c>session:</c> for session keys) rather than silently dropped.
    /// </summary>
    private static void ApplyKey(List<string> lines, string key, string? desiredValue, bool isSessionKey)
    {
        if (TryFindKeyLine(lines, key, out var idx, out var m))
        {
            var indent = m.Groups["indent"].Value;
            if (desiredValue is not null)
            {
                lines[idx] = $"{indent}{key}: {desiredValue}";
            }
            else if (!m.Groups["hash"].Success)
            {
                var keypart = m.Groups["keypart"].Value;
                lines[idx] = $"{indent}# {keypart}";
            }
            // else: already commented and staying unset — leave the line exactly as-is.
            return;
        }

        if (desiredValue is null) return; // key absent and staying unset: nothing to do.

        if (isSessionKey)
        {
            var sessionIdx = lines.FindIndex(l => l.TrimStart().StartsWith("session:"));
            var insertAt = sessionIdx >= 0 ? sessionIdx + 1 : lines.Count;
            lines.Insert(insertAt, $"  {key}: {desiredValue}");
        }
        else
        {
            lines.Add($"{key}: {desiredValue}");
        }
    }

    private static CredentialStoreKind ParseCredentialStore(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null => CredentialStoreKind.Unset,
        "default" => CredentialStoreKind.Default,
        "wincred" => CredentialStoreKind.Wincred,
        "file" => CredentialStoreKind.File,
        _ => CredentialStoreKind.Unset, // unrecognized value: degrade to unset rather than throw.
    };

    private static string? FormatCredentialStore(CredentialStoreKind kind) => kind switch
    {
        CredentialStoreKind.Default => "default",
        CredentialStoreKind.Wincred => "wincred",
        CredentialStoreKind.File => "file",
        _ => null,
    };

    private static readonly Regex SizeRegex = new(@"^\d+[ \t]?(B|KB|MB|GB|TB)$", RegexOptions.IgnoreCase);

    /// <summary>Validates a <c>session.cpuCount</c> candidate. Returns null when valid, or a
    /// message describing why it is not.</summary>
    public static string? ValidateCpuCount(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "CPU count is required.";
        var trimmed = value.Trim();
        if (IsDefault(trimmed)) return null;
        return int.TryParse(trimmed, out var n) && n > 0
            ? null
            : "CPU count must be a positive integer or 'default'.";
    }

    /// <summary>Validates a <c>session.memorySize</c> candidate (e.g. "2GB") or "default".</summary>
    public static string? ValidateMemorySize(string value) => ValidateSize(value);

    /// <summary>Validates a <c>session.maxStorageSize</c> candidate (e.g. "500GB") or "default".</summary>
    public static string? ValidateMaxStorageSize(string value) => ValidateSize(value);

    private static string? ValidateSize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "A size is required.";
        var trimmed = value.Trim();
        if (IsDefault(trimmed)) return null;
        return SizeRegex.IsMatch(trimmed)
            ? null
            : "Value must be a size like '2GB', '512MB', '1TB', or 'default'.";
    }

    /// <summary>Validates a <c>session.defaultBindingAddress</c> candidate (an IP) or "default".</summary>
    public static string? ValidateDefaultBindingAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "An address is required.";
        var trimmed = value.Trim();
        if (IsDefault(trimmed)) return null;
        return System.Net.IPAddress.TryParse(trimmed, out _)
            ? null
            : "Address must be a valid IP address or 'default'.";
    }

    private static bool IsDefault(string trimmed) => string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase);
}
