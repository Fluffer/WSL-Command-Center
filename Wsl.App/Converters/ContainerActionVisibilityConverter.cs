using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Wsl.App.Logic.ViewModels;
using Wsl.Core.Containers;

namespace Wsl.App.Converters;

/// <summary>
/// Converts a <see cref="WslcContainerState"/> to <see cref="Visibility"/> for per-row action
/// gating in the Containers list. Compiled (x:Bind) function-call bindings can't reach back to
/// the enclosing Page from inside a DataTemplate whose x:DataType differs from the page — a
/// converter sidesteps that. The gating rule itself lives once in
/// <see cref="ContainersViewModel.CanStart"/>/<see cref="ContainersViewModel.CanStopOrRestart"/>;
/// this just calls it. <c>ConverterParameter="start"</c> checks CanStart, anything else checks
/// CanStopOrRestart.
/// </summary>
public sealed class ContainerActionVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not WslcContainerState state) return Visibility.Collapsed;
        var visible = (parameter as string) == "start"
            ? ContainersViewModel.CanStart(state)
            : ContainersViewModel.CanStopOrRestart(state);
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
