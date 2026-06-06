using Windows.UI;

namespace Wsl.App.Theming;

/// <summary>A full color palette: recolors window, nav pane, cards and text. All palettes are dark-based.</summary>
public sealed record Palette(
    string Name,
    Color Pane,          // NavigationView pane / window chrome
    Color Content,       // page content background
    Color Card,          // SettingsCard / card fill
    Color CardStroke,    // card border
    Color TextPrimary,
    Color TextSecondary,
    Color Accent);       // signature accent (used when user accent is "Default")

/// <summary>Popular developer color themes offered in Settings. "None" = stock Windows look.</summary>
public static class Palettes
{
    private static Color C(uint rgb) => Color.FromArgb(
        255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static readonly Palette[] All =
    {
        new("Dracula",
            Pane: C(0x21222C), Content: C(0x282A36), Card: C(0x343746), CardStroke: C(0x191A21),
            TextPrimary: C(0xF8F8F2), TextSecondary: C(0x9DA8C7), Accent: C(0xBD93F9)),
        new("Nord",
            Pane: C(0x272C36), Content: C(0x2E3440), Card: C(0x3B4252), CardStroke: C(0x242933),
            TextPrimary: C(0xECEFF4), TextSecondary: C(0xAEB6C4), Accent: C(0x88C0D0)),
        new("Catppuccin Mocha",
            Pane: C(0x181825), Content: C(0x1E1E2E), Card: C(0x313244), CardStroke: C(0x11111B),
            TextPrimary: C(0xCDD6F4), TextSecondary: C(0xA6ADC8), Accent: C(0xCBA6F7)),
        new("Tokyo Night",
            Pane: C(0x16161E), Content: C(0x1A1B26), Card: C(0x24283B), CardStroke: C(0x101014),
            TextPrimary: C(0xC0CAF5), TextSecondary: C(0x8A91B4), Accent: C(0x7AA2F7)),
        new("One Dark",
            Pane: C(0x21252B), Content: C(0x282C34), Card: C(0x2C313A), CardStroke: C(0x181A1F),
            TextPrimary: C(0xD7DAE0), TextSecondary: C(0x9DA5B4), Accent: C(0x61AFEF)),
        new("Gruvbox",
            Pane: C(0x1D2021), Content: C(0x282828), Card: C(0x3C3836), CardStroke: C(0x141617),
            TextPrimary: C(0xEBDBB2), TextSecondary: C(0xA89984), Accent: C(0xFE8019)),
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
