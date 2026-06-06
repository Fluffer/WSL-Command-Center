namespace Wsl.Core;

public class WslConfigService
{
    private readonly IProcessRunner _runner;
    private readonly Func<string> _globalPath;

    public WslConfigService(IProcessRunner runner, Func<string>? globalPathProvider = null)
    {
        _runner = runner;
        _globalPath = globalPathProvider ?? DefaultGlobalPath;
    }

    private static string DefaultGlobalPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");

    public async Task<WslGlobalConfig> ReadGlobalAsync(CancellationToken ct = default)
    {
        var path = _globalPath();
        if (!File.Exists(path)) return new WslGlobalConfig();
        var text = await File.ReadAllTextAsync(path, ct);
        return WslGlobalConfig.FromIni(IniParser.Parse(text));
    }

    public async Task WriteGlobalAsync(WslGlobalConfig cfg, CancellationToken ct = default)
    {
        var text = IniParser.Write(cfg.ToIni());
        await File.WriteAllTextAsync(_globalPath(), text, ct);
    }

    public async Task<WslDistroConfig> ReadDistroAsync(string name, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync(
            "wsl.exe", new[] { "-d", name, "-u", "root", "cat", "/etc/wsl.conf" }, null, ct);
        // Missing file => empty config (cat exits non-zero); treat empty as defaults.
        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StdOut))
            return new WslDistroConfig();
        return WslDistroConfig.FromIni(IniParser.Parse(result.StdOut));
    }

    public async Task WriteDistroAsync(string name, WslDistroConfig cfg, CancellationToken ct = default)
    {
        var body = IniParser.Write(cfg.ToIni());
        var result = await _runner.RunWithInputAsync(
            "wsl.exe", new[] { "-d", name, "-u", "root", "tee", "/etc/wsl.conf" }, body, null, ct);
        WslErrorMapper.ThrowIfFailed(result, $"Write wsl.conf for {name}");
    }
}
