using System;
using Microsoft.UI.Xaml.Data;

namespace Wsl.App.Converters;

/// <summary>
/// Formats a <see cref="DateTimeOffset"/> for display. x:Bind only auto-converts numerics and
/// enums to string, not structs like DateTimeOffset — and inside a DataTemplate, a converter is
/// also the only way to apply formatting without a function-call binding reaching back into the
/// Page (which compiled bindings can't do from inside a DataTemplate with a different x:DataType).
/// </summary>
public sealed class DateTimeOffsetToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is DateTimeOffset dt ? dt.LocalDateTime.ToString("g") : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
