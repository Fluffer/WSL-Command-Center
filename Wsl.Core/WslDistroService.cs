namespace Wsl.Core;

public class WslDistroService
{
    private readonly IProcessRunner _runner;

    public WslDistroService(IProcessRunner runner) => _runner = runner;

    public async Task<IReadOnlyList<Distro>> ListAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe", new[] { "--list", "--verbose" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, "List distros");
        return Parse(result.StdOut);
    }

    internal static IReadOnlyList<Distro> Parse(string stdout)
    {
        var lines = stdout.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var distros = new List<Distro>();
        foreach (var line in lines)
        {
            // Skip header (NAME ... STATE ... VERSION).
            if (line.TrimStart().StartsWith("NAME")) continue;

            var isDefault = line.StartsWith("*");
            // Drop the 2-char marker column, then split on whitespace runs.
            var body = (line.Length >= 2 ? line[2..] : line).Trim();
            var parts = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            // VERSION is the last token, STATE is second-to-last, NAME is everything before.
            var version = int.TryParse(parts[^1], out var v) ? v : 0;
            var state = ParseState(parts[^2]);
            var name = string.Join(' ', parts[..^2]);
            distros.Add(new Distro(name, state, version, isDefault));
        }
        return distros;
    }

    public Task StartAsync(string name, CancellationToken ct = default)
        => Run(new[] { "-d", name, "--", "true" }, $"Start {name}", ct);

    public Task TerminateAsync(string name, CancellationToken ct = default)
        => Run(new[] { "--terminate", name }, $"Terminate {name}", ct);

    public Task SetDefaultAsync(string name, CancellationToken ct = default)
        => Run(new[] { "--set-default", name }, $"Set default {name}", ct);

    public Task SetVersionAsync(string name, int version, CancellationToken ct = default)
        => Run(new[] { "--set-version", name, version.ToString() }, $"Set version {name}", ct);

    public Task UnregisterAsync(string name, CancellationToken ct = default)
        => Run(new[] { "--unregister", name }, $"Unregister {name}", ct);

    public Task SetDefaultUserAsync(string name, string user, CancellationToken ct = default)
        => Run(new[] { "--manage", name, "--set-default-user", user }, $"Set default user for {name}", ct);

    public Task ShutdownAsync(CancellationToken ct = default)
        => Run(new[] { "--shutdown" }, "Shutdown WSL", ct);

    /// <summary>Lists login-capable users in the distro (root + regular UIDs 1000–59999,
    /// excluding nologin/false shells). Starts the distro implicitly if stopped.</summary>
    public async Task<IReadOnlyList<string>> ListUsersAsync(string name, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("wsl.exe",
            new[] { "-d", name, "-u", "root", "--", "getent", "passwd" }, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"List users for {name}");
        return result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(':'))
            .Where(p => p.Length >= 7)
            .Where(p => p[0] == "root" ||
                        (int.TryParse(p[2], out var uid) && uid >= 1000 && uid < 60000))
            .Where(p => !p[6].Trim().EndsWith("nologin") && !p[6].Trim().EndsWith("false"))
            .Select(p => p[0])
            .ToList();
    }

    private async Task Run(string[] args, string op, CancellationToken ct)
    {
        var result = await _runner.RunAsync("wsl.exe", args, null, ct);
        WslErrorMapper.ThrowIfFailed(result, op);
    }

    private static DistroState ParseState(string s) => s switch
    {
        "Running" => DistroState.Running,
        "Stopped" => DistroState.Stopped,
        "Installing" => DistroState.Installing,
        _ => DistroState.Unknown
    };
}
