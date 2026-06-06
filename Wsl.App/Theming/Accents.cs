using Windows.UI;
using Windows.UI.ViewManagement;

namespace Wsl.App.Theming;

/// <summary>Popular accent colors offered in Settings. "Default" follows the system accent.</summary>
public static class Accents
{
    public static readonly (string Name, Color Color)[] All =
    {
        ("Default", default),
        ("Blue",    Color.FromArgb(255, 0, 120, 212)),
        ("Teal",    Color.FromArgb(255, 0, 183, 195)),
        ("Green",   Color.FromArgb(255, 22, 163, 74)),
        ("Orange",  Color.FromArgb(255, 233, 84, 32)),
        ("Purple",  Color.FromArgb(255, 124, 58, 237)),
        ("Red",     Color.FromArgb(255, 232, 17, 35)),
        ("Pink",    Color.FromArgb(255, 227, 0, 140)),
    };

    public static string[] Names()
    {
        var names = new string[All.Length];
        for (var i = 0; i < All.Length; i++) names[i] = All[i].Name;
        return names;
    }

    /// <summary>Resolve a name to a color; "Default" (or unknown) returns the live system accent.</summary>
    public static Color Resolve(string name)
    {
        foreach (var a in All)
            if (a.Name == name && name != "Default") return a.Color;
        return new UISettings().GetColorValue(UIColorType.Accent);
    }
}

/// <summary>Popular UI / console fonts offered in Settings.</summary>
public static class AppFonts
{
    public static readonly string[] All =
    {
        "Segoe UI Variable",
        "Segoe UI",
        "Anthropic Sans",
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "Lucida Console",
    };
}
