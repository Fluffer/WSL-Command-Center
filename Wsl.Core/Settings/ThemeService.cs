using System;
using System.IO;
using System.Text.Json;

namespace Wsl.Core.Settings;

/// <summary>
/// File-backed settings store. Uses %LOCALAPPDATA%\WSL Command Center\settings.json by default.
/// ApplicationData.Current.LocalSettings is intentionally NOT used: it throws
/// InvalidOperationException when the app runs unpackaged (no package identity).
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly string _path;

    /// <summary>Production ctor — resolves the default %LOCALAPPDATA% path.</summary>
    public ThemeService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WSL Command Center", "settings.json"))
    {
    }

    /// <summary>Test ctor — explicit path.</summary>
    public ThemeService(string path) => _path = path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings));
    }
}
