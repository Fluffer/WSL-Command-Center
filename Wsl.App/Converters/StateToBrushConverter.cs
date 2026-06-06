using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Wsl.App.Converters;

/// <summary>Maps a distro state string to a brush. "Running" -> success, else critical.
/// Pass ConverterParameter="bg" for the pill background variant.</summary>
public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // value is a DistroState enum (boxed) or a string; compare by name.
        var running = string.Equals(value?.ToString(), "Running", StringComparison.OrdinalIgnoreCase);
        var bg = string.Equals(parameter as string, "bg", StringComparison.OrdinalIgnoreCase);
        var key = (running, bg) switch
        {
            (true, true) => "SystemFillColorSuccessBackgroundBrush",
            (true, false) => "SystemFillColorSuccessBrush",
            (false, true) => "SystemFillColorNeutralBackgroundBrush",
            (false, false) => "SystemFillColorCriticalBrush",
        };
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
