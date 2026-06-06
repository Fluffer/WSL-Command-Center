using Windows.UI;

namespace Wsl.App.Theming;

/// <summary>
/// A developer color theme: a solid window background plus a signature accent.
/// The dark base theme's translucent card/text layers compose over the background,
/// which keeps every control readable and theme switching loss-free.
/// </summary>
public sealed record Palette(string Name, Color Background, Color Accent);

/// <summary>Popular developer color themes offered in Settings, after System/Light/Dark.</summary>
public static class Palettes
{
    private static Color C(uint rgb) => Color.FromArgb(
        255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static readonly Palette[] All =
    {
        new("Dracula",          C(0x282A36), C(0xBD93F9)),
        new("Nord",             C(0x2E3440), C(0x88C0D0)),
        new("Catppuccin Mocha", C(0x1E1E2E), C(0xCBA6F7)),
        new("Tokyo Night",      C(0x1A1B26), C(0x7AA2F7)),
        new("One Dark",         C(0x282C34), C(0x61AFEF)),
        new("Gruvbox",          C(0x282828), C(0xFE8019)),
    };

    /// <summary>Every palette name, appended after System/Light/Dark in the theme combo.</summary>
    public static string[] Names()
    {
        var names = new string[All.Length];
        for (var i = 0; i < All.Length; i++) names[i] = All[i].Name;
        return names;
    }

    /// <summary>Resolve a theme name to a palette; base themes (System/Light/Dark) or unknown return null.</summary>
    public static Palette? Resolve(string? name)
    {
        foreach (var p in All)
            if (p.Name == name) return p;
        return null;
    }
}
