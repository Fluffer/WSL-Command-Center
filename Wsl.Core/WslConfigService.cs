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
}
