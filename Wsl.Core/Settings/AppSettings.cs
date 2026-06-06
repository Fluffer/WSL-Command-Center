namespace Wsl.Core.Settings;

/// <summary>Persisted app preferences. Stored as JSON under %LOCALAPPDATA%.</summary>
public sealed class AppSettings
{
    /// <summary>One of "System", "Light", "Dark". Defaults to "System".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Accent color name (e.g. "Default", "Blue", "Green"). "Default" = system accent.</summary>
    public string Accent { get; set; } = "Default";

    /// <summary>UI font family name. Defaults to the system "Segoe UI Variable".</summary>
    public string Font { get; set; } = "Segoe UI Variable";
}
