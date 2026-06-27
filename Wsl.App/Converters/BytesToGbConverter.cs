using System;
using Microsoft.UI.Xaml.Data;

namespace Wsl.App.Converters;

public sealed class BytesToGbConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var bytes = value switch
        {
            long l => l,
            int i  => (long)i,
            _      => 0L,
        };
        return $"{bytes / 1_073_741_824.0:F1} GB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
