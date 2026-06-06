using System.Text.Json;

namespace Wsl.Core;

public class BootstrapStateStore
{
    private readonly string _path;

    public BootstrapStateStore(string? path = null)
        => _path = path ?? DefaultPath();

    private static string DefaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WslCommandCenter", "bootstrap.json");

    private record State(string Step);

    public async Task<BootstrapStep> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return BootstrapStep.Done;
        var json = await File.ReadAllTextAsync(_path, ct);
        var state = JsonSerializer.Deserialize<State>(json);
        return state is not null && Enum.TryParse<BootstrapStep>(state.Step, out var step)
            ? step : BootstrapStep.Done;
    }

    public async Task WriteAsync(BootstrapStep step, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(new State(step.ToString()));
        await File.WriteAllTextAsync(_path, json, ct);
    }

    public Task ClearAsync(CancellationToken ct = default) => WriteAsync(BootstrapStep.Done, ct);
}
