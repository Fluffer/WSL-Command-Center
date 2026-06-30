using Microsoft.UI.Xaml.Data;

namespace Wsl.App.Converters;

/// <summary>True → checkmark glyph, false → error glyph (Segoe Fluent Icons).
/// Glyphs built via ConvertFromUtf32 so the source stays ASCII.</summary>
public class BoolToStatusGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? char.ConvertFromUtf32(0xE73E) : char.ConvertFromUtf32(0xEA39);
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
