namespace Wsl.Core.Settings;

/// <summary>Persisted app preferences. Stored as JSON under %LOCALAPPDATA%.</summary>
public sealed class AppSettings
{
    /// <summary>One of "System", "Light", "Dark". Defaults to "System".</summary>
    public string Theme { get; set; } = "System";
}
