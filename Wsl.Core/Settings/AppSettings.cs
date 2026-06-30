namespace Wsl.Core.Settings;

/// <summary>Persisted app preferences. Stored as JSON under %LOCALAPPDATA%.</summary>
public sealed class AppSettings
{
    /// <summary>
    /// "System", "Light", "Dark", or a color-palette name (e.g. "Dracula", "Nord").
    /// Palette themes force the dark base and recolor window/card/text surfaces.
    /// </summary>
    public string Theme { get; set; } = "System";

    /// <summary>Accent color name (e.g. "Default", "Blue", "Green"). "Default" = system accent.</summary>
    public string Accent { get; set; } = "Default";

    /// <summary>UI font family name. Defaults to the system "Segoe UI Variable".</summary>
    public string Font { get; set; } = "Segoe UI Variable";

    /// <summary>Reveals developer/diagnostics tools (e.g. the WSL2 debug shell). Off by default.</summary>
    public bool DeveloperMode { get; set; }

    /// <summary>Enables the experimental WSL Containers (wslc) page. wslc is a preview CLI whose
    /// output format is unstable; the page is gated off by default until the user opts in.</summary>
    public bool EnableWslcPreview { get; set; }
}
