using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Wsl.App.Theming;

/// <summary>
/// Overrides the framework accent + default-font resources at Application scope.
/// MUST run before the window (and its NavigationView) is constructed for the chrome to pick
/// up the accent on first paint; for live changes, re-run then rebuild visible pages.
/// Palette themes only contribute their signature accent here — their background is applied
/// per-window (see MainWindow.ApplyAppearance), NOT via brush overrides: overriding theme
/// brushes poisons WinUI's per-theme-dictionary StaticResource caches and the colors then
/// survive switching back to a stock theme.
/// </summary>
public static class Appearance
{
    public static void OverrideResources(string accent, string font, Palette? palette = null)
    {
        var res = Application.Current.Resources;

        // Palette supplies the accent only when the user left accent on "Default".
        var c = palette is not null && accent == "Default"
            ? palette.Accent
            : Accents.Resolve(accent);

        res["SystemAccentColor"] = c;
        res["SystemAccentColorLight1"] = Lighten(c, 0.15);
        res["SystemAccentColorLight2"] = Lighten(c, 0.30);
        res["SystemAccentColorLight3"] = Lighten(c, 0.45);
        res["SystemAccentColorDark1"] = Darken(c, 0.15);
        res["SystemAccentColorDark2"] = Darken(c, 0.30);
        res["SystemAccentColorDark3"] = Darken(c, 0.45);

        res["AccentFillColorDefaultBrush"] = new SolidColorBrush(c);
        res["AccentFillColorSecondaryBrush"] = new SolidColorBrush(c) { Opacity = 0.9 };
        res["AccentFillColorTertiaryBrush"] = new SolidColorBrush(c) { Opacity = 0.8 };
        res["NavigationViewSelectionIndicatorForeground"] = new SolidColorBrush(c);
        res["AccentButtonBackground"] = new SolidColorBrush(c);
        res["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(c) { Opacity = 0.9 };
        res["AccentButtonBackgroundPressed"] = new SolidColorBrush(c) { Opacity = 0.8 };

        if (!string.IsNullOrWhiteSpace(font))
            res["ContentControlThemeFontFamily"] = new FontFamily(font);
    }

    private static Color Lighten(Color c, double f) => Color.FromArgb(
        c.A,
        (byte)(c.R + (255 - c.R) * f),
        (byte)(c.G + (255 - c.G) * f),
        (byte)(c.B + (255 - c.B) * f));

    private static Color Darken(Color c, double f) => Color.FromArgb(
        c.A, (byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));
}
